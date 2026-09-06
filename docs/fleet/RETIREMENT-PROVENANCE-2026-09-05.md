# Retirement Provenance — 2026-09-05

## Reconciled execution evidence

All **65 retired refs** now have an exact deletion-time head and timestamp from
the operator's original execution ledgers: **40 in wave 1 and 25 in wave 2**.
The coding agent supplied both files verbatim in
[#193 comment 5555320434](https://github.com/geromet/TornHappyGymStats/issues/193#issuecomment-5555320434).
Their TSV blocks are preserved unchanged here:

- [retire-branches-20260905.ledger.tsv](retire-branches-20260905.ledger.tsv)
- [retire-branches-20260905-wave2.ledger.tsv](retire-branches-20260905-wave2.ledger.tsv)

These were originally generated under the operator's gitignored `workspace/`.
A clean clone now contains the records; no machine-local file upload is needed.
They are historical evidence, not current branch authority or executable work
instructions. Read #140 and enumerate origin for current ownership and existence.

## Reconciliation with the earlier reconstruction

[RETIRED-BRANCH-HEADS-2026-09-05.tsv](RETIRED-BRANCH-HEADS-2026-09-05.tsv)
retains the prior historical observations and adds separate execution fields.

| Evidence | Reconciliation result |
| --- | --- |
| 63 GitHub PR `head.ref` / `head.sha` mappings | All match the supplied execution heads |
| 1 main-equality observation | Matches the supplied execution head |
| 1 previously unknown head | Resolved by the wave-1 execution ledger |

The previously unresolved `fix/security-local-return-urls` was deleted at
`2026-09-05T17:40:26Z` with head
`150f9aeb65dedcc556bdeb6ee9b1eaa48e538fba`. This value comes from its own operator
record, not replacement PR #144. Its original `historical_head_sha=UNKNOWN` and
note remain as a record of what the earlier reconstruction could establish;
`deletion_head_sha` now provides the resolved value.

The existing `historical_head_sha`, `evidence_kind`, `evidence_url` and `note`
columns retain their original values. Added columns carry `deleted_at`,
`deletion_head_sha`, `execution_evidence_url`, `execution_ledger` and
`reconciliation`. All `deletion_head_verified=yes` flags mean **reconciled against
the supplied operator execution record**. They do not claim this auditor witnessed
the deletion or independently replayed its compare-and-swap operation.

The earlier reconstruction used #193's named deletion sets, the latest-numbered
GitHub PR with an exactly matching `head.ref`, and
[the main-equality observation](https://github.com/geromet/TornHappyGymStats/issues/140#issuecomment-5554569979).
PR heads alone were insufficient deletion-time evidence. The supplied original
ledgers close that evidence gap without rewriting earlier observations.

## Validation and limits

Reconciliation checked both original TSV blocks byte-for-byte against the supplied
GitHub comment, 65 unique branch names, disjoint 40/25 wave membership, exact
coverage of the earlier ledger, full 40-character hexadecimal SHAs, and each
recorded restore command's agreement with its own SHA and branch. All 64 previously
known heads agree; the remaining head is resolved; no discrepancies remain.

Every one of the 65 SHAs also resolved through GitHub's Git commit API
(`GET /repos/geromet/TornHappyGymStats/git/commits/{sha}`) to the same SHA and a
commit tree during this reconciliation. This is a point-in-time object-availability
check, not a permanent object backup guarantee. A future restoration may require
fetching the object first. A reviewed Git bundle would be a separate backup task.

MD-012 in [#223](https://github.com/geromet/TornHappyGymStats/issues/223) has its
missing execution evidence reconciled. #221 carries the clean-clone copies and
MD-011's corrected current-authority guidance. Default incorporation and final
acceptance of #221 remain with Gerome's coding-agent/human workflow; the broader
MD-001..MD-015 tracker is not completed by this retirement repair.

## Restoration boundary

The original TSVs preserve operator-generated `git push` restore commands as
data. **Do not execute those commands as part of this audit or documentation
repair.** Neither this ledger nor the earlier deletion approval authorizes
restoring refs. Any later restoration requires explicit scope and fresh #140
ownership, ref-existence and local-worktree checks. No refs were restored or
deleted by this reconciliation; fleet/manual agents never merge to default.
