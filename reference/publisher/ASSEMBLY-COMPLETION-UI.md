# Assembly completion survives workspace refresh

QA-07 browser finding, 2026-11-07. Baseline `a2a05c5`; the affected component was unchanged from review baseline `90f3d36`.

After an assembly completed, its callback refreshed the parent workspace revision. That revision was part of the editor's React key, so the editor remounted and lost its verified result and completion notification state. The durable request survived in localStorage, but the operator saw only the refresh button even though the file had been mounted.

The editor now retains its identity for the same application and sequence. A revision change explicitly invalidates unsubmitted inputs, file parsing and preview requests, while preserving the execution handle, verified result and notification fence. Source mapping still remounts on revision changes. Switching application or sequence still replaces the editor.

Evidence under `.artifacts/qa-07-20261107/`:

- Real Chromium on Instabox reproduced the disappearing completion after a successful worker commit.
- A component test in which the completion callback advances the parent's revision reproduced the same failure: 22 passed, 1 failed in `assembly-ui-red.log`.
- Current assembly/source UI tests passed 29/29 in `assembly-ui-green.log`, including stale preview rejection and late responses after revision changes. TypeScript noEmit and scoped ESLint passed. Economic-model read-only review PASS; no reviewer build/test/write/delete.
- The rebuilt UI passed the real Chromium chain: upload a synthetic PDF, select the actual node, generate preview, explicitly select the valid row, execute through the real API/worker, keep the completion result visible after revision refresh, refresh that result, then create a new plan. See `assembly-browser-final.log` and `assembly-browser-state.json`.
- The same browser environment also created a synthetic EU application/sequence and saved/re-read an EU regional draft with its pending qualification message intact (`regional-browser-saved.json`).

The browser used a task-owned loopback API with the InMemory provider, actual current backend assemblies and static frontend, not mocked HTTP. Chromium 154.0.8037.57 and the API ran under a seccomp filter refusing unlink/unlinkat/rmdir. A test-only hosting extension used ephemeral Data Protection. Initial harness failures (missing startup dependency registration, overly long socket path, omitted standard assets and premature clicks) were corrected and are not product defect evidence. The six EU provider assets were supplied before regional verification. Files and browser profiles are retained on `insta01` in `/data/home/ratools-qa-76555a3-20261107/qa07-browser/`.

This independent fix does not close QA-07. Production gzip budgets still fail (recorded separately); two legacy report tests with physical cleanup remain excluded. Full finalization/delivery browser workflows lack their planned P4-08 entry point, and broader Linux resource qualification plus external validator evidence remain outstanding. Do not substitute successful assembly/regional checks for these unrun gates.
