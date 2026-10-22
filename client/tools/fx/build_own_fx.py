#!/usr/bin/env python3
r"""Client wave 5 (w5fxown): LOKI'S OWN skill effects — a design, NOT the official client's look.

No source shows what the official client draws for the skills of design notes (it decides them in its
own program: design notes), so the design specified Loki's own effects (2026-09-30: "Build the missing ones yourself"). This tool
writes the table the client reads at run time, client/Assets/Loki/Resources/Fx/loki_own_fx.txt (Loki's own data: rAthena's facts per
skill plus Loki's recipe choice and GRF file NAMES; no Gravity data — every texture / STR is read from the player's GRF at run time,
and a file that GRF lacks is skipped).

  python build_own_fx.py <rathena> <data.grf> <data/fx dir> <skills.tsv> <missing-effects.tsv> <out.tsv>

One visual language, driven by rAthena's facts (db/re/skill_db.yml, e985006: Type, TargetType, DamageFlags NoDamage / Splash, Element,
Range, SplashArea, HitCount, Status, MaxLevel — cited per row in the table's columns):
  attack (no NoDamage flag)  TargetType Attack: strike (melee: |Range| < 4), shot (ranged Weapon / Misc), bolt (ranged Magic);
                             a SplashArea > 0 adds a shock ring of that radius at the impact.
                             TargetType Self: nova (a ring of SplashArea around the caster); TargetType Ground: field (a sigil disc).
  heal                       the named restoring skills (HEAL below): rising light on the target.
  buff (NoDamage)            Self: aura; Self with a SplashArea (party / area buffs): song (performers: notes) or aura + a ring;
                             Support: bless (a link from the caster, light descending on the target).
  debuff (NoDamage)          TargetType Attack with a Status: curse (dark motes sinking, a ring closing in).
  utility (NoDamage)         no Status: pulse (a small ripple at the feet); on an enemy: mark; on the ground: zone.
The element gives the colour and the art (the palette rows below: GRF texture names). Weapon / Endowed / Random elements use the
"steel" palette (-1); skills with no Element field take their class family's palette (FAMILY below) for non-attacks.
Hand-made recipes (HAND) for the skills players notice first, and the 4th-job skills whose own GRF folders hold their art (recipe
"grf": the files are theirs, the moments and anchors Loki's: rules in grf_recipe()).
"""
import os, re, struct, sys
import yaml

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE); sys.path.insert(0, os.path.join(HERE, '..', 'combat'))
BS = chr(92)

ELEMENTS = {'Neutral': 0, 'Water': 1, 'Earth': 2, 'Fire': 3, 'Wind': 4, 'Poison': 5, 'Holy': 6, 'Dark': 7, 'Ghost': 8, 'Undead': 9,
            'Weapon': -1, 'Endowed': -1, 'Random': -1}

