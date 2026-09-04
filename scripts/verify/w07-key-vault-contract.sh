#!/usr/bin/env bash
# w07-key-vault-contract.sh — canonical verifier for M009 (workspace/V2/handoff/07).
#
# Numbering follows w06 (see that script's note: every downstream verifier is +1 from the
# handoff's own numbering).
#
# SCOPE. M009 S02 (the vault) is built; S01/S03–S09 are not. This script asserts what
# exists and PRINTS what it cannot yet check, rather than passing silently over unbuilt
# acceptance criteria — a verifier that goes green on work nobody has done is worse than
# no verifier.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "${BASH_SOURCE[0]%/*}" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
readonly TEST_PROJECT="${ROOT_DIR}/tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj"

readonly VAULT_SOURCE="${ROOT_DIR}/src/HappyGymStats.Core/War/WarKeyVault.cs"
readonly VAULT_TESTS="${ROOT_DIR}/tests/HappyGymStats.Tests/WarKeyVaultTests.cs"
readonly TOS_DOC="${ROOT_DIR}/docs/torn-api/terms-of-service.md"
readonly TOS_PAGE="${ROOT_DIR}/src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Terms.razor"
readonly TOS_VERSION_SOURCE="${ROOT_DIR}/src/HappyGymStats.Contracts/Compliance/TermsDocument.cs"

readonly TEST_FILTER="WarKeyVaultTests"

required_files=(
  "$TEST_PROJECT"
  "$VAULT_SOURCE"
  "$VAULT_TESTS"
  "$TOS_DOC"
  "$TOS_PAGE"
  "$TOS_VERSION_SOURCE"
)

# Acceptance criteria from workspace/V2/handoff/07, each pinned to a named test.
required_tests=(
  # "never in an exception message"
  "A_failed_open_never_names_the_key_or_the_ciphertext"
  # "never logged" — captured output of a failing call is grepped
  "A_failing_call_logs_nothing_containing_the_key"
  # revocation is per member: a blob cannot be replayed into another member's row
  "A_blob_moved_to_another_members_row_fails_to_open"
  "A_blob_reused_for_another_purpose_fails_to_open"
  # the stored blob must not carry the key in the clear, and must not repeat
  "Ciphertext_never_contains_the_plaintext_key"
  "Two_encryptions_of_the_same_key_differ"
  # misconfiguration must not echo the master key
  "A_master_key_of_the_wrong_length_is_refused_without_echoing_it"
  "A_non_base64_master_key_is_refused_without_echoing_it"
)

# Structural rules from handoff 07 that no runtime test can prove. A C# test asserting
# "the key is never held in a field" passes vacuously, so these are source assertions.
# Greps code only. Several rules below name the very thing they forbid ("never in
# appsettings", "not the Ecies scheme"), so a raw grep would fail on its own explanation.
code_grep() {
  local pattern="$1" file="$2"
  rg -n "$pattern" "$file" | rg -v '^[0-9]+:\s*(///|//|\*)' | rg -q .
}

pass() { printf 'PASS: %s\n' "$1"; }
fail() { printf 'FAIL: %s\n' "$1" >&2; exit 1; }
note() { printf 'NOTE: %s\n' "$1"; }

case "${1:-}" in
  -h|--help)
    printf 'Usage: bash scripts/verify/w07-key-vault-contract.sh\n\nCanonical M009 key-vault verifier: pinned acceptance tests + source rules that no runtime test can prove.\n'
    exit 0
    ;;
  "") ;;
  *) fail "unknown option '${1}'" ;;
esac

for path in "${required_files[@]}"; do
  [[ -f "$path" ]] || fail "missing required path ${path#"${ROOT_DIR}/"}"
done
pass "required key-vault paths present"

for test_name in "${required_tests[@]}"; do
  rg -n --fixed-strings "$test_name" "$VAULT_TESTS" >/dev/null \
    || fail "pinned acceptance test '$test_name' not found"
done
pass "all ${#required_tests[@]} pinned acceptance tests present"

# --- Source rules -----------------------------------------------------------

# The vault must not be the Ecies scheme. handoff 07 calls this out by name: Ecies
# encrypts to a client-held public key so the server CANNOT decrypt, which is exactly
# wrong for a key the server uses unattended.
if code_grep 'Ecies' "$VAULT_SOURCE"; then
  fail "vault references Ecies outside a comment — handoff 07 forbids reusing that scheme here"
fi
pass "vault does not use the Ecies scheme"

# "Decrypted only inside the call that uses it. Never held in a field, never in a static,
# never captured in a closure that outlives the call." The enforcing design is that no
# method hands a decrypted key back to a caller: the only way out is the callback.
if code_grep 'public\s+(static\s+)?string\s+\w*(Unprotect|Decrypt|GetKey|Reveal)\w*\s*\(' "$VAULT_SOURCE"; then
  fail "vault exposes a method returning a decrypted key as a string — use the UseKey callback shape"
fi
pass "vault exposes no method that returns a decrypted key"

# A decrypted key must not be assignable to instance state.
if code_grep '^\s*private\s+(readonly\s+)?string\s+_(apiKey|key|plaintext)' "$VAULT_SOURCE"; then
  fail "vault holds a key-shaped string field — handoff 07: never held in a field"
fi
pass "vault holds no key-shaped field"

