#!/usr/bin/env bash
# Classify a diff into repository evidence tiers plus an orthogonal security-boundary signal.
set -euo pipefail

readonly ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
readonly DEFAULT_RULES="${ROOT_DIR}/.github/evidence/evidence-rules.tsv"

usage() {
  cat <<'EOF'
Usage:
  bash .github/evidence/required-evidence.sh [--base REF] [--format tsv|json]
  bash .github/evidence/required-evidence.sh [--format tsv|json] --files PATH [PATH ...]
  bash .github/evidence/required-evidence.sh --security-boundary [--base REF | --files PATH ...]

Without --files, changed paths come from `git diff --name-only <base>...HEAD`.
The default base is `main`; CI callers should pass the exact pull-request base SHA.
Default TSV output remains one required T1-T4 tier per line. `--security-boundary`
prints only `security_boundary=changed|unchanged`. JSON includes both fields.
EOF
}

base="main"
format="tsv"
rules_file="${EVIDENCE_RULES_FILE:-${DEFAULT_RULES}}"
security_only=0
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
    --security-boundary)
      security_only=1
      shift
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

rule_line=0
while IFS=$'\t' read -r pattern tier security reason extra; do
  ((rule_line += 1))
  (( rule_line == 1 )) && {
    [[ "${pattern}" == "pattern" && "${tier}" == "tier" && "${security}" == "security" && "${reason}" == "reason" ]] || {
      echo "ERROR: malformed evidence-rules header" >&2
      exit 2
    }
    continue
  }
  [[ -z "${pattern}${tier}${security}${reason}${extra}" ]] && continue
  [[ -z "${extra:-}" ]] || { echo "ERROR: extra TSV field on rule line ${rule_line}" >&2; exit 2; }
  [[ -n "${pattern}" ]] || { echo "ERROR: empty pattern on rule line ${rule_line}" >&2; exit 2; }
  [[ "${tier}" =~ ^T[1-4]$ ]] || { echo "ERROR: invalid tier '${tier}' on rule line ${rule_line}" >&2; exit 2; }
  [[ "${security}" == "changed" || "${security}" == "unchanged" ]] \
    || { echo "ERROR: invalid security flag '${security}' on rule line ${rule_line}" >&2; exit 2; }
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
security_boundary="unchanged"
for file in "${files[@]}"; do
  [[ -n "${file}" ]] || continue
  matched=0
  rule_line=0
  while IFS=$'\t' read -r pattern tier security reason extra; do
    ((rule_line += 1))
    (( rule_line == 1 )) && continue
    [[ -n "${pattern}${tier}${security}${reason}${extra}" ]] || continue

    if [[ "${file}" == ${pattern} ]]; then
      required["${tier}"]=1
      [[ "${security}" == "changed" ]] && security_boundary="changed"
      matched=1
    fi
  done < "${rules_file}"

  if (( matched == 0 )); then
    required[T1]=1
  fi
done

declare -a ordered=()
for tier in T1 T2 T3 T4; do
  [[ -n "${required[${tier}]:-}" ]] && ordered+=("${tier}")
done

if (( security_only )); then
  printf 'security_boundary=%s\n' "${security_boundary}"
  exit 0
fi

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
printf '],"security_boundary":"%s"}\n' "${security_boundary}"