# palette: element -> name, colour, blend ("add" / "alpha"), spark texture, orb texture, column texture, puff texture, sigil texture
# (GRF names under data\texture\effect\; read at run time; a missing one falls back to a texture drawn in code)
PALETTE = [
    (-1, 'steel', '0.90,0.94,1.00', 'add', 'pikapika_white.tga', 'glow11.bmp', 'ring_white.tga', 'smoke.tga', 'magic_target.tga'),
    (0, 'neutral', '1.00,0.95,0.80', 'add', 'pikapika_white.tga', 'glow11.bmp', 'ring_white.tga', 'smoke.tga', 'magic_target.tga'),
    (1, 'water', '0.45,0.72,1.00', 'add', 'iceparticle.bmp', 'waterorb.bmp', 'ring_blue.tga', 'freezing_smoke.bmp', 'magic_y_01.tga'),
    (2, 'earth', '0.90,0.68,0.38', 'add', 'stone-1.bmp', 'stoneorb.bmp', 'ring_brown.tga', 'smoke_mud.bmp', 'magic_y_01.tga'),
    (3, 'fire', '1.00,0.50,0.16', 'add', 'fireparticle.tga', 'fireorb.bmp', 'ring_red.tga', 'fire_smoke_a.bmp', 'magic_y_01.tga'),
    (4, 'wind', '0.78,1.00,0.58', 'add', 'pikapika_lime.tga', 'lightningorb.bmp', 'ring_yellow.tga', 'smoke.tga', 'magic_y_01.tga'),
    (5, 'poison', '0.62,0.92,0.36', 'add', 'pikapika_genetic_yellowgreen.tga', 'poison_pupu.bmp', 'magic_green.tga', 'poison_smoke.bmp', 'magic_y_01.tga'),
    (6, 'holy', '1.00,0.93,0.62', 'add', 'ring_star.tga', 'glow11.bmp', 'ring_white.tga', 'smoke.tga', 'magic_target.tga'),
    (7, 'dark', '0.62,0.34,0.92', 'add', 'pikapika_violet.tga', 'glow12.bmp', 'ring_purple.tga', 'smoke.tga', 'magic_y_01.tga'),
    (8, 'ghost', '0.80,0.66,1.00', 'add', 'pikapika_lilac.tga', 'ghost01.bmp', 'ring_purple.tga', 'smoke.tga', 'magic_y_01.tga'),
    (9, 'undead', '0.62,0.82,0.50', 'add', 'pikapika_genetic_yellowgreen.tga', 'ghost01.bmp', 'ring_jadu.tga', 'smoke_mud.bmp', 'magic_y_01.tga'),
]

# a non-attack skill without an Element of its own takes its class family's palette (Loki's choice: priests holy, linkers / sages ghost,
# shadows dark, rangers wind, alchemists earth, smiths / machines fire, the rest neutral)
FAMILY = [(('AB_', 'PR_', 'CD_', 'IQ_', 'AL_', 'HP_', 'ALL_RAY'), 6), (('SL_', 'SA_', 'PF_', 'SP_', 'HW_', 'WZ_', 'WL_', 'SO_', 'EM_', 'AG_'), 8),
          (('SC_', 'GC_', 'ABC_', 'SHC_', 'AS_', 'ST_', 'RG_', 'NJ_', 'KO_', 'KG_', 'OB_', 'SS_'), 7), (('RA_', 'WH_', 'HT_', 'SN_'), 4),
          (('GN_', 'BO_', 'AM_', 'CR_', 'SU_'), 2), (('NC_', 'MT_', 'WS_', 'BS_', 'GS_', 'RL_', 'NW_'), 3), (('TK_', 'SG_', 'SJ_', 'SKE_'), 0)]

# the restoring skills (Loki's reading of their descriptions / statuses): drawn as heal
HEAL = {'AM_POTIONPITCHER', 'CD_DILECTIO_HEAL', 'CD_REPARATIO', 'SA_FULLRECOVERY', 'AB_RENOVATIO', 'RK_REFRESH', 'GC_ANTIDOTE',
        'SR_GENTLETOUCH_CURE', 'SO_EL_CURE', 'NV_HELPANGEL', 'PR_REDEMPTIO', 'AB_EXPIATIO'}
PERFORMERS = ('WM_', 'BA_', 'DC_', 'BD_', 'TR_', 'CG_')

# w5looks draws these four (their official body animations / flashes: design notes w5looks): no own effect on top
W5LOOKS = {'BA_FROSTJOKER', 'DC_SCREAM', 'CH_PALMSTRIKE', 'PF_MINDBREAKER'}

