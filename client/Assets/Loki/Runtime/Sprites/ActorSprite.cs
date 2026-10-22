using System.Collections.Generic;
using System.Text;
using Loki.Formats;
using UnityEngine;

namespace Loki.Sprites
{
    /// <summary>
    /// Plays RO sprite animations (ACT/SPR) for one unit: body plus, for players, hair, up to three headgears, weapon (+ slash
    /// trail), shield / left-hand weapon and garment. All visible layers are composited into ONE image per animation frame
    /// (painter's order, back to front) and rendered as a single relief mesh, so the unit is one solid shape.
    /// Local space: x/y in GAT cells (35 px = 1 cell), -z towards the viewer; the root is rotated every LateUpdate to face the
    /// camera around the vertical axis. Position = the unit's feet.
    /// Directions use the client/ACT convention: 0 = facing south (towards the default camera) … 4 = north.
    /// </summary>
    public sealed class ActorSprite : MonoBehaviour
    {
        public const float PixelsPerCell = 35f;

        /// <summary>PC action groups (roBrowserLegacy EntityAction.js:161-173; zrenderer sprite.d PlayerAction). Verified on the
        /// client's body ACTs (초보자/검사/룬나이트_남.act): 104 actions = 13 groups, frames per group
        /// [3,8,3,3,6,5,3,1,1,1,9,8,6]; group 8's frame is the wide lying-down pose (67×55), group 9 repeats the idle frame.</summary>
        public enum PlayerAction { Idle = 0, Walk = 1, Sit = 2, PickUp = 3, Standby = 4, Attack1 = 5, Hurt = 6, Freeze = 7, Dead = 8, Freeze2 = 9, Attack2 = 10, Attack3 = 11, Skill = 12 }
        public enum MonsterAction { Idle = 0, Walk = 1, Attack = 2, Hurt = 3, Dead = 4 }

        /// <summary>Sprite layers of a unit. Body/Head/headgears follow the unit packet fields; Shield also holds a left-hand weapon.</summary>
        public enum Slot { Body = 0, Head = 1, HeadBottom = 2, HeadTop = 3, HeadMid = 4, Weapon = 5, WeaponTrail = 6, Shield = 7, Robe = 8 }
        private const int SlotCount = 9;

        private sealed class Part { public SprFile Spr; public ActFile Act; public Color32[] Palette; public int Id; public int ViewId; }

        private readonly Part[] _parts = new Part[SlotCount];
        private Part _body => _parts[(int)Slot.Body];
        private Shader _shader;
        private GameObject _quad; private MeshFilter _mf; private MeshRenderer _mr; private Material _mat;
        private int _actionGroup; private bool _loop = true; private float _startTime; private float _speedFactor = 1f;
        private string _lastKey;
        private readonly List<SpriteFrameCache.CompositeLayer> _layers = new List<SpriteFrameCache.CompositeLayer>();
        private readonly StringBuilder _kb = new StringBuilder();
        private readonly int[] _ordSlot = new int[SlotCount], _ordPri = new int[SlotCount], _ordSeq = new int[SlotCount];

        // Stable id per (SPR, palette) so identical looks on different units share composite cache entries.
        private static readonly Dictionary<(SprFile, Color32[]), int> _partIds = new Dictionary<(SprFile, Color32[]), int>();
        private static int PartId(SprFile spr, Color32[] pal)
        {
            if (spr == null) return 0;
            if (!_partIds.TryGetValue((spr, pal), out int id)) { id = _partIds.Count + 1; _partIds[(spr, pal)] = id; }
            return id;
        }

        /// <summary>Facing direction (client convention, 0 = south/towards default camera).</summary>
        public int Direction;
        /// <summary>Head turn for idle/sit (0 straight, 1 right, 2 left) — the "doridori" frames.</summary>
        public int HeadDir;
        public bool IsPlayerType;

        /// <summary>
        /// Client wave 3 (w3status): the sprite colour is a product of independent factors, as roBrowserLegacy composes its
        /// effectColor (EntityState.js:34-59: bodyState × healthState × effectState × virtue × flash, component by component), so a
        /// hover highlight no longer overwrites a frozen unit's blue or a hidden unit's alpha. Base is what <see cref="Tint"/>'s
        /// setter writes (ground items, followers copying their owner's colour); Hover = the pointer (Unit.SetHover); Effect = the
        /// e_option look (hide / cloak / chase walk translucency, EntityManager.ApplyOption); Body = opt1, Health = opt2, Virtue = opt3
        /// (UnitState); Flash = free for a hit flash (roBrowserLegacy _flashColor); Stealth = client wave 4 (w4units): Shadow Form's
        /// shade (Sprites/UnitEfstLooks.cs).
        /// </summary>
        public enum TintFactor { Base = 0, Hover, Effect, Body, Health, Virtue, Flash, Stealth }
        private const int TintFactorCount = 8;
        private readonly Color[] _tints = { Color.white, Color.white, Color.white, Color.white, Color.white, Color.white, Color.white, Color.white };
        private Color _tint = Color.white;
        /// <summary>The composed colour the shader multiplies the sprite by. Setting it sets the <see cref="TintFactor.Base"/> factor.</summary>
        public Color Tint { get => _tint; set => SetTint(TintFactor.Base, value); }
        public void SetTint(TintFactor f, Color c)
        {
            _tints[(int)f] = c;
            var t = Color.white;
            for (int i = 0; i < TintFactorCount; i++) t *= _tints[i];
            _tint = t;
        }
        public Color GetTint(TintFactor f) => _tints[(int)f];
        public float Relief = -1f;
        public static float DefaultRelief = 0.6f; // 0 = unlit flat sprite colours, 1 = full N·L shading of the relief
        /// <summary>Client wave 4 (w4units): the drawn size (1 = 35 px per cell; baby classes 0.8: roBrowserLegacy EntityView.js:253
        /// xSize = isBaby ? 4 : 5, Sprites/UnitLooks.cs).</summary>
        public float Scale { get => _scale; set { if (_scale != value) { _scale = value; Invalidate(); } } }
        /// <summary>The body sprite's path as the look chose it (w4units: smoke and diagnostics; CP949 bytes as Latin-1), or null.</summary>
        public string BodyPath;
        private float _scale = 1f;

