using Loki.World;
using UnityEngine;

namespace Loki.Sprites
{
    /// <summary>
    /// Client wave 4 (w4units): the looks other units' status icons give them beyond w3status's (UnitState: ailments, opt3, Hiding's puff,
    /// boarding). Status icons of players reach every client in the area (a player status with an icon: status.cpp:16419-16423
    /// StatusRelevantBLTypes = BL_PC, clif.cpp:6555-6561) as ZC_MSG_STATE_CHANGE 0x0983 / ZC_STATE_CHANGE 0x0196, and the DisplayPc ones
    /// again as 0x0984 when the player comes into view (clif.cpp:5083, 6623-6624). Ids: the client's efstids.lub (data/efst.tsv), which
    /// rAthena's efst_type (status.hpp:1453…) follows. Looks: roBrowserLegacy Engine/MapEngine/Entity.js:1966-2406 onEntityStatusChange
    /// and Renderer/Entity/EntityState.js:375-501 (a reimplementation; the official client could not be checked):
    ///  • EFST_RUN 133 (TK_RUN): EF_RUN 442 when it starts, EF_STOPEFFECT 444 when it ends (Entity.js:2060-2074);
    ///  • EFST_TING 134: EF_QUAKEBODY 426 (2076-2084);
    ///  • the Taekwon stances — EFST_STORMKICK_ON / _READY 135 / 136, DOWNKICK 137 / 138, TURNKICK 139 / 140, COUNTER 141 / 142: the SKILL
    ///    action (group 12) still on frame 0 / 2 / 3 / 4; EFST_DODGE_ON / _READY 143 / 144: the PICKUP action (group 3) still on frame 1
    ///    (2086-2148; rB sets the frame whatever the state, so the pose stays until the unit's next action: Loki sets it when the icon
    ///    starts, and the pose already shown stays when it ends, the same);
    ///  • EFST_TRICKDEAD 29 (Play Dead): the death pose, idle when it ends (2233-2252);
    ///  • EFST_STOP 95 (NPC_STOP): the 이팩트\스톱 attachment, looping, until it ends (2303-2313);
    ///  • EFST_SUHIDE 933 (Doram hide): the SKILL action still on frame 4, idle when it ends (2326-2344); it also hides the unit (below);
    ///  • EFST_SU_STOOP 893: the SKILL action, frames 1-3 looping; idle when it ends (2346-2365) — Loki loops the whole SKILL action;
    ///  • EFST_MONSTER_TRANSFORM 621 / EFST_ACTIVE_MONSTER_TRANSFORM 665 (transformation scrolls; DisplayPc + SendVal1 = the monster's
    ///    id, status.yml Monster_Transform / Active_Monster_Transform): the player is drawn as that monster, without hair or headgear
    ///    (rB EntityView.js:159-196 getEffectiveJob / shouldSuppressHead; the active one wins);
    ///  • EFST_CAMOUFLAGE 360, EFST_STEALTHFIELD 379: rB fades the unit to alpha 0.1 with effect\assasin_cloaking.wav
    ///    (EntityState.js:451-462). rAthena treats both, and SUHIDE, exactly like hiding for sight (status_check_visibility,
    ///    status.cpp:2327: OPTION_HIDE|CLOAK|CHASEWALK or SC_CAMOUFLAGE / SC_STEALTHFIELD / SC_SUHIDE → not visible), and the design's decision
    ///    (2026-09-30) keeps other players' hidden units fully invisible: Loki draws them as hidden units (EntityManager.ApplyOption:
    ///    alpha 0, no name plate, not pointable; our own character 0.45), playing rB's sound;
    ///  • EFST_SHADOWFORM 394: rB's shade (0.2, 0.2, 0.2, alpha 0.2) and the same sound (464-477) — not a hiding state in rAthena;
    ///  • EFST_CLAIRVOYANCE 184 on ourselves (Maya Purple's intravision, status.yml Intravision): every hidden unit is drawn black
    ///    instead of invisible (EntityState.js:441-458; the server keeps sending hidden units' moves to such a player, clif.cpp:452-453).
    ///    It still can't be pointed at: intravision does not make a unit targetable (status_check_visibility only lets detectors see).
    /// </summary>
    public sealed class UnitEfstLooks : MonoBehaviour
    {
        public const int EfstTrickDead = 29, EfstStop = 95, EfstRun = 133, EfstTing = 134, EfstStormkickOn = 135, EfstStormkickReady = 136,
            EfstDownkickOn = 137, EfstDownkickReady = 138, EfstTurnkickOn = 139, EfstTurnkickReady = 140, EfstCounterOn = 141, EfstCounterReady = 142,
            EfstDodgeOn = 143, EfstDodgeReady = 144, EfstClairvoyance = 184, EfstCamouflage = 360, EfstStealthField = 379, EfstShadowForm = 394,
            EfstMonsterTransform = 621, EfstActiveMonsterTransform = 665, EfstSuStoop = 893, EfstSuHide = 933;
        public const int EfRun = 442, EfStopEffect = 444, EfQuakeBody = 426;
        private const string StopFile = "½ºÅé"; // 스톱 (CP949 as Latin-1; rB '\xbd\xba\xc5\xe9')
        private const string CloakSound = "effect\\assasin_cloaking.wav";

