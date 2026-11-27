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
        # towns on the kingdom map: the whole pack (the user's call, 2026-11-27), demo scenes included as layout references
        r"re:^Assets/Synty/PolygonFantasyKingdom/",
        r"re:PolygonFantasyKingdom/Prefabs/Buildings/Presets/SM_Bld_Preset_.*_Optimized\.prefab$",
        r"re:PolygonFantasyKingdom/Prefabs/Environments/SM_Env_(Bridge_Stone_(01|Path_01)|Path_0\d|Path_Brick_Group_0\d|StoneWall_0\d|StoneWall_Pillar_01|Ground_Farm_(Row_0\d|Flat_01)|Ground_Flat_Dirt_0\d|Fountain_01|Bush_Flowers_0\d|Bush_Cluster_0\d|Flowers_0\d|Sunflower_0\d|Big_Tree_01|Tree_Large_0\d|Grass_WallEdge_0\d)\.prefab$",
        r"re:PolygonFantasyKingdom/Prefabs/Props/(.*/)?SM_Prop_(Market_(Stall_0\d|Tray_0\d)|Well_01|Well_Plinth_01|Street_Lamp_0\d|Barrel_01|Barrel_Stack_0\d|Crate_Stack_0\d|Crate_Wood_01|Sack_Stack_0\d|Sack_0\d|Hay_(Bale_01|Cart_01|Pile_0\d)|Wood_Pile_0\d|Fence_0\d|Fence_Gate_01|Direction_Sign_0\d|Boat_01|Statue_(Hero_01|Plinth_01)|Gravestone_0\d|Log_0\d|Ground_(Planks|Logs)_0\d|Wheat_Bunch_01|Windmill_0\d|Table_Wood_0[1-3]|Bread_Crate_01)\.prefab$",
        r"re:PolygonFantasyKingdom/Prefabs/Vehicles/SM_Veh_(Cart_0\d|TraderWagon_01|Wheelbarrow_01|Boat_Dinghy_01)\.prefab$",
    ],
    # icon candidates for the HUD mockup (unit types, cash, battle, buy and sell), rendered to images by an eval
    r"E:\Game Assets\Synty\POLYGON - Icons Pack": [
        r"re:PolygonIcons/Prefabs/SM_Icon_(Axe_0[12]|Shield_01|KnightHelmet_01|Armour_01|Crafting_Feather_01|Dagger_01|Sword_0[12]|Coin_0[1-3]|Coins_01|Arrow_0[1-5]|Arrow_Angled_01|Chevron_01|Crown_01|Banner_0[12]|Flag_01|Crafting_Bolt_01|Boots_01|Farm_01|Crop_01|Skull_01|Letter_0[12]|QuestionMark_01)\.prefab$",
    ],
    # every UI sound (the user's call, 2026-11-27): the HUD's clicks, ticks, coins and notices come from here
    os.path.join(os.environ.get("APPDATA", ""), r"Unity\Asset Store-5.x\Celeris Lab\AudioSound FX\The Complete UI Sound Effects Library.unitypackage"): [
        r"re:^Assets/CelerisLab/",
    ],
    # the kingdom map, from Synty's three nature biome packs. Entries starting "re:" are patterns matched against the
    # paths inside the package. Meadow Forest: the broadleaf woods, birches, orchards, meadow rocks and the grass,
    # dirt and rock ground layers. Alpine Mountain: pines, mountain rocks and cliffs, snow and rocky ground.
    # Swamp Marshland: swamp trees, reeds, lily pads and mud.
    r"E:\Game Assets\Synty\POLYGON - Meadow Forest - Nature Biome": [
        r"re:PNB_Meadow_Forest/Prefabs/SM_Env_(Tree_(Meadow|Birch|Fruit)_0\d|Bush_0\d|Grass_Bush_01|Rock_(0\d|Round_01|Small_01|Pile_0[1-4]|Cliff_0[1-3])|Lillies_0\d|Grass_(Tall|Med)_Clump_0[1-3]|Water_Plane_01)\.prefab$",
        r"re:PNB_Meadow_Forest/.*Terrain_Meadow_(Grass_01|Grass_02|Grass_Flowers_01|Grass_Leaves_01|Grass_Pebbles_01|Dirt_01|Mud_01|Rock_01|Rock_Moss_01|Moss_Rock_01|Gravel_01)\.terrainlayer$",
    ],
    r"E:\Game Assets\Synty\POLYGON - Alpine Mountain - Nature Biome": [
        r"re:PNB_Alpine_Mountain/Prefabs/SM_Env_(Pine_0\d|Pine_NoLeaves_01|Rock_0\d+|Rock_Cliff_0\d|Rock_Rough_0\d|Rock_River_0\d|Bush_0[12]|Bush_Flower_01|Moss_Lumps_0\d)\.prefab$",
        r"re:PNB_Alpine_Mountain/.*/(layer_snow_[^/]*|SnowCliff|Pine_Moss_01|Pine_Grass|Pine_Dirt|RiverRocks_01|Grass_01|Dirt_01)\.terrainlayer$",
    ],
    os.path.join(os.environ.get("APPDATA", ""), r"Unity\Asset Store-5.x\Synty Studios\3D ModelsEnvironmentsLandscapes\POLYGON - Swamp Marshland - Nature Biomes - 3D Environment Art by Synty.unitypackage"): [
        r"re:PNB_Swamp_Marshland/Prefabs/SM_Env_(Tree_Swamp_0\d|Tree_Mangrove_0\d|Tree_Dead_0[12]|Reeds_0\d|SwampGrass_(Group_0\d|Tall_01|Medium_01)|LillyPads_0\d|MossMound_0\d|Bush_Bramble_0\d|Swamp_Log_0[12])\.prefab$",
        r"re:PNB_Swamp_Marshland/.*/Terrain_(Mud_01|Grass_Swamp_01|Moss_01|Mud_Dry_01)\.terrainlayer$",
    ],
    # the cavalry's horses: Omabuarts' zebra, recoloured as a bay horse (Tools > Battle > Build Horse Coat). This pack
    # is already imported whole into Assets/Quirky Series here, so the job only matters for a fresh checkout.
    os.path.join(os.environ.get("APPDATA", ""), r"Unity\Asset Store-5.x\Omabuarts Studio\3D ModelsCharactersAnimals\Quirky Series - Animals Mega Pack Vol 1.unitypackage"): [
        "Assets/Quirky Series/Ultimate Pack Vol.1/Mega Pack Vol.1/Safari Vol.1/Prefabs/Single LODs/Zebra_LOD0.prefab",
        "Assets/Quirky Series/Ultimate Pack Vol.1/Mega Pack Vol.1/Safari Vol.1/Animations/Zebra_Animations.fbx",
        "Assets/Quirky Series/Ultimate Pack Vol.1/Mega Pack Vol.1/Safari Vol.1/Animations/AC_Zebra.controller",
    ],
    # battle sounds (the user's call, 2026-11-27): FS Melee's sword clashes, hits and slams, and Feel's demo grunts
    os.path.join(os.environ.get("APPDATA", ""), r"Unity\Asset Store-5.x\Fantacode Studios\Editor ExtensionsGame Toolkits\FS - Melee Combat System.unitypackage"): [
        r"re:^Assets/Fantacode Studios/Melee Combat System/Sfx/(sword clash|sword clash 2|sword-hit-flesh|hit|hit 2|hit 3|hit blocked|scream|Slam|Fall and Take Hit)\.wav$",
    ],
    os.path.join(os.environ.get("APPDATA", ""), r"Unity\Asset Store-5.x\More Mountains\ScriptingEffects\Feel.unitypackage"): [
        r"re:^Assets/Feel/FeelDemos/Barbarians/Sounds/FeelBarbarian(sGrunt\d|Hit)\.wav$",
    ],
}
# packs already partly imported somewhere else; new files from them go there too
REMAP = {
    # "Assets/Synty/PolygonWesternFrontier/": "Assets/ThirdParty/Synty/PolygonWesternFrontier/",
    # the repository is public: everything from Synty stays in the git-ignored ThirdParty folder
    "Assets/Synty/": "Assets/_Project/ThirdParty/Synty/",
    "Assets/Quirky Series/": "Assets/_Project/ThirdParty/Quirky Series/",
    "Assets/CelerisLab/": "Assets/_Project/ThirdParty/CelerisLab/",
    "Assets/Fantacode Studios/Melee Combat System/Sfx/": "Assets/_Project/ThirdParty/Audio/FS Melee/",
    "Assets/Feel/FeelDemos/Barbarians/Sounds/": "Assets/_Project/ThirdParty/Audio/Feel Barbarians/",
}
# python Tools/extract_props.py icons: only the jobs whose pack name contains that word, to skip re-reading the rest
if len(sys.argv) > 1:
    JOBS = {pack: paths for pack, paths in JOBS.items() if sys.argv[1].lower() in pack.lower()}
GUID = re.compile(rb"guid: ([0-9a-f]{32})")

def find_package(hint, root):
    if os.path.isfile(hint):
        return hint
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
    for r in roots:
        if r.startswith("re:"):
            matched = [g for p, g in by_path.items() if re.search(r[3:], p)]
            print("  pattern", r[3:60], "matched", len(matched))
            todo += matched
    print("  roots found:", len(todo), "from", len(roots), "entries")
    wanted = set()
    while todo:
        guid = todo.pop()
        if guid in wanted or guid not in entries:
            continue
        wanted.add(guid)
        data = entries[guid]
        texts = [data.get("asset.meta", b"")]
        if paths[guid].endswith((".prefab", ".mat", ".terrainlayer", ".asset", ".shadergraph", ".shadersubgraph", ".controller")):
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
