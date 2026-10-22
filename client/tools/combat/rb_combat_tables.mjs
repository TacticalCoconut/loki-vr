// Evaluates roBrowserLegacy's combat look-up tables (Gravity file names and effect keys, not code) and prints them as JSON
// for build_combat_data.py (client wave 3, w3combat; design notes §2f / §2g):
//   weaponHit:   weapon type (rAthena W_* = rB WeaponType, 0..30 and 103) -> [hit wav, ...]   rB src/DB/Items/WeaponHitSoundTable.js
//   jobHit:      player job id -> [wav] (hits on players, by the target's job)                rB src/DB/Jobs/JobHitSoundTable.js
//   weaponSwing: weapon type -> [swing wav, ...] (the ACT 'atk' event)                         rB src/DB/Items/WeaponSoundTable.js
//   projectile / spawn: monster class -> effect key of its normal attack                     rB src/DB/Monsters/AttackEffectTable.js
// Strings holding CP949 bytes (the gun hit sounds) stay Latin-1 characters, like every other Loki data file.
// Usage: node rb_combat_tables.mjs <roBrowserLegacy>/src > rb_combat.json
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { register } from 'node:module';

const src = process.argv[2];
if (!src) { console.error('usage: node rb_combat_tables.mjs <roBrowserLegacy/src> > rb_combat.json'); process.exit(1); }
// rb imports some modules by bundler alias ('DB/Items/WeaponType.js'); resolve those against <src> (as tools/appearance/rb_tables.mjs).
const srcUrl = pathToFileURL(path.resolve(src) + '/').href;
register('data:text/javascript,' + encodeURIComponent(
  `export async function resolve(s, c, next) { return /^(DB|Core|Engine|Renderer|UI|Utils|Audio|Network|Loaders|Controls|Preferences|Plugins|App)\\//.test(s) ? next(new URL(s, ${JSON.stringify(srcUrl)}).href, c) : next(s, c); }`));
const load = async rel => (await import(pathToFileURL(path.join(src, rel)).href)).default;

const WeaponHitSound = await load('DB/Items/WeaponHitSoundTable.js');
const JobHitSound = await load('DB/Jobs/JobHitSoundTable.js');
const WeaponSwing = await load('DB/Items/WeaponSoundTable.js');
const AttackEffect = await load('DB/Monsters/AttackEffectTable.js');

// numeric keys only: rb typos (an undefined JobId constant in a duplicateEntry call) create an 'undefined' key
const numeric = (t, name, list) => {
  const o = {};
  for (const k of Object.keys(t).sort((a, b) => a - b)) {
    if (!/^\d+$/.test(k)) { console.error(`${name}: skipped non-numeric key '${k}'`); continue; }
    o[k] = list ? (Array.isArray(t[k]) ? t[k] : [t[k]]) : t[k];
  }
  return o;
};

process.stdout.write(JSON.stringify({
  weaponHit: numeric(WeaponHitSound, 'WeaponHitSoundTable', true),
  jobHit: numeric(JobHitSound, 'JobHitSoundTable', true),
  weaponSwing: numeric(WeaponSwing, 'WeaponSoundTable', true),
  projectile: numeric(AttackEffect.PROJECTILE, 'AttackEffectTable.PROJECTILE', false),
  spawn: numeric(AttackEffect.SPAWN, 'AttackEffectTable.SPAWN', false),
}, null, 1));
