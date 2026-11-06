# Finalization input binding

QA-01 closes the ability to reuse a completed Development run for changed delivery output at a newer workspace revision. Finalization v2 compares the complete delivery inventory with the primary input archived by that run. An independently registered input need not originate in the current workspace.

The existing v2 input manifest is the delivery structure identity. Finalization reconstructs its canonical digest, verifies capture/input identity (including ZIP archive SHA-256), application, sequence, source kind and resource limits, then binds primary/comparison identities to the run's combined input digest. Every archived report must carry the complete run binding, including Development reports.

Every planned Payload file must correspond to exactly one selected input file and vice versa. Paths use ordinal equality after removing `payload/`; length, SHA-256 and MD5 all agree. This includes ICH/regional XML, `index-md5.txt`, standards assets and PDFs. Added/removed members or changed metadata output cannot borrow an old report. The capture store still remeasures written bytes.

Revision alone is not the delivery identity: a newer revision producing identical delivery bytes may pass capture; existing content uniqueness still prevents a second identical finalized snapshot in the same mode. Non-payload regional metadata is separately manifest-bound evidence. Formal readiness gates remain in force.

Tool version is `ratools-finalization-v2`; release manifest schema and old immutable reads are unchanged. Content digest excludes tool version/revision by existing design. `LocalReleaseCaptureStore.CommitAsync` verifies and adopts existing directories by digest, preserving the original immutable manifest; it neither compares manifest texts nor overwrites v1 evidence. New capture requires the complete persisted registry manifest/capture contract rather than incomplete synthetic test JSON.

Windows local evidence (2026-11-06, baseline `344a719`): old implementation 17 pass/13 expected fail; complete current Application and targeted tests compile with .NET 8 Recommended analyzers, repository editorconfig and warnings-as-errors; 32 targeted tests pass. A new PostgreSQL 16.14 cluster applies 31 forward migrations, repeats with no pending migrations, and passes 5 actual persistence tests (old-run content rejection, identical-output acceptance, idempotency/content uniqueness/timestamp round trips). No downgrade/delete tests run. Economic model read-only review PASS; suggested v1/v2 manifest text conflict withdrawn after inspecting digest-only adoption.

Evidence `.artifacts/qa-01-20261106/{red-integrity,current-analyzed,application-stage3,postgres-stage1}/`; retained runners and database are in that parent directory. Backups `backups/qa-01-20261106/`.

Standard full tests remain blocked by pre-existing constructor/analyzer errors recorded in QA-00. Whole-repository/browser/Linux/downgrade/external regulatory qualification is not established by this scoped acceptance.
