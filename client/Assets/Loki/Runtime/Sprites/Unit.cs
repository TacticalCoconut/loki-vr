using Loki.Formats;
using Loki.UI;
using Loki.World;
using UnityEngine;

namespace Loki.Sprites
{
    /// <summary>
    /// A unit on the map: position in GAT cells (fractional while walking), facing, look (job/sex/hair) and
    /// its ActorSprite. Walks the server's own path, step by step at the server's speed and on the server's clock
    /// (client wave 3, w3move: <see cref="WalkTo"/>), and slides for knockbacks, fast moves and stops (<see cref="Slide"/>).
    /// Client wave 10 (w10move): <see cref="Cell"/> is the LOGICAL position, the one every rule of the game reads; the body is drawn
    /// at <see cref="DrawnCell"/> (Sprites/Unit.Smooth.cs: smoothed, at most half a cell away), which only the transform follows.
    /// </summary>
    public sealed partial class Unit : MonoBehaviour
    {
        /// <summary>What the pointer does with it. From the packet's objecttype (rAthena clif_bl_type) and the class id.</summary>
        public enum UnitKind { Player, Npc, Monster, Warp, Other, Companion }

        public uint Id;
        public int Job, Sex, Hair, HeadgearTop, HeadgearMid, HeadgearLow;
        /// <summary>Full look (players): equipment, palettes, costume flag. Job/Sex/Hair above stay the authoritative copies.</summary>
        public PlayerLook Look = new PlayerLook();
        public string Name = "";
        /// <summary>Guild id and emblem version from the unit packets (GUID / GEmblemVer — only the low 16 bits of the version), and the
        /// party / guild / position names from ZC_ACK_REQNAMEALL2 0x0a30 (asked for with 0x0368; research/guild_woe_packets.md §2.23-2.24).</summary>
        public uint GuildId; public int GuildEmblem; public string PartyName = "", GuildName = "", GuildTitle = "";
        /// <summary>Achievement title id from 0x0a30 (0 = none).</summary>
        public int TitleId;
        public const int DefaultPlayerWalkSpeed = 136;
        public const int DefaultWalkSpeed = 150;
        public int Speed = DefaultWalkSpeed;           // ms per cell
        public bool IsPlayerType, IsMob, IsNpc;
        public UnitKind Kind = UnitKind.Other;
        public bool IsSelf;
        /// <summary>True after ZC_NOTIFY_VANISH CLR_DEAD (1) or idle_unit state 1 until ZC_RESURRECTION / SetCell.</summary>
        public bool Dead { get; private set; }
        /// <summary>e_option bits from the unit packets / ZC_STATE_CHANGE (hide, cloak, cart, riding, falcon, costumes…).</summary>
        public int Option;
        public ActorSprite Sprite { get; private set; }
        public Overhead Overhead { get; private set; }
        /// <summary>Walking a server path (0x0087 / 0x09fd), not a slide.</summary>
        public bool Walking => _motion == Motion.Walk;
        /// <summary>A knockback, fast move or stop slide is running (0x01ff / 0x08d2 / 0x0088).</summary>
        public bool Sliding => _motion == Motion.Slide;
        /// <summary>objecttype of the last unit packet (rA clif_bl_type): 0 PC, 5 monster, 7 pet … -1 unknown.</summary>
        public int ObjectType = -1;

        public Vector2 Cell;              // current (fractional) LOGICAL position: on the server's path, as before client wave 10 (the body is drawn at DrawnCell)
        private GatFile _gat;
        private BoxCollider _collider;
        private bool _hover;

        /// <summary>rAthena JT_WARPNPC (every `warp`/`warp2` script line) and JT_HIDDEN_WARP_NPC (touch-area NPCs).</summary>
        public const int WarpClass = 45, HiddenWarpClass = 139, HiddenNpcClass = 111; // npc.hpp e_job_types

        /// <param name="look">Optional full player look (weapon, headgear, palettes …); job/sex/hair arguments win over its copies.</param>
        public static Unit Create(Transform mapRoot, GatFile gat, uint id, int job, int sex, int hair, RoAssets assets, PlayerLook look = null)
        {
            var go = new GameObject($"unit:{id}:{job}");
            go.transform.SetParent(mapRoot, false);
            var u = go.AddComponent<Unit>();
            u.Id = id; u.Job = job; u.Sex = sex; u.Hair = hair; u._gat = gat;
            if (look != null) u.Look = look.Clone();
            u.RefreshClassFlags();
            u.Kind = u.IsPlayerType ? UnitKind.Player : u.IsNpc ? UnitKind.Npc : UnitKind.Monster; // refined by SetObjectType
            if (u.Kind == UnitKind.Player) u.Speed = DefaultPlayerWalkSpeed;
            u.Sprite = go.AddComponent<ActorSprite>();
            u.Sprite.IsPlayerType = u.IsPlayerType;
            u.ApplyLook(assets);
            return u;
        }

        /// <summary>Player/NPC/monster flags from the class id (after a LOOK_BASE change too). Kind is left as the packet set it.</summary>
        public void RefreshClassFlags()
        {
            IsPlayerType = RoNames.IsPlayer(Job); IsNpc = RoNames.IsNpc(Job); IsMob = !IsPlayerType && !IsNpc;
            if (Sprite != null) Sprite.IsPlayerType = IsPlayerType;
        }

        /// <summary>Action group a player's normal attack plays with the equipped weapon (5, 10 or 11); monsters use 2.</summary>
        public int AttackActionGroup => IsPlayerType
            ? AppearanceDb.AttackActionGroup(Job, Sex, AppearanceDb.AttackWeaponType(Look))
            : (int)ActorSprite.MonsterAction.Attack;

        /// <summary>The action a unit falls back to when a one-shot animation ends: walking if it is on the move, else the
        /// battle stance for players (group 4 — the only group besides the attack itself that draws weapon and shield) or idle.</summary>
        private int RestGroup(bool afterAttack)
        {
            if (Walking) return 1;
            if (!IsPlayerType) return 0;
            int cur = Sprite.ActionGroup;
            if (cur == (int)ActorSprite.PlayerAction.Sit) return cur;                                   // hit while sitting: stays seated
            if (afterAttack || cur == (int)ActorSprite.PlayerAction.Standby) return (int)ActorSprite.PlayerAction.Standby;
            return 0;
        }

