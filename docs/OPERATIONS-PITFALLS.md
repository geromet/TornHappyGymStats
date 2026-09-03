# Operational pitfalls

Every entry here cost real time on a real evening. Each one names the symptom
you will actually see first — which is usually *not* the cause — and what to do.

Read with `bash scripts/menu.sh --pitfalls`.

The rule that generated most of this list: **the error message points at the
last thing that failed, not the thing that broke.** A missing `rsync`, a
"missing" Postgres role, a 404 on a cached file and a crash-looping frontend
were all misdirection.

---

## 1. The sudo password echoed on screen

**Symptom.** Characters appear as you type the sudo password over SSH.

**Cause.** The remote command was not given a controlling terminal, so `ssh`
never put the tty into no-echo mode.

**Rule.** Never pipe an interactive `ssh`, and always hand it `/dev/tty`.
`scripts/lib/remote-exec.sh` does this correctly — use it rather than writing a
new `ssh ... 'bash -s'`.

**If you see it again:** stop, do not type a real password. It is a regression,
not a quirk. `bash scripts/verify/remote-heredoc-lint.sh` asserts the
interactive ssh is neither piped nor deprived of a tty.

---

## 2. `$1: unbound variable` from a script you can read and see is fine

**Symptom.** A remote helper script dies on `$1`, or a backtick runs on the
wrong machine.

**Cause.** A quoted heredoc (`<<'INNER'`) nested inside an unquoted one
(`<<REMOTE`) offers no protection: the outer heredoc is parsed first, so the
*local* shell expands the inner text before it is ever written out.

