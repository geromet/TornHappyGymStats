# V2 Implementation Plan

## Architecture Summary

### Security model
- **Pseudonymization**: All DB tables use `AnonymousId` (Guid) instead of `PlayerId` (int TornID). No TornID persists in any data table.
- **IdentityMap**: Separate table `(AnonymousId, KeycloakSub, EncryptedTornPlayerId)` — access-controlled, admin role cannot query it.
- **PII encryption**: `TornPlayerId`, `FactionId`, `CompanyId` fields encrypted client-side with user's public key before storage. Stored as ciphertext blobs.
- **Numeric stats**: Pseudonymized plaintext — server reconstructs normally, no PII needed.
- **No DB-level encryption**: Redundant given pseudonymization + column-level encryption + access control + TLS in transit.

### Auth (Keycloak)
- Groups: `/users`, `/users/faction-owners` (inherits), `/admins` (separate, no decryption keys)
- JWT claims: `anonymous_id`, `faction_anonymous_ids`, `public_key`
- Hosted at `auth.geromet.com`, same Hetzner VPS, Docker container

### Anonymous log submission flow
1. User submits Torn API key unauthenticated
2. Server stores logs under a provisional `AnonymousId`, returns a signed provisional JWT (24h TTL)
3. App stores provisional JWT in `localStorage`
4. On account creation, app sends Keycloak JWT + provisional JWT to `POST /identity/claim-provisional`
5. Server links `KeycloakSub → provisionalAnonymousId` in IdentityMap, clears provisional record

### Faction ownership
- Stored as `(FactionAnonymousId, MemberAnonymousId)` — no TornIDs
- Verification: **stub** (always returns false/unauthorized) until faction log samples are available to design proper verification
- Interface: `IFactionOwnershipVerifier` — swappable when real verification is implemented

### Admin panel
- Separate ASP.NET app (`HappyGymStats.AdminPanel`), separate port/process, no shared DI with main API
- Read-only views over pseudonymized data — sees UUIDs and encrypted blobs, no decryption capability

---

## New Projects / Services

| Project | Purpose |
|---|---|
| `HappyGymStats.Identity` | Keycloak client, AnonymousId issuance, keypair management, provisional token flow |
| `HappyGymStats.Encryption` | Crypto primitives: ECDH, AES-GCM, PBKDF2 |
| `HappyGymStats.AdminPanel` | Separate Blazor/ASP.NET admin app |
| Keycloak (Docker) | OIDC/OAuth2 provider |
| PostgreSQL (Docker) | Replaces SQLite |

---

## Build Order

### Phase 0 — Infrastructure ✅ / 🔲

- [x] Docker installed on server (Docker 29.4.2)
- [x] `happygym-containers` Linux user created, added to docker group
- [x] `.env` created at `/opt/happygymstats/containers/.env` (chmod 600, owned by happygym-containers)
- [x] `docker-compose.yml` + `postgres-init/01-keycloak-db.sh` deployed to `/opt/happygymstats/containers/`
- [ ] `docker compose up -d` — start Postgres + Keycloak containers
- [ ] Verify containers healthy (`docker compose ps`)
- [ ] Copy `infra/nginx-auth.conf` to `/etc/nginx/sites-enabled/` on server
- [ ] `sudo nginx -t && sudo systemctl reload nginx`
- [ ] Add `auth.geromet.com` DNS record in Cloudflare pointing to VPS
- [ ] Verify Cloudflare Origin cert covers `auth.geromet.com` (wildcard `*.geromet.com` or new cert)
- [ ] Verify Keycloak reachable at `https://auth.geromet.com`
- [ ] Keycloak initial setup: create realm, groups (`/users`, `/users/faction-owners`, `/admins`), client for API

### Phase 1 — DB Migration

- [ ] Add Npgsql EF Core provider to `HappyGymStats.Data`
- [ ] Replace `int PlayerId` with `Guid AnonymousId` in all entities:
  - `UserLogEntryEntity`
  - `AffiliationEventEntity`
  - `DerivedGymTrainEntity` (via LogId FK chain)
  - All repositories and reconstruction runner signatures
- [ ] New migration: drop SQLite, target PostgreSQL
- [ ] Update `appsettings.json` / `AppConfiguration` to use Postgres connection string
- [ ] Smoke test: reconstruction runs end-to-end against Postgres

### Phase 2 — Identity Project

- [ ] Create `HappyGymStats.Identity` project
- [ ] Keycloak admin client (realm management, user registration hooks)
- [ ] `AnonymousId` issuance on account creation → stored as Keycloak user attribute
- [ ] `IdentityMap` table + repository (separate access policy, not queryable by admin role)
- [ ] Provisional token flow:
  - `POST /import/anonymous` → issues signed provisional JWT, stores data under provisional AnonymousId
  - `POST /identity/claim-provisional` → links Keycloak sub to provisional AnonymousId
- [ ] User keypair generation (in-browser ECDH), public key stored on server, private key as encrypted blob

### Phase 3 — API Auth Middleware

- [ ] Add Keycloak JWT validation middleware to `HappyGymStats.Api`
- [ ] Keycloak group → role mapping: `user`, `faction_owner`, `admin`
- [ ] `IdentityMap` access boundary: admin role returns 403 on any IdentityMap query
- [ ] Auth-gated endpoints: user data endpoints require valid JWT + matching AnonymousId claim

### Phase 4 — Encrypted PII Fields

- [ ] Create `HappyGymStats.Encryption` project (AES-GCM, ECDH, PBKDF2 helpers)
- [ ] Add `EncryptedTornPlayerId` column to `IdentityMap` (ciphertext, encrypted with user's public key)
- [ ] Add `EncryptedAffiliationId` column to `AffiliationEventEntity`
- [ ] Client-side encryption flow in Blazor: decrypt private key blob → decrypt PII fields on fetch

### Phase 5 — Faction Features

- [ ] `FactionMembership` table: `(FactionAnonymousId, MemberAnonymousId)`
- [ ] `IFactionOwnershipVerifier` interface + `StubFactionOwnershipVerifier` (always false)
- [ ] Faction owner DTO endpoint: returns member stat rows (pseudonymized plaintext) if ownership verified
- [ ] Frontend: faction member list with hotlinks to individual gym train point clouds
- [ ] Frontend: "no data" screen with registration link for members without accounts

### Phase 6 — Admin Panel

- [ ] Create `HappyGymStats.AdminPanel` project (separate ASP.NET app)
- [ ] Admin-only JWT validation (rejects non-admin tokens)
- [ ] Read-only aggregate views: query data tables by AnonymousId, display stats without identity
- [ ] No IdentityMap access — admin literally cannot resolve AnonymousId → player

---

## Server Layout

```
/opt/happygymstats/
  containers/
    docker-compose.yml
    .env                  (chmod 600, owned by happygym-containers)
    postgres-init/
      01-keycloak-db.sh
```

Docker containers (127.0.0.1 only):
- Postgres: `127.0.0.1:5432`
- Keycloak: `127.0.0.1:8080` → proxied by nginx as `auth.geromet.com`

Existing systemd services (unchanged for now):
- `happygymstats-api.service` → `127.0.0.1:5047`
- `happygymstats-blazor.service` → `127.0.0.1:5182`

---

## Open Items

- Keycloak version pinned at `26.0` in docker-compose — check for newer stable release before going live
- `deploy-containers.sh` needs update to run `docker compose` as `happygym-containers` (via `sudo -u`)
- Cloudflare Origin cert: confirm `*.geromet.com` wildcard covers `auth.geromet.com`
