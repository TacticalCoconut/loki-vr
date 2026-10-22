using System.Collections.Generic;
using Loki.Formats;
using Loki.World;
using UnityEngine;

namespace Loki.Sprites
{
    /// <summary>
    /// Client wave 3 (w3status): a unit's ailments and state looks, for every unit kind (players, monsters, NPCs, companions, ourselves),
    /// from RO packets only (protocol-generic: the SpacetimeDB module sends the same packets).
    ///
    /// What the server sends (rAthena e985006, re-audit §2d):
    ///  • opt1 "body state" (e_sc_opt1, status.hpp:2971-2982): 1 Stone, 2 Freeze, 3 Stun, 4 Sleep, 6 StoneWait, 7 Burning, 8 Imprison;
    ///    opt2 "health state" bits (e_sc_opt2, status.hpp:2985-2997): Poison 0x1, Curse 0x2, Silence 0x4, Confusion 0x8, Blind 0x10,
    ///    Angelus 0x20, Bleeding 0x40, DPoison 0x80, Fear 0x100; opt3 "virtue" bits (e_sc_opt3, status.hpp:3000-3020).
    ///  • opt1 / opt2 travel in the entry packets 0x09fd / 0x09fe / 0x09ff (bodyState / healthState, clif.cpp:1111-1112) and in
    ///    ZC_STATE_CHANGE 0x0229 (clif_changeoption_target, clif.cpp:4378-4432), sent when a status with SendOption starts or ends
    ///    (status.cpp:13165-13166, 14087-14088). The ailments have no icon (status.yml Stone … Dpoison), so no EFST packet names them.
    ///  • opt3 travels only in the entry packets' virtue (clif.cpp:1083) and in ZC_NPC_SHOWEFST_UPDATE 0x028a (clif_changeoption2,
    ///    clif.cpp:4437-4461: sent instead of 0x0229 when a NonPlayer status ends, status.cpp:14085-14086). A live opt3 change of a
    ///    player shows only as the status's icon (0x0983 / 0x0196 go to the area for every iconed player status: status.cpp:16419-16423
    ///    StatusRelevantBLTypes = BL_PC, clif.cpp:6555-6561), so the icon → opt3 bit table below comes from status.yml (the statuses with
    ///    both an Icon and an Opt3), as roBrowserLegacy maps them (EntityState.js:61-89 _stateToVirtue, Entity.js:2024-2058 toggleOpt3).
    ///  • Deep Sleep sets opt1 = Sleep (status.yml:4170-4182 Opt1 Sleep) but has no SendOption flag: neither its start nor its end
    ///    sends 0x0229 (status.cpp:13165, 14087). Its icon EFST_DEEP_SLEEP is BlEffect + DisplayPc, so it reaches every client in the
    ///    area (status.cpp:16419-16420); the client mirrors the server's opt1 from it, else a unit that entered view asleep (entry
    ///    bodyState 4) would stay asleep for good.
    ///
    /// What is drawn (roBrowserLegacy Renderer/Entity/EntityState.js; a reimplementation — the official client could not be checked):
    ///  • tints (updateBodyState 193-293, updateHealthState 300-368, updateVirtue 118-185): Stone (0.1, 0.1, 0.1), StoneWait
    ///    (0.3, 0.3, 0.3), Freeze (0, 0.4, 0.8); Curse × (0.5, 0.15, 0.1), Poison × (0.9, 0.4, 0.8); the opt3 colours; composed as
    ///    factors in ActorSprite (hover × effect × body × health × virtue);
    ///  • overlays (StatusOverlay): status-stun / status-sleep at the head (loop), status-curse at the head (loop, opacity 0.5, action by
    ///    facing), the ice block 얼음땡 (action 0 frame 0 held; action 1 played once when it thaws), the "…" emotion (ET 9) for silence;
    ///  • held animation (ActorSprite.Hold / HoldPose) for Stone (the current frame) and Freeze (FREEZE2 frame 0 for players, idle for
    ///    the others: EntityAction.js — monsters have no FREEZE2, setAction(-1) falls back to IDLE); on thaw the battle stance
    ///    (READYFIGHT) for players, idle for the others; Stone resumes;
    ///  • sounds: _stun.wav, _stonecurse.wav, _curse.wav, _poison.wav, _blind.wav, _silence.wav on the way in; _frozen_explosion.wav
    ///    and _stone_explosion.wav on the way out;
    ///  • opt3 beyond the tint (UnitVirtueFx): Assumptio's halo, the after-images of the quicken / overthrust / … buffs.
    /// No reference draws Burning, Imprison, Confusion, Angelus, Bleeding, Deadly Poison or Fear: they are tracked (Body / Health) and
    /// logged, nothing is drawn for them.
    /// </summary>
    public sealed class UnitState : MonoBehaviour
    {
        public const int Opt1Stone = 1, Opt1Freeze = 2, Opt1Stun = 3, Opt1Sleep = 4, Opt1StoneWait = 6, Opt1Burning = 7, Opt1Imprison = 8;
        public const int Opt2Poison = 0x1, Opt2Curse = 0x2, Opt2Silence = 0x4, Opt2Confusion = 0x8, Opt2Blind = 0x10, Opt2Angelus = 0x20,
            Opt2Bleeding = 0x40, Opt2DPoison = 0x80, Opt2Fear = 0x100;
        public const int Opt3Quicken = 0x1, Opt3OverThrust = 0x2, Opt3EnergyCoat = 0x4, Opt3ExplosionSpirits = 0x8, Opt3SteelBody = 0x10,
            Opt3BladeStop = 0x20, Opt3AuraBlade = 0x40, Opt3Berserk = 0x80, Opt3LightBlade = 0x100, Opt3Moonlit = 0x200, Opt3Marionette = 0x400,
            Opt3Assumptio = 0x800, Opt3Warm = 0x1000, Opt3Kaite = 0x2000, Opt3Bunsin = 0x4000, Opt3SoulLink = 0x8000, Opt3Undead = 0x10000,
            Opt3Contract = 0x20000;
        public const int EfstHiding = 4, EfstAllRiding = 613, EfstDeepSleep = 435; // efst_type (status.hpp:1453…): EFST_HIDING, EFST_ALL_RIDING, EFST_DEEP_SLEEP

