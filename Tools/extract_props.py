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
    # battle test scene: soldiers, a few weapons, banners and field dressing
    r"E:\Game Assets\Synty\POLYGON - Fantasy Kingdom Pack": [
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Characters/SM_Chr_Soldier_Male_01.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Characters/SM_Chr_Soldier_Female_01.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Characters/SM_Chr_Peasant_Male_01.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Characters/SM_Chr_Peasant_Female_01.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Characters/SM_Chr_Rider_01.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Characters/Attachments/SM_Chr_Attach_Soldier_01.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Weapons/SM_Wep_Spear_01.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Weapons/SM_Wep_Shield_01.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Weapons/SM_Wep_Sword_01.prefab",
        # archers: bow in the left hand, quiver on the back, and the arrows they shoot
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Weapons/SM_Prop_Bow_01.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Weapons/SM_Prop_Quiver_01.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Weapons/SM_Prop_Arrow_01.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Props/Banners/SM_Prop_Battle_Banner_01.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Props/Banners/SM_Prop_Battle_Banner_02.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Props/Banners/SM_Prop_Battle_Banner_03.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Environments/SM_Env_Rock_01.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Environments/SM_Env_Rock_02.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Environments/SM_Env_Grass_Tuft_01.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Environments/SM_Env_Grass_Tuft_02.prefab",
        # terrain dressing: forest, marsh, hill
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Environments/SM_Env_Tree_Round_01.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Environments/SM_Env_Tree_Round_02.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Environments/SM_Env_Tree_Thin_01.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Environments/SM_Env_Tree_Thin_02.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Environments/SM_Env_Bush_01.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Environments/SM_Env_Bush_02.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Environments/SM_Env_Bush_Small_01.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Environments/SM_Env_Reeds_01.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Environments/SM_Env_Reeds_02.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Environments/SM_Env_Reeds_03.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Environments/SM_Env_Lily_01.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Environments/SM_Env_Rock_03.prefab",
        "Assets/Synty/PolygonFantasyKingdom/Prefabs/Environments/SM_Env_Rock_04.prefab",
    ],
    # the cavalry's horses: Omabuarts' zebra, recoloured as a bay horse (Tools > Battle > Build Horse Coat). This pack
    # is already imported whole into Assets/Quirky Series here, so the job only matters for a fresh checkout.
    os.path.join(os.environ.get("APPDATA", ""), r"Unity\Asset Store-5.x\Omabuarts Studio\3D ModelsCharactersAnimals\Quirky Series - Animals Mega Pack Vol 1.unitypackage"): [
        "Assets/Quirky Series/Ultimate Pack Vol.1/Mega Pack Vol.1/Safari Vol.1/Prefabs/Single LODs/Zebra_LOD0.prefab",
        "Assets/Quirky Series/Ultimate Pack Vol.1/Mega Pack Vol.1/Safari Vol.1/Animations/Zebra_Animations.fbx",
        "Assets/Quirky Series/Ultimate Pack Vol.1/Mega Pack Vol.1/Safari Vol.1/Animations/AC_Zebra.controller",
    ],
}
# packs already partly imported somewhere else; new files from them go there too
REMAP = {
    # "Assets/Synty/PolygonWesternFrontier/": "Assets/ThirdParty/Synty/PolygonWesternFrontier/",
    # the repository is public: everything from Synty stays in the git-ignored ThirdParty folder
    "Assets/Synty/": "Assets/_Project/ThirdParty/Synty/",
    "Assets/Quirky Series/": "Assets/_Project/ThirdParty/Quirky Series/",
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
