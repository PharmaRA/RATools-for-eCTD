# QA-07 environment recovery — 2026-11-08

This record preserves the remote platform, **successful Linux fresh NuGet restore**,
MSBuild and PostgreSQL evidence. Current backend coverage is1824/1824 across runs,
including the Windows native cases subsequently accepted in
[native acceptance](QA-07-NATIVE-ACCEPTANCE.md). Development native download and
API/PG restart also passed. Formal implementation/acceptance, Linux Python content
and official/external qualification remain open as specified in the
[fixed closure checklist](QA-07-CLOSURE-CHECKLIST.md). Counts and limitations below
describe individual historical runs; they do not reopen already accepted work.

## Environment and execution

The retained machine ID `m_agquz43gf246rsedwy3bkathci` now resolves to the machine
named `linux`; the former `insta01` name no longer selected it. A direct `true`
probe using the ID returned exit 0. The default remote entry now uses that ID.
The diagnostic receipt is in
`.artifacts/local-check/environment-unblock-20261108T195519-eccc658224884bdf8a7a5ac75184cd69/`.
Credential values and raw account responses were not persisted or printed.

`/data` still had zero free bytes and `/tmp` had 149,553,152 free bytes.
The existing executable `/dev/shm` tmpfs had 4,169,068,544 free bytes at the first
check. New tasks use its explicitly allowed `/dev/shm/ratools-remote` root or
named task roots recorded in their launch receipts. No disk resize, machine
restart, expiry or deletion was performed. Tmpfs is not persistent across reboot;
result logs, manifests, TRX and restore assets are downloaded locally. Retained
remote databases and tool/cache files consume memory until a separately
authorized lifecycle operation; moving them to recycle does not reclaim space.

The background launch failed with CLI exit 1 after successful upload/extraction.
Its cause was not established. A new `-Foreground` mode is limited to new
`postgres-platform` and `linux-platform` tasks, refuses resume, checks complete
exit/timeout/truncation/OOM receipts and retains the existing source/summary
binding. It does not claim the background-job protocol was repaired.

```powershell
pwsh -NoProfile -File ./scripts/remote-check.ps1 -Task test -Scope linux-platform -RemoteRoot /dev/shm/ratools-remote -Foreground
pwsh -NoProfile -File ./scripts/remote-check.ps1 -Task test -Scope postgres-platform -RemoteRoot /dev/shm/ratools-remote -Foreground
python -B ./scripts/local-check/linux-case.py
```

The first command passed end-to-end in
`.artifacts/remote-check/20261108T211302-1349e614d98947c9bcb0bfaaabf7f841/`:
10/10 selected platform checks, zero failed/skipped, exit 0. Its source archive,
entry hash and summary identity were reviewed. The PostgreSQL platform run below
used the same uploaded scope through a recorded manual foreground command;
the second command is the supported entry, not a claim of a second platform run.

## Independent Linux filesystem acceptance

`linux-case-20261108T204210-6d2498873861474aaec09258c7a830b1` under
`.artifacts/local-check/` compiles current cache/test source with SDK 8.0.423,
then calls the three original methods with real xUnit assertions. All three
passed; the real Linux case retains distinct `a.pdf` and `A.pdf` with expected
digests. This is direct-csc source-method execution, not VSTest/full MSBuild.
Two methods overlap the Windows run; only the Linux filesystem method is new.

Eight required inputs, archive, exact `no-delete.py` launch chain and results
were independently reviewed. First run `200314-901913cc3ee14b3f8ec552e2bbd5aca2`
failed before tests on CS1701. The driver now uses the SDK's default
`NoWarn=1701;1702`; no assertion changed. Final source archive SHA-256:
`2c32d1f439266f2d060e32a8ed4de61baa318b65459182ccd4f9e5fb31dd98b9`.

## PostgreSQL platform and fresh restore

PostgreSQL platform evidence:
`.artifacts/remote-check/20261108T210327-38040602f8864ca7b7fdfaa87464e554/`.
The pinned PostgreSQL 16.15 tools compiled/loaded the existing recycle adapter,
passed its preservation/refusal probes, initialized a cluster and retained a
committed SQL value through clean stop/restart. Eleven steps exited 0, both
shutdown process lists and final native process lists were empty, and 24
recycled entries were retained. Source archive SHA-256:
`3151390436001279f21de9c72d43c29ad60ba506d4257639fe98907223558840`.
This platform check alone does not establish product migrations or PG tests.

Windows fresh restore attempts `fresh-restore-20261108T204725-ba63bd96ca1f4fe293ba92690291096d`
and `fresh-restore-proxy-20261108T205033-09bd561e18b947fe894d234aa093a17c`
failed with NU1301 and temporary-path access denied under deny-delete ACL.
Both failed evidence sets remain; no deletion protection was relaxed.

