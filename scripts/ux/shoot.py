#!/usr/bin/env python3
"""Screenshot the running app at the viewports and themes we actually care about.

Driven by scripts/screenshot-board.sh, which starts and stops the hosts. This
file only drives the browser, so it can also be pointed at an already-running
instance during iteration.

Why Playwright rather than a headless-browser one-liner: the UX slices that come
next need interaction, not just a picture. U003 needs a real viewport (not a
window size), U005 needs Tab and focus rings, and U006 needs
prefers-color-scheme emulation. A screenshot flag covers none of those.

It uses its own Chromium under ~/.cache/ms-playwright. It never touches a
browser you use yourself, and it holds no profile, cookies or history.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

try:
    from playwright.sync_api import sync_playwright, Error as PlaywrightError
except ImportError:  # pragma: no cover - the wrapper checks this first
    sys.exit("playwright is not installed. Run: bash scripts/screenshot-board.sh --setup")

# Named so a filename says what it is without opening it.
VIEWPORTS = {
    "phone": (390, 900),      # war nights happen here (UX plan U003)
    "tablet": (768, 1000),
    "desktop": (1440, 1000),
}

THEMES = ("light", "dark")


ACCOUNT_STATES = ("connected", "replacement-error", "revoke-confirmation")


def apply_account_state(page, state: str) -> None:
    page.get_by_text("Connected", exact=True).wait_for()

    if state == "connected":
        return

    if state == "replacement-error":
        fixture_secret = "render-proof-secret-must-disappear"
        page.get_by_role("button", name="Replace Torn API key").click()
        secret = page.get_by_label("Torn API key", exact=True)
        secret.fill(fixture_secret)
        page.get_by_label("I consent to encrypted server-side storage", exact=False).check()
        page.get_by_role("button", name="Replace connection").click()
        page.get_by_text("Torn rejected this API key", exact=False).wait_for()
        if secret.input_value():
            raise PlaywrightError("replacement failure left the submitted Torn API key in the input")
        if fixture_secret in page.content():
            raise PlaywrightError("replacement failure left the submitted Torn API key in rendered markup")
        return

    if state == "revoke-confirmation":
        page.get_by_role("button", name="Revoke connection").click()
        page.get_by_role("button", name="Confirm revoke").wait_for()
        page.get_by_role("button", name="Keep connection").wait_for()
        return

    raise PlaywrightError(f"unsupported account state: {state}")


def shoot(base_url: str, route: str, out_dir: Path, viewports, themes, full_page: bool, account_states) -> list[Path]:
    written: list[Path] = []
    slug = route.strip("/").replace("/", "-") or "home"

    with sync_playwright() as p:
        browser = p.chromium.launch()
        try:
            for vp_name in viewports:
                width, height = VIEWPORTS[vp_name]
                for theme in themes:
                    states = account_states or (None,)
                    for account_state in states:
                        context = browser.new_context(
                            viewport={"width": width, "height": height},
                            color_scheme=theme,
                            device_scale_factor=2,  # legible text in the PNG
                        )
                        page = context.new_page()

                        errors: list[str] = []
                        page.on("pageerror", lambda e: errors.append(str(e)))
                        # A console error is worth surfacing: a board that renders but
                        # throws is a broken board, and the screenshot alone hides it.
                        page.on("console", lambda m: errors.append(m.text) if m.type == "error" else None)

                        page.goto(f"{base_url}{route}", wait_until="networkidle", timeout=30_000)
                        # Blazor Server paints, then the circuit connects and re-renders.
                        # Without this the shot catches the pre-interactive frame.
                        page.wait_for_timeout(1500)
                        if account_state:
                            apply_account_state(page, account_state)
                            page.wait_for_timeout(500)

                        state_suffix = f"-{account_state}" if account_state else ""
                        target = out_dir / f"{slug}{state_suffix}-{vp_name}-{theme}.png"
                        page.screenshot(path=str(target), full_page=full_page)
                        written.append(target)

                        if errors:
                            print(f"  ! {target.name}: {len(errors)} console/page error(s)")
                            for line in errors[:3]:
                                print(f"      {line[:160]}")

                        context.close()
        finally:
            browser.close()

    return written


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-url", default="http://localhost:5137")
    parser.add_argument("--route", default="/war", help="e.g. /war, /, /terms")
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--viewport", action="append", choices=sorted(VIEWPORTS), default=None)
    parser.add_argument("--theme", action="append", choices=THEMES, default=None)
    parser.add_argument("--account-state", action="append", choices=ACCOUNT_STATES, default=None)
    parser.add_argument("--no-full-page", action="store_true")
    args = parser.parse_args()

    viewports = args.viewport or list(VIEWPORTS)
    themes = args.theme or list(THEMES)
    args.out.mkdir(parents=True, exist_ok=True)
    if args.account_state and args.route.rstrip("/") != "/player-account":
        parser.error("--account-state is only valid with --route /player-account")

    try:
        written = shoot(
            args.base_url,
            args.route,
            args.out,
            viewports,
            themes,
            not args.no_full_page,
            args.account_state,
        )
    except PlaywrightError as exc:
        print(f"FAIL: {exc}", file=sys.stderr)
        print(f"      Is the app running at {args.base_url}?", file=sys.stderr)
        return 1

    for path in written:
        print(f"  {path}")
    print(f"\n{len(written)} screenshot(s) written to {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