**Rule.** Inside an unquoted remote heredoc, escape every `$`, `` ` `` and
`$( )` that must survive to the server.

**Guard.** `bash scripts/verify/remote-heredoc-lint.sh` — offline, no host
needed. Run it before any edit to the remote-exec scripts.

---

## 3. `install` truncates the file you just edited

**Symptom.** An env file you filled in is empty, and the service reports a
missing secret.

**Cause.** `sudo install -m 0640 /dev/null /etc/…/blazor.env` **creates and
truncates**. Running it after editing wipes the file.

**Rule.** `install` first to create with the right mode and owner, edit second.
Then verify rather than assume:

```bash
stat -c '%a %U:%G %n' /etc/happygymstats/blazor.env
sudo -u www-data head -c 40 /etc/happygymstats/blazor.env
```

Expect `640 root:www-data`, and content. A `nano`-created file is `root:root`
and unreadable by the service.

---

## 4. A misspelled env key takes the frontend down

**Symptom.** `happygymstats-blazor` crash-loops with
`code=dumped, signal=ABRT`; `journalctl -n 50 --no-pager` shows an unhandled
`InvalidOperationException` about a missing client secret.

**Cause, the real one.** `Keycloack__ClientSecret` — a `c` in Keycloak. The
binder never saw the key, so the secret read as empty.

**Two rules.**

- Check the key reached the process, not just that the file looks right:
  ```bash
  sudo awk -F= '/^Keycloak__ClientSecret=/ {print "chars:", length($2)}' /etc/happygymstats/blazor.env
  ```
  No output means the key name is wrong. `systemctl show -p Environment <unit>`
  does **not** show `EnvironmentFile` contents — it will mislead you here.
- `EnvironmentFile=` without a leading `-` makes a missing file a unit **start
  failure**. Create the file before installing the unit.

**Since fixed:** the host now logs `Critical` and keeps serving anonymous pages
instead of crash-looping. Grep for it after any change to these files:

```bash
sudo journalctl -u happygymstats-blazor -b | grep -i RequireClientSecret
```

---

## 5. "rsync is not installed" on a host where rsync is installed

**Symptom.** `DEPLOY_PRECHECK_FAIL category=missing_remote_command detail=command=rsync`.

**Cause.** The precheck suppressed ssh's output to stay quiet, which also
suppressed its key-passphrase prompt. The connection failed; the script reported
the only thing it could imagine.

**Rule.** Load the key once per session so the many short-lived ssh calls do not
each need a passphrase:

```bash
ssh-add ~/.ssh/id_token2_bio3_hetzner
```

**Since fixed:** the precheck distinguishes the two, and an unreachable host now
reports `remote_probe_unreachable`.

---

## 6. A dev deploy wrote into the production root

**Symptom.** `bash scripts/deploy-dev.sh --target backend` printed
`==> Skipping write precheck for /var/www/happygymstats` — the *production*
path.

**Cause.** `.env.deploy` uses plain assignments and `deploy-config.sh` sources
it **after** the caller's environment is set, so the file silently overwrote the
dev target `deploy-dev.sh` had exported. The existing guardrail checked the
variables it set, not the ones the child used.

**Rule.** Read the precheck line. It names the root that will actually be
written.

**Since fixed, twice over:** exported overrides now survive `.env.deploy`, and
`deploy-dev.sh` exports `DEPLOY_FORBID_PRODUCTION_TARGET=1`, which makes both
child scripts refuse a production target outright.

---

## 7. `status=203/EXEC` after an interrupted deploy

**Symptom.** A unit will not start; the journal says only `203/EXEC`.

**Cause.** Release activation is one long ssh command: rsync, `chmod 644` over
every file, then `chmod 755` back onto the executable. A connection dropping
between the last two leaves the binary non-executable — likeliest on a slow
link, which is exactly when deploys get interrupted.

**Check.** `sudo ls -l /var/www/<root>/current/HappyGymStats.*`

**Since fixed:** the 644 sweep skips the executable, so no interruption can
produce an unstartable release. Prefer re-running the deploy over `chmod`ing by
hand, so `current` points at a complete release.

---

## 8. Migrations apply themselves on restart

**Not a bug — a property to plan around.** The API runs EF migrations at
startup. Consequences:

- A dev build pointed at the production database would migrate the **production**
  schema. This is why the dev host needs its own database and role, and why
  `deploy-dev.sh` refuses a production target.
- A migration goes live whenever the API next restarts — including an unattended
  reboot, not only a deliberate deploy. If `current` points at an unreviewed
  build, that build goes live on the next restart.

**Before deploying a migration:** read it. `AddChainLapseDeadline` is the model
to copy — additive and nullable, so existing rows keep working and a rollback is
just the previous release.

---

## 9. Postgres: the superuser is not `postgres`

**Symptom.** `pg_dumpall` fails, aborting a container upgrade before it starts.

**Cause.** The cluster is created with `POSTGRES_USER: happygym`. The postgres
image creates **that** role and no role called `postgres`.

**Rule.** Ask the container rather than assuming:

```bash
sudo docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' <pg> | grep POSTGRES_USER
```

`upgrade-containers.sh` now detects this and prints which superuser it will use,
with `DEPLOY_PG_SUPERUSER` as an override.

---

## 10. Keycloak: three things that look configured but are not

- **Group membership emits nothing by default.** Keycloak ships no
  group-membership mapper in the default client scopes; `microprofile-jwt`'s
  mapper *named* `groups` emits realm roles, not group paths. Add a **Group
  Membership** mapper per web client, **Full group path ON** (the code compares
  against the literal `/admins`), and **Add to userinfo ON** — the Blazor host
  sets `GetClaimsFromUserInfoEndpoint`.
- **The audience is not automatic.** The API pins `Keycloak:Audience`. Without an
  **Audience** mapper on the web client, sign-in succeeds and every API call
  returns 401.
- **PAR fails at the challenge, not the callback.** The .NET OIDC handler uses
  pushed authorization requests, and with a confidential client that call itself
  needs the secret. A missing secret therefore fails *before* the login page
  appears, as `401 invalid_request / "Authentication failed."` — nothing like
  what you would expect a bad secret to look like.

**Verify without a round trip:** the client's **Client scopes → Evaluate** tab
shows the generated access token. Look for `"groups": ["/admins"]` and the right
`aud`.

---

## 11. `www-authenticate: Bearer` from `server: cloudflare`

**Symptom.** A 401 with an empty body from a host you just set up.

**Cause.** Cloudflare Access rejected the request at the edge. It never reached
the origin, so no server-side log will mention it.

**Tell them apart:** the app's admin gate returns **403** with a text body
(`This deployment is restricted to administrators.`) and challenges anonymous
visitors with a redirect. It never emits 401.

---

## 12. A test tier that has never run proves nothing

**Symptom.** A green suite that includes tests which silently skip.

**Cause.** The Postgres integration tier skipped from the day it was written.
When it finally ran it had three defects: a readiness `TimeoutException` that
bypassed the skip mechanism, a configuration override that never applied because
minimal hosting reads config before `WebApplicationFactory`'s callbacks, and
assertions describing a payload the endpoint has never produced.

**Rule.** Check `Skipped: 0`, not just `Failed: 0`. Those tests need a container
runtime on the machine running `dotnet test`.

---

## 13. Publishing a disclosure is a gate, not paperwork

`docs/torn-api/terms-of-service.md` said *"API key is not stored and not shared"*
while a key vault was being built. Storing a key against that published text
breaches Torn's API terms, and the exposure is the faction's.

`scripts/verify/w07-key-vault-contract.sh` binds this: while the document is a
draft it reports the gate as unmet, and it becomes a hard failure the moment any
source both names `StoredApiKey` and calls `SaveChanges`.

---

## Reading an error category

`deploy-*` and the maintenance scripts print a machine-readable category.

| Category | Means |
|---|---|
| `survey_unreachable` | Never reached the host. **Not** "nothing to do". Usually `cloudflared access login https://ssh.geromet.com`. |
| `remote_probe_unreachable` | ssh answered nothing. Usually an unloaded key — `ssh-add`. |
| `docker_not_queryable` | Reached the host, could not run docker. An empty container list here means "could not look", not "nothing there". |
| `sudo_auth_failed` | Password refused. Nothing ran. |
| `preflight_failed` | Something would not survive a reboot. Fix the `!!` lines first. |
| `production_target_refused` | A dev deploy resolved to a production root or unit. Nothing was uploaded. |
| `missing_remote_command` | The command really is absent on the server. |
