#!/usr/bin/env bash
set -euo pipefail

README_FILE="README.md"
OVERVIEW_FILE="docs/OVERVIEW.md"
SETUP_FILE="docs/SETUP.md"
DEPLOYMENT_FILE="docs/DEPLOYMENT.md"
HTTP_FILE="src/HappyGymStats.Api/HappyGymStats.Api.http"

fail() {
  echo "[FAIL] $1" >&2
  exit 1
}

pass() {
  echo "[PASS] $1"
}

check_file_exists() {
  local path="$1"
  [[ -f "$path" ]] || fail "Missing required file: $path"
  pass "File exists: $path"
}

check_contains() {
  local path="$1"
  local needle="$2"
  local label="$3"
  grep -Fq -- "$needle" "$path" || fail "$label (expected token: $needle in $path)"
  pass "$label"
}

check_not_contains() {
  local path="$1"
  local needle="$2"
  local label="$3"
  if grep -Fq -- "$needle" "$path"; then
    fail "$label (unexpected token: $needle in $path)"
  fi
  pass "$label"
}

check_file_exists "$README_FILE"
check_file_exists "$OVERVIEW_FILE"
check_file_exists "$SETUP_FILE"
check_file_exists "$DEPLOYMENT_FILE"
check_file_exists "$HTTP_FILE"

# Required current-state contract markers (README)
check_contains "$README_FILE" "ASP.NET API, a Blazor frontend, and an AdminPanel surface" "README states current runtime surfaces"
check_contains "$README_FILE" "backed by Postgres, with Keycloak-protected admin/auth flows" "README states Postgres + Keycloak production shape"
check_contains "$README_FILE" "src/HappyGymStats.Api" "README links current API project"
check_contains "$README_FILE" "/api/v1/torn/*" "README references current API route family"
check_contains "$README_FILE" ".gsd/milestones/M003/M003-ROADMAP.md" "README includes milestone/audit reference"

# Required current-state contract markers (Overview)
check_contains "$OVERVIEW_FILE" "POST /api/v1/torn/import-jobs" "Overview documents import route contract"
check_contains "$OVERVIEW_FILE" "GET /api/v1/torn/surfaces/meta" "Overview documents surfaces/meta route"
check_contains "$OVERVIEW_FILE" "GET /api/v1/torn/surfaces/latest" "Overview documents surfaces/latest route"
check_contains "$OVERVIEW_FILE" "Blazor frontend" "Overview includes Blazor operational peer"
check_contains "$OVERVIEW_FILE" "AdminPanel" "Overview includes AdminPanel operational peer"
check_contains "$OVERVIEW_FILE" "Identity/Keycloak" "Overview includes Keycloak boundary"

# Required current-state contract markers (Setup)
check_contains "$SETUP_FILE" "HAPPYGYMSTATS_CONNECTION_STRING" "Setup includes required connection string env var"
check_contains "$SETUP_FILE" "ConnectionStrings__HappyGymStats" "Setup includes API config alias env var"
check_contains "$SETUP_FILE" "bash scripts/verify/production-smoke.sh" "Setup includes production smoke verifier"
check_contains "$SETUP_FILE" "--no-launch-profile" "Setup documents launch-profile override guard"
check_contains "$SETUP_FILE" "happygymstats-blazor" "Setup includes Blazor service contract name"
check_contains "$SETUP_FILE" "happygymstats-adminpanel" "Setup includes AdminPanel service contract name"

# Required current-state contract markers (Deployment)
check_contains "$DEPLOYMENT_FILE" "happygymstats-api" "Deployment includes API service name"
check_contains "$DEPLOYMENT_FILE" "happygymstats-blazor" "Deployment includes Blazor service name"
check_contains "$DEPLOYMENT_FILE" "happygymstats-adminpanel" "Deployment includes AdminPanel service name"
check_contains "$DEPLOYMENT_FILE" "https://torn.geromet.com/api/v1/torn/health" "Deployment includes external API health route"
check_contains "$DEPLOYMENT_FILE" "https://admin.geromet.com/admin/health" "Deployment includes admin health route"
check_contains "$DEPLOYMENT_FILE" "bash scripts/verify/production-smoke.sh" "Deployment includes canonical smoke command"

# Required current-state contract markers (.http examples)
check_contains "$HTTP_FILE" "@HappyGymStats.Api_HostAddress = http://localhost:5047" ".http uses current local API default host"
check_contains "$HTTP_FILE" "replace-with-torn-api-key" ".http uses non-secret API key placeholder"
check_contains "$HTTP_FILE" "/api/v1/torn/import-jobs" ".http includes import route"
check_contains "$HTTP_FILE" "/api/v1/torn/surfaces/meta" ".http includes surfaces meta route"
check_contains "$HTTP_FILE" "/api/v1/torn/surfaces/latest" ".http includes surfaces latest route"
check_contains "$HTTP_FILE" "/api/v1/torn/gym-trains?limit=2" ".http includes gym-trains read model route"
check_contains "$HTTP_FILE" "/api/v1/torn/happy-events?limit=2" ".http includes happy-events read model route"

# Known stale primary-claim guards (fail fast)
check_not_contains "$README_FILE" "SQLite is the production database" "README does not claim SQLite is primary production storage"
check_not_contains "$README_FILE" "web/ static frontend is the primary frontend" "README does not claim legacy static web path is primary"
check_not_contains "$OVERVIEW_FILE" "primary architecture is static web" "Overview does not claim static web as primary architecture"
check_not_contains "$DEPLOYMENT_FILE" "/api/import" "Deployment does not reference stale non-v1 route"
check_not_contains "$HTTP_FILE" "/api/import" ".http does not reference stale non-v1 import route"

echo "S08 docs contract drift checks passed."
