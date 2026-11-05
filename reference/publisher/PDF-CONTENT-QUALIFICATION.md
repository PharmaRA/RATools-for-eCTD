# PDF content processing qualification — P5-08

The content adapter is enabled only through an explicitly configured external
Python runtime and the pinned qpdf executable. The application does not bundle
Python, PyMuPDF, qpdf, or OCR models. The durable worker binds the configured
script, runtime probe, qpdf version/hash and OCR model hash into its content plan;
the result receipt and resource usage are stored with the immutable result before
the operator can adopt it.

## Qualified capability scope

The retained Windows evidence in
`pdf-content-qualification/windows-20261105/qualification.json` contains three
independent samples. Each source and result is retained, each result passed qpdf
syntax checking, and text was independently extracted with Xpdf `pdftotext`
4.06.

| Operation | Verified scope | Result |
| --- | --- | --- |
| OCR | PyMuPDF 1.28.2, Tesseract English `eng.traineddata` SHA-256 `7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2`, 150 DPI, explicit pages 1, 2 and 4, invisible text overlay | Search text, reading order, bookmarks, links, page boxes, rotation and 72/150 DPI visible pixels preserved; skipped pages remain unchanged |
| Standard fonts | Helvetica, Times and Courier, twelve Type1 WinAnsi variants with pinned font-program hashes | Output font dictionaries point to embedded programs; text, links, navigation and visible pixels preserved |
| Version normalization | qpdf 12.4.1, PDF 1.4 input to PDF 1.7 output | qpdf check, independent text extraction, page geometry and visible pixels preserved |

OCR pages with existing searchable text are skipped and reported. Every OCR
result requires an operator to review text and reading order. Font processing
rejects unknown or missing programs. Version normalization does not claim PDF/A
or regulatory conformance. Encrypted, signed, form, active-JavaScript and repair-
needed files fail closed.

## Runtime and resource contract

`scripts/pdf_content_worker.py` is the pinned request worker and its SHA-256 for
this evidence is `e2861b16943d3077a1c7a5970e4db52fb688dbca5382c20790cf2a3b50a574f8`.
The probe reports Python 3.11.9, PyMuPDF 1.28.2 and runtime fingerprint
`6dee5506accb425a4cc0423a70479369fb601ed3618a03ca6eb03eb45c0d3956`, derived from
loaded package modules and metadata-listed native files without exposing paths.
`mode=inspect` returns source facts and blockers without producing a PDF;
`mode=probe` returns runtime facts without reading a source document.

The durable host supplies positive integer input/output/report/pixel limits,
serializes content engine work, passes structured arguments without a shell, and
records wall time, CPU and peak memory. Cancellation and deadline handling belong
to the process tree; results are exclusive-created and cannot overwrite source or
an existing result. Content-only plans reject navigation edits and bind all OCR,
font, version and engine parameters. A returned receipt must match the plan,
source/result hashes, output version and requested operation before adoption.

On Windows the process is created suspended, assigned to its resource-limited Job
and resumed only after assignment succeeds. Job limits apply to its descendants.
The retained memory statistic is Windows `PeakJobMemoryUsed`, including the system's
high-water accounting of attempted allocations. It can exceed the configured
commit limit even when the allocation is rejected. It is not clipped or presented
as successful committed memory. The resource regression verifies the configured
Job flags/limit and an actual failed allocation, rather than treating this raw
peak statistic as an enforcement measurement.

## Configure and reproduce

Install the optional runtime from `scripts/requirements-pdf-content.txt` into the
explicit Python interpreter. PyMuPDF is licensed under AGPL-3.0 or Artifex
commercial terms; evaluate those terms for the intended deployment. This change
redistributes neither the engine nor its fonts/model. The English model comes
from the Tesseract `tessdata_fast` 4.1.0 release and is checked against the hash
above. Keep its license/notices with any separately provisioned model.

Configure the `PdfProcessing:Content` section (environment prefix
`PdfProcessing__Content__`) with:

| Setting | Required value/purpose |
| --- | --- |
| `Enabled` | `true` to opt in; defaults to disabled |
| `PythonExecutablePath` | Absolute path to the qualified external Python |
| `PythonVersion` | `3.11.9` for this Windows qualification |
| `ScriptPath` | Absolute path to `scripts/pdf_content_worker.py` |
| `ScriptSha256` | The reviewed script hash listed above |
| `ScratchDirectory` | Absolute path to controlled task-owned probe storage |
| `TessdataDirectory` | Absolute directory containing the qualified English model; omit to disable OCR while retaining qualified font/version operations |
| `MaxInputBytes` / `MaxOutputBytes` | Default 512 MiB each; explicit bounded policy |
| `MaxReportBytes` / `MaxPixels` | Default 1 MiB report and 24 million pixels per page |
| `MaxMemoryBytes` / `MaxCpuTime` | Default 1 GiB committed memory and three minutes of Job CPU |
| `ProbeTimeout` / `InspectionTimeout` | Default 20 seconds and 30 seconds |

The qpdf path uses the existing `PdfProcessing:Engine:ExecutablePath` setting.
The processing job's wall deadline uses `PdfProcessing:ExecutionTimeout`. Changes
to bound tools or resource settings invalidate content plans and require a new
preview. Missing dependencies and unqualified hosts return disabled capabilities;
the UI displays reasons and keeps those operations unavailable.

To create a new retained qualification run from repository root, use Python
3.11.9 with PyMuPDF 1.28.2 and absolute paths to the pinned tools/model:

```powershell
python -B scripts/qualify_pdf_content.py --output-dir .artifacts/pdf-content-new-run --qpdf $env:RATOOLS_QPDF --tessdata $env:RATOOLS_TESSDATA --pdftotext $env:RATOOLS_PDFTOTEXT
$env:RATOOLS_PDF_CONTENT_TESTS = '1'
python -B -m unittest scripts.tests.test_pdf_content_worker -v
```

The output directory must be new. Samples, failed attempts and diagnostics are
retained. No test or worker removes source files or retained versions.

## Boundaries

This evidence covers Windows only. Linux execution, arbitrary fonts and encodings,
large-document scale, hard resource isolation outside the Windows process-job
implementation, OCR languages other than English, PDF/A, regional conformance,
digital-signature validity and independent regulatory-validator acceptance remain
unqualified. The retained sample is an engineering capability result; it does not
establish submission or regulatory readiness.
