#!/usr/bin/env python3
"""Execute the client crypto module in a real Chromium secure context."""

from __future__ import annotations

import functools
import http.server
import pathlib
import threading
from contextlib import contextmanager

from playwright.sync_api import sync_playwright


ROOT = pathlib.Path(__file__).resolve().parents[2]
MODULE_PATH = "/src/HappyGymStats.Blazor/HappyGymStats.Blazor.Client/wwwroot/crypto.js"
PASSWORD = "correct horse battery staple"


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


def main() -> None:
    with repo_server() as port, sync_playwright() as playwright:
        browser = playwright.chromium.launch(headless=True)
        try:
            page = browser.new_page()
            page.goto(f"http://127.0.0.1:{port}/", wait_until="domcontentloaded")
            result = page.evaluate(
                """
                async ({ modulePath, password }) => {
                    const module = await import(modulePath);
                    const generated = await module.generateWrappedKeyPair(password);
                    const unlocked = await module.unwrapPublicKeySpki(
                        generated.wrappedPrivateKey,
                        password);
                    const wrongPassword = await module.unwrapPublicKeySpki(
                        generated.wrappedPrivateKey,
                        password + "-wrong");

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
                {"modulePath": MODULE_PATH, "password": PASSWORD},
            )
        finally:
            browser.close()

    assert result["secureContext"], "localhost browser proof must run in a secure context"
    assert result["publicKeyMatches"], "unwrapped P-256 public key did not match generated SPKI"
    assert result["wrongPasswordRejected"], "wrong password unexpectedly unlocked the private key"
    assert result["tamperedIterationsRejected"], "tampered PBKDF2 work factor was not rejected"
    assert result["wrappedIterations"] == 100000, "stored PBKDF2 iteration header changed"
    assert result["wrappedLength"] >= 64, "wrapped-key wire payload is too short"
    print("browser crypto regression: PASS")


if __name__ == "__main__":
    main()
