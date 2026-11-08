# Linux PDF process resource enforcement

QA-07 found that Linux QpdfProcessingEngine ignored its configured aggregate memory and CPU limits. Source comparison against the review baseline confirmed the plain Process.Start branch. A real Linux parent with two child processes exceeded both64MiB aggregate memory and200ms CPU, writing forbidden escape markers: run `20261107T140749-8ca3361ca680457398efb91f2e9893a1`, **0 passed /2 failed**. PythonPdfContentEngine has a separate intentional Linux refusal and remains unqualified.

## Required deployment

Linux qpdf execution now requires **RATOOLS_PDF_CGROUP_ROOT**, an existing service-owned cgroup-v2 subtree below `/sys/fs/cgroup` with cpu/memory/pids delegated and enabled. The calling process must also run within a hierarchy from which its account can migrate children into this subtree. A writable destination alone is insufficient: a normal login session could not migrate into its sibling user-manager tree. Missing delegation fails instead of running without the configured bounds. Windows continues to use the existing Job Object branch; Windows was not rerun in this continuation.

Each tool invocation, including its version probe, gets a unique retained cgroup. Before tool code executes, the host sets aggregate memory.max, zero swap, group OOM killing, pids.max=4 and cpu.max=one core. A shell joins the group, waits for a stdin gate, and execs quoted arguments only after the parent confirms its PID is enrolled. No tool arguments are interpolated into shell code.

The kernel enforces memory, swap, process-count and CPU-rate bounds. Aggregate CPU usage is sampled every20ms; reaching MaxCpuTime kills the whole group. **This is a sampled cumulative CPU deadline, not an exact kernel cumulative cutoff.** Scheduling and sampling can overshoot. A configured200ms budget used219,726µs in the accepted test. The fixed one-core rate may reduce throughput relative to unrestricted Linux execution. If accounting fails, the host kills the group, finishes process/reader cleanup and reports the failure. Completion/cancellation/timeout/output-limit also clear the group before releasing the shared slot. Empty groups remain retained; no physical directory deletion is performed. This assumes a controlled service account and ancestors, not a malicious concurrent writer sandbox.

## Verification

Unified command:

```powershell
pwsh -NoProfile -File ./scripts/remote-check.ps1 -Task test -Scope linux-resources -Machine <existing-machine-id>
```

`build` compiles the same backend source scope without executing probes. The entry starts an inner no-delete worker through the user systemd manager, giving it usable delegation. A unique RemainAfterExit unit is retained. The launcher polls its main-process status; on a poll error/deadline it stops both that unit and its task-specific sibling resource subtree, then checks both are empty. No --collect, stop/reset or file cleanup is used. The retained unit/cgroup count grows; this implementation defines no automatic expiry policy.

Accepted run **20261107T142441-ae3a1129cba8480bbab3a5b11b76c835**, base **4a422f7**, source archive **5efcca789fff21feeb9a6dfd8fa3fb3de6d0e54e3990bb733da4025e48918eb5**, whole exit0:

- Resource checks **3/3**:64MiB aggregate pressure caused oom_group_kill=1/oom_kill=4; cumulative CPU reached219726µs and stopped before the helper could exceed its workload target; an actual cpu.stat permission fault produced UnauthorizedAccessException and still emptied the group/recovered the slot. Test-only permissions were restored.
- Existing owned-process cancel/timeout/stdout-limit **3/3**,0 skips, now through the cgroup host.
- A real nested user-manager job migrated a child into its sibling subtree, then the launcher received injected poll exit5. Launcher failed as expected; both worker and child group were confirmed stopped.
- All recorded resource groups populated0. Normal launcher receipt MainPID0/ExecMainStatus0; unit remains active/exited. Current four backend layers compiled with the documented direct-csc path.

Economic read-only review of code and final evidence PASS. Evidence is in that run's results plus `.artifacts/qa-07-20261107/linux-resource-review.txt`.

Earlier run141035 failed before tool execution due to cross-hierarchy migration; it is not a resource pass. Run141531 had passing child checks but --pipe waited indefinitely on a retained unit; its launcher was explicitly terminated with exit143 and the summary-binding check rejected overall success. The replacement manager-launch receipt path passed in142045;142441 additionally proves the real polling-failure cleanup. All artifacts remain preserved.

This qualifies the **actual qpdf process host with controlled helper workloads**. It does not establish actual qpdf12.4.1 PDF output, Python3.11.9/PyMuPDF content execution/OCR, all resource workloads, an exact cumulative CPU cutoff, Windows regression, full MSBuild or Recommended-analyzer parity. Those gates remain distinct.


## Current foreground revalidation — 2026-11-09

`pwsh -NoProfile -File scripts/remote-check.ps1 -Task test -Scope linux-resources -RemoteRoot /dev/shm/ratools-remote -Foreground -WaitMinutes 1`

Run `20261109T000339-152b9f5adc114109be2c2d540197c4c2` passed resource3,
process-tree3 and injected manager-fault1. The actual foreground transport
completed in24.405s without timeout/truncation/OOM. Four stages exited zero;
all788 source-manifest entries match the archive and current files in final
read-only review. Memory pressure recorded oom_kill4/groupkill1; sampled CPU
usage was201974us against200000us; accounting fault recovered its slot. All
five recorded groups were populated0. Injected poll exit5 stopped the worker
and migrated child, and the outer manager had MainPID0/exit0/timeout480.
Source archive SHA256: `5ce232d10b68995a2e97fb5dd5fcd89d2ec411e5ac57c61097a4d7c2235fec72`.

The launcher now rejects existing unit/resource names before launch or cleanup,
includes launch failures in its cleanup guard, and uses480s inside the600s CLI
foreground deadline. Background retains1800s. This is current direct-csc
resource-host verification; these seven probes do not add backend xUnit cases
or qualify Windows content. Actual qpdf54+PG/qpdf7 is a separate run documented
in [QA-07-NATIVE-ACCEPTANCE.md](QA-07-NATIVE-ACCEPTANCE.md).
