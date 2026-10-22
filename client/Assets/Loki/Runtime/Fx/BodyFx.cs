using System.Collections.Generic;
using Loki.Sprites;
using UnityEngine;

namespace Loki.Effects
{
    /// <summary>
    /// Client wave 5 (w5looks): the FUNC parts of roBrowserLegacy's effect table that animate a unit's body instead of drawing a part —
    /// colour flashes, the halo, the body size, a turn, an after-image trail — and the ones that shake the camera or play timed sounds.
    /// roBrowserLegacy (a reimplementation; the official client could not be checked) is the only source: each kind below is its FUNC,
    /// DB/Effects/EffectTable.js line by line, keyed by effect id and part index (the JSON keeps rB's part order; Loki's data drops the JS
    /// bodies, tools/fx/build_fx_tables.py). rB runs a FUNC when the part is spammed (Renderer/EffectManager.js:725-735, an attached part
    /// only when its owner exists), even when the instance's startTick lies ahead (a skill's hit: Engine/MapEngine/Entity.js:1688-1702
    /// spams at Renderer.tick + attackMT): most add an entity animation whose callback gets the ms since it was added — the spam — and
    /// ends when it returns true (Renderer/Entity/EntityAnimations.js:28-51); the quakes and the Sonic Blow turn use startTick themselves.
    /// So EffectManager starts these parts at the spam (<see cref="EffectManager.Instance.SpamTime"/>), never as repeating instances (rB
    /// keeps nothing of a FUNC that adds no renderer, so a persistent ground effect runs it once).
    /// Colours: rB's entity._flashColor, one factor of its effectColor product (Renderer/Entity/EntityState.js:34-59) = ActorSprite's
    /// <see cref="ActorSprite.TintFactor.Flash"/> (w3status composes the same product); like rB, several flashes on one unit share it (the
    /// last write wins, each end writes white). The halo: entity._enableHalo, drawn with Assumptio's (EntityRender.js:878 →
    /// <see cref="UnitVirtueFx.FuncHalo"/>). The size: entity.xSize / ySize ÷ 5 (<see cref="Unit.SetFxScale"/>).
    /// </summary>
    public sealed class BodyFx : FxRenderer
    {
        public enum Kind
        {
            None, SonicBlowTurn, MakeBlur, EnergyDrainOwner, EnergyDrainOther, Bash3d, Bash3d2, TransBlueBody, MagicCrasher, Bash3d3, Bash3d4,
            Portal5, MagicCrasher2, BabyBody, BabyBody2, GiantBody, GiantBody2, TetraCasting, Bash3d6, RedFadeIn, SpiralPierceCast, RushWindmill,
            SwingDance, MaximizePowerSounds, Quake
        }

        private struct Spec { public Kind Kind; public int QuakeDelayMs, QuakeDurationMs; public string Where; }