        /// <summary>
        /// Status icon → opt3 bit: the statuses that have both an Icon and an Opt3 in rAthena's db/re/status.yml (e985006):
        /// Twohandquicken 317, Spearquicken 971, Concentration 1216, Onehand 1652 → Quicken; Overthrust 612, Maxoverthrust 1701, Swoo 2445 →
        /// OverThrust; Energycoat 661; Explosionspirits 1027; Steelbody 1508; Bladestop 1053; Aurablade 1198; Berserk 1236;
        /// Marionette 1389 / Marionette2 1405; Assumptio 1268 (EFST_ASSUMPTIO2); Warm 2380 (EFST_SG_SUN_WARM); Kaite 2434;
        /// Bunsinjyutsu 2538; Spirit 2080 (EFST_SOULLINK); Changeundead 1421 (EFST_PROPERTYUNDEAD). Ids: efst_type in status.hpp.
        /// </summary>
        public static readonly Dictionary<int, int> EfstOpt3 = new Dictionary<int, int>
        {
            { 2, Opt3Quicken }, { 68, Opt3Quicken }, { 105, Opt3Quicken }, { 161, Opt3Quicken },   // TWOHANDQUICKEN, SPEARQUICKEN, LKCONCENTRATION, ONEHANDQUICKEN
            { 25, Opt3OverThrust }, { 188, Opt3OverThrust }, { 154, Opt3OverThrust },               // OVERTHRUST, OVERTHRUSTMAX, SWOO
            { 31, Opt3EnergyCoat }, { 86, Opt3ExplosionSpirits }, { 87, Opt3SteelBody }, { 85, Opt3BladeStop }, { 103, Opt3AuraBlade },
            { 107, Opt3Berserk }, { 121, Opt3Marionette }, { 122, Opt3Marionette }, { 473, Opt3Assumptio }, { 165, Opt3Warm },
            { 153, Opt3Kaite }, { 207, Opt3Bunsin }, { 149, Opt3SoulLink }, { 97, Opt3Undead },
        };

