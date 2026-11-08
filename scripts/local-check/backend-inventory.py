"""Inventory the authorized backend scope and classify known execution prerequisites.

This is a direct-call audit, not proof of native-library behavior. The actual
VSTest discovery list is reconciled against these classes by the caller.
"""
import hashlib
import json
from pathlib import Path
import re
import sys

repo = Path(__file__).resolve().parents[2]
output = Path(sys.argv[1]).resolve()
if output.parent != (repo / '.artifacts/local-check').resolve():
    raise ValueError('Inventory evidence must be a direct local-check child')

native_classes = {
    'PdfCombinedQualificationTests', 'PdfContentHttpTests', 'PdfContentResourceTests',
    'PdfContentWorkflowTests', 'PdfRenameEngineTests', 'PdfRenameHttpTests',
    'PdfRenamePreviewTests', 'PdfRenameWorkflowTests', 'ProcessingHttpTests',
    'ProcessingWorkflowTests', 'QpdfConcurrencyTests', 'QpdfNavigationIntegrityTests',
    'QpdfProcessingEngineTests',
}
portable_methods = {
    'ImportFileHashCacheTests': ['DistinctCaseSensitiveAddressesDoNotReuseTheFirstByteSourcesDigest',
                               'GetAsync_ComputesMd5AndSha256FromOneOpenPerPath'],
    'FdaM1EndToEndTests': ['ActualHttpSaveOfExplicitIdentifiersReachesDistinctFormXmlAndFullReimport'],
    'EuM1EndToEndTests': ['ActualHttpSaveKeepsBothEnvelopesAndIndependentPiContextsThroughOutputAndReimport'],
    'RegionalActivationAcceptanceTests': ['ActualHttpCapturesExposeDistinctRegionalRulesAndRetainRequiredQualificationGaps',
                                          'ChangedRegionalBehaviorCannotReusePreRegionalEngineIdentity'],
}
test_attribute = re.compile(r'\[(?:Fact|Theory|RequiresPostgresFact|LinuxFact|WindowsFact)(?:\(|\])')
deletion = re.compile(r'\b(?:File|Directory)\s*\.\s*Delete\s*\(|FileOptions\s*\.\s*DeleteOnClose|\bDROP\s+DATABASE\b')
sources = {}
classes = []
violations = []
recycle_calls = []
for folder in ('tests/RATools.Tests', 'src/RATools.Domain', 'src/RATools.Application',
               'src/RATools.Infrastructure', 'src/RATools.Api', 'src/RATools.DatabaseMigrator'):
    for path in sorted((repo / folder).rglob('*.cs')):
        relative = path.relative_to(repo)
        if {'bin', 'obj', 'App_Data'} & set(relative.parts):
            continue
        if path.is_symlink():
            raise ValueError('Audit source must not be a symbolic link')
        data = path.read_bytes()
        text = data.decode('utf-8-sig')
        sources[relative.as_posix()] = hashlib.sha256(data).hexdigest()
        for number, line in enumerate(text.splitlines(), 1):
            # Comments may describe why deletion is forbidden; do not execute it.
            code = line.split('//', 1)[0]
            if deletion.search(code):
                violations.append({'file': relative.as_posix(), 'line': number})
            if re.search(r'(?:RecycleFileSystem|FileSystem)\.Delete(?:File|Directory)\(', code):
                recycle_calls.append({'file': relative.as_posix(), 'line': number})
        if folder != 'tests/RATools.Tests' or not test_attribute.search(text):
            continue
        namespace = re.search(r'^namespace\s+([\w.]+)\s*;', text, re.M)
        primary = re.search(r'^public\s+(?:(?:sealed|abstract|partial)\s+)*class\s+(\w+)', text, re.M)
        if namespace is None or primary is None:
            raise ValueError(f'Unclassified test source: {relative}')
        name = primary[1]
        reasons = []
        if name.startswith('Postgres') or 'RequiresPostgresFact' in text or 'RATOOLS_TEST_POSTGRES' in text:
            reasons.append('PostgreSQL environment/native cleanup/migration prerequisites; only explicitly allowed methods may run')
        if name in native_classes:
            reasons.append('Native PDF tool cleanup qualification pending')
        if '[LinuxFact' in text:
            reasons.append('Linux filesystem prerequisite; only explicitly allowed methods may run')
        methods = portable_methods.get(name, [])
        classes.append({'class': namespace[1] + '.' + name, 'source': relative.as_posix(),
                        'selected': not reasons or bool(methods), 'reasons': reasons,
                        'allowedMethods': methods})

result = {'scope': 'All backend C# source direct deletion audit and test class inventory; not native qualification',
          'sources': sources, 'classes': classes, 'directDeletionViolations': violations,
          'reviewedWindowsRecycleCalls': recycle_calls,
          'limits': ['No PostgreSQL or native PDF test execution', 'Four reviewed mixed classes use explicit method allowlists; other prerequisite methods remain excluded',
                     'Windows recycle calls remain allowed; library internals are not proven by this scan']}
(output / 'backend-inventory.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(f'Backend inventory: {len(sources)} source files; {sum(c["selected"] for c in classes)} selected classes; '
      f'{sum(not c["selected"] for c in classes)} prerequisite classes; {len(violations)} direct violations')
if violations:
    raise SystemExit(1)
