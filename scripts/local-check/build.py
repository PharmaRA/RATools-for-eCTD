"""Current-source Windows checks without MSBuild cleanup or artifact deletion."""
import hashlib
import json
import os
import re
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[2]
evidence = Path(sys.argv[1]).resolve()
evidence.relative_to(root / '.artifacts/local-check')
out = evidence / 'backend'; out.mkdir()
sdk = root / '.artifacts/tools/dotnet-8.0.423'
dotnet = sdk / 'dotnet.exe'
version = '8.0.423'; runtime_version = '8.0.29'
env = {**os.environ, 'DOTNET_ROOT': str(sdk), 'DOTNET_NOLOGO': '1', 'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'PYTHONDONTWRITEBYTECODE': '1'}
summary = {'task': sys.argv[2], 'sourceHead': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip(),
           'steps': [], 'exitCode': 1, 'category': 'build-failed',
           'scope': 'current-source direct csc with .NET8 Recommended analyzers; not full MSBuild/test-suite parity'}
inputs = {}
dependency_hashes = {}
common = ['System', 'System.Collections.Generic', 'System.IO', 'System.Linq', 'System.Net.Http', 'System.Threading', 'System.Threading.Tasks']
web = ['Microsoft.AspNetCore.Builder', 'Microsoft.AspNetCore.Hosting', 'Microsoft.AspNetCore.Http', 'Microsoft.AspNetCore.Routing',
       'Microsoft.Extensions.Configuration', 'Microsoft.Extensions.DependencyInjection', 'Microsoft.Extensions.Hosting', 'Microsoft.Extensions.Logging']


def capture(path):
    inputs[str(path.relative_to(root)).replace('\\', '/')] = hashlib.sha256(path.read_bytes()).hexdigest()


def package_file(assets, key, relative):
    library = assets['libraries'][key]
    for folder in assets['packageFolders']:
        path = Path(folder) / library['path'] / relative
        if path.is_file(): return path
    raise FileNotFoundError('Required cached dependency is absent: ' + key + '/' + relative)


def dependencies(project):
    path = project.parent / 'obj/project.assets.json'
    capture(path)
    assets = json.loads(path.read_text(encoding='utf-8-sig'))
    target = assets['targets']['net8.0']
    tree = ET.parse(project)
    for declaration in tree.iter('PackageReference'):
        if declaration.attrib['Include'] + '/' + declaration.attrib['Version'] not in target:
            raise RuntimeError('Restore assets do not match current project dependency: ' + declaration.attrib['Include'])
    references = {}; generators = {}
    for key, item in target.items():
        if item['type'] != 'package': continue
        for relative in item.get('compile', {}):
            if relative.endswith('.dll'):
                path = package_file(assets, key, relative); references[path.name] = path
        for relative in item.get('runtime', {}):
            if relative.endswith('.dll'):
                path = package_file(assets, key, relative)
                destination = out / path.name
                data = path.read_bytes()
                # Later projects can resolve a more recent transitive package. Keep
                # each layer's compiler references exact; retain distinct runtime versions.
                if destination.exists() and destination.read_bytes() != data:
                    raise RuntimeError('Conflicting runtime assembly versions: ' + path.name)
                if not destination.exists(): destination.write_bytes(data)
        for relative in assets['libraries'][key].get('files', []):
            if relative.endswith('.dll') and not relative.endswith('.resources.dll') and '/roslyn4.4/cs/' in relative:
                path = package_file(assets, key, relative); generators[path.name] = path
    return tree, references, list(generators.values())


def compile_sources(name, sources, references, generators, tree=None, executable=False, extra_usings=(), defines=()):
    generated = out / (name + '.GlobalUsings.cs')
    product_version = (root / 'VERSION').read_text().strip()
    if not re.fullmatch(r'\d+\.\d+\.\d+', product_version): raise RuntimeError('Review changed assembly version format')
    generated.write_text(''.join('global using ' + item + ';\n' for item in [*common, *extra_usings]) +
                         '[assembly: System.Reflection.AssemblyVersion("' + product_version + '.0")]\n')
    arguments = ['-nologo', '-target:' + ('exe' if executable else 'library'), '-langversion:12', '-nullable:enable',
                 '-warnaserror+', '-nowarn:1701', '-define:' + ';'.join(['NET8_0', 'NET', 'NETCOREAPP', *defines]), '-out:' + str(out / (name + '.dll'))]
    arguments += ['-r:' + str(path) for path in references.values()]
    for path in references.values(): dependency_hashes[name + '/reference/' + path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    analyzer_root = sdk / ('sdk/' + version + '/Sdks/Microsoft.NET.Sdk/analyzers')
    analyzers = [analyzer_root / name for name in ['Microsoft.CodeAnalysis.NetAnalyzers.dll', 'Microsoft.CodeAnalysis.CSharp.NetAnalyzers.dll']]
    analyzers += list((sdk / ('packs/Microsoft.NETCore.App.Ref/' + runtime_version + '/analyzers/dotnet/cs')).glob('*.dll'))
    arguments += ['-analyzer:' + str(path) for path in [*analyzers, *generators]]
    for path in [*analyzers, *generators]: dependency_hashes[name + '/analyzer/' + path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    arguments += ['-analyzerconfig:' + str(analyzer_root / 'build/config/analysislevel_8_recommended.globalconfig'), '-analyzerconfig:' + str(root / '.editorconfig')]
    if tree is not None:
        project = root / 'src' / name / (name + '.csproj')
        for resource in tree.iter('EmbeddedResource'):
            path = (project.parent / resource.attrib['Include'].replace('\\', '/')).resolve()
            path.relative_to(root); capture(path)
            arguments.append('-resource:' + str(path) + ',' + resource.attrib['LogicalName'])
            if path.is_relative_to(root / 'reference'):
                destination = out / path.relative_to(root)
                destination.parent.mkdir(parents=True, exist_ok=True)
                if not destination.exists(): destination.write_bytes(path.read_bytes())
    for source in sources: capture(source)
    arguments += [str(generated), *map(str, sources)]
    response = out / (name + '.rsp')
    response.write_text('\n'.join('"' + argument.replace('"', '\\"') + '"' for argument in arguments), encoding='utf-8')
    with (evidence / (name + '.compile.log')).open('w', encoding='utf-8') as log:
        result = subprocess.run([str(dotnet), str(sdk / ('sdk/' + version + '/Roslyn/bincore/csc.dll')), '@' + str(response)],
                                cwd=root, env=env, stdout=log, stderr=subprocess.STDOUT)
    summary['steps'].append({'name': name, 'sources': len(sources), 'exitCode': result.returncode, 'log': name + '.compile.log'})
    print(name + ': exit ' + str(result.returncode), flush=True)
    if result.returncode: raise RuntimeError(name + ' failed; see compiler log')


try:
    if os.name != 'nt': raise RuntimeError('This entry qualifies Windows only')
    policy = json.loads((root / 'global.json').read_text())['sdk']
    if policy != {'version': '8.0.400', 'rollForward': 'latestFeature', 'allowPrerelease': False}:
        raise RuntimeError('Review the changed SDK policy')
    if subprocess.check_output([str(dotnet), '--version'], env=env, cwd=root, text=True).strip() != version:
        raise RuntimeError('The pinned local SDK is unavailable')
    for name in ['global.json', 'Directory.Build.props', '.editorconfig', 'VERSION', 'scripts/local-check.ps1', 'scripts/local-check/build.py', 'scripts/local-check/SelectedTests.cs']: capture(root / name)
    dependency_hashes['compiler/csc.dll'] = hashlib.sha256((sdk / ('sdk/' + version + '/Roslyn/bincore/csc.dll')).read_bytes()).hexdigest()
    frameworks = {}
    for pack in ['Microsoft.NETCore.App.Ref', 'Microsoft.AspNetCore.App.Ref']:
        for file in (sdk / 'packs' / pack / runtime_version / 'ref/net8.0').glob('*.dll'): frameworks[file.name] = file
    layers = {}
    for layer in ['Domain', 'Application', 'Infrastructure', 'Api']:
        name = 'RATools.' + layer; project = root / 'src' / name / (name + '.csproj'); capture(project)
        tree, packages, generators = dependencies(project)
        sources = sorted(path for path in project.parent.rglob('*.cs') if not {'bin', 'obj', 'App_Data'}.intersection(path.relative_to(project.parent).parts))
        compile_sources(name, sources, {**frameworks, **packages, **layers}, generators, tree, executable=layer == 'Api', extra_usings=web if layer == 'Api' else [])
        layers[name + '.dll'] = out / (name + '.dll')
    runtime = {'runtimeOptions': {'tfm': 'net8.0', 'frameworks': [{'name': 'Microsoft.NETCore.App', 'version': runtime_version}, {'name': 'Microsoft.AspNetCore.App', 'version': runtime_version}]}}
    if sys.argv[2] in {'input-api', 'input-browser'}:
        compile_sources('InputApiStartup', [root / 'scripts/local-check/InputApiStartup.cs'], {**frameworks, **layers}, [], executable=False)
    (out / 'RATools.Api.runtimeconfig.json').write_text(json.dumps(runtime))
    target = '.NETCoreApp,Version=v8.0'
    (out / 'RATools.Api.deps.json').write_text(json.dumps({'runtimeTarget': {'name': target}, 'targets': {target: {'RATools.Api/0.1.0': {
        'runtime': {path.name: {} for path in out.glob('*.dll')}}}}, 'libraries': {'RATools.Api/0.1.0': {'type': 'project', 'serviceable': False, 'sha512': ''}}}))
    if sys.argv[2] == 'input-api':
        driver = root / 'scripts/local-check/input-api.py'; capture(driver)
        summary['category'] = 'api-test-failed'
        result = subprocess.run([sys.executable, str(driver), str(evidence)], cwd=root, env=env)
        summary['steps'].append({'name': 'input-api', 'exitCode': result.returncode})
        if result.returncode: raise RuntimeError('External ZIP API checks failed; see input-api.json/log')
    if sys.argv[2] == 'input-browser':
        for name in ['browser-build.mjs','browser-fixture.py','browser-guard.ps1','BrowserJob.cs','browser-session.mjs','input-browser.mjs']:
            capture(root / 'scripts/local-check' / name)
        capture(root / 'scripts/remote-check/no-delete.mjs')
        summary['category'] = 'browser-test-failed'
        commands = [
            ('frontend-build', ['node', '--import', (root / 'scripts/remote-check/no-delete.mjs').as_uri(), str(root / 'scripts/local-check/browser-build.mjs'), str(evidence)]),
            ('browser-fixture', [sys.executable, str(root / 'scripts/local-check/browser-fixture.py'), str(evidence)]),
            ('input-browser', ['pwsh', '-NoProfile', '-File', str(root / 'scripts/local-check/browser-guard.ps1'), '-Evidence', str(evidence), '-Driver', 'input-browser.mjs'])]
        for name, command in commands:
            with (evidence / (name + '.log')).open('w', encoding='utf-8') as log:
                result = subprocess.run(command, cwd=root, env=env, stdout=log, stderr=subprocess.STDOUT)
            summary['steps'].append({'name': name, 'exitCode': result.returncode, 'log': name + '.log'})
            print(name + ': exit ' + str(result.returncode), flush=True)
            if result.returncode: raise RuntimeError(name + ' failed; see its log')
    if sys.argv[2] in {'pdf', 'capture', 'upload', 'multipart', 'report'}:
        backend = out
        out = evidence / 'tests'; out.mkdir()
        for path in layers.values(): (out / path.name).write_bytes(path.read_bytes())
        for path in (backend / 'reference').rglob('*'):
            if path.is_file():
                destination = out / path.relative_to(backend)
                destination.parent.mkdir(parents=True, exist_ok=True)
                destination.write_bytes(path.read_bytes())
        project = root / 'tests/RATools.Tests/RATools.Tests.csproj'; capture(project)
        _, packages, generators = dependencies(project)
        if sys.argv[2] == 'pdf':
            names = ['QpdfConcurrencyTests', 'QpdfProcessingEngineTests', 'QpdfNavigationIntegrityTests', 'PdfContentResourceTests', 'PdfContentWorkflowTests']
            directory = 'DocumentProcessing'
        elif sys.argv[2] == 'capture':
            names = ['ReleaseCaptureServiceTests', 'ReleaseCaptureBindingIntegrationTests', 'RegionalMetadataCaptureTests', 'FinalizationServiceTests', 'ReleaseDeliveryPackageTests', 'ReleaseCaptureTests']
            directory = 'Finalization'
        elif sys.argv[2] == 'report':
            names = ['PublishReportStoreTests', 'PublishReportAtomicityTests']
            directory = 'Publishing'
        else:
            names = ['MultipartRetentionTests'] if sys.argv[2] == 'multipart' else ['PackageUploadRetentionTests']
            directory = 'PackageValidation'
        sources = [root / 'tests/RATools.Tests' / directory / (name + '.cs') for name in names]
        sources.append(root / 'scripts/local-check/SelectedTests.cs')
        compile_sources('SelectedTests', sources, {**frameworks, **packages, **layers}, generators, executable=True, extra_usings=['Xunit'], defines=[sys.argv[2].upper()] if sys.argv[2] != 'pdf' else [])
        (out / 'SelectedTests.runtimeconfig.json').write_text(json.dumps(runtime))
        summary['category'] = 'test-failed'
        with (evidence / 'tests.log').open('w', encoding='utf-8') as log:
            result = subprocess.run([str(dotnet), str(out / 'SelectedTests.dll'), str(evidence / 'tests.json')], cwd=root, env=env,
                                    stdout=log, stderr=subprocess.STDOUT)
        summary['steps'].append({'name': sys.argv[2] + '-tests', 'exitCode': result.returncode, 'log': 'tests.log'})
        print(sys.argv[2] + ' tests: exit ' + str(result.returncode), flush=True)
        if result.returncode: raise RuntimeError('Selected tests failed; see tests.log')
        summary['tests'] = {key: value for key, value in json.loads((evidence / 'tests.json').read_text()).items() if key != 'cases'}
    summary.update(exitCode=0, category='passed', sdk=version, runtime=runtime_version)
except Exception as error:
    summary['error'] = str(error)
    print(str(error), flush=True)
finally:
    (evidence / 'source-manifest.json').write_text(json.dumps(inputs, indent=2))
    (evidence / 'dependency-hashes.json').write_text(json.dumps(dependency_hashes, indent=2))
    summary['sourceManifestSha256'] = hashlib.sha256((evidence / 'source-manifest.json').read_bytes()).hexdigest()
    (evidence / 'summary.json').write_text(json.dumps(summary, indent=2))
sys.exit(summary['exitCode'])