        /// <summary>"key#part" → what the FUNC does (EffectTable.js lines; part = the index in the entry's list).</summary>
        private static readonly Dictionary<string, Spec> Table = new Dictionary<string, Spec>
        {
            // body animations
            { "122#0", new Spec { Kind = Kind.SonicBlowTurn, Where = "3526-3545" } },          // EF_SONICBLOWHIT (AS_SONICBLOW hitEffectId)
            { "166#0", new Spec { Kind = Kind.MakeBlur, Where = "4066-4084" } },               // EF_MAKEBLUR (MO_BODYRELOCATION)
            { "217#1", new Spec { Kind = Kind.EnergyDrainOwner, Where = "4566-4614" } },       // EF_ENERGYDRAIN (HW_SOULDRAIN / NPC_ENERGYDRAIN on the caster)
            { "217#2", new Spec { Kind = Kind.EnergyDrainOther, Where = "4615-4659" } },       //   … and its other unit
            { "366#0", new Spec { Kind = Kind.Bash3d, Where = "8176-8206" } },                 // EF_BASH3D (PA_SACRIFICE)
            { "377#0", new Spec { Kind = Kind.Bash3d2, Where = "8405-8435" } },                // EF_BASH3D2 (CH_TIGERFIST)
            { "379#0", new Spec { Kind = Kind.TransBlueBody, Where = "8509-8563" } },          // EF_TRANSBLUEBODY (PF_HPCONVERSION success on the caster)
            { "380#0", new Spec { Kind = Kind.MagicCrasher, Where = "8565-8591" } },           // EF_MAGICCRASHER (HW_MAGICCRASHER, Stave Crasher)
            { "399#0", new Spec { Kind = Kind.Bash3d3, Where = "8761-8806" } },                // EF_BASH3D3 (LK_HEADCRUSH cast)
            { "400#0", new Spec { Kind = Kind.Bash3d4, Where = "8852-8872" } },                // EF_BASH3D4 (LK_JOINTBEAT cast)
            { "402#0", new Spec { Kind = Kind.Portal5, Where = "8927-8967" } },                // EF_PORTAL5 (CH_SOULCOLLECT cast)
            { "403#0", new Spec { Kind = Kind.MagicCrasher2, Where = "8969-8994" } },          // EF_MAGICCRASHER2 (PF_MINDBREAKER success)
            { "420#0", new Spec { Kind = Kind.BabyBody, Where = "9187-9205" } },               // EF_BABYBODY (@size 1, SL_SWOO success)
            { "421#0", new Spec { Kind = Kind.BabyBody2, Where = "9207-9218" } },              // EF_BABYBODY2 (a small unit coming into view)
            { "422#0", new Spec { Kind = Kind.GiantBody, Where = "9220-9238" } },              // EF_GIANTBODY (@size 2)
            { "423#0", new Spec { Kind = Kind.GiantBody2, Where = "9240-9251" } },             // EF_GIANTBODY2 (a big unit coming into view)
            { "805#0", new Spec { Kind = Kind.TetraCasting, Where = "13643-13668" } },         // EF_TETRACASTING (WL_TETRAVORTEX cast)
            { "885#0", new Spec { Kind = Kind.Bash3d6, Where = "14221-14251" } },              // EF_BASH3D6 (no skill names it: a server's specialeffect)
            { "1111#0", new Spec { Kind = Kind.RedFadeIn, Where = "14851-14877" } },           // 1111 (no rAthena EF name, no skill: a server's specialeffect)
            { "339_beforecast#0", new Spec { Kind = Kind.SpiralPierceCast, Where = "7948-7977" } }, // LK_SPIRALPIERCE / ML_SPIRALPIERCE cast
            { "ef_rush_windmill#4", new Spec { Kind = Kind.RushWindmill, Where = "15219-15249" } }, // MI_RUSH_WINDMILL
            { "ef_swing_dance#4", new Spec { Kind = Kind.SwingDance, Where = "15320-15350" } },     // WA_SWING_DANCE
            { "maximize_power_sounds#0", new Spec { Kind = Kind.MaximizePowerSounds, Where = "15081-15100" } }, // BS_MAXIMIZE cast
            // Camera.setQuake(start, duration) — Renderer/Camera.js:212-222, defaults 650 ms; played only with Comfort.CameraShake (Fx/CameraQuake.cs)
            { "79#6", Q(0, 200, "3016-3022") },        // EF_EARTHSPIKE
            { "92#1", Q(600, 0, "3155-3161") },        // EF_METEORSTORM
            { "102#1", Q(350, 0, "3327-3333") },       // EF_CRASHEARTH
            { "142#2", Q(0, 200, "3863-3870") },       // EF_HEAVENSDRIVE
            { "183#1", Q(0, 200, "4160-4167") },       // EF_SUI_EXPLOSION
            { "220#1", Q(200, 200, "4692-4699") },     // EF_POTION_BERSERK
            { "303#0", Q(132, 0, "6386-6392") },       // EF_CHEMICAL2
            { "365#2", Q(500, 0, "8167-8173") },       // EF_PRESSURE
            { "380#1", Q(300, 0, "8594-8600") },       // EF_MAGICCRASHER
            { "387#1", Q(300, 0, "8669-8675") },       // EF_FALCONASSAULT
            { "512#0", Q(132, 0, "10567-10573") },     // EF_CHEMICAL2DASH
            { "522_ground#1", Q(0, 200, "10661-10668") }, // HW_GRAVITATION's ground effect
            { "537#0", Q(200, 0, "10913-10919") },     // EF_ACIDDEMON
            { "563#0", Q(0, 200, "10981-10988") },     // EF_SCREEN_QUAKE
            { "666#1", Q(0, 0, "11797-11805") },       // EF_NPC_EARTHQUAKE (duplicate 3, 35 ms apart)
            { "668#1", Q(0, 0, "11819-11825") },       // EF_DRAGONFEAR
            { "732#1", Q(0, 200, "12367-12374") },     // EF_EARTHWALL
            { "quake#0", Q(0, 0, "16225-16231") },     // 'quake' (MO_STEELBODY, MO_EXPLOSIONSPIRITS, MO_COMBOFINISH, LK_BERSERK, LK_FURY, CH_PALMSTRIKE, CH_TIGERFIST, SG_FUSION, SL_SKA, MS_BERSERK)
            { "quake_magnum#0", Q(0, 50, "16236-16243") }, // 'quake_magnum' (SM_MAGNUM, MS_MAGNUM)
        };
        private static Spec Q(int delay, int duration, string where) => new Spec { Kind = Kind.Quake, QuakeDelayMs = delay, QuakeDurationMs = duration, Where = where };

