#!/usr/bin/env bash
# w07-key-vault-contract.sh — canonical verifier for M009 (workspace/V2/handoff/07).
#
# Numbering follows w06 (see that script's note: every downstream verifier is +1 from the
# handoff's own numbering).
#
# SCOPE. M009 S01 (versioned consent persistence) and S02 (the vault) are built;
# S03–S09 are not. This script asserts what exists and PRINTS what it cannot yet check,
# rather than passing silently over unbuilt acceptance criteria — a verifier that goes green
# on work nobody has done is worse than no verifier.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
readonly TEST_PROJECT="${ROOT_DIR}/tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj"

readonly VAULT_SOURCE="${ROOT_DIR}/src/HappyGymStats.Core/War/WarKeyVault.cs"
readonly VAULT_TESTS="${ROOT_DIR}/tests/HappyGymStats.Tests/WarKeyVaultTests.cs"
readonly CONSENT_SOURCE="${ROOT_DIR}/src/HappyGymStats.Contracts/Entities/ConsentRecordEntity.cs"
readonly CONSENT_TESTS="${ROOT_DIR}/tests/HappyGymStats.Tests/ConsentRecordPersistenceTests.cs"
readonly CONSENT_MIGRATION="${ROOT_DIR}/src/HappyGymStats.Data/Migrations/20260904170000_AddConsentRecords.cs"
readonly MODEL_SNAPSHOT="${ROOT_DIR}/src/HappyGymStats.Data/Migrations/HappyGymStatsDbContextModelSnapshot.cs"
readonly DB_CONTEXT="${ROOT_DIR}/src/HappyGymStats.Data/HappyGymStatsDbContext.cs"
readonly TOS_DOC="${ROOT_DIR}/docs/torn-api/terms-of-service.md"
readonly TOS_PAGE="${ROOT_DIR}/src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Terms.razor"
readonly TOS_VERSION_SOURCE="${ROOT_DIR}/src/HappyGymStats.Contracts/Compliance/TermsDocument.cs"

readonly TEST_FILTER="WarKeyVaultTests"
readonly CONSENT_TEST_FILTER="ConsentRecordPersistenceTests"

required_files=(
  "$TEST_PROJECT"
  "$VAULT_SOURCE"
  "$VAULT_TESTS"
  "$CONSENT_SOURCE"
  "$CONSENT_TESTS"
  "$CONSENT_MIGRATION"
  "$MODEL_SNAPSHOT"
  "$DB_CONTEXT"
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

required_consent_tests=(
  "Consent_record_persists_published_version_purpose_and_revocation_without_player_identity"
  "Consent_records_keep_distinct_versions_as_auditable_history"
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
    printf 'Usage: bash scripts/verify/w07-key-vault-contract.sh\n\nCanonical M009 key-vault verifier: consent/version contract + pinned vault acceptance tests + source rules that no runtime test can prove.\n'
    exit 0
    ;;
  "") ;;
  *) fail "unknown option '${1}'" ;;
esac

for path in "${required_files[@]}"; do
  [[ -f "$path" ]] || fail "missing required path ${path#"${ROOT_DIR}/"}"
done
pass "required M009 S01/S02 paths present"

for test_name in "${required_tests[@]}"; do
  rg -n --fixed-strings "$test_name" "$VAULT_TESTS" >/dev/null \
    || fail "pinned acceptance test '$test_name' not found"
done
pass "all ${#required_tests[@]} pinned vault acceptance tests present"

for test_name in "${required_consent_tests[@]}"; do
  rg -n --fixed-strings "$test_name" "$CONSENT_TESTS" >/dev/null \
    || fail "pinned consent test '$test_name' not found"
done
pass "all ${#required_consent_tests[@]} pinned consent persistence tests present"

# --- S01 consent/version contract ------------------------------------------

# Consent is an audit fact about the HGS anonymous identity. The row is intentionally
# incapable of becoming a second credential store or a raw Torn identity table.
if code_grep 'TornPlayerId|ApiKey|StoredApiKey|Ciphertext|Plaintext' "$CONSENT_SOURCE"; then
  fail "ConsentRecordEntity contains a raw Torn identity/key-shaped property — consent must stay separate from credential storage"
fi
rg -q 'AnonymousId' "$CONSENT_SOURCE" || fail "ConsentRecordEntity is not scoped by the HGS anonymous identity"
rg -q 'DocumentVersion' "$CONSENT_SOURCE" || fail "ConsentRecordEntity does not stamp a disclosure version"
rg -q 'Purpose' "$CONSENT_SOURCE" || fail "ConsentRecordEntity does not stamp a consent purpose"
rg -q 'AcceptedAtUtc' "$CONSENT_SOURCE" || fail "ConsentRecordEntity does not record acceptance time"
rg -q 'RevokedAtUtc' "$CONSENT_SOURCE" || fail "ConsentRecordEntity does not record revocation state"
pass "consent row contains audit facts only, with no Torn key/player identity field"

# Persistence has to exist before S03 is allowed to add a stored-key row. Pin all three
# EF surfaces so an entity-only change cannot masquerade as durable consent.
rg -q 'DbSet<ConsentRecordEntity> ConsentRecords' "$DB_CONTEXT" \
  || fail "HappyGymStatsDbContext does not expose ConsentRecords"
rg -q 'CreateTable(' "$CONSENT_MIGRATION" \
  || fail "consent migration does not create a table"
rg -q 'name: "ConsentRecords"' "$CONSENT_MIGRATION" \
  || fail "consent migration does not create ConsentRecords"
rg -q 'HappyGymStats.Data.Entities.ConsentRecordEntity' "$MODEL_SNAPSHOT" \
  || fail "EF model snapshot does not contain ConsentRecordEntity"
pass "consent persistence is represented in DbContext, migration and model snapshot"

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
# database." So this is not "does the document mention encryption" — a draft can mention it
# and still fail the gate. S01 now supplies the durable consent record S03 must transact with.
#
# The rule enforced here: if any non-test source both names a stored-key entity AND
# persists (SaveChanges), the disclosure must be published; the S03 slice must additionally
# prove consent + key persistence are one transaction before it may claim completion.
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

rg -q 'TermsDocument.Version' "$CONSENT_TESTS" \
  || fail "consent persistence tests do not stamp TermsDocument.Version — consent could drift from the published disclosure"
rg -q 'ConsentPurposes.WarMemberApiKey' "$CONSENT_TESTS" \
  || fail "consent persistence tests do not pin the war-member-key purpose"
pass "consent persistence is pinned to the published version and explicit purpose"

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

dotnet test "$TEST_PROJECT" --filter "$CONSENT_TEST_FILTER" --nologo
pass "pinned consent persistence tests passed (${CONSENT_TEST_FILTER})"

# --- Not yet in scope -------------------------------------------------------
note "M009 slices not built, therefore NOT verified here:"
note "  S03 StoredApiKeyEntity + migration + transactional consent/key write gate"
note "  S04 linking endpoints/page, incl. ownership verification and refusing a Full-access key"
note "  S05 client methods, S06 poller extension, S07 scoped bearer token, S08 data tiers"
note "  revocation deleting identifiable readings — needs S03's rows to delete"
note "Extend required tests/source assertions as each slice lands; do not let this script go green on unbuilt criteria."

printf 'PASS: canonical M009 verifier succeeded (S01 consent + S02 vault scope)\n'
