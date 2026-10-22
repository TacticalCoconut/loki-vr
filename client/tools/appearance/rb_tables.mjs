// Evaluates roBrowserLegacy's job/item look-up tables (Gravity file names, not code) and prints them as JSON.
// Usage: node rb_tables.mjs <roBrowserLegacy>/src > rb_tables.json
// Strings are Latin-1 JS strings holding CP949 bytes ('\xb0\xcb...'); they are emitted as hex of those bytes.
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { register } from 'node:module';

const src = process.argv[2];
if (!src) { console.error('usage: node rb_tables.mjs <roBrowserLegacy/src>'); process.exit(1); }
// rb imports some modules by bundler alias ('DB/Items/WeaponType.js'); resolve those against <src>.
const srcUrl = pathToFileURL(path.resolve(src) + '/').href;
register('data:text/javascript,' + encodeURIComponent(
  `export async function resolve(s, c, next) { return /^(DB|Core|Engine|Renderer|UI|Utils|Audio|Network|Loaders|Controls|Preferences|Plugins|App)\\//.test(s) ? next(new URL(s, ${JSON.stringify(srcUrl)}).href, c) : next(s, c); }`));
const load = async rel => (await import(pathToFileURL(path.join(src, rel)).href)).default;

const JobId = await load('DB/Jobs/JobConst.js');
const JobNameTable = await load('DB/Jobs/JobNameTable.js');
const WeaponJobTable = await load('DB/Jobs/WeaponJobTable.js');
const PalNameTable = await load('DB/Jobs/PalNameTable.js');
const ShieldTable = await load('DB/Items/ShieldTable.js');
const WeaponTrailTable = await load('DB/Items/WeaponTrailTable.js');
const WeaponAction = await load('DB/Jobs/WeaponAction.js');

const hex = s => Buffer.from(s, 'latin1').toString('hex');
const dump = t => {
  const o = {};
  for (const k of Object.keys(t)) {
    if (k === 'undefined' || !/^\d+$/.test(k)) continue; // rb typos (e.g. JobId.ARCHBISHOP_2ND) create an 'undefined' key
    if (typeof t[k] === 'string') o[k] = hex(t[k]);
  }
  return o;
};

// Costume (LOOK_BODY2 = 1) job remap, transcribed from roBrowserLegacy src/Renderer/Entity/EntityView.js getBodyVal()
// (lines 412-521): each 3rd/4th job (and its _H/_B variants) -> its *_2ND costume id.
const costumeGroups = [
  ['RUNE_KNIGHT_2ND', 'DRAGON_KNIGHT', 'DRAGON_KNIGHT_H', 'DRAGON_KNIGHT_B', 'RUNE_KNIGHT', 'RUNE_KNIGHT_H', 'RUNE_KNIGHT_B'],
  ['MECHANIC_2ND', 'MEISTER', 'MEISTER_H', 'MEISTER_B', 'MECHANIC', 'MECHANIC_H', 'MECHANIC_B'],
  ['GUILLOTINE_CROSS_2ND', 'SHADOW_CROSS', 'SHADOW_CROSS_H', 'SHADOW_CROSS_B', 'GUILLOTINE_CROSS', 'GUILLOTINE_CROSS_H', 'GUILLOTINE_CROSS_B'],
  ['WARLOCK_2ND', 'ARCH_MAGE', 'ARCH_MAGE_H', 'ARCH_MAGE_B', 'WARLOCK', 'WARLOCK_H', 'WARLOCK_B'],
  ['ARCH_BISHOP_2ND', 'CARDINAL', 'CARDINAL_H', 'CARDINAL_B', 'ARCHBISHOP', 'ARCHBISHOP_H', 'ARCHBISHOP_B'],
  ['RANGER_2ND', 'WINDHAWK', 'WINDHAWK_H', 'WINDHAWK_B', 'RANGER', 'RANGER_H', 'RANGER_B'],
  ['ROYAL_GUARD_2ND', 'IMPERIAL_GUARD', 'IMPERIAL_GUARD_H', 'IMPERIAL_GUARD_B', 'ROYAL_GUARD', 'ROYAL_GUARD_H', 'ROYAL_GUARD_B'],
  ['GENETIC_2ND', 'BIOLO', 'BIOLO_H', 'BIOLO_B', 'GENETIC', 'GENETIC_H', 'GENETIC_B'],
  ['SHADOW_CHASER_2ND', 'ABYSS_CHASER', 'ABYSS_CHASER_H', 'ABYSS_CHASER_B', 'SHADOW_CHASER', 'SHADOW_CHASER_H', 'SHADOW_CHASER_B'],
  ['SORCERER_2ND', 'ELEMENTAL_MASTER', 'ELEMENTAL_MASTER_H', 'ELEMENTAL_MASTER_B', 'SORCERER', 'SORCERER_H', 'SORCERER_B'],
  ['SURA_2ND', 'INQUISITOR', 'INQUISITOR_H', 'INQUISITOR_B', 'SURA', 'SURA_H', 'SURA_B'],
  ['MINSTREL_2ND', 'TROUBADOUR', 'TROUBADOUR_H', 'TROUBADOUR_B', 'MINSTREL', 'MINSTREL_H', 'MINSTREL_B'],
  ['WANDERER_2ND', 'TROUVERE', 'TROUVERE_H', 'TROUVERE_B', 'WANDERER', 'WANDERER_H', 'WANDERER_B'],
];
// JobNameTable name for each costume id: rb duplicates the 3rd job's body name onto the *_2ND id
// (JobNameTable.js:254-270). JobNameTable.js:259 uses the misspelt JobId.ARCHBISHOP_2ND (undefined), so the
// Arch Bishop costume name is taken from its 3rd job (the group member named in the same duplicateEntry line).
const costumeBase = { ARCH_BISHOP_2ND: 'ARCHBISHOP' };
const costume = {}, costumeName = {}, missing = [];
for (const [target, ...jobs] of costumeGroups) {
  const tid = JobId[target];
  if (tid === undefined) { missing.push(target); continue; }
  const name = JobNameTable[tid] ?? JobNameTable[JobId[costumeBase[target]]];
  if (name !== undefined) costumeName[tid] = hex(name);
  for (const j of jobs) { if (JobId[j] === undefined) { missing.push(j); continue; } costume[JobId[j]] = tid; }
}

// WeaponAction[job] = {weaponType: 0|1|2} or [female, male] (DB/Jobs/WeaponAction.js); 0/1/2 = ATTACK1/2/3.
const weaponAction = {};
for (const job of Object.keys(WeaponAction)) {
  if (!/^\d+$/.test(job)) continue;
  const e = WeaponAction[job];
  const put = (sex, t) => { for (const k of Object.keys(t)) if (/^\d+$/.test(k)) (weaponAction[job] ??= []).push([sex, +k, t[k]]); };
  if (Array.isArray(e)) { put(0, e[0]); put(1, e[1]); } else put(-1, e);
}

const out = {
  jobname: dump(JobNameTable), weaponjob: dump(WeaponJobTable), palname: dump(PalNameTable), shield: dump(ShieldTable), trail: dump(WeaponTrailTable),
  costume, costumeName, missingJobConst: missing, weaponAction,
};
process.stdout.write(JSON.stringify(out, null, 1));