        /// <summary>Whether part <paramref name="partIndex"/> of effect <paramref name="key"/> is one of these FUNCs.</summary>
        public static bool Handles(string key, int partIndex) => partIndex >= 0 && Table.ContainsKey(key + "#" + partIndex);
        public static Kind KindOf(string key, int partIndex) => Table.TryGetValue(key + "#" + partIndex, out var s) ? s.Kind : Kind.None;
        /// <summary>EF_BABYBODY 420, EF_BABYBODY2 421, EF_GIANTBODY 422, EF_GIANTBODY2 423: a unit's size, not a passing effect (EffectManager's
        /// "Skill effects" density setting never leaves them out).</summary>
        public static bool IsBodySize(string key) => key == "420" || key == "421" || key == "422" || key == "423";
        /// <summary>Every (key, part, kind, EffectTable.js lines) handled — smoke and the progress file.</summary>
        public static IEnumerable<(string keyPart, Kind kind, string lines)> All { get { foreach (var kv in Table) yield return (kv.Key, kv.Value.Kind, kv.Value.Where); } }
        /// <summary>Instances started per kind (smoke).</summary>
        public static readonly Dictionary<Kind, int> Started = new Dictionary<Kind, int>();
        public static event System.Action<BodyFx> StartedFx;

        private Spec _spec; private Unit _u; private float _size0 = 1f; private int _sounds; private bool _halo; private float _spamTime;
        public Kind What => _spec.Kind;
        public Unit Target => _u;
        /// <summary>When the FUNC ran (rB's animation tick 0) and the ms since then.</summary>
        public float SpamTime => _spamTime;
        public float TickMs => Tick;

        public static BodyFx Create(EffectManager m, EffectManager.Instance inst, int partIndex)
        {
            if (!Table.TryGetValue(inst.Key + "#" + partIndex, out var spec)) return null;
            // the unit the FUNC animates: Params.Init.ownerEntity, or otherEntity for EF_ENERGYDRAIN's second part (EffectTable.js:4618)
            var u = spec.Kind == Kind.EnergyDrainOther ? inst.Other : inst.Owner;
            if (spec.Kind != Kind.Quake && spec.Kind != Kind.MaximizePowerSounds && (u == null || u.Sprite == null)) return null; // rB: `if (!entity) return true`
            var f = Make<BodyFx>(m, inst, "body:" + inst.Key + "#" + partIndex);
            f._spec = spec; f._u = u; f._spamTime = inst.SpamTime;
            Started[spec.Kind] = (Started.TryGetValue(spec.Kind, out var n) ? n : 0) + 1;
            f.Begin();
            return f;
        }

        public override bool EndsAtEndTick => false;

