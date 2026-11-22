"""Pulls chosen files, and everything they depend on, out of Synty .unitypackage files into the project.

Edit JOBS (package folder -> asset paths inside it) first, then: python Tools/extract_props.py
Files whose GUID is already in the project are skipped, and REMAP sends packs that already live elsewhere in
the project there. Unity imports the new files when it next refreshes.
"""
import os, re, sys, tarfile

# the project this script sits in (Tools/ is one level below its root)
PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
JOBS = {
    # package file or folder -> asset paths inside it; everything they reference comes along, e.g.
    # r"E:\Game Assets\Synty\POLYGON - Icons Pack": ["Assets/Synty/PolygonIcons/Prefabs/SM_Icon_Gem_01.prefab"],
}
# packs already partly imported somewhere else; new files from them go there too
REMAP = {
    # "Assets/Synty/PolygonWesternFrontier/": "Assets/ThirdParty/Synty/PolygonWesternFrontier/",
}
# python Tools/extract_props.py icons: only the jobs whose pack name contains that word, to skip re-reading the rest
if len(sys.argv) > 1:
    JOBS = {pack: paths for pack, paths in JOBS.items() if sys.argv[1].lower() in pack.lower()}
GUID = re.compile(rb"guid: ([0-9a-f]{32})")

def find_package(hint, root):
    target = os.path.basename(hint).lower().replace("polygon - ", "").split()[0]
    for folder, _, files in os.walk(os.path.dirname(hint) if os.path.isdir(hint) is False else hint):
        for f in files:
            if f.endswith(".unitypackage"):
                return os.path.join(folder, f)
    for folder, _, files in os.walk(r"E:\Game Assets\Synty"):
        for f in files:
            if f.endswith(".unitypackage") and root.split("/")[2].replace("Polygon", "").lower() in f.lower().replace("_", ""):
                return os.path.join(folder, f)
    return None

# files already in the project, by guid, so nothing is imported twice under another path
existing = set()
for folder, _, files in os.walk(os.path.join(PROJECT, "Assets")):
    for f in files:
        if f.endswith(".meta"):
            with open(os.path.join(folder, f), "rb") as meta:
                found = GUID.search(meta.read(400))
                if found:
                    existing.add(found.group(1).decode())

for hint, roots in JOBS.items():
    package = find_package(hint, roots[0])
    print("package:", package)
    if package is None:
        continue
    entries = {}
    with tarfile.open(package, "r:gz") as tar:
        for member in tar:
            if not member.isfile():
                continue
            parts = member.name.lstrip("./").split("/")
            if len(parts) == 2 and parts[1] in ("asset", "asset.meta", "pathname"):
                entries.setdefault(parts[0], {})[parts[1]] = tar.extractfile(member).read()
    paths = {g: e["pathname"].decode("utf-8").split("\n")[0].strip() for g, e in entries.items() if "pathname" in e}
    by_path = {p: g for g, p in paths.items()}
    todo = [by_path[r] for r in roots if r in by_path]
    print("  roots found:", len(todo), "of", len(roots))
    wanted = set()
    while todo:
        guid = todo.pop()
        if guid in wanted or guid not in entries:
            continue
        wanted.add(guid)
        data = entries[guid]
        texts = [data.get("asset.meta", b"")]
        if paths[guid].endswith((".prefab", ".mat")):
            texts.append(data.get("asset", b""))
        for text in texts:
            for found in GUID.findall(text):
                found = found.decode()
                if found != guid:
                    todo.append(found)
    for guid in sorted(wanted):
        path = paths[guid]
        data = entries[guid]
        if "asset" not in data:
            continue
        if guid in existing:
            continue
        for prefix, moved in REMAP.items():
            if path.startswith(prefix):
                path = moved + path[len(prefix):]
        target = os.path.join(PROJECT, path.replace("/", os.sep))
        if os.path.exists(target):
            print("  exists", path)
            continue
        os.makedirs(os.path.dirname(target), exist_ok=True)
        with open(target, "wb") as f:
            f.write(data["asset"])
        if "asset.meta" in data:
            with open(target + ".meta", "wb") as f:
                f.write(data["asset.meta"])
        print("  ", len(data["asset"]) // 1024, "KB", path)
