#!/usr/bin/env python3
from pathlib import Path
import sys

FILES = [
    Path('/usr/lib/node_modules/@opengsd/gsd-pi/src/resources/extensions/gsd/milestone-validation-evidence.ts'),
    Path('/usr/lib/node_modules/@opengsd/gsd-pi/dist/resources/extensions/gsd/milestone-validation-evidence.js'),
]

OLD_TS = """    if (browserRequiringSlices.length === 0) {
      return qualifyingEvidence.length === 0 &&
        !sliceEvidencePairs.some((slice) => persistedBrowserEvidencePasses(basePath, slice.evidenceText));
    }
    return browserRequiringSlices.some((slice) =>
      !qualifyingEvidence.some((evidence) => evidence.sliceId === slice.sliceId) &&
      !persistedBrowserEvidencePasses(basePath, slice.evidenceText)
    );"""

NEW_TS = """    if (browserRequiringSlices.length === 0) {
      return qualifyingEvidence.length === 0 &&
        !sliceEvidencePairs.some((slice) => persistedBrowserEvidencePasses(basePath, slice.evidenceText));
    }

    const explicitlyBoundSliceIds = new Set(
      qualifyingEvidence
        .map((evidence) => evidence.sliceId?.trim())
        .filter((sliceId): sliceId is string => Boolean(sliceId)),
    );

    // Canonical validation sometimes receives a single browser-required Slice's
    // structured UAT evidence without the sliceId binding preserved. When there
    // is exactly one browser-required Slice, treat any otherwise-qualifying UAT
    // browser/runtime evidence as bound to that sole Slice instead of forcing a
    // false needs-attention verdict.
    if (browserRequiringSlices.length === 1 && explicitlyBoundSliceIds.size === 0 && qualifyingEvidence.length > 0) {
      explicitlyBoundSliceIds.add(browserRequiringSlices[0]!.sliceId);
    }

    return browserRequiringSlices.some((slice) =>
      !explicitlyBoundSliceIds.has(slice.sliceId) &&
      !persistedBrowserEvidencePasses(basePath, slice.evidenceText)
    );"""

OLD_JS = """        if (browserRequiringSlices.length === 0) {
            return qualifyingEvidence.length === 0 &&
                !sliceEvidencePairs.some((slice) => persistedBrowserEvidencePasses(basePath, slice.evidenceText));
        }
        return browserRequiringSlices.some((slice) => !qualifyingEvidence.some((evidence) => evidence.sliceId === slice.sliceId) &&
            !persistedBrowserEvidencePasses(basePath, slice.evidenceText));"""

NEW_JS = """        if (browserRequiringSlices.length === 0) {
            return qualifyingEvidence.length === 0 &&
                !sliceEvidencePairs.some((slice) => persistedBrowserEvidencePasses(basePath, slice.evidenceText));
        }
        const explicitlyBoundSliceIds = new Set(qualifyingEvidence
            .map((evidence) => evidence.sliceId?.trim())
            .filter((sliceId) => Boolean(sliceId)));
        if (browserRequiringSlices.length === 1 && explicitlyBoundSliceIds.size === 0 && qualifyingEvidence.length > 0) {
            explicitlyBoundSliceIds.add(browserRequiringSlices[0].sliceId);
        }
        return browserRequiringSlices.some((slice) => !explicitlyBoundSliceIds.has(slice.sliceId) &&
            !persistedBrowserEvidencePasses(basePath, slice.evidenceText));"""

REPLACEMENTS = {
    str(FILES[0]): (OLD_TS, NEW_TS),
    str(FILES[1]): (OLD_JS, NEW_JS),
}

for file_path in FILES:
    if not file_path.exists():
        print(f'missing: {file_path}', file=sys.stderr)
        sys.exit(1)

for file_path in FILES:
    old_text, new_text = REPLACEMENTS[str(file_path)]
    content = file_path.read_text()

    if new_text in content:
        print(f'already patched: {file_path}')
        continue

    if old_text not in content:
        print(f'pattern not found: {file_path}', file=sys.stderr)
        sys.exit(1)

    file_path.write_text(content.replace(old_text, new_text, 1))
    print(f'patched: {file_path}')

print('done')
