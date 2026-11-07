"""Current-source Linux browser host; explicit compiler, not MSBuild parity."""
import hashlib
import io
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
from urllib.request import urlopen
import xml.etree.ElementTree as ET
import zipfile

root = Path.cwd()
results = root / 'results'
out = root / 'backend'
out.mkdir()
packages_dir = root / 'nuget'
packages_dir.mkdir()
sdk = Path('/data/home/ratools-qa-76555a3-20261107/dotnet-8.0.423')
env = {**os.environ, 'DOTNET_ROOT': str(sdk), 'DOTNET_EnableDiagnostics': '0',
       'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'DOTNET_NOLOGO': '1'}
version = subprocess.check_output([str(sdk / 'dotnet'), '--version'], env=env, text=True).strip()
selection = json.loads((root / 'global.json').read_text())['sdk']
if version != '8.0.423' or selection != {'version': '8.0.400', 'rollForward': 'latestFeature', 'allowPrerelease': False}:
    raise RuntimeError('Review the changed SDK policy before compilation')
projects = [root / f'src/RATools.{layer}/RATools.{layer}.csproj' for layer in ['Domain', 'Application', 'Infrastructure', 'Api']]
trees = [ET.parse(path) for path in projects]
selected = {}
archives = {}
records = []


def numbers(value):
    if not re.fullmatch(r'\d+(\.\d+){1,3}', value):
        raise ValueError('Unsupported dependency version: ' + value)
    return tuple(map(int, value.split('.')))


def require(name, version):
    name = name.lower()
    # Design has no compile assets in these project declarations.
    if name == 'microsoft.entityframeworkcore.design':
        return
    lower = version.strip('[]()').split(',')[0].strip()
    if not lower:
        raise ValueError('Dependency without a pinned minimum')
    if name not in selected or numbers(lower) > numbers(selected[name]):
        selected[name] = lower


def framework_rank(name):
    name = name.lower().replace('.', '').replace('version=v', '').replace(',', '')
    aliases = {'net80': 100, 'net8': 100, 'net70': 90, 'net7': 90, 'net60': 80, 'net6': 80,
               'net50': 70, 'net5': 70, 'netcoreapp31': 60, 'netstandard21': 50,
               'netstandard20': 40, 'netstandard16': 30, 'netstandard13': 20, '': 1}
    return aliases.get(name, -1)


for tree in trees:
    for node in tree.iter('PackageReference'):
        require(node.attrib['Include'], node.attrib['Version'])
while True:
    pending = [(name, release) for name, release in selected.items() if (name, release) not in archives]
    if not pending:
        break
    for name, release in pending:
        url = f'https://api.nuget.org/v3-flatcontainer/{name}/{release}/{name}.{release}.nupkg'
        data = urlopen(url, timeout=90).read()
        (packages_dir / f'{name}.{release}.nupkg').write_bytes(data)
        archive = zipfile.ZipFile(io.BytesIO(data))
        archives[(name, release)] = archive
        records.append({'name': name, 'version': release, 'url': url, 'sha256': hashlib.sha256(data).hexdigest()})
        spec = ET.fromstring(archive.read(next(p for p in archive.namelist() if p.endswith('.nuspec'))))
        groups = [node for node in spec.iter() if node.tag.endswith('}group')]
        supported = [node for node in groups if framework_rank(node.attrib.get('targetFramework', '')) >= 0]
        group = max(supported, key=lambda node: framework_rank(node.attrib.get('targetFramework', '')), default=None)
        dependencies = list(group) if group is not None else [node for node in spec.iter() if node.tag.endswith('}dependency')] if not groups else []
        if groups and not supported:
            raise RuntimeError('Unsupported dependency framework: ' + name)
        for node in dependencies:
            if node.tag.endswith('}dependency'):
                require(node.attrib['id'], node.attrib['version'])

package_generators = []
for name, release in sorted(selected.items()):
    archive = archives[(name, release)]
    libs = [p for p in archive.namelist() if re.fullmatch(r'lib/[^/]+/[^/]+\.dll', p)]
    frameworks = {p.split('/')[1] for p in libs if framework_rank(p.split('/')[1]) >= 0}
    best = max(frameworks, key=framework_rank, default=None)
    if libs and best is None:
        raise RuntimeError('Unsupported assembly framework: ' + name)
    for item in libs:
        if item.split('/')[1] == best:
            target = out / Path(item).name
            data = archive.read(item)
            if target.exists() and target.read_bytes() != data:
                raise RuntimeError('Conflicting dependency assembly: ' + target.name)
            if not target.exists():
                target.write_bytes(data)
    if name == 'microsoft.extensions.logging.abstractions':
        generator = 'analyzers/dotnet/roslyn4.4/cs/Microsoft.Extensions.Logging.Generators.dll'
        target = packages_dir / 'Microsoft.Extensions.Logging.Generators.dll'
        target.write_bytes(archive.read(generator))
        package_generators.append(target)

refs = {}
for pack in ['Microsoft.NETCore.App.Ref', 'Microsoft.AspNetCore.App.Ref']:
    for item in (sdk / f'packs/{pack}/8.0.29/ref/net8.0').glob('*.dll'):
        refs[item.name] = item
for item in out.glob('*.dll'):
    refs[item.name] = item
analyzers = sorted((sdk / 'packs/Microsoft.NETCore.App.Ref/8.0.29/analyzers/dotnet/cs').glob('*.dll')) + package_generators
common = ['System', 'System.Collections.Generic', 'System.IO', 'System.Linq', 'System.Net.Http', 'System.Threading', 'System.Threading.Tasks']
web = ['Microsoft.AspNetCore.Builder', 'Microsoft.AspNetCore.Hosting', 'Microsoft.AspNetCore.Http', 'Microsoft.AspNetCore.Routing', 'Microsoft.Extensions.Configuration', 'Microsoft.Extensions.DependencyInjection', 'Microsoft.Extensions.Hosting', 'Microsoft.Extensions.Logging']
compile_records = []
for path, tree in zip(projects, trees):
    name = path.stem
    using = out / (name + '.GlobalUsings.cs')
    using.write_text(''.join(f'global using {item};\n' for item in common + (web if name == 'RATools.Api' else [])))
    sources = sorted(p for p in path.parent.rglob('*.cs') if not {'bin', 'obj', 'App_Data'}.intersection(p.relative_to(path.parent).parts))
    arguments = ['-nologo', '-target:' + ('exe' if name == 'RATools.Api' else 'library'), '-langversion:12', '-nullable:enable', '-warnaserror+', '-nowarn:1701', '-define:NET8_0;NET;NETCOREAPP', f'-out:{out / (name + ".dll")}']
    arguments += [f'-r:{item}' for item in refs.values()]
    arguments += [f'-analyzer:{item}' for item in analyzers]
    for resource in tree.iter('EmbeddedResource'):
        file = (path.parent / resource.attrib['Include'].replace('\\', '/')).resolve()
        file.relative_to(root)
        arguments.append(f'-resource:{file},{resource.attrib["LogicalName"]}')
        if file.is_relative_to(root / 'reference'):
            dest = out / file.relative_to(root)
            dest.parent.mkdir(parents=True, exist_ok=True)
            if not dest.exists():
                shutil.copyfile(file, dest)
    arguments += [str(using), *map(str, sources)]
    response = out / (name + '.rsp')
    response.write_text('\n'.join(arguments))
    command = [str(sdk / 'dotnet'), str(sdk / f'sdk/{version}/Roslyn/bincore/csc.dll'), '@' + str(response)]
    with (results / (name + '.compile.log')).open('w') as log:
        result = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT)
    compile_records.append({'project': name, 'sources': len(sources), 'command': command, 'exitCode': result.returncode})
    (results / 'backend-toolchain.json').write_text(json.dumps({'sdk': version, 'packages': records, 'selected': selected, 'compilations': compile_records,
        'scope': 'direct csc with SDK source generators; not MSBuild, NuGet restore or Recommended analyzer parity'}, indent=2))
    if result.returncode:
        raise SystemExit(result.returncode)
    refs[name + '.dll'] = out / (name + '.dll')