        public static int CameraDirection { get; set; }
        /// <summary>ACT frame sound events ("poring_attack.wav" …, and "atk", the weapon swing / damage frame) as the body animation reaches them.</summary>
        public event System.Action<string> Sound;
        /// <summary>Size of the current composite image in map cells (width, height).</summary>
        public Vector2 SizeCells { get; private set; } = new Vector2(1f, 2f);
        /// <summary>How far the current relief mesh bulges towards the viewer, in cells (overlays sit in front of it).</summary>
        public float ReliefCells { get; private set; } = 0.25f;
        /// <summary>The composite drawn now (w3status: the opt3 halo and after-images redraw it): mesh, texture and the quad's transform
        /// (null before the first frame, or while nothing is drawn).</summary>
        public Mesh CurrentMesh => _quad != null && _quad.activeSelf ? _mf.sharedMesh : null;
        public Texture CurrentTexture => _quad != null && _quad.activeSelf ? _mat.mainTexture : null;
        public Transform CurrentQuad => _quad != null && _quad.activeSelf ? _quad.transform : null;
        private int _evtAction = -1, _evtFrame = -1;
        // client wave 4 (w4fx): the non-body layers' ACT events (per slot: action index, frame, event name; -1 = layer not drawn) and the
        // shared "last played" of rB's EntitySound (action, frame, name)
        private readonly int[] _pEvAction = new int[SlotCount], _pEvFrame = new int[SlotCount], _pLastAction = { -1, -1, -1, -1, -1, -1, -1, -1, -1 }, _pLastFrame = { -1, -1, -1, -1, -1, -1, -1, -1, -1 };
        private readonly string[] _pEvName = new string[SlotCount];
        private int _sndAction = -1, _sndFrame = -1; private string _sndName;
        /// <summary>Layer events heard (body included) — the smoke counts the garment / weapon ones.</summary>
        public static int LayerEventsPlayed;
        private void PlayEvent(int action, int frame, string e)
        {
            if (string.IsNullOrEmpty(e) || !(e == "atk" || e.EndsWith(".wav", System.StringComparison.OrdinalIgnoreCase))) return;
            if (_sndName == e && _sndAction == action && _sndFrame == frame) return;
            _sndAction = action; _sndFrame = frame; _sndName = e;
            LayerEventsPlayed++;
            Sound?.Invoke(e);
        }
        public bool HasHead => _parts[(int)Slot.Head] != null;
        /// <summary>Which layers are loaded, e.g. "Body Head HeadTop Weapon Shield" (smoke-test log).</summary>
        public string LayerSummary() { var sb = new StringBuilder(); for (int i = 0; i < _parts.Length; i++) if (_parts[i] != null) sb.Append(((Slot)i).ToString()).Append(' '); return sb.ToString().TrimEnd(); }
        public int ActionGroup => _actionGroup;

        private void Awake() { if (_parts[0] == null) _parts[0] = new Part(); }

        public void SetBody(SprFile spr, ActFile act, Color32[] palette = null)
        {
            _parts[0] ??= new Part();
            _body.Spr = spr; _body.Act = act; _body.Palette = palette; _body.Id = PartId(spr, palette); Invalidate();
        }
        public void SetHead(SprFile spr, ActFile act, Color32[] palette = null) => SetPart(Slot.Head, spr, act, palette);

        /// <summary>Sets one layer (null spr/act clears it). viewId = headgear view id, used for the client's per-item layer priority.</summary>
        public void SetPart(Slot slot, SprFile spr, ActFile act, Color32[] palette = null, int viewId = 0)
        {
            if (slot == Slot.Body) { SetBody(spr, act, palette); return; }
            if (spr == null || act == null) { ClearPart(slot); return; }
            var p = _parts[(int)slot] ??= new Part();
            p.Spr = spr; p.Act = act; p.Palette = palette; p.Id = PartId(spr, palette); p.ViewId = viewId; Invalidate();
        }

        public void ClearPart(Slot slot)
        {
            if (slot == Slot.Body) { SetBody(null, null); return; }
            if (_parts[(int)slot] == null) return;
            _parts[(int)slot] = null; Invalidate();
        }

