# Retirement Provenance — 2026-09-05

## What is durably recoverable

[RETIRED-BRANCH-HEADS-2026-09-05.tsv](RETIRED-BRANCH-HEADS-2026-09-05.tsv)
records all **65 named refs** in #193's two deletion sets: 40 in wave 1 and 25
in wave 2. Each row records a full historical SHA when a branch-bound source
supports it, the evidence type, its GitHub URL and the remaining verification limit.

Reconstruction sources:

- [#193](https://github.com/geromet/TornHappyGymStats/issues/193): checked wave-1
  candidates (excluding the unchecked preserve) and explicit wave-2 deleted set.
- GitHub REST pull-request history (`state=all`, all pages): the latest-numbered
  PR whose `head.ref` exactly matches the branch. The ledger preserves its full
  `head.sha`; a squash `merge_commit_sha` is deliberately **not substituted**.
- [The audit owner's main-equality record](https://github.com/geromet/TornHappyGymStats/issues/140#issuecomment-5554569979)
  for `audit/docs-markdown-accuracy-20260905-2`, which had no PR or unique work.

Result: **63 PR-head mappings**, **1 explicitly recorded main-equality mapping**,
and **1 unresolved head** (`fix/security-local-return-urls`). Its replacement
#144 is evidence of supersession, not proof of the old branch's exact head.
No SHA has been guessed from a similar commit message or replacement tree.

## What this does not certify

The TSV is a historical-head evidence ledger, **not an exact restore ledger**.
All rows currently say `deletion_head_verified=no`. A branch can move after its
PR's last observed head; PR metadata alone cannot prove the deletion-time ref.
The original operator execution ledgers are required to certify those values:

- `workspace/retire-branches-20260905.ledger.tsv`
- `workspace/retire-branches-20260905-wave2.ledger.tsv`

Those machine-local files were named in #193 but were not present in this repair
environment or its Git history. They are missing evidence, not required project
setup. The branch-freeze statements in the execution report support the operator's
account but do not replace the missing per-ref execution rows.

MD-012 in [#223](https://github.com/geromet/TornHappyGymStats/issues/223) therefore
remains **PARTIAL**, not complete. MD-011's obsolete current review instructions
are removed by the companion guidance repair. Default incorporation of that repair
still belongs to the coding-agent/human review workflow.

## Closing the remaining evidence gap

1. Supply the two original TSV ledgers, preserving branch and full pre-deletion
   SHA fields. Review for unrelated machine/private data before committing.
2. Reconcile every named branch against this 40/25 set. Preserve discrepancies
   explicitly; never overwrite a PR observation and pretend it was the execution
   value. Add separate execution-head/source fields if values differ.
3. Recover the unresolved head from the original ledger (or another independently
   branch-bound observation). Do not substitute #144's merge or head SHA.
4. Verify each supplied SHA is a commit; record any unavailable object. Historical
   metadata is not a permanent backup guarantee. If a durable object backup is
   required, preserve a reviewed bundle/artifact rather than reopening 65 branches.
5. Only mark deletion-time verification complete when all rows are reconciled to
   the original execution evidence. Until then #193 must not claim the provenance
   acceptance criterion complete.

No restore commands are generated: neither this ledger nor the original deletion
approval authorizes restoring refs. Any later restoration needs fresh ownership,
ref-existence and worktree checks under #140 and explicit scope.

## Validation performed for this reconstruction

The 40/25 sets are disjoint and contain 65 unique full branch names. Each recovered
SHA is 40 hexadecimal characters and each PR source maps exactly to the recorded
branch. The original historical inventory rows are preserved; only their active
authority framing is corrected. The live companion now describes read-only
enumeration instead of pinning a branch count that immediately becomes stale.