        /// <summary>Plays the weapon's attack swing, then after attackMT ms (≤ 0: the swing's own length) the battle stance
        /// (roBrowserLegacy Entity.js:846-866 READYFIGHT); monsters go back to idle.</summary>
        public void PlayAttack(float attackMs = 0f) { if (!Dead) Sprite.PlayThen(AttackActionGroup, attackMs, RestGroup(true)); }

        /// <summary>Flinch (PC group 6 / monster group 3), then back to walking or idle instead of freezing on the last frame.</summary>
        public void PlayHurt() { if (!Dead) Sprite.PlayThen(IsPlayerType ? (int)ActorSprite.PlayerAction.Hurt : (int)ActorSprite.MonsterAction.Hurt, 0f, RestGroup(false)); }

        /// <summary>Replaces the whole look (copies it) and rebuilds the sprite layers.</summary>
        public void SetLook(PlayerLook look, RoAssets assets)
        {
            Look = look.Clone();
            Job = look.Job; Sex = look.Sex; Hair = look.Hair;
            RefreshClassFlags();
            ApplyLook(assets);
        }

        /// <summary>objecttype from the unit packets: 0 PC, 1/6/0xC NPC, 5/0xD/0xE monster, 7 pet, 8 homunculus, 9 mercenary, 0xA elemental.</summary>
        public void SetObjectType(int objecttype)
        {
            var prevKind = Kind;
            ObjectType = objecttype;
            if (Job == WarpClass || Job == HiddenWarpClass) Kind = UnitKind.Warp;
            else if (Job == HiddenNpcClass || Job == 2337) Kind = UnitKind.Other; // invisible script NPCs (no sprite; the client never shows them)
            else switch (objecttype)
            {
                case 0x0: Kind = UnitKind.Player; break;
                case 0x1: case 0x6: case 0xC: Kind = UnitKind.Npc; break;
                case 0x5: case 0xD: case 0xE: Kind = UnitKind.Monster; break;
                case 0x7: case 0x8: case 0x9: case 0xA: Kind = UnitKind.Companion; break; // pet, homunculus, mercenary, elemental
                default: Kind = UnitKind.Other; break;
            }
            if (Kind == UnitKind.Player && prevKind != UnitKind.Player && Speed == DefaultWalkSpeed) Speed = DefaultPlayerWalkSpeed;
            else if (Kind != UnitKind.Player && prevKind == UnitKind.Player && Speed == DefaultPlayerWalkSpeed) Speed = DefaultWalkSpeed;
            SetupInteraction();
        }

        /// <summary>e_option riding bits (status.hpp:3025-3062): RIDING 0x20, DRAGON1-5, WUGRIDER, MADOGEAR — roBrowser EntityState.js:392-406.</summary>
        public const int RidingMask = 0x20 | 0x80000 | 0x800000 | 0x1000000 | 0x2000000 | 0x4000000 | 0x200000 | 0x400000;
        /// <summary>What a player is drawn as (client wave 4, w4units: Sprites/UnitLooks.cs — mounts, the boarding mount, costume outfits,
        /// the dragon's colour, the orc head).</summary>
        public UnitLooks.Drawn Drawn => UnitLooks.Resolve(Job, Option, AllRiding);
        /// <summary>The class drawn instead of the job (a mount, the boarding mount of EFST_ALL_RIDING, a costume outfit), else 0.</summary>
        public int MountJob { get { if (!IsPlayerType) return 0; int d = Drawn.Job; return d != Job ? d : 0; } }
        /// <summary>EFST_ALL_RIDING on this unit (UnitState sets it from 0x0983 / 0x0196 / 0x0984).</summary>
        public bool AllRiding;
        /// <summary>Client wave 4 (w4units): a pet's accessory view id (ZC_CHANGESTATE_PET 0x01a4 type 3; 0 = none) — its body ACT is
        /// replaced by <see cref="PetAccessoryTable"/>'s file (roBrowserLegacy Engine/MapEngine/Pet.js:190-203).</summary>
        public int PetAccessory;
        /// <summary>Hidden from us (drawn with alpha 0, or black by our intravision): no name plate, not pointable (EntityManager.SetUnseen).</summary>
        public bool Unseen;

        /// <summary>Client wave 5 (w5looks): the body size an effect set (roBrowserLegacy's xSize / ySize ÷ 5: EF_BABYBODY / EF_BABYBODY2 2.5 → 0.5,
        /// EF_GIANTBODY / EF_GIANTBODY2 7.5 → 1.5; DB/Effects/EffectTable.js:9187-9251, Fx/BodyFx.cs), null = the look's own size. rB writes xSize
        /// over the baby / adult size and only its job setter puts it back (Renderer/Entity/EntityView.js:252-253 `this.xSize = this.ySize =
        /// DB.isBaby(job) ? 4 : 5`): Loki forgets it when the class changes.</summary>
        public float? FxScale { get; private set; }
        private int _fxScaleJob = -1;
        /// <summary>Sets <see cref="FxScale"/> and the drawn size now (Fx/BodyFx.cs, every frame of a 300 ms transition).</summary>
        public void SetFxScale(float scale)
        {
            FxScale = scale; _fxScaleJob = Job;
            if (Sprite != null) Sprite.Scale = scale;
        }

