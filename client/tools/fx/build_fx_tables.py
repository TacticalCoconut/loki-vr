"""
Builds Assets/StreamingAssets/data/fx/*.json — the client's effect tables as roBrowserLegacy transcribed them — from the dumps that
tools/fx/dump_fx.mjs writes (node dump_fx.mjs <roBrowserLegacy>/src <dump dir>; client wave 4: committed, it reproduces the earlier,
uncommitted research/fx/dump_fx.mjs byte for byte and adds each FUNC part's recorded "calls"):
  effect_table.json  effect id (EF_* number, identical to rAthena's e_special_effects) or roBrowser string key → {name, entries:[part…]}
  skill_effect.json  skill id → {name, effectId, effectIdOnCaster, hitEffectId, beforeHitEffectId, releaseEffectId, beginCastEffectId,
                     successEffectId(OnCaster), hideCastBar, hideCastAura}  (research/skill_effects.md §1.3)
  skill_unit.json    UNT_* id → {name, effectId}
JS function bodies (FUNC parts, id functions) are replaced by true: Loki re-creates EF_LOCKON / EF_GROUNDSAMPLE by key and the ground
FUNCs (songs, Volcano, Land Protector, Spider Web …) from their "calls" (what the FUNC hands to this.add: Fx/GroundFx.cs).
Usage: build_fx_tables.py <research/fx dir> <StreamingAssets/data/fx dir>
"""
import json, os, sys

def strip(o):
    if isinstance(o, dict):
        if '__function' in o and len(o) == 1: return {'__function': True}
        if '__stub' in o and len(o) == 1: return {'__stub': True}
        return {k: strip(v) for k, v in o.items()}
    if isinstance(o, list): return [strip(x) for x in o]
    return o

def main(src, dst):
    os.makedirs(dst, exist_ok=True)
    for name in ('effect_table.json', 'skill_effect.json', 'skill_unit.json'):
        data = strip(json.load(open(os.path.join(src, name), encoding='utf-8')))
        with open(os.path.join(dst, name), 'w', encoding='utf-8', newline='\n') as o:
            json.dump(data, o, ensure_ascii=False, separators=(',', ':'))
        print(name, len(data), 'keys', os.path.getsize(os.path.join(dst, name)), 'bytes')

if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
