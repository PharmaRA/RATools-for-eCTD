"""Compile current bounded production source on Linux; no Windows build output."""
import hashlib
import io
import json
import os
from pathlib import Path
import re
import subprocess
import sys
from urllib.request import urlopen
import xml.etree.ElementTree as ET
import zipfile

root = Path.cwd()
results = root / "results"


def phase(category):
    (results / 'linux-phase.json').write_text(json.dumps({'category': category}))


phase('toolchain-unavailable')
sdk = Path('/data/home/ratools-qa-76555a3-20261107/dotnet-8.0.423')
dotnet = sdk / 'dotnet'
out = root / 'linux-platform'
out.mkdir()
env = {**os.environ, 'DOTNET_ROOT': str(sdk), 'DOTNET_CLI_TELEMETRY_OPTOUT': '1',
       'DOTNET_NOLOGO': '1', 'DOTNET_EnableDiagnostics': '0'}
required = json.loads((root / 'global.json').read_text())['sdk']
version = subprocess.check_output([str(dotnet), '--version'], env=env, text=True).strip()
if not version.startswith('8.0.') or required['version'] != '8.0.400' or required['rollForward'] != 'latestFeature':
    raise SystemExit('Installed Linux SDK must satisfy the audited global.json selection')
project = ET.parse(root / 'src/RATools.Infrastructure/RATools.Infrastructure.csproj')
provider = next(node.attrib['Version'] for node in project.iter('PackageReference')
                if node.attrib['Include'] == 'Npgsql.EntityFrameworkCore.PostgreSQL')
packages = []


def package(name, release):
    url = f'https://api.nuget.org/v3-flatcontainer/{name}/{release}/{name}.{release}.nupkg'
    data = urlopen(url, timeout=90).read()
    (out / f'{name}.{release}.nupkg').write_bytes(data)
    packages.append({'name': name, 'version': release, 'sha256': hashlib.sha256(data).hexdigest(), 'url': url})
    return zipfile.ZipFile(io.BytesIO(data))


phase('dependency-install-failed')
with package('npgsql.entityframeworkcore.postgresql', provider) as archive:
    specification = ET.fromstring(archive.read('Npgsql.EntityFrameworkCore.PostgreSQL.nuspec'))
    dependency = next(node for node in specification.iter() if node.tag.endswith('dependency') and node.attrib.get('id') == 'Npgsql')
    selected = dependency.attrib['version'].strip('[]()').split(',')[0].strip()
    if not re.fullmatch(r'\d+\.\d+\.\d+', selected):
        raise SystemExit('Unrecognized pinned provider dependency range')
with package('npgsql', selected) as archive:
    (out / 'Npgsql.dll').write_bytes(archive.read('lib/net8.0/Npgsql.dll'))

phase('build-failed')
refs = sorted((sdk / 'packs/Microsoft.NETCore.App.Ref/8.0.29/ref/net8.0').glob('*.dll'))
refs += sorted((sdk / 'packs/Microsoft.AspNetCore.App.Ref/8.0.29/ref/net8.0').glob('*.dll'))
if len(refs) < 100:
    raise SystemExit('Required Linux SDK reference packs unavailable')
sources = ['scripts/remote-check/LinuxPlatform.cs',
           'src/RATools.Infrastructure/DocumentProcessing/PdfToolProcessBudget.cs',
           'src/RATools.Infrastructure/DocumentProcessing/PdfContentProcess.cs',
           'src/RATools.Infrastructure/Security/LocalOnlyDeploymentValidator.cs',
           'src/RATools.Infrastructure/Security/DeploymentOptions.cs']
assembly = out / 'LinuxPlatform.dll'
arguments = ['-nologo', '-target:exe', '-langversion:12', '-nullable:enable', '-warnaserror+', f'-out:{assembly}']
arguments += [f'-r:{item}' for item in [*refs, out / 'Npgsql.dll']]
arguments += [str(root / name) for name in sources]
(out / 'compile.rsp').write_text('\n'.join(arguments))
toolchain = {'sdk': version, 'runtime': '8.0.29', 'sourceFiles': sources, 'packages': packages,
             'scope': 'bounded production-source compilation; compiler warnings as errors; no full MSBuild or analyzer qualification'}
(results / 'linux-toolchain.json').write_text(json.dumps(toolchain, indent=2))
with (results / 'linux-compile.log').open('w') as log:
    build = subprocess.run([str(dotnet), str(sdk / f'sdk/{version}/Roslyn/bincore/csc.dll'), '@' + str(out / 'compile.rsp')],
                           env=env, stdout=log, stderr=subprocess.STDOUT)
if build.returncode:
    raise SystemExit(build.returncode)
(out / 'LinuxPlatform.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {'tfm': 'net8.0', 'frameworks': [
    {'name': 'Microsoft.NETCore.App', 'version': '8.0.29'}, {'name': 'Microsoft.AspNetCore.App', 'version': '8.0.29'}]}}))
if sys.argv[1] == 'test':
    phase('test-failed')
    with (results / 'linux-tests.log').open('w') as log:
        try:
            test = subprocess.run([str(dotnet), str(assembly), str(results)], env=env, stdout=log, stderr=subprocess.STDOUT, timeout=120)
        except subprocess.TimeoutExpired:
            phase('test-timeout')
            raise
    if test.returncode:
        raise SystemExit(test.returncode)
    report = json.loads((results / 'linux-tests.json').read_text())
    if report['failed'] or report['passed'] != 10 or report['skipped']:
        raise SystemExit('Incomplete Linux platform acceptance')
print('Linux bounded platform compilation' + (' and 10 selected checks passed' if sys.argv[1] == 'test' else ' passed'))