# "never accepted as a command-line argument"
if code_grep 'args\[|GetCommandLineArgs|Environment\.CommandLine' "$VAULT_SOURCE"; then
  fail "vault reads command-line input — handoff 07: a key is never a command-line argument"
fi
pass "vault reads no command-line input"

# The master key comes from the environment, never from configuration files or git.
if code_grep 'appsettings|IConfiguration' "$VAULT_SOURCE"; then
  fail "vault reads configuration — WAR_KEY_MASTER is environment-only, never in appsettings"
fi
if ! rg -n --fixed-strings 'WAR_KEY_MASTER' "$VAULT_SOURCE" >/dev/null; then
  fail "vault does not name WAR_KEY_MASTER"
fi
pass "master key is environment-sourced only"

# The master key must never be committed. Catches a base64 32-byte literal in the source.
if code_grep '"[A-Za-z0-9+/]{43}="' "$VAULT_SOURCE"; then
  fail "a 32-byte base64 literal appears in the vault source — that looks like a committed master key"
fi
pass "no master-key-shaped literal in the vault source"

# The vault is Core logic: no transport, no HTTP, no web host.
forbidden_patterns=(
  'TornApiClient'
  'HttpClient'
  'api\.torn\.com'
  'WebApplication'
  'Kestrel'
)
for pattern in "${forbidden_patterns[@]}"; do
  if code_grep "$pattern" "$VAULT_SOURCE"; then
    fail "boundary drift: forbidden token '$pattern' in the vault source"
  fi
done
pass "vault stays inside the Core boundary"

# --- The compliance gate ----------------------------------------------------
#
# handoff 07 binds the gate to a specific moment: "Before the first key is written to the
# database." So this is not "does the document mention encryption" — the draft mentions it
# and is still unpublished; passing on that would be the exact go-green-on-unbuilt-work
# failure this script is written to avoid.
#
# The rule enforced here: if any non-test source both names a stored-key entity AND
# persists (SaveChanges), the disclosure must no longer be a draft.
gate_writers="$(rg -l --glob '!**/bin/**' --glob '!**/obj/**' 'StoredApiKey' "${ROOT_DIR}/src" 2>/dev/null || true)"
persisting_writer=""
for candidate in $gate_writers; do
  if rg -q 'SaveChanges' "$candidate"; then
    persisting_writer="$candidate"
    break
  fi
done

if ! rg -qi 'encrypt' "$TOS_DOC"; then
  fail "terms-of-service.md does not mention encryption — the gate in handoff 07 requires the disclosure to state that war keys are stored encrypted"
fi

# The disclosure only means anything if the document, the served page and the
# version stamped on consent records all say the same thing. Consent recorded
# against a version nobody can produce cannot be honoured.
doc_version="$(rg -o -m1 'Document version: \S+' "$TOS_DOC" | sed 's/Document version: //')"
code_version="$(rg -o -m1 'Version = "[^"]+"' "$TOS_VERSION_SOURCE" | sed 's/Version = "//; s/"//')"
[[ -n "${doc_version}" ]] || fail "could not read the version from terms-of-service.md"
[[ -n "${code_version}" ]] || fail "could not read TermsDocument.Version"
[[ "${doc_version}" == "${code_version}" ]] \
  || fail "disclosure version drift: terms-of-service.md says '${doc_version}', TermsDocument.Version says '${code_version}'"
pass "disclosure version agrees between the document and the code (${code_version})"

# The page must render the version, not hardcode a copy of it.
rg -q 'TermsDocument.Version' "$TOS_PAGE" \
  || fail "Terms.razor does not render TermsDocument.Version — the page could drift from the document silently"
# and it must actually carry the substance, not the old placeholder
rg -q 'Stored, encrypted' "$TOS_PAGE" \
  || fail "Terms.razor does not state that keys are stored encrypted"
rg -qi 'placeholder' "$TOS_PAGE" \
  && fail "Terms.razor still contains placeholder text"
pass "the served page carries the disclosure and its version"

if rg -q -- '-draft' "$TOS_DOC"; then
  if [[ -n "$persisting_writer" ]]; then
    fail "COMPLIANCE GATE: ${persisting_writer#"${ROOT_DIR}/"} persists a stored key while terms-of-service.md is still a draft. Publish the disclosure and record member consent first — storing a key against the published text is a Torn ToS breach."
  fi
  note "terms-of-service.md is a DRAFT and the gate is therefore UNMET — but nothing persists a key yet, so no breach. This check turns into a hard failure the moment a source both names StoredApiKey and calls SaveChanges."
else
  pass "terms-of-service.md is published (not a draft) and discloses encrypted key storage"
fi

dotnet test "$TEST_PROJECT" --filter "$TEST_FILTER" --nologo
pass "pinned key-vault tests passed (${TEST_FILTER})"

# --- Not yet in scope -------------------------------------------------------
note "M009 slices not built, therefore NOT verified here:"
note "  S01 consent record (ConsentRecordEntity + migration; document live on the site)"
note "  S03 StoredApiKeyEntity + migration"
note "  S04 linking endpoints/page, incl. refusing a Full-access key"
note "  S05 client methods, S06 poller extension, S07 scoped bearer token, S08 data tiers"
note "  revocation deleting identifiable readings — needs S03's rows to delete"
note "Extend required_tests above as each lands; do not let this script go green on unbuilt criteria."

printf 'PASS: canonical M009 key-vault verifier succeeded (S02 scope)\n'