        /// <summary>Removes every layer except the body.</summary>
        public void ClearAttachments() { for (int i = 1; i < SlotCount; i++) ClearPart((Slot)i); }

        /// <summary>Client wave 4b (w4granny): every Play call counts, even one that keeps a looping action running — a 3-D body restarts its
        /// clip on each (roBrowserLegacy GR2ModelRenderer.js syncFromEntity: "the client restarts the clip on every ZC_NOTIFY_ACT").</summary>
        public int PlayCount { get; private set; }

        public void Play(int actionGroup, bool loop, float speedFactor = 1f)
        {
            PlayCount++;
            if (_held) { _req = true; _reqGroup = actionGroup; _reqLoop = loop; _reqSpeed = speedFactor; _reqNext = -1; return; } // w3status: kept for Release
            _nextGroup = -1; _fixedFrame = -1; // w4units: any new action ends a frozen frame
            if (_frameOffset != 0) { _frameOffset = 0; Invalidate(); } // w5looks: and a start frame (rB setAction replaces animation.frame)

            if (_actionGroup == actionGroup && _loop == loop && loop) { _speedFactor = speedFactor; return; }
            _actionGroup = actionGroup; _loop = loop; _speedFactor = speedFactor; _startTime = Time.time; Invalidate();
        }
        public void Play(PlayerAction a, bool loop = true, float speedFactor = 1f) => Play((int)a, loop, speedFactor);
        public void Play(MonsterAction a, bool loop = true, float speedFactor = 1f) => Play((int)a, loop, speedFactor);

        private int _nextGroup = -1; private bool _nextLoop; private float _nextAtMs;
        /// <summary>
        /// Plays a one-shot action and then, once afterMs have elapsed (afterMs ≤ 0: when the action's own frames have run out),
        /// switches to nextGroup. roBrowserLegacy ends every attack this way (Entity.js:846-866: attackMT later → READYFIGHT,
        /// group 4), and only that group and the attack group carry the weapon and shield layers, so without it the shield
        /// never shows and the weapon freezes on the last swing frame. Any later Play() cancels the pending switch.
        /// </summary>
        public void PlayThen(int actionGroup, float afterMs, int nextGroup, bool nextLoop = true)
        {
            if (_held) { _req = true; _reqGroup = actionGroup; _reqLoop = false; _reqSpeed = 1f; _reqNext = nextGroup; _reqNextAt = afterMs; _reqNextLoop = nextLoop; return; } // w3status
            Play(actionGroup, false);
            _nextGroup = nextGroup; _nextLoop = nextLoop; _nextAtMs = afterMs;
        }

        /// <summary>Length of the current action for one direction in ms at the current speed (0 when it has no frames).</summary>
        private float ActionLengthMs(int dir)
        {
            if (_body?.Act == null || _body.Act.Actions.Length == 0) return 0f;
            int ai = _actionGroup * 8 + dir; if (ai >= _body.Act.Actions.Length) ai = Mathf.Min(dir, _body.Act.Actions.Length - 1);
            var a = _body.Act.Actions[ai];
            return a.Frames.Length * Mathf.Max(a.Delay, 1f) * 25f / Mathf.Max(_speedFactor, 0.05f);
        }

        private void RunPending(int dir)
        {
            if (_nextGroup < 0) return;
            float elapsed = (Time.time - _startTime) * 1000f;
            float at = _nextAtMs > 0f ? _nextAtMs : ActionLengthMs(dir);
            if (elapsed < at) return;
            int g = _nextGroup; bool l = _nextLoop;
            Play(g, l);
            // the battle stance is held for a while, then the unit relaxes (the client keeps the stance until something else happens; Loki bounds it)
            if (IsPlayerType && g == (int)PlayerAction.Standby && l) { _nextGroup = (int)PlayerAction.Idle; _nextLoop = true; _nextAtMs = StandbyHoldMs; }
        }
        /// <summary>How long a player keeps the battle stance (group 4, shield visible) after the last attack.</summary>
        public static float StandbyHoldMs = 5000f;

        // ------------------------------------------------------------------ held frame (client wave 3, w3status)
        // Stone and Freeze stop a unit's animation, whatever the unit kind (roBrowserLegacy EntityState.js:237-242 Stone:
        // animation.play = false, "stop at the current frame" EntityRender.js:1008-1014; 261-276 Freeze: the FREEZE2 action,
        // frame 0, play = false). While held, Play / PlayThen requests (a stop packet, a death…) are only remembered: the last one
        // runs when the hold ends, so the frozen look can't be replaced by an idle loop meanwhile.
        private bool _held; private float _heldElapsedMs;
        private bool _req; private int _reqGroup, _reqNext = -1; private bool _reqLoop, _reqNextLoop; private float _reqSpeed = 1f, _reqNextAt;
        public bool Held => _held;
        /// <summary>The ACT action index and frame drawn last (smoke: a held unit's frame does not move).</summary>
        public int LastActionIndex { get; private set; } = -1;
        public int LastFrame { get; private set; } = -1;

        /// <summary>Stops the current action on the frame it shows now (Stone).</summary>
        public void Hold()
        {
            if (_held) return;
            _heldElapsedMs = (Time.time - _startTime) * 1000f; _held = true; _req = false;
        }

