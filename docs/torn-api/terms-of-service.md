# Torn API Key Terms Disclosure

**Document version: 2.0.0 · published 2026-09-04**

Served at `/terms`. The version above, the page, and
`HappyGymStats.Contracts.Compliance.TermsDocument.Version` must agree;
`scripts/verify/w07-key-vault-contract.sh` fails the build if they drift.

Source policy: https://www.torn.com/api.html#

This service uses Torn API keys in three distinct ways. They are listed separately because
they differ in access level, in whether the key is stored, and in what is done with the
data — and Torn's disclosure requirements apply to each.

## Status

This version replaces 1.0.0, which described a single usage and stated *"API key
is not stored and not shared"*. That statement was accurate for usage 1 and only
for usage 1.

Usages 2 and 3 below describe **stored** keys. The disclosure is published
first, deliberately: no key row may be written until it is live and the member
storing a key has accepted this version. `w07-key-vault-contract.sh` enforces
that ordering — it fails the build if any code persists a stored key while this
document is still a draft.

## 1. One-off import key

| | |
|---|---|
| Access level requested | Full Access |
| Key storage | **Not stored.** Sent over HTTPS for the import run and held in memory only for its duration. |
| Key sharing | Not shared. |
| Data storage | Persistent — imported and derived data is retained until removed on request. |
| Data sharing | Aggregated and anonymised data is shown publicly. |
| Purpose | Non-malicious statistical analysis: gym-gain reconstruction from your own logs. |

## 2. War poller key

| | |
|---|---|
| Access level requested | Limited |
| Key storage | **Stored, encrypted.** AES-256-GCM under a master key held only in the server environment, never in the repository and never in configuration files. |
| Key sharing | Not shared. Not readable through any endpoint, by any role, including administrators. |
| Data storage | War state (scores, chains, membership) retained for war history. |
| Data sharing | Visible to members of the faction the war belongs to. |
| Purpose | Polling public faction and war endpoints during a ranked war, unattended, so the war board stays live. |

## 3. Member key

| | |
|---|---|
| Access level requested | **Limited only.** A Full Access key is refused, not silently accepted. |
| Key storage | **Stored, encrypted**, as in usage 2. Decrypted only for the duration of the call that uses it. |
| Key sharing | Not shared. Not readable through any endpoint, by any role, including administrators. |
| Data storage | Your bars, cooldowns, and attack records, retained while your key is linked. |
| Data sharing | Derived war figures are visible to your faction. Raw personal readings are not. |
| Purpose | Turning guesses into facts on the war board: real energy instead of inferred hole severity, real cooldowns, and attacks made against you. |

## Commitments that apply to every stored key

- **Revocation is immediate and complete.** Unlinking a key removes it from service at once
  and **deletes the identifiable personal readings taken with it**. It is not a flag that
  hides the row.
- **A key is never readable back.** You can see *that* a key is linked and revoke it. Nobody
  — including whoever runs the server — can read it through the application.
- **A key never appears in a log, an error message, an API response, or a command line.**
- **A key rejected by Torn is not retried.** It is marked invalid and its owner is told.
- **Least access.** Each usage above asks for the lowest access level that works. Where a
  Limited key is enough, a Full key is refused.

## Consent

Storing a key under usages 2 or 3 requires your active, recorded acceptance of this
document — a deliberate action, not implied by using the site. The record stores which
version you accepted, so a later material change can identify who needs asking again.

If you do not accept, usage 1 remains available and nothing about your account changes.

---

### Changelog

- **2.0.0** (2026-09-04) — Splits the single disclosure into the three distinct
  key usages; states plainly that war and member keys are stored encrypted; adds
  the revocation, non-readability and least-access commitments; adds versioned
  consent. Published before any key-storing code exists, per M009 S01
  (`workspace/V2/handoff/07`).
- **1.0.0** — Single usage: Full Access import key, not stored.
