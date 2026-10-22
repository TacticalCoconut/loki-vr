# Client data tables

`spritenames.txt` (in Assets/StreamingAssets/data) is generated from (since client wave 3b: `../clientdata/build_spritenames.py` writes it from
the same lubs through lub51.py, and the client makes it itself on first run; the steps below are the earlier recipe):

1. The client's own `data\luafiles514\lua files\datainfo\*.lub` (Lua 5.1 bytecode, size_t = 4 because the client is
   32-bit). `lub32to64.py` rewrites them to size_t = 8 so a stock 64-bit `lua5.1` can `dofile` them; `dump.lua` then
   dumps `jobtbl`, `JobNameTable` (mob + NPC sprite names), `AccNameTable`, `RobeNameTable`, `WeaponNameTable` as JSON.
2. `pcjobnames_hex.json`: PC job id → body sprite file name (EUC-KR bytes as hex). The LATAM client's `pcjobname.lub`
   only carries Portuguese display names; the body sprite names are hard-coded in Ragexe. This table was evaluated from
   roBrowserLegacy's `src/DB/Jobs/JobNameTable.js` + `JobConst.js` (data about Gravity's file names, not code).

Extract the lubs with `LokiBatch.ExtractFiles` (see Assets/Loki/Editor/LokiBatch.cs).

Equipment look tables (weapon/shield folders, palettes, layer priorities, item ClassNum) are in `appearance.txt` and
`itemviews.tsv`, built by `../appearance/build_appearance.py` (see `../appearance/README.md`).