        private ActorSprite S => _u != null ? _u.Sprite : null;
        private void Flash(float r, float g, float b, float a)
        {
            // rB writes the factor unclamped; a WebGL fixed-point target clamps every fragment to 0..1 before blending, so clamping the
            // factor here gives the same pixels (no other factor of the product goes negative or above 1)
            if (S != null) S.SetTint(ActorSprite.TintFactor.Flash, new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), Mathf.Clamp01(a)));
        }
        private void White() => Flash(1f, 1f, 1f, 1f);
        private Color Cur => S != null ? S.GetTint(ActorSprite.TintFactor.Flash) : Color.white;
        private void Halo(bool on) { if (_u == null) return; _halo = on; UnitVirtueFx.Of(_u).FuncHalo = on; }
        private bool Casting => _u != null && _u.Overhead != null && _u.Overhead.CastShown; // rB entity.cast.display (EntityCast.js:55-72, 110-125)
        private void Done() { Finished = true; }

        /// <summary>The ms since the FUNC ran (the spam): rB's animation tick.</summary>
        private float Tick => (Time.time - _spamTime) * 1000f;

        private void Begin()
        {
            StartedFx?.Invoke(this); // before it acts (smoke: the state it starts from)
            // what the FUNC does when it is called, before its first animation tick
            switch (_spec.Kind)
            {
                case Kind.BabyBody: case Kind.GiantBody: _size0 = S.Scale; break; // const xSize = entity.xSize (:9191, :9224)
                case Kind.BabyBody2: _u.SetFxScale(2.5f / 5f); Done(); break;     // entity.xSize = entity.ySize = 2.5 (:9211-9213)
                case Kind.GiantBody2: _u.SetFxScale(7.5f / 5f); Done(); break;    // 7.5 (:9244-9246)
                case Kind.RedFadeIn: Flash(1f, 0f, 0f, 0f); break;                 // (:14856-14860)
                case Kind.SpiralPierceCast: White(); break;                        // (:7953-7957)
                case Kind.MakeBlur:
                    // entity._enableTrail = true; _blurType = 1; setTimeout(off, Params.Init.duration || 2000) (:4069-4082): no skill effect passes a duration
                    UnitVirtueFx.Of(_u).StartFuncTrail(Inst.End > 0 ? Inst.End - Inst.Start : 2f);
                    Done(); break;
                case Kind.Quake:
                    // Camera.setQuake(Params.Inst.startTick + delay, duration) — startTick: the instance's (a hit's lies ahead)
                    CameraQuake.Request(Inst.Start + _spec.QuakeDelayMs / 1000f, _spec.QuakeDurationMs, Inst.Key);
                    Done(); break;
            }
        }

        private void LateUpdate()
        {
            if (Finished) return;
            if (_spec.Kind != Kind.MaximizePowerSounds && (_u == null || _u.gameObject == null || S == null)) { Finished = true; return; }
            float t = Tick;
            switch (_spec.Kind)
            {
                case Kind.SonicBlowTurn:
                    // `const delay = Params.Inst.startTick - Renderer.tick; … if (tick >= delay) { entity.direction = (entity.direction + 2) % 8;
                    // return true; }` (:3528-3542): once, when the hit's startTick comes
                    if (Time.time < Inst.Start) break;
                    S.Direction = (S.Direction + 2) % 8; Done(); break;
                case Kind.EnergyDrainOwner:
                    if (t < 500f) break; // "Wait for drain particles" (:4572-4575)
                    DrainBlue(t - 500f, false); break;
                case Kind.EnergyDrainOther: DrainBlue(t, false); break;
                case Kind.TransBlueBody:
                    if (t < 500f) break;
                    Halo(true); // BL_DOUBLE_BODY (:8521)
                    DrainBlue(t - 500f, true); break;
                case Kind.Bash3d: Window(t, 150f, 450f, 500f, 1f, 0.78f, 0.78f); break;   // SetArgb(-1, 255, 200, 200) (:8183-8205)
                case Kind.Bash3d2: Window(t, 50f, 350f, 400f, 1f, 0.39f, 0.39f); break;   // SetArgb(-1, 255, 100, 100) (:8412-8434)
                case Kind.Bash3d6: Window(t, 150f, 450f, 500f, 0.5f, 0.5f, 1f); break;    // blue variant (:14228-14250)
                case Kind.Bash3d4:
                    Halo(t >= 150f && t <= 500f); // (:8859-8869)
                    if (t > 600f) { Halo(false); Done(); }
                    break;
                case Kind.MagicCrasher:
                    if (t < 1000f) { if (t > 500f) Flash(Random.value, Random.value, Random.value, 0.6f); } // (:8571-8578)
                    else { White(); Done(); }
                    break;
                case Kind.MagicCrasher2:
                    if (t < 1000f) { Flash(Random.value, Random.value, Random.value, Cur.a); } // alpha left as it is (:8975-8979)
                    else { White(); Done(); }
                    break;
                case Kind.Bash3d3:
                {
                    Halo(true); // (:8768)
                    if (t < 200f) { Flash(1f, 1f, (255f - (t + 50f)) / 255f, 1f); break; } // (:8770-8778)
                    if (t < 500f) { float sv = 5f / 255f, p = (t - 200f) / 300f; Flash(1f, 1f, sv + (1f - sv) * p, 1f); break; } // (:8781-8790)
                    Halo(false); White(); Done(); break;
                }
                case Kind.Portal5:
                    if (t < 200f) { Flash(1f, 1f, 0f, 0.1f); break; } // (:8935-8941)
                    if (t < 800f) { float p = (t - 200f) / 600f; Flash(1f, 1f, p, 0.1f + 0.9f * p); break; } // (:8945-8953)
                    White(); Done(); break;
                case Kind.BabyBody: Grow(t, 2.5f); break;  // xSize + (2.5 − xSize) · min(tick, 300) / 300; done after 300 ms (:9194-9201)
                case Kind.GiantBody: Grow(t, 7.5f); break; // 7.5 (:9227-9234)
                case Kind.TetraCasting:
                    if (!Casting) { White(); Done(); break; } // "we don't know cast time here so.. let's hack" (:13649-13657)
                    Flash(Random.value, Random.value, Random.value, 0.5f + Random.value / 2f); break; // (:13659-13663)
                case Kind.SpiralPierceCast:
                {
                    if (!Casting) { White(); Done(); break; } // (:7960-7967)
                    float v = Mathf.Sin(t / (6f * Mathf.PI)); var c = Cur; Flash(c.r, v, v, c.a); break; // _flashColor[1] = _flashColor[2] = sin(tick / (6π)) (:7969-7971)
                }
                case Kind.RedFadeIn:
                    if (t > 300f) { White(); Done(); break; } // (:14866-14873)
                    Flash(1f, 0f, 0f, t / 100f); break;      // _flashColor[3] = tick / 100 (:14864)
                case Kind.SwingDance: Flicker(t, 1f, 0.1f, 0.5f, 0.4f); break; // (:15323-15349)
                case Kind.RushWindmill:
                    // rB's FUNC takes `Params.Init` as the entity (:15220) and so throws at `entity.animations.add`: nothing is drawn there. Loki
                    // follows the evident intent — the same flicker as ef_swing_dance on the owner (as w4fx took AbyssEffects' list, tools/fx/dump_fx.mjs)
                    Flicker(t, 1f, 1f, 1f, 0.4f); break;
                case Kind.MaximizePowerSounds:
                {
                    // Events.setTimeout(SoundManager.play(…), 1 / 550 / 700 / 950) (:15084-15097): not positional (SoundManager.play)
                    var at = new[] { 1f, 550f, 700f, 950f };
                    var wav = new[] { "effect\\black_maximize_power_circle.wav", "effect\\black_maximize_power_sword.wav", "effect\\black_maximize_power_sword.wav", "effect\\black_maximize_power_sword_bic.wav" };
                    while (_sounds < at.Length && t >= at[_sounds]) { Audio.LokiAudio.Instance?.PlayWav(wav[_sounds]); SoundsPlayed++; _sounds++; }
                    if (_sounds >= at.Length) Done();
                    break;
                }
            }
        }

        /// <summary>Timed sounds this renderer played (smoke).</summary>
        public static int SoundsPlayed;

        /// <summary>EF_ENERGYDRAIN / EF_TRANSBLUEBODY: "SetArgb(-1, 255-(m_stateCnt+50), 255-(m_stateCnt+50), 255)" for 200 ms, then 200 ms back to white.</summary>
        private void DrainBlue(float time, bool halo)
        {
            if (time < 200f) { float v = (255f - (time + 50f)) / 255f; Flash(v, v, 1f, 1f); return; }
            if (time < 400f) { float sv = 5f / 255f, p = (time - 200f) / 200f, v = sv + (1f - sv) * p; Flash(v, v, 1f, 1f); return; }
            if (halo) Halo(false);
            White(); Done();
        }

        /// <summary>EF_BASH3D / 3D2 / 3D6: halo and colour between <paramref name="a"/> and <paramref name="b"/> ms, white outside; ends after <paramref name="end"/>.</summary>
        private void Window(float t, float a, float b, float end, float r, float g, float bl)
        {
            bool on = t >= a && t <= b;
            Halo(on);
            if (on) Flash(r, g, bl, 1f); else White();
            if (t > end) { Halo(false); Done(); } // `return tick > end` after the colour was set
        }

        /// <summary>ef_swing_dance / ef_rush_windmill: for 500 ms, on even ms the colour, on odd ones white (`tick % 2 == 0`), then white.</summary>
        private void Flicker(float t, float r, float g, float b, float a)
        {
            if (t >= 500f) { White(); Done(); return; }
            if (((int)t) % 2 == 0) Flash(r, g, b, a); else White();
        }

        private void Grow(float t, float target)
        {
            float x0 = _size0 * 5f; // xSize
            float x = x0 + (target - x0) * (Mathf.Min(t, 300f) / 300f);
            _u.SetFxScale(x / 5f);
            if (t > 300f) Done();
        }

        private void OnDestroy()
        {
            // removed before its end (map change, the owner's effects cleared): leave the unit as the animation would have left it
            if (_u == null || _u.gameObject == null || Finished) return;
            switch (_spec.Kind)
            {
                case Kind.BabyBody: _u.SetFxScale(0.5f); break;
                case Kind.GiantBody: _u.SetFxScale(1.5f); break;
                case Kind.MaximizePowerSounds: case Kind.SonicBlowTurn: case Kind.Quake: case Kind.MakeBlur: break;
                default: if (S != null) S.SetTint(ActorSprite.TintFactor.Flash, Color.white); break;
            }
            if (_halo) UnitVirtueFx.Of(_u).FuncHalo = false;
        }
    }
}