# hand-made recipes: aegis -> (recipe, argument). The spirits' argument is their class colour: (GRF sparkle texture, r,g,b)
SPIRITS = {
    'SL_ALCHEMIST': ('pikapika_genetic_yellowgreen.tga', '0.70,1.00,0.35'), 'SL_MONK': ('pikapika_sun_orange.tga', '1.00,0.62,0.22'),
    'SL_STAR': ('pikapika_ultramarine.tga', '0.40,0.55,1.00'), 'SL_SAGE': ('pikapika_sorcerer_light_blue.tga', '0.55,0.85,1.00'),
    'SL_CRUSADER': ('pikapika_royal_guard_blue.tga', '0.35,0.55,1.00'), 'SL_SUPERNOVICE': ('pikapika_cyan.tga', '0.40,1.00,1.00'),
    'SL_KNIGHT': ('pikapika_rune_knight_red.tga', '1.00,0.35,0.30'), 'SL_WIZARD': ('pikapika_warlock_violet.tga', '0.75,0.45,1.00'),
    'SL_PRIEST': ('pikapika_archbishop_white.tga', '1.00,0.97,0.88'), 'SL_BARDDANCER': ('pikapika_minstrel_pink.tga', '1.00,0.55,0.80'),
    'SL_ROGUE': ('pikapika_gray.tga', '0.75,0.78,0.85'), 'SL_ASSASIN': ('pikapika_guillotine_cross_silver.tga', '0.85,0.88,0.95'),
    'SL_BLACKSMITH': ('pikapika_mechanic_gold.tga', '1.00,0.82,0.35'), 'SL_HUNTER': ('pikapika_ranger_green.tga', '0.45,1.00,0.50'),
    'SL_SOULLINKER': ('pikapika_lilac.tga', '0.82,0.62,1.00'), 'SL_HIGH': ('pikapika_white.tga', '1.00,1.00,1.00'),
}
HAND = {
    'SL_KAITE': ('kaite', ''), 'SL_KAUPE': ('kaupe', ''), 'SL_SKE': ('eske', ''),
    'TK_STORMKICK': ('tk_storm', ''), 'TK_DOWNKICK': ('tk_down', ''), 'TK_TURNKICK': ('tk_turn', ''), 'TK_COUNTER': ('tk_counter', ''),
    'TK_READYSTORM': ('tk_ready', 'storm'), 'TK_READYDOWN': ('tk_ready', 'down'), 'TK_READYTURN': ('tk_ready', 'turn'),
    'TK_READYCOUNTER': ('tk_ready', 'counter'), 'TK_DODGE': ('tk_dodge', ''), 'TK_SEVENWIND': ('tk_sevenwind', ''),
    'SG_SUN_COMFORT': ('sg_comfort', 'sun'), 'SG_MOON_COMFORT': ('sg_comfort', 'moon'), 'SG_STAR_COMFORT': ('sg_comfort', 'star'),
    'SG_SUN_WARM': ('sg_warm', 'sun'), 'SG_MOON_WARM': ('sg_warm', 'moon'), 'SG_STAR_WARM': ('sg_warm', 'star'),
    'SG_FEEL': ('sg_feel', ''), 'SG_HATE': ('sg_hate', ''),
    'HW_NAPALMVULCAN': ('napalm', ''), 'HW_GANBANTEIN': ('ganbantein', ''), 'NJ_BUNSINJYUTSU': ('mirror', ''),
    'GS_GATLINGFEVER': ('gatling', ''), 'CG_TAROTCARD': ('tarot', ''), 'WS_CARTTERMINATION': ('cartterm', ''),
}
# a template's argument (Loki's choice): Tetra Vortex's four elements fly as four orbs
ARGS = {'WL_TETRAVORTEX': 'multi'}

