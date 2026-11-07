"""Install npm v3 lock archives without cleanup or lifecycle scripts.

Each run owns a fresh tree. Archives and failed attempts are retained. This is
not an npm-ci claim: packages requiring lifecycle setup must fail the real
build/test and be handled explicitly, never silently upgraded.
"""
import base64
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
from pathlib import Path, PurePosixPath
import platform
import tarfile
from urllib.parse import urlparse
from urllib.request import urlopen

root = Path.cwd()
lock = json.loads((root / "frontend/package-lock.json").read_text())
manifest = json.loads((root / "frontend/package.json").read_text())
if lock.get("lockfileVersion") != 3:
    raise SystemExit("Only the audited npm lockfile v3 layout is supported")
locked_root = lock.get("packages", {}).get("", {})
for field in ("dependencies", "devDependencies", "optionalDependencies", "peerDependencies",
              "peerDependenciesMeta", "engines", "name", "version"):
    if manifest.get(field) != locked_root.get(field):
        raise SystemExit("package.json and package-lock.json differ: " + field)
if platform.machine() != "x86_64" or platform.system() != "Linux":
    raise SystemExit("Dependency selection requires Linux x86_64")
if platform.libc_ver()[0] != "glibc":
    raise SystemExit("Dependency selection requires Linux glibc")
archives = root / "dependency-archives"
archives.mkdir()


def accepts(values, current):
    return not values or ("!" + current not in values and
                          (current in values or all(value.startswith("!") for value in values)))


def install(item):
    location, package = item
    if not location:
        return
    if not all(accepts(package.get(key), current) for key, current in
               (("os", "linux"), ("cpu", "x64"), ("libc", "glibc"))):
        if not package.get("optional"):
            raise ValueError("Required dependency has incompatible platform: " + location)
        return
    path = PurePosixPath(location)
    if not location.startswith("node_modules/") or path.is_absolute() or ".." in path.parts or "\\" in location:
        raise ValueError("Unsafe lockfile location")
    if package.get("link"):
        raise ValueError("Linked dependencies require separate review")
    url = package.get("resolved", "")
    parsed = urlparse(url)
    if parsed.scheme != "https" or parsed.hostname != "registry.npmjs.org" or parsed.username or parsed.password:
        raise ValueError("Only public npm registry archives are allowed: " + location)
    algorithm, encoded = package.get("integrity", "").split("-", 1)
    if algorithm not in ("sha512", "sha256"):
        raise ValueError("Unsupported lockfile integrity algorithm: " + location)
    digest = hashlib.new(algorithm)
    archive = archives / (hashlib.sha256(location.encode()).hexdigest() + ".tgz")
    with urlopen(url, timeout=120) as source, archive.open("xb") as target:
        while chunk := source.read(1024 * 1024):
            target.write(chunk)
            digest.update(chunk)
    if digest.digest() != base64.b64decode(encoded, validate=True):
        raise ValueError("Lockfile integrity mismatch: " + location)
    destination = root / "frontend" / location
    duplicate_entries = 0
    with tarfile.open(archive, "r:gz") as bundle:
        archive_root = None
        for member in bundle:
            parts = PurePosixPath(member.name).parts
            if (not parts or PurePosixPath(member.name).is_absolute() or ".." in parts or
                    "\\" in member.name or ":" in member.name or
                    (archive_root is not None and parts[0] != archive_root)):
                raise ValueError("Unsafe archive member: " + location)
            archive_root = parts[0]
            if not (member.isdir() or member.isfile()):
                raise ValueError("Archive links/devices require separate review: " + location)
            if len(parts) == 1 and not member.isdir():
                raise ValueError("Archive must have a directory root: " + location)
            output = destination.joinpath(*parts[1:])
            if member.isdir():
                output.mkdir(parents=True, exist_ok=True)
            else:
                output.parent.mkdir(parents=True, exist_ok=True)
                if output.exists():
                    # Some registry tarballs repeat a regular entry. Accept only
                    # byte-identical repeats; never overwrite an installed file.
                    with bundle.extractfile(member) as source, output.open("rb") as existing:
                        while chunk := source.read(1024 * 1024):
                            if chunk != existing.read(len(chunk)):
                                raise ValueError("Conflicting duplicate archive entry: " + location)
                        if existing.read(1):
                            raise ValueError("Conflicting duplicate archive length: " + location)
                    duplicate_entries += 1
                    continue
                with bundle.extractfile(member) as source, output.open("xb") as target:
                    while chunk := source.read(1024 * 1024):
                        target.write(chunk)
                output.chmod(0o755 if member.mode & 0o111 else 0o644)
    if not (destination / "package.json").is_file():
        raise ValueError("Archive has no package manifest: " + location)
    return {"path": location, "version": package["version"], "integrity": package["integrity"],
            "archiveRoot": archive_root, "identicalDuplicateEntries": duplicate_entries}


with ThreadPoolExecutor(max_workers=6) as executor:
    installed = [entry for entry in executor.map(install, lock["packages"].items()) if entry]
(root / "results/dependencies.json").write_text(json.dumps(installed, indent=2))
print(f"Installed {len(installed)} lockfile packages; integrity verified; lifecycle scripts not run")
