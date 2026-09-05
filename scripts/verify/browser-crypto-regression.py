#!/usr/bin/env python3
"""Execute the client crypto module in a real Chromium secure context."""

from __future__ import annotations

import functools
import http.server
import json
import pathlib
import subprocess
import tempfile
import threading
from contextlib import contextmanager

from playwright.sync_api import sync_playwright


ROOT = pathlib.Path(__file__).resolve().parents[2]
MODULE_PATH = "/src/HappyGymStats.Blazor/HappyGymStats.Blazor.Client/wwwroot/crypto.js"
PASSWORD = "correct horse battery staple"
ENCRYPTION_PROJECT = ROOT / "src/HappyGymStats.Encryption/HappyGymStats.Encryption.csproj"


class QuietHandler(http.server.SimpleHTTPRequestHandler):
    def log_message(self, format: str, *args: object) -> None:  # noqa: A002
        pass


@contextmanager
def repo_server():
    handler = functools.partial(QuietHandler, directory=str(ROOT))
    server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        yield server.server_port
    finally:
        server.shutdown()
        thread.join(timeout=5)
        server.server_close()


def generate_legacy_dotnet_fixture() -> dict[str, str]:
    """Generate a pre-WebCrypto blob through the repository's .NET KeyWrapping code."""
    with tempfile.TemporaryDirectory(prefix="hgs-legacy-keywrap-") as temp_dir:
        temp = pathlib.Path(temp_dir)
        project_path = temp / "LegacyKeyWrappingFixture.csproj"
        program_path = temp / "Program.cs"
        project_path.write_text(
            f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="{ENCRYPTION_PROJECT.as_posix()}" />
  </ItemGroup>
</Project>
""",
            encoding="utf-8",
        )
        program_path.write_text(
            """using System.Security.Cryptography;
using System.Text.Json;
using HappyGymStats.Encryption;

if (args.Length != 1)
    return 2;

using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
var publicKeySpki = ecdh.ExportSubjectPublicKeyInfo();
var privateKeyPkcs8 = ecdh.ExportPkcs8PrivateKey();
var wrappedPrivateKey = KeyWrapping.WrapKey(privateKeyPkcs8, args[0].AsSpan());
Console.WriteLine(JsonSerializer.Serialize(new
{
    publicKeySpki = Convert.ToBase64String(publicKeySpki),
    wrappedPrivateKey = Convert.ToBase64String(wrappedPrivateKey)
}));
return 0;
""",
            encoding="utf-8",
        )
        completed = subprocess.run(
            [
                "dotnet",
                "run",
                "--project",
                str(project_path),
                "--configuration",
                "Release",
                "--verbosity",
                "quiet",
                "--",
                PASSWORD,
            ],
            check=True,
            capture_output=True,
            text=True,
        )
        json_lines = [line for line in completed.stdout.splitlines() if line.startswith("{")]
        if not json_lines:
            raise AssertionError(f"legacy .NET fixture produced no JSON: {completed.stdout!r}")
        fixture = json.loads(json_lines[-1])
        if not isinstance(fixture.get("publicKeySpki"), str) or not isinstance(
            fixture.get("wrappedPrivateKey"), str
        ):
            raise AssertionError("legacy .NET fixture returned an unexpected shape")
        return fixture


def main() -> None:
    legacy_fixture = generate_legacy_dotnet_fixture()

    with repo_server() as port, sync_playwright() as playwright:
        browser = playwright.chromium.launch(headless=True)
        try:
            page = browser.new_page()
            page.goto(f"http://127.0.0.1:{port}/", wait_until="domcontentloaded")
            result = page.evaluate(
                """
                async ({ modulePath, password, legacyFixture }) => {
                    const module = await import(modulePath);
                    const generated = await module.generateWrappedKeyPair(password);
                    const unlocked = await module.unwrapPublicKeySpki(
                        generated.wrappedPrivateKey,
                        password);
                    const wrongPassword = await module.unwrapPublicKeySpki(
                        generated.wrappedPrivateKey,
                        password + "-wrong");
                    const legacyUnlocked = await module.unwrapPublicKeySpki(
                        legacyFixture.wrappedPrivateKey,
                        password);

                    const wrapped = Uint8Array.from(
                        atob(generated.wrappedPrivateKey),
                        value => value.charCodeAt(0));
                    const tamperedIterations = wrapped.slice();
                    new DataView(tamperedIterations.buffer).setUint32(0, 999999, false);
                    let tamperedBinary = "";
                    for (const value of tamperedIterations)
                        tamperedBinary += String.fromCharCode(value);
                    const tampered = await module.unwrapPublicKeySpki(
                        btoa(tamperedBinary),
                        password);

                    return {
                        publicKeyMatches: unlocked === generated.publicKeySpki,
                        legacyPublicKeyMatches: legacyUnlocked === legacyFixture.publicKeySpki,
                        wrongPasswordRejected: wrongPassword === null,
                        tamperedIterationsRejected: tampered === null,
                        wrappedIterations: new DataView(
                            wrapped.buffer,
                            wrapped.byteOffset,
                            wrapped.byteLength).getUint32(0, false),
                        wrappedLength: wrapped.length,
                        secureContext: window.isSecureContext
                    };
                }
                """,
                {
                    "modulePath": MODULE_PATH,
                    "password": PASSWORD,
                    "legacyFixture": legacy_fixture,
                },
            )
        finally:
            browser.close()

    assert result["secureContext"], "localhost browser proof must run in a secure context"
    assert result["publicKeyMatches"], "unwrapped P-256 public key did not match generated SPKI"
    assert result["legacyPublicKeyMatches"], "WebCrypto could not unlock a legacy .NET KeyWrapping blob"
    assert result["wrongPasswordRejected"], "wrong password unexpectedly unlocked the private key"
    assert result["tamperedIterationsRejected"], "tampered PBKDF2 work factor was not rejected"
    assert result["wrappedIterations"] == 100000, "stored PBKDF2 iteration header changed"
    assert result["wrappedLength"] >= 64, "wrapped-key wire payload is too short"
    print("browser crypto regression: PASS")


if __name__ == "__main__":
    main()
