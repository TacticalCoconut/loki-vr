using System;
using System.Collections.Generic;
using Loki.Game;
using Loki.Sprites;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 22 (w22tree), the design's point 64 (2026-10-09): "Lasering or hand-hovering over each skill spawns a small ghostly apparition
    /// that plays it (for example, placing my hand on the 'fire wall' skill spawns a tiny mage on top of a piece of ground near my hand,
    /// that casts fire wall, before a poring walks onto it and is affected with its effects [damage and pushback])".
    /// THE PREVIEW: a small see-through scene beside the medallion a pointer rests on: a piece of ground, the player's OWN character (its
    /// job, hair and gear: Sprites/PlayerAppearance.cs, the same sprite code as every unit) and a Poring, both drawn from the player's own
    /// game data at run time (never shipped), playing the skill. LOCAL ONLY: nothing is sent to a server, no unit is made on the map,
    /// no sound.
    ///   A SLOW LOOP     client wave 23 (w23tree), design note 2026-10-11: "should continue to play on a slow loop, like it plays but between
    ///                   loops there is a significant idle period": it PLAYS <see cref="Loop"/> s, then RESTS <see cref="Rest"/> s holding
    ///                   the pose its play ends in (both sprites held on their frame, the effects' picture left as it is: nothing moves),
    ///                   then plays again, for as long as it is shown (UI/SkillTree3D.Panels.cs PanelsTick: while a pointer rests on the
    ///                   medallion, or while the skill is LOCKED = selected, with no pointer on it).
    ///   ITS SOUND       client wave 23 (w23tree), item 24 (design note: "the sound effects coming from the skill preview animations"): the
    ///                   skill's OWN sound(s), as the game plays them when the skill is used (<see cref="SoundsOf"/>: the effect tables
    ///                   the world uses, LokiGame.W22Tree.cs W24SkillSounds; files of the player's own install), on EVERY play of the
    ///                   slow loop (<see cref="PreviewSoundEveryPlay"/>; design note, 2026-10-12: "Every repeat."); the rest starts none.
    ///   What is played  one generic scene per skill KIND (<see cref="Kind"/>: a melee hit, a shot, a bolt from above, an area, a wall
    ///                   on the ground, a buff, a heal, a curse, a trap, hiding, a jump, a passive, a utility) and hand-tuned ones for
    ///                   the best-known skills (<see cref="Tuned"/>, by the skill's own name in the game data: Fire Wall as specified).
    ///                   The kind of any other skill: the server's own flags when it sent the skill (passive / ground / self / support /
    ///                   attack: Game/Progression.cs), Loki's own effect table where it has the skill (Fx/OwnSkillFx.cs: its recipe and
    ///                   colour), the skill's tags (Game/SkillTags.cs), its range and description. FLAGGED: the effects are Loki's own
    ///                   small shapes in the kind's colour, not the game's effect files (those are bound to the map and its units).
    ///   How it is drawn the sprites' frames are the game's own (a hidden ActorSprite animates; its current frame is drawn again with
    ///                   the tree's shader, tinted pale and see-through, over the tree: no depth test, as the tree); the ground and the
    ///                   effects are two small meshes made new each frame while it shows. 4 draw calls, about 200 triangles.
    /// </summary>
    public sealed class SkillDiorama : MonoBehaviour
    {
        public enum Kind { Melee, Shot, Bolt, Area, Wall, Buff, Heal, Curse, Trap, Hide, Jump, Passive, Utility }
        /// <summary>Client wave 26 (w26skill): <see cref="Demo"/> what the characters do to show the skill's GAIN (Game/SkillGains.cs:
        /// dodge, crit, speed, accuracy; None: the kind's scene), <see cref="DemoBuff"/> the skill is cast (a buff) or always on.</summary>
        public struct Spec { public Kind Kind; public Color Col; public int Hits; public float Knock; public bool Freeze, WalkIn; public string How; public SkillGains.Demo Demo; public bool DemoBuff; }

        /// <summary>A cell of the game in the tree's millimetres (a character about 7 cm tall), the ground's radius, the loop's length
        /// and the moments in it: the cast begins, the first hit lands.</summary>
        public const float CellMm = 26f, GroundR = 80f, Loop = 2.9f, TCast = 0.5f, THit = 0.95f;
        /// <summary>Client wave 23 (w23tree): the REST between two plays (seconds; the one constant: the orchestrator's 4 s, the design gave no
        /// number: FLAGGED).</summary>
        public const float Rest = 4f;
        /// <summary>Client wave 23 (w23tree): the box the whole scene stays in, mm from the ground's middle (its backdrop, both sprites,
        /// the tallest effect and the number over it): half its width, its bottom, its top. What must overlap no panel as the eyes see
        /// it (UI/SkillTree3D.Panels.cs PreviewFreeAt).</summary>
        public const float BoxHalfW = GroundR + 8f, BoxBottom = -36f, BoxTop = 142f;
        /// <summary>Client wave 26 (w26skill): THE CAPTION over the scene (what the skill gains the player, Game/SkillGains.cs): at most
        /// <see cref="CaptionLines"/> lines, the first one's middle <see cref="CaptionTop"/> mm over the ground, <see cref="CaptionPitch"/>
        /// mm apart, letters <see cref="CaptionPx"/> px (0.5 mm each: 14 mm, 1.2 degrees from 0.65 m; shrunk to fit a long line, never
        /// under <see cref="CaptionMinPx"/>); "Lv N" small at its left when the skill is not learned (the numbers are level 1's). The box's
        /// top (<see cref="BoxTop"/>, 110 before) is 32 mm higher for it.</summary>
        public const int CaptionLines = 3, CaptionPx = 28, CaptionMinPx = 20, CaptionLvPx = 22;
        public const float CaptionTop = 132f, CaptionPitch = 15f, CaptionX = 16f, CaptionW = 142f, CaptionLvX = -72f, CaptionFullW = 172f;
        /// <summary>A line whose text with its condition is longer than this many letters gets its condition on a line of its own under
        /// it, when there is a line left ("DEF +70" / "(vs undead, demon)"; "ATK +40 (dagger, sword)" stays one line).</summary>
        public const int CaptionSplitChars = 24;
        /// <summary>Client wave 23 (w23tree), item 24: the scene's sound(s) start again on every play of the slow loop (design note,
        /// 2026-10-12: "Every repeat."; false: on the first play after a lock or a hover begins only). The rest starts none.</summary>
        public const bool PreviewSoundEveryPlay = true;
        /// <summary>The ghosts' tints: the character pale blue-white, the Poring its own pink, both a little see-through.</summary>
        public static readonly Color GhostCaster = new Color(0.82f, 0.94f, 1f, 0.86f), GhostPoring = new Color(1f, 0.95f, 1f, 0.9f);
        /// <summary>Where the two stand on the ground (mm from its middle).</summary>
        public const float CasterX = -35f, PoringX = 35f;
        public static readonly Color Fire = new Color(1f, 0.48f, 0.18f), Ice = new Color(0.5f, 0.82f, 1f), Wind = new Color(1f, 0.95f, 0.45f), Earth = new Color(0.72f, 0.58f, 0.36f),
            Holy = new Color(1f, 0.96f, 0.72f), Dark = new Color(0.68f, 0.44f, 0.9f), Poison = new Color(0.72f, 0.28f, 0.92f), Ghost = new Color(0.8f, 0.82f, 0.95f), Plain = new Color(0.95f, 0.95f, 0.95f),
            Green = new Color(0.45f, 0.95f, 0.5f), Coin = new Color(1f, 0.84f, 0.3f);

        /// <summary>The best-known skills' own scenes, by the skill's name in the game data (AEGIS name).</summary>
        public static readonly Dictionary<string, Spec> Tuned = new Dictionary<string, Spec>
        {
            // Novice, Swordman line
            { "NV_FIRSTAID", S(Kind.Heal, Green) }, { "NV_TRICKDEAD", S(Kind.Hide, Plain, how: "dead") },
            { "SM_BASH", S(Kind.Melee, Fire, 1, 4f) }, { "SM_MAGNUM", S(Kind.Area, Fire, 1, 22f, how: "self") }, { "SM_PROVOKE", S(Kind.Curse, Fire) }, { "SM_ENDURE", S(Kind.Buff, Earth) },
            { "KN_PIERCE", S(Kind.Melee, Plain, 3) }, { "KN_BRANDISHSPEAR", S(Kind.Melee, Plain, 1, 26f) }, { "KN_BOWLINGBASH", S(Kind.Melee, Wind, 2, 30f) }, { "KN_SPEARBOOMERANG", S(Kind.Shot, Plain) },
            { "KN_SPEARSTAB", S(Kind.Melee, Plain, 1, 30f) }, { "KN_TWOHANDQUICKEN", S(Kind.Buff, Wind) }, { "KN_AUTOCOUNTER", S(Kind.Buff, Plain) }, { "LK_SPIRALPIERCE", S(Kind.Shot, Wind, 5) },
            { "LK_BERSERK", S(Kind.Buff, Fire) }, { "CR_HOLYCROSS", S(Kind.Melee, Holy, 2) }, { "CR_GRANDCROSS", S(Kind.Area, Holy, 3, how: "self") }, { "CR_SHIELDBOOMERANG", S(Kind.Shot, Plain) },
            // Mage line
            { "MG_FIREBOLT", S(Kind.Bolt, Fire, 3) }, { "MG_COLDBOLT", S(Kind.Bolt, Ice, 3) }, { "MG_LIGHTNINGBOLT", S(Kind.Bolt, Wind, 3) }, { "MG_NAPALMBEAT", S(Kind.Area, Ghost, 1) },
            { "MG_SOULSTRIKE", S(Kind.Shot, Ghost, 3) }, { "MG_FIREBALL", S(Kind.Shot, Fire, 1, how: "burst") }, { "MG_FIREWALL", S(Kind.Wall, Fire, 2, 24f, walkIn: true) },
            { "MG_FROSTDIVER", S(Kind.Shot, Ice, 1, freeze: true) }, { "MG_STONECURSE", S(Kind.Curse, Earth, freeze: true) }, { "MG_THUNDERSTORM", S(Kind.Area, Wind, 3) },
            { "MG_SAFETYWALL", S(Kind.Buff, new Color(1f, 0.6f, 0.85f), how: "pillar") }, { "MG_SIGHT", S(Kind.Buff, Fire, how: "orbit") }, { "MG_ENERGYCOAT", S(Kind.Buff, Ice) },
            { "WZ_STORMGUST", S(Kind.Area, Ice, 4, 10f, freeze: true) }, { "WZ_METEOR", S(Kind.Area, Fire, 3) }, { "WZ_JUPITEL", S(Kind.Shot, Wind, 3, 34f) }, { "WZ_VERMILION", S(Kind.Area, Wind, 4) },
            { "WZ_FIREPILLAR", S(Kind.Trap, Fire, 2, walkIn: true) }, { "WZ_ICEWALL", S(Kind.Wall, Ice, 0, 0f, walkIn: true) }, { "WZ_QUAGMIRE", S(Kind.Area, Earth, 0) }, { "WZ_EARTHSPIKE", S(Kind.Bolt, Earth, 3, how: "up") },
            { "WZ_HEAVENDRIVE", S(Kind.Area, Earth, 3) }, { "WZ_WATERBALL", S(Kind.Shot, Ice, 4) }, { "WZ_FROSTNOVA", S(Kind.Area, Ice, 1, freeze: true, how: "self") },
            // Acolyte line
            { "AL_HEAL", S(Kind.Heal, Green) }, { "AL_BLESSING", S(Kind.Buff, Holy) }, { "AL_INCAGI", S(Kind.Buff, Wind, how: "up") }, { "AL_DECAGI", S(Kind.Curse, Dark) }, { "AL_ANGELUS", S(Kind.Buff, Holy) },
            { "AL_TELEPORT", S(Kind.Jump, Ice) }, { "AL_WARP", S(Kind.Utility, Ice, how: "portal") }, { "AL_PNEUMA", S(Kind.Buff, Green, how: "pillar") }, { "AL_RUWACH", S(Kind.Buff, Ice, how: "orbit") },
            { "AL_HOLYLIGHT", S(Kind.Bolt, Holy, 1) }, { "AL_CURE", S(Kind.Heal, Holy) }, { "AL_CRUCIS", S(Kind.Curse, Holy) }, { "PR_MAGNUS", S(Kind.Area, Holy, 4) }, { "PR_KYRIE", S(Kind.Buff, Holy, how: "pillar") },
            { "PR_SANCTUARY", S(Kind.Heal, Holy, how: "ground") }, { "PR_LEXAETERNA", S(Kind.Curse, Holy) }, { "PR_TURNUNDEAD", S(Kind.Bolt, Holy, 1) }, { "PR_GLORIA", S(Kind.Buff, Holy) }, { "PR_IMPOSITIO", S(Kind.Buff, Holy) },
            { "MO_FINGEROFFENSIVE", S(Kind.Shot, Holy, 5) }, { "MO_EXTREMITYFIST", S(Kind.Melee, Holy, 1, 8f) }, { "MO_INVESTIGATE", S(Kind.Melee, Earth) }, { "MO_CALLSPIRITS", S(Kind.Buff, Holy, how: "orbit") },
            // Archer line
            { "AC_DOUBLE", S(Kind.Shot, Plain, 2) }, { "AC_SHOWER", S(Kind.Area, Plain, 2, 14f, how: "rain") }, { "AC_CHARGEARROW", S(Kind.Shot, Plain, 1, 36f) }, { "AC_CONCENTRATION", S(Kind.Buff, Wind) },
            { "HT_LANDMINE", S(Kind.Trap, Earth, 1, walkIn: true) }, { "HT_ANKLESNARE", S(Kind.Trap, Plain, 0, walkIn: true, freeze: true) }, { "HT_BLASTMINE", S(Kind.Trap, Wind, 1, 12f, walkIn: true) },
            { "HT_CLAYMORETRAP", S(Kind.Trap, Fire, 1, 12f, walkIn: true) }, { "HT_FREEZINGTRAP", S(Kind.Trap, Ice, 1, walkIn: true, freeze: true) }, { "HT_SANDMAN", S(Kind.Trap, Dark, 0, walkIn: true, freeze: true) },
            { "HT_SKIDTRAP", S(Kind.Trap, Plain, 0, 34f, walkIn: true) }, { "HT_BLITZBEAT", S(Kind.Bolt, Plain, 3, how: "dive") }, { "SN_SHARPSHOOTING", S(Kind.Shot, Wind, 1) }, { "SN_FALCONASSAULT", S(Kind.Bolt, Plain, 1, how: "dive") },
            { "BA_MUSICALSTRIKE", S(Kind.Shot, Plain, 2) }, { "DC_THROWARROW", S(Kind.Shot, Plain, 2) },
            // Thief line
            { "TF_DOUBLE", S(Kind.Melee, Plain, 2) }, { "TF_STEAL", S(Kind.Utility, Coin, how: "take") }, { "TF_HIDING", S(Kind.Hide, Dark) }, { "TF_POISON", S(Kind.Melee, Poison, 1) }, { "TF_DETOXIFY", S(Kind.Heal, Poison) },
            { "TF_BACKSLIDING", S(Kind.Jump, Plain, how: "back") }, { "AS_SONICBLOW", S(Kind.Melee, Plain, 8) }, { "AS_GRIMTOOTH", S(Kind.Shot, Earth, 1, how: "ground") }, { "AS_CLOAKING", S(Kind.Hide, Dark) },
            { "AS_ENCHANTPOISON", S(Kind.Buff, Poison) }, { "AS_VENOMDUST", S(Kind.Trap, Poison, 0, walkIn: true) }, { "ASC_BREAKER", S(Kind.Shot, Dark, 1) }, { "ASC_METEORASSAULT", S(Kind.Area, Dark, 1, how: "self") },
            { "RG_BACKSTAP", S(Kind.Melee, Dark, 1) }, { "RG_RAID", S(Kind.Area, Dark, 1, how: "self") }, { "RG_STEALCOIN", S(Kind.Utility, Coin, how: "take") }, { "RG_INTIMIDATE", S(Kind.Melee, Dark, 1) },
            // Merchant line
            { "MC_MAMMONITE", S(Kind.Melee, Coin, 1, how: "coins") }, { "MC_CARTREVOLUTION", S(Kind.Area, Plain, 1, 22f) }, { "MC_LOUD", S(Kind.Buff, Fire) }, { "MC_VENDING", S(Kind.Utility, Coin) },
            { "BS_HAMMERFALL", S(Kind.Area, Earth, 0, freeze: true, how: "self") }, { "BS_ADRENALINE", S(Kind.Buff, Fire) }, { "BS_WEAPONPERFECT", S(Kind.Buff, Plain) }, { "BS_OVERTHRUST", S(Kind.Buff, Fire) },
            { "BS_MAXIMIZE", S(Kind.Buff, Wind) }, { "AM_ACIDTERROR", S(Kind.Shot, Poison, 1) }, { "AM_DEMONSTRATION", S(Kind.Shot, Fire, 2, how: "burst") }, { "AM_POTIONPITCHER", S(Kind.Heal, Green) },
            { "WS_CARTTERMINATION", S(Kind.Melee, Coin, 1, 6f, how: "coins") }, { "CR_ACIDDEMONSTRATION", S(Kind.Shot, Poison, 4) },
        };
        private static Spec S(Kind k, Color c, int hits = 1, float knock = 0f, bool freeze = false, bool walkIn = false, string how = "")
            => new Spec { Kind = k, Col = c, Hits = hits, Knock = knock, Freeze = freeze, WalkIn = walkIn, How = how };

        // ---- what the game gives it (LokiGame.W22Tree.cs)
        public Func<World.RoAssets> Assets;
        public Func<PlayerLook> Look;
        /// <summary>The server's own entry of a skill (null: it did not send it).</summary>
        public Func<int, Progression.SkillEntry> Entry;
        /// <summary>Loki's own effect table's row for a skill (Fx/OwnSkillFx.cs: kind, target, recipe; null: not there).</summary>
        public Func<int, (string kind, string target, string recipe)?> Own;
        /// <summary>Client wave 23 (item 24): a skill's own sounds for its scene: the moment of the play (s) and the wav's name, in
        /// order of time (empty: the data names none: the scene is silent); and what plays one (the trees' own voices).</summary>
        public Func<int, List<(float at, string wav)>> SoundsOf;
        public Action<string> PlaySound;
        public Action StopSound;
        /// <summary>Sounds started for the scene shown, the last one's name, how many its data names.</summary>
        public int SoundsStarted { get; private set; }
        public string LastSound { get; private set; } = "";
        public int SoundsNamed => _snd != null ? _snd.Count : 0;
        private List<(float at, string wav)> _snd; private int _sndDone;
        /// <summary>Client wave 26 (w26skill): what a skill gains the player now (Game/SkillGains.cs For with the character's numbers;
        /// null: no row), and the gain of the scene shown (null: none: no caption).</summary>
        public Func<int, SkillGains.Gain> GainOf;
        public SkillGains.Gain GainShown { get; private set; }
        /// <summary>The caption's lines as drawn (plain text, no colour tags) and its level tag ("" when none).</summary>
        public string CaptionText { get { var b = new System.Text.StringBuilder(); foreach (var c in _cap) if (c != null && c.text.Length > 0) b.Append(b.Length > 0 ? " | " : "").Append(SkillGains.Plain(c.text)); return b.ToString(); } }
        public string CaptionLevel => _capLv != null ? _capLv.text : "";
        public Text CaptionLine(int i) => i >= 0 && i < _cap.Length ? _cap[i] : null;
        private readonly Text[] _cap = new Text[CaptionLines]; private Text _capLv; private readonly List<string> _rows = new List<string>();

        public bool Shown => gameObject.activeSelf;
        public int Skill { get; private set; }
        public Spec Playing { get; private set; }
        /// <summary>How the scene's kind was found: "tuned", "skill data", "server", "own table", "tags", "description" (smoke, log).</summary>
        public string Source { get; private set; } = "";
        /// <summary>Loops played of the scene shown, previews shown so far; whether the two sprites have a frame drawn now.</summary>
        public int Loops { get; private set; }
        public int Shows { get; private set; }
        /// <summary>Client wave 23: plays begun of the scene shown (1: the first), the rest between two plays is on now, seconds since
        /// this play began (from <see cref="Loop"/> on: resting).</summary>
        public int Plays { get; private set; }
        public bool Resting { get; private set; }
        public float PlayTime => _t;
        public bool CasterDrawn => _caster != null && _caster.CurrentTexture != null && _dCaster != null && _dCaster.enabled;
        public bool PoringDrawn => _poring != null && _poring.CurrentTexture != null && _dPoring != null && _dPoring.enabled;
        public int Triangles => _bBack.Triangles + _bFront.Triangles + 4;
        public const int DrawCalls = 4;
        public string NumberText => _number != null ? _number.text : "";

        private ActorSprite _caster, _poring; private Transform _casterAt, _poringAt;
        private MeshRenderer _dCaster, _dPoring, _rBack, _rFront; private MeshFilter _fCaster, _fPoring;
        private Material _mCaster, _mPoring, _mFx; private Mesh _meshBack, _meshFront;
        private readonly TreeMeshBuilder _bBack = new TreeMeshBuilder(), _bFront = new TreeMeshBuilder();
        private Text _number; private float _t; private int _hitsDone; private bool _castDone, _lookSet; private PlayerLook _lookShown; private int _attackGroup = 5;

        public static SkillDiorama Create(Transform parent)
        {
            var go = new GameObject("SkillDiorama", typeof(RectTransform)); go.layer = UiKit.PanelLayer; go.transform.SetParent(parent, false);
            var d = go.AddComponent<SkillDiorama>();
            d.Build();
            go.SetActive(false);
            return d;
        }

        private static Material Mat(string name, int queue, bool additive, bool texture)
        {
            var sh = Shader.Find(texture ? "Loki/TreeGhost" : "Loki/Tree"); if (sh == null) { Debug.LogWarning("[Tree] the preview's shader is missing"); sh = Shader.Find("Sprites/Default"); }
            var m = new Material(sh) { name = name, renderQueue = queue };
            m.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            return m;
        }

        private MeshRenderer Layer(string name, Material m, out MeshFilter mf)
        {
            var go = new GameObject(name); go.layer = UiKit.PanelLayer; go.transform.SetParent(transform, false);
            mf = go.AddComponent<MeshFilter>(); var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.sortingOrder = SkillTree3D.SortingOrder + 2;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return r;
        }

        /// <summary>Client wave 26 (w26skill): the scene's texts (the caption, the hit's number, "Miss") are drawn by a canvas of their
        /// own one order over the scene's meshes (<see cref="SkillTree3D.SortingOrder"/> + 2): on the trees' canvas (+ 0) the dark
        /// backdrop and the plate behind the caption were drawn over them.</summary>
        public const int TextOrder = SkillTree3D.SortingOrder + 3;
        private void Build()
        {
            _canvas = gameObject.AddComponent<Canvas>(); KeepTextOrder(); // client wave 26 (w26skill)
            _mFx = Mat("LokiTreePreviewFx", 3100, true, false);
            var back = Mat("LokiTreePreviewGround", 3100, false, false);
            _mCaster = Mat("LokiTreePreviewCaster", 3102, false, true); _mPoring = Mat("LokiTreePreviewPoring", 3103, false, true);
            _mCaster.SetColor("_Tint", GhostCaster); _mPoring.SetColor("_Tint", GhostPoring);
            _rBack = Layer("ground", back, out var fb); _meshBack = new Mesh { name = "SkillDiorama:ground" }; _meshBack.MarkDynamic(); fb.sharedMesh = _meshBack;
            _dCaster = Layer("caster", _mCaster, out _fCaster); _dPoring = Layer("poring", _mPoring, out _fPoring);
            var front = new Material(_mFx) { name = "LokiTreePreviewFxFront", renderQueue = 3106 };
            _rFront = Layer("effects", front, out var ff); _meshFront = new Mesh { name = "SkillDiorama:effects" }; _meshFront.MarkDynamic(); ff.sharedMesh = _meshFront;
            _casterAt = Holder("casterAt", CasterX); _poringAt = Holder("poringAt", PoringX);
            _caster = MakeSprite(_casterAt, true); _poring = MakeSprite(_poringAt, false);
            // the hit's number over the Poring (two text pixels a millimetre, as the tree's levels)
            _number = UiKit.Label((RectTransform)transform, "", Vector2.zero, new Vector2(160, 44), 30, TextAnchor.MiddleCenter, Color.white);
            _number.rectTransform.localScale = Vector3.one * SkillTree3D.TextScale; _number.fontStyle = FontStyle.Bold;
            _number.horizontalOverflow = HorizontalWrapMode.Overflow; _number.verticalOverflow = VerticalWrapMode.Overflow;
            // client wave 26 (w26skill): the caption's lines (what the skill gains) and its level tag
            for (int i = 0; i < _cap.Length; i++)
            {
                var c = UiKit.Label((RectTransform)transform, "", Vector2.zero, new Vector2(CaptionW / SkillTree3D.TextScale, CaptionPitch / SkillTree3D.TextScale), CaptionPx, TextAnchor.MiddleCenter, new Color(0.93f, 0.95f, 1f));
                c.rectTransform.localScale = Vector3.one * SkillTree3D.TextScale; c.rectTransform.anchoredPosition = new Vector2(CaptionX, CaptionTop - i * CaptionPitch);
                c.supportRichText = true; c.resizeTextForBestFit = true; c.resizeTextMaxSize = CaptionPx; c.resizeTextMinSize = CaptionMinPx;
                c.horizontalOverflow = HorizontalWrapMode.Wrap; c.verticalOverflow = VerticalWrapMode.Truncate;
                _cap[i] = c;
            }
            _capLv = UiKit.Label((RectTransform)transform, "", Vector2.zero, new Vector2(60f / SkillTree3D.TextScale, CaptionPitch / SkillTree3D.TextScale), CaptionLvPx, TextAnchor.MiddleCenter, new Color(0.7f, 0.74f, 0.82f));
            _capLv.rectTransform.localScale = Vector3.one * SkillTree3D.TextScale; _capLv.rectTransform.anchoredPosition = new Vector2(CaptionLvX, CaptionTop);
            _capLv.horizontalOverflow = HorizontalWrapMode.Overflow; _capLv.verticalOverflow = VerticalWrapMode.Overflow;
            _dCaster.enabled = false; _dPoring.enabled = false;
        }

        /// <summary>Client wave 26 (w26skill): the caption for the gain of the skill shown (Game/SkillGains.cs), at its numbers now.</summary>
        private void Caption()
        {
            var g = GainShown = GainOf != null && Skill != 0 ? GainOf(Skill) : null;
            _rows.Clear();
            if (g != null)
                foreach (var l in g.Lines)
                {
                    if (_rows.Count >= CaptionLines) break;
                    // a long line's condition on its own line under it (a smaller text would be too small to read)
                    if (l.Cond != null && SkillGains.Plain(l.Text).Length > CaptionSplitChars && _rows.Count + 2 <= CaptionLines) { _rows.Add(l.Main); _rows.Add("<color=#B8C2D6>" + l.CondText + "</color>"); }
                    else _rows.Add(l.Text);
                }
            string lv = g != null && !g.Learned ? Lang.F("Lv {0}", g.Level) : "";
            if (_capLv.text != lv) _capLv.text = lv;
            for (int i = 0; i < _cap.Length; i++)
            {
                string s = i < _rows.Count ? _rows[i] : ""; if (_cap[i].text != s) _cap[i].text = s;
                // the first line leaves room for the level tag at its left; the others take the scene's whole width
                bool narrow = i == 0 && lv.Length > 0; var rt = _cap[i].rectTransform;
                rt.anchoredPosition = new Vector2(narrow ? CaptionX : 0f, CaptionTop - i * CaptionPitch);
                rt.sizeDelta = new Vector2((narrow ? CaptionW : CaptionFullW) / SkillTree3D.TextScale, CaptionPitch / SkillTree3D.TextScale);
            }
        }

        private Transform Holder(string name, float x)
        {
            var go = new GameObject(name); go.layer = UiKit.PanelLayer; go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(x, 0f, 0f); go.transform.localScale = Vector3.one * CellMm; // a cell of the game = CellMm of the tree
            return go.transform;
        }
        private static ActorSprite MakeSprite(Transform holder, bool player)
        {
            var go = new GameObject("sprite"); go.layer = UiKit.PanelLayer; go.transform.SetParent(holder, false);
            var s = go.AddComponent<ActorSprite>(); s.IsPlayerType = player;
            return s;
        }

        // ------------------------------------------------------------------ which scene
        /// <summary>The scene of a skill: its own (the best-known ones), else its kind's.</summary>
        public Spec SpecOf(int skill, out string source)
        {
            var info = SkillDb.Get(skill);
            if (!string.IsNullOrEmpty(info.Aegis) && Tuned.TryGetValue(info.Aegis, out var tuned)) { source = "tuned"; if (tuned.Hits == 0 && tuned.Kind != Kind.Trap && tuned.Kind != Kind.Wall && tuned.Kind != Kind.Area) tuned.Hits = 1; return tuned; }
            var col = ColourOf(info); int range = 1; var ex = SkillTreeDb.ExtraOf(skill); foreach (int r in ex.Range) if (r > range) range = r;
            // the kind Game/SkillTags.cs read from the server's own skill table (every skill of the job trees)
            if (SkillTags.Has(skill))
            {
                source = "skill data";
                switch (SkillTags.KindOf(skill))
                {
                    case SkillKind.MeleeHit: return S(Kind.Melee, col);
                    case SkillKind.Bolt: return S(Kind.Bolt, col, 3);
                    case SkillKind.Projectile: return S(Kind.Shot, col);
                    case SkillKind.GroundArea: return S(Kind.Area, col, 2);
                    case SkillKind.SelfArea: return S(Kind.Area, col, 1, how: "self");
                    case SkillKind.Buff: return S(Kind.Buff, col);
                    case SkillKind.Debuff: return S(Kind.Curse, col);
                    case SkillKind.Heal: return S(Kind.Heal, Green);
                    case SkillKind.Trap: return S(Kind.Trap, col, 1, walkIn: true);
                    case SkillKind.Passive: return S(Kind.Passive, col);
                    case SkillKind.Movement: return S(Kind.Jump, col);
                    case SkillKind.Summon: return S(Kind.Utility, col);
                }
            }
            var k = Entry != null ? Entry(skill) : null;
            if (k != null)
            {
                if (k.Range > range) range = k.Range;
                source = "server";
                if (k.Passive) return S(Kind.Passive, col);
                if (k.Ground) return S(Kind.Area, col, 2);
                if (k.SelfCast) return S(HealLike(skill, info) ? Kind.Heal : Kind.Buff, HealLike(skill, info) ? Green : col);
                if (k.Support) return S(HealLike(skill, info) ? Kind.Heal : Kind.Buff, HealLike(skill, info) ? Green : col);
                if (k.AttackSkill) return range > 3 ? S(MagicLike(info) ? Kind.Bolt : Kind.Shot, col, MagicLike(info) ? 3 : 1) : S(Kind.Melee, col);
                return S(Kind.Utility, col);
            }
            var own = Own != null ? Own(skill) : null;
            if (own.HasValue)
            {
                source = "own table"; var (kind, target, recipe) = own.Value;
                switch (recipe)
                {
                    case "strike": return S(Kind.Melee, col); case "shot": return S(Kind.Shot, col); case "bolt": return S(Kind.Bolt, col, 3);
                    case "nova": return S(Kind.Area, col, 1, how: "self"); case "field": case "zone": return S(Kind.Area, col, 2);
                    case "heal": return S(Kind.Heal, Green); case "curse": case "mark": return S(Kind.Curse, col);
                    case "aura": case "song": case "bless": case "pulse": return S(Kind.Buff, col);
                }
                if (kind == "heal") return S(Kind.Heal, Green);
                if (kind == "buff") return S(Kind.Buff, col);
                if (kind == "attack") return target == "Ground" ? S(Kind.Area, col, 2) : range > 3 ? S(Kind.Shot, col) : S(Kind.Melee, col);
            }
            // the skill's tags, then its description and range
            var tags = new HashSet<string>(); foreach (var id in SkillTags.For(skill)) tags.Add(id.ToLowerInvariant());
            bool Has(params string[] words) { foreach (var t in tags) foreach (var w in words) if (t.Contains(w)) return true; return false; }
            string desc = (info.DescriptionEn ?? "").ToLowerInvariant();
            if (tags.Count > 0)
            {
                source = "tags";
                if (Has("heal")) return S(Kind.Heal, Green);
                if (Has("area") && Has("damage", "dot")) return S(Kind.Area, col, 2);
                if (Has("cc", "debuff", "control")) return S(Kind.Curse, col);
                if (Has("mobility", "evasion") && !Has("damage", "burst")) return S(Kind.Jump, col);
                if (Has("shield", "surviv", "resist", "buff", "support")) return S(desc.Contains("passive") ? Kind.Passive : Kind.Buff, col);
                if (Has("burst", "consistent", "damage", "dot")) return desc.Contains("passive") ? S(Kind.Passive, col) : range > 3 ? S(MagicLike(info) ? Kind.Bolt : Kind.Shot, col, MagicLike(info) ? 3 : 1) : S(Kind.Melee, col);
            }
            source = "description";
            if (desc.Contains("passive")) return S(Kind.Passive, col);
            if (HealLike(skill, info)) return S(Kind.Heal, Green);
            if (desc.Contains("trap")) return S(Kind.Trap, col, 1, walkIn: true);
            if (desc.Contains("ground") || desc.Contains("area")) return S(Kind.Area, col, 2);
            if (desc.Contains("supportive") || desc.Contains("self")) return S(Kind.Buff, col);
            if (desc.Contains("offensive") || desc.Contains("damage")) return range > 3 ? S(MagicLike(info) ? Kind.Bolt : Kind.Shot, col, MagicLike(info) ? 3 : 1) : S(Kind.Melee, col);
            return S(Kind.Utility, col);
        }
        private static bool HealLike(int skill, SkillDb.Skill info) { string a = info.Aegis ?? ""; return a.Contains("HEAL") || a.Contains("CURE") || a.Contains("RECOVERY") || a.Contains("SANCTUARY"); }
        private static bool MagicLike(SkillDb.Skill info) { string a = info.Aegis ?? ""; return a.StartsWith("MG_") || a.StartsWith("WZ_") || a.StartsWith("HW_") || a.StartsWith("SA_") || a.StartsWith("PF_") || a.StartsWith("WL_") || a.StartsWith("SO_") || a.StartsWith("AL_") || a.StartsWith("PR_") || a.StartsWith("AB_") || a.StartsWith("NJ_") || a.StartsWith("SL_") || a.StartsWith("AG_") || a.StartsWith("EM_") || a.StartsWith("CD_"); }
        /// <summary>A skill's colour from the words of its name and description (its element), else plain.</summary>
        private static Color ColourOf(SkillDb.Skill info)
        {
            string s = ((info.Aegis ?? "") + " " + (info.NameEn ?? "")).ToLowerInvariant();
            if (s.Contains("fire") || s.Contains("flame") || s.Contains("magma") || s.Contains("burn") || s.Contains("meteor")) return Fire;
            if (s.Contains("cold") || s.Contains("ice") || s.Contains("frost") || s.Contains("water") || s.Contains("frozen") || s.Contains("storm gust")) return Ice;
            if (s.Contains("lightning") || s.Contains("thunder") || s.Contains("wind") || s.Contains("jupitel") || s.Contains("vermilion")) return Wind;
            if (s.Contains("earth") || s.Contains("stone") || s.Contains("quake") || s.Contains("ground")) return Earth;
            if (s.Contains("holy") || s.Contains("heal") || s.Contains("bless") || s.Contains("sanct") || s.Contains("light")) return Holy;
            if (s.Contains("poison") || s.Contains("venom") || s.Contains("acid")) return Poison;
            if (s.Contains("dark") || s.Contains("shadow") || s.Contains("soul") || s.Contains("curse") || s.Contains("hell")) return Dark;
            return Plain;
        }

        // ------------------------------------------------------------------ show / hide
        /// <summary>Shows the preview of a skill at a place of <paramref name="space"/> (the tree's content: it moves with the tree).</summary>
        public void Show(int skill, Transform space, Vector3 localAt)
        {
            StopSound?.Invoke();
            if (transform.parent != space) transform.SetParent(space, false);
            transform.localPosition = localAt; transform.localRotation = Quaternion.identity; transform.localScale = Vector3.one;
            Skill = skill; Playing = SpecOf(skill, out string src); Source = src;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            Shows++; Loops = 0; Plays = 1; Resting = false; _t = 0f; _hitsDone = 0; _castDone = false;
            _snd = SoundsOf != null ? SoundsOf(skill) : null; SoundsStarted = 0; LastSound = "";
            // client wave 26 (w26skill): what the skill gains the player: the caption, and the demo its row asks for
            Caption();
            if (GainShown != null && GainShown.Demo != SkillGains.Demo.None) { var sp = Playing; sp.Demo = GainShown.Demo; sp.DemoBuff = GainShown.Buff; Playing = sp; }
            SetLook(); Reset();
            Debug.Log($"[Tree] preview: {skill} {SkillDb.Get(skill).Aegis}: {Playing.Kind}{(Playing.How.Length > 0 ? " (" + Playing.How + ")" : "")}, {Playing.Hits} hit(s), by {Source}{(GainShown != null ? $"; its gain at level {GainShown.Level}{(GainShown.IconOn ? " (its icon is on: + only)" : "")}: {CaptionText}{(Playing.Demo != SkillGains.Demo.None ? ", demo " + Playing.Demo : "")} (rAthena {GainShown.Source})" : "")}");
        }
        public void Hide() { if (gameObject.activeSelf || Skill != 0) StopSound?.Invoke(); if (gameObject.activeSelf) gameObject.SetActive(false); Skill = 0; }
        /// <summary>Client wave 23 (w23tree): the scene shown is put at another place (a panel came to stand behind it): it plays on
        /// as it was, nothing starts again.</summary>
        public void MoveTo(Transform space, Vector3 localAt)
        {
            if (transform.parent != space) transform.SetParent(space, false);
            transform.localPosition = localAt; transform.localRotation = Quaternion.identity; transform.localScale = Vector3.one;
        }

        /// <summary>The two sprites from the player's own game data: the player's own look, a Poring (class 1002).</summary>
        private void SetLook()
        {
            var assets = Assets != null ? Assets() : null; if (assets == null) return;
            var look = Look != null ? Look() : null;
            try
            {
                if (look != null && (_lookShown == null || !_lookShown.SameAs(look)))
                {
                    _lookShown = look.Clone(); PlayerAppearance.Apply(_caster, look, assets);
                    _attackGroup = AppearanceDb.AttackActionGroup(look.Job, look.Sex, AppearanceDb.AttackWeaponType(look));
                }
                if (!_lookSet && assets.Sprite(RoNames.BodyPath(1002, 0), out var spr, out var act)) { _poring.SetBody(spr, act); _lookSet = true; }
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>A loop begins: both stand as at its start.</summary>
        private void Reset()
        {
            var sp = Playing;
            _caster.Release(); _poring.Release(); // (client wave 23: both were held on their frame through the rest)
            _sndDone = 0;
            _casterAt.localPosition = new Vector3(CasterX, 0f, 0f);
            _poringAt.localPosition = new Vector3(sp.WalkIn ? PoringX + 34f : PoringX, 0f, 0f);
            _caster.Play(ActorSprite.PlayerAction.Standby, true); _poring.Play(sp.WalkIn ? ActorSprite.MonsterAction.Walk : ActorSprite.MonsterAction.Idle, true);
            _mCaster.SetColor("_Tint", new Color(0.82f, 0.94f, 1f, 0.86f)); _mPoring.SetColor("_Tint", new Color(1f, 0.95f, 1f, 0.9f));
            _hitsDone = 0; _castDone = false; if (_number != null) _number.text = "";
            _demoDone = 0; _standAt = -1f; _missAt = -9f; DemoMisses = 0; DemoCrits = 0; DemoSwings = 0; DemoHits = 0;
            if (Plays > 1) Caption(); // (client wave 26: each play shows the numbers as they are now: a level learned while it plays)
        }

        // ------------------------------------------------------------------ client wave 26 (w26skill): the demos
        /// <summary>The demos' moments (s into the play): a buff's cast (the dodge's and the crit's: the kinds' cast moment; the speed's and
        /// the accuracy's after their "before" swings), the Poring's two lunges (dodge), the two critical strikes, the slow and the quick
        /// swings (speed), the missed swing and the two hits (accuracy). A passive (always on) has no cast and starts earlier.</summary>
        public static float CastAt(Spec sp) => sp.Demo == SkillGains.Demo.Speed ? 1.12f : sp.Demo == SkillGains.Demo.Accuracy ? 0.95f : TCast;
        public static float[] DemoTimes(Spec sp)
        {
            float first = sp.DemoBuff ? 1.15f : 0.8f;
            switch (sp.Demo)
            {
                case SkillGains.Demo.Dodge: case SkillGains.Demo.Crit: return new[] { first, first + 0.85f };
                case SkillGains.Demo.Speed: return new[] { 0.05f, 0.65f, 1.55f, 1.8f, 2.05f, 2.3f }; // (slow 0.6 s apart, quick 0.25 s)
                case SkillGains.Demo.Accuracy: return new[] { 0.3f, 1.45f, 2.1f };
                default: return new float[0];
            }
        }
        /// <summary>The quick swings' speed against the slow ones' (the speed demo).</summary>
        public const float QuickSwing = 2.2f;
        /// <summary>Played now: the Poring's lunges dodged ("Miss" over the character), critical strikes, swings, hits landed (smoke, log).</summary>
        public int DemoMisses { get; private set; }
        public int DemoCrits { get; private set; }
        public int DemoSwings { get; private set; }
        public int DemoHits { get; private set; }
        private int _demoDone; private float _standAt = -1f, _missAt = -9f; private bool _missOnCaster;
        private static float Bump(float a, float up, float down) => a < 0f ? 0f : a < up ? a / up : a < up + down ? 1f - (a - up) / down : 0f;

        private void DemoAct(Spec sp, float t)
        {
            var times = DemoTimes(sp);
            // a buff is cast at its moment (a passive is always on: no cast)
            if (sp.DemoBuff && !_castDone && t >= CastAt(sp)) { _castDone = true; _standAt = -1f; _caster.PlayThen((int)ActorSprite.PlayerAction.Skill, 600f, (int)ActorSprite.PlayerAction.Standby); }
            float cx = CasterX, px = PoringX;
            for (int i = 0; i < times.Length; i++)
            {
                float at = times[i]; bool fire = t >= at && (_demoDone & (1 << i)) == 0;
                if (fire) _demoDone |= 1 << i;
                switch (sp.Demo)
                {
                    case SkillGains.Demo.Dodge:
                        // the Poring lunges at the character, who side-steps it: "Miss" over the character
                        if (fire) { _poring.PlayThen((int)ActorSprite.MonsterAction.Attack, 0f, (int)ActorSprite.MonsterAction.Idle); DemoMisses++; Miss(true, at + 0.1f); }
                        px -= 34f * Bump(t - at, 0.14f, 0.26f); cx -= 13f * Bump(t - at - 0.06f, 0.08f, 0.22f);
                        break;
                    case SkillGains.Demo.Crit:
                        // the character strikes: each a critical (a star burst, Effects), the Poring hurt and pushed a little
                        if (t >= at - 0.15f && (_demoDone & (1 << (8 + i))) == 0) { _demoDone |= 1 << (8 + i); _caster.PlayThen(_attackGroup, 0f, (int)ActorSprite.PlayerAction.Standby); DemoSwings++; }
                        if (fire) { _poring.PlayThen((int)ActorSprite.MonsterAction.Hurt, 0f, (int)ActorSprite.MonsterAction.Idle); DemoCrits++; DemoHits++; }
                        px += 7f * Bump(t - at, 0.08f, 0.4f);
                        break;
                    case SkillGains.Demo.Speed:
                        // two slow swings, the skill, four quick ones: each hits
                        if (fire)
                        {
                            bool quick = i >= 2; _caster.Play(_attackGroup, false, quick ? QuickSwing : 1f); _standAt = at + (quick ? 0.27f : 0.45f); DemoSwings++;
                            _poring.PlayThen((int)ActorSprite.MonsterAction.Hurt, 0f, (int)ActorSprite.MonsterAction.Idle); DemoHits++;
                        }
                        break;
                    case SkillGains.Demo.Accuracy:
                        // a swing that misses ("Miss" over the Poring), the skill, two that hit
                        if (fire)
                        {
                            _caster.PlayThen(_attackGroup, 0f, (int)ActorSprite.PlayerAction.Standby); DemoSwings++;
                            if (i == 0) { DemoMisses++; Miss(false, at + 0.15f); }
                            else { _poring.PlayThen((int)ActorSprite.MonsterAction.Hurt, 0f, (int)ActorSprite.MonsterAction.Idle); DemoHits++; }
                        }
                        break;
                }
            }
            if (_standAt > 0f && t >= _standAt) { _standAt = -1f; _caster.Play(ActorSprite.PlayerAction.Standby, true); }
            _casterAt.localPosition = new Vector3(cx, 0f, 0f); _poringAt.localPosition = new Vector3(px, 0f, 0f);
            // "Miss" (the game's own word) rises and fades over the one it missed
            if (_number != null)
            {
                float age = t - _missAt;
                if (age >= 0f && age < 0.75f)
                {
                    if (_number.text != _missWord) { _number.text = _missWord; _number.color = new Color(1f, 0.93f, 0.55f); }
                    var c = _number.color; c.a = Mathf.Clamp01(1.5f - age * 2f); _number.color = c;
                    _number.rectTransform.anchoredPosition3D = new Vector3(_missOnCaster ? CasterX - 6f : PoringX, 76f + 12f * Mathf.Clamp01(age / 0.6f), -6f);
                }
                else if (_number.text.Length > 0 && age >= 0.75f) _number.text = "";
            }
        }
        private string _missWord = "Miss";
        private void Miss(bool onCaster, float at) { _missOnCaster = onCaster; _missAt = at; _missWord = Lang.T("Miss"); }
        /// <summary>The word drawn now over the scene (smoke).</summary>
        public string DemoWord => _number != null && _number.text.Length > 0 && _number.text == _missWord ? _missWord : "";

        // ------------------------------------------------------------------ every frame
        /// <summary>The texts' canvas over the scene's meshes: Unity gives a nested canvas's own order up when it is moved under another
        /// parent (each Show / MoveTo moves the scene to its tree), so it is set again (as UI/SkillTree3D.Panels.cs does for its panels).</summary>
        private void KeepTextOrder() { if (_canvas != null && (!_canvas.overrideSorting || _canvas.sortingOrder != TextOrder)) { _canvas.overrideSorting = true; _canvas.sortingOrder = TextOrder; } }
        private Canvas _canvas;

        private void LateUpdate()
        {
            KeepTextOrder(); // client wave 26 (w26skill)
            float dt = Time.unscaledDeltaTime; _t += dt;
            var sp = Playing;
            // the two face each other across the ground whatever the map's view is (as UI/CharPreview.cs)
            _caster.Direction = (7 - ActorSprite.CameraDirection) & 7; _poring.Direction = (1 - ActorSprite.CameraDirection) & 7;
            if (Resting)
            {
                // THE REST (client wave 23): the pose the play ended in is held: nothing is moved and nothing is drawn anew
                if (_t < Loop + Rest) { Draw(_caster, _dCaster, _fCaster); Draw(_poring, _dPoring, _fPoring); return; }
                _t = 0f; Resting = false; Plays++; Reset(); // the next play
            }
            else if (_t >= Loop)
            {
                // the play is over: the scene as its last moment leaves it, both sprites held on the frame they show
                _t = Loop; Loops++; Resting = true;
                Act(sp, Loop);
                _caster.Hold(); _poring.Hold();
                Draw(_caster, _dCaster, _fCaster); Draw(_poring, _dPoring, _fPoring);
                Paint(sp, Loop);
                return;
            }
            float t = _t;
            // (item 24) the skill's own sounds at their moments, on every play (PreviewSoundEveryPlay; false: the first play only)
            if (_snd != null && (Plays == 1 || PreviewSoundEveryPlay))
                while (_sndDone < _snd.Count && t >= _snd[_sndDone].at) { PlaySound?.Invoke(_snd[_sndDone].wav); SoundsStarted++; LastSound = _snd[_sndDone].wav; _sndDone++; }
            Act(sp, t);
            Draw(_caster, _dCaster, _fCaster); Draw(_poring, _dPoring, _fPoring);
            Paint(sp, t);
        }
        /// <summary>The ground's and the effects' meshes for a moment of the play.</summary>
        private void Paint(Spec sp, float t)
        {
            _bBack.Clear(); _bFront.Clear();
            Ground(sp, t); Effects(sp, t);
            _bBack.Fill(_meshBack); _bFront.Fill(_meshFront);
        }

        /// <summary>Draws a hidden sprite's current frame with the preview's own material.</summary>
        private static void Draw(ActorSprite s, MeshRenderer d, MeshFilter f)
        {
            var q = s.CurrentQuad; var mesh = s.CurrentMesh; var tex = s.CurrentTexture;
            if (q == null || mesh == null || tex == null) { d.enabled = false; return; }
            var own = q.GetComponent<MeshRenderer>(); if (own != null && !own.forceRenderingOff) own.forceRenderingOff = true; // the game's own material is not drawn
            if (d.transform.parent != q.parent) d.transform.SetParent(q.parent, false);
            d.transform.localPosition = q.localPosition; d.transform.localRotation = q.localRotation; d.transform.localScale = q.localScale;
            f.sharedMesh = mesh; d.sharedMaterial.mainTexture = tex; d.enabled = true;
        }

        /// <summary>The hit times of the loop: the first at <see cref="THit"/>, the others a little apart.</summary>
        private float HitAt(Spec sp, int i) => (sp.WalkIn ? THit + 0.25f : THit) + i * (sp.Hits > 4 ? 0.09f : sp.Hits > 2 ? 0.16f : 0.26f);

        private void Act(Spec sp, float t)
        {
            if (sp.Demo != SkillGains.Demo.None) { DemoAct(sp, t); return; } // client wave 26 (w26skill): the gain's demo
            // the caster: its attack or its cast once, at the cast's moment
            if (!_castDone && t >= TCast && sp.Kind != Kind.Passive)
            {
                _castDone = true;
                if (sp.How == "dead") _caster.Play(ActorSprite.PlayerAction.Dead, false);
                else if (sp.Kind == Kind.Melee || (sp.Kind == Kind.Shot && !MagicLike(SkillDb.Get(Skill)))) _caster.PlayThen(_attackGroup, 0f, (int)ActorSprite.PlayerAction.Standby);
                else _caster.PlayThen((int)ActorSprite.PlayerAction.Skill, 600f, (int)ActorSprite.PlayerAction.Standby);
            }
            if (sp.Kind == Kind.Hide && sp.How != "dead") { float a = t < TCast ? 0.86f : t < Loop - 0.5f ? Mathf.Lerp(0.86f, 0.12f, (t - TCast) / 0.35f) : Mathf.Lerp(0.12f, 0.86f, (t - (Loop - 0.5f)) / 0.5f); _mCaster.SetColor("_Tint", new Color(0.82f, 0.94f, 1f, Mathf.Clamp(a, 0.12f, 0.86f))); }
            if (sp.Kind == Kind.Jump)
            {
                float to = sp.How == "back" ? CasterX - 22f : PoringX + 26f, tj = TCast + 0.25f;
                _casterAt.localPosition = new Vector3(t < tj ? CasterX : t < Loop - 0.4f ? to : CasterX, 0f, 0f);
                float flash = Mathf.Min(Mathf.Abs(t - tj), Mathf.Abs(t - (Loop - 0.4f))); _mCaster.SetColor("_Tint", new Color(0.82f, 0.94f, 1f, flash < 0.12f ? 0.2f : 0.86f));
            }
            // the Poring: it walks in (a wall, a trap), flinches at each hit and is pushed, may freeze
            float px = sp.WalkIn ? Mathf.Lerp(PoringX + 34f, PoringX - 6f, Mathf.Clamp01(t / (THit + 0.25f))) : PoringX;
            int hits = 0; for (int i = 0; i < Mathf.Max(sp.Hits, sp.WalkIn || sp.Freeze || sp.Kind == Kind.Curse ? 1 : 0); i++) if (t >= HitAt(sp, i)) hits++;
            bool offensive = sp.Kind == Kind.Melee || sp.Kind == Kind.Shot || sp.Kind == Kind.Bolt || sp.Kind == Kind.Area || sp.Kind == Kind.Wall || sp.Kind == Kind.Trap || sp.Kind == Kind.Curse;
            if (offensive && hits > _hitsDone)
            {
                _hitsDone = hits;
                if (sp.Hits > 0 || sp.Kind == Kind.Curse) _poring.PlayThen((int)ActorSprite.MonsterAction.Hurt, 0f, (int)ActorSprite.MonsterAction.Idle);
                else _poring.Play(ActorSprite.MonsterAction.Idle, true);
                if (sp.Hits > 0 && sp.Kind != Kind.Curse) PopNumber(sp, hits);
            }
            if (offensive && _hitsDone > 0)
            {
                float since = t - HitAt(sp, 0);
                if (sp.Knock > 0f) px += sp.Knock * Mathf.Clamp01(since / 0.18f) * (1f - Mathf.Clamp01((since - 0.9f) / 0.6f));
                if (sp.Freeze) _mPoring.SetColor("_Tint", Color.Lerp(new Color(1f, 0.95f, 1f, 0.9f), new Color(0.45f, 0.75f, 1f, 0.95f), Mathf.Clamp01(since / 0.2f)));
                else if (sp.Col == Poison) _mPoring.SetColor("_Tint", Color.Lerp(new Color(1f, 0.95f, 1f, 0.9f), new Color(0.75f, 0.38f, 0.95f, 0.95f), Mathf.Clamp01(since / 0.2f)));
            }
            _poringAt.localPosition = new Vector3(px, 0f, 0f);
            if ((sp.Kind == Kind.Heal || sp.How == "take") && _hitsDone == 0 && t >= THit) { _hitsDone = 1; if (sp.Kind == Kind.Heal) PopNumber(sp, 1); }
            // the number rises and fades
            if (_number != null && _number.text.Length > 0)
            {
                float age = t - _numberAt; var c = _number.color; c.a = Mathf.Clamp01(1.4f - age * 1.6f); _number.color = c;
                bool heal = sp.Kind == Kind.Heal;
                _number.rectTransform.anchoredPosition3D = new Vector3(heal ? CasterX : _poringAt.localPosition.x, (heal ? 82f : 50f) + 22f * Mathf.Clamp01(age / 0.7f), -6f);
                if (c.a <= 0.01f) _number.text = "";
            }
        }
        private float _numberAt;
        private void PopNumber(Spec sp, int hit)
        {
            if (_number == null) return;
            int v = 60 + (Skill * 37 + hit * 53) % 340;
            bool heal = sp.Kind == Kind.Heal;
            _number.text = heal ? "+" + v : v.ToString(); _number.color = heal ? new Color(0.5f, 1f, 0.55f) : sp.Hits > 1 ? new Color(1f, 0.95f, 0.5f) : Color.white; _numberAt = _t;
        }

        // ------------------------------------------------------------------ the scene's shapes
        private static Color32 C(Color c, float a) => new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
        /// <summary>How flat the ground's ovals are drawn: the ground is a picture facing the player, as the game's own tilted view shows
        /// a circle on the ground (a level disc beside a medallion at the eyes' height would be a line).</summary>
        public const float GroundFlat = 0.36f;
        /// <summary>An oval of the ground: its middle's and its rim's alpha (rz: its half depth, drawn <see cref="GroundFlat"/> of it tall).</summary>
        private static void Oval(TreeMeshBuilder b, Vector3 mid, float rx, float rz, Color col, float aMid, float aRim, int n = 18)
        {
            float ry = rz * 2f * GroundFlat;
            b.Pen(C(col, aMid), 0f, 1f); int c = b.Vert(mid, Vector3.back, mid, 0f);
            b.Pen(C(col, aRim), 0f, 1f); int first = b.V.Count;
            for (int i = 0; i < n; i++) { float a = i * Mathf.PI * 2f / n; b.Vert(mid + new Vector3(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry, 0f), Vector3.back, mid, 0f); }
            for (int i = 0; i < n; i++) b.Tri(c, first + i, first + (i + 1) % n);
        }
        /// <summary>A soft round patch facing the player (the scene's backdrop).</summary>
        private static void Blob(TreeMeshBuilder b, Vector3 mid, float rx, float ry, Color col, float aMid, float aRim, int n = 20)
        {
            b.Pen(C(col, aMid), 0f, 1f); int c = b.Vert(mid, Vector3.back, mid, 0f), first = b.V.Count;
            for (int i = 0; i < n; i++) { float a = i * Mathf.PI * 2f / n; b.Vert(mid + new Vector3(Mathf.Cos(a) * rx * 0.72f, Mathf.Sin(a) * ry * 0.72f, 0f), Vector3.back, mid, 0f); }
            b.Pen(C(col, aRim), 0f, 1f); int outer = b.V.Count;
            for (int i = 0; i < n; i++) { float a = i * Mathf.PI * 2f / n; b.Vert(mid + new Vector3(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry, 0f), Vector3.back, mid, 0f); }
            for (int i = 0; i < n; i++) { int j = (i + 1) % n; b.Tri(c, first + i, first + j); b.Quad(first + i, outer + i, outer + j, first + j); }
        }
        /// <summary>Client wave 26 (w26skill): a flat dark plate facing the player (behind the caption): solid inside, its edges fading
        /// out over <paramref name="soft"/> mm (the backdrop's oval left the caption's line ends over the medallions behind it).</summary>
        private static void Plate(TreeMeshBuilder b, float x0, float y0, float x1, float y1, float soft, Color col, float a)
        {
            var n = Vector3.back; float z = 3f; var mid = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, z);
            int V(float x, float y, float al) { b.Pen(C(col, al), 0f, 1f); return b.Vert(new Vector3(x, y, z), n, mid, 0f); }
            float ix0 = x0 + soft, iy0 = y0 + soft, ix1 = x1 - soft, iy1 = y1 - soft;
            int i00 = V(ix0, iy0, a), i10 = V(ix1, iy0, a), i11 = V(ix1, iy1, a), i01 = V(ix0, iy1, a);
            int o00 = V(x0, y0, 0f), o10 = V(x1, y0, 0f), o11 = V(x1, y1, 0f), o01 = V(x0, y1, 0f);
            b.Quad(i00, i10, i11, i01);
            b.Quad(o00, o10, i10, i00); b.Quad(i10, o10, o11, i11); b.Quad(i01, i11, o11, o01); b.Quad(o00, i00, i01, o01);
        }
        /// <summary>A ring on the ground.</summary>
        private static void GroundRing(TreeMeshBuilder b, Vector3 mid, float r, float w, Color col, float a, int n = 20)
        {
            b.Pen(C(col, a), 0f, 1f); int first = b.V.Count;
            for (int i = 0; i < n; i++) { float an = i * Mathf.PI * 2f / n; var d = new Vector3(Mathf.Cos(an), Mathf.Sin(an) * GroundFlat, 0f); b.Vert(mid + d * r, Vector3.back, mid, 0f); b.Vert(mid + d * (r + w), Vector3.back, mid, 0f); }
            for (int i = 0; i < n; i++) { int p = first + i * 2, q = first + ((i + 1) % n) * 2; b.Quad(p, q, q + 1, p + 1); }
        }
        /// <summary>An upright streak facing the player (a flame, a bolt, a beam): from its foot up, wide at the foot.</summary>
        private static void Streak(TreeMeshBuilder b, Vector3 foot, float h, float w, Color col, float aFoot, float aTop)
        {
            var n = Vector3.back;
            b.Pen(C(col, aFoot), 0f, 1f); int v0 = b.Vert(foot + new Vector3(-w * 0.5f, 0, 0), n, foot, 0f), v1 = b.Vert(foot + new Vector3(w * 0.5f, 0, 0), n, foot, 0f);
            b.Pen(C(col, aTop), 0f, 1f); int v2 = b.Vert(foot + new Vector3(0, h, 0), n, foot, 0f);
            b.Tri(v0, v1, v2);
        }
        /// <summary>A burst of short rays round a point (a hit).</summary>
        private static void Burst(TreeMeshBuilder b, Vector3 at, float r, Color col, float a, int rays = 8)
        {
            b.Pen(C(col, a), 0f, 1f);
            for (int i = 0; i < rays; i++) { float an = i * Mathf.PI * 2f / rays + 0.3f; var d = new Vector3(Mathf.Cos(an), Mathf.Sin(an), 0f); b.Line(at + d * (r * 0.35f), at + d * r, 2.2f, 0f, 0f); }
            b.Arc(at, r * 0.12f, r * 0.3f, 0f, 360f, 10, 0f, at);
        }

        private void Ground(Spec sp, float t)
        {
            // a dark see-through backdrop (the scene stands in front of other medallions), then the piece of ground: a pale oval with a brighter rim
            var mid = new Vector3(0f, -1f, 2f);
            Blob(_bBack, new Vector3(0f, 26f, 3f), GroundR + 8f, 62f, new Color(0.03f, 0.05f, 0.09f), 0.78f, 0f);
            Oval(_bBack, mid, GroundR, GroundR * 0.5f, new Color(0.45f, 0.72f, 0.9f), 0.42f, 0.22f);
            GroundRing(_bBack, mid, GroundR - 2.5f, 2.5f, new Color(0.65f, 0.92f, 1f), 0.85f);
            // client wave 26 (w26skill): a dark plate behind the caption (what the skill gains), within the scene's box
            if (GainShown != null && GainShown.Lines.Count > 0) Plate(_bBack, -BoxHalfW + 4f, CaptionTop - (CaptionLines - 0.5f) * CaptionPitch - 2f, BoxHalfW - 4f, BoxTop - 1f, 5f, new Color(0.03f, 0.05f, 0.09f), 0.86f);
            var col = sp.Col; float since = t - TCast;
            switch (sp.Kind)
            {
                case Kind.Area:
                    if (since > 0f) { var at = new Vector3(sp.How == "self" ? CasterX : PoringX, 0f, 0f); float g = Mathf.Clamp01(since / 0.35f), fade = 1f - Mathf.Clamp01((since - 1.4f) / 0.5f); Oval(_bBack, at, 30f * g, 15f * g, col, 0.35f * fade, 0.1f * fade); GroundRing(_bBack, at, 30f * g, 3f, col, 0.8f * fade); }
                    break;
                case Kind.Trap:
                    if (since > 0f) { var at = new Vector3(PoringX - 6f, 0f, 0f); float fade = 1f - Mathf.Clamp01((t - (THit + 0.9f)) / 0.4f); GroundRing(_bBack, at, 9f, 2.4f, col, 0.9f * fade); GroundRing(_bBack, at, 3f, 2f, col, 0.9f * fade); }
                    break;
                case Kind.Wall:
                    if (since > 0f) { float fade = 1f - Mathf.Clamp01((since - 1.9f) / 0.4f); Oval(_bBack, new Vector3(4f, 0f, 0f), 12f, 16f, col, 0.5f * fade, 0.15f * fade); }
                    break;
                case Kind.Heal:
                    if (sp.How == "ground" && since > 0f) { float fade = 1f - Mathf.Clamp01((since - 1.6f) / 0.5f); Oval(_bBack, new Vector3(CasterX, 0f, 0f), 26f, 13f, col, 0.4f * fade, 0.1f * fade); }
                    break;
                case Kind.Passive:
                    GroundRing(_bBack, new Vector3(CasterX, 0f, 0f), 13f + 2f * Mathf.Sin(t * 3f) * (1f - Mathf.Clamp01((t - (Loop - 0.4f)) / 0.4f)), 2f, col, 0.5f); // (client wave 23: settled at the play's end, the rest's pose)
                    break;
                case Kind.Utility:
                    if (sp.How == "portal" && since > 0.2f) { float g = Mathf.Clamp01((since - 0.2f) / 0.3f); GroundRing(_bBack, new Vector3(4f, 0f, 0f), 12f * g, 4f, col, 0.9f); Oval(_bBack, new Vector3(4f, 0f, 0f), 12f * g, 6f * g, col, 0.5f, 0.2f); }
                    break;
            }
        }

        private void Effects(Spec sp, float t)
        {
            var b = _bFront; var col = sp.Col; float since = t - (sp.Demo != SkillGains.Demo.None ? CastAt(sp) : TCast);
            Vector3 caster = new Vector3(CasterX, 30f, -4f), target = new Vector3(_poringAt.localPosition.x, 15f, -4f);
            int hits = Mathf.Max(1, sp.Hits);
            switch (sp.Kind)
            {
                case Kind.Melee:
                    for (int i = 0; i < hits; i++)
                    {
                        float a = t - HitAt(sp, i); if (a < 0f || a > 0.3f) continue; float f = 1f - a / 0.3f;
                        // a slash: a crescent across the Poring, turned a little each hit
                        b.Pen(C(col, f), 0f, 1f); float turn = -50f + (i % 3) * 40f; b.Arc(target, 14f + 10f * (1f - f), 18f + 10f * (1f - f), turn, turn + 120f, 8, 0f, target);
                        Burst(b, target, 10f + 12f * (1f - f), col, f * 0.9f, 6);
                        if (sp.How == "coins") for (int c = 0; c < 4; c++) { var p = target + new Vector3((c - 1.5f) * 9f, 10f + 34f * (1f - f) - (c % 2) * 6f, 0f); b.Pen(C(Coin, f), 0f, 1f); b.Ring(p, 0f, 3.2f, 8, 0f, p); }
                    }
                    break;
                case Kind.Shot:
                    for (int i = 0; i < hits; i++)
                    {
                        float hit = HitAt(sp, i), fly = 0.28f, a = t - (hit - fly);
                        if (a >= 0f && a < fly)
                        {
                            float f = a / fly; var src = sp.How == "ground" ? new Vector3(CasterX + 8f, 1f, -4f) : caster; var dst = sp.How == "ground" ? new Vector3(target.x, 1f, -4f) : target;
                            var p = Vector3.Lerp(src, dst, f); var tail = Vector3.Lerp(src, dst, Mathf.Max(0f, f - 0.28f));
                            b.Pen(C(col, 0.95f), 0f, 1f); b.Line(tail, p, 3f, 0f, 0f); b.Ring(p, 0f, 3.4f, 8, 0f, p);
                        }
                        float after = t - hit; if (after >= 0f && after < 0.3f) Burst(b, target, (sp.How == "burst" ? 16f : 9f) + 12f * after / 0.3f, col, 1f - after / 0.3f);
                    }
                    break;
                case Kind.Bolt:
                    for (int i = 0; i < hits; i++)
                    {
                        float hit = HitAt(sp, i), fall = 0.2f, a = t - (hit - fall); float x = target.x + (i - (hits - 1) * 0.5f) * 5f;
                        if (a >= 0f && a < fall)
                        {
                            float f = a / fall;
                            if (sp.How == "up") Streak(b, new Vector3(x, 0f, -4f), 30f * f, 9f, col, 0.95f, 0.3f); // out of the ground
                            else if (sp.How == "dive") { var p = Vector3.Lerp(caster + new Vector3(10f, 46f, 0f), target, f); b.Pen(C(col, 0.95f), 0f, 1f); b.Line(p + new Vector3(-7f, 8f, 0f), p, 3f, 0f, 0f); b.Line(p + new Vector3(-9f, 2f, 0f), p, 2f, 0f, 0f); }
                            else { float y = Mathf.Lerp(78f, 14f, f); b.Pen(C(col, 0.95f), 0f, 1f); b.Line(new Vector3(x + 3f, y + 20f, -4f), new Vector3(x, y, -4f), 3.4f, 0f, 0f); b.Ring(new Vector3(x, y, -4f), 0f, 3.6f, 8, 0f, target); }
                        }
                        float after = t - hit; if (after >= 0f && after < 0.26f) Burst(b, target, 8f + 10f * after / 0.26f, col, 1f - after / 0.26f, 6);
                    }
                    break;
                case Kind.Area:
                    if (since > 0f)
                    {
                        var at = new Vector3(sp.How == "self" ? CasterX : PoringX, 0f, -4f);
                        // pillars / falling things all over the area while it lasts
                        for (int i = 0; i < 5; i++)
                        {
                            float ph = Mathf.Repeat(since * 2.4f + i * 0.2f, 1f); if (since > 1.5f) break;
                            var foot = at + new Vector3(Mathf.Cos(i * 2.4f) * 20f, 0f, 0f);
                            if (sp.How == "rain") { float y = Mathf.Lerp(60f, 4f, ph); b.Pen(C(col, 0.9f), 0f, 1f); b.Line(foot + new Vector3(4f, y + 12f, 0f), foot + new Vector3(0f, y, 0f), 1.8f, 0f, 0f); }
                            else Streak(b, foot, 14f + 30f * Mathf.Sin(ph * Mathf.PI), 8f, col, 0.9f * Mathf.Sin(ph * Mathf.PI), 0.1f);
                        }
                        for (int i = 0; i < hits; i++) { float after = t - HitAt(sp, i); if (after >= 0f && after < 0.25f) Burst(b, target, 8f + 10f * after / 0.25f, col, 1f - after / 0.25f, 6); }
                    }
                    break;
                case Kind.Wall:
                    if (since > 0f && since < 2.3f)
                    {
                        float fade = 1f - Mathf.Clamp01((since - 1.9f) / 0.4f), up = Mathf.Clamp01(since / 0.25f);
                        for (int i = 0; i < 3; i++) Streak(b, new Vector3(4f + (i - 1) * 5f, (1 - i) * 5f, -4f), up * (30f + 6f * Mathf.Sin(t * 14f + i * 2f)), 12f, col, 0.95f * fade, 0.15f * fade); // (three flames in a row across the ground)
                        if (col == Fire) for (int i = 0; i < 3; i++) Streak(b, new Vector3(4f + (i - 1) * 3f, 0f, -5f), up * (16f + 4f * Mathf.Sin(t * 17f + i)), 6f, Wind, 0.8f * fade, 0.1f * fade); // the flames' bright hearts
                        for (int i = 0; i < hits; i++) { float after = t - HitAt(sp, i); if (after >= 0f && after < 0.25f) Burst(b, target, 8f + 10f * after / 0.25f, col, 1f - after / 0.25f, 6); }
                    }
                    break;
                case Kind.Buff:
                    if (since > 0f && since < 1.7f)
                    {
                        float fade = 1f - Mathf.Clamp01((since - 1.2f) / 0.5f);
                        if (sp.How == "pillar") { b.Pen(C(col, 0.35f * fade), 0f, 1f); var p0 = new Vector3(CasterX - 13f, 0f, -4f); b.Quad(b.Vert(p0, Vector3.back, p0, 0f), b.Vert(p0 + new Vector3(26f, 0f, 0f), Vector3.back, p0, 0f), b.Vert(p0 + new Vector3(26f, 56f, 0f), Vector3.back, p0, 0f), b.Vert(p0 + new Vector3(0f, 56f, 0f), Vector3.back, p0, 0f)); }
                        else if (sp.How == "orbit") { float an = since * 6f; var p = new Vector3(CasterX + Mathf.Cos(an) * 20f, 24f + Mathf.Sin(an * 0.5f) * 4f, -4f - Mathf.Sin(an) * 6f); b.Pen(C(col, fade), 0f, 1f); b.Ring(p, 0f, 4.5f, 10, 0f, p); b.Arc(p, 5.5f, 7.5f, 0f, 360f, 10, 0f, p); }
                        else for (int i = 0; i < 2; i++) { float ph = Mathf.Repeat(since * 1.3f + i * 0.5f, 1f); var p = new Vector3(CasterX, 4f + 46f * ph, -4f); b.Pen(C(col, fade * Mathf.Sin(ph * Mathf.PI)), 0f, 1f); b.Arc(p, 13f - 4f * ph, 15.5f - 4f * ph, 0f, 360f, 14, 0f, p); }
                        if (sp.How == "up") for (int i = 0; i < 3; i++) { float ph = Mathf.Repeat(since * 1.6f + i * 0.33f, 1f); var p = new Vector3(CasterX + (i - 1) * 9f, 8f + 44f * ph, -5f); b.Pen(C(col, fade * (1f - ph)), 0f, 1f); b.Line(p + new Vector3(-4f, -4f, 0f), p, 2f, 0f, 0f); b.Line(p + new Vector3(4f, -4f, 0f), p, 2f, 0f, 0f); }
                    }
                    break;
                case Kind.Heal:
                    if (since > 0f && since < 1.6f)
                        for (int i = 0; i < 4; i++)
                        {
                            float ph = Mathf.Repeat(since * 1.2f + i * 0.25f, 1f); var p = new Vector3(CasterX + (i - 1.5f) * 8f, 8f + 44f * ph, -5f); float a = Mathf.Sin(ph * Mathf.PI);
                            b.Pen(C(col, a), 0f, 1f); b.Line(p + new Vector3(-4f, 0f, 0f), p + new Vector3(4f, 0f, 0f), 2.4f, 0f, 0f); b.Line(p + new Vector3(0f, -4f, 0f), p + new Vector3(0f, 4f, 0f), 2.4f, 0f, 0f); // a plus
                        }
                    break;
                case Kind.Curse:
                    { float a = t - HitAt(sp, 0); if (a >= 0f && a < 1.5f) { float fade = 1f - Mathf.Clamp01((a - 1f) / 0.5f); var p = target + new Vector3(0f, 26f, 0f); b.Pen(C(col, fade), 0f, 1f); float an = a * 5f; b.Arc(p, 8f, 10.5f, an * Mathf.Rad2Deg, an * Mathf.Rad2Deg + 250f, 10, 0f, p); b.Line(p + new Vector3(0f, -3f, 0f), p + new Vector3(0f, 5f, 0f), 2.2f, 0f, 0f); b.Ring(p + new Vector3(0f, -6f, 0f), 0f, 1.4f, 6, 0f, p); } }
                    break;
                case Kind.Trap:
                    { float a = t - HitAt(sp, 0); if (a >= 0f && a < 0.4f) Burst(b, new Vector3(PoringX - 6f, 6f, -4f), 10f + 20f * a / 0.4f, col, 1f - a / 0.4f); }
                    break;
                case Kind.Jump:
                    { float tj = TCast + 0.25f, a = Mathf.Abs(t - tj); if (a < 0.2f) { Streak(b, new Vector3(CasterX, 0f, -4f), 60f, 10f, col, 1f - a / 0.2f, 0f); Streak(b, new Vector3(sp.How == "back" ? CasterX - 22f : PoringX + 26f, 0f, -4f), 60f, 10f, col, 1f - a / 0.2f, 0f); } }
                    break;
                case Kind.Passive:
                    for (int i = 0; i < 2; i++) { float ph = Mathf.Repeat(t * 0.7f + i * 0.5f, 1f); var p = new Vector3(CasterX + 16f, 14f + 30f * ph, -5f); b.Pen(C(col, 0.9f * Mathf.Sin(ph * Mathf.PI) * (1f - Mathf.Clamp01((t - (Loop - 0.4f)) / 0.4f))), 0f, 1f); b.Line(p + new Vector3(-4f, -4f, 0f), p, 2f, 0f, 0f); b.Line(p + new Vector3(4f, -4f, 0f), p, 2f, 0f, 0f); }
                    break;
                case Kind.Utility:
                    if (sp.How == "take") { float a = t - THit; if (a >= 0f && a < 0.5f) { var p = Vector3.Lerp(target, caster, a / 0.5f) + new Vector3(0f, 14f * Mathf.Sin(a / 0.5f * Mathf.PI), 0f); b.Pen(C(col, 1f), 0f, 1f); b.Ring(p, 0f, 4f, 8, 0f, p); } }
                    else if (since > 0f && since < 0.6f) Burst(b, caster + new Vector3(8f, 18f, 0f), 6f + 10f * since / 0.6f, col, 1f - since / 0.6f, 6);
                    break;
            }
            if (col == Poison && sp.Kind != Kind.Buff && sp.Kind != Kind.Passive && t >= HitAt(sp, 0))
            {
                float pa = t - HitAt(sp, 0), fade = 1f - Mathf.Clamp01((pa - 1.1f) / 0.5f);
                if (fade > 0.01f)
                    for (int i = 0; i < 4; i++)
                    {
                        float ph = Mathf.Repeat(pa * 1.4f + i * 0.25f, 1f);
                        var p = target + new Vector3((i - 1.5f) * 6f, -4f + 28f * ph, -1f);
                        b.Pen(C(Poison, fade * Mathf.Sin(ph * Mathf.PI)), 0f, 1f);
                        b.Ring(p, 0f, 2.8f - 1.2f * ph, 8, 0f, p);
                    }
            }
            if (sp.Demo != SkillGains.Demo.None) DemoEffects(sp, t, caster, target); // client wave 26 (w26skill)
        }

        /// <summary>Client wave 26 (w26skill): the demos' shapes: a critical strike's star burst (yellow rays round a red ring), a hit's
        /// burst (speed, accuracy), the light of an always-on skill at the moment a buff would be cast (speed, accuracy).</summary>
        public static readonly Color CritYellow = new Color(1f, 0.86f, 0.25f), CritRed = new Color(1f, 0.32f, 0.2f);
        private void DemoEffects(Spec sp, float t, Vector3 caster, Vector3 target)
        {
            var b = _bFront; var times = DemoTimes(sp);
            if (!sp.DemoBuff && (sp.Demo == SkillGains.Demo.Speed || sp.Demo == SkillGains.Demo.Accuracy))
            {
                float a = t - CastAt(sp); if (a >= 0f && a < 0.45f) { b.Pen(C(sp.Col, 0.9f * (1f - a / 0.45f)), 0f, 1f); b.Arc(caster, 16f + 18f * a, 19f + 18f * a, 0f, 360f, 20, 0f, caster); }
            }
            for (int i = 0; i < times.Length; i++)
            {
                float a = t - times[i];
                if (sp.Demo == SkillGains.Demo.Crit && a >= 0f && a < 0.38f)
                {
                    float f = 1f - a / 0.38f, r = 16f + 22f * a / 0.38f; var at = target + new Vector3(0f, 8f, -1f);
                    b.Pen(C(CritYellow, f), 0f, 1f);
                    for (int k = 0; k < 10; k++) { float an = k * Mathf.PI * 2f / 10f + 0.2f; var d = new Vector3(Mathf.Cos(an), Mathf.Sin(an), 0f); b.Line(at + d * (r * 0.3f), at + d * (k % 2 == 0 ? r : r * 0.6f), 3f, 0f, 0f); }
                    b.Pen(C(CritRed, f), 0f, 1f); b.Arc(at, r * 0.18f, r * 0.3f, 0f, 360f, 14, 0f, at);
                }
                bool hit = sp.Demo == SkillGains.Demo.Speed || (sp.Demo == SkillGains.Demo.Accuracy && i > 0);
                if (hit && a >= 0.05f && a < 0.27f) Burst(b, target, 8f + 8f * (a - 0.05f) / 0.22f, Plain, 1f - (a - 0.05f) / 0.22f, 6);
            }
        }

    }
}