        private Unit _u;
        /// <summary>opt1 (body state), opt2 (health state) as the server last sent them.</summary>
        public int Body { get; private set; }
        public int Health { get; private set; }
        /// <summary>opt3 as the entry packet / 0x028a last sent it, and the bits the status icons seen since added.</summary>
        public int VirtueServer { get; private set; }
        private readonly HashSet<int> _efst = new HashSet<int>();
        public int Virtue { get; private set; }
        public bool DeepSleep { get; private set; }
        /// <summary>Sounds this component played (smoke), most recent last.</summary>
        public readonly List<string> Sounds = new List<string>();

        /// <summary>Where sprites come from (set by the game at start; falls back to the bootstrap's).</summary>
        public static RoAssets Assets;
        /// <summary>Every state change, for the log / smoke.</summary>
        public static event System.Action<Unit, string> Changed;

        public static UnitState Of(Unit u)
        {
            if (u == null) return null;
            var s = u.GetComponent<UnitState>();
            if (s == null) { s = u.gameObject.AddComponent<UnitState>(); s._u = u; }
            return s;
        }

        private static RoAssets A => Assets ?? (LokiBootstrap.Instance != null ? LokiBootstrap.Instance.Assets : null);

        // ------------------------------------------------------------------ opt1
        /// <summary>opt1 from an entry packet or 0x0229. Same value: nothing happens (EntityState.js:194).</summary>
        public void SetBody(int opt1)
        {
            if (opt1 == Body) return;
            int old = Body;
            // leave the old state (EntityState.js:204-233)
            switch (old)
            {
                case Opt1Sleep: StatusOverlay.Remove(_u, "status-sleep"); break;
                case Opt1Freeze:
                    Sound("_frozen_explosion.wav");
                    Overlay("status-freeze", IceFile, 1, StatusOverlay.Mode.Once); // frame: 1 = the shattering action, played once
                    _u.Sprite.Release(_u.IsPlayerType ? (int)ActorSprite.PlayerAction.Standby : 0, true, DeadGroup);
                    break;
                case Opt1Stone:
                    Sound("_stone_explosion.wav");
                    _u.Sprite.Release();
                    break;
                case Opt1Stun: StatusOverlay.Remove(_u, "status-stun"); break;
            }
            // enter the new one (EntityState.js:236-289)
            switch (opt1)
            {
                case Opt1Stone: _u.Sprite.Hold(); break;
                case Opt1StoneWait: Sound("_stonecurse.wav"); break;
                case Opt1Sleep: Overlay("status-sleep", "status-sleep", 0, StatusOverlay.Mode.Loop, head: true); break;
                case Opt1Freeze:
                    Overlay("status-freeze", IceFile, 0, StatusOverlay.Mode.Hold); // animationId 0: the first frame, held
                    _u.Sprite.HoldPose(_u.IsPlayerType ? (int)ActorSprite.PlayerAction.Freeze2 : 0);
                    break;
                case Opt1Stun: Sound("_stun.wav"); Overlay("status-stun", "status-stun", 0, StatusOverlay.Mode.Loop, head: true); break;
            }
            Body = opt1;
            _u.Sprite.SetTint(ActorSprite.TintFactor.Body, BodyTint(opt1));
            Report($"body {old} → {opt1} ({BodyName(opt1)})");
        }

        public static Color BodyTint(int opt1) =>
            opt1 == Opt1Stone ? new Color(0.1f, 0.1f, 0.1f, 1f) : opt1 == Opt1StoneWait ? new Color(0.3f, 0.3f, 0.3f, 1f) : opt1 == Opt1Freeze ? new Color(0f, 0.4f, 0.8f, 1f) : Color.white;

        public static string BodyName(int opt1)
        {
            switch (opt1)
            {
                case 0: return "none"; case Opt1Stone: return "Stone"; case Opt1Freeze: return "Freeze"; case Opt1Stun: return "Stun";
                case Opt1Sleep: return "Sleep"; case 5: return "5"; case Opt1StoneWait: return "StoneWait"; case Opt1Burning: return "Burning";
                case Opt1Imprison: return "Imprison"; default: return opt1.ToString();
            }
        }

