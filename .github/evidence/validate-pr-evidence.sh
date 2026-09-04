#!/usr/bin/env bash
# Validate the exact machine-readable hgs-evidence block in a PR body.
set -euo pipefail

readonly ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
readonly CLASSIFIER="${ROOT_DIR}/.github/evidence/required-evidence.sh"

usage() {
  cat <<'EOF'
Usage:
  bash .github/evidence/validate-pr-evidence.sh --body-file FILE --base REF
  bash .github/evidence/validate-pr-evidence.sh --body-file FILE --files PATH [PATH ...]
EOF
}

body_file=""
base=""
declare -a files=()
while (($#)); do
  case "$1" in
    --body-file) (($# >= 2)) || { echo "ERROR: --body-file requires a path" >&2; exit 2; }; body_file="$2"; shift 2 ;;
    --base) (($# >= 2)) || { echo "ERROR: --base requires a ref" >&2; exit 2; }; base="$2"; shift 2 ;;
    --files) shift; files=("$@"); break ;;
    -h|--help) usage; exit 0 ;;
    *) echo "ERROR: unknown argument: $1" >&2; exit 2 ;;
  esac
done

[[ -f "$body_file" ]] || { echo "ERROR: PR body file missing: $body_file" >&2; exit 2; }
if [[ -z "$base" && ${#files[@]} -eq 0 ]]; then
  echo "ERROR: pass --base or --files" >&2
  exit 2
fi

block="$(awk '
  /^<!--[[:space:]]+hgs-evidence[[:space:]]*$/ { if (inside) duplicate=1; inside=1; found++; next }
  inside && /^-->[[:space:]]*$/ { inside=0; next }
  inside { print }
  END { if (found != 1 || inside || duplicate) exit 42 }
' "$body_file")" || {
  echo "ERROR: PR body must contain exactly one complete <!-- hgs-evidence ... --> block" >&2
  exit 1
}

field() {
  local key="$1" count value
  count="$(printf '%s\n' "$block" | grep -cE "^${key}:[[:space:]]*" || true)"
  [[ "$count" == "1" ]] || { echo "ERROR: evidence field '${key}' must appear exactly once" >&2; return 1; }
  value="$(printf '%s\n' "$block" | sed -nE "s/^${key}:[[:space:]]*//p")"
  [[ -n "$value" ]] || { echo "ERROR: evidence field '${key}' may not be empty" >&2; return 1; }
  printf '%s' "$value"
}

for required_field in task lease required observed unverified regression security-negative-control tier2 tier3 tier4; do
  field "$required_field" >/dev/null || exit 1
done

task="$(field task)"
lease="$(field lease)"
declared_required="$(field required)"
observed="$(field observed)"
unverified="$(field unverified)"
regression="$(field regression)"
security_negative_control="$(field security-negative-control)"
tier2_detail="$(field tier2)"
tier3_detail="$(field tier3)"
tier4_detail="$(field tier4)"

[[ "$task" != "#ISSUE" ]] || { echo "ERROR: replace the task placeholder" >&2; exit 1; }
[[ "$regression" != "describe the check/negative control that would fail without this change" ]] || {
  echo "ERROR: replace the regression placeholder" >&2
  exit 1
}

normalize_tiers() {
  local raw="$1"
  if [[ "$raw" == "none" ]]; then printf 'none'; return 0; fi
  local compact token out="" tier
  compact="${raw//[[:space:]]/}"
  declare -A seen=()
  IFS=',' read -r -a tokens <<< "$compact"
  for token in "${tokens[@]}"; do
    [[ "$token" =~ ^T[1-4]$ ]] || { echo "ERROR: invalid evidence tier '${token}' in '${raw}'" >&2; return 1; }
    seen["$token"]=1
  done
  for tier in T1 T2 T3 T4; do
    [[ -n "${seen[$tier]:-}" ]] || continue
    [[ -z "$out" ]] || out+=","
    out+="$tier"
  done
  printf '%s' "$out"
}

if ((${#files[@]} > 0)); then
  mapfile -t computed_lines < <(bash "$CLASSIFIER" --files "${files[@]}")
  security_boundary="$(bash "$CLASSIFIER" --security-boundary --files "${files[@]}")"
else
  mapfile -t computed_lines < <(bash "$CLASSIFIER" --base "$base")
  security_boundary="$(bash "$CLASSIFIER" --security-boundary --base "$base")"
fi
computed_csv="$(IFS=,; printf '%s' "${computed_lines[*]}")"
[[ -n "$computed_csv" ]] || computed_csv="none"
security_boundary="${security_boundary#security_boundary=}"
[[ "$security_boundary" == "changed" || "$security_boundary" == "unchanged" ]] || {
  echo "ERROR: classifier returned invalid security boundary '${security_boundary}'" >&2
  exit 2
}

declared_required="$(normalize_tiers "$declared_required")" || exit 1
observed="$(normalize_tiers "$observed")" || exit 1
unverified="$(normalize_tiers "$unverified")" || exit 1

if [[ "$declared_required" != "$computed_csv" ]]; then
  echo "ERROR: required evidence declaration disagrees with the diff" >&2
  echo "       computed: $computed_csv" >&2
  echo "       declared: $declared_required" >&2
  exit 1
fi

contains_tier() { [[ ",$1," == *",$2,"* ]]; }

if [[ "$computed_csv" != "none" ]]; then
  IFS=',' read -r -a required_tiers <<< "$computed_csv"
  for tier in "${required_tiers[@]}"; do
    if contains_tier "$observed" "$tier"; then
      contains_tier "$unverified" "$tier" && { echo "ERROR: ${tier} cannot be both observed and unverified" >&2; exit 1; }
    elif ! contains_tier "$unverified" "$tier"; then
      echo "ERROR: required ${tier} is neither observed nor explicitly unverified" >&2
      exit 1
    fi
  done
fi

for label in observed unverified; do
  csv="${!label}"
  [[ "$csv" == "none" ]] && continue
  IFS=',' read -r -a declared_tiers <<< "$csv"
  for tier in "${declared_tiers[@]}"; do
    if ! contains_tier "$computed_csv" "$tier"; then
      echo "ERROR: ${tier} is marked ${label} but is not required by this diff" >&2
      exit 1
    fi
  done
done

if [[ "$security_boundary" == "changed" ]]; then
  if [[ "$security_negative_control" == "n/a" || "$security_negative_control" == "describe the forbidden path and observed rejection/failure" ]]; then
    echo "ERROR: security boundary changed; security-negative-control must name the forbidden path and observed rejection/failure" >&2
    exit 1
  fi
elif [[ "$security_negative_control" != "n/a" ]]; then
  echo "ERROR: security-negative-control must be n/a when the classifier reports security_boundary=unchanged" >&2
  exit 1
fi

check_detail() {
  local tier="$1" detail="$2"
  if contains_tier "$computed_csv" "$tier"; then
    [[ "$detail" != "n/a" ]] || { echo "ERROR: ${tier} is required; its detail field may not be n/a" >&2; return 1; }
  fi
}
check_detail T2 "$tier2_detail" || exit 1
check_detail T3 "$tier3_detail" || exit 1
check_detail T4 "$tier4_detail" || exit 1

printf 'PR EVIDENCE PASS — task=%s lease=%s required=%s observed=%s unverified=%s security_boundary=%s\n' \
  "$task" "$lease" "$computed_csv" "$observed" "$unverified" "$security_boundary"