        /// <summary>Our own intravision (EFST_CLAIRVOYANCE on ourselves): hidden units are drawn black.</summary>
        public static bool Intravision { get; private set; }

        private Unit _u;
        public bool Camouflage, StealthField, SuHide, ShadowForm;
        public int TransformMob, ActiveTransformMob;
        /// <summary>A status hides the unit from others (rAthena status_check_visibility, status.cpp:2327).</summary>
        public bool HiddenByStatus => Camouflage || StealthField || SuHide;
        public string LastLook = "";

        public static UnitEfstLooks Of(Unit u)
        {
            var l = u.GetComponent<UnitEfstLooks>();
            if (l == null) { l = u.gameObject.AddComponent<UnitEfstLooks>(); l._u = u; }
            return l;
        }
        public static UnitEfstLooks Find(Unit u) => u != null ? u.GetComponent<UnitEfstLooks>() : null;
        public static bool IsHiddenByStatus(Unit u) { var l = Find(u); return l != null && l.HiddenByStatus; }
        /// <summary>The monster a transformation scroll draws the unit as (the active one wins), 0 = none.</summary>
        public static int TransformOf(Unit u) { var l = Find(u); return l == null ? 0 : l.ActiveTransformMob > 0 ? l.ActiveTransformMob : l.TransformMob; }