        public void ApplyLook(RoAssets assets)
        {
            if (FxScale.HasValue && Job != _fxScaleJob) FxScale = null; // w5looks: a new class resets the size (rB EntityView.js:252-253)
            Look.Job = Job; Look.Sex = Sex; Look.Hair = Hair;
            HeadgearTop = Look.HeadTop; HeadgearMid = Look.HeadMid; HeadgearLow = Look.HeadBottom;
            if (Job == WarpClass || Job == HiddenWarpClass) { Sprite.SetBody(null, null); Sprite.ClearAttachments(); return; }
            if (IsPlayerType && UnitEfstLooks.TransformOf(this) > 0)
            {
                // w4units: a transformation scroll draws the player as the monster, without hair or headgear (roBrowserLegacy
                // EntityView.js:159-196 getEffectiveJob / shouldSuppressHead)
                string mb = RoNames.BodyPath(UnitEfstLooks.TransformOf(this), Sex);
                if (mb != null && assets.Sprite(mb, out var mspr, out var mact)) Sprite.SetBody(mspr, mact); else Sprite.SetBody(null, null);
                Sprite.BodyPath = mb;
                Sprite.ClearAttachments(); Sprite.Scale = FxScale ?? 1f;
                return;
            }
            if (IsPlayerType)
            {
                var drawn = Drawn;
                PlayerAppearance.Apply(Sprite, Look, assets, drawn);
                Sprite.Scale = FxScale ?? (UnitLooks.IsBaby(Job) || UnitLooks.IsBaby(drawn.Job) ? UnitLooks.BabyScale : 1f); // w4units: babies at 4/5 size; w5looks: an effect's size wins
                return;
            }
            Sprite.Scale = FxScale ?? 1f;
            string body = RoNames.BodyPath(Job, Sex);
            Sprite.BodyPath = body;
            if (body != null && assets.Sprite(body, out var bspr, out var bact))
            {
                // w4units: a pet's accessory swaps its body ACT for the accessory's (the SPR stays): roBrowserLegacy Pet.js:190-203
                if (PetAccessory > 0 && PetAccessoryTable.Act.TryGetValue(PetAccessory, out var accAct))
                {
                    string ap = "data\\sprite\\" + accAct.Replace('/', '\\');
                    if (ap.EndsWith(".act", System.StringComparison.OrdinalIgnoreCase)) ap = ap.Substring(0, ap.Length - 4);
                    var aact = assets.Exists(ap + ".act") ? assets.SpriteAct(ap) : null;
                    if (aact != null) bact = aact; else Debug.Log($"[W4Units] pet {Id}: no accessory ACT {ap}.act for view {PetAccessory}");
                }
                Sprite.SetBody(bspr, bact);
            }
            else { Sprite.SetBody(null, null); }
            Sprite.ClearAttachments();
            GrannyUnit.Apply(this, assets); // client wave 4b (w4granny): a .gr2 body in 3-D (the stand-in above until it is decoded, and as the fallback)
        }

        /// <summary>Client wave 4b (w4granny): the pointer box (width, height in cells) of a body that is not a sprite — a Granny model's
        /// (Sprites/GrannyUnit.cs); null: the sprite's size.</summary>
        public Vector2? SizeOverride;

        private bool _setup;
        /// <summary>Adds the pointer collider, name plate and (for warps) the portal marker.</summary>
        public void SetupInteraction()
        {
            if (_setup) return;
            _setup = true;
            // client wave 14 (w14play), point 22 of the design's headset list ("Hide all"): class 139 (HIDDEN_WARP_NPC, npc.hpp) is never drawn by
            // the official client: no marker, no "Door" plate, nothing for the laser or the look (no collider). It stays Kind Warp: the
            // walk still keeps off its cells (LokiGame.W3Walk.cs W3WalkWarpCells), as a touch area still acts when stepped on
            if (HiddenDoor) return;
            if (Kind == UnitKind.Warp)
            {
                PortalMarker.Attach(transform, Job == HiddenWarpClass);
                Overhead = Overhead.Attach(transform, Job == HiddenWarpClass ? Lang.T("Door") : Lang.T("Portal"), new Color(0.55f, 0.85f, 1f), 3.2f); // w4lang
            }
            else if (Kind == UnitKind.Other) return; // invisible NPCs: nothing to point at
            else if (Kind == UnitKind.Companion)
            {
                // pets, homunculi, mercenaries, elementals: a name on hover (always for one's own, set by the game) and pointable,
                // so one's own companion opens its window
                Overhead = Overhead.Attach(transform, DisplayName, new Color(0.85f, 0.9f, 1f), Mathf.Max(1.4f, Sprite.SizeCells.y + 0.3f));
                Overhead.AlwaysShowName = false;
            }
            else
            {
                Color c = Kind == UnitKind.Npc ? new Color(0.6f, 0.9f, 1f) : Kind == UnitKind.Monster ? new Color(1f, 0.85f, 0.8f) : Color.white;
                Overhead = Overhead.Attach(transform, DisplayName, c, Kind == UnitKind.Monster ? Mathf.Max(1.2f, Sprite.SizeCells.y + 0.3f) : 2.7f);
                Overhead.AlwaysShowName = Kind != UnitKind.Monster;
            }
            if (IsSelf) return; // never block the pointer with your own body
            _collider = gameObject.AddComponent<BoxCollider>();
            var t = gameObject.AddComponent<Targetable>();
            t.Unit = this;
            UpdateCollider();
        }

        /// <summary>Client wave 14 (w14play), point 22: a class-139 unit (HIDDEN_WARP_NPC), which nothing draws (SetupInteraction).</summary>
        public bool HiddenDoor => Job == HiddenWarpClass;

        public string DisplayName => Game.NpcDialog.CleanName(Name ?? "");

        public void SetName(string n)
        {
            Name = n ?? "";
            if (Overhead != null && Kind != UnitKind.Warp) Overhead.SetName(IsSelf ? "" : DisplayName);
        }

        public void SetHover(bool h)
        {
            if (_hover == h) return;
            _hover = h;
            if (Sprite != null) Sprite.SetTint(ActorSprite.TintFactor.Hover, h ? new Color(1f, 1f, 0.7f, 1f) : Color.white); // one factor of the composed colour (w3status)
            if (Overhead != null) Overhead.Hovered = h;
        }

        private void UpdateCollider()
        {
            if (_collider == null) return;
            var s = Kind == UnitKind.Warp ? new Vector2(2f, 3f) : SizeOverride ?? (Sprite != null ? Sprite.SizeCells : new Vector2(1f, 2f));
            float w = Mathf.Clamp(s.x, 0.8f, 4f), h = Mathf.Clamp(s.y, 0.8f, 6f);
            _collider.size = new Vector3(w, h, w);
            _collider.center = new Vector3(0, h * 0.5f, 0);
            if (Overhead != null && Kind == UnitKind.Monster) Overhead.HeightCells = h + 0.3f;
        }