        private int DeadGroup => _u.IsPlayerType ? (int)ActorSprite.PlayerAction.Dead : (int)ActorSprite.MonsterAction.Dead;

        // ------------------------------------------------------------------ opt2
        /// <summary>opt2 from an entry packet or 0x0229 (EntityState.js:300-368).</summary>
        public void SetHealth(int opt2)
        {
            if (opt2 == Health) return;
            int old = Health;
            bool Gained(int bit) => (opt2 & bit) != 0 && (old & bit) == 0;
            var c = Color.white;
            if ((opt2 & Opt2Curse) != 0)
            {
                if (Gained(Opt2Curse)) { Sound("_curse.wav"); Overlay("status-curse", "status-curse", -1, StatusOverlay.Mode.Loop, head: true, opacity: 0.5f); }
                c.r *= 0.5f; c.g *= 0.15f; c.b *= 0.1f;
            }
            else StatusOverlay.Remove(_u, "status-curse");
            if ((opt2 & Opt2Poison) != 0)
            {
                if (Gained(Opt2Poison)) Sound("_poison.wav");
                c.r *= 0.9f; c.g *= 0.4f; c.b *= 0.8f;
            }
            if (Gained(Opt2Blind)) Sound("_blind.wav");
            if ((opt2 & Opt2Silence) != 0)
            {
                if (Gained(Opt2Silence))
                {
                    Sound("_silence.wav");
                    // the emotion sprite's action for ET 9 ("/…", Emotions.indexes[9]; data/emotions.tsv from the client's emotionlist.lub)
                    int act = Game.Emotes.ActionOf(9);
                    if (act >= 0) Overlay("status-silence", "emotion", act, StatusOverlay.Mode.Loop, head: true);
                }
            }
            else StatusOverlay.Remove(_u, "status-silence");
            Health = opt2;
            _u.Sprite.SetTint(ActorSprite.TintFactor.Health, c);
            Report($"health 0x{old:x} → 0x{opt2:x} ({HealthNames(opt2)})");
        }

        public static string HealthNames(int opt2)
        {
            if (opt2 == 0) return "none";
            var n = new List<string>();
            string[] names = { "Poison", "Curse", "Silence", "Confusion", "Blind", "Angelus", "Bleeding", "DPoison", "Fear" };
            for (int i = 0; i < names.Length; i++) if ((opt2 & (1 << i)) != 0) n.Add(names[i]);
            return string.Join("+", n);
        }

        // ------------------------------------------------------------------ opt3
        /// <summary>opt3 from an entry packet (virtue) or 0x028a (showEFST): replaces what the icons had set.</summary>
        public void SetVirtue(int opt3)
        {
            VirtueServer = opt3; _efst.Clear();
            ApplyVirtue();
        }

        /// <summary>A status icon started / ended on this unit (0x0983 / 0x0196 / 0x0984).</summary>
        public void OnEfst(int type, bool on, int val1)
        {
            if (type == EfstAllRiding)
            {
                // roBrowserLegacy Entity.js:2375-2381: the state of 0x0983 / 0x0196, val1 of 0x0984. rAthena sends val1 = 1 in both: 0x0984 carries
                // the display value dval1 = 1 (status.cpp:13093-13095), 0x0983 sends 1 for a status without SendVal1 (status.cpp:13190)
                bool ride = on && val1 != 0;
                if (_u.AllRiding != ride) { _u.AllRiding = ride; if (_u.IsPlayerType && A != null) _u.ApplyLook(A); Report($"all riding {ride} → drawn as class {_u.MountJob}"); }
                return;
            }
            if (type == EfstDeepSleep)
            {
                DeepSleep = on;
                if (on && Body == 0) SetBody(Opt1Sleep);
                else if (!on && Body == Opt1Sleep) SetBody(0);
                return;
            }
            if (!EfstOpt3.TryGetValue(type, out int bit)) return;
            if (on) _efst.Add(type);
            else
            {
                _efst.Remove(type);
                // the icon ended: the bit goes unless another active icon still sets it (several quickens share one bit)
                bool still = false; foreach (int t in _efst) if (EfstOpt3[t] == bit) still = true;
                if (!still) VirtueServer &= ~bit;
            }
            ApplyVirtue();
        }