        /// <summary>A status icon started / ended on a unit in view. Returns true when the unit's visibility must be recomputed
        /// (EntityManager.RefreshVisibility), and plays / sets whatever the icon draws.</summary>
        public static bool OnEfst(Unit u, int type, bool on, int val1, Loki.Effects.EffectManager fx, RoAssets assets, bool isSelf, System.Action<Unit> relook)
        {
            if (u == null) return false;
            var s = u.Sprite;
            switch (type)
            {
                case EfstClairvoyance:
                    if (!isSelf) return false;
                    if (Intravision == on) return false;
                    Intravision = on; Log(u, "intravision " + on);
                    return true; // the caller re-applies every unit's visibility
                case EfstRun:
                {
                    int parts = fx != null ? fx.Spam((on ? EfRun : EfStopEffect).ToString(), u.Id, null, Time.time, -1f) : 0;
                    Of(u).LastLook = on ? "run" : "run end"; Log(u, $"run {(on ? "starts: EF_RUN" : "ends: EF_STOPEFFECT")} ({parts} drawable part(s))");
                    return false;
                }
                case EfstTing:
                    if (!on) return false;
                    { int parts = fx != null ? fx.Spam(EfQuakeBody.ToString(), u.Id, null, Time.time, -1f) : 0; Of(u).LastLook = "ting"; Log(u, $"ting: EF_QUAKEBODY ({parts} drawable part(s))"); }
                    return false;
                case EfstStormkickOn: case EfstStormkickReady: Stance(u, on, (int)ActorSprite.PlayerAction.Skill, 0, "storm kick stance"); return false;
                case EfstDownkickOn: case EfstDownkickReady: Stance(u, on, (int)ActorSprite.PlayerAction.Skill, 2, "down kick stance"); return false;
                case EfstTurnkickOn: case EfstTurnkickReady: Stance(u, on, (int)ActorSprite.PlayerAction.Skill, 3, "turn kick stance"); return false;
                case EfstCounterOn: case EfstCounterReady: Stance(u, on, (int)ActorSprite.PlayerAction.Skill, 4, "counter kick stance"); return false;
                case EfstDodgeOn: case EfstDodgeReady: Stance(u, on, (int)ActorSprite.PlayerAction.PickUp, 1, "tumbling"); return false;
                case EfstTrickDead:
                    if (!u.IsPlayerType) return false;
                    if (on) s.Play((int)ActorSprite.PlayerAction.Dead, false); else s.Play((int)ActorSprite.PlayerAction.Idle, true);
                    Of(u).LastLook = on ? "play dead" : "play dead end"; Log(u, on ? "play dead: the death pose" : "play dead ends: idle");
                    return false;
                case EfstStop:
                    if (on && assets != null && assets.Sprite($"data\\sprite\\{RoNames.Effect}\\{StopFile}", out var spr, out var act))
                        StatusOverlay.Attach(u, "status-stop", spr, act, -1, StatusOverlay.Mode.Loop);
                    else if (!on) StatusOverlay.Remove(u, "status-stop");
                    Of(u).LastLook = on ? "stop" : "stop end"; Log(u, $"stop {(on ? "on" : "off")}: overlays [{StatusOverlay.List(u)}]");
                    return false;
                case EfstSuStoop:
                    if (!u.IsPlayerType) return false;
                    if (on) s.Play((int)ActorSprite.PlayerAction.Skill, true); else s.Play((int)ActorSprite.PlayerAction.Idle, true);
                    Of(u).LastLook = on ? "stoop" : "stoop end"; Log(u, on ? "stoop: the skill action, looping" : "stoop ends: idle");
                    return false;
                case EfstSuHide:
                {
                    var l = Of(u); l.SuHide = on;
                    if (u.IsPlayerType) { if (on) s.FreezeFrame((int)ActorSprite.PlayerAction.Skill, 4); else s.Play((int)ActorSprite.PlayerAction.Idle, true); }
                    l.LastLook = on ? "doram hide" : "doram hide end"; Log(u, $"doram hide {on}");
                    return true;
                }
                case EfstCamouflage: case EfstStealthField:
                {
                    var l = Of(u);
                    bool was = l.HiddenByStatus;
                    if (type == EfstCamouflage) l.Camouflage = on; else l.StealthField = on;
                    if (on && !was) Audio.LokiAudio.Instance?.PlayWav(CloakSound, u.Cell);
                    l.LastLook = (type == EfstCamouflage ? "camouflage " : "stealth field ") + on; Log(u, l.LastLook + " → hidden " + l.HiddenByStatus);
                    return true;
                }
                case EfstShadowForm:
                {
                    var l = Of(u); l.ShadowForm = on;
                    s.SetTint(ActorSprite.TintFactor.Stealth, on ? new Color(0.2f, 0.2f, 0.2f, 0.2f) : Color.white);
                    if (on) Audio.LokiAudio.Instance?.PlayWav(CloakSound, u.Cell);
                    l.LastLook = "shadow form " + on; Log(u, $"{l.LastLook}: tint {UnitState.Fmt(s.Tint)}");
                    return false;
                }
                case EfstMonsterTransform: case EfstActiveMonsterTransform:
                {
                    var l = Of(u);
                    int mob = on ? val1 : 0;
                    if (type == EfstMonsterTransform) l.TransformMob = mob; else l.ActiveTransformMob = mob;
                    l.LastLook = $"transform {TransformOf(u)}"; Log(u, $"{(type == EfstMonsterTransform ? "monster" : "active monster")} transformation {(on ? "into " + val1 : "ends")} → drawn as {TransformOf(u)}");
                    relook?.Invoke(u);
                    return false;
                }
            }
            return false;
        }

        private static void Stance(Unit u, bool on, int group, int frame, string what)
        {
            if (!u.IsPlayerType) return;
            if (on) u.Sprite.FreezeFrame(group, frame);
            Of(u).LastLook = what + (on ? "" : " end");
            Log(u, on ? $"{what}: action {group} frame {frame}, still" : $"{what} ends (the pose stays until the next action)");
        }

        private static void Log(Unit u, string what) => Debug.Log($"[W4Units] {u.Id} ({u.Name}): {what}");
    }
}