        public void SetCell(int x, int y, int dir = -1)
        {
            Dead = false;
            if (_collider != null) _collider.enabled = true;
            var tgt = GetComponent<Targetable>(); if (tgt != null) tgt.enabled = true;
            _motion = Motion.None; _placed = true;
            Cell = new Vector2(x, y);
            SmoothTeleport(); // w10move: placed at once, the drawn body too
            if (dir >= 0) Sprite.Direction = dir;
            Sprite.Play(IsPlayerType ? (int)ActorSprite.PlayerAction.Idle : (int)ActorSprite.MonsterAction.Idle, true);
            Place();
        }

        /// <summary>Stops an active server walk at the unit's current position (when CLR_DEAD arrives while DeathSync waits for the killing hit).</summary>
        public void HaltWalkInPlace()
        {
            if (_collider != null) _collider.enabled = false;
            var tgt = GetComponent<Targetable>(); if (tgt != null) tgt.enabled = false;
            if (_motion != Motion.Walk) return;
            float now = Time.time;
            Vector2 drawnBefore = SmoothBefore(now);
            if (IsSelf && _placed) FreeStopped(drawnBefore, Cell);
            _slideCarry = false;
            _motion = Motion.None; _segs = 0; _yawMoving = false;
            if (Sprite != null && Sprite.ActionGroup == 1 && !DrawnOwnsLook) Sprite.Play(0, true);
            SmoothAfter(now, drawnBefore, Cell, false);
            Place();
        }

        /// <summary>Marks the unit dead, stops any walk or slide in place, and plays its death action once.</summary>
        public void Die()
        {
            float now = Time.time;
            if (_motion != Motion.None)
            {
                Vector2 drawnBefore = SmoothBefore(now);
                if (IsSelf && _placed) FreeStopped(drawnBefore, Cell);
                _slideCarry = false;
                _motion = Motion.None; _segs = 0; _yawMoving = false;
                SmoothAfter(now, drawnBefore, Cell, false);
            }
            Dead = true;
            if (_collider != null) _collider.enabled = false;
            var tgt = GetComponent<Targetable>(); if (tgt != null) tgt.enabled = false;
            if (IsSelf) { Steer = null; if (Held.HasValue) DropHold(); }
            _steering = false; _lookWalking = false; _remoteTarget = null; _remoteW = 0f;
            if (Sprite != null) Sprite.Play(IsPlayerType ? (int)ActorSprite.PlayerAction.Dead : (int)ActorSprite.MonsterAction.Dead, false);
            Place();
        }

        /// <summary>Revives a dead unit back to standing idle (ZC_RESURRECTION).</summary>
        public void Revive()
        {
            Dead = false;
            if (_collider != null) _collider.enabled = true;
            var tgt = GetComponent<Targetable>(); if (tgt != null) tgt.enabled = true;
            if (Sprite != null) Sprite.Play(0, true);
        }

        // ================================================================== movement (client wave 3, w3move)
        // The server walks a unit cell by cell along path_search's steps (rA unit.cpp:254-341 unit_walktoxy_sub, :542-790
        // unit_walktoxy_timer): a step takes `speed` ms, or speed*14/10 on a diagonal (unit.cpp:226-229, integer maths), and the
        // unit's cell changes at the end of each step (unit.cpp:623-625). The move packets carry only the start cell, the
        // destination and the server tick the walk started at (rA clif.cpp:2051-2061 clif_walkok, :1369-1467
        // clif_set_unit_walking); the client finds the same steps with RoPathSearch on its GAT and places the unit on them by
        // the server's clock (ServerClock).
        private enum Motion { None, Walk, Slide }
        private Motion _motion;
        private bool _placed;
        private const int MaxPoints = RoPathSearch.MaxWalkPath + 4;
        // segment i goes _pts[i] → _pts[i + 1], from local time _segStart[i] for _segDur[i] s, facing _segDir[i] (-1: keep)
        private readonly Vector2[] _pts = new Vector2[MaxPoints];
        private readonly float[] _segStart = new float[MaxPoints], _segDur = new float[MaxPoints];
        private readonly int[] _segDir = new int[MaxPoints];
        private int _segs, _segShown = -1;
        private static readonly RoPathSearch.WalkPath SharedPath = new RoPathSearch.WalkPath();

        /// <summary>Smoke / diagnostics: log every planned walk and slide ([Move] lines).</summary>
        public static bool LogMoves;

        /// <summary>The last server walk as planned (smoke checks and diagnostics).</summary>
        public sealed class WalkInfo
        {
            public int X0, Y0, X1, Y1, Sx, Sy, TotalMs, ElapsedMs;
            /// <summary>"A*", "easy", "easy (A* failed)" or "straight (no path)".</summary>
            public string Kind;
            /// <summary>The cells the server's unit stands on: Cells[0] = (X0,Y0) … the destination.</summary>
            public Vector2Int[] Cells;
            /// <summary>Server ms from moveStartTime until Cells[k] is reached (ReachMs[0] = 0) — unit_get_walkpath_time's sum.</summary>
            public int[] ReachMs;
            public uint MoveStart; public bool HasStart;
            /// <summary>Catch-up factor applied to the step durations (1 = the server's own pace).</summary>
            public float Factor;
            /// <summary>true: the unit was placed where the server's unit is now (it had no position or was far away).</summary>
            public bool Jumped;
            /// <summary>Local times (Time.time): when the walk was planned, when the plan reaches (x1,y1), when the unit got there.</summary>
            public float PlannedLocal, PlannedArriveLocal, ArriveLocal = -1f;
            /// <summary>Local time (Time.time) at which the unit is drawn reaching Cells[k]; -1 when the plan does not pass its centre.</summary>
            public float[] ReachLocal;
        }
        public WalkInfo LastWalk { get; private set; }