Final Linux fresh restore:
`.artifacts/local-check/linux-restore-20261108T211940-ae195e02bf184d74a074027fe67bcb53/`.
The initial package directory was empty. Actual `dotnet restore --force
--no-cache --disable-parallel` ran for API, DatabaseMigrator and Tests against
only the official NuGet source, resolving all six project graphs. The compiler
and three restore process groups all exited 0 without timeout or surviving
members. Six downloaded assets files contain only this run's package directory,
the official source and no fallback folders. Seccomp plus the reviewed task
recycle adapter retained 246 cleanup objects. This establishes fresh restore,
not a fresh OS/SDK installation or certificate trust-chain qualification.

Earlier `210928` had successful commands but incomplete process evidence;
`211552` completed restore but failed the runner's trailing-slash path comparison.
Both remain separate. The final comparison normalizes directory paths.
Final archive SHA-256:
`2682b0822554e4c2454b2c6d63540a8dd32d6599b4a2eb4a3542c35092df728a`;
summary SHA-256:
`4a329176b81043ee2580b229938ab7ce0b6495c6bd54b8de1a635a41bba1fff5`.

## Current Linux build and PostgreSQL tests

Final evidence:
`.artifacts/local-check/linux-postgres-20261108T213745-c79498e201cf433480c53b2fc67f6912/`.
`result-path.txt` locates its result subdirectory. The source archive contains
862 verified inputs; actual API, DatabaseMigrator and complete Tests MSBuild
all pass with Recommended analyzers, warnings-as-errors, zero warnings/errors.
An isolated database receives 31 migrations through the independent migrator;
the repeated invocation applies zero. PostgreSQL connections remain task-only
environment values, and psql uses `-X` to refuse startup configuration.

**28 classes / 107 cases passed, zero failed/skipped.** Discovery, selection and
TRX complete name multisets agree. Nine stages exited 0 with no surviving group
members; the server exited 0 and the independent tool-path/PID/start checks found
no native survivors. Three classes requiring unqualified Python/qpdf execution
are explicitly excluded: PostgresPdfContentTests, PostgresPdfRenameTests and
PostgresProcessingAdoptionTests (nine cases).

First run `212402-4ed6b6d29ab249778bb3828a5d5664ae` executed 116 cases: 99 passed,
17 failed. Nine failures were native-tool fixture/path prerequisites; the runner
had selected them too early, so this run is not accepted. A later independent
tool-path check confirmed no PG survivors. Seven failures were stale downgrade
expectations, and one was an absent shared HTTP content-root directory.

The latest regional-evidence migration unconditionally rejects downgrade with
`NotSupportedException`. Tests now require that precise message, unchanged
complete migration history and retained input/report/snapshot/PDF bytes.
Production migration protection is unchanged. The earlier SQL `55000` conditional
guards are unreachable through this latest chain and are **not** covered here.
The HTTP fixture now uses the existing task repository root. The draft cleanup
row is retained instead of deleted. These repairs preserve business assertions.

Final source archive SHA-256:
`44cf73b0c70fe39dda4efba156269370ff2c51acd22bec50b397ea1cb2c1434e`;
source manifest:
`3e0627b5d68d3901a2b0c08fc5f7ed308609bf215176cc3041b4c1f71b96663b`;
TRX:
`36b17bce1aae1825e880d4c578e7427128461ecf0803286acc5a7525e684c7c1`.
Code, execution and evidence read-only reviews passed.

Five PG cases overlap the Windows 1,641 (four methods; EU theory has two cases).
There are 102 new PG cases plus one new Linux filesystem case. Combined coverage
is therefore 1,744 cases across different executions, not one unfiltered pass.
The remaining 80 comprise 71 native PDF cases and nine PG/native PDF cases.
Preserve multiset counts: two Windows parameterized results share a truncated
display name but have different test IDs, so name-set deduplication loses a case.

Current Python content processing deliberately refuses Linux in production
`PdfContentProcess` and `PythonPdfContentEngine`. Installing Python alone cannot
qualify that capability. Native tool enforcement, current combined browser/PG/
resources, full-suite and official/external Formal gates remain separate.


2026-11-08 当前浏览器补验：新前端build215950类型检查/29预算通过，静态产物在Linux Chromium运行；当前API/Migrator来自PG最终实际MSBuild且111/52输出摘要固定。InMemory223734为7browser+3ZIP/PDF通过；PG230609-61d082f8为8browser+3通过，原生下载completed、ZIP69937B，API/PG重启后26584B报告逐字节一致、workspace/manifest/versions不变，PG/profile/阶段进程残留均空。首轮profile socket过长和缺Migrator依赖失败保留，短唯一根及完整独立migrator输出修复；未放宽seccomp。精确SHA和证据见[当前浏览器验收](FINALIZATION-ACTUAL-BROWSER.md)。本项关闭选定Development原生下载与当前API/PG进程重启，不代表Formal、机器重启/崩溃、复杂PDF或全部资源/native联合。