startup = out / 'BrowserStartup.dll'
subprocess.run([str(sdk / 'dotnet'), str(sdk / f'sdk/{version}/Roslyn/bincore/csc.dll'), '-nologo', '-target:library', '-warnaserror+',
                f'-out:{startup}', *[f'-r:{item}' for item in refs.values()], str(root / 'scripts/remote-check/BrowserStartup.cs')], env=env, check=True)
runtime = {'runtimeOptions': {'tfm': 'net8.0', 'frameworks': [{'name': 'Microsoft.NETCore.App', 'version': '8.0.29'}, {'name': 'Microsoft.AspNetCore.App', 'version': '8.0.29'}]}}
(out / 'RATools.Api.runtimeconfig.json').write_text(json.dumps(runtime))
target = '.NETCoreApp,Version=v8.0'
(out / 'RATools.Api.deps.json').write_text(json.dumps({'runtimeTarget': {'name': target}, 'targets': {target: {'RATools.Api/1.0.0': {
    'runtime': {p.name: {} for p in out.glob('*.dll')}}}}, 'libraries': {'RATools.Api/1.0.0': {'type': 'project', 'serviceable': False, 'sha512': ''}}}))
probe = out / 'CaptureRecycleProbe.dll'
subprocess.run([str(sdk / 'dotnet'), str(sdk / f'sdk/{version}/Roslyn/bincore/csc.dll'), '-nologo', '-target:exe', '-langversion:12', '-nullable:enable', '-warnaserror+',
                f'-out:{probe}', *[f'-r:{item}' for item in refs.values()], str(root / 'scripts/remote-check/CaptureRecycleProbe.cs')], env=env, check=True)
(out / 'CaptureRecycleProbe.runtimeconfig.json').write_text(json.dumps(runtime))
print('Four current-source layers compiled for the bounded Linux browser host.')
