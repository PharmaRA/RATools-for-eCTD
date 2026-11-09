# Windows native no-delete qualification harness

The earlier restricted-token/ACL design failed its creation-time DELETE access
probe. The new harness intercepts destructive operations in the reviewed x64
process tree before the first user-mode instruction. It does not claim that
ACL creation-time behavior changed, and it is not an adversarial sandbox against
direct syscalls, deliberate code patching or a child starting a separate debugger.

Implementation is in `scripts/local-check/native-no-delete/`. The launcher runs
outside the tested Job, so it does not add a wrapper to each PDF tool's existing
four-process/memory/CPU Job. Product source and business assertions remain unchanged;
two fixture roots now accept an optional task-owned directory.

## Enforcement and failure handling

`NativeNoDelete.cs` creates the root suspended with DEBUG_PROCESS, assigns its
owned kill-on-close Job, then handles every CREATE_PROCESS event. Before the
first Continue it verifies x64 architecture, owned Job membership, the remote
ntdll image/header, four complete original syscall entries and Nt/Zw alias
identity. The supported KnownDLL mapping must match the launcher's verified
mapping; otherwise the task is terminated.

Each process receives a private policy page changed from RW to RX, with a
readback and instruction-cache flush. The entry patches are checked, original
entry permissions restored, and an exclusive PID-and-creation-time-bound receipt is written
before execution continues. It intercepts:

- `NtDeleteFile`: unconditional refusal.
- `NtCreateFile` / `NtOpenFile`: delete-on-close refusal; creation also refuses
  FILE_SUPERSEDE. Normal file creation/write/read remains available.
- `NtSetInformationFile`: only reviewed basic attributes, position, allocation,
  EOF, completion and I/O-priority classes (4/14/19/20/30/43) are allowed.
  Disposition, link, mode and other classes are refused. Rename classes10/65
  are also refused by default; optional modes below validate specific retained moves.

The x64 policy preserves argument registers and stack layout. Initial debugging
breakpoints are recognized only once per process, using the current validated
ntdll entry/window. Other CLR/SEH exceptions are passed to the application with
DBG_EXCEPTION_NOT_HANDLED. The current loader breakpoint is a platform binding,
not a general Windows ABI guarantee.

Only the read-only NUL input and fresh output log are inherited at the root.
Debug-event process handles are duplicated for launcher ownership; system-owned
thread/process handles are not closed twice. Normal exit, timeout and installation
failure terminate remaining owned processes and drain exit events. Success needs
root exit/signalling, no tracked descendants, Job active count zero and a matching
Job total/guarded process count. This detects uninstrumented process-count drift;
it is not preventative coverage of an intentionally new debugging chain.

The default mode refuses all rename/link operations. Optional business and CLI
initialization modes are mutually exclusive and require a new empty task root.
Trusted tools must use their
reviewed ordinary ntdll paths. No system policy, installed tool, kernel driver,
production DLL or on-disk system executable is modified.

## Initial qualification evidence

Base: `.artifacts/local-check/windows-native-guard-20261109T072721-c7be1196b6804e07ac520412e5cdcc5a/`.

| Stage | Accepted evidence and result |
| --- | --- |
| Independent policy ABI | `policy-red.log` reproduces the old NtCreateFile argument-offset defect using a fake syscall tail; `policy-green.log` passes 25 checks. No filesystem syscall is reachable from that tail. |
| Protected csc build | `build-4/`: actual .NET csc exit0, guard/code/assembly/ntdll hashes bound. Compiler and conhost both protected before first Continue and stopped. |
| Parent/child/grandchild | `build-4/tree/`: three dotnet processes each perform 17 rejection/passthrough checks plus retained write/read; one conhost is also protected. Four guarded = four Job processes, all stopped. |
| CLR exception | `build-4/exception/`: original caught managed exception returns42 as expected; all processes stopped. |
| Installation failures | `build-4/before-patch/` and `after-patch/`: expected exit1, forced stop established; no probe output executed. |
| Timeout | `build-4/timeout/`: receipt verification completed before waiting; expected exit124 and full stop established. |
| Fixed native versions | `native-versions/`: Python3.11.9, PyMuPDF1.28.2 and qpdf12.4.1 each exit0, two guarded = two Job processes, stopped. These are version/import probes, not PDF/OCR qualification. |
| Current MSBuild | `current-msbuild-1/`: API, Migrator and complete Tests each zero warnings/errors; outer26 guarded =26 Job processes, stopped. All881 source inputs match current files. |

Code and each accepted evidence stage above received independent read-only review.
The synthetic review reconstructed all44 policies across11 PID receipts and
checked code/hash/RX and three retained sentinel hashes. The MSBuild review checked
the26 process receipts and all881 inputs.

