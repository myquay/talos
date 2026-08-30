# Talos operations

## Container publishing

The container workflow builds from this repository without build-time secrets and
publishes `ghcr.io/myquay/talos:sha-<full-commit-sha>`. The production deployment
uses that immutable tag. Configure the `myquay/talos` GHCR package as public once
in GitHub package settings so the droplet can pull it without registry credentials.

Before changing build inputs, confirm `.dockerignore` still excludes environment
files, credentials, private keys, local databases, and generated output. Never pass
secrets through Docker `ARG`, `ENV`, or `RUN`; all production secrets are runtime
environment variables populated by infrastructure from Azure Key Vault.

## Stale SQLite migration lock recovery

Startup migrations have a bounded timeout and intentionally do not delete locks.
If startup repeatedly reports a migration lock timeout:

1. Stop the Talos container so there is exactly one potential database writer.
2. Copy `/app/data/talos.db` to a timestamped backup outside the container.
3. Run `PRAGMA quick_check;` and stop if it does not return `ok`.
4. Inspect `__EFMigrationsLock` and application logs. Delete its row only after
   confirming no Talos process is running and the lock belongs to a terminated
   migration.
5. Start Talos, wait for its health check, then verify metadata, authorization,
   and token endpoints through the public hostname.

Do not automate lock deletion: removing a live migration lock can allow concurrent
schema changes and corrupt the database.