# Loki's choices inside a 4th-job folder (files with '/' separators): the variants, loops and status counters left out, and files of
# one folder given to the skill that uses them (their names follow the folder of the skill that owns the look)
GRF_DROP = {
    'SU_LUNATICCARROTBEAT': r'su_lunaticcarrotbeat[01]$',           # three near-identical carrot rains: the first and the sprite
    'SU_BUNCHOFSHRIMP': r'brunchofshrimp',                         # the same file twice (one misspelt)
    'MT_A_MACHINE': r'waiting',                                    # the machine's idle loops
    'ABC_FROM_THE_ABYSS': r'_attack$|shadowball_die$|shadowball$',   # those are ABC_FROM_THE_ABYSS_ATK's (GRF_ADD)
    'BO_ACIDIFIED_ZONE_WATER': r'_a_(start|loop)|_c$', 'BO_ACIDIFIED_ZONE_GROUND': r'_a_(start|loop)|_ground_a$|_c$',
    'BO_ACIDIFIED_ZONE_WIND': r'_a_(start|loop)|_c$', 'BO_ACIDIFIED_ZONE_FIRE': r'_a_(start|loop)|_c$',
    'EM_CONFLAGRATION': r'^state_effect/|^conflagration/conflagration$',  # the burning status' own look and its root copy
    'TR_SOUNDBLEND': r'soundblend_red$', 'CD_COMPETENTIA': r'_loop',
    'EM_ELEMENTAL_BUSTER': r'^elemental_buster/elemental_buster/|_hit$',  # the base skill: its cast; the elements' files go to their skills
}
GRF_ADD = {
    'ABC_FROM_THE_ABYSS_ATK': ['cast:caster:STR:from_the_abyss/from_the_abyss_attack/from_the_abyss_attack:0',
                               'hit:target:STR:from_the_abyss/from_the_abyss_shadowball_die/from_the_abyss_shadowball_die:0'],
}
for _el, _f in (('FIRE', 'fire'), ('WATER', 'water'), ('WIND', 'wind'), ('GROUND', 'land'), ('POISON', 'poison')):
    GRF_ADD['EM_ELEMENTAL_BUSTER_' + _el] = ['main:caster:STR:elemental_buster/elemental_buster/elemental_buster_%s/elemental_buster_%s:0' % (_f, _f),
                                             'main:caster:STR:elemental_buster/elemental_buster/elemental_buster_%s_bottom/elemental_buster_%s_bottom:0' % (_f, _f),
                                             'hit:target:STR:elemental_buster/elemental_buster_hit/elemental_buster_%s_hit/elemental_buster_%s_hit:0' % (_f, _f)]

# brief (CLIENT_WAVE5.md w5fxown) names Star Gladiator's Warmth casts; missing-effects.tsv filed them "not needed: its ground unit is
# drawn", but their unit is UNT_DUMMYSKILL (skill_db.yml Unit Id Dummyskill: never shown, CombatFx.UntDummySkill), so they draw nothing
EXTRA = {'SG_SUN_WARM', 'SG_MOON_WARM', 'SG_STAR_WARM'}


def first(v, key):
    if isinstance(v, list): return v[0].get(key) if v else None
    return v


def facts(e):
    df = e.get('DamageFlags') or {}
    el = first(e.get('Element'), 'Element')
    return dict(type=e.get('Type') or '', target=e.get('TargetType') or '', nodmg=bool(df.get('NoDamage')), splashflag=bool(df.get('Splash')),
                element=el, hits=first(e.get('HitCount'), 'Count'), splash=first(e.get('SplashArea'), 'Area'), range=first(e.get('Range'), 'Size'),
                status=e.get('Status') or '', maxlv=e.get('MaxLevel') or 1, unit=(e.get('Unit') or {}).get('Id') or '')


def palette_of(aegis, f, kind):
    """The skill's own Element; Weapon / Endowed / Random: steel (-1); no Element field (and Neutral for a non-attack): the family's."""
    el = f['element']
    if el is not None and el in ELEMENTS and not (kind != 'attack' and el == 'Neutral'): return ELEMENTS[el]
    for prefixes, p in FAMILY:
        if aegis.startswith(prefixes): return p
    return -1 if kind == 'attack' else 0


def template(aegis, f):
    """(recipe, kind, argument) from the facts (the rules in the module text)."""
    tgt, rg, sp = f['target'], f['range'] or 0, f['splash'] or 0
    if aegis in HEAL: return 'heal', 'heal', ''
    if not f['nodmg']:
        if tgt == 'Ground': return 'field', 'attack', ''
        if tgt == 'Self': return 'nova', 'attack', ''
        if abs(rg) >= 4: return ('bolt' if f['type'] == 'Magic' else 'shot'), 'attack', ''
        return 'strike', 'attack', ''
    if tgt == 'Ground': return 'zone', 'util', ''
    if tgt == 'Attack': return ('curse', 'debuff', '') if f['status'] else ('mark', 'util', '')
    if tgt == 'Support': return 'bless', 'buff', ''
    # Self
    if sp and sp != 0: return 'song', 'buff', 'notes' if aegis.startswith(PERFORMERS) else 'ring'
    if f['status']: return 'aura', 'buff', ''
    return 'pulse', 'util', ''


