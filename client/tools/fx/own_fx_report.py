#!/usr/bin/env python3
r"""Client wave 5 (w5fxown): the per-skill table of LOKI'S OWN skill effects (a design, not the official look) for design notes
— skill, recipe, art used — from the table the client reads (client/Assets/Loki/Resources/Fx/loki_own_fx.txt, build_own_fx.py).

  python own_fx_report.py <loki_own_fx.txt> > table.md

The art named per recipe is what Fx/OwnSkillFx.cs draws (keep the two in step): the palette's GRF textures (spark / orb / column / puff /
sigil, data\texture\effect\...), the template's shared ones, the hand-made recipes' own picks, and the 4th-job GRF files (recipe grf).
"""
import sys

SHARED = {
    'strike': ['yellowslash.tga', '{spark}', 'glow11.bmp', '(splash) explosive.tga'],
    'shot': ['(code) streak', '{spark}', 'glow11.bmp', '(splash) explosive.tga'],
    'bolt': ['{orb}', '{spark}', 'glow11.bmp', 'ring_b.bmp', '(splash) explosive.tga'],
    'nova': ['explosive.tga', '{column}', '{spark}'],
    'field': ['{sigil}', '{spark}', 'explosive.tga'],
    'aura': ['ring_b.bmp', '{column}', '{spark}'],
    'song': ['ring_b.bmp', 'glow11.bmp', '{notes}'],
    'bless': ['glow11.bmp', '{spark}', 'ring_b.bmp'],
    'heal': ['glow11.bmp', 'pikapika_white.tga', 'ring_star.tga', '{column}'],
    'curse': ['{spark}', 'ring_b.bmp', '{puff}'],
    'mark': ['ring_b.bmp', 'glow11.bmp', '{spark}'],
    'pulse': ['ring_b.bmp', '{spark}'],
    'zone': ['{sigil}', 'ring_b.bmp'],
}
HAND = {
    'spirit': 'magic_target.tga, pikapika_lilac.tga, ring_white.tga, {class sparkle}, glow11.bmp, (code) ring',
    'kaite': 'glow11.bmp, pikapika_lilac.tga, explosive.tga, icearrow.tga, pikapika_lilac.tga',
    'kaupe': 'glow11.bmp, pikapika_cyan.tga, feather.bmp, ring_b.bmp',
    'eske': 'fireparticle.tga, ring_b.bmp, glow11.bmp',
    'tk_storm': 'yellowslash.tga, explosive.tga, smoke.tga, pikapika_white.tga',
    'tk_down': 'yellowslash.tga, explosive.tga, stone-1.bmp, glow11.bmp',
    'tk_turn': 'yellowslash.tga, explosive.tga, pikapika_white.tga',
    'tk_counter': 'yellowslash.tga, pikapika_white.tga',
    'tk_ready': 'ring_b.bmp, pikapika_white.tga',
    'tk_dodge': 'yellowslash.tga, ring_b.bmp',
    'tk_sevenwind': 'the level\'s element spark (skill_db: Earth, Wind, Water, Fire, Ghost, Dark, Holy), ring_b.bmp',
    'sg_comfort': 'glow01.bmp (sun) / (code) crescent (moon) / pikapika_ultramarine.tga (star), magic_target.tga, ring_star.tga / pikapika_white.tga',
    'sg_warm': 'glow01.bmp / (code) crescent / pikapika_ultramarine.tga, ring_b.bmp, ring_yellow.tga',
    'sg_feel': 'glow01.bmp / (code) crescent / pikapika_ultramarine.tga, magic_y_01.tga',
    'sg_hate': 'glow01.bmp / (code) crescent / pikapika_ultramarine.tga, ring_b.bmp',
    'napalm': 'ghost01.bmp, pikapika_lilac.tga, glow11.bmp, ring_b.bmp',
    'ganbantein': 'magic_ring_a.bmp, ring_white.tga, pikapika_white.tga, glow11.bmp',
    'mirror': 'the caster\'s own sprite (afterimages), smoke.tga',
    'gatling': 'line3.tga, fireparticle.tga, ring_b.bmp',
    'tarot': 'tarot01-04.tga, pikapika_violet.tga, glow11.bmp',
    'cartterm': 'yellowslash.tga, explosive.tga, efst_gold_shower\\coin001-005.bmp, smoke.tga',
}
TPL_TEXT = {'strike': 'strike (slash, sparks, flash)', 'shot': 'shot (streak, sparks)', 'bolt': 'bolt (orb travels, burst)', 'nova': 'nova (shock ring round the caster)',
            'field': 'field (sigil, falling sparks, ring)', 'aura': 'aura (ring, column, rising motes)', 'song': 'song (area ring, motes / notes)',
            'bless': 'bless (link, light descending)', 'heal': 'heal (rising stars, soft glow)', 'curse': 'curse (dark motes closing in)',
            'mark': 'mark (ring, flash)', 'pulse': 'pulse (small ripple)', 'zone': 'zone (ground sigil)'}


def main(path):
    pal = {}; rows = []
    for ln in open(path, encoding='utf-8'):
        c = ln.rstrip('\n').split('\t')
        if c[0] == 'palette': pal[int(c[1])] = dict(name=c[2], spark=c[5], orb=c[6], column=c[7], puff=c[8], sigil=c[9])
        elif c[0] == 'skill': rows.append(c)
    print('| id | skill (aegis) | verdict | recipe (Loki\'s own design) | palette | art (GRF textures / files, read at run time) |')
    print('|---|---|---|---|---|---|')
    for c in rows:
        sid, aegis, verdict, recipe, arg, p = c[1], c[2], c[3], c[4], c[5], int(c[6])
        P = pal.get(p, pal[0])
        def art_of(tpl):
            out = []
            for a in SHARED.get(tpl, []):
                if a == '{notes}': a = 'c_melody_wing\\note1-3.bmp' if arg == 'notes' else P['spark']
                elif a.startswith('{'): a = P[a.strip('{}')]
                if a.startswith('(splash)') and c[11] in ('0', ''): continue
                out.append(a)
            return ', '.join(out)
        if recipe == 'grf':
            files = [f.split(':') for f in c[15].split(';') if f]
            art = '; '.join('%s %s on the %s%s' % (f[0], f[3].split('/')[-1], f[1], ' (level %s)' % f[4] if f[4] != '0' else '') for f in files) + ' — a phase without a file: the %s template' % arg
            rtext = 'grf (4th-job folder; template %s)' % arg
        elif recipe in HAND:
            art = HAND[recipe].replace('{class sparkle}', arg.split('|')[0] if recipe == 'spirit' else '')
            rtext = 'hand-made: ' + recipe + (' (' + arg.split('|')[0].replace('pikapika_', '').replace('.tga', '') + ')' if recipe == 'spirit' else (' (' + arg + ')' if arg and recipe != 'tk_sevenwind' else ''))
        else:
            art = art_of(recipe)
            rtext = TPL_TEXT.get(recipe, recipe) + (' — four orbs' if arg == 'multi' else '') + (' — notes' if arg == 'notes' else '')
        if verdict == 'not needed': verdict = 'not needed (the brief names it: Warmth)'
        print('| %s | %s | %s | %s | %s | %s |' % (sid, aegis, verdict, rtext, P['name'] if recipe not in HAND else '-', art))


if __name__ == '__main__':
    main(sys.argv[1])
