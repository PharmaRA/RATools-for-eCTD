# Production Rollback Runbook

This runbook applies only to the supported single-host, local-only production
topology. A rollback is an operational recovery event: preserve the failed state and
record every image ID, source revision, backup name, command, and verification result.

## Before Every Upgrade

1. Confirm `/health/ready` is healthy and record `/version`.
2. Preserve the exact current images under rollback tags:

   ```powershell
   docker tag ratools:local ratools:pre-upgrade
   docker tag ratools-migrator:local ratools-migrator:pre-upgrade
   docker image inspect ratools:pre-upgrade ratools-migrator:pre-upgrade > .artifacts/pre-upgrade-images.json
   docker save --output .artifacts/ratools-pre-upgrade-images.tar ratools:pre-upgrade ratools-migrator:pre-upgrade
   Get-FileHash -Algorithm SHA256 .artifacts/ratools-pre-upgrade-images.tar > .artifacts/ratools-pre-upgrade-images.sha256.txt
   ```

3. Create and validate a named backup, then copy the whole backup directory to an
   encrypted location outside the Docker host:

   ```powershell
   python scripts/backup_production.py --backup-name pre-upgrade
   python scripts/restore_production_backup.py deploy/production/runtime/backups/pre-upgrade
   ```

4. Copy the image archive, its hash, image inspection JSON, prior release `VERSION`,
   `CHANGELOG.md`, source revision, SBOMs, and `SHA256SUMS` beside the encrypted
   off-host backup. Do not deploy if the restore drill or any checksum check fails.

## Application-Only Rollback

Use this path only when the deployed migration set is unchanged or the prior release
is explicitly proven compatible with the current schema. EF migrations are forward
only; an older image reporting no pending migrations does not prove backward schema
compatibility.

```powershell
docker tag ratools:pre-upgrade ratools:local
docker tag ratools-migrator:pre-upgrade ratools-migrator:local
docker compose -f compose.production.yml up --detach --no-build --force-recreate migration api
docker compose -f compose.production.yml ps
```

Verify `/version`, `/health/ready`, application listing, publish history, one known
document download, and one known published package. Record the results before
declaring recovery complete.

## Stateful Rollback

Use this path after an incompatible schema migration, suspected data corruption, or
any file/database consistency failure. The automated restore drill never applies a
backup in place. Do not delete, empty, or reuse the failed production volumes.

1. Stop the failed stack without `--volumes`, preserve its logs, and prevent further
   operator access.
2. On a separate controlled recovery host, check out the recorded prior revision and
   verify `VERSION`, SBOM checksums, and image OCI labels against the retained release
   evidence.
3. Verify the retained image archive and release evidence before loading anything.
4. Load the prior images and initialize new secrets. The recovery host gets a new
   database password, API key, and Caddy CA; no secrets are restored from the data
   backup. Create the fresh Compose resources, start only `proxy` and `postgres`, and
   wait for PostgreSQL health:

   ```powershell
   Get-FileHash -Algorithm SHA256 <release-evidence>/ratools-pre-upgrade-images.tar
   docker load --input <release-evidence>/ratools-pre-upgrade-images.tar
   docker tag ratools:pre-upgrade ratools:local
   docker tag ratools-migrator:pre-upgrade ratools-migrator:local
   python scripts/initialize_production.py
   docker compose -f compose.production.yml create api
   docker compose -f compose.production.yml up --detach proxy postgres
   docker compose -f compose.production.yml ps
   ```

5. Copy and restore the database dump into the empty recovery database:

   ```powershell
   docker compose -f compose.production.yml cp <backup>/database.dump postgres:/tmp/ratools-restore.dump
   docker compose -f compose.production.yml exec -T postgres pg_restore --exit-on-error --no-owner --no-privileges --username=ratools --dbname=ratools /tmp/ratools-restore.dump
   ```

6. Restore the file archive into the fresh named volumes. `<backup-absolute-path>`
   must be an absolute path accepted by Docker Desktop:

   ```powershell
   docker run --rm --network none --read-only --user 1654:1654 --cap-drop ALL --security-opt no-new-privileges:true --volume ratools-production_ratools_app_data:/restore/app-data --volume ratools-production_ratools_workspaces:/restore/workspaces --volume <backup-absolute-path>:/backup:ro alpine:3.22.5 tar -xzf /backup/files.tar.gz -C /restore
   ```

7. Start the complete prior topology. Its migration job must be a successful no-op:

   ```powershell
   docker compose -f compose.production.yml up --detach --no-build
   docker compose -f compose.production.yml ps
   ```

8. Re-run the same health, version, database-count, file-inventory, download, and
   publish-artifact checks used by the restore drill. Trust the new Caddy CA only after
   confirming the recovered host. Keep the failed host and volumes quarantined until
   the incident owner approves disposal.

If any recovery verification fails, stop. Preserve both environments and escalate
with the backup manifest, command output, container logs, release evidence, and the
exact failed check.
