"""Turns Azgaar's full JSON export of the kingdom map into the compact file the Unity map builder reads.

    python Tools/export_kingdom_map.py [Design/Map/kingdom-strip.json]

Writes Assets/_Project/Data/Map/Kingdom Map.json: every cell's position, height, biome and water, the lakes'
water levels, the rivers as point lists, and the towns. Positions stay in Azgaar's map pixels (640 x 2048, y down);
the builder (Tools > Map > Build Kingdom Terrain) turns them into metres. Standard library only.
"""
import json, os, sys

PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
source = sys.argv[1] if len(sys.argv) > 1 else os.path.join(PROJECT, "Design", "Map", "kingdom-strip.json")
target = os.path.join(PROJECT, "Assets", "_Project", "Data", "Map", "Kingdom Map.json")

with open(source, encoding="utf-8") as f:
    data = json.load(f)
pack = data["pack"]
cells = pack["cells"]
features = pack["features"]

lakes, lake_index = [], {}
for feature in features:
    if feature and feature.get("type") == "lake":
        lake_index[feature["i"]] = len(lakes)
        lakes.append({"name": feature.get("name", ""), "level": round(feature.get("height", 20), 2)})

xs, ys, hs, biomes, water, lake = [], [], [], [], [], []
for cell in cells:
    xs.append(round(cell["p"][0], 2))
    ys.append(round(cell["p"][1], 2))
    hs.append(cell["h"])
    biomes.append(cell["biome"])
    feature = features[cell["f"]] if 0 < cell["f"] < len(features) else None
    kind = feature.get("type") if feature else None
    water.append(1 if kind == "ocean" else 2 if kind == "lake" else 0)
    lake.append(lake_index.get(cell["f"], -1))

rivers = []
for river in pack["rivers"]:
    points = []
    for c in river["cells"]:
        if 0 <= c < len(cells):
            points += [round(cells[c]["p"][0], 2), round(cells[c]["p"][1], 2)]
    if len(points) >= 4:
        rivers.append({"name": river.get("name", ""), "type": river.get("type", ""), "width": river.get("width", 0),
                       "sourceWidth": river.get("sourceWidth", 0), "discharge": river.get("discharge", 0),
                       "length": river.get("length", 0), "points": points})

towns = [{"name": b["name"], "x": round(b["x"], 2), "y": round(b["y"], 2), "capital": bool(b.get("capital")),
          "port": bool(b.get("port")), "population": round(b.get("population", 0), 2)}
         for b in pack["burgs"] if b and b.get("name") and not b.get("removed")]

out = {
    "source": "Azgaar's Fantasy Map Generator " + data["info"]["version"] + ", map seed " + data["info"]["seed"],
    "width": data["info"]["width"], "height": data["info"]["height"], "seaLevel": 20,
    "cells": {"x": xs, "y": ys, "h": hs, "biome": biomes, "water": water, "lake": lake},
    "biomeNames": [b["name"] for b in pack["biomes"]] if isinstance(pack["biomes"], list) else [],
    "lakes": lakes, "rivers": rivers, "towns": towns,
}
os.makedirs(os.path.dirname(target), exist_ok=True)
with open(target, "w", encoding="utf-8") as f:
    json.dump(out, f, separators=(",", ":"))
print(len(xs), "cells,", len(lakes), "lakes,", len(rivers), "rivers,", len(towns), "towns ->", target,
      os.path.getsize(target) // 1024, "KB")
