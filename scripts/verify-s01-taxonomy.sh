#!/usr/bin/env bash
set -euo pipefail

TAXONOMY_FILE=".gsd/milestones/M002/slices/S01/research/modifier-provenance-taxonomy.md"
API_FILE="src/HappyGymStats.Api/Program.cs"
GYM_TRAINS_CONTROLLER="src/HappyGymStats.Api/Controllers/GymTrainsController.cs"
EXTRACTOR_FILE="src/HappyGymStats.Core/Reconstruction/LogEventExtractor.cs"

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
  grep -Fq "$needle" "$path" || fail "$label (expected token: $needle in $path)"
  pass "$label"
}

check_markdown_heading() {
  local heading="$1"
  grep -Eq "^## ${heading}$" "$TAXONOMY_FILE" || fail "Missing taxonomy section heading: ## ${heading}"
  pass "Taxonomy section present: ${heading}"
}

check_file_exists "$TAXONOMY_FILE"
check_file_exists "$API_FILE"
check_file_exists "$GYM_TRAINS_CONTROLLER"
check_file_exists "$EXTRACTOR_FILE"

# Taxonomy completeness guardrails (required sections from S01/T02 artifact)
check_markdown_heading "Purpose"
check_markdown_heading "Source Anchors"
check_markdown_heading "Key Scope Requirements"
check_markdown_heading "Taxonomy Matrix"
check_markdown_heading "Confidence Impact Mapping"
check_markdown_heading "Open Unknowns"
check_markdown_heading "Layering Boundary Guardrail"

# Taxonomy -> API anchor integrity
check_contains "$TAXONOMY_FILE" "src/HappyGymStats.Api/Program.cs" "Taxonomy references Program.cs anchor"
check_contains "$GYM_TRAINS_CONTROLLER" "api/v1/torn/gym-trains" "GymTrainsController defines /api/v1/torn/gym-trains route"

# Taxonomy -> extractor token integrity (field candidates listed in matrix)
check_contains "$TAXONOMY_FILE" "data.happy_used" "Taxonomy includes data.happy_used candidate"
check_contains "$TAXONOMY_FILE" "data.maximum_happy_after" "Taxonomy includes data.maximum_happy_after candidate"
check_contains "$TAXONOMY_FILE" "data.maximum_happy_before" "Taxonomy includes data.maximum_happy_before candidate"
check_contains "$TAXONOMY_FILE" "data.happy_decreased" "Taxonomy includes data.happy_decreased candidate"
check_contains "$TAXONOMY_FILE" "data.happy_increased" "Taxonomy includes data.happy_increased candidate"

check_contains "$EXTRACTOR_FILE" "\"happy_used\"" "Extractor still parses happy_used"
check_contains "$EXTRACTOR_FILE" "\"maximum_happy_after\"" "Extractor still parses maximum_happy_after"
check_contains "$EXTRACTOR_FILE" "\"maximum_happy_before\"" "Extractor still parses maximum_happy_before"
check_contains "$EXTRACTOR_FILE" "\"happy_decreased\"" "Extractor still parses happy_decreased"
check_contains "$EXTRACTOR_FILE" "\"happy_increased\"" "Extractor still parses happy_increased"

echo "S01 taxonomy drift checks passed."
