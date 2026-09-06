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
import json
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


class FocusProofError(RuntimeError):
    """The rendered page did not satisfy the requested keyboard contract."""


def _active_element(page, expected_selector: str) -> dict:
    return page.evaluate(
        """selector => {
            const element = document.activeElement;
            if (!element) return { present: false, matches: false };
            const rect = element.getBoundingClientRect();
            const style = getComputedStyle(element);
            const path = [];
            for (let node = element; node && node !== document.body; node = node.parentElement) {
                const siblings = node.parentElement ? Array.from(node.parentElement.children) : [];
                path.unshift(`${node.tagName.toLowerCase()}:${siblings.indexOf(node)}`);
            }
            return {
                present: element !== document.body,
                matches: element.matches(selector),
                tag: element.tagName.toLowerCase(),
                id: element.id || null,
                testId: element.getAttribute('data-testid'),
                name: element.getAttribute('name'),
                type: element.getAttribute('type'),
                ariaLabel: element.getAttribute('aria-label'),
                text: (element.innerText || element.value || '').trim().slice(0, 120),
                tabIndex: element.tabIndex,
                disabled: Boolean(element.disabled),
                ariaHidden: element.getAttribute('aria-hidden'),
                domPath: path.join('/'),
                outlineStyle: style.outlineStyle,
                outlineWidth: style.outlineWidth,
                visible: rect.width > 0 && rect.height > 0
                    && style.visibility !== 'hidden' && style.display !== 'none',
            };
        }""",
        expected_selector,
    )


def prove_focus_order(page, selectors: list[str], max_tabs: int, screenshot_prefix: Path) -> list[dict]:
    evidence: list[dict] = []
    page.evaluate("document.activeElement instanceof HTMLElement && document.activeElement.blur()")

    for index, selector in enumerate(selectors, start=1):
        seen: set[str] = set()
        matched = None
        for _ in range(max_tabs):
            page.keyboard.press("Tab")
            page.wait_for_timeout(40)
            active = _active_element(page, selector)
            if not active.get("present"):
                raise FocusProofError(
                    f"focus left the rendered document before reaching selector {selector!r}"
                )
            fingerprint = json.dumps(active, sort_keys=True)
            if active.get("matches"):
                matched = active
                break
            if fingerprint in seen:
                raise FocusProofError(
                    f"focus traversal cycled before reaching selector {selector!r}"
                )
            seen.add(fingerprint)

        if matched is None:
            raise FocusProofError(
                f"selector {selector!r} was not reached within {max_tabs} Tab presses"
            )
        if not matched.get("present") or not matched.get("visible"):
            raise FocusProofError(f"selector {selector!r} received hidden or missing focus")
        if matched.get("disabled") or matched.get("ariaHidden") == "true" or matched.get("tabIndex", -1) < 0:
            raise FocusProofError(f"selector {selector!r} is not an operable focus target")
        if matched.get("outlineStyle") == "none" or matched.get("outlineWidth") in (None, "0px"):
            raise FocusProofError(f"selector {selector!r} has no computed focus outline")

        # Prove reverse traversal is not trapped or lost, then return to the exact
        # same DOM node before capturing its visible focus ring.
        page.evaluate("window.__hgsFocusProofTarget = document.activeElement")
        page.keyboard.press("Shift+Tab")
        page.keyboard.press("Tab")
        if not page.evaluate("document.activeElement === window.__hgsFocusProofTarget"):
            raise FocusProofError(
                f"Shift+Tab/Tab did not return focus to selector {selector!r}"
            )

        target = screenshot_prefix.with_name(f"{screenshot_prefix.stem}-focus-{index:02d}.png")
        page.screenshot(path=str(target), full_page=False)
        evidence.append({"selector": selector, "activeElement": matched, "screenshot": target.name})

    return evidence


def shoot(
    base_url: str,
    route: str,
    out_dir: Path,
    viewports,
    themes,
    full_page: bool,
    focus_selectors: list[str],
    focus_max_tabs: int,
) -> list[Path]:
    written: list[Path] = []
    focus_evidence: list[dict] = []
    slug = route.strip("/").replace("/", "-") or "home"

    with sync_playwright() as p:
        browser = p.chromium.launch()
        try:
            for vp_name in viewports:
                width, height = VIEWPORTS[vp_name]
                for theme in themes:
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

                    target = out_dir / f"{slug}-{vp_name}-{theme}.png"
                    page.screenshot(path=str(target), full_page=full_page)
                    written.append(target)

                    if focus_selectors:
                        frame_evidence = prove_focus_order(
                            page, focus_selectors, focus_max_tabs, target
                        )
                        for item in frame_evidence:
                            item.update({"viewport": vp_name, "theme": theme})
                            written.append(out_dir / item["screenshot"])
                        focus_evidence.extend(frame_evidence)

                    if errors:
                        print(f"  ! {target.name}: {len(errors)} console/page error(s)")
                        for line in errors[:3]:
                            print(f"      {line[:160]}")
                        if focus_selectors:
                            raise FocusProofError(
                                f"{target.name} produced browser errors during keyboard proof"
                            )

                    context.close()
        finally:
            browser.close()

    if focus_selectors:
        manifest = out_dir / "focus-proof.json"
        manifest.write_text(
            json.dumps(
                {
                    "route": route,
                    "selectors": focus_selectors,
                    "frames": focus_evidence,
                },
                indent=2,
                sort_keys=True,
            ) + "\n",
            encoding="utf-8",
        )

    return written


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-url", default="http://localhost:5137")
    parser.add_argument("--route", default="/war", help="e.g. /war, /, /terms")
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--viewport", action="append", choices=sorted(VIEWPORTS), default=None)
    parser.add_argument("--theme", action="append", choices=THEMES, default=None)
    parser.add_argument(
        "--focus-selector",
        action="append",
        default=[],
        help="CSS selector that real Tab traversal must reach, in argument order",
    )
    parser.add_argument("--focus-max-tabs", type=int, default=60)
    parser.add_argument("--no-full-page", action="store_true")
    args = parser.parse_args()

    viewports = args.viewport or list(VIEWPORTS)
    themes = args.theme or list(THEMES)
    args.out.mkdir(parents=True, exist_ok=True)

    try:
        if args.focus_max_tabs < 1:
            parser.error("--focus-max-tabs must be positive")
        written = shoot(
            args.base_url,
            args.route,
            args.out,
            viewports,
            themes,
            not args.no_full_page,
            args.focus_selector,
            args.focus_max_tabs,
        )
    except (PlaywrightError, FocusProofError) as exc:
        print(f"FAIL: {exc}", file=sys.stderr)
        print(f"      Is the app running at {args.base_url}?", file=sys.stderr)
        return 1

    for path in written:
        print(f"  {path}")
    print(f"\n{len(written)} screenshot(s) written to {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