        /// <summary>Shows the first frame of an action group and holds it (Freeze: FREEZE2 for players, idle for the others).</summary>
        public void HoldPose(int actionGroup)
        {
            _held = false; Play(actionGroup, false);
            _held = true; _heldElapsedMs = 0f; _req = false;
        }

        /// <summary>
        /// Ends a hold. A Play / PlayThen asked for meanwhile runs now; else, with a fallback group, that group loops (the battle
        /// stance for players after a thaw, roBrowserLegacy EntityState.js:217-222 READYFIGHT, relaxed to idle after
        /// <see cref="StandbyHoldMs"/> like after an attack); else the held action carries on from the held frame (Stone: play = true).
        /// forceFallback: the fallback wins over a remembered request unless that request is the death pose.
        /// </summary>
        public void Release(int fallbackGroup = -1, bool forceFallback = false, int deadGroup = -1)
        {
            if (!_held) return;
            _held = false;
            bool useReq = _req && (!forceFallback || fallbackGroup < 0 || _reqGroup == deadGroup);
            _req = false;
            if (useReq)
            {
                if (_reqNext >= 0) PlayThen(_reqGroup, _reqNextAt, _reqNext, _reqNextLoop); else Play(_reqGroup, _reqLoop, _reqSpeed);
                return;
            }
            if (fallbackGroup >= 0)
            {
                _actionGroup = -1; Play(fallbackGroup, true);
                if (IsPlayerType && fallbackGroup == (int)PlayerAction.Standby) { _nextGroup = (int)PlayerAction.Idle; _nextLoop = true; _nextAtMs = StandbyHoldMs; }
                return;
            }
            _startTime = Time.time - _heldElapsedMs / 1000f; Invalidate();
        }

        private void Invalidate() { _lastKey = null; }

        // ------------------------------------------------------------------ frozen frame (client wave 4, w4units)
        // roBrowserLegacy's setAction({ action, frame, play: false }) (Engine/MapEngine/Entity.js:2086-2148: the Taekwon kick stances,
        // Tumbling): one frame of an action shown still until the unit's next action. Unlike Hold, a later Play replaces it at once.
        private int _fixedFrame = -1;
        public int FixedFrame => _fixedFrame;
        public void FreezeFrame(int actionGroup, int frame)
        {
            if (_held) return; // stone / freeze keep their look
            Play(actionGroup, false);
            _fixedFrame = Mathf.Max(0, frame); Invalidate();
        }

        // ------------------------------------------------------------------ start frame (client wave 5, w5looks)
        // roBrowserLegacy's setAction({ action, frame, repeat: true, play: true }) (Engine/MapEngine/Entity.js:2118-2126, EFST_TRUESIGHT:
        // IDLE from frame 3): calcAnimation adds animation.frame to a playing action's frame index (Renderer/Entity/EntityRender.js:1056-1068
        // repeat: anim = floor(tick / delay) % count + count · headDir + frame, % size) — except a player's body and garment in IDLE / SIT,
        // which show the head turn whatever the frame (:984-994). So it moves only the other layers (hair, headgears, weapon, shield) by
        // that many frames; an idle ACT with 3 frames (one per head turn) looks the same. A later Play clears it, as any setAction does.
        private int _frameOffset;
        public int FrameOffset => _frameOffset;

        /// <summary>A 3-D model draws this unit instead of the sprite (Sprites/GrannyUnit.cs, client wave 4b w4granny): the body is
        /// empty on purpose and the model times the actions itself, so LateUpdate does not end them (client wave 5).</summary>
        public bool ModelBody { get; set; }
        /// <summary>Plays the action group looping from start frame <paramref name="frame"/> (rB setAction … frame, repeat, play).</summary>
        public void PlayFrom(int actionGroup, int frame)
        {
            if (_held) return; // stone / freeze keep their look
            Play(actionGroup, true);
            _frameOffset = Mathf.Max(0, frame); Invalidate();
        }

        // ------------------------------------------------------------------ draw order
        // Priorities are in the units of the client's TB_Layer_Priority (tb_layer_priority.lub: Default_Mid 100, Default_Top 200,
        // Default_Bottom 300, Default_Robe 400, per-headgear Items_List overrides), which roBrowserLegacy adds to bodyZ = 250
        // (EntityRender.js:445-517). The other layers are rb's zIndex minus that 250 (research/appearance_layers.md §4.4):
        //   shield while facing away (d 2..5): zIndex -1 → -251;   garment when not facing away (d 0,1,2,6,7): 1 → -249, sitting -100 → -350;
        //   hair: bodyZ+50 → 50;   weapon + trail: bodyZ+250 → 250;   shield otherwise: bodyZ+300 → 300;   garment facing away: Default_Robe, sitting 550.
        // Body: rb draws it at zIndex 150 (= -100 here). The client data needs the body ABOVE -100: Items_List gives back-mounted
        // items -100/-200/-300 exactly for the directions where they must hide behind the body (e.g. Charcoal_Stove 1602:
        // -100 facing the camera, +100 for d 3..5; C_God_Of_Sun_Poring 2669: -100 for d 0,1,2,6,7). No rb layer lies between
        // -100 and 50, so body = 0 keeps every rb order and makes those overrides work. Riding/tall-robe corrections of rb are
        // not reproduced (Loki has no riding state). Ties keep rb's draw sequence (later draw = nearer, SpriteRenderer.js:71).
        private const int PriShieldBehind = -251, PriRobeFront = -249, PriRobeFrontSit = -350, PriBody = 0, PriHead = 50,
            PriWeapon = 250, PriShieldFront = 300, PriRobeBackSit = 550;

