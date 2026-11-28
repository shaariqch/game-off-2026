# game-off-2026

An entry for [Game Off 2026](https://itch.io/jam/game-off-2026), GitHub's annual month-long game jam.
The theme is **SHIFT**.

The game is still being designed, so this README will grow: title, how to play, and a link to the playable build on
itch.io will go here.

## Status

Project set up on 22 November 2026. Nothing playable yet.

## Built with

- Unity 6000.7.0b10 (Universal Render Pipeline, Input System, Cinemachine)
- Targets: WebGL (played in the browser on itch.io) and Windows

## What is and isn't in this repository

Game Off asks for the source to be public, and allows purchased or proprietary assets to be left out. This
repository follows that:

**Included**

- All of the game's own code, scenes, prefabs, settings and other original work: `Assets/_Project`, `Assets/Scenes`,
  `Assets/Settings`, `Assets/WebGLTemplates`
- Project configuration: `Packages`, `ProjectSettings`
- Build and helper scripts: `Tools`

**Not included**

Paid Unity Asset Store packages and anything made from them. Their licences don't allow redistribution, so they are
kept out with `.gitignore` and were never committed. They are present on my machine and in the playable build.

Editor tools and code libraries (needed to compile if the game's code uses them):

| Package | Author | Folder it imports to |
|---|---|---|
| Asset Inventory 4 | Impossible Robert | `Assets/AssetInventory` |
| Odin Inspector and Serializer | Sirenix | `Assets/Plugins/Sirenix` |
| DOTween | Demigiant | `Assets/Plugins/Demigiant` |
| Easy Character Movement 2 | Oscar Gracin | `Assets/ECM2` |
| Shapes | Freya Holmér | `Assets/Shapes` |
| Spline Architect | Mike Danielsson | `Assets/SplineArchitect` |
| Sound Shapes | TelePresent Games | `Assets/TelePresent` |
| vFolders 2, vFavorites 2 | kubacho lab | `Assets/vFolders`, `Assets/vFavorites` |
| Flat Kit: Toon Shading and Water | Dustyroom | `Assets/FlatKit` |
| Linework: Outlines and Edge Detection | Alexander Ameye | `Packages/dev.ameye.linework` |
| Feel | More Mountains | `Assets/Feel` (the demo folders aren't needed) |
| Damage Numbers Pro | Ekincan Tas | `Assets/DamageNumbersPro` (its `Demo/Prefabs` are used) |
| COZY: Stylized Weather 3 | Distant Lands | `Packages/com.distantlands.cozy.core` (an embedded package, with its URP integration unpacked over it) |

Damage Numbers Pro also needs TextMeshPro's essential resources (`Window > TextMeshPro > Import TMP Essential
Resources`), which aren't in the repository either.

Art, audio and other content packs used by the game will be listed under "Third-party assets" below as they are
added.

## Opening the project

1. Clone the repository and open the folder with Unity 6000.7.0b10 (or newer) through Unity Hub.
2. Unity downloads the registry packages listed in `Packages/manifest.json` by itself.
3. Without the packages above, scenes open with missing meshes, materials or scripts wherever the game uses them,
   and code that calls into a missing library won't compile. If you own a package, import it from the Asset Store
   into its usual folder and those references resolve, because Unity matches assets by their GUIDs.

The code is here to be read; the playable game is the build on itch.io.

## Third-party assets

| Pack | Author | Used for | Licence | In this repository |
|---|---|---|---|---|
| POLYGON - Fantasy Kingdom Pack | Synty Studios | Soldiers, weapons, banners and field props; the towns on the kingdom map (buildings, bridge, market, farm and street props) | Synty Store licence | No |
| Human Mega Animations Pack | Kevin Iglesias | Every soldier's and rider's animation: idles, walk and run, sword, spear and bow attacks, hit reactions, knockdowns and deaths (clips only) | Unity Asset Store EULA | No |
| Quirky Series - Animals Mega Pack Vol.1 | Omabuarts Studio | The cavalry's horses: the zebra, recoloured as a bay horse, with its own animations | Unity Asset Store EULA | No |
| POLYGON - Meadow Forest - Nature Biome | Synty Studios | The kingdom map: broadleaf and fruit trees, birches, bushes, grass, gravel and rock ground layers | Synty Store licence | No |
| POLYGON - Alpine Mountain - Nature Biome | Synty Studios | The kingdom map: pines, mountain rocks and shrubs, snow and moss ground layers | Synty Store licence | No |
| POLYGON - Swamp Marshland - Nature Biomes | Synty Studios | The kingdom map: swamp trees, mangroves, reeds, marsh grass, mud and swamp ground layers | Unity Asset Store EULA | No |
| Flat Kit: Toon Shading and Water | Dustyroom | The kingdom map's look: water, cel-shaded land, trees and buildings (Stylized Surface and terrain shaders), and its fog and outline effects | Unity Asset Store EULA | No |
| POLYGON - Icons Pack | Synty Studios | The Trade HUD's icons (units, cash, battle), rendered from its 3D icons | Synty Store licence | No |
| COZY: Stylized Weather 3 | Distant Lands | The kingdom map's time of day, sun, moon and sky light, fog and weather | Unity Asset Store EULA | No |
| The Complete UI Sound Effects Library | Celeris Lab | The Trade HUD's sounds (key presses, counting, trades, notices) | Unity Asset Store EULA | No |
| Feel | More Mountains | Battle feel (slow motion, freeze frames, squash springs) and the Trade HUD's key feedback | Unity Asset Store EULA | No |
| Cinzel | Natanael Gama | HUD headings, unit names and key labels | SIL Open Font License 1.1 | Yes, `Assets/_Project/Fonts` |
| Alegreya Sans | Juan Pablo del Peral (Huerta Tipográfica) | HUD numbers and text, in copies whose digits use the font's own lining figures (`Tools/lining_figures.py`) | SIL Open Font License 1.1 | Yes, `Assets/_Project/Fonts` |

<!-- One row per pack: name, author, what it's used for, licence, "not in this repository". -->

## AI disclosure

This project is developed with AI assistance: [Claude Code](https://claude.com/claude-code) is used for programming,
project setup and tooling. Any AI-generated art, audio or text that ends up in the game will be listed here. So far: the test battlefields' terrain shapes and their flat-colour ground textures, and the soft round particle texture used for dust and hits, were generated by Claude Code. The two armies' clothing colours are Synty's character texture recoloured by a script Claude Code wrote (`Assets/_Project/Scripts/Editor/TeamColours.cs`). The horses' bay coat is Omabuarts' zebra palette recoloured by a script Claude Code wrote (`HorseCoat.cs`), and the riders' seated pose was set by Claude Code through humanoid muscle values on a Kevin Iglesias idle (`RidingPose.cs`). The archers' bow shot and the knockdown are Kevin Iglesias clips joined end to end by a script Claude Code wrote (`JoinedClips.cs`). The kingdom map's land (heights, rivers, lakes, ground paint and where the trees and rocks stand) is built by a script Claude Code wrote (`KingdomMapBuilder.cs`) from a map made in [Azgaar's Fantasy Map Generator](https://azgaar.github.io/Fantasy-Map-Generator/) (`Assets/_Project/Data/Map/Kingdom Map.json`, exported by `Tools/export_kingdom_map.py`). The town of Millbrook on that map was laid out by Claude Code from Synty's Fantasy Kingdom buildings and props (placed by a script; the roads and levelled ground come from `KingdomMapBuilder.cs`). The Trade HUD (`Assets/_Project/UI`, `Scripts/UI`, `Scripts/Market`) was written by Claude Code from mockups made with Claude; its icons are Synty's POLYGON Icons rendered by a Claude Code script, and its market events (the pool of sixteen that each trading day draws from) and the raid notice were written by Claude Code, starting from the design doc's examples. The raid fields near Harrowby, Highfield and Stonewatch were chosen by a Claude Code search for flat, dry ground, and their trees cleared. The map's Flat Kit look (cel-shaded copies of the Synty materials, the terrain material, fog and outline settings) was set up by Claude Code (`Scripts/Editor/MapFlatLook.cs`). The map's day and night colours (a COZY atmosphere profile), how COZY's weather is spread over the map, the storm darkening and lightning flashes, and the town lights at night were set up by Claude Code (`Scripts/Map/MapDayCycle.cs`, `MapWeather.cs`, `MapStorm.cs`, `TownLights.cs`). Battle figures are drawn by a shader Claude Code wrote (`Assets/_Project/Shaders`), which poses the Synty characters and the horse on the GPU from their Kevin Iglesias and Quirky Series animations, baked by a Claude Code script (`Scripts/Editor/FigureBaker.cs`).

## Licence

Copyright © 2026 shaariqch. All rights reserved.

The source is public so that it can be read, as Game Off requires. No permission is given to copy, modify,
redistribute or reuse the code or assets. Third-party assets remain the property of their authors under their own
licences.