        /// <summary>
        /// Server move (ZC_NOTIFY_PLAYERMOVE 0x0087 for us, ZC_NOTIFY_MOVE / unit_walking 0x09fd for others): from (x0,y0) —
        /// with the sub-cell sx0 / sy0, "client-side: x0+=sx0*0.0625-0.5" (rA clif.cpp:181-190 WBUFPOS2) — to (x1,y1), the
        /// walk having started on the server at moveStartTime (its gettick(), clif.cpp:2056 / :1395).
        ///  1. The steps: RoPathSearch from (x0,y0) — the server's cell — to (x1,y1) with CELL_CHKNOPASS, the check of
        ///     unit_walktoxy_sub (unit.cpp:268): A*, or the easy path when <paramref name="easyPath"/> (a pet going for loot,
        ///     pet.cpp:1855); if that search fails the other one; if both fail a straight line (the client before wave 3).
        ///  2. How far the server's unit already is: ServerClock's now − moveStartTime, clamped to the walk.
        ///  3. A unit already on the map within 2.5 cells of the start keeps going from where it is drawn (no jump): first
        ///     to the start cell (or straight on if it is already on the first step), and the whole walk runs a little faster or
        ///     slower (x0.6 … x1.25) so that it reaches (x1,y1) when the server's unit does. A unit that just appeared, or was
        ///     further away, is placed where the server's unit is now and walks on at the server's pace.
        /// A step's facing is its own direction (unit_walktoxy_timer turns the unit each step, unit.cpp:576).
        /// </summary>
        public void WalkTo(int x0, int y0, int x1, int y1, int sx0 = 8, int sy0 = 8, uint moveStartTime = 0, bool hasStartTime = false, bool easyPath = false)
        {
            if (Dead) return;
            float now = Time.time;
            Vector2 drawnBefore = SmoothBefore(now); bool wasWalking = _motion == Motion.Walk; // w10move
            _slideCarry = false; // (wmove) a walk's logical moves never carry a steering body
            if (x1 != Mathf.FloorToInt(Cell.x + 0.5f) || y1 != Mathf.FloorToInt(Cell.y + 0.5f)) FreeWalkBegins(x1, y1); // free movement (wfree): where in its destination our own body ends
            var from = new Vector2(x0 + (sx0 - 8) / 16f, y0 + (sy0 - 8) / 16f);
            int speed = Mathf.Max(Speed, 1);
            var info = new WalkInfo { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Sx = sx0, Sy = sy0, MoveStart = moveStartTime, HasStart = hasStartTime, PlannedLocal = now };

            // 1. the server's steps
            var wp = SharedPath; bool found = false;
            if (_gat != null)
            {
                if (easyPath && RoPathSearch.Search(wp, _gat, x0, y0, x1, y1, 1)) { found = true; info.Kind = "easy"; }
                else if (RoPathSearch.Search(wp, _gat, x0, y0, x1, y1, 0)) { found = true; info.Kind = "A*"; }
                else if (!easyPath && RoPathSearch.Search(wp, _gat, x0, y0, x1, y1, 1)) { found = true; info.Kind = "easy (A* failed)"; }
            }
            var cells = new System.Collections.Generic.List<Vector2Int> { new Vector2Int(x0, y0) };
            var stepMs = new System.Collections.Generic.List<int>();
            var stepDir = new System.Collections.Generic.List<int>();
            if (found)
            {
                int cx = x0, cy = y0;
                for (int i = 0; i < wp.Len; i++)
                {
                    int d = wp.Path[i];
                    if (d < 0 || d > 7) break; // DIR_CENTER: the walk ends there (unit.cpp:572-574)
                    cx += RoPathSearch.DirX[d]; cy += RoPathSearch.DirY[d];
                    cells.Add(new Vector2Int(cx, cy));
                    stepMs.Add(RoPathSearch.Diagonal(d) ? speed * RoPathSearch.MoveDiagonalCost / RoPathSearch.MoveCost : speed);
                    stepDir.Add(FromServerDir(d));
                }
            }
            else if (x0 != x1 || y0 != y1)
            {
                info.Kind = "straight (no path)";
                var to = new Vector2(x1, y1);
                cells.Add(new Vector2Int(x1, y1));
                stepMs.Add(Mathf.RoundToInt(Vector2.Distance(from, to) * speed));
                stepDir.Add(DirFromDelta(to - from));
            }
            else info.Kind = "none";
            int total = 0;
            var reach = new int[cells.Count];
            for (int i = 0; i < stepMs.Count; i++) { total += stepMs[i]; reach[i + 1] = total; }
            info.Cells = cells.ToArray(); info.ReachMs = reach; info.TotalMs = total;

            // 2. how long the server's unit has been walking
            int elapsed = 0;
            if (hasStartTime && ServerClock.Valid)
            {
                elapsed = ServerClock.MsSince(moveStartTime);
                // an estimate far beyond the walk means the clock is off (a new map server, a clock step): start now
                // (roBrowserLegacy does the same beyond 4x the walk, Renderer/Entity/EntityWalk.js:89-91 — a reimplementation)
                if (elapsed < 0 || elapsed > Mathf.Max(4 * total, 3000)) elapsed = 0;
                if (elapsed > total) elapsed = total;
            }
            info.ElapsedMs = elapsed;

            // 3. the plan: points, durations in server ms, facings
            var pts = new System.Collections.Generic.List<Vector2>();
            var ptCell = new System.Collections.Generic.List<int>(); // which of the server's cells a point is (-1: none)
            var dur = new System.Collections.Generic.List<float>();
            var dirs = new System.Collections.Generic.List<int>();
            bool fromIsCell = sx0 == 8 && sy0 == 8;
            // the first step may start between cells (a sub-cell other than 8: unit_stop_walking_soon, unit.cpp:1636-1693)
            float firstMs = stepMs.Count > 0
                ? stepMs[0] * Vector2.Distance(from, cells[1]) / Mathf.Max(0.001f, Vector2.Distance(cells[0], cells[1]))
                : 0f;
            Vector2 cur = _placed ? PositionAt(now) : from;
            float gap = Vector2.Distance(cur, from);
            bool continuous = _placed && gap <= 2.5f;
            float factor = 1f, startLocal = now;
            if (continuous)
            {
                pts.Add(cur); ptCell.Add(gap <= 0.02f && fromIsCell ? 0 : -1);
                int bestSeg = -1; float bestT = 0f, bestPerp = 0.45f;
                for (int s = 0; s < stepMs.Count; s++)
                {
                    Vector2 a = s == 0 ? from : (Vector2)cells[s], b = cells[s + 1], ab = b - a;
                    float l2 = ab.sqrMagnitude;
                    if (l2 <= 0f) continue;
                    float st = Vector2.Dot(cur - a, ab) / l2;
                    if (st <= 0.02f || st >= 1f) continue;
                    float sp = Vector2.Distance(a + ab * st, cur);
                    if (sp < bestPerp) { bestPerp = sp; bestT = st; bestSeg = s; }
                }
                int k0 = 0;
                if (bestSeg >= 0)
                {
                    // already along segment bestSeg: go straight on to cells[bestSeg + 1] (no step back to earlier cells)
                    float segMs = bestSeg == 0 ? firstMs : stepMs[bestSeg];
                    pts.Add(cells[bestSeg + 1]); ptCell.Add(bestSeg + 1); dur.Add(segMs * (1f - bestT)); dirs.Add(stepDir[bestSeg]);
                    k0 = bestSeg + 1;
                }
                else if (stepMs.Count > 0 && gap > 0.02f && gap <= 1.35f && Vector2.Dot((Vector2)cells[1] - cur, (Vector2)cells[1] - from) > 0f)
                {
                    // turning onto a new step where cells[1] is ahead of cur: head directly to cells[1] instead of stepping back to from
                    float d1 = Vector2.Distance(cur, cells[1]);
                    pts.Add(cells[1]); ptCell.Add(1); dur.Add(Mathf.Max(0.15f, d1) * speed);
                    dirs.Add(d1 >= 0.25f ? DirFromDelta((Vector2)cells[1] - cur) : stepDir[0]);
                    k0 = 1;
                }
                else if (gap > 0.02f)
                {
                    pts.Add(from); ptCell.Add(fromIsCell ? 0 : -1); dur.Add(gap * speed); dirs.Add(gap >= 0.5f ? DirFromDelta(from - cur) : -1);
                }
                for (int k = k0; k < stepMs.Count; k++) { pts.Add(cells[k + 1]); ptCell.Add(k + 1); dur.Add(k == 0 ? firstMs : stepMs[k]); dirs.Add(stepDir[k]); }
                float natural = 0f; foreach (var d in dur) natural += d;
                float remaining = total - elapsed; // server ms until the server's unit arrives
                if (natural > 1f) factor = Mathf.Clamp(remaining / natural, 0.6f, 1.25f);
                info.Jumped = false;
            }
            else
            {
                // not on the map yet, or far off: be where the server's unit is now
                pts.Add(from); ptCell.Add(fromIsCell ? 0 : -1);
                for (int k = 0; k < stepMs.Count; k++) { pts.Add(cells[k + 1]); ptCell.Add(k + 1); dur.Add(k == 0 ? firstMs : stepMs[k]); dirs.Add(stepDir[k]); }
                startLocal = now - ServerClock.ToLocalSeconds(elapsed);
                info.Jumped = true;
            }
            info.Factor = factor;
            LastWalk = info;

            _segs = Mathf.Min(dur.Count, MaxPoints - 1);
            float at = startLocal;
            for (int i = 0; i < _segs; i++)
            {
                _pts[i] = pts[i];
                _segStart[i] = at;
                _segDur[i] = Mathf.Max(0.0005f, ServerClock.ToLocalSeconds(dur[i] * factor));
                _segDir[i] = dirs[i];
                at += _segDur[i];
            }
            _pts[_segs] = pts[_segs];
            info.PlannedArriveLocal = at;
            info.ReachLocal = new float[cells.Count];
            for (int k = 0; k < cells.Count; k++) info.ReachLocal[k] = -1f;
            for (int i = 0; i <= _segs && i < ptCell.Count; i++)
                if (ptCell[i] >= 0) info.ReachLocal[ptCell[i]] = i == 0 ? (_segs > 0 ? _segStart[0] : now) : _segStart[i - 1] + _segDur[i - 1];
            _segShown = -1; _placed = true;
            if (LogMoves)
                Debug.Log($"[Move] {Id} walk {info.Kind} ({x0},{y0})→({x1},{y1}) sub {sx0},{sy0} steps {stepMs.Count} {total} ms, server started {(hasStartTime && ServerClock.Valid ? elapsed + " ms ago" : "?")}, {(info.Jumped ? "placed on the path" : $"from ({cur.x:F2},{cur.y:F2}) x{1f / factor:F2} speed")}; cells {string.Join(" ", System.Array.ConvertAll(info.Cells, c => c.x + "," + c.y))}");
            if (_segs == 0)
            {
                _motion = Motion.None; Cell = pts[0];
                if (Sprite.ActionGroup == 1 && !DrawnOwnsLook) Sprite.Play(0, true);
                if (info.Jumped) SmoothTeleport(); else SmoothAfter(now, drawnBefore, Cell, false); // w10move
                info.ArriveLocal = now; Place();
                return;
            }
            _motion = Motion.Walk;
            // w10move: a unit placed on the path (it appeared, or was far off) is drawn there at once, as before; one that walks on
            // from where it is keeps its drawn body where it was (Sprites/Unit.Smooth.cs)
            Cell = PositionAt(now);
            if (info.Jumped) SmoothTeleport(); else SmoothAfter(now, drawnBefore, _pts[0], !wasWalking);
            if (!DrawnOwnsLook) Sprite.Play(1 /* walk: PC and monster group 1 */, true, 150f / Mathf.Max(Speed, 20) / factor); // (free movement, wfree: a body the server's walk does not draw keeps its own look)
            Advance(now);
        }

