#!/usr/bin/env bash
# w07-key-vault-contract.sh — canonical verifier for M009 (workspace/V2/handoff/07).
#
# Numbering follows w06 (see that script's note: every downstream verifier is +1 from the
# handoff's own numbering).
#
# SCOPE. M009 S01 (versioned consent persistence), S02 (the vault), and S03 (consent-gated
# ciphertext persistence) are built. S04–S09 are not. This script asserts what exists and
# PRINTS what it cannot yet check rather than passing silently over unbuilt criteria.
set -euo pipefail

readonly SCRIPT_DIR="$(cd "${BASH_SOURCE[0]%/*}" && pwd)"
readonly ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
readonly TEST_PROJECT="${ROOT_DIR}/tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj"

readonly VAULT_SOURCE="${ROOT_DIR}/src/HappyGymStats.Core/War/WarKeyVault.cs"
readonly VAULT_TESTS="${ROOT_DIR}/tests/HappyGymStats.Tests/WarKeyVaultTests.cs"
readonly CONSENT_SOURCE="${ROOT_DIR}/src/HappyGymStats.Contracts/Entities/ConsentRecordEntity.cs"
readonly CONSENT_TESTS="${ROOT_DIR}/tests/HappyGymStats.Tests/ConsentRecordPersistenceTests.cs"
readonly CONSENT_MIGRATION="${ROOT_DIR}/src/HappyGymStats.Data/Migrations/20260904170000_AddConsentRecords.cs"
readonly STORED_KEY_SOURCE="${ROOT_DIR}/src/HappyGymStats.Contracts/Entities/StoredApiKeyEntity.cs"
readonly STORED_KEY_STORE="${ROOT_DIR}/src/HappyGymStats.Data/Repositories/StoredApiKeyStore.cs"
readonly STORED_KEY_TESTS="${ROOT_DIR}/tests/HappyGymStats.Tests/StoredApiKeyPersistenceTests.cs"
readonly STORED_KEY_POSTGRES_TESTS="${ROOT_DIR}/tests/HappyGymStats.Tests/StoredApiKeyPostgresPersistenceTests.cs"
readonly STORED_KEY_MIGRATION="${ROOT_DIR}/src/HappyGymStats.Data/Migrations/20260905231500_AddStoredApiKeys.cs"
readonly MODEL_SNAPSHOT="${ROOT_DIR}/src/HappyGymStats.Data/Migrations/HappyGymStatsDbContextModelSnapshot.cs"
readonly DB_CONTEXT="${ROOT_DIR}/src/HappyGymStats.Data/HappyGymStatsDbContext.cs"
readonly TOS_DOC="${ROOT_DIR}/docs/torn-api/terms-of-service.md"
readonly TOS_PAGE="${ROOT_DIR}/src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Terms.razor"
readonly TOS_VERSION_SOURCE="${ROOT_DIR}/src/HappyGymStats.Contracts/Compliance/TermsDocument.cs"

readonly TEST_FILTER="WarKeyVaultTests"
readonly CONSENT_TEST_FILTER="ConsentRecordPersistenceTests"
readonly STORED_KEY_TEST_FILTER="StoredApiKeyPersistenceTests"

required_files=(
  "$TEST_PROJECT"
  "$VAULT_SOURCE"
  "$VAULT_TESTS"
  "$CONSENT_SOURCE"
  "$CONSENT_TESTS"
  "$CONSENT_MIGRATION"
  "$STORED_KEY_SOURCE"
  "$STORED_KEY_STORE"
  "$STORED_KEY_TESTS"
  "$STORED_KEY_POSTGRES_TESTS"
  "$STORED_KEY_MIGRATION"
  "$MODEL_SNAPSHOT"
  "$DB_CONTEXT"
  "$TOS_DOC"
  "$TOS_PAGE"
  "$TOS_VERSION_SOURCE"
)

required_tests=(
  "A_failed_open_never_names_the_key_or_the_ciphertext"
  "A_failing_call_logs_nothing_containing_the_key"
  "A_blob_moved_to_another_members_row_fails_to_open"
  "A_blob_reused_for_another_purpose_fails_to_open"
  "Ciphertext_never_contains_the_plaintext_key"
  "Two_encryptions_of_the_same_key_differ"
  "A_master_key_of_the_wrong_length_is_refused_without_echoing_it"
  "A_non_base64_master_key_is_refused_without_echoing_it"
)

required_consent_tests=(
  "Consent_record_persists_published_version_purpose_and_revocation_without_player_identity"
  "Consent_records_keep_distinct_versions_as_auditable_history"
)

required_stored_key_tests=(
  "Current_consent_and_owner_store_only_vault_ciphertext"
  "Another_tenants_consent_cannot_authorize_storage"
  "Revoked_or_stale_consent_cannot_authorize_storage"
  "Consent_without_an_owning_identity_cannot_authorize_storage"
)

code_grep() {
  local pattern="$1" file="$2"
  rg -n "$pattern" "$file" | rg -v '^[0-9]+:\s*(///|//|\*)' | rg -q .
}

pass() { printf 'PASS: %s\n' "$1"; }
fail() { printf 'FAIL: %s\n' "$1" >&2; exit 1; }
note() { printf 'NOTE: %s\n' "$1"; }

case "${1:-}" in
  -h|--help)
    printf 'Usage: bash scripts/verify/w07-key-vault-contract.sh\n\nCanonical M009 key-vault verifier: consent/version contract + vault and stored-key persistence acceptance tests + source rules that no runtime test can prove.\n'
    exit 0
    ;;
  "") ;;
  *) fail "unknown option '${1}'" ;;
esac

for path in "${required_files[@]}"; do
  [[ -f "$path" ]] || fail "missing required path ${path#"${ROOT_DIR}/"}"
done
pass "required M009 S01/S02/S03 paths present"

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

for test_name in "${required_stored_key_tests[@]}"; do
  rg -n --fixed-strings "$test_name" "$STORED_KEY_TESTS" >/dev/null \
    || fail "pinned stored-key test '$test_name' not found"
done
pass "all ${#required_stored_key_tests[@]} pinned stored-key persistence tests present"

if code_grep 'TornPlayerId|ApiKey|StoredApiKey|Ciphertext|Plaintext' "$CONSENT_SOURCE"; then
  fail "ConsentRecordEntity contains a raw Torn identity/key-shaped property — consent must stay separate from credential storage"
fi
rg -q 'AnonymousId' "$CONSENT_SOURCE" || fail "ConsentRecordEntity is not scoped by the HGS anonymous identity"
rg -q 'DocumentVersion' "$CONSENT_SOURCE" || fail "ConsentRecordEntity does not stamp a disclosure version"
rg -q 'Purpose' "$CONSENT_SOURCE" || fail "ConsentRecordEntity does not stamp a consent purpose"
rg -q 'AcceptedAtUtc' "$CONSENT_SOURCE" || fail "ConsentRecordEntity does not record acceptance time"
rg -q 'RevokedAtUtc' "$CONSENT_SOURCE" || fail "ConsentRecordEntity does not record revocation state"
pass "consent row contains audit facts only, with no Torn key/player identity field"

rg -q 'DbSet<ConsentRecordEntity> ConsentRecords' "$DB_CONTEXT" \
  || fail "HappyGymStatsDbContext does not expose ConsentRecords"
rg -Fq 'CreateTable(' "$CONSENT_MIGRATION" \
  || fail "consent migration does not create a table"
rg -q 'name: "ConsentRecords"' "$CONSENT_MIGRATION" \
  || fail "consent migration does not create ConsentRecords"
rg -q 'HappyGymStats.Data.Entities.ConsentRecordEntity' "$MODEL_SNAPSHOT" \
  || fail "EF model snapshot does not contain ConsentRecordEntity"
pass "consent persistence is represented in DbContext, migration and model snapshot"

if code_grep 'Ecies' "$VAULT_SOURCE"; then
  fail "vault references Ecies outside a comment — handoff 07 forbids reusing that scheme here"
fi
pass "vault does not use the Ecies scheme"

if code_grep 'public\s+(static\s+)?string\s+\w*(Unprotect|Decrypt|GetKey|Reveal)\w*\s*\(' "$VAULT_SOURCE"; then
  fail "vault exposes a method returning a decrypted key as a string — use the UseKey callback shape"
fi
pass "vault exposes no method that returns a decrypted key"

if code_grep '^\s*private\s+(readonly\s+)?string\s+_(apiKey|key|plaintext)' "$VAULT_SOURCE"; then
  fail "vault holds a key-shaped string field — handoff 07: never held in a field"
fi
pass "vault holds no key-shaped field"

if code_grep 'args\[|GetCommandLineArgs|Environment\.CommandLine' "$VAULT_SOURCE"; then
  fail "vault reads command-line input — handoff 07: a key is never a command-line argument"
fi
pass "vault reads no command-line input"

if code_grep 'appsettings|IConfiguration' "$VAULT_SOURCE"; then
  fail "vault reads configuration — WAR_KEY_MASTER is environment-only, never in appsettings"
fi
if ! rg -n --fixed-strings 'WAR_KEY_MASTER' "$VAULT_SOURCE" >/dev/null; then
  fail "vault does not name WAR_KEY_MASTER"
fi
pass "master key is environment-sourced only"

if code_grep '"[A-Za-z0-9+/]{43}="' "$VAULT_SOURCE"; then
  fail "a 32-byte base64 literal appears in the vault source — that looks like a committed master key"
fi
pass "no master-key-shaped literal in the vault source"

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

rg -q 'DbSet<StoredApiKeyEntity> StoredApiKeys' "$DB_CONTEXT" \
  || fail "HappyGymStatsDbContext does not expose StoredApiKeys"
rg -q 'name: "StoredApiKeys"' "$STORED_KEY_MIGRATION" \
  || fail "stored-key migration does not create StoredApiKeys"
rg -q 'FK_StoredApiKeys_IdentityMap_AnonymousId' "$STORED_KEY_MIGRATION" \
  || fail "stored-key migration does not enforce owning AnonymousId"
rg -q 'ConsentRecordId.*AnonymousId|ConsentRecordId, x.AnonymousId' "$STORED_KEY_MIGRATION" \
  || fail "stored-key migration does not bind consent to the same AnonymousId"
if code_grep 'public\s+string\??\s+\w*(ApiKey|Plaintext|KeyValue)' "$STORED_KEY_SOURCE"; then
  fail "StoredApiKeyEntity exposes a plaintext/key-shaped string property"
fi
rg -q 'byte\[\]\s+Ciphertext' "$STORED_KEY_SOURCE" \
  || fail "StoredApiKeyEntity does not store vault ciphertext"
rg -q 'IsolationLevel.Serializable' "$STORED_KEY_STORE" \
  || fail "stored-key writer does not check prerequisites inside a serializable transaction"
rg -q 'x.AnonymousId == anonymousId' "$STORED_KEY_STORE" \
  || fail "stored-key writer does not scope prerequisite queries by owning AnonymousId"
rg -q 'x.Purpose == ConsentPurposes.WarMemberApiKey' "$STORED_KEY_STORE" \
  || fail "stored-key writer does not require explicit war-member-key consent"
rg -q 'x.DocumentVersion == TermsDocument.Version' "$STORED_KEY_STORE" \
  || fail "stored-key writer does not require consent to the current disclosure"
rg -q 'x.RevokedAtUtc == null' "$STORED_KEY_STORE" \
  || fail "stored-key writer does not reject revoked consent"
rg -q '_vault.Protect' "$STORED_KEY_STORE" \
  || fail "stored-key writer does not encrypt through WarKeyVault"
pass "stored-key persistence is ciphertext-only and transactionally gated by owner/current consent"

if ! rg -qi 'encrypt' "$TOS_DOC"; then
  fail "terms-of-service.md does not mention encryption — the gate in handoff 07 requires the disclosure to state that war keys are stored encrypted"
fi

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

rg -q 'TermsDocument.Version' "$TOS_PAGE" \
  || fail "Terms.razor does not render TermsDocument.Version — the page could drift from the document silently"
rg -q 'Stored, encrypted' "$TOS_PAGE" \
  || fail "Terms.razor does not state that keys are stored encrypted"
rg -qi 'placeholder' "$TOS_PAGE" \
  && fail "Terms.razor still contains placeholder text"
pass "the served page carries the disclosure and its version"

if rg -q -- '-draft' "$TOS_DOC"; then
  fail "COMPLIANCE GATE: stored-key persistence exists while terms-of-service.md is still a draft. Publish the disclosure and record member consent first."
else
  pass "terms-of-service.md is published (not a draft) and discloses encrypted key storage"
fi

dotnet test "$TEST_PROJECT" --filter "$TEST_FILTER" --nologo
pass "pinned key-vault tests passed (${TEST_FILTER})"

dotnet test "$TEST_PROJECT" --filter "$CONSENT_TEST_FILTER" --nologo
pass "pinned consent persistence tests passed (${CONSENT_TEST_FILTER})"

dotnet test "$TEST_PROJECT" --filter "$STORED_KEY_TEST_FILTER" --nologo
pass "pinned stored-key persistence tests passed (${STORED_KEY_TEST_FILTER})"

note "M009 slices not built, therefore NOT verified here:"
note "  S04 linking endpoints/page, incl. ownership verification and refusing a Full-access key"
note "  S05 client methods, S06 poller extension, S07 scoped bearer token, S08 data tiers"
note "  revocation deleting identifiable readings — now has S03 rows to target, but deletion wiring is later"
note "Extend required tests/source assertions as each slice lands; do not let this script go green on unbuilt criteria."

printf 'PASS: canonical M009 verifier succeeded (S01 consent + S02 vault + S03 stored-key persistence)\n'