# ---- the 4th-job skills whose own GRF folders hold their art (missing-effects.tsv "the GRF has its files but not when / where they
# play"): the files are theirs; Loki decides the moment and the anchor from each file's name:
#   *_cast*            the caster, when the cast bar starts (0x0b1a), or at the use when the skill has no cast time
#   *_hit* / *_hitsub  each damaged target, per hit (0x01de, + attackMT + 200 ms · i)
#   efst_*             the target when the skill lands (its status look)
#   *_count_NN         left out (a status counter, not the skill)
#   a numbered set _1.._N with N = the skill's MaxLevel: the file of the used level; other numbered sets play together
#   everything else    the skill's main anchor when it lands: the target (Attack / Support), the cell (Ground), the caster (Self)
# min_* files are the GRF's reduced copies: used only when a skill has nothing else.
def grf_recipe(files, f):
    rows = []; full = [x for x in files if not x[1].split(BS)[-1].startswith('min_')]
    use = full or files
    main_anchor = 'cell' if f['target'] == 'Ground' else 'target' if f['target'] in ('Attack', 'Support') else 'caster'
    numbered = {}
    for kind, rel in use:
        leaf = rel.split(BS)[-1]
        m = re.match(r'(.*?)_?(\d+)$', leaf)
        if m: numbered.setdefault((rel.rsplit(BS, 1)[0], m.group(1)), []).append((int(m.group(2)), kind, rel))
    for kind, rel in use:
        leaf = rel.split(BS)[-1]; path = rel.lower()
        if re.search(r'_count_\d+$', leaf): continue
        if '_cast' in path or 'casting' in path: phase, anchor = 'cast', 'caster'
        elif re.search(r'_hit(sub)?$', leaf) or BS + 'hit' in path: phase, anchor = 'hit', 'target'
        elif leaf.startswith('efst_'): phase, anchor = 'main', 'target' if main_anchor != 'cell' else 'caster'
        else: phase, anchor = 'main', main_anchor
        lv = 0
        m = re.match(r'(.*?)_?(\d+)$', leaf)
        if m:
            grp = numbered.get((rel.rsplit(BS, 1)[0], m.group(1)), [])
            if len(grp) > 1 and len(grp) == f['maxlv'] and sorted(n for n, _, _ in grp) == list(range(1, len(grp) + 1)): lv = int(m.group(2))
        rows.append('%s:%s:%s:%s:%d' % (phase, anchor, kind, rel.replace(BS, '/'), lv))
    return rows


