# How the candidates were made

Screenshots only, taken in the browser on 2026-11-26. Nothing has been exported from either site yet.
Each image is 1280 x 4096: the 640 x 2048 map drawn at double pixel density.

## A and B: Azgaar's Fantasy Map Generator v1.158.0

https://azgaar.github.io/Fantasy-Map-Generator/

These maps can't be rebuilt from a URL. Rebuild one in the generator like this:

1. Options > Advanced: map size 640 x 2048.
2. Options: map seed 514512085, points 10K, cultures 13 (European set), states 3, burgs 80, religions 5.
3. Options > Climate: precipitation 130 %. Leave the rest at their defaults (equator 28 °C, north pole -14 °C,
   south pole -18 °C, map size 3 %, latitude 25, longitude 50).
4. Tools > Heightmap > Erase > Template Editor. Add the steps below, set the template's Seed field, press Run,
   then Exit Customization.

The heights match exactly when rebuilt. Towns, names and borders should match because they come from the map seed,
but only a saved `.map` file is certain to.

Steps are written as type, count, height, x %, y %.

**Template A, "strip" (candidate B used template seed 1002):**

| Step | Count | Height | X | Y |
|---|---|---|---|---|
| Add | 15 | | | all cells |
| Hill | 8-10 | 15-25 | 0-100 | 5-80 |
| Hill | 3-4 | 60-80 | 10-90 | 0-6 |
| Range | 2-3 | 40-60 | 10-90 | 0-15 |
| Hill | 2-3 | 20-30 | 15-85 | 30-55 |
| Trough | 3-4 | 10-20 | 20-80 | 15-85 |
| Pit | 1-2 | 10-20 | 20-80 | 60-75 |
| Smooth | 2 | | | |

**Template B, "river valley" (candidate A used template seed 2002):**

| Step | Count | Height | X | Y |
|---|---|---|---|---|
| Add | 15 | | | all cells |
| Hill | 8-10 | 15-25 | 0-100 | 5-85 |
| Hill | 3-4 | 60-80 | 10-90 | 0-6 |
| Range | 1-2 | 35-50 | 10-90 | 40-50 |
| Hill | 2 | 15-25 | 15-85 | 20-35 |
| Trough | 2 | 15-25 | 45-55 | 10-90 |
| Pit | 2-3 | 10-15 | 20-80 | 60-75 |
| Smooth | 2 | | | |

Other candidates looked at: template A with seeds 1001, 1003, 1004 and 1005, and template B with seed 2001.

The numbered black dots (places) and red dots (battlefields) were drawn by me as a temporary overlay in the
browser. They aren't part of Azgaar's map.

## C: Watabou's Perilous Shores

https://watabou.github.io/perilous-shores/?seed=2101&tags=coast,civilized,safe,lawful&w=700&h=2240

The URL rebuilds the land, forests, rivers and towns, but **the names change on every load**. The same seed first
gave Frostfall, Gola's Fens and Blackwood, and then bloomcaster, Raven Fens and Ironwoods. Only an exported file
keeps the names.

Other seeds looked at: 1001 (lake), 2102 to 2110 with coast, land and bay shapes and the lowland, wetland and
woodland tags.
