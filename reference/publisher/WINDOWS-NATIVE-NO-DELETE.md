# Windows native no-delete qualification harness

The earlier restricted-token/ACL design failed its creation-time DELETE access
probe. The new harness intercepts destructive operations in the reviewed x64
process tree before the first user-mode instruction. It does not claim that
ACL creation-time behavior changed, and it is not an adversarial sandbox against
direct syscalls, deliberate code patching or a child starting a separate debugger.

Implementation is in `scripts/local-check/native-no-delete/`. The launcher runs
outside the tested Job, so it does not add a wrapper to each PDF tool's existing
four-process/memory/CPU Job. Product source and test assertions remain unchanged.

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
  Disposition, rename, link, mode and all other classes are refused.

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

The strict implementation refuses all rename/link operations. A failing business
test does not authorize relaxing this policy. Trusted tools must use their
reviewed ordinary ntdll paths. No system policy, installed tool, kernel driver,
production DLL or on-disk system executable is modified.

## Current evidence

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
Current source-manifest SHA256:
`4b0daf207da3a0fe3796adf98cd073f526342cb50b79af0eb6ac213292ceefc5`.
Current test DLL SHA256:
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
