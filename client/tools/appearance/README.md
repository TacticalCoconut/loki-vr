# Player appearance tables (Assets/StreamingAssets/data/appearance.txt, itemviews.tsv)

Read at runtime by `Sprites/PlayerAppearance.cs` (`AppearanceDb`). `spritenames.txt` (see `../rodata/README.md`) still
provides body/hair/headgear/garment/weapon-name tables; these two files add what the equipment layers need.

| file | content | source |
|---|---|---|
| `appearance.txt` `wjob` | weapon/shield class folder per job | roBrowserLegacy `src/DB/Jobs/WeaponJobTable.js` |
| `appearance.txt` `wjob2` | fallback weapon folder `folder\prefix` | zrenderer `resolver_data/job_weapon_names.txt` (MIT; row = job id, or job id − 3950 above 4000) |
| `appearance.txt` `pal`, `pal2` | clothes palette base name per job | rb `src/DB/Jobs/PalNameTable.js`; zrenderer `resolver_data/job_pal_names.txt` |
| `appearance.txt` `shield` | shield ClassNum → file suffix | rb `src/DB/Items/ShieldTable.js` |
| `appearance.txt` `trail` | weapon type → slash sprite suffix (`_검_검광` …) | rb `src/DB/Items/WeaponTrailTable.js` |
| `appearance.txt` `costume`, `costumename` | LOOK_BODY2 costume job + its body file name | rb `src/Renderer/Entity/EntityView.js` getBodyVal (transcribed in `rb_tables.mjs`) + JobNameTable |
| `appearance.txt` `watk` | PC attack motion (group 5/10/11) per job/sex/weapon type | rb `src/DB/Jobs/WeaponAction.js` |
| `appearance.txt` `wexp` | item-specific weapon view → base type | client `datainfo\weapontable.lub` `Expansion_Weapon_IDs` |
| `appearance.txt` `layerdef`, `layer` | headgear/garment draw priority | client `datainfo\tb_layer_priority.lub` `TB_Layer_Priority` |
| `itemviews.tsv` | `id, rAthena Type, ClassNum, rAthena weapon SubType (W_*), Left_Hand` | client `System/iteminfo_new.lub` (wins) + `System/itemInfo.lua`; rAthena `db/re/item_db_equip.yml` |

Strings keep their CP949 bytes as Latin-1 characters, exactly like `spritenames.txt`.

## Rebuild

```sh
cd tools/appearance
# 1. roBrowserLegacy tables (the repo's package.json has "type": "module")
node rb_tables.mjs <roBrowserLegacy>/src > rb_tables.json
# 2. client lubs: copy data\luafiles514\lua files\datainfo\{accessoryid,weapontable,tb_layer_priority}.lub out of the GRF
#    (tb_layer_priority.lub from datainfo\, not the older copy in data\), convert IN PLACE, then dump
python3 ../rodata/lub32to64.py accessoryid.lub weapontable.lub tb_layer_priority.lub
lua5.1 dump_lubs.lua > lubs.tsv
# 3. ClassNum from both client item tables (iteminfo_new.lub converted with lub32to64.py first)
lua5.1 dump_classnum.lua iteminfo_new.lub > classnum_new.tsv
lua5.1 dump_classnum.lua itemInfo.lua > classnum_old.tsv
# 4. zrenderer checkout (https://github.com/zhad3/zrenderer, used at b651017) for resolver_data/
python3 build_appearance.py rb_tables.json lubs.tsv <zrenderer>/resolver_data classnum_new.tsv classnum_old.tsv <rathena>/db ../../Assets/StreamingAssets/data
```

`node rb_tables.mjs` registers a resolve hook because rb imports some modules by bundler alias (`DB/Items/WeaponType.js`).

## Offline render check (`render_test/`)

A .NET 8 console project that compiles the real `ActorSprite.cs`, `SpriteFrameCache.cs`, `PlayerAppearance.cs`, `RoNames.cs`,
`ActFile.cs`, `SprFile.cs`, `RoReader.cs` against small managed stand-ins for the UnityEngine types (`UnityStub.cs`) and an
`RoAssets` stand-in that reads extracted sprites from folders (paths are hard-coded to this workspace: <sprite-dir>/ex3,
<sprite-dir>/ex2). It resolves a few `PlayerLook`s with the real `PlayerAppearance.Apply`, calls
`ActorSprite.BuildFrame` for 8 directions × several actions and writes the composites as PPM plus the draw order per cell.
`dotnet run -- <outDir>` from that folder. It is a test tool only; nothing in the game uses it.

The rules that use these tables (paths, fallbacks, draw order) are documented with citations in
`research/appearance_impl_notes.md`.