def main(argv):
    root, grf_path, fx_dir, skills_tsv, missing_tsv, out = argv[1:7]
    import json
    import grf_skill_effects as gse, fx_coverage as fc
    from grf import Grf
    db = yaml.safe_load(open(os.path.join(root, 'db', 're', 'skill_db.yml'), encoding='utf-8'))
    by_id = {e['Id']: e for e in db['Body']}; by_aegis = {e['Name']: e for e in db['Body']}
    g = Grf(grf_path)
    et = json.load(open(os.path.join(fx_dir, 'effect_table.json'), encoding='utf-8'))
    aegis = fc.read_aegis(skills_tsv)
    files = gse.Files(g, gse.taken_files(gse.with_ez2(g, et), set(g.names())))
    classless = gse.classless_map(aegis)
    aegis_of_id = {}
    for k, v in aegis.items(): aegis_of_id.setdefault(v, k)
    skill_of_name = {k.lower(): v for k, v in aegis.items()}
    rows = [l.rstrip('\n').split('\t') for l in open(missing_tsv, encoding='utf-8') if not l.startswith('#')]
    want = [(r[0], r[1], int(r[2]), r[3]) for r in rows if r[0] != 'not needed']
    for a in sorted(EXTRA): want.append(('not needed', 'brief: Star Gladiator Warmth casts (unit UNT_DUMMYSKILL: nothing drawn)', by_aegis[a]['Id'], a))
    o = open(out, 'w', encoding='utf-8', newline='\n')
    o.write('# Loki\'s OWN skill effects (client wave 5, w5fxown): a DESIGN, not the official client\'s look (no source shows that: design notes).\n')
    o.write('# Made by client/tools/fx/build_own_fx.py from rAthena e985006 db/re/skill_db.yml + design notes + the GRF\'s file NAMES.\n')
    o.write('# Read by client/Assets/Loki/Runtime/Fx/OwnSkillFx.cs. Every texture / STR named here is read from the PLAYER\'S GRF at run time; a missing one is skipped.\n')
    o.write('# palette\telement\tname\tr,g,b\tblend\tspark\torb\tcolumn\tpuff\tsigil\n')
    for p in PALETTE: o.write('palette\t' + '\t'.join(str(x) for x in p) + '\n')
    o.write('# skill\tid\taegis\tverdict\trecipe\targ\tpalette\tkind\ttarget\ttype\trange\tsplash\thits\tmaxlv\tstatus\tfiles (phase:anchor:kind:file:level;...)\n')
    stats = {}
    for verdict, reason, sid, a in sorted(want, key=lambda x: x[2]):
        if a in W5LOOKS: continue
        e = by_id[sid]; f = facts(e)
        recipe, kind, arg = template(a, f)
        grf_rows = []
        if a in SPIRITS: recipe, kind, arg = 'spirit', 'buff', SPIRITS[a][0] + '|' + SPIRITS[a][1]
        elif a in HAND:
            recipe, arg = HAND[a][0], HAND[a][1]
            if recipe == 'tk_sevenwind':  # Mild Wind's element per level (skill_db.yml TK_SEVENWIND Element list): the argument
                arg = ','.join(str(ELEMENTS[x['Element']]) for x in e['Element'])
        else:
            arg = ARGS.get(a, arg)
            if 'GRF has its files' in reason:
                fs = sorted(x for x in files.named(gse.skill_names(aegis_of_id, sid, classless), sid, skill_of_name) if files.drawable(x))
                grf_rows = grf_recipe(fs, f)
            if a in GRF_DROP: grf_rows = [r for r in grf_rows if not re.search(GRF_DROP[a], r.split(':')[3])]
            if a in GRF_ADD: grf_rows = [r for r in grf_rows if r not in GRF_ADD[a]] + GRF_ADD[a]
            for r in grf_rows:  # every file named must be a drawable one of this GRF (the table is checked against it here)
                kind_, rel_ = r.split(':')[2], r.split(':')[3].replace('/', BS)
                if not files.drawable((kind_, rel_)) and (kind_, rel_) not in [(k, x) for k, x in files.by_base.get(rel_.split(BS)[-1], set())]:
                    raise SystemExit('not a drawable GRF file: %s (%s)' % (r, a))
            if grf_rows: arg = recipe; recipe = 'grf'  # the template stays as the argument: its parts draw where the files are missing
        pal = palette_of(a, f, kind)
        stats[recipe] = stats.get(recipe, 0) + 1
        o.write('\t'.join(str(x) for x in ['skill', sid, a, verdict, recipe, arg, pal, kind, f['target'], f['type'], f['range'] or 0, f['splash'] or 0,
                                           f['hits'] or 1, f['maxlv'], f['status'], ';'.join(grf_rows)]) + '\n')
    o.close()
    print('skills:', sum(stats.values()), ' recipes:', ', '.join('%s %d' % kv for kv in sorted(stats.items())))


if __name__ == '__main__':
    main(sys.argv)
