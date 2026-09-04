#!/usr/bin/env bash
# Classify a diff into the repository evidence tiers from checked-in path rules.
set -euo pipefail

readonly ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
readonly DEFAULT_RULES="${ROOT_DIR}/.github/evidence/evidence-rules.tsv"

usage() {
  cat <<'EOF'
Usage:
  bash .github/evidence/required-evidence.sh [--base REF] [--format tsv|json]
  bash .github/evidence/required-evidence.sh [--format tsv|json] --files PATH [PATH ...]

Without --files, changed paths come from `git diff --name-only <base>...HEAD`.
The default base is `main`; CI callers should pass the exact pull-request base SHA.
EOF
}

base="main"
format="tsv"
rules_file="${EVIDENCE_RULES_FILE:-${DEFAULT_RULES}}"
declare -a files=()

while (($#)); do
  case "$1" in
    --base)
      (($# >= 2)) || { echo "ERROR: --base requires a ref" >&2; exit 2; }
      base="$2"
      shift 2
      ;;
    --format)
      (($# >= 2)) || { echo "ERROR: --format requires tsv or json" >&2; exit 2; }
      format="$2"
      shift 2
      ;;
    --rules)
      (($# >= 2)) || { echo "ERROR: --rules requires a path" >&2; exit 2; }
      rules_file="$2"
      shift 2
      ;;
    --files)
      shift
      files=("$@")
      break
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "ERROR: unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

case "${format}" in
  tsv|json) ;;
  *) echo "ERROR: unsupported format '${format}'" >&2; exit 2 ;;
esac

[[ -f "${rules_file}" ]] || { echo "ERROR: evidence rules not found: ${rules_file}" >&2; exit 2; }

# Validate the policy before classifying anything. An exception/rule with no
# checked-in reason is rejected rather than silently becoming folklore.
rule_line=0
while IFS=$'\t' read -r pattern tier reason extra; do
  ((rule_line += 1))
  (( rule_line == 1 )) && {
    [[ "${pattern}" == "pattern" && "${tier}" == "tier" && "${reason}" == "reason" ]] || {
      echo "ERROR: malformed evidence-rules header" >&2
      exit 2
    }
    continue
  }
  [[ -z "${pattern}${tier}${reason}${extra}" ]] && continue
  [[ -z "${extra:-}" ]] || { echo "ERROR: extra TSV field on rule line ${rule_line}" >&2; exit 2; }
  [[ -n "${pattern}" ]] || { echo "ERROR: empty pattern on rule line ${rule_line}" >&2; exit 2; }
  [[ "${tier}" =~ ^T[1-4]$ ]] || { echo "ERROR: invalid tier '${tier}' on rule line ${rule_line}" >&2; exit 2; }
  [[ -n "${reason}" ]] || { echo "ERROR: missing checked-in reason on rule line ${rule_line}" >&2; exit 2; }
done < "${rules_file}"

if ((${#files[@]} == 0)); then
  command -v git >/dev/null 2>&1 || { echo "ERROR: git is required when --files is not supplied" >&2; exit 2; }
  git rev-parse --verify "${base}^{commit}" >/dev/null 2>&1 || {
    echo "ERROR: base ref '${base}' is unavailable; pass --base <sha/ref> or --files" >&2
    exit 2
  }
  mapfile -t files < <(git -C "${ROOT_DIR}" diff --name-only "${base}...HEAD")
fi

declare -A required=()
for file in "${files[@]}"; do
  [[ -n "${file}" ]] || continue
  matched=0
  rule_line=0
  while IFS=$'\t' read -r pattern tier reason extra; do
    ((rule_line += 1))
    (( rule_line == 1 )) && continue
    [[ -n "${pattern}${tier}${reason}${extra}" ]] || continue

    # Deliberately use Bash pattern matching, not pathname expansion. The rule
    # table therefore stays small and deterministic and does not depend on which
    # files happen to exist in the current checkout.
    if [[ "${file}" == ${pattern} ]]; then
      required["${tier}"]=1
      matched=1
    fi
  done < "${rules_file}"

  # Unmatched source/docs/config still require ordinary T1 proof. This is the
  # fail-safe default: adding a new path can never result in "no evidence".
  if (( matched == 0 )); then
    required[T1]=1
  fi
done

declare -a ordered=()
for tier in T1 T2 T3 T4; do
  [[ -n "${required[${tier}]:-}" ]] && ordered+=("${tier}")
done

if [[ "${format}" == "tsv" ]]; then
  printf '%s\n' "${ordered[@]}"
  exit 0
fi

printf '{"tiers":['
first=1
for tier in "${ordered[@]}"; do
  (( first == 1 )) || printf ','
  printf '"%s"' "${tier}"
  first=0
done
printf ']}\n'
