# Operations runbook

*Started 31 October 2026 with M19. What the site needs from whoever runs it, and what to do when something is wrong. Everything here is a thing the tests cannot do for you.*

## The database

- **What it is.** One SQL Server database, `Selficient`, reached through `Database__ConnectionString`. It holds the catalogue (`offers`, `checks`, `clicks`, `runs`), and from M19 the Data Protection key ring and the cache. No table holds a person.
- **Schema.** The site migrates itself at start from the scripts embedded in the build (`src/Selficient.Infrastructure/Data/Migrations/NNNN_what-it-does.sql`), recording each in `dbo.schema_versions`. A script once shipped is never edited; the next change is the next number. If `schema_versions` on the vhost ever lists a name the build does not ship, someone edited history: restore the script, do not rename the row.
- **Start-up.** Migration runs before the site answers. A failed migration fails the start, the app pool shows stopped, and the deploy's "Verify site is healthy" step fails. Read the site log in `httpdocs\logs\selficient-<date>.log` for the SQL error; fix the script in a new migration; redeploy.
- **Backups.** A SQL Server Agent job: full nightly, log every fifteen minutes, to a folder the existing off-box copy picks up. Test a restore to a scratch database quarterly. A lost key ring signs the operator out and invalidates nothing else; a lost catalogue is re-seeded from the shipped picks on the next start but loses every check, click and reader's tip.
- **Re-seeding.** To start the catalogue again from the shipped picks: stop the app pool, `DELETE FROM dbo.clicks; DELETE FROM dbo.checks; DELETE FROM dbo.offers; DELETE FROM dbo.runs;`, start the app pool. The worker seeds on its first load.
- **Locally.** The default instance with integrated security; `Database__ConnectionString=Server=localhost;Database=Selficient;Integrated Security=true;TrustServerCertificate=true;Encrypt=true`. The tests make and drop a database each on the same server (`SELFICIENT_TEST_SQL` names another server if you want them elsewhere).

## Keys, cache, probes, telemetry, audit

- **The key ring.** `dbo.data_protection_keys` holds the Data Protection keys that sign the operator's cookie and the antiforgery tokens, wrapped by the machine's DPAPI on Windows. Losing the table signs the operator out and invalidates nothing else. Never copy it to another machine expecting it to work; it will not, by design.
- **The cache.** `dbo.cache` is the distributed half of `HybridCache` (geocodes, elevations, solar yields, share cards). It is safe to truncate at any time; the site refills it. Expired rows are swept on access every thirty minutes.
- **Probes.** `/health` answers `ok` while the process runs. `/health/ready` answers `ready` (200) when the database answers, or `not ready` (503) when it does not; a missing basemap or an unloaded catalogue is degraded, which still answers `ready`. The deploy workflow gates on `/health/ready`. The reviewer at `/admin/catalogue` shows each check's sentence.
- **Telemetry.** Set `OTEL_EXPORTER_OTLP_ENDPOINT` (and `OTEL_EXPORTER_OTLP_HEADERS` where the collector wants a key) on the runner and the site exports traces, metrics and logs over OTLP; unset, nothing leaves and the file log under `httpdocs\logs` is the record. Upstream spans carry the host only, never the path, so no postcode reaches a collector. Probes and tiles are not traced.
- **Audit.** `dbo.operator_actions` is every hand taken on the reviewer (sign-in, sign-out, hide, show, verify) with its outcome; `dbo.job_runs` is every wake of the worker with how it went. Both are on the reviewer, newest first. A night with no `wake` row means the worker did not run: check `Catalogue__WorkerEnabled` and the app pool's idle timeout (set it to 0 so the pool does not sleep).
- **The click counter.** Clicks are counted in memory and written to `dbo.clicks` once a second (and once more at stop); a crash loses at most that second. The reviewer's 30-day clicks are therefore up to a second behind the hops.

## Operators

Accounts live in `dbo.operators`. Each has a password, an authenticator and ten recovery codes; the reviewer at `/admin/catalogue` is not reachable without all three.

**The first account on a new server.** Set `Operator__BootstrapUsername` and `Operator__BootstrapPassword` (at least fourteen characters) on the vhost and start the site. The account is made once, with a warning in the log, and can do nothing but change its password and enrol an authenticator. Sign in, do both, then remove the two settings and recycle. They do nothing while an operator exists, so leaving them is untidy rather than dangerous, but remove them.

**A second operator.** There is no page for it yet. Insert the row with a hash made by the application, or add the account by running the site once with the bootstrap settings against an empty table. **Open for Rob:** an operators page on the reviewer is the obvious next piece of M20 and was left out deliberately rather than forgotten.

**A locked account.** Five wrong passwords lock it for a minute, and each failure after that doubles the wait to an hour. Waiting is the fix. To clear it by hand:

```sql
UPDATE dbo.operators SET failed_attempts = 0, locked_until = NULL WHERE username = N'<name>';
```

**A lost phone.** Sign in with one of the recovery codes, then enrol a new authenticator. With the codes gone too, clear the second factor so the next sign-in enrols again:

```sql
UPDATE dbo.operators SET totp_secret = NULL, totp_confirmed_at = NULL, recovery_codes = NULL, security_stamp = NEWID() WHERE username = N'<name>';
```

**Turning an account off.** `UPDATE dbo.operators SET is_enabled = 0, security_stamp = NEWID() WHERE username = N'<name>';`. Moving the stamp is what ends the sessions it already has; without it they live until they expire.

**Who did what.** `dbo.operator_actions` holds every hand with the operator's name; `dbo.operator_signin_attempts` holds every attempt with its outcome. Both are on the reviewer, newest first. Neither holds an address or a user agent.

**After restoring a database to another machine.** The authenticator seeds and the recovery codes are encrypted with the Data Protection key ring, which is itself in `dbo.data_protection_keys` and wrapped by the machine on Windows. A database restored without that machine's DPAPI cannot read them: every operator will need their second factor cleared as above.
