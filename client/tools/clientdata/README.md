# Since client wave 3b (w3data): the client makes the Gravity-derived tables itself

Every table below that comes from Gravity's data (the GRF or the install's `System\` folder) is made by the client on the player's
PC on first run, into the per-user data folder (`Assets/Loki/Runtime/Data/ClientDataBuilder.cs`: these tools ported to C# step
for step), and a release build carries none of them. The tools here stay the reference: `../w3data/pyref.py` runs them all on a GRF
(no lua5.1 needed: `lubdump.py` runs the Lua dumpers on `lub51.py`, `luasrc.py` reads `System\itemInfo.lua`), and
`../w3data/compare.py` compares the client's tables with theirs byte for byte. The parts the client needs from rAthena /
roBrowserLegacy / zrenderer ship in `StreamingAssets/data/firstrun`, written by `build_firstrun_parts.py`. The whole list, with each
file's inputs: `design notes` §1. New here in w3data: `lubdump.py`, `luasrc.py`, `build_texttables.py` (msgstrings.tsv,
mapnames.tsv), `build_spritenames.py`, `build_efst.py`, `build_firstrun_parts.py`.

# Item and quest tables (Assets/StreamingAssets/data/items.tsv, quests.tsv)

Generated from the LATAM client's own System folder plus rAthena's databases (same commit as the server):

1. Copy `System/iteminfo_new.lub` and `System/OngoingQuestInfoList_True.lub` out of the client (the client's
   `itemInfo.lub` is an empty loader; `iteminfo_new.lub` is the newest table and is UTF-8; the quest table is CP1252).
2. `python3 ../rodata/lub32to64.py <files>` (converts IN PLACE — work on copies).
3. `lua5.1 dumpitems.lua iteminfo_new.lub items_raw.tsv` and `lua5.1 dumpquests.lua OngoingQuestInfoList_True.lub quests_raw.tsv`
4. `python3 build_tables.py items_raw.tsv quests_raw.tsv <rathena>/db ../../Assets/StreamingAssets/data`

Item resource names are stored as hex of their CP949 bytes (GRF paths are CP949): sprites are
`data\sprite\아이템\<name>.spr/.act`, inventory icons `data\texture\유저인터페이스\item\<name>.bmp`.

# Skill window tables (Assets/StreamingAssets/data/skilltree.tsv, skills.tsv)

`python build_skilltree.py "C:\Ragnarok Online\data.grf" <Unity project>\Assets\StreamingAssets\data` reads the
client's skill lubs straight out of the GRF (0x200 / 0x300, `grf.py`) and runs them with `lub51.py` (a Lua 5.1 bytecode
interpreter: no lua5.1 needed). See the header of `build_skilltree.py` for the files and the row kinds.

# Equipment window table (Assets/StreamingAssets/data/randomoptions.tsv, client wave 3 w3equip)

`python build_equip_tables.py "C:\Ragnarok Online\data.grf" <Unity project>\Assets\StreamingAssets\data` reads the
client's random option tables out of the GRF (datainfo\enumvar.lub, addrandomoptionnametable_enus.lub / addrandomoptionnametable.lub,
addrandomoption_f.lub) with `lub51.py` and writes the option names the item details show (id, EnumVAR name, English and
Portuguese format, SetPercentValue). See the header of `build_equip_tables.py`.