Current ntdll SHA256:
`f528b1ff309d98c87e6a4021a1a1b11dfc8002eb49c3680cc16a26ae8f036a0f`.
Initial accepted guard source SHA256 (`build-4`):
`d91446ae27c1d9248ebcab8ade5acda0e247863b3b6d590b6557ec154515aa60`.
Initial source-manifest SHA256:
`4b0daf207da3a0fe3796adf98cd073f526342cb50b79af0eb6ac213292ceefc5`.
Initial test DLL SHA256:
`37454aa346a9a44ec7d2cebaf2364431b2c09b699af40eac7438487312b6aa63`.

Earlier `build-1` failed before any child: DebugSetProcessKillOnExit was called
before attaching a debug object. `build-2` safely ran the guarded compiler but
its response file split `/out:` from the path. `build-3` compiled successfully,
then a probe incorrectly expected INVALID_HANDLE for the -1 current-process
pseudo-handle. A no-file-handle baseline proved OBJECT_TYPE_MISMATCH for -1 and
INVALID_HANDLE for null; the probe now uses null. None counts as native acceptance.
The incorrect ABI policy and unexecuted C compiler draft are retained in backups;
cl/link were not launched.

All rejection probes use null object attributes or invalid handles. They never
supply a valid deletion target, use delete-on-close on a real file, or execute
physical deletion. Guard verification precedes those probes, and permits/refusals
are checked independently of retained normal-write sentinels.

## Reproduction

Use fresh absolute evidence directories below `.artifacts/local-check/`:

```powershell
pwsh -NoProfile -File scripts/local-check/native-no-delete/build.ps1 -Evidence <new-guard-build>
pwsh -NoProfile -File scripts/local-check/native-no-delete/verify.ps1 -Evidence <new-guard-build>
```

Both paths force the25 independent ABI checks. Compilation runs under the same
guard; only the launcher is compiled in PowerShell memory. Each failed run is
retained, and paths are never reused or physically cleaned.

`run-tests.ps1` consumes a current protected MSBuild run and a qualified guard
build. It binds current source inputs, tool/worker/model/fixture hashes and
test outputs, requires actual Windows and absent PostgreSQL environment, runs
guarded discovery, selects an explicit group and compares exact name multisets
to the TRX. Groups are resources8, workflow7 and combined2. Presence of a group
in the runner is not evidence that it passed; current results and remaining
QA-07 boundaries are in [QA-07-NATIVE-ACCEPTANCE.md](QA-07-NATIVE-ACCEPTANCE.md).