        /// <summary>
        /// A straight move to (x,y) that is not a walk: the facing and the action stay (unless the unit was walking, or
        /// <paramref name="standUp"/>), ms ≤ 0 = at once. Knockbacks (ZC_HIGHJUMP 0x01ff), fast moves (ZC_FASTMOVE 0x08d2) and
        /// stops (ZC_STOPMOVE 0x0088) use it (EntityManager).
        /// Client wave 5 (w5move), for a fast move: <paramref name="holdGroup"/> ≥ 0 shows that action's first frame, held, while
        /// the unit slides, and <paramref name="endIdle"/> makes it stand idle when the slide ends unless something else was played
        /// meanwhile — roBrowserLegacy's onEntityFastMove (Engine/MapEngine/Entity.js:461-484: a Monk's ATTACK / a Gunslinger's
        /// SKILL, frame 0, play false) and fastMoveTo's end (Renderer/Entity/EntityWalk.js:646-701 finishWalk: IDLE unless dead);
        /// a reimplementation, the only source (Sprites/FastMoveLook.cs). A move at once holds nothing and ends idle at once.
        /// </summary>
        public void Slide(int x, int y, float ms, bool faceTarget, bool standUp, int holdGroup = -1, bool endIdle = false, bool carry = true)
        {
            var target = new Vector2(x, y);
            bool wasPlaced = _placed; Vector2 drawnBefore = SmoothBefore(Time.time); // w10move
            _slideCarry = carry; // (wmove) a knockback / fast move carries a steering body along (Unit.Smooth.cs); a stop's short slide (Stop) does not
            Vector2 cur = _placed ? PositionAt(Time.time) : target;
            int g = Sprite.ActionGroup;
            if (!Dead && (standUp || (g == 1 && !DrawnOwnsLook))) Sprite.Play(IsPlayerType ? (int)ActorSprite.PlayerAction.Idle : (int)ActorSprite.MonsterAction.Idle, true);
            if (faceTarget && (target - cur).sqrMagnitude >= 0.25f) Sprite.Direction = DirFromDelta(target - cur);
            _slideEndIdle = false;
            if (LogMoves) Debug.Log($"[Move] {Id} slide ({cur.x:F2},{cur.y:F2})→({x},{y}) {ms:F0} ms{(holdGroup >= 0 ? $", holding group {holdGroup} frame 0" : "")}{(endIdle ? ", idle at the end" : "")}");
            if (ms <= 0f || !_placed || (target - cur).sqrMagnitude < 0.0001f)
            {
                _motion = Motion.None; _placed = true; Cell = target;
                if (!Dead && endIdle) Sprite.Play(IsPlayerType ? (int)ActorSprite.PlayerAction.Idle : (int)ActorSprite.MonsterAction.Idle, true);
                // w10move: a move at once (a snap knockback, a first position) takes the drawn body along; a stop within a hair of
                // the cell lets what is left of its offset fade
                if (ms <= 0f || !wasPlaced) { var keep = _rho; SmoothTeleport(); if (wasPlaced) KeepRest(keep); } else SmoothAfter(Time.time, drawnBefore, target, false); // (free movement, wfree: a snap knockback keeps where in its cell our own body stands)
                Place();
                return;
            }
            _pts[0] = cur; _pts[1] = target; _segs = 1;
            _segStart[0] = Time.time; _segDur[0] = ms / 1000f; _segDir[0] = -1; _segShown = -1;
            _motion = Motion.Slide;
            Cell = cur; SmoothAfter(Time.time, drawnBefore, cur, false); // w10move: the slide carries the drawn body's offset, fading
            if (!Dead && holdGroup >= 0) Sprite.FreezeFrame(holdGroup, 0);
            _slideEndIdle = !Dead && endIdle; _slidePlays = Sprite.PlayCount;
            Advance(Time.time);
        }

