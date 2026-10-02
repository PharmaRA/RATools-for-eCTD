# Independent package input reader (P2-02)

`IPackageInputReader` captures external sequence directories, application
directories and ZIP/ZIP64 files without a database, publishing model, writer or
business repository. It is an infrastructure service callable directly from tests
or another host. Durable jobs, upload authorization and API endpoints belong to
P2-06; this change does not expose arbitrary filesystem paths through an API.

## Selection and logical scope

Supply an explicit application identity, stable source ID and physical path in
`PackageInputSelection`. The application ID groups explicitly chosen sources; it
does not assert that XML administrative metadata was checked. Discovery recognizes
exact `index.xml` filenames under four-digit sequence roots. Multiple candidates
raise `PackageSelectionRequiredException` containing all candidates. Two copies
of the same sequence require a `RootRelativePath` selection. Root-only packages
whose physical names do not establish a sequence number require that number.

Only the selected sequence is available through `OpenRead("0001/index.xml")`.
Other sequence folders are recorded as surrounding input; they are never silently
adopted as history. Every captured file is mapped to an opaque filename in a fresh,
flat task directory. No ZIP path is used as a physical output path, and no links
are created to assemble historical packages.

`PackageInputSet` maps each historical logical sequence to one explicitly selected
capture. The baseline validates unique earlier sequence numbers and records source
kind, source ID, digest, profile, trust and optional validation/snapshot references.
The input set rejects mismatched application, source, digest or resource limits.
It preserves Unverified/Rejected trust; capturing files never upgrades trust.
Missing history fails exact lookup instead of searching neighboring workspaces.
Current-sequence append, where permitted, resolves within the target capture and
does not require an earlier-history entry.

## Controlled input handling

The reader rejects absolute names, parent traversal, backslashes, alternate data
streams, reserved device names, trailing spaces/dots and conflicting file/directory
names. Unicode NFC and invariant uppercase keys detect portable name collisions
without changing the original logical spelling. Directory ancestors and entries
cannot be links/reparse points; ZIP symlinks, reparse flags and special file types
are rejected. Windows opens verify the actual disk handle path. Linux opens use
`O_NOFOLLOW`/`O_NONBLOCK` and verify the `/proc/self/fd` target before reading;
nonseekable files are rejected. Other platforms currently fail explicitly.

`PackageReadLimits` binds entry count (including inferred directories), path depth
and length, per-file bytes, total expanded bytes, archive bytes, central-directory
bytes, compression ratio and elapsed time. File payloads are streamed with SHA-256
and compatibility MD5 calculation. Advertised ZIP metadata is checked before
expansion; actual streamed lengths, totals and ratios are also bounded. The central
directory is scanned before `ZipArchive` allocates entry objects, including actual
record counts and ZIP64 bounds. Encrypted, split-volume and malformed archives
are rejected. Timeout and user cancellation remain distinct outcomes.

Input and capture roots cannot overlap. Cancellation or rejection removes only the
new task capture. Cleanup verifies links and flat ownership instead of recursively
deleting a caller-supplied path. Callers must dispose opened streams before disposing
the capture. Capture directories must be private service-owned storage in the host;
durable ownership, retention and recovery after process death belong to P2-06.

## Digests and stability

`PackageInputManifest.Digest` hashes manifest schema v2: application ID, sequence,
ordered files with length/SHA-256/MD5, directories with emptiness, and surrounding
files/directories. This extends the G0 v1 example with structural inventory so
empty directories and additional files cannot disappear from report identity.
Equivalent selected directory and ZIP layouts have identical manifest digests.
Physical machine paths and task IDs do not enter this content digest.

`ICapturedPackageInput.InputDigest` additionally binds the original ZIP SHA-256,
when present, through a canonical object containing `schemaVersion`,
`manifestDigest` and `archiveSha256`. Use **InputDigest**, not the layout digest,
for `ValidationBinding.InputManifestDigest` and historical `ContentDigest`.
This prevents a repacked or metadata-modified ZIP from reusing the exact input
binding. The separate layout digest remains suitable for directory/ZIP parity.
The limits digest also participates in the seven-part run binding.

Capture verifies the original input twice and checks all captured bytes. Before
accepting a validation result, call `VerifyUnchangedAsync` on the input set again.
It re-enumerates and hashes the original content, checks original archive bytes,
and verifies capture integrity. Changed, removed or inaccessible input raises
`PackageInputChangedException` and must produce the `InputChanged` run state.
Captures isolate parsing from source edits; they are not formal immutable archives.

`PackageLogicalPath.ResolveReference` is the shared URI boundary for later XML,
lifecycle and PDF checks. It splits query/fragment, decodes path segments exactly
once, rejects encoded separators, validates the result, and prevents escaping the
logical application root. HTTP(S)/mailto URLs are classified without network I/O.
Inventory filenames themselves are never percent-decoded. Rule-specific policies
for queries, external links and target sequence validity belong to P2-03/04/05.

## Verification and remaining work

The hand-authored G0 sequences 0000–0003 are read directly and from real ZIPs.
`input-manifest-v2-vectors.json` is checked by an independent Python filesystem
and canonical JSON oracle. Tests also exercise ambiguous resends, missing history,
exact source binding, ZIP/ZIP64 metadata, path/link attacks, compression and byte
limits, input/capture modification, repacking, cancellation and injected deadlines.

```powershell
py -3.11 -X utf8 scripts/tests/test_publisher_reference_contract.py
dotnet test tests/RATools.Tests/RATools.Tests.csproj -c Release --filter FullyQualifiedName~Tests.PackageValidation
```

Windows checks have been run locally. Linux implementation is included but local
Linux qualification remains NotEvaluated until an actual Linux gate runs. These
inputs are not yet independently XML/DTD-validated or lifecycle-qualified;
P2-03 and P2-04 implement those stages. Per-rule execution coverage remains
incomplete until the run engine connects the stages; the development profiles
continue to block formal readiness.
