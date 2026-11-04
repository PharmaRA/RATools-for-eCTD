# PDF navigation engine qualification (P5-05)

The selected navigation primitive is **qpdf 12.4.1 JSON object updates**. On
2026-11-04, a Windows trial changed a real two-page PDF's nested Unicode
bookmarks, page destinations, initial view, internal link and remote filename.
PyMuPDF 1.28.2 independently reopened the result, resolved destinations, extracted
text and rendered both pages. Decoded page streams, text and render hashes were
unchanged. The original PDF bytes were retained.

The [capability matrix](pdf-engine-capabilities.json) records the exact scope.
This is an engine trial, not a production PDF editor or overall P5 acceptance.
The trial intentionally uses fixed object addresses in a frozen hand-written
fixture. P5-06 must implement generic source-version binding, preview, durable
tasks, resource controls, post-processing validation and explicit adoption.

## Reproduce

Use Python 3.11+ and an externally installed qpdf 12.4.1. For Windows, the official
`qpdf-12.4.1-msvc64.zip` was downloaded from the qpdf GitHub release, and its
SHA-256 was compared with the same release's checksum list. This checks download
integrity; it is not a verified publisher signature. The exact archive,
executable and license-text hashes are in the capability matrix.

Install the optional independent-reader dependency from
`scripts/requirements-pdf-qualification.txt` in the qualification environment.
It is separate from the application's runtime dependencies. From repository root:

```powershell
$env:RATOOLS_QPDF = 'C:\path\to\qpdf.exe'
$env:PYTHONDONTWRITEBYTECODE = '1'
python -m unittest scripts.tests.test_pdf_navigation_qualification -v
python scripts/qualify_pdf_navigation.py --qpdf $env:RATOOLS_QPDF --output-dir .artifacts/pdf-engine-new-run
```

The output directory must be new. Test outputs are retained under
`.artifacts/pdf-navigation-qualification/`; the command retains source, target,
output PDF, JSON patch and receipt in the requested directory. There is no file
deletion. If cleanup is needed, move only these task-owned files to the recycle
bin. Normal test discovery without `RATOOLS_QPDF` explicitly skips this optional
qualification; a skip is **NotEvaluated**, not a passing engine trial. With the
environment variable set, missing tools/dependencies or a failed assertion fail
the run.

For Linux, the same Python commands accept an absolute Linux qpdf executable
path. **Linux has not been executed.** The local WSL entry point did not provide
a runnable distribution, and Docker was not available on PATH. No Linux result
is inferred from the Windows test.

## Evidence and checks

- [Input PDF](../../tests/RATools.Tests/Fixtures/PdfProcessing/navigation-source.pdf)
  is a hand-written object graph with two distinct page streams, nested outlines,
  catalog `/Dests`, `GoTo`, `GoToR` and `URI` actions. Helvetica is deliberately
  not embedded; this fixture does not establish font compliance.
- [Receipt](pdf-navigation-qualification/windows-20261104/receipt.json) binds
  input, output, remote target and JSON patch hashes, tool versions and page render
  hashes to this actual run.
- [Output PDF](pdf-navigation-qualification/windows-20261104/navigation.pdf),
  [remote target](pdf-navigation-qualification/windows-20261104/renamed.pdf) and
  [JSON patch](pdf-navigation-qualification/windows-20261104/navigation.update.json)
  allow independent inspection without rerunning the transformation.
- Tests exercise source/output overwrite protection, AES-256 rejection, signature
  dictionary rejection and direct-process timeout, in addition to the navigation
  and fidelity assertions. The signature sample is synthetic and does not prove
  cryptographic signature validation.

MuPDF 1.28.2 exposes remote named destinations as a file URI with
`#nameddest=...`. The test also independently reads the actual PDF `GoToR`
action's `/F` and `/D`, then resolves that destination in the remote target.
It does not infer a valid link merely from matching filenames.

## License and deployment conditions

qpdf's installed `--copyright` output and bundled `license.rst.txt` identify
Apache-2.0. Preserve the license and applicable notices for any redistribution.
The Windows MSVC distribution also carries Microsoft runtime DLLs; reviewing
their redistribution terms and all bundled third-party notices remains required
before shipping that distribution with the product. This change ships no qpdf
binaries and installs no system service or PATH entry.

PyMuPDF metadata identifies dual AGPL-3.0 / Artifex commercial licensing. It is
used only as a separately installed local qualification oracle. It is not a
selected production dependency, rendering component, or approval to redistribute
an integrated proprietary application. Production preview remains a separate
dependency decision.

The command uses structured subprocess arguments without a shell. Timeout kills
and waits for the direct qpdf process. The fixture is capped at 1 MiB and outputs
use exclusive creation. A production process sandbox, hard memory/CPU/output
limits, cancellation during a durable job, arbitrary PDF navigation, large-file
fidelity, OCR, font remediation and regional acceptance remain unqualified.