        // client wave 5 (w5move): a fast move ends idle (Slide's endIdle) unless the sprite played something else since it began
        private bool _slideEndIdle; private int _slidePlays;

        /// <summary>Where a running slide ends (null when none runs).</summary>
        public Vector2? SlideTarget => _motion == Motion.Slide ? _pts[_segs] : (Vector2?)null;

        /// <summary>
        /// ZC_STOPMOVE 0x0088: the unit is at (x,y) and not walking. rAthena's note on the client: "Visually moves(slides) a
        /// character to x,y … If the char is sitting it will stand up" (rA clif.cpp:2199-2203). A slide already heading there
        /// (0x01ff is followed by 0x0088, clif_blown clif.cpp:2192-2198) goes on untouched; otherwise a short slide (≤ 120 ms,
        /// 80 ms per cell: a Loki choice, no official figure is known) or, closer than 0.05 cell, a stop in place.
        /// Not followed: "If the target cell isn't walkable, the char doesn't move at all" — with a client GAT that differs from
        /// the server's map cache (design notes) that would leave the unit where the server says it is not.
        /// </summary>
        public void Stop(int x, int y, bool snap = false)
        {
            if (Dead) return;
            var target = new Vector2(x, y);
            if (_motion == Motion.Slide && (_pts[_segs] - target).sqrMagnitude < 0.0025f) return;
            Vector2 cur = _placed ? PositionAt(Time.time) : target;
            float d = Vector2.Distance(cur, target);
            bool standUp = (Sprite.ActionGroup == 1 && !DrawnOwnsLook) || (IsPlayerType && Sprite.ActionGroup == (int)ActorSprite.PlayerAction.Sit);
            if (d < 0.05f || !_placed)
            {
                bool wasPlaced = _placed; Vector2 drawnBefore = SmoothBefore(Time.time); // w10move
                if (wasPlaced) FreeStopped(drawnBefore, target); // free movement (wfree): our own body moves only into the stop cell's square
                _slideCarry = false; // (wmove)
                _motion = Motion.None; _placed = true; Cell = target; if (standUp) Sprite.Play(0, true);
                if (wasPlaced) SmoothAfter(Time.time, drawnBefore, target, false); else SmoothTeleport();
                Place(); return;
            }
            FreeStopped(SmoothBefore(Time.time), target); // free movement (wfree): our own body moves only into the stop cell's square
            Slide(x, y, snap ? 0f : Mathf.Clamp(80f * d, 40f, 120f), false, standUp, carry: d >= 1.5f); // (wmove: a stop's correction of under a step does not move a steering body; a pull of 1.5+ cells, EntityManager's "big", does)
        }

        /// <summary>Where the unit is drawn at local time t (Time.time), without moving it.</summary>
        public Vector2 PositionAt(float t)
        {
            if (_motion == Motion.None || _segs == 0) return Cell;
            if (t <= _segStart[0]) return _pts[0];
            for (int i = 0; i < _segs; i++)
            {
                float end = _segStart[i] + _segDur[i];
                if (t < end) return Vector2.Lerp(_pts[i], _pts[i + 1], (t - _segStart[i]) / _segDur[i]);
            }
            return _pts[_segs];
        }

