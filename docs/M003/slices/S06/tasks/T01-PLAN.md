---
estimated_steps: 8
estimated_files: 2
skills_used: []
---

# T01: Move container deploy onto shared config

Why: Container deploy currently hardcodes SSH details while app deploys use shared config. This creates drift and makes future server changes error-prone.

Do:
1. Refactor `scripts/deploy-containers.sh` to source `scripts/deploy-config.sh` or an intentionally shared subset.
2. Preserve existing remote paths and behavior unless corrected by documented variables.
3. Replace duplicated SSH command construction with shared helpers.
4. Add `--help` output that lists required remote env preconditions without asking the user to paste secrets.
5. Keep script non-interactive for deployment; secret collection remains outside scripts.

Done when: deploy scripts use a common SSH/config convention and container deploy no longer has hardcoded duplicate SSH settings.

## Inputs

- `scripts/deploy-containers.sh`
- `scripts/deploy-config.sh`
- `infra/docker-compose.yml`

## Expected Output

- `scripts/deploy-containers.sh`
- `scripts/deploy-config.sh`

## Verification

bash -n scripts/deploy-containers.sh && bash scripts/deploy-containers.sh --help && ! rg -n "id_token2_bio3_hetzner|cloudflared access ssh|anon@ssh\.geromet\.com" scripts/deploy-containers.sh

## Observability Impact

Signals added/changed: shared config output/help for container deploy.
How a future agent inspects this: `bash scripts/deploy-containers.sh --help`.
Failure state exposed: missing env/SSH config in a consistent format.
