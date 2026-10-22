// Dumps roBrowserLegacy's effect tables (a GPL reimplementation of the official client's effect data, not the official client) to the
// JSON files tools/fx/build_fx_tables.py turns into StreamingAssets/data/fx/*.json (client wave 4, w4fx; the earlier dumper,
// research/fx/dump_fx.mjs, was never committed — this one reproduces its output, see design notes, plus the FUNC sources).
//   node dump_fx.mjs <roBrowserLegacy>/src <out dir>
// Reads DB/Effects/EffectConst.js, EffectTable.js, DB/Skills/SkillConst.js, SkillEffect.js, SkillUnitConst.js, SkillUnit.js,
// DB/Jobs/JobConst.js. The modules are evaluated as text: their imports become inert stubs (renderers, the camera, the chat box …), so
// every value that is plain data comes out as it is; a JS function becomes {"__function": "<its source>"} (FUNC parts, id functions).
// Writes effect_table.json (key → {name, entries}), skill_effect.json (skill id → {name, fields…}), skill_unit.json (UNT id → {name,
// effectId, effectName, inEffectTable}).
import fs from 'fs';
import path from 'path';

const src = process.argv[2], out = process.argv[3];
if (!src || !out) { console.error('usage: node dump_fx.mjs <roBrowserLegacy>/src <out dir>'); process.exit(2); }

// While a FUNC part runs (see runFunc below) every call into an imported module is recorded here; null otherwise.
let LOG = null;
// Renderer factories: calling one returns a class (FlatColorTile(name, spec), Tiles.HoveringTexture(tex, size, alpha), Tiles.FlatTexture(tex, size)).
const FACTORIES = new Set(['FlatColorTile', 'Tiles.HoveringTexture', 'Tiles.FlatTexture']);

// A stand-in for every imported module: any property gives another stub; `new X(…)` gives a record {__cls, __args} (a renderer the FUNC
// hands to this.add); calling X(…) is logged while a FUNC runs (Camera.setQuake, …WeatherEffect.startOrRestart, …); a factory returns a class.
function stub(name, factoryArgs) {
	const f = function () { return stub(name + '()'); };
	return new Proxy(f, {
		get(t, k) {
			if (k === Symbol.toPrimitive) return () => 0;
			if (k === '__stubName') return name;
			if (k === 'prototype') return {};
			return stub(name + '.' + String(k));
		},
		apply(t, self, args) {
			if (FACTORIES.has(name)) return stub(name, args);
			if (LOG) LOG.push({ call: name, args: args.map(plainArg) });
			return stub(name + '()');
		},
		construct(t, args) {
			const o = { __cls: name, __args: args.map(plainArg) };
			if (factoryArgs) o.__factory = factoryArgs.map(plainArg);
			return o;
		}
	});
}

// the values a FUNC is called with: positions / the owner become markers so the recorded calls are data
const POS = [100, 100, 0], OWNER_POS = [200, 200, 0], OTHER_POS = [210, 190, 0];
function entity(label, pos) {
	const target = {};
	return new Proxy(target, {
		get(t, k) {
			if (k === 'position') return pos;
			if (k === '__entity') return label;
			if (k === Symbol.toPrimitive) return () => 0;
			if (k in t) return t[k];
			return stub(label + '.' + String(k));
		},
		set(t, k, v) { if (LOG) LOG.push({ set: label + '.' + String(k), value: plainArg(v) }); t[k] = v; return true; }
	});
}
function plainArg(v) {
	if (v === POS) return '$pos';
	if (v === OWNER_POS) return '$ownerPos';
	if (v === OTHER_POS) return '$otherPos';
	if (v && v.__entity) return '$' + v.__entity;
	if (typeof v === 'function') return v.__stubName ? { __stub: v.__stubName } : { __function: v.toString() };
	if (Array.isArray(v)) return v.map(plainArg);
	if (v && typeof v === 'object') { const o = {}; for (const k of Object.keys(v)) o[k] = plainArg(v[k]); return o; }
	return v;
}

// runs one FUNC part with recording stand-ins: returns what it handed to this.add (renderers with their constructor arguments) and
// every module call it made, in order
function runFunc(fn, part) {
	LOG = [];
	const owner = entity('owner', OWNER_POS), other = entity('other', OTHER_POS);
	const Params = {
		Inst: { position: POS, otherPosition: OTHER_POS, startTick: 0, endTick: 1000, duration: 1000, effectID: 0, duplicateID: 0 },
		Init: { ownerEntity: owner, otherEntity: other, ownerAID: 1, otherAID: 2, position: POS, otherPosition: OTHER_POS, startTick: 0 },
		effect: part
	};
	const ctx = { add(obj) { LOG.push({ add: obj && obj.__cls ? obj : plainArg(obj) }); } };
	try { fn.call(ctx, Params); } catch (e) { LOG.push({ error: String(e && e.message || e) }); }
	const log = LOG; LOG = null;
	return log;
}