Official debugging-event and creation-flag pages are retained with HTTP200,
UTC and SHA256 receipts in the evidence base. The mechanism depends on the
documented CREATE_PROCESS event occurring before user-mode execution:
[Debugging Events](https://learn.microsoft.com/en-us/windows/win32/debug/debugging-events),
[Process Creation Flags](https://learn.microsoft.com/en-us/windows/win32/procthread/process-creation-flags).


## PID reuse repair and Windows business checks

`resources-1/` passed8/8 original Windows resource/concurrency cases, zero
failed/skipped, with final read-only review. Discovery5 and tests30 process
instances all matched their Job counts and stopped. All35 process receipts,
10 input bindings,78 test output files and exact discovery/TRX name multisets
were independently checked. TRX SHA256:
`d8dbd34d05cfc0f4f02f4d4d8f4579618cc43604ffa7d3d026eeae038f4d8493`.

`workflow-1/` exposed a launcher evidence bug: Windows reused exited conhost
PID15716, causing the exclusive `process-PID.json` filename to collide. The
launcher stopped all215 Job processes;214 were protected before the rejected
instance. No TRX was produced; this run provides no workflow acceptance.

The repair binds the kernel creation FILETIME from each duplicated process
handle, uses `process-PID-creationTime.json`, and records the same pair in
creation/guard events. The probe independently checks its own creation time.
Policy bytes are unchanged. `build-5/` passed protected csc,25 ABI checks and
all five synthetic scenarios. New guard SHA256:
`0348455537b8eeb1bfbf3aedfaef4807860ce558288bbbc1d39cab7f4c885f2c`.
`BuildGuardRun` binds the earlier qualified build guard and the current qualified
execution guard separately against the same ntdll; unchanged product outputs
remain bound to their original build.

`workflow-2/` completed435 guarded/Job instances, including59 reused PIDs, and
stopped completely. Its actual TRX records6 passed/1 failed/0 skipped. The legacy
navigation case's source path requires `QpdfProcessingEngine`'s non-overwriting
`File.Move`; the observed access-denied error is consistent with all-rename
refusal. This run did not record a per-syscall denial trace. The first Combined case similarly
requires `AssemblyFileStore.PromoteSource`'s staging-directory move. These are
guard compatibility gaps, not demonstrated product defects. Business assertions
and the refusal policy were retained; the workflow group is not fully accepted.

Final read-only review of build5 and workflow2 passed for the launcher repair/evidence: all435 process-instance receipts match creation/guard event multisets, including59 reused PIDs. The six individual workflow passes are accepted, bringing combined coverage to1819/1824; the seven-case workflow group remains failed, with five baseline cases outstanding overall.

## Current controlled moves and Windows qualification

`RetainedMovePolicy.cs` validates the exact paused syscall context for rename
classes10/65. Business mode accepts only `output.pdf.<32hex>.qpdf.tmp` to
same-directory `output.pdf`, or `_assembly-staging/GUID` to same-sequence
`_assembly-sources/GUID` containing exactly one plain file. It verifies the source
handle's actual path, volume/file ID and single link, rejects reparse ancestors,
requires an absent target and zero replacement flags, and copies the validated
request to independent remote read-only memory before calling the original syscall.
The kernel nonreplacement operation protects an intervening target collision;
the harness does not claim an atomic whole-Job filesystem snapshot.

Path tests cover wrong names, outside roots, existing targets, wrong sequence/GUID,
extra directory entries, hardlinks and file/directory symlinks. Task-owned link
fixtures remain retained. No link creation operation is enabled in the guarded
business path. Actual target IDs/volumes/link counts and retained bytes were
checked for the successful workflow and combined promotions.

Two prior path-length failures remain retained: `workflow-3` had a274-character
working directory, and `wf-20261109-01` produced a270-character qpdf input.
Short evidence roots `w1/f` and `c1/f` resolved these harness constraints without
changing content assertions. Their final7/7 and2/2 results and digests are in
[native acceptance](QA-07-NATIVE-ACCEPTANCE.md).

`current-msbuild-3/` is the current API/Migrator/Tests build: each has zero
warnings/errors, all881 inputs match, and26 guarded/Job instances stopped.
Its manifest is `4a1f184778443dfcce5f7c5d713d8b3211d311d1f254947caa669be8324cb44c`.
`current-msbuild-2` failed because two ACL backups used the same leaf name;
`msbuild.ps1` now uses a GUID in every backup and records that path.

Separate CLI initialization mode requires the fixed executable's full path and
SHA at launch and at its CREATE_PROCESS event. It permits only the five reviewed
first-time SSH configuration promotions inside the isolated task profile;
replacement requests are normalized to nonreplacement and existing targets are
refused. Other executables cannot use that exception. Key, certificate and
configuration contents are never read or hashed by this harness.

`build-12/` qualifies current guard source with seven scenarios and14 path checks,
including five synthetic CLI initializations and three refusals; all process
trees stopped. `tunnel-9/` independently established an actual SSH banner through
an Instabox loopback-only tunnel, five guarded/Job instances stopped, final
read-only review PASS. Later connection attempts require their own receipts;
this probe does not establish PostgreSQL acceptance.

## PostgreSQL reproduction and proxy boundary

The final successful PostgreSQL execution is `.artifacts/local-check/pg2-11/`:
identity matched, migrations31/0, content2/2, all166 local guarded/Job instances
stopped, and remote owned-marker shutdown left no server/native processes.
Final read-only source/evidence review passed; coverage accounting is tracked in
[native acceptance](QA-07-NATIVE-ACCEPTANCE.md).

The current environment requires a private SSH tunnel over local SOCKS5 port7890.
`SshProxy.cs` binds system OpenSSH by absolute path and SHA, preserves original
arguments, and adds a ProxyCommand using the pinned Python relay. The child PATH
change is limited to the task CLI process. It does not edit SSH configuration.
`ssh-socks-relay.py` uses binary `os.read`/`os.write` and only connects to local7890.
The C# relay mode remains diagnostic and is not accepted for the live tunnel.
Synthetic protocol/quoting tests plus anonymous-pipe interaction total19 checks;
actual tunnel qualification is `tunnel-17`,8 guarded/Job instances stopped.
Raw SSH stderr is forwarded inside the process tree and consumed only in memory
by the scoped runner, which retains fixed classifications. Direct use of the
wrapper outside that runner does not have this redaction boundary.

Use the retained qualified artifacts with a fresh short evidence directory:

```powershell
$repo = 'C:\Repositories\RATools-for-eCTD'
$nativeBase = "$repo\.artifacts\local-check\windows-native-guard-20261109T072721-c7be1196b6804e07ac520412e5cdcc5a"
pwsh -NoProfile -File scripts/local-check/native-no-delete/run-postgres.ps1 `
  -Evidence "$repo\.artifacts\local-check\pg-next" `
  -BuildRun "$nativeBase\current-msbuild-3" `
  -IdentityProbe "$repo\.artifacts\local-check\pg-identity-1\PostgresIdentityProbe.dll" `
  -SshProxyPath "$repo\.artifacts\local-check\ssh-proxy-green7\ssh.exe"
```

This is a current-workspace reproduction, not a portable environment installer.
It requires the pinned CLI, current source/output manifests, retained remote
PostgreSQL platform and available API environment variable. Existing profile
roots are never reused. `build-ssh-proxy.ps1 -Evidence <fresh-root>` can rebuild
the wrapper under guard, but rebuilt artifacts require their own review and
tunnel evidence. The independent failure-only migrator diagnostic currently
uses retained `pg-diag-1` bindings and is not counted as migration acceptance.
The earlier unused StartupHook source was inspected for links and moved into
`backups/qa-07-local-20261109/unused-startup-hook/`; no source was physically deleted.