        /// <summary>
        /// Client wave 4 (w4units): an opt3 bit a packet other than the entry packets / 0x028a / the icons turns on or off — Blade Stop's
        /// ZC_BLADESTOP 0x01d1 (roBrowserLegacy Entity.js:2582 srcEntity.toggleOpt3(BLADESTOP, state)). Kept apart from the server's opt3.
        /// </summary>
        public void ToggleOpt3(int bit, bool on)
        {
            int old = _extraOpt3;
            _extraOpt3 = on ? _extraOpt3 | bit : _extraOpt3 & ~bit;
            if (old != _extraOpt3) ApplyVirtue();
        }
        private int _extraOpt3;

        private void ApplyVirtue()
        {
            int v = VirtueServer | _extraOpt3;
            foreach (int t in _efst) v |= EfstOpt3[t];
            if (v == Virtue) return;
            int old = Virtue; Virtue = v;
            _u.Sprite.SetTint(ActorSprite.TintFactor.Virtue, VirtueTint(v));
            if (v != 0 || _u.GetComponent<UnitVirtueFx>() != null) UnitVirtueFx.Of(_u).SetVirtue(v); // Assumptio's halo, the after-images
            Report($"virtue 0x{old:x} → 0x{v:x}");
        }

        /// <summary>roBrowserLegacy EntityState.js:118-185 updateVirtue (later bits overwrite earlier channels, as there).</summary>
        public static Color VirtueTint(int v)
        {
            var c = Color.white;
            if ((v & Opt3Quicken) != 0) c.b = 0f;
            if ((v & Opt3ExplosionSpirits) != 0) { c.r = 1f; c.g = 0.75f; c.b = 0.75f; }
            if ((v & Opt3BladeStop) != 0) { c.r = 0.25f; c.g = 0.25f; c.b = 0.25f; }
            if ((v & (Opt3EnergyCoat | Opt3Bunsin)) != 0) { c.r = 0.5f; c.g = 0.5f; c.b = 0.85f; }
            if ((v & Opt3OverThrust) != 0) { c.g = 0.75f; c.b = 0.75f; }
            if ((v & Opt3Warm) != 0) { c.g = 0.4f; c.b = 0.4f; }
            if ((v & Opt3SoulLink) != 0) { c.r = 0.35f; c.g = 0.35f; c.b = 0.9f; c.a = 0.9f; }
            if ((v & Opt3Undead) != 0) { c.r = 0.7f; c.b = 0.65f; }
            if ((v & Opt3Marionette) != 0) { c.r = 1f; c.g = 0.34f; c.b = 0.71f; c.a = 0.5f; }
            if ((v & Opt3Berserk) != 0) { c.r = 1f; c.g = 0.4f; c.b = 0.4f; c.a = 1f; }
            return c;
        }

        // ------------------------------------------------------------------ helpers
        private const string IceFile = "¾óÀ½¶¯"; // 얼음땡 as its CP949 bytes read as Latin-1 (rB '\xbe\xf3\xc0\xbd\xb6\xaf')

        private void Overlay(string uid, string file, int action, StatusOverlay.Mode mode, bool head = false, float opacity = 1f)
        {
            var a = A;
            if (a == null || !a.Sprite($"data\\sprite\\{RoNames.Effect}\\{file}", out SprFile spr, out ActFile act)) { Debug.LogWarning($"[W3Status] no sprite for {uid} ({file})"); return; }
            StatusOverlay.Attach(_u, uid, spr, act, action, mode, 0, head, opacity);
        }

        private void Sound(string wav)
        {
            Sounds.Add(wav); if (Sounds.Count > 32) Sounds.RemoveAt(0);
            Audio.LokiAudio.Instance?.PlayWav(wav, _u.Cell);
        }

        private void Report(string what)
        {
            Debug.Log($"[W3Status] {_u.Id} ({_u.Name}): {what}; overlays [{StatusOverlay.List(_u)}], held {_u.Sprite.Held}, tint {Fmt(_u.Sprite.Tint)}");
            Changed?.Invoke(_u, what);
        }

        public static string Fmt(Color c) => $"({c.r:0.00}, {c.g:0.00}, {c.b:0.00}, {c.a:0.00})";
    }
}