// evaluates an ES module's text: `import X from '…'` → the given value or a stub; `export default` → the result
function evalModule(file, provided) {
	let text = fs.readFileSync(path.join(src, file), 'utf8');
	const names = [];
	text = text.replace(/^import\s+(\*\s+as\s+)?(\w+)\s+from\s+['"]([^'"]+)['"];?\s*$/gm, (m, star, name, from) => { names.push([name, from]); return ''; });
	text = text.replace(/^export\s+default\s+/m, '__out.value = ');
	text = text.replace(/^export\s+(const|let|function|class)\s/gm, '$1 ');
	const args = ['__out'], vals = [{}];
	for (const [n, from] of names) { args.push(n); vals.push(n in provided ? provided[n] : stub(n)); }
	const fn = new Function(...args, text);
	fn(...vals);
	return vals[0].value;
}

const EC = evalModule('DB/Effects/EffectConst.js', {});
const SK = evalModule('DB/Skills/SkillConst.js', {});
const SU = evalModule('DB/Skills/SkillUnitConst.js', {});
const JobId = evalModule('DB/Jobs/JobConst.js', {});
// rB's song / dance ground effects (Renderer/Effects/Songs.js) evaluated with the renderer factories stubbed: their exports become lists
// of recording classes, so a FUNC that does `LullabyEffects.forEach(e => self.add(new e(…)))` records FlatColorTile / HoveringTexture args
// Math.random is pinned to 0 while Songs.js runs so the dump is reproducible: its Bragi notes (spell_0N.bmp, N random 1..8, Songs.js
// BragiRandNumbers) come out as spell_01 and Loki draws a random N at run time as rB does (Fx/GroundFx.cs).
const SongEffects = (() => { const r = Math.random; Math.random = () => 0; try { return evalModule('Renderer/Effects/Songs.js', {}); } finally { Math.random = r; } })();
// rB's EffectTable calls AbyssEffects.forEach, but Songs.js exports AbyssEffects as a function returning the list (a random gem
// texture each call), so rB throws and draws nothing for Into the Abyss (284_ground). Loki takes the evident intent: the list the
// function returns, with Math.random pinned to 0 (the red gem; Loki picks one of the three at run time).
{ const r = Math.random; Math.random = () => 0; try { SongEffects.AbyssEffects = SongEffects.AbyssEffects(); } finally { Math.random = r; } }
const EffectTable = evalModule('DB/Effects/EffectTable.js', { EffectConst: EC, EC, SongEffects });
const SkillEffect = evalModule('DB/Skills/SkillEffect.js', { SK, JobId });
const SkillUnit = evalModule('DB/Skills/SkillUnit.js', { SU, EC });

const efName = {}; for (const k in EC) if (!(EC[k] in efName)) efName[EC[k]] = k;
const skName = {}; for (const k in SK) if (!(SK[k] in skName)) skName[SK[k]] = k;
const suName = {}; for (const k in SU) if (!(SU[k] in suName)) suName[SU[k]] = k;

// plain data out; functions keep their source; stubs (a module value used as data) are marked
function plain(v) {
	if (typeof v === 'function') { if (v.__stubName) return { __stub: v.__stubName }; return { __function: v.toString() }; }
	if (Array.isArray(v)) return v.map(plain);
	if (v && typeof v === 'object') { const o = {}; for (const k of Object.keys(v)) o[k] = plain(v[k]); return o; }
	return v;
}

const effects = {};
let funcs = 0;
for (const key of Object.keys(EffectTable)) {
	const list = Array.isArray(EffectTable[key]) ? EffectTable[key] : [EffectTable[key]];
	const entries = plain(list);
	// a FUNC part: also what it does when run (w4fx: Loki re-creates the renderers it hands to this.add from these records)
	list.forEach((part, i) => { if (part && part.type === 'FUNC' && typeof part.func === 'function' && !part.func.__stubName) { entries[i].calls = runFunc(part.func, part); funcs++; } });
	effects[key] = { name: key in efName ? efName[key] : (/^-?\d+$/.test(key) && efName[+key]) || null, entries };
}
console.log('FUNC parts run:', funcs);
const skills = {};
for (const id of Object.keys(SkillEffect)) skills[id] = Object.assign({ name: skName[id] || null }, plain(SkillEffect[id]));
const units = {};
for (const id of Object.keys(SkillUnit)) {
	const e = SkillUnit[id];
	units[id] = { name: suName[id] || null, effectId: e, effectName: typeof e === 'number' ? efName[e] || null : null, inEffectTable: e in EffectTable };
}

fs.mkdirSync(out, { recursive: true });
const write = (f, o) => { fs.writeFileSync(path.join(out, f), JSON.stringify(o, null, 1), 'utf8'); console.log(f, Object.keys(o).length, 'keys'); };
write('effect_table.json', effects);
write('skill_effect.json', skills);
write('skill_unit.json', units);