        private int Priority(int slot, int d, out int seq)
        {
            bool behind = d >= 2 && d <= 5, back = d >= 3 && d <= 5, sit = _actionGroup == (int)PlayerAction.Sit;
            switch ((Slot)slot)
            {
                case Slot.Shield: seq = behind ? 0 : 10; return behind ? PriShieldBehind : PriShieldFront;
                case Slot.Robe:
                    seq = back ? 7 : 1;
                    if (back) return sit ? PriRobeBackSit : AppearanceDb.DefaultRobe;
                    return sit ? PriRobeFrontSit : PriRobeFront;
                case Slot.Body: seq = 2; return PriBody;
                case Slot.Head: seq = 3; return PriHead;
                case Slot.HeadMid: seq = 4; return AppearanceDb.LayerPriority(d, AppearanceDb.LayerMid, _parts[slot].ViewId);
                case Slot.HeadTop: seq = 5; return AppearanceDb.LayerPriority(d, AppearanceDb.LayerTop, _parts[slot].ViewId);
                case Slot.HeadBottom: seq = 6; return AppearanceDb.LayerPriority(d, AppearanceDb.LayerBottom, _parts[slot].ViewId);
                case Slot.Weapon: seq = 8; return PriWeapon;
                case Slot.WeaponTrail: seq = 9; return PriWeapon;
                default: seq = 11; return 0;
            }
        }