        private void Advance(float t)
        {
            int i = 0;
            while (i < _segs && t >= _segStart[i] + _segDur[i]) i++;
            SmoothKnots(i); // w10move: the corners passed join the drawn body's past
            if (i >= _segs)
            {
                var m = _motion;
                if (m == Motion.Walk) LeadEnds(t); // wave 27 (wfix): Sprites/Unit.Smooth.cs
                _motion = Motion.None; Cell = _pts[_segs];
                _yawMoving = false;
                if (m == Motion.Walk) FreeWalkEnded(); // free movement (wfree): a walk to an exact point stands there
                if (m == Motion.Walk && DrawnOwnsLook) { if (LastWalk != null) LastWalk.ArriveLocal = t; } // (free movement, wfree: the look is the drawn body's)
                else if (m == Motion.Walk)
                {
                    // w10move: in Smooth mode the facing followed the drawn movement; the server's last step direction is taken
                    // only when the body faces more than one direction (45 degrees) away from it
                    int last = _segDir[_segs - 1];
                    if (Smooth && !_segFacing) { if (last >= 0 && Mathf.Abs(Mathf.DeltaAngle((Sprite.Direction & 7) * 45f, last * 45f)) > 45.5f) Sprite.Direction = last; }
                    else if (_segShown != _segs - 1 && _segDir[_segs - 1] >= 0) Sprite.Direction = _segDir[_segs - 1];
                    if (!Dead && Sprite.ActionGroup == 1) Sprite.Play(0, true);
                    if (LastWalk != null) LastWalk.ArriveLocal = t;
                    if (LogMoves) Debug.Log($"[Move] {Id} arrived ({Cell.x:F0},{Cell.y:F0})");
                }
                else if (m == Motion.Slide && _slideEndIdle)
                {
                    // client wave 5 (w5move): rB's finishWalk after a fast move (EntityWalk.js:646-701): IDLE, unless the unit
                    // played something else meanwhile (an attack, a death: Play counts every request, ActorSprite.PlayCount)
                    _slideEndIdle = false;
                    if (!Dead && Sprite.PlayCount == _slidePlays) Sprite.Play(IsPlayerType ? (int)ActorSprite.PlayerAction.Idle : (int)ActorSprite.MonsterAction.Idle, true);
                    if (LogMoves) Debug.Log($"[Move] {Id} fast move ended at ({Cell.x:F0},{Cell.y:F0}): action group {Sprite.ActionGroup}");
                }
                Place();
                return;
            }
            Cell = t <= _segStart[i] ? _pts[i] : Vector2.Lerp(_pts[i], _pts[i + 1], (t - _segStart[i]) / _segDur[i]);
            if (_motion == Motion.Walk && !DrawnOwnsLook && !Dead && Sprite != null && (Sprite.ActionGroup == 0 || (IsPlayerType && Sprite.ActionGroup == (int)ActorSprite.PlayerAction.Standby)))
                Sprite.Play(1, true, 150f / Mathf.Max(Speed, 20));
            if (i != _segShown) { _segShown = i; if (_segDir[i] >= 0 && (!Smooth || _segFacing || _motion != Motion.Walk) && !DrawnOwnsLook) Sprite.Direction = _segDir[i]; } // w10move: Smooth faces by the drawn movement (Unit.Smooth.cs Face); free movement (wfree): not a body the server's walk does not draw
            Place();
        }

        private float _colliderCheck;
        private void Update()
        {
            if (Time.time > _colliderCheck) { _colliderCheck = Time.time + 0.5f; UpdateCollider(); }
            if (_motion == Motion.None) { if (SmoothBusy) Place(); else SmoothIdle(Time.time); return; } // w10move: an offset fading, the stick steering
            Advance(Time.time);
        }

        private void Place()
        {
            // client wave 10 (w10move): the transform follows the DRAWN position (Sprites/Unit.Smooth.cs); Cell stays the logical one.
            // Classic: DrawnCell is Cell and the height is the cell's mean height, exactly as before; Smooth: the ground's height under
            // the body (the cell's corner heights, interpolated), so the first-person view does not step at every cell.
            SmoothStep(Time.time);
            float h = 0f;
            if (_gat != null) h = GroundHeight(DrawnCell);
            transform.localPosition = new Vector3(DrawnCell.x + 0.5f, h, DrawnCell.y + 0.5f);
        }

        /// <summary>Server (rAthena) direction, 0 = north … 4 = south, → client/ACT direction, 0 = south (facing the default camera). Involution.</summary>
        public static int FromServerDir(int serverDir) => (12 - (serverDir & 7)) & 7;
        public static int ToServerDir(int clientDir) => (12 - (clientDir & 7)) & 7;

        /// <summary>RO direction from a movement delta: 0 = south (−y), 2 = west, 4 = north (+y), 6 = east; odd = diagonals.</summary>
        public static int DirFromDelta(Vector2 d)
        {
            int dx = d.x > 0.3f ? 1 : d.x < -0.3f ? -1 : 0;
            int dy = d.y > 0.3f ? 1 : d.y < -0.3f ? -1 : 0;
            if (dx == 0 && dy < 0) return 0;
            if (dx < 0 && dy < 0) return 1;
            if (dx < 0 && dy == 0) return 2;
            if (dx < 0 && dy > 0) return 3;
            if (dx == 0 && dy > 0) return 4;
            if (dx > 0 && dy > 0) return 5;
            if (dx > 0 && dy == 0) return 6;
            if (dx > 0 && dy < 0) return 7;
            return 0;
        }
    }

    /// <summary>Something the VR pointer can target: a unit or a ground item.</summary>
    public sealed class Targetable : MonoBehaviour
    {
        public Unit Unit;
        public Game.GroundItem Item;
        public void SetHover(bool h) { if (Unit != null) Unit.SetHover(h); if (Item != null) Item.SetHover(h); }
        /// <summary>Client wave 14 (w14laser), point 20: every enabled one (the laser fades in near one: VR/DioramaRig.Laser.cs), and its collider.</summary>
        public static readonly System.Collections.Generic.List<Targetable> Live = new System.Collections.Generic.List<Targetable>();
        public Collider Col { get { if (_col == null) _col = GetComponent<Collider>(); return _col; } }
        [System.NonSerialized] private Collider _col;
        private void OnEnable() { Live.Add(this); }
        private void OnDisable() { Live.Remove(this); }
    }
}