        private void LateUpdate()
        {
            if (_body == null || _body.Act == null || _body.Spr == null || _body.Act.Actions.Length == 0)
            {
                if (_quad != null) _quad.SetActive(false);
                // client wave 5 (w5ai): a unit with no drawable body (a sprite missing from the GRF) still leaves its one-shot action — else
                // its attack group stayed forever and the AI read it as MOTION_ATTACK (V_MOTION: a mercenary that never followed again).
                // Not a unit drawn in 3-D (Sprites/GrannyUnit.cs hides the stand-in's body): its model reads the action and times it
                // itself (actorAction.js), and a length of 0 here would end a hit's damage action before the model saw it.
                if (!_held && _fixedFrame < 0 && !ModelBody) RunPending(Direction & 7);
                return;
            }
            if (_shader == null) _shader = Shader.Find("Loki/Sprite");

            var cam = Camera.main;
            if (cam != null)
            {
                var to = transform.position - cam.transform.position; to.y = 0;
                if (to.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
            }

            int dir = (Direction + CameraDirection) & 7;
            if (!_held && _fixedFrame < 0) RunPending(dir);
            float elapsedMs = _held ? _heldElapsedMs // w3status: a held unit keeps its frame
                : _fixedFrame >= 0 ? (_fixedFrame + 0.5f) * CurrentDelay * 25f / Mathf.Max(_speedFactor, 0.05f) // w4units: a frozen frame
                : (Time.time - _startTime) * 1000f;
            if (!BuildFrame(dir, elapsedMs, out int actionIndex, out int frame, out var bodyFrame)) { if (_quad != null) _quad.SetActive(false); return; }
            LastActionIndex = actionIndex; LastFrame = frame;
            if (actionIndex != _evtAction || frame != _evtFrame)
            {
                // a new action or a loop of this one lets the same sound play again (rB EntitySound.js:45-66 play / :81-97 freeOnAnimationEnd)
                if (actionIndex != _evtAction || frame < _evtFrame) _sndName = null;
                _evtAction = actionIndex; _evtFrame = frame;
                var ev = _body.Act.Events;
                if (bodyFrame.EventId >= 0 && ev != null && bodyFrame.EventId < ev.Length) PlayEvent(actionIndex, frame, ev[bodyFrame.EventId]); // "atk": client wave 3, Audio/WeaponSounds.ActEvent
            }
            // client wave 4 (w4fx; w3combat's leftover): every layer's ACT events, as rB plays each rendered layer's (EntityRender.js:803-806),
            // one shared "last played" so a garment copying the body's event on the same frame is heard once (EntitySound.js:45-66)
            for (int s = 1; s < SlotCount; s++)
            {
                if (_pEvFrame[s] < 0) { _pLastFrame[s] = -1; continue; }
                if (_pEvAction[s] == _pLastAction[s] && _pEvFrame[s] == _pLastFrame[s]) continue;
                _pLastAction[s] = _pEvAction[s]; _pLastFrame[s] = _pEvFrame[s];
                if (_pEvName[s] != null) PlayEvent(actionIndex, _pEvFrame[s], _pEvName[s]);
            }

            string key = _kb.ToString();
            if (_mat != null && _mat.GetColor("_Color") != Tint) _mat.SetColor("_Color", Tint);
            if (key == _lastKey) return;
            _lastKey = key;

            var gpu = SpriteFrameCache.GetComposite(key, _layers, out var center);
            if (gpu == null) { if (_quad != null) _quad.SetActive(false); return; }
            EnsureQuad();
            _quad.SetActive(true);
            float sc = _scale / PixelsPerCell; // w4units: babies 0.8
            SizeCells = new Vector2(gpu.Width * sc, gpu.Height * sc);
            ReliefCells = gpu.DepthPx * sc;
            _mf.sharedMesh = gpu.Mesh;
            _mat.mainTexture = gpu.Texture;
            _mat.SetFloat("_Relief", Relief >= 0 ? Relief : DefaultRelief);
            _mat.SetColor("_Color", Tint);
            float localY = Mathf.Max(-center.y * sc, (gpu.Height * sc) * 0.5f);
            _quad.transform.localPosition = new Vector3(center.x * sc, localY, 0f);
            _quad.transform.localScale = new Vector3(sc, sc, sc);
        }

        /// <summary>
        /// Fills the composite layer list and cache key for one drawn direction (0..7, camera applied) at elapsedMs into the
        /// current action. No Unity engine calls, so it can be exercised outside the player. False when the body has no frame.
        /// </summary>
        internal bool BuildFrame(int dir, float elapsedMs, out int actionIndex, out int frame, out ActFile.Frame bodyFrame)
        {
            actionIndex = _actionGroup * 8 + dir;
            if (actionIndex >= _body.Act.Actions.Length) actionIndex = Mathf.Min(dir, _body.Act.Actions.Length - 1);
            var action = _body.Act.Actions[actionIndex];
            if (action.Frames.Length == 0) { frame = 0; bodyFrame = null; return false; }
            int frameCount = action.Frames.Length;
            bool idleLike = IsPlayerType && (_actionGroup == (int)PlayerAction.Idle || _actionGroup == (int)PlayerAction.Sit);
            int headDir = Mathf.Max(HeadDir, 0);
            float speed = Mathf.Max(_speedFactor, 0.05f);
            float bodyPhase = elapsedMs / (Mathf.Max(action.Delay, 1f) * 25f / speed);
            if (idleLike) frame = Mathf.Min(headDir, frameCount - 1); // PC idle/sit frames are head turns, not an animation
            else
            {
                frame = (int)bodyPhase;
                frame = _loop ? frame % frameCount : Mathf.Min(frame, frameCount - 1);
            }

            bodyFrame = action.Frames[frame];

            // Draw order for this direction: sort the present layers by (priority, rb draw sequence), back to front.
            int n = 0;
            for (int s = 0; s < SlotCount; s++)
            {
                var p = _parts[s];
                if (p == null || p.Spr == null || p.Act == null || p.Act.Actions.Length == 0) continue;
                int pri = Priority(s, dir, out int seq);
                int k = n++;
                while (k > 0 && (_ordPri[k - 1] > pri || (_ordPri[k - 1] == pri && _ordSeq[k - 1] > seq)))
                {
                    _ordSlot[k] = _ordSlot[k - 1]; _ordPri[k] = _ordPri[k - 1]; _ordSeq[k] = _ordSeq[k - 1]; k--;
                }
                _ordSlot[k] = s; _ordPri[k] = pri; _ordSeq[k] = seq;
            }

            _kb.Length = 0;
            _layers.Clear();
            for (int i = 0; i < SlotCount; i++) _pEvFrame[i] = -1;
            for (int i = 0; i < n; i++)
            {
                int s = _ordSlot[i];
                if (s == (int)Slot.Body) { AddLayers(_body, bodyFrame, Vector2Int.zero); continue; }
                var p = _parts[s];
                // roBrowserLegacy EntityRender.js:791-796: every layer uses its own ACT, same action*8+direction, wrapped
                var pAction = p.Act.Actions[(_actionGroup * 8 + dir) % p.Act.Actions.Length];
                int pf = PartFrame((Slot)s, pAction, idleLike, headDir, elapsedMs, speed, bodyPhase);
                if (pf < 0) continue;
                var pFrame = pAction.Frames[pf];
                // this layer's ACT event on this frame (client wave 4, w4fx: played in LateUpdate)
                _pEvAction[s] = (_actionGroup * 8 + dir) % p.Act.Actions.Length; _pEvFrame[s] = pf;
                _pEvName[s] = pFrame.EventId >= 0 && p.Act.Events != null && pFrame.EventId < p.Act.Events.Length ? p.Act.Events[pFrame.EventId] : null;
                var off = Vector2Int.zero;
                // §4.2: hair and headgears are placed by body anchor − own anchor (EntityRender.js:811-814); weapon, trail,
                // shield and garment are drawn at the origin (is_main = true)
                bool attached = s == (int)Slot.Head || s == (int)Slot.HeadBottom || s == (int)Slot.HeadTop || s == (int)Slot.HeadMid;
                if (attached && bodyFrame.Anchors.Length > 0 && pFrame.Anchors.Length > 0)
                    off = new Vector2Int(bodyFrame.Anchors[0].X - pFrame.Anchors[0].X, bodyFrame.Anchors[0].Y - pFrame.Anchors[0].Y);
                AddLayers(p, pFrame, off);
            }
            return true;
        }

        /// <summary>
        /// Frame of a non-body layer, after roBrowserLegacy calcAnimation (EntityRender.js:954-1088):
        /// idle/sit — garment uses headDir like the body; hair and headgears ("head" type) use frames/3 per head turn and animate
        /// within that third; weapon, trail and shield cycle their own frames. Walk — every part shares the body's
        /// distance/time phase modulo its own frame count. Other actions — each part runs on its own ACT delay, clamped at its
        /// last frame when the action does not loop.
        /// </summary>
        private int PartFrame(Slot slot, ActFile.Action a, bool idleLike, int headDir, float elapsedMs, float speed, float bodyPhase)
        {
            int count = a.Frames.Length;
            if (count == 0) return -1;
            int last = count - 1;
            float ownPhase = elapsedMs / (Mathf.Max(a.Delay, 1f) * 25f / speed);
            if (idleLike)
            {
                if (slot == Slot.Robe) return Mathf.Min(headDir, last);
                if (slot == Slot.Head || slot == Slot.HeadBottom || slot == Slot.HeadTop || slot == Slot.HeadMid)
                {
                    int per = Mathf.Max(count / 3, 1);
                    int hd = Mathf.Min(headDir, last);
                    return ((int)ownPhase % per + per * hd + _frameOffset) % count; // w5looks: + the start frame (rB EntityRender.js:1056-1068)
                }
                return ((int)ownPhase % count + _frameOffset) % count;
            }
            if (_actionGroup == (int)PlayerAction.Walk) return (int)bodyPhase % count;
            int f = (int)ownPhase;
            return _loop ? f % count : Mathf.Min(f, last);
        }

        /// <summary>
        /// Client wave 9 (h9feel): builds and caches ONE composite of this unit's layers (action group, drawn direction, frame index) without
        /// drawing it, so that a sprite's frames can be made ahead of their first use (the Horde arena pre-builds the run character's job
        /// sprites during the lobby countdown: Horde/Client/HordeSprites.cs). Idle-like groups read the frame as the head turn, as
        /// <see cref="BuildFrame"/> does. The unit's own action, head turn and speed are left as they were. False: nothing to build.
        /// </summary>
        internal bool WarmFrame(int actionGroup, int dir, int frame)
        {
            if (_body == null || _body.Act == null || _body.Spr == null || _body.Act.Actions.Length == 0) return false;
            int g0 = _actionGroup; bool l0 = _loop; int hd0 = HeadDir; float sp0 = _speedFactor; int fo0 = _frameOffset;
            _actionGroup = actionGroup; _loop = true; _speedFactor = 1f; HeadDir = frame; _frameOffset = 0;
            int ai = actionGroup * 8 + (dir & 7); if (ai >= _body.Act.Actions.Length) ai = Mathf.Min(dir & 7, _body.Act.Actions.Length - 1);
            var a = _body.Act.Actions[ai];
            float ms = (frame + 0.5f) * Mathf.Max(a.Delay, 1f) * 25f;
            bool ok = a.Frames.Length > 0 && BuildFrame(dir & 7, ms, out _, out _, out _);
            if (ok && _layers.Count > 0) SpriteFrameCache.GetComposite(_kb.ToString(), _layers, out _);
            _actionGroup = g0; _loop = l0; HeadDir = hd0; _speedFactor = sp0; _frameOffset = fo0; _lastKey = null;
            return ok;
        }

        /// <summary>Client wave 15 (w15hfx), point 18 of the design's wave 14 list ("all of the different preview images are headless"): the
        /// composite of one frame with EVERY loaded layer (body, hair with its colour, headgears ...), as <see cref="WarmFrame"/> builds it,
        /// returned for a picture (Horde's job cards). The unit's own action is left as it was. Null when nothing can be drawn.</summary>
        internal SpriteFrameCache.FrameGpu CompositeFrame(int actionGroup, int dir, int frame)
        {
            if (_body == null || _body.Act == null || _body.Spr == null || _body.Act.Actions.Length == 0) return null;
            int g0 = _actionGroup; bool l0 = _loop; int hd0 = HeadDir; float sp0 = _speedFactor; int fo0 = _frameOffset;
            _actionGroup = actionGroup; _loop = true; _speedFactor = 1f; HeadDir = frame; _frameOffset = 0;
            int ai = actionGroup * 8 + (dir & 7); if (ai >= _body.Act.Actions.Length) ai = Mathf.Min(dir & 7, _body.Act.Actions.Length - 1);
            var a = _body.Act.Actions[ai];
            float ms = (frame + 0.5f) * Mathf.Max(a.Delay, 1f) * 25f;
            SpriteFrameCache.FrameGpu gpu = null;
            if (a.Frames.Length > 0 && BuildFrame(dir & 7, ms, out _, out _, out _) && _layers.Count > 0) gpu = SpriteFrameCache.GetComposite(_kb.ToString(), _layers, out _);
            _actionGroup = g0; _loop = l0; HeadDir = hd0; _speedFactor = sp0; _frameOffset = fo0; _lastKey = null;
            return gpu;
        }

        /// <summary>Client wave 15 (w15hfx), point 20: how long the body's action group lasts once in a direction at speed 1 (ms; 0 when none).</summary>
        internal float GroupLengthMs(int actionGroup, int dir)
        {
            if (_body == null || _body.Act == null || _body.Act.Actions.Length == 0) return 0f;
            int ai = actionGroup * 8 + (dir & 7); if (ai >= _body.Act.Actions.Length) ai = Mathf.Min(dir & 7, _body.Act.Actions.Length - 1);
            var a = _body.Act.Actions[ai];
            return a.Frames.Length * Mathf.Max(a.Delay, 1f) * 25f;
        }

        /// <summary>Client wave 9 (h9feel): how many frames the body's action group has in a direction (0 when none).</summary>
        internal int WarmFrameCount(int actionGroup, int dir)
        {
            if (_body == null || _body.Act == null || _body.Act.Actions.Length == 0) return 0;
            int ai = actionGroup * 8 + (dir & 7); if (ai >= _body.Act.Actions.Length) ai = Mathf.Min(dir & 7, _body.Act.Actions.Length - 1);
            bool idleLike = IsPlayerType && (actionGroup == (int)PlayerAction.Idle || actionGroup == (int)PlayerAction.Sit);
            return idleLike ? Mathf.Min(3, _body.Act.Actions[ai].Frames.Length) : _body.Act.Actions[ai].Frames.Length;
        }

        private void AddLayers(Part part, ActFile.Frame frame, Vector2Int extraOffsetPx)
        {
            var spr = part.Spr;
            foreach (var layer in frame.Layers)
            {
                if (layer.SpriteNumber < 0) continue;
                int available = layer.SpriteType == 1 ? spr.RgbaFrames.Length : spr.IndexedFrames.Length;
                if (layer.SpriteNumber >= available) continue;
                var col = layer.Color;
                if (col.a == 0 || layer.Scale.x == 0f || layer.Scale.y == 0f) continue; // invisible placeholder layers (weapon ACTs use scale 0 / alpha 0)
                var gpu = SpriteFrameCache.Get(spr, layer.SpriteNumber, layer.SpriteType, part.Palette);
                if (gpu.Width == 0 || gpu.Height == 0) continue;
                int x = layer.X + extraOffsetPx.x, y = layer.Y + extraOffsetPx.y;
                _layers.Add(new SpriteFrameCache.CompositeLayer { Frame = gpu, X = x, Y = y, Mirror = layer.Mirror, Color = col, ScaleX = layer.Scale.x, ScaleY = layer.Scale.y, Angle = layer.Angle });
                _kb.Append(part.Id).Append(':').Append(layer.SpriteNumber).Append(layer.SpriteType == 1 ? 'r' : 'i').Append(layer.Mirror ? 'm' : 'n')
                   .Append('@').Append(x).Append(',').Append(y)
                   .Append('#').Append(((uint)col.r << 24) | ((uint)col.g << 16) | ((uint)col.b << 8) | col.a);
                if (layer.Scale.x != 1f || layer.Scale.y != 1f || layer.Angle % 360 != 0)
                    _kb.Append('s').Append(Mathf.RoundToInt(layer.Scale.x * 1000f)).Append('/').Append(Mathf.RoundToInt(layer.Scale.y * 1000f)).Append('r').Append(layer.Angle);
                _kb.Append('|');
            }
        }

        private void EnsureQuad()
        {
            if (_quad != null) return;
            _quad = new GameObject("relief");
            _quad.transform.SetParent(transform, false);
            _mf = _quad.AddComponent<MeshFilter>();
            _mr = _quad.AddComponent<MeshRenderer>();
            _mat = new Material(_shader);
            _mr.sharedMaterial = _mat;
            _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            _mr.receiveShadows = true;
        }

        /// <summary>Client wave 3 (w3combat): the ACT delay (25 ms units, at least 1) of the body action now playing in the drawn direction,
        /// and a new speed factor for that action — Game/HitSchedule.cs PaceAttack paces an attack swing with them.</summary>
        public float CurrentDelay
        {
            get
            {
                if (_body?.Act == null || _body.Act.Actions.Length == 0) return 4f;
                int dir = (Direction + CameraDirection) & 7, ai = _actionGroup * 8 + dir;
                if (ai >= _body.Act.Actions.Length) ai = Mathf.Min(dir, _body.Act.Actions.Length - 1);
                return Mathf.Max(_body.Act.Actions[ai].Delay, 1f);
            }
        }
        public void SetSpeed(float speedFactor) { _speedFactor = Mathf.Max(speedFactor, 0.05f); Invalidate(); }

        /// <summary>Quantised camera yaw relative to the map, so sprites show the side the viewer sees.
        /// Client dir increases clockwise (0 S, 1 SW, 2 W, 3 NW, 4 N …) and Unity yaw increases clockwise too; turning the
        /// camera by +yaw is the unit turning by −yaw relative to it, so action = (dir − round(yaw/45)) & 7.</summary>
        public static int ComputeCameraDirection(Transform mapRoot, Camera cam)
        {
            if (cam == null) return 0;
            var f = mapRoot != null ? mapRoot.InverseTransformDirection(cam.transform.forward) : cam.transform.forward;
            float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            return (8 - Mathf.RoundToInt(yaw / 45f)) & 7;
        }
    }
}
