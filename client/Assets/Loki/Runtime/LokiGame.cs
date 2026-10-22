using System.Collections.Generic;
using System.Linq;
using Loki.Audio;
using Loki.Game;
using Loki.Net;
using Loki.Sprites;
using Loki.UI;
using Loki.World;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Gameplay glue: auto-login with the configured account, pick/create a character, load its map into the diorama,
    /// route map-server packets to the game modules, and turn pointer clicks into requests:
    ///   monster → attack (continuous), NPC → talk, portal → walk into it, ground item → walk over and pick up, ground → walk.
    /// </summary>
    public sealed partial class LokiGame : MonoBehaviour
    {
        public RoSession Session { get; private set; }
        public EntityManager Entities { get; private set; }
        public PlayerState Player { get; } = new PlayerState();
        public Inventory Inventory { get; } = new Inventory();
        public QuestLog Quests { get; } = new QuestLog();
        public NpcDialog Dialog { get; private set; }
        public GroundItems Ground { get; private set; }
        public LokiHud Hud { get; private set; }
        public LokiAudio Audio { get; private set; }
        public Progression Prog { get; } = new Progression();
        public CombatFx Combat { get; private set; }
        public Targeting Target { get; private set; }
        public VR.ViewController View { get; private set; }
        public RoomMask Room { get; private set; }
        public SkyController Sky { get; private set; }
        public Cutaway Cutaway { get; private set; }
        public Minimap MiniMap { get; private set; }
        public Shops Shops { get; private set; }
        public Storage Storage { get; private set; }
        public Menus Menus { get; private set; }
        public Emotes Emotes { get; private set; }
        public Hotkeys Hotkeys { get; private set; }
        public Navigation Nav { get; private set; }
        public BuffBar Buffs { get; private set; }
        public SocialHub Social { get; private set; }
        public Party Party { get; private set; }
        public Trade Trade { get; private set; }
        public Loki.Effects.EffectManager Effects { get; private set; }
        public Refinery Refinery { get; private set; }
        public Crafting Crafting { get; private set; }
        public Guild Guild { get; private set; }
        public Companions Companions { get; private set; }
        public Mail Mail { get; private set; }
        public Achievements Achievements { get; private set; }
        public CheckIn CheckIn { get; private set; }
        public Bank Bank { get; private set; }
        public CashShop CashShop { get; private set; }
        public EquipSwitch Swap { get; private set; }
        /// <summary>"Hide my costumes" (CZ_CONFIG type 5). The server keeps it per character but doesn't announce it at login in this build
        /// (clif.cpp:11038-11040 needs PACKETVER ≥ 20230419), so Loki remembers what it last sent or heard.</summary>
        public bool CostumesHidden { get; private set; }
        public ComfortWindow ComfortUi { get; private set; }
        public Tutorial Tutorial { get; private set; }
        public VR.ComfortVignette Vignette { get; private set; }
        /// <summary>Client wave 19 (w19world), point 48: the Follow view's character edge glow.</summary>
        public VR.EdgeGlow EdgeGlow { get; private set; }
        public QuickBar Quick { get; private set; }
        public Cart Cart { get; private set; }
        public Market Market { get; private set; }
        private Progression.SkillEntry _armed; private int _armedLevel = 1; private float _lastItemUse = -10f;

        private LokiBootstrap _boot;
        private readonly HashSet<ushort> _loggedIds = new HashSet<ushort>();
        private string _user = "", _pass = ""; // the auto-login account (loki.json / -lokiUser): stays empty in a release build (w4release)

        // pending "walk there first, then act" requests
        private uint _pendingPickup, _pendingNpc, _pendingCapture; private float _pendingUntil;
        // current attack target: when the server answers ZC_ATTACK_FAILURE_FOR_DISTANCE (0x0139) the client must walk
        // towards it and repeat the request; rAthena then attacks as soon as a step brings it into range (unit_attack stepaction).
        private uint _attackTarget; private float _nextChase; private int _markerCount;

        public void Begin(LokiBootstrap boot)
        {
            _boot = boot;
            var cfg = boot.Config;
            if (Arg("-lokiMute", "0") == "1") { VR.Comfort.Muted = true; AudioListener.volume = VR.Comfort.MutedVolume; Debug.Log($"[Game] muted (-lokiMute 1): listener volume {AudioListener.volume}"); } // any build: a silent instance (design note 2026-10-05)
            W4ReleaseLogBuild(cfg); // client wave 4b (w4release): which kind of build this is, and what a release build ignores (LokiGame.W4Release.cs)
            ClientText.Portuguese = Lang.Portuguese; // client wave 4b (w4lang): the language setting picks the GRF's column (loki.json "textLanguage" is no longer read)

            Entities = gameObject.AddComponent<EntityManager>();
            Entities.Init(boot.Assets, boot.Map);
            Ground = new GroundItems(boot.Assets, boot.Map);
            Audio = gameObject.AddComponent<LokiAudio>();
            Audio.Init(boot.Assets.Fs, cfg.clientRoot); Audio.SetOverrides(cfg.bgmOverrides);
            Audio.ListenerCell = () => Entities.Self != null ? Entities.Self.Cell : (Vector2?)null;
            Audio.MapRoot = boot.Map.transform;
            Audio.MusicQuiet = () => W23MusicQuiet; // client wave 23 (w23audio), the design's answer 36: the music quiet while the tutorial runs (LokiGame.W23Audio.cs)
            Combat = new CombatFx(Entities, boot.Map);
            Effects = gameObject.AddComponent<Loki.Effects.EffectManager>();
            Effects.Init(boot.Assets, boot.Map, Entities, Audio, () => boot.Rig != null ? boot.Rig.Head : Camera.main);
            Combat.Effects = Effects;
            // Comfort "Skill effects" density; your companions' effects count as yours
            VR.Comfort.Load(); Effects.Density = VR.Comfort.EffectsLevel; VR.Comfort.Changed += () => Effects.Density = VR.Comfort.EffectsLevel;
            Effects.IsMine = id => Companions != null && Companions.Owns(id);
            // normal attacks: EF_HIT1 on a monster that took damage lands with the first hit (Game/HitSchedule.cs, client wave 3); Sight / Ruwach auras follow the option bits (Entity.js:2439-2453)
            Entities.OptionChanged += (u, old, now) =>
            {
                const int Sight = 0x1, Ruwach = 0x2000;
                if ((now & Sight) != 0 && (old & Sight) == 0) Effects.Spam("22", u.Id, null, Time.time, -1f, 0, persistent: true); else if ((now & Sight) == 0 && (old & Sight) != 0) Effects.Remove(u.Id, 22);
                if ((now & Ruwach) != 0 && (old & Ruwach) == 0) Effects.Spam("33", u.Id, null, Time.time, -1f, 0, persistent: true); else if ((now & Ruwach) == 0 && (old & Ruwach) != 0) Effects.Remove(u.Id, 33);
            };
            Room = gameObject.AddComponent<RoomMask>(); Room.Init(boot.Assets.Fs);
            VR.DioramaRig.HiddenWorld = (c, p) => Room != null && Room.Active && _boot.Map.Current != null && c != null && c.gameObject == _boot.Map.Current.Ground &&
                !Room.Visible(new Vector2(_boot.Map.transform.InverseTransformPoint(p).x, _boot.Map.transform.InverseTransformPoint(p).z)); // client wave 20 (w20rooms): the laser passes through another room's (undrawn) ground
            Sky = gameObject.AddComponent<SkyController>(); Sky.Init(boot.Assets.Fs, boot.Map.transform);

            // Auto-login (dev / smoke: loki.json username + password, -lokiLogin 0) or the VR login screen (no credentials, or -lokiLogin 1).
            // A release build always shows the login screen: _user stays empty and the auto-login code is not compiled (w4release).
            if (string.IsNullOrEmpty(_user) && boot.Rig == null) { Debug.LogWarning("[Game] no username in loki.json and no VR rig — offline diorama only"); return; }
            // vSync is off (LokiSetup) so a desktop / smoke run would render uncapped and starve a second client on the same PC;
            // XR runtimes pace frames themselves and ignore targetFrameRate
            Application.targetFrameRate = 60;
            Session = new RoSession(cfg.loginHost, cfg.loginPort, _user, _pass);
            Session.HashedLogin = cfg.hashedLogin;
            Dialog = new NpcDialog(Session, id => Entities.Get(id)?.Name ?? "");
            Session.OnEnteredMap += OnEnteredMap;
            Session.OnMapPacket += OnMapPacket;
            Session.OnFailed += (s, why) =>
            {
                Debug.LogError("[Game] session failed: " + why); Hud?.Chat.Add(Lang.F("Connection failed: {0}", why), "ff7a6a");
                if (_wasInGame) ConnectionLost(why);
                else if (s.OnlineLink) Stdb?.MapEntryRefused(why); // wave H (haccount): Loki Online refused the map entry: back to the character list with the reason
            };
            Session.OnLeaveAccepted += (s, how) => StartCoroutine(LeaveNow(how));
            Session.OnLeaveRefused += (s, how) => OnLeaveRefused(how);
            // Loki Online: one SpacetimeDB session for the whole run (it survives this scene's reloads); in the world it carries
            // the map protocol for Session (RoSession.EnterWorldOnline)
            Stdb = Net.Stdb.StdbSession.GetShared();
            Stdb.DetachHandlers();
            Stdb.WorldReady += EnterWorldOnline;
            Session.OnEnteredMap += s => { if (s.OnlineLink) Stdb.RaiseEnteredMap(); };
            // after "Leave the game": character select resumes at the char server (or on the SpacetimeDB character list);
            // log out shows the login screen (no auto-login)
            _resumeTicket = RoSession.PendingCharSelect; RoSession.PendingCharSelect = null;
            bool loggedOut = _showLoginOnBoot; _showLoginOnBoot = false;
            if (_resumeTicket != null || loggedOut) _autoLogin = false;
            if (_resumeTicket != null && !_resumeTicket.Online) Session.ResumeCharSelect(_resumeTicket); // a release build has no auto-login (w4release)

            if (boot.Rig != null)
            {
                boot.Rig.FreeTrigger += OnFreeTrigger;
                boot.Rig.TargetClicked += OnTargetClicked;
                boot.Rig.Recentred += () => { if (View == null || View.Mode == VR.ViewController.ViewMode.Table) FocusSelf(); else if (View.Mode == VR.ViewController.ViewMode.Follow) View.PlaceAnchor(); };
                View = gameObject.AddComponent<VR.ViewController>(); View.Init(boot, boot.Rig, () => Entities.Self);
                boot.Rig.ViewCycleRequested += () => View.Cycle(); View.Chosen += W19ViewChosen; // (client wave 19, w19view41, point 55: a chosen view is kept)
                Cutaway = gameObject.AddComponent<Cutaway>(); Cutaway.Init(boot.Map.transform, () => Entities.Self, () => View.Mode != VR.ViewController.ViewMode.FirstPerson, () => Room != null && Room.Active);
                View.Changed += m => { boot.Rig.PanEnabled = m == VR.ViewController.ViewMode.Table; boot.Rig.ScaleEnabled = m != VR.ViewController.ViewMode.FirstPerson; Hud?.Chat.Add(m == VR.ViewController.ViewMode.FirstPerson ? Lang.T("Camera: first person") : m == VR.ViewController.ViewMode.Follow ? Lang.T("Camera: follow") : m == VR.ViewController.ViewMode.Table ? Lang.T("Camera: table") : Lang.F("Camera: {0}", m.ToString().ToLowerInvariant()), "9ad1ff"); };
                MiniMap = Minimap.Create(boot.Rig.transform, boot.Rig.LeftHand, boot.Rig.Head, () => Entities.Units.Values, () => Ground.Items.Values, c => Room.Visible(c));
                Target = gameObject.AddComponent<Targeting>();
                Target.Init(boot.Rig, Entities, Ground, boot.Map, Room, () => Session.State == RoSession.Stage.InGame && !Player.Dead && !Dialog.IsOpen && !Wave2BlocksWalking && !IsSelfSitting(), (x, y) => { _pendingPickup = _pendingNpc = 0; _attackTarget = 0; W3WalkCancel("stick"); Session.WalkTo(x, y); });
                Hud = gameObject.AddComponent<LokiHud>();
                Hud.Build(boot.Rig, Dialog, Player, Inventory, Quests, Session, () => Session.SelectedChar?.Name ?? "", Prog, View, MiniMap, UseSkill);
                Hud.CancelArmed = () => { if (W12CancelGestures()) return true; if (_armed == null) return false; Disarm(); return true; }; // (client wave 12: B first calls off an open casting gesture)
                Hud.Chat.Submit = line => { line = NormalizeCommand(line); Debug.Log("[Game] console: " + line); if (Session.State == RoSession.Stage.InGame) { if (TryLocalCommand(line)) return; if (Social != null) Social.Say(line); else Session.Chat(Session.SelectedChar.Name, line); } };
                Hud.Chat.NpcWantsKeyboard = () => Dialog.WantsKeyboard;
                Hud.Chat.NpcChar = c => Dialog.TypeChar(c);
                Hud.Chat.NpcBackspace = () => Dialog.Backspace();
                Hud.Chat.NpcEnter = () => Dialog.SubmitInput();
                Hud.Chat.Add(Lang.T("Connecting to the local server…"), "9aa3b5");
                System.Func<Camera> head = () => boot.Rig.Head;
                Shops = new Shops(Session, Inventory, Player, ListWindow.Create(boot.Rig.transform, "ShopWindow"), head, ItemChat, () => Dialog.DismissLocal());
                Storage = new Storage(Session, Inventory, ListWindow.Create(boot.Rig.transform, "StorageWindow"), head, () => Dialog.DismissLocal());
                Menus = new Menus(Session, Inventory, ListWindow.Create(boot.Rig.transform, "MenuWindow"), head, ItemChat);
                Hud.Wrist.OnUseCard = e => Menus.UseCard(e.Index);
                Hotkeys = new Hotkeys(Session);
                Emotes = new Emotes(Session, Entities, boot.Assets, m => Hud.Wrist.Toast(m));
                Nav = new Navigation(() => _boot.Map.Current?.Name, () => Entities.Self, boot.Map.transform, (t, c) => Chat(t, c));
                Buffs = BuffBar.Create(boot.Rig.transform, boot.Rig.LeftHand, boot.Rig.Head, Prog, () => Hud.Wrist.ShownOnWrist);
                Hud.Wrist.Hotkeys = Hotkeys;
                // client wave 15 (w15gear), points 3 and 29: who we are for the equipment page (Game/EquipRules.cs) and how we look (its doll)
                EquipRules.Job = () => Entities?.SelfLook != null ? Entities.SelfLook.Job : -1;
                EquipRules.Sex = () => Entities?.SelfLook != null ? Entities.SelfLook.Sex : -1;
                EquipRules.SkillLevel = id => Prog.Skills.TryGetValue(id, out var k) ? k.Level : 0;
                EquipmentWindow.SelfLook = () => Entities?.SelfLook;
                StartW21JobUi(); // client wave 21 (w21jobui): the panel theme's job variants follow our job (LokiGame.W21JobUi.cs, UI/JobTheme.cs)
                Hud.Wrist.SendEmote = id => Emotes.Send(id);
                Hud.Wrist.DropItem = (i, n) => Session.DropItem(i, n);
                Social = new SocialHub(Session, Entities, () => Session.SelectedChar?.Name ?? "", (t, c) => Chat(t, c));
                Hud.Wrist.Social = Social;
                Party = new Party(Session, () => Session.SelectedChar?.Name ?? "", (t, c) => Chat(t, c));
                Hud.Wrist.Party = Party;
                // party-mates in view: HP bar over the head (ZC_NOTIFY_HP_TO_GROUPM_R2 comes when they appear and on every change) and a tinted name
                Party.HpChanged += (aid, hp, max) => { var u = Entities.Get(aid); if (u != null && !u.IsSelf && u.Overhead != null) u.Overhead.SetHp(max > 0 ? Mathf.Clamp01(hp / (float)max) : -1f, true); };
                Party.Changed += DecoratePartyUnits;
                Entities.UnitAdded += u => { if (!u.IsSelf && Party.Members.TryGetValue(u.Id, out var m)) DecoratePartyUnit(u, m); };
                MiniMap.PartyCells = PartyCellsHere;
                Trade = new Trade(Session, Inventory, ListWindow.Create(boot.Rig.transform, "TradeWindow"), head, aid => Entities.Get(aid)?.Name, () => Player.Zeny, ItemChat);
                Cart = new Cart(Session, Inventory, Prog, ListWindow.Create(boot.Rig.transform, "CartWindow"), head, ItemChat);
                Market = new Market(Session, Inventory, Cart, ListWindow.Create(boot.Rig.transform, "MarketWindow"), head, aid => Entities.Get(aid), () => Player.Zeny, ItemChat);
                Cart.CanVend = () => Prog.Skills.ContainsKey(41);
                Cart.OpenShop = () => { Cart.Hide(); Market.BeginVending(Prog.Skills.TryGetValue(41, out var v) ? v.Level : 1, Session.AccountId); };
                Entities.UnitAdded += Market.OnUnitAdded;
                Refinery = new Refinery(Session, Inventory, ListWindow.Create(boot.Rig.transform, "RefineWindow"), head, ItemChat);
                Refinery.Changed += () => { if (Refinery.Open) Dialog?.DismissLocal(); };
                Crafting = new Crafting(Session, Inventory, ListWindow.Create(boot.Rig.transform, "CraftWindow"), head, () => Session.LastSkillId, ItemChat);
                Guild = new Guild(Session, ListWindow.Create(boot.Rig.transform, "GuildWindow"), head, () => Session.SelectedChar?.CharId ?? 0u, () => Session.AccountId, (t, c) => Chat(t, c),
                                  () => Session.State == RoSession.Stage.InGame && Entities.Self != null);
                Guild.UnitById = id => Entities.Get(id);
                Target.IsHostile = IsHostilePlayer;
                Guild.Gazed = () => { var t = Target != null ? Target.Locked : null; return t != null && t.Unit != null && t.Unit.IsPlayerType && !t.Unit.IsSelf ? t.Unit : null; };
                Guild.Toast = m => Hud.Wrist.Toast(m);
                Guild.UnitInfo += DecorateGuildUnit;
                Guild.EmblemChanged += gid => { foreach (var u in Entities.Units.Values) if (u != null && u.GuildId == gid) DecorateGuildUnit(u); };
                Guild.Changed += DecorateGuildUnits;
                Guild.MapPropertyChanged += DecorateGuildUnits;
                Entities.UnitAdded += u => { if (u.GuildId != 0) { DecorateGuildUnit(u); Guild.RequestName(u.Id); } };
                Entities.GuildChanged += u => { DecorateGuildUnit(u); if (u.GuildId != 0) Guild.RequestName(u.Id, true); }; // (true: its guild changed, an earlier answer is stale: Game/Guild.cs RequestName)
                Entities.NamesChanged += DecorateGuildUnit;
                MiniMap.GuildCells = GuildCellsHere;
                Hud.Wrist.Guild = Guild;
                Hud.Wrist.OpenGuild = () => Guild.Show();
                Hud.Wrist.OpenGuildMembers = () => Guild.Show(Guild.Page.Members);
                // pets, homunculi, mercenaries: packets, the companion window and the client-side homunculus / mercenary AI
                Companions = new Companions(Session, Inventory, ListWindow.Create(boot.Rig.transform, "CompanionWindow"), head, () => Entities.Self, id => Entities.Get(id), (t, c) => Chat(t, c));
                Companions.ShowEmotion = (id, e) => Emotes.ShowOn(id, e);
                Companions.Toast = m => Hud.Wrist.Toast(m);
                Companions.GazedMonster = () => { var t = Target != null ? Target.Locked : null; return t != null && t.Unit != null && (t.Unit.Kind == Unit.UnitKind.Monster || IsHostilePlayer(t.Unit)) ? t.Unit : null; };
                Companions.UnitChanged += DecorateCompanion;
                Companions.Changed += () => { if (Companions.HomId != 0) DecorateCompanion(Companions.HomId); if (Companions.MercId != 0) DecorateCompanion(Companions.MercId); };
                Entities.Vanished += Companions.OnVanished;
                Entities.UnitAdded += u => { if (u.Kind == Unit.UnitKind.Companion && Companions.Owns(u.Id)) DecorateCompanion(u.Id); };
                Entities.SelfDied += Companions.OnSelfDied;
                // the homunculus and the mercenary defend their owner and themselves: the official default AI does it now, from every
                // unit's target (Game/Companions.W4Ai.cs GetOwnerEnemy / GetMyEnemy, client wave 4 w4units); a command here would also
                // break its standby (FOLLOW_CMD_ST ignores attackers)
                Hud.Wrist.Companions = Companions;
                // mail, achievements + titles, daily check-in, bank, cash shop, equipment swap set, costumes (item 9; research/windows9_packets.md)
                System.Action<string, string> chat = (t, c) => Chat(t, c);
                Mail = new Mail(Session, Inventory, ListWindow.Create(boot.Rig.transform, "MailWindow"), head, () => Player.Zeny, chat);
                Mail.Toast = m => Hud.Wrist.Toast(m);
                Achievements = new Achievements(Session, ListWindow.Create(boot.Rig.transform, "AchievementWindow"), head, chat);
                Achievements.Toast = m => Hud.Wrist.Toast(m);
                CheckIn = new CheckIn(Session, ListWindow.Create(boot.Rig.transform, "CheckInWindow"), head, chat);
                Achievements.OpenCheckIn = () => CheckIn.Open();
                Bank = new Bank(Session, ListWindow.Create(boot.Rig.transform, "BankWindow"), head, () => Player.Zeny, chat);
                CashShop = new CashShop(Session, ListWindow.Create(boot.Rig.transform, "CashShopWindow"), head, chat);
                Swap = new EquipSwitch(Session, Inventory, ListWindow.Create(boot.Rig.transform, "SwapWindow"), head, chat);
                Hud.Wrist.Mail = Mail; Hud.Wrist.Achievements = Achievements; Hud.Wrist.CheckIn = CheckIn; Hud.Wrist.Bank = Bank; Hud.Wrist.CashShop = CashShop; Hud.Wrist.Swap = Swap;
                // client wave 14 (w14menu), point 9: the costume switch is Settings → Graphics "Costume looks" (in the game only)
                ComfortWindow.CostumesHidden = () => Session != null && Session.State == RoSession.Stage.InGame ? CostumesHidden : (bool?)null;
                ComfortWindow.SetCostumesHidden = hide => { if (hide != CostumesHidden) SetCostumesHidden(hide); };
                // titles: every player's 0x0a30 carries it (asked for when they appear; the guild code's queue sends 0x0368), ours included
                Entities.NamesChanged += DecorateTitle;
                Entities.UnitAdded += u => { if (u.IsPlayerType && !u.IsSelf) Guild.RequestName(u.Id); };
                // comfort & polish (item 10): settings window, first-launch tutorial, vignette, left-handed hands, rumble
                ComfortUi = new ComfortWindow(ListWindow.Create(boot.Rig.transform, "ComfortWindow"), head);
                Tutorial = new Tutorial(ListWindow.Create(boot.Rig.transform, "TutorialWindow"), head);
                ComfortUi.ShowTutorial = () => Tutorial.Show();
                InitW18Tut(boot); // client wave 18 (w18tut), point 34: the interactive tutorial; Settings' "Controls tutorial" now opens it (LokiGame.W18Tutorial.cs)
                Hud.Wrist.OpenComfort = () => ComfortUi.Show();
                LeaveUi = new LeaveWindow(ListWindow.Create(boot.Rig.transform, "LeaveWindow"), head);
                LeaveUi.Chosen = OnLeaveChosen;
                Hud.Wrist.OpenLeave = () => { Hud.Wrist.Hide(); LeaveUi.Show(); };
                Vignette = VR.ComfortVignette.Attach(boot.Rig.Head);
                Vignette.Moving = () => boot.Rig.TurningNow || boot.Rig.PanningNow || (View != null && View.Mode != VR.ViewController.ViewMode.Table && Entities.Self != null && Entities.Self.Walking);
                boot.Rig.SnapTurned += () => Vignette.Flash();
                // client wave 19 (w19world), point 48: in Follow, a glow at the view's edge towards our character (its middle: half a cell over the feet)
                EdgeGlow = VR.EdgeGlow.Attach(boot.Rig.Head);
                EdgeGlow.Target = () => View != null && View.Mode == VR.ViewController.ViewMode.Follow && Entities.Self != null && _boot.Map != null
                    ? Entities.Self.transform.position + Vector3.up * (0.5f * Mathf.Abs(_boot.Map.transform.lossyScale.x)) : (Vector3?)null;
                InitW3Move(); // client wave 3, w3move: pet loot walks, self knockback comfort (LokiGame.W3Move.cs)
                InitW3Walk(); // client wave 3b, w3walk: far walk targets in legs, the minimap walks (LokiGame.W3Walk.cs)
                InitW5Move(); // client wave 5, w5move: the attack chase in legs (LokiGame.W5Move.cs)
                InitW10Move(); // client wave 10, w10move: smoothed movement, the steering stick (LokiGame.W10Move.cs)
                // the server's view area as a faint square on the ground, its range measured from units leaving sight (World/VisionArea.cs)
                Vision = gameObject.AddComponent<World.VisionArea>();
                Vision.Init(() => Entities.Self, id => Entities.Get(id), boot.Map);
                Entities.Vanished += Vision.OnVanished;
                Quick = QuickBar.Create(boot.Rig.transform, boot.Rig.OffHand, boot.Rig.Head, Hotkeys, Inventory, Prog, slot => Hud.Wrist.QuickUse(slot),
                                        () => Hud.Wrist.ShownOnWrist || Session.State != RoSession.Stage.InGame || Entities.Self == null);
                InitWheels(boot); // the radial skill wheels on a held walking-hand trigger (LokiGame.Wheels.cs)
                InitW12Cast(boot); // client wave 12 (w12cast): the casting gesture (pad / flick / wand), the spell in hand (client wave 14: the item buttons are gone) (LokiGame.W12Cast.cs)
                var au = Loki.Audio.LokiAudio.Instance; if (au != null) { au.MusicVolume = VR.Comfort.MusicVolume; au.EffectsVolume = VR.Comfort.EffectsVolume; } // saved levels (Comfort)
                boot.Rig.HandsSwapped += ApplyHands; ApplyHands();
                // hit rumble: per landed hit on the HitScheduler (LokiGame.W3Combat.cs, client wave 3)
                Entities.SelfDied += () => VR.Haptics.Both(VR.Haptics.Kind.Death, 0.9f, 0.5f);
                Player.BaseLevelUp += lv => VR.Haptics.Both(VR.Haptics.Kind.LevelUp, 0.7f, 0.3f);
                Inventory.Gained += (id, n) => VR.Haptics.Pulse(VR.Haptics.Kind.Pickup, false, 0.2f, 0.05f);
                BeginW4Vr(boot); // client wave 4 (w4vr): controllers / hands, job level and low-HP rumble, button strip, quick bar rows (LokiGame.W4Vr.cs)
                Hud.Wrist.OpenCart = () => Cart.Show();
                Hud.Wrist.OpenMarket = () => { if (Market.Vending || Market.Buying) Market.ShowMine(); else Market.BeginBuying(Session.AccountId); };
                Hud.Wrist.MarketLabel = () => Market.Vending ? Lang.T("My shop") : Market.Buying ? Lang.T("My store") : Lang.T("Buy store");
                Hud.Wrist.TradeGazed = () => { var t = Target != null ? Target.Locked : null; if (t != null && t.Unit != null && t.Unit.IsPlayerType && !t.Unit.IsSelf) Trade.Request(t.Unit.Id, t.Unit.Name); else Hud.Wrist.Toast(Lang.T("Look at a player first (gaze lock).")); };
                Hud.Wrist.GazedPlayerName = () => { var t = Target != null ? Target.Locked : null; return t != null && t.Unit != null && t.Unit.IsPlayerType && !t.Unit.IsSelf ? t.Unit.Name : null; };
                Hud.Wrist.RequestKeyboard = f => { Keyboard.Attach(f); Keyboard.Show(); };
                Hud.Wrist.PrefillChat = t => { if (Hud.ChatRefused()) return; Hud.Chat.SetInput(t); if (!Hud.Chat.Visible) Hud.Notify(Lang.T("Open the chat at your elbow."), 4f); Hud.Chat.KeyboardRequested?.Invoke(); }; // (client wave 19, point 46; w19view41, point 41: the line is filled and the keyboard opens, but only the elbow's chat button shows the chat)
                // the NPC dialog (specified after the 2026-10-01 playtest): a place its text links to starts the navigation there, as
                // the official client's link does (type 0 = map and x / y, 1 = the map only; Kafra + airship allowed, 101, the script
                // command's default; the guide's chat line and arrow, no window); and every button of every panel can be chosen by
                // looking at it (design note, the same day: "add Gaze Choosing to all UI elements", the laser on a button winning)
                Hud.Dialog.NaviRequested = l =>
                {
                    if (Nav == null || string.IsNullOrEmpty(l.Map)) return;
                    Debug.Log($"[Dialog] place link: {l}");
                    Nav.Go(l.X > 0 || l.Y > 0 ? 0 : 1, l.Map, l.X, l.Y, 0, 101, true, true);
                };
                boot.Rig.GazeChoosable = b => b.gameObject.activeInHierarchy && Hud.Wrist.GazeAllowed(b); // (client wave 11: not the menu ring's icons in its twist mode)
                InitW11Menu(boot); // client wave 11 (w11menu): the gaze dot, the console commands list (LokiGame.W11Menu.cs)
                InitW22Clips(boot); // client wave 22 (w22clips), points 53 and 59: the tutorial's clips and the ghost controller on the guide (LokiGame.W22Clips.cs)
                InitW20Tut(boot); // client wave 20 (w20tut), points 42, 43, 65: the look calibration, the Poring's filling ring, the check marks (LokiGame.W20Tut.cs; after InitW11Menu: it wraps the look ring's test)
                InitW14Kit(); // client wave 14 (w14kit), point 24: the Commands window's Character presets (LokiGame.W14Kit.cs)
                // VR keyboard: the login screen, character names, the chat console and NPC text prompts all type through it
                Keyboard = VrKeyboard.Create(boot.Rig.transform, boot.Rig.Head);
                Hud.Chat.KeyboardRequested = () => { Keyboard.OnChar = c => Hud.Chat.TypeChar(c); Keyboard.OnBackspace = () => Hud.Chat.Backspace(); Keyboard.OnEnter = () => Hud.Chat.Enter(); Keyboard.Show(); };
                foreach (var lw in boot.Rig.GetComponentsInChildren<ListWindow>(true)) lw.RequestKeyboard = f => { Keyboard.Attach(f); Keyboard.Show(); }; // every list window (shop, storage, menu, trade, cart, market) types through the VR keyboard
                var servers = LoginPanel.BuildServerList(cfg); // loki.json's servers + the ones the player added in game (UI/LoginPanel.Servers.cs)
                W5FriendsLogServers(servers); // client wave 5 (w5friends): the list in Player.log, for a friend's support (LokiGame.W5Friends.cs)
                Login = LoginPanel.Create(boot.Rig.transform, boot.Rig.Head, Keyboard, new RoAccountSession(Session), Stdb, servers);
                Login.QuitRequested += () => Application.Quit();
                Hud.Chat.LoginWantsKeyboard = () => Login.WantsKeyboard;
                Hud.Chat.LoginChar = c => Login.KeyChar(c); Hud.Chat.LoginBackspace = () => Login.KeyBackspace(); Hud.Chat.LoginEnter = () => Login.KeyEnter();
                if (_autoLogin) Login.Hide();
                else if (_resumeTicket != null && _resumeTicket.Online) { Login.PlaceInFront(); Login.ResumeOnline(); Hud.Chat.SetVisible(false); Stdb.ShowCharactersAgain(); }
                else { Login.PlaceInFront(); Login.Show(_resumeTicket != null ? LoginPanel.Screen.Connecting : LoginPanel.Screen.Login); Hud.Chat.SetVisible(false); }
                BeginW24Hud(boot); // client wave 24 (w24), item 9: the chat box's close button and its open tab (LokiGame.W24Hud.cs)
                BeginW24Login(boot); // client wave 24 (w24): the login screen's music, the login panel and the keyboard taken by their edge and kept clear of the ground (LokiGame.W24Login.cs)
                BeginWave1(boot); // player menu, progress bars, skill menus, NPC keyboard, traits, stop attack, connection loss
                BeginWave2(boot); // chat rooms, whispers, ignore list, party booking, point shops, search store, stylist, laphine, merge, family… (LokiGame.Wave2.cs)
                BeginW3Skills(boot); // client wave 3: the skill window follows the character's job (LokiGame.W3Skills.cs)
                BeginW22Tree(boot); // client wave 22 (w22tree), point 64: the 3D skill tree's hands, pull and preview (LokiGame.W22Tree.cs)
                BeginW3Login(boot); // client wave 3: keyboard caret on the chat console and the login fields (LokiGame.W3Login.cs)
                BeginW4Account(boot); // client wave 4: data folder on the login screen, reconnect after a lost map server (LokiGame.W4Account.cs)
                BeginW3Social(boot); // client wave 3: world map, minimap zoom, 12-member party page, all emotes (LokiGame.W3Social.cs)
                BeginW4Items(boot); // client wave 4: roulette, cart ↔ storage, cart change, cooking results, dual wield after a job change (LokiGame.W4Items.cs)
                BeginW4Social(boot); // client wave 4: chat tabs, slash commands, channels, clans, PvP, duels, emblems (LokiGame.W4Social.cs)
                BeginW4World(boot); // client wave 4: weather, quest icons, the Guide, screenshots, the desktop spectator view (LokiGame.W4World.cs)
                BeginW25Misc(boot); // client wave 25 (w25misc): the login backdrop's sky (after BeginW4World: its weather), Play Dead's sound, a sound when a skill goes up a level (LokiGame.W25Misc.cs)
                BeginWCodex(boot); // wave 28 (wcodex): the Codex book, its look ring, what writes creatures and items into it (LokiGame.WCodex.cs)
                BeginWcxp(); // wave 28 (wcxp): the Codex's bounties, the server's (LokiGame.Wcxp.cs; after BeginWCodex: its book, ring and card)
                BeginW4Lang(boot); // client wave 4b (w4lang): a change of language redraws the open panels (LokiGame.W4Lang.cs)
                BeginW5Ui(boot); // client wave 5 (w5ui): joke lines, the skill window's sex for job names (LokiGame.W5Ui.cs)
                BeginW6Voice(boot); // client wave 6 (w6voice): voice between friends: the player menu's rows, the first-time notice (LokiGame.W6Voice.cs)
                BeginW7Data(boot); // client wave 7 (w7data): the install's patch and what the start found about the game data, on the login screen (LokiGame.W7Data.cs)
                BeginW8Horde(boot); // client wave 8 (w8game): the Horde side mode: the wrist button, A in the arena (LokiGame.W8Horde.cs)
                InitW21Map(); // client wave 21 (w21map), point 47: the tutorial's own map, a private copy the server makes (LokiGame.W21Map.cs; after InitW14Kit, whose whisper answer taker it wraps, and BeginWave2, which makes Modes)
                W4ReleaseLogStart(); // client wave 4b (w4release): what the start shows, in one log line (LokiGame.W4Release.cs)
            }

            BeginW3Combat(); // per-hit damage numbers, flinches, hit sounds, arrows and rumble on one schedule (LokiGame.W3Combat.cs, client wave 3)
            BeginW3Status(boot); // client wave 3: other units' ailments and looks, status icons of others (LokiGame.W3Status.cs)
            BeginW4Units(boot); // client wave 4: mounts, costumes, babies, pets' accessories, spheres, companion AI, elementals, other units' looks (LokiGame.W4Units.cs)
            BeginW4Fx(boot); // client wave 4, w4fx: the GRF's ez2 skill effects, skill units changing their look (LokiGame.W4Fx.cs)
            BeginW5FxOwn(boot); // client wave 5, w5fxown: Loki's own skill effects' switch (LokiGame.W5FxOwn.cs)
            BeginW4Granny(boot); // client wave 4b, w4granny: the Granny 3-D units' map light and guild emblems (LokiGame.W4Granny.cs)
            BeginW5Ai(boot); // client wave 5, w5ai: the custom homunculus / mercenary AI, /hoai /merai /traceai (LokiGame.W5Ai.cs)
            BeginW5Looks(boot); // client wave 5, w5looks: Warlock spheres, True Sight, C_Marker, Hallucination's view, camera quakes (LokiGame.W5Looks.cs)
            Entities.SelfDied += () => { Player.Dead = true; Hud?.ShowDeath(); Hud?.Chat.Add(Lang.T("You have been defeated."), "ff7a6a"); };
            Entities.SelfRevived += () => { Player.Dead = false; Hud?.HideDeath(); };
            Entities.SelfSitting += sit =>
            {
                Hud?.SetSitting(sit);
                if (sit && Target != null)
                {
                    var me = Entities.Self;
                    if (Target.Steer.Active || (me != null && me.Steer.HasValue)) Target.Steer.Release(me, W3WalkRoutes, (x, y) => Session.WalkTo(x, y), false);
                    if (Target.Steer.Settling) Target.Steer.CancelSettle(me, "sat down");
                    if (me != null) me.Steer = null;
                }
            };
            Inventory.Gained += (id, n) => Hud?.Chat.Add(n > 1 ? Lang.F("Got {0} x{1}.", GameDb.GetItem(id).Name, n) : Lang.F("Got {0}.", GameDb.GetItem(id).Name), "a8e6a3");
            Inventory.Message += m => Hud?.Chat.Add(m, "ffb070");
            Quests.Added += q => { Hud?.Chat.Add(Lang.F("New quest: {0}", q.Info.Display), "ffd060"); Audio.PlayWav("effect\\st_job_level_up.wav"); };
            Quests.Marker += (npc, eff, col) => { W4WorldQuestMarker(npc, eff, col); if (eff != 9999) _markerCount++; }; // client wave 4 (w4world): sprite icons + minimap marks (was Entities.SetQuestMarker's text)
            Player.Changed += () =>
            {
                Player.SetShieldFraction(Prog.ComputeShieldFraction(Player.Hp, Player.MaxHp, Player.Sp, Player.MaxSp));
                Entities.Self?.Overhead?.SetHp(Player.MaxHp > 0 ? Player.Hp / (float)Player.MaxHp : 1f, true);
                Entities.Self?.Overhead?.SetShield(Player.ShieldFraction);
                Entities.Self?.Overhead?.SetSp(Player.MaxSp > 0 ? Player.Sp / (float)Player.MaxSp : 1f, true);
            };
            Prog.Changed += () =>
            {
                Player.SetShieldFraction(Prog.ComputeShieldFraction(Player.Hp, Player.MaxHp, Player.Sp, Player.MaxSp));
                Entities.Self?.Overhead?.SetShield(Player.ShieldFraction);
            };
            Player.BaseLevelUp += lv => { Hud?.Chat.Add(Lang.F("Base level up! Now level {0}.", lv), "ffd060"); if (!CxpSuppressingLevelUpEffect) Audio.PlayWav("levelup.wav"); if (Entities.Self != null) Fx.Burst(_boot.Map.transform, Entities.Self.transform.localPosition + Vector3.up, new Color(1f, 0.9f, 0.4f), 1.8f, 40); };
            Player.Par += (v, val) => Prog.OnParChange(v, val);
            // Heal numbers: the server sends potion healing as a plain HP/SP update, so show numbers for recoveries that
            // follow one of our own item uses, and for heal-skill recoveries (0x0a27); natural regen stays silent as in RO.
            Inventory.Used += id => { _lastItemUse = Time.time; Audio?.PlayItemUse(id, null); };
            Player.Recovered += (isSp, amount, fromSkill) =>
            {
                if (Time.time - _lastItemUse > 1.5f && !fromSkill) return;
                Entities.Self?.Overhead?.PopNumber("+" + amount, isSp ? new Color(0.45f, 0.65f, 1f) : new Color(0.4f, 1f, 0.5f));
                if (Entities.Self != null) Fx.Burst(_boot.Map.transform, Entities.Self.transform.localPosition + Vector3.up, isSp ? new Color(0.45f, 0.65f, 1f) : new Color(0.4f, 1f, 0.5f), 0.9f, 16);
            };
            // client wave 11 (w11menu): what the server refused or what failed is shown in front of the player whether or not a menu
            // is open (UI/LokiHud.cs Notify; a wrist toast was only seen with the menu open: the design's 2026-10-06 playtest, Steal)
            Prog.Message += m => { if (W14KBusy) { Debug.Log("[Presets] (kept out of the chat) " + m); return; } Hud?.Chat.Add(m, "ffd060"); Hud?.Notify(m); }; // client wave 18 (w18gear): a preset's own skill raises are not announced one by one
            Combat.Message += m => { Hud?.Chat.Add(m, "ffb070"); Hud?.Notify(m); Debug.Log("[Skill] " + m); };
            Inventory.Message += m => Hud?.Notify(m);
        }

        /// <summary>
        /// rAthena looks @commands up case-sensitively (atcommand.cpp: strdb lookup of the lower-case names), so
        /// "@WARP prontera" (Caps Lock) is "Unknown Command". Lower-case the command word, drop a space typed right after
        /// the @/#, and leave the arguments alone (character names are case-sensitive; map names are not).
        /// </summary>
        public static string NormalizeCommand(string line)
        {
            if (string.IsNullOrEmpty(line) || (line[0] != '@' && line[0] != '#')) return line;
            string rest = line.Substring(1).TrimStart();
            int sp = rest.IndexOf(' ');
            string cmd = sp < 0 ? rest : rest.Substring(0, sp), args = sp < 0 ? "" : rest.Substring(sp);
            return line[0] + cmd.ToLowerInvariant() + args;
        }

        internal static string Arg(string name, string fallback)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return fallback;
        }


        private float _nextRoomCheck;
        private void Update()
        {
            // Loki Online first (port wave H, hplay1): the SDK applies received packet_out rows only inside FrameTick (Stdb.Update →
            // StdbSession's OnInsert → RoConnection.Feed) and Session.Update frames the link's inbox (RoConnection.Pump). With the
            // session first, every packet of the module waited one more frame than a TCP packet of rAthena does (round trip measured
            // by the clock samples: 33 ms against 16.6 ms at 60 fps). On rAthena Stdb has no connection and does nothing.
            MenuNav.GameOpensBegin(); // client wave 14 (w14ui), point 29: a window opened while the servers' news is handled is the game's (UI/MenuNav.cs)
            Stdb?.Update();
            Session?.Update();
            _presence?.Poll();
            _w8Stdb?.Poll();
            MenuNav.GameOpensEnd();
            UpdatePending();
            UpdateWalkPickup(); // items walked over (LokiGame.WalkPickup.cs)
            UpdateWheels(); // the radial skill wheels (LokiGame.Wheels.cs)
            UpdateW12Cast(); // client wave 12 (w12cast): the casting gesture, the spell in hand (client wave 14: the item buttons are gone) (LokiGame.W12Cast.cs)
            UpdateW20Pen(); // client wave 20 (w20pen), point 57: the air pen (LokiGame.W20Pen.cs; after the casting: the wand and the overhead menu ran)
            UpdateW22Clips(); // client wave 22 (w22clips), points 53 and 59: the step's clip and its ghost (LokiGame.W22Clips.cs; before the tutorial draws its guide: the clip's band is there in the guide's first frame)
            UpdateW18Tut(); // client wave 18 (w18tut), point 34: the interactive tutorial's steps (LokiGame.W18Tutorial.cs; after the casting: it reads the gestures' state)
            UpdateW20Tut(); // client wave 20 (w20tut), point 65: the look calibration from Settings, on the guide (LokiGame.W20Tut.cs)
            UpdateW21Map(); // client wave 21 (w21map), point 47: a tutorial copy nobody uses is given back (LokiGame.W21Map.cs)
            UpdateW6Voice(); // client wave 6 (w6voice): the first-time voice notice, the Voice page's live meter (LokiGame.W6Voice.cs)
            UpdateW8Horde(); // client wave 8 (w8game): a Horde run cannot outlive the RO session's world (LokiGame.W8Horde.cs)
            UpdateW18Account(); // client wave 18 (w18acct): the account's settings kept in step (LokiGame.W18Account.cs)
            UpdateWCodex(); // wave 28 (wcodex): the look ring, the account's book, the item lists (LokiGame.WCodex.cs)
            UpdateWcxp(); // wave 28 (wcxp): the account's Codex bounties asked at each entry into the world (LokiGame.Wcxp.cs)
            Refinery?.Tick();
            Guild?.Tick();
            Progress?.Tick();
            if (Session != null && Session.State == RoSession.Stage.InGame) Companions?.Tick();
            if (Time.time > _nextRoomCheck && Room != null && Room.Active) { _nextRoomCheck = Time.time + 0.3f; if (Entities.Self != null) Room.Track(Entities.Self.Cell); ApplyRoomVisibility(); } // (client wave 20, w20rooms: Track = the room the character walked into)
            var cam = Camera.main;
            if (cam != null && _boot.Map != null) ActorSprite.CameraDirection = ActorSprite.ComputeCameraDirection(_boot.Map.transform, cam);
            if (Nav != null)
            {
                Nav.Update();
                if (MiniMap != null) MiniMap.Marker = Nav.Active && Nav.Waypoint.HasValue && string.Equals(Nav.WaypointMap, _boot.Map.Current?.Name, System.StringComparison.OrdinalIgnoreCase) ? Nav.Waypoint : null;
            }
        }

        private bool _autoLogin;
        public VrKeyboard Keyboard { get; private set; }
        public LoginPanel Login { get; private set; }


        private void OnEnteredMap(RoSession s)
        {
            _wasInGame = true;
            Net.ClientUpdater.InGameContext = true;
            string map = StripExt(s.MapName);
            Guild?.ResetForCharacter(s.SelectedChar.CharId); // before EnterMap: guild packets can arrive before our LoadEndAck
            Companions?.ResetForCharacter();                 // the server re-sends every companion after our LoadEndAck (clif.cpp:10852-10890)
            Prog?.Reset();
            Mail?.ResetForCharacter(); Achievements?.ResetForCharacter(); CheckIn?.Reset(); Bank?.Reset(); CashShop?.ResetForCharacter(); Swap?.Reset();
            CostumesHidden = PlayerPrefs.GetInt("loki.hidecostumes." + s.SelectedChar.Name, 0) != 0;
            EnterMap(map, s.StartX, s.StartY, s.StartDir);
            Player.InitFrom(s.SelectedChar);
            Inventory.SetSelf(s.AccountId); Party?.SetSelf(s.AccountId);
            var ch = s.SelectedChar;
            Debug.Log($"[Game] in game on {map} at {s.StartX},{s.StartY} as job {ch.Job} sex {ch.Sex} hair {ch.Head}");
            Hud?.Chat.Add(Lang.F("Welcome, {0}. You are on {1}.", ch.Name, map), "9ad1ff");
            if (Hud != null) { Hud.Chat.SetVisible(Hud.ChatWanted); if (!Hud.ChatWanted) Hud.PlaceChat(); } Login?.Hide(); // (client wave 19, w19view41, point 41: as the player last left it with the chat button; client wave 24, w24, item 17: kept with the account's settings, and HIDDEN until the player first shows it; hidden, it is placed in front all the same: its open tab stands at its corner: LokiGame.W24Hud.cs)
            bool w18tut = W18TutOnEnteredMap(s); // client wave 18 (w18tut), point 34: a new character's first login opens the interactive tutorial (LokiGame.W18Tutorial.cs)
            if (!VR.Comfort.TutorialDone && !w18tut && Tutorial != null && !Tutorial.Visible) Tutorial.Show(); // first launch only (a release build runs no smoke and sends no startupCommands: w4release; client wave 18: not over the interactive one)
        }

        /// <summary>Console commands the client answers itself: /trade Name (the server needs the account id, so the player must be in view).</summary>
        private bool TryLocalCommand(string line)
        {
            if (line == null) return false;
            if (TryWave2Command(line)) return true; // /bt: battleground team chat (client wave 2)
            if (TryW3SocialCommand(line)) return true; // emote commands /lv, /! … (client wave 3, LokiGame.W3Social.cs)
            if (TryW4SocialCommand(line)) return true; // #channel lines and the official slash commands (client wave 4, LokiGame.W4SocialCommands.cs)
            if (TryW4WorldCommand(line)) return true; // /navi (client wave 4, LokiGame.W4World.cs)
            if (line.StartsWith("/guild ", System.StringComparison.OrdinalIgnoreCase)) { Guild?.Create(line.Substring(7).Trim()); return true; } // the official client's /guild <name>
            if (line.StartsWith("/trade ", System.StringComparison.OrdinalIgnoreCase))
            {
                string name = line.Substring(7).Trim();
                var u = FindPlayer(name);
                if (u == null) Chat(Lang.F("{0} isn't in view — you have to be next to someone to trade.", name), "ffb070"); else Trade?.Request(u.Id, u.Name);
                return true;
            }
            return W4SocialUnknownSlash(line); // client wave 4: any other "/…" is "Invalid Command", never public chat (LokiGame.W4Social.cs)
        }

        // ---- party decorations on the units in view
        private static readonly Color PartyNameColor = new Color(0.72f, 1f, 0.8f);
        private void DecoratePartyUnit(Sprites.Unit u, Party.Member m)
        {
            if (u == null || u.Overhead == null) return;
            u.Overhead.SetNameColor(PartyNameColor);
            if (m.MaxHp > 0 && m.Hp >= 0) u.Overhead.SetHp(Mathf.Clamp01(m.Hp / (float)m.MaxHp), true);
        }
        private void DecoratePartyUnits()
        {
            if (Entities == null || Party == null) return;
            foreach (var u in Entities.Units.Values)
            {
                if (u == null || u.IsSelf || !u.IsPlayerType || u.Overhead == null) continue;
                if (Party.Members.TryGetValue(u.Id, out var m)) DecoratePartyUnit(u, m);
                else { u.Overhead.SetNameColor(Color.white); u.Overhead.SetHp(-1f); } // left the party (or we did): back to a plain player
            }
        }
        private IEnumerable<(Vector2 cell, bool dead)> PartyCellsHere()
        {
            if (Party == null || !Party.InParty) yield break;
            string here = _boot.Map.Current?.Name;
            foreach (var m in Party.Members.Values)
            {
                if (m.Aid == Session.AccountId || !m.Online) continue;
                var u = Entities.Get(m.Aid);
                if (u != null) { yield return (u.Cell, m.Dead); continue; }          // in view: the live position beats the coarse party one
                if (m.HasPosition && string.Equals(m.Map, here, System.StringComparison.OrdinalIgnoreCase)) yield return (new Vector2(m.X, m.Y), m.Dead);
            }
        }

        // ---- guild decorations: "[emblem] Guild [Title]" over players (and guardians / the Emperium of an owned castle)
        private static readonly Color GuildOwnColor = new Color(0.55f, 0.85f, 1f), GuildAllyColor = new Color(0.6f, 1f, 0.65f), GuildEnemyColor = new Color(1f, 0.5f, 0.45f), GuildOtherColor = new Color(0.85f, 0.87f, 0.95f);
        /// <summary>Own guild blue, allies green, antagonists red — and every other guild red too while the War of Emperium runs on this map.</summary>
        public Color GuildColor(uint gid, string name = null)
        {
            if (Guild == null || (gid == 0 && string.IsNullOrEmpty(name))) return GuildOtherColor;
            if (Guild.InGuild && (gid != 0 ? gid == Guild.Id : name == Guild.Name)) return GuildOwnColor;
            var rel = gid != 0 ? Guild.RelationWith(gid) : Guild.Relations.Find(x => x.Name == name);
            if (rel != null) return rel.Kind == 0 ? GuildAllyColor : GuildEnemyColor;
            return Guild.WoeHere ? GuildEnemyColor : GuildOtherColor;
        }
        /// <summary>
        /// Can this player be attacked here? Only on a "vs" map — AGITZONE while the War of Emperium runs on it, or FREEPVPZONE (0x099b type 3 / 1) —
        /// and only if it is not in our party, our guild or an allied guild: battle_check_target's rivalry rules (battle.cpp:8138-8171). The server decides for real.
        /// </summary>
        public bool IsHostilePlayer(Sprites.Unit u)
        {
            if (u == null || !u.IsPlayerType || u.IsSelf || Guild == null) return false;
            var bg = Modes?.BgHostile(u); if (bg.HasValue) return bg.Value; // on a battlefield the camp (0x02dd) decides, not guild or party (client wave 2)
            if (Guild.MapType != 1 && Guild.MapType != 3) return false;
            if (Party != null && Party.Members.ContainsKey(u.Id)) return false;
            if (Guild.InGuild)
            {
                if (u.GuildId != 0 && (u.GuildId == Guild.Id || (Guild.RelationWith(u.GuildId)?.Kind ?? -1) == 0)) return false;
                if (u.GuildId == 0 && u.GuildName.Length > 0 && (u.GuildName == Guild.Name || Guild.Relations.Exists(x => x.Kind == 0 && x.Name == u.GuildName))) return false;
            }
            return true;
        }
        /// <summary>A freshly founded guild shows up by name only: its founder's unit packets keep GUID 0 until they are seen again (guild.cpp:722-742 sends no 0x0b47).</summary>
        public Color GuildColorOf(Sprites.Unit u) => u == null ? GuildOtherColor : GuildColor(u.GuildId, u.GuildName);
        private void DecorateGuildUnit(Sprites.Unit u)
        {
            if (u == null || u.Overhead == null || u.IsSelf || Guild == null) return;
            if (u.GuildId == 0 && u.GuildName.Length == 0) { u.Overhead.SetGuildLine(null, null, Color.white); return; }
            string text = u.GuildName.Length > 0 ? u.GuildName + (u.GuildTitle.Length > 0 ? $" [{u.GuildTitle}]" : "") : "";
            u.Overhead.SetGuildLine(text, Guild.EmblemOf(u.GuildId, u.GuildEmblem, false), GuildColorOf(u));
        }
        private void DecorateGuildUnits()
        {
            if (Entities == null || Guild == null) return;
            if (Entities.Self != null) { Entities.Self.GuildId = Guild.Id; Entities.Self.GuildEmblem = Guild.EmblemVersion & 0xFFFF; Entities.Self.GuildName = Guild.Name; Entities.Self.GuildTitle = Guild.MyTitle; }
            foreach (var u in Entities.Units.Values)
            {
                if (u == null || u.IsSelf) continue;
                if (u.GuildId != 0 || u.GuildName.Length > 0 || (u.Overhead != null && (u.Overhead.GuildLine.Length > 0 || u.Overhead.HasEmblem))) DecorateGuildUnit(u);
                // a new member's first name reply can come before the map server has it on the roster (no title yet, guild.cpp:1105-1151): ask again
                if (Guild.InGuild && u.GuildId == Guild.Id && u.IsPlayerType && u.GuildTitle.Length == 0) Guild.RequestName(u.Id, true);
            }
        }
        /// <summary>Panels that float by the walking hand follow it when left-handed mode is switched (Comfort.LeftHanded).</summary>
        private void ApplyHands()
        {
            var rig = _boot.Rig;
            Hud?.Wrist.SetHand(rig.OffHand); MiniMap?.SetHand(rig.OffHand, VR.Comfort.LeftHanded); Buffs?.SetHand(rig.OffHand); Quick?.SetHand(rig.OffHand);
            Debug.Log($"[Comfort] hands: pointer {(VR.Comfort.LeftHanded ? "left" : "right")}");
        }

        private void SetCostumesHidden(bool hide)
        {
            Session?.SendPacket(new PacketWriter(0x02d8).U32(5).U32(hide ? 1u : 0u).ToArray()); // CZ_CONFIG → pc_set_costume_view → 0x01d7 to the area, 0x02d9 back
            Debug.Log($"[Costume] request hidden {hide}");
        }

        /// <summary>Title line over other players; our own title goes to the achievements window.</summary>
        private void DecorateTitle(Unit u)
        {
            if (u == null) return;
            if (u.IsSelf) { Achievements?.OnOwnTitle(u.TitleId); return; }
            if (u.IsPlayerType && u.Overhead != null) u.Overhead.SetTitleLine(u.TitleId > 0 ? Achievements.TitleName(u.TitleId) : null);
        }

        // ---- own companions: name always shown (tinted), homunculus / mercenary HP bar
        private static readonly Color CompanionOwnColor = new Color(0.6f, 1f, 0.75f);
        private void DecorateCompanion(uint id)
        {
            var u = Entities?.Get(id);
            if (u == null || u.Overhead == null || Companions == null || !Companions.Owns(id)) return;
            u.Overhead.AlwaysShowName = true; u.Overhead.SetNameColor(CompanionOwnColor);
            var c = Companions;
            float hp = id == c.HomId && c.HomMaxHp > 0 ? c.HomHp / (float)c.HomMaxHp : id == c.MercId && c.MercMaxHp > 0 ? c.MercHp / (float)c.MercMaxHp : -1f;
            u.Overhead.SetHp(hp, true);
        }

        private IEnumerable<Vector2> GuildCellsHere()
        {
            if (Guild == null || !Guild.InGuild) yield break;
            foreach (var kv in Guild.MatePositions)
            {
                if (kv.Key == Session.AccountId || (Party != null && Party.Members.ContainsKey(kv.Key))) continue; // party markers already show them
                var u = Entities.Get(kv.Key);
                yield return u != null ? u.Cell : (Vector2)kv.Value;
            }
        }

        private void EnterMap(string map, int x, int y, int serverDir)
        {
            Ground.Clear();
            if (_boot.Map.Current == null || _boot.Map.Current.Name != map)
            {
                Entities.Clear(); _w2MapMissing = false;
                try { _boot.LoadMap(map); }
                catch (System.IO.FileNotFoundException e) when (_boot.Map.Current != null)
                {
                    // client wave 2: the server sent us to a map this game data doesn't have (e.g. bat_c03 in the LATAM data): keep the
                    // old scene standing instead of breaking, and say so — the player can warp out or use a wing
                    Debug.LogError($"[Game] map {map} is missing from the client data: {e.Message}"); _w2MapMissing = true;
                    Chat(Lang.F("Your game data has no map '{0}'. Warp out or use a Butterfly Wing.", map), "ff7a6a");
                    ShowNotice(Lang.T("Map missing"), Lang.F("Your game data has no map '{0}'.\nWarp out (a Butterfly Wing, or ask a GM).", map));
                }
                catch (System.Exception e) { W7DataMapUnreadable(map, e); } // client wave 7 (w7data): a map of an unknown version or a damaged one keeps the old scene too (LokiGame.W7Data.cs)
            }
            else Entities.Clear();
            var ch = Session.SelectedChar;
            Shops?.Reset(); Storage?.Reset(); Menus?.Reset(); Trade?.Reset(); Market?.Reset(); Refinery?.Reset(); Crafting?.Reset(); SkillMenus?.Reset(); PlayerMenu?.Reset(); Dialog?.DismissLocal();
            Entities.SetSelfLook(ch);
            Entities.CreateSelf(Session.AccountId, ch.Job, ch.Sex, ch.Head, x, y, serverDir);
            _pendingPickup = _pendingNpc = 0; _attackTarget = 0;
            FocusSelf();
            var m = _boot.Map.Current;
            Audio.LeaveLoginBgm(Audio.PlayMapBgm(map)); // (client wave 24, w24: the login screen's track fades over to the map's: Audio/LokiAudio.cs)
            Audio.SetMapAmbience(m?.Rsw, m?.Gnd, m?.Name); // (client wave 23, w23audio: the map's name, for the ambience muted per map: Audio/LokiAudio.cs AmbientMutes)
            Hud?.HideDeath(); Player.Dead = false;
            Combat.Clear(); Combat.SetSelf(Session.AccountId); Prog.SetSelf(Session.AccountId); Effects?.Clear();
            Party?.OnMapChange();
            Guild?.OnMapChange();
            Companions?.OnMapChange();
            Mail?.OnMapChange();
            if (Guild != null && Session != null) Guild.RequestName(Session.AccountId); // our own 0x0a30 tells the title we show
            Target?.ClearOnMapChange();
            if (m != null)
            {
                Room.Rebuild(map, m.Gat, _boot.Map.transform, new Vector2Int(x, y));
                Sky.Apply(map, Room.IsIndoor(map), m.Rsw, m.LightDirection);
                if (MiniMap != null)
                {
                    var tex = _boot.Assets.Texture("\u00c0\u00af\u00c0\u00fa\u00c0\u00ce\u00c5\u00cd\u00c6\u00e4\u00c0\u00cc\u00bd\u00ba\\map\\" + map + ".bmp", false); // 유저인터페이스\map
                    MiniMap.SetMap(map, m.Gat, tex == _boot.Assets.MissingTexture ? null : tex, _boot.Map.transform);
                }
            }
            if (!_viewStarted && View != null) { _viewStarted = true; if (View.Mode != StartView) View.SetMode(StartView); } // client wave 19 (w19small), point 55: the default view
            View?.OnSelfRecreated();
            Nav?.Recompute(false);
            OnMapChangeWave2(map); // client wave 2: chat rooms, viewpoints, instance / battleground HUD, map effects… (LokiGame.Wave2.cs)
            OnMapChangeW3Presence(map); // client wave 3: floating headsets, the pose relay (LokiGame.W3Presence.cs)
            OnMapChangeW6Voice(map); // client wave 6: voice between friends over that relay (LokiGame.W6Voice.cs)
            OnMapChangeW7Host(map); // client wave 7 (w7host): this client's game data compared with the host's, over that relay (LokiGame.W7Host.cs)
            OnMapChangeW3Social(map); // client wave 3: the world map's "you are here" (LokiGame.W3Social.cs)
            OnMapChangeW4Social(map); // client wave 4: PvP state, boss mark, channel / clan reset per character (LokiGame.W4Social.cs)
            OnMapChangeW4World(map); // client wave 4 (w4world): quest icons of the old map go (LokiGame.W4World.cs)
            OnMapChangeW4Units(map); // client wave 4: pets' accessories, our own intravision (LokiGame.W4Units.cs)
            OnMapChangeW8Horde(map); // client wave 8 (w8game): the Horde side mode (made on the first map; keeps its veil during a run) (LokiGame.W8Horde.cs)
            OnMapChangeW21Map(map, x, y); // client wave 21 (w21map), point 47: on or off the tutorial's own map (LokiGame.W21Map.cs)
        }
        /// <summary>Last EXP notice (var id, amount, quest flag) — smoke test.</summary>
        public (int var, long amount, int type) LastExp = (0, 0, 0);

        // ---- leaving the game (wrist menu → View → Leave the game…): character select, log out, quit
        public LeaveWindow LeaveUi { get; private set; }
        private LeaveWindow.Choice _leaveChoice; private int _leaveSerial;
        private RoSession.CharSelectTicket _resumeTicket;
        /// <summary>Survives the scene reload of a log-out: the next game shows the login screen instead of logging in from loki.json.</summary>
        private static bool _showLoginOnBoot;

        private void OnLeaveChosen(LeaveWindow.Choice choice)
        {
            if (Session == null || Session.State != RoSession.Stage.InGame || Session.Leaving != RoSession.Leave.None) return;
            _leaveChoice = choice;
            if (choice == LeaveWindow.Choice.CharSelect) Session.RequestCharSelect(); else Session.RequestQuit();
            LeaveUi?.SetStatus(choice == LeaveWindow.Choice.CharSelect ? Lang.T("Going back to the character list…") : choice == LeaveWindow.Choice.LogOut ? Lang.T("Logging out…") : Lang.T("Quitting…"), true);
            StartCoroutine(LeaveTimeout(++_leaveSerial));
        }

        /// <summary>No answer in 6 s: quitting or logging out goes ahead anyway (the map server saves a character whose connection
        /// drops — map_quit on EOF); a character select needs the server's ticket, so it is called off.</summary>
        private System.Collections.IEnumerator LeaveTimeout(int serial)
        {
            yield return new WaitForSeconds(6f);
            if (serial != _leaveSerial || Session == null || Session.Leaving == RoSession.Leave.None) yield break;
            Debug.LogWarning($"[Game] no answer to the {_leaveChoice} request");
            if (_leaveChoice == LeaveWindow.Choice.CharSelect) { Session.CancelLeave(); LeaveUi?.SetStatus(Lang.T("The server didn't answer — try again."), false); yield break; }
            yield return LeaveNow(RoSession.Leave.Quit);
        }

        private void OnLeaveRefused(RoSession.Leave how)
        {
            _leaveSerial++;
            string why = Lang.T("You can't leave right after fighting — wait 10 seconds and try again.");
            Debug.Log("[Game] leaving refused (prevent_logout): " + how);
            LeaveUi?.SetStatus(why, false); Chat(why, "ffb070");
        }

        private System.Collections.IEnumerator LeaveNow(RoSession.Leave how)
        {
            _leaveSerial++;
            Debug.Log($"[Game] leaving the game: {_leaveChoice} (server: {how})");
            LeaveUi?.SetStatus(_leaveChoice == LeaveWindow.Choice.Quit ? Lang.T("Goodbye!") : Lang.T("One moment…"), true);
            yield return new WaitForSeconds(0.3f);
            if (_leaveChoice == LeaveWindow.Choice.Quit)
            {
                Application.Quit();
                yield break;
            }
            if (_leaveChoice == LeaveWindow.Choice.LogOut)
            {
                _showLoginOnBoot = true;
                // Loki Online: log the account out on the server too (the `logout` reducer: leave_world + drop the identity's binding),
                // so the next start asks for the password — or "Continue as before" says this PC is no longer logged in
                if (Session.OnlineLink && Stdb != null) { Debug.Log("[Game] logging out of Loki Online"); Stdb.Reset(); }
            }
            LokiBootstrap.Reload(); // a fresh scene: every window, unit and per-character state starts over
        }

        /// <summary>Client wave 19 (w19small), point 55: the first map of a launch opens in <see cref="VR.ViewController.DefaultMode"/> (Follow).
        /// Smoke runs written before point 55 assumed the Table at the start: they keep it (nothing changes for them); w19small checks the default.
        /// Client wave 19 (w19view41), point 55 (design note, 2026-10-10: "Yes, it should remember."): the view the player last CHOSE (the view
        /// button's hold, the menu's View page: VR/ViewController.cs Chosen) is kept with the account (VR/Comfort.cs LastView, loki-user.json
        /// viewMode; not a per-PC field) and the first map of every launch opens in it; nothing chosen yet (a new player): Follow. A view the
        /// game sets itself (this start, Horde's Follow, a smoke's) is not a choice and is not kept.</summary>
        private bool _viewStarted, _viewChosen;
        /// <summary>The player chose a view (the view button's hold, the menu's View page): kept with the account's settings.</summary>
        private void W19ViewChosen(VR.ViewController.ViewMode m)
        {
            _viewChosen = true;
            if (VR.Comfort.LastView == (int)m) return;
            VR.Comfort.LastView = (int)m; VR.Comfort.Save();
            Debug.Log($"[View] {m} chosen: kept with the account's settings for the next launch");
        }
        /// <summary>The account's settings came in (LokiGame.W18Account.cs): a view the player has not changed this launch takes the kept one.</summary>
        private void W19ViewFromAccount()
        {
            if (View == null || !_viewStarted || _viewChosen || View.Mode == StartView) return;
            Debug.Log($"[View] the account's kept view: {StartView}"); View.SetMode(StartView);
        }
        private VR.ViewController.ViewMode StartView
        {
            get
            {
                return VR.Comfort.LastView >= 0 ? (VR.ViewController.ViewMode)VR.Comfort.LastView : VR.ViewController.DefaultMode; // (client wave 19, w19view41, point 55: the view the player last chose)
            }
        }

        private void OnApplicationQuit() { Debug.Log("[Game] application quitting"); Codex?.SaveNow("the game quits"); Net.Stdb.StdbSession.Shared?.Disconnect(); } // (wave 28, wcodex: the Codex's last entries saved)

        /// <summary>Brings the player's character to the middle of the table (the original client's camera always follows you).</summary>
        private void FocusSelf()
        {
            var self = Entities?.Self;
            if (self == null) return;
            _boot.FocusCell(self.DrawnCell, self.transform.localPosition.y); // client wave 10 (w10move): the drawn body, as VR/ViewController does
        }


        private static string StripExt(string map) { int dot = map.IndexOf('.'); return dot > 0 ? map.Substring(0, dot) : map; }

        private bool IsSelfSitting() => _w4Sitting || (Entities != null && Entities.Self != null && Entities.Self.Sprite != null && Entities.Self.Sprite.ActionGroup == (int)ActorSprite.PlayerAction.Sit);

        // ---------------------------------------------------------------- pointer actions
        private void OnCellClicked(Vector2Int cell, string tag = "cell")
        {
            if (Session == null || Session.State != RoSession.Stage.InGame || Player.Dead) return;
            CancelPendingSkill("a walk"); // a click on the ground calls off a walk-then-cast (LokiGame.SkillRange.cs)
            if (Wave2BlocksWalking) { Hud?.Notify(Lang.T("Leave the chat room to walk.")); Chat(Lang.T("You can't walk while you're in a chat room."), "ffb070"); return; }
            if (IsSelfSitting()) { Hud?.Notify(Lang.T("Stand up to walk.")); return; }
            _pendingPickup = _pendingNpc = 0; _attackTarget = 0;
            var gat = _boot.Map.Current.Gat;
            if (!gat.InBounds(cell.x, cell.y)) return; // not a cell of this map
            if (Room != null && !Room.Visible(cell)) { Debug.Log($"[Game] a click on {cell}, outside the room drawn: no walk"); return; } // client wave 20 (w20rooms): an indoor map's other rooms are not drawn, so not walked to
            if (!gat.Get(cell.x, cell.y).Walkable && Entities.Self != null) { cell = NearestWalkable(cell, Vector2Int.RoundToInt(Entities.Self.Cell)); _freeExact = null; } // (free movement, wfree: another cell: no exact point)
            W3WalkStart(cell, tag); // far or near (client wave 3b, w3walk)
        }

        /// <summary>On indoor maps, name plates and pointer colliders of things outside your room are switched off too.</summary>
        private void ApplyRoomVisibility()
        {
            foreach (var u in Entities.Units.Values)
            {
                if (u == null || u.IsSelf) continue;
                bool v = Room.Visible(u.Cell);
                foreach (var c in u.GetComponentsInChildren<Canvas>(true)) c.enabled = v;
                // client wave 25 (w25misc), item 11 (design note: "when I interact with the wounded swordsman at the beginning, by pointing my laser at
                // him, he won't respond"): a hidden or cloaked unit keeps its pointer box OFF (Sprites/EntityManager.cs SetUnseen). This line
                // turned it back on every 0.3 s on an indoor map's drawn room, so on the start ship the swordsman's cloaked twin (rAthena
                // npc/re/jobs/novice/academy.txt:67-129: the lying one, cloaked for you once clicked, and the one who talks, at the same cell)
                // took the laser: every pull went to the invisible lying one again ("Oh...") and the talking one was never reached; the look
                // skips unseen units (Game/Targeting.cs), so looking worked. Measured: w25misc F11.
                var col = u.GetComponent<Collider>(); if (col != null) col.enabled = v && !u.Unseen;
            }
            foreach (var g in Ground.Items.Values)
            {
                if (g == null) continue;
                bool v = Room.Visible(g.Cell);
                foreach (var c in g.GetComponentsInChildren<Canvas>(true)) c.enabled = v;
                var col = g.GetComponent<Collider>(); if (col != null) col.enabled = v;
            }
        }

        // ---------------------------------------------------------------- skills
        /// <summary>From the wrist menu: self skills fire at once; others are armed for the next trigger pull.</summary>
        private void UseSkill(Progression.SkillEntry k)
        {
            if (Session == null || Session.State != RoSession.Stage.InGame || Player.Dead) return;
            if (k.CooldownUntil > Time.time) { Hud?.Notify(Lang.F("{0} is cooling down.", k.Info.Name)); return; }
            int lv = Prog.CastLevel(k); // the level chosen in the skill window (Lv button), else the highest learned
            if (k.SelfCast) { Debug.Log($"[Skill] cast {k.Info.Aegis} lv {lv} on self"); W3WalkCancel("skill"); Session.UseSkill(k.Id, lv, Session.AccountId); return; }
            ArmNow(k, lv);
            if (VR.Comfort.VisionAimSpells && VR.Comfort.CastOnTake && ReferenceEquals(_armed, k))
            {
                if (k.Ground) { var c = W12AimedCell(); if (c.HasValue) CastArmed(null, c); }
                else { var u = W12AimedUnit(); if (u != null) CastArmed(u, null); }
            }
        }

        private void ArmNow(Progression.SkillEntry k, int lv)
        {
            _w12Held = null; // (client wave 12: armed from a menu, the quick bar or a wheel: not a "spell in hand" of the casting gesture)
            _armed = k; _armedLevel = lv;
            if (Target != null) Target.ShowGroundAim = k.Ground;
            Hud?.SetArmed(W4LangArmedText(k, lv)); // w4lang: one whole sentence per aim (LokiGame.W4Lang.cs)
            Debug.Log($"[Skill] armed {k.Info.Aegis} lv {lv}");
        }

        /// <summary>Client wave 12 (w12cast): arms a skill for the next trigger pull whatever its kind (the casting gesture's "spell in
        /// hand"): a target or ground skill exactly as UseSkill arms it; a self skill too (UseSkill casts those at once), which the
        /// trigger then casts on ourselves (CastArmed).</summary>
        private void ArmSkill(Progression.SkillEntry k)
        {
            if (Session == null || Session.State != RoSession.Stage.InGame || Player.Dead) return;
            if (k.CooldownUntil > Time.time) { Hud?.Notify(Lang.F("{0} is cooling down.", k.Info.Name)); return; }
            ArmNow(k, Prog.CastLevel(k));
        }

        private void Disarm() { _armed = null; _w12Held = null; if (Target != null) Target.ShowGroundAim = false; Hud?.SetArmed(null); }

        /// <summary>Casts the armed skill at a unit (target skills) or a cell (ground skills). Returns false if nothing to aim at.</summary>
        private bool CastArmed(Unit unit, Vector2Int? cell)
        {
            var k = _armed; if (k == null) return false;
            // client wave 12 (w12cast): a self skill kept in the hand (ArmSkill; "cast when taken" off): the trigger casts it on ourselves
            if (k.SelfCast) { Debug.Log($"[Skill] cast {k.Info.Aegis} lv {_armedLevel} on self"); W3WalkCancel("skill"); Session.UseSkill(k.Id, _armedLevel, Session.AccountId); Disarm(); return true; }
            if (k.Ground)
            {
                var c = cell ?? (unit != null ? Vector2Int.RoundToInt(unit.Cell) : (Vector2Int?)null) ?? Target?.GazeCell;
                if (!c.HasValue) { Hud?.Notify(LookSelect.WorldOn ? Lang.T("Look where it should land.") : Lang.T("Point where it should land.")); return false; } // (client wave 13, w13look: "Look select: off": the laser aims)
                Debug.Log($"[Skill] cast {k.Info.Aegis} lv {_armedLevel} at {c.Value}");
                W3WalkCancel("skill");
                Session.UseSkillOnGround(k.Id, _armedLevel, c.Value.x, c.Value.y);
            }
            else
            {
                uint target = unit != null ? unit.Id : k.Support ? Session.AccountId : 0;
                if (target == 0) { Hud?.Notify(LookSelect.WorldOn ? Lang.T("Look at a target first.") : Lang.T("Point at a target first.")); return false; }
                CancelPendingSkill("a new cast");
                // out of range: walk into range first, the cast follows (LokiGame.SkillRange.cs; rAthena drops it otherwise)
                if (unit != null && SkillWalkIntoRange(k, _armedLevel, unit)) { Disarm(); return true; }
                Debug.Log($"[Skill] cast {k.Info.Aegis} lv {_armedLevel} on {target}");
                W3WalkCancel("skill");
                Session.UseSkill(k.Id, _armedLevel, target);
            }
            Disarm();
            return true;
        }

        /// <summary>
        /// ZC_AUTORUN_SKILL 0x0147 (39 bytes): id.W inf.L lv.W sp.W range.W name[24] up.B. The itemskill script only arms
        /// sd->skillitem; the client casts with the normal packets (self skills at once with our own AID, ground/target skills
        /// armed like a skill from the wrist menu). The level is forced to the item's by the server anyway.
        /// </summary>
        private void OnAutorunSkill(ref PacketReader r)
        {
            r.Skip(2); int sid = r.U16(); int inf = r.S32(); int lv = r.U16(); int sp = r.U16(); int range = r.U16();
            var k = new Progression.SkillEntry { Id = sid, Inf = inf, Level = lv, Sp = sp, Range = range };
            Debug.Log($"[Skill] item skill {k.Info.Aegis} lv {lv} (inf 0x{inf:x})");
            if (k.SelfCast || inf == 0) { Session.UseSkill(sid, lv, Session.AccountId); return; }
            _armed = k; _armedLevel = lv;
            if (Target != null) Target.ShowGroundAim = k.Ground;
            Hud?.SetArmed(W4LangArmedText(k, lv)); // w4lang: one whole sentence per aim (LokiGame.W4Lang.cs)
        }

        /// <summary>Trigger pulled with the laser on the ground or on nothing: armed skill → gaze lock → walk.</summary>
        private void OnFreeTrigger(Vector2Int? cell)
        {
            _freeExact = cell.HasValue ? _boot.Rig?.PointedLocal : null; // free movement (wfree): the laser's exact point (LokiGame.W3Walk.cs W3WalkStart)
            try { OnFreeTriggerOn(cell); } finally { _freeExact = null; }
        }

        private void OnFreeTriggerOn(Vector2Int? cell)
        {
            if (W8HordeBlocksWorld) return; // client wave 8 (w8game): the Horde arena is shown: no click reaches the RO world (LokiGame.W8Horde.cs)
            if (Session == null || Session.State != RoSession.Stage.InGame || Player.Dead || Entities.Self == null) { if (cell.HasValue) OnCellClicked(cell.Value); return; }
            if (_armed != null) { CastArmed(VR.Comfort.VisionAimSpells ? W12AimedUnit() : Target?.Locked?.Unit, _armed.Ground ? (VR.Comfort.VisionAimSpells ? W12AimedCell() : (cell ?? Target?.GazeCell)) : null); return; }
            // client wave 19 (w19small), point 39 (design note: "me looking at an NPC should not take precedence over me walking towards where I am
            // aiming"): with "Laser walking" On, the laser's ground cell wins over the look's target; the look acts only when the laser is on nothing
            if (cell.HasValue && VR.Comfort.LaserWalk) { OnCellClicked(cell.Value); return; }
            if (Target != null && Target.Locked != null) { OnTargetClicked(Target.Locked); return; }
            if (cell.HasValue && W14LaserWalks(cell.Value)) OnCellClicked(cell.Value); // (client wave 14, w14laser, point 20: "Laser walking" off: no walk)
        }

        private void OnTargetClicked(Targetable t)
        {
            if (W8HordeBlocksWorld) return; // client wave 8 (w8game): the Horde arena is shown (LokiGame.W8Horde.cs)
            if (W18TutTargetClicked(t)) return; // client wave 18 (w18tut): the tutorial's practice Poring is the client's alone: never sent (LokiGame.W18Tutorial.cs)
            if (Session == null || Session.State != RoSession.Stage.InGame || Player.Dead || Entities.Self == null) return;
            if (_armed != null && t.Unit != null) { CastArmed(VR.Comfort.VisionAimSpells ? (W12AimedUnit() ?? t.Unit) : t.Unit, null); return; }
            CancelPendingSkill("another target"); // (LokiGame.SkillRange.cs)
            var self = Entities.Self.Cell;
            if (t.Item != null)
            {
                var it = t.Item;
                if (Chebyshev(self, it.Cell) <= 2f || Vector2.Distance(Entities.Self.DrawnCell, it.Cell) <= 2.5f) { Session.TakeItem(it.Id); return; }
                _pendingPickup = it.Id; _pendingNpc = 0; _pendingUntil = Time.time + 10f;
                W3WalkStart(it.Cell, "item");
                return;
            }
            var u = t.Unit;
            if (u == null) return;
            if (Companions != null && Companions.CaptureMode && u.Kind == Unit.UnitKind.Monster) { TryCapture(u); return; }
            bool hostile = IsHostilePlayer(u);
            if (u.Kind != Unit.UnitKind.Monster && !hostile) _attackTarget = 0;
            _pendingCapture = 0;
            switch (u.Kind)
            {
                case Unit.UnitKind.Monster:
                case Unit.UnitKind.Player when hostile:
                    _pendingPickup = _pendingNpc = 0;
                    _attackTarget = u.Id; _nextChase = 0;
                    if (Target != null && Target.Steer.Settling && Entities.Self != null) Target.Steer.CancelSettle(Entities.Self, "attack");
                    W3WalkCancel("attack");
                    W5MoveAttack(u); // the homunculus and the mercenary join in when the monster turns on us (their AI, w4units); behind a wall the chase walks round first (w5move)
                    break;
                case Unit.UnitKind.Companion when Companions != null && Companions.Owns(u.Id):
                    _pendingPickup = _pendingNpc = 0;
                    Companions.Show(Companions.PageOf(u.Id)); // + the elemental's page (w4units)
                    break;
                case Unit.UnitKind.Npc:
                    if (Dialog.IsOpen) { Hud?.Chat.Add(Lang.T("Finish the current conversation first."), "ffb070"); return; }
                    if (Chebyshev(self, u.Cell) <= 13) { Session.ContactNpc(u.Id); return; }
                    _pendingNpc = u.Id; _pendingPickup = 0; _pendingUntil = Time.time + 15f;
                    var near = NearestWalkable(Vector2Int.RoundToInt(u.Cell), Vector2Int.RoundToInt(self));
                    W3WalkStart(near, "npc");
                    break;
                case Unit.UnitKind.Warp:
                    _pendingPickup = _pendingNpc = 0;
                    var wc = Vector2Int.RoundToInt(u.Cell);
                    if (!_boot.Map.Current.Gat.InBounds(wc.x, wc.y) || !_boot.Map.Current.Gat.Get(wc.x, wc.y).Walkable) wc = NearestWalkable(wc, Vector2Int.RoundToInt(self));
                    W3WalkStart(wc, "portal");
                    break;
                case Unit.UnitKind.Player when Market != null && (Market.HasShop(u.Id) || Market.HasBuyingStore(u.Id)):
                    _pendingPickup = _pendingNpc = 0;
                    if (Chebyshev(self, u.Cell) > 13) { Chat(Lang.T("Get closer to that shop."), "ffb070"); var nw = NearestWalkable(Vector2Int.RoundToInt(u.Cell), Vector2Int.RoundToInt(self)); W3WalkStart(nw, "shop"); break; }
                    if (Market.HasShop(u.Id)) Market.OpenShop(u.Id); else Market.OpenBuyingStore(u.Id);
                    break;
                case Unit.UnitKind.Player when PlayerMenu != null:
                    _pendingPickup = _pendingNpc = 0;
                    PlayerMenu.Open(u); // whisper, trade, party / guild / friend, equipment view, walk (the official right-click menu)
                    break;
                default:
                    _pendingPickup = _pendingNpc = 0;
                    var w = NearestWalkable(Vector2Int.RoundToInt(u.Cell), Vector2Int.RoundToInt(self));
                    W3WalkStart(w, "unit");
                    break;
            }
        }

        /// <summary>Taming: the server refuses a monster more than pet_distance_check (5) cells away (pet.cpp:1241-1357), so walk up to it first.</summary>
        private void TryCapture(Unit u)
        {
            _pendingPickup = _pendingNpc = 0; _attackTarget = 0;
            if (Chebyshev(Entities.Self.Cell, u.Cell) <= 4f) { _pendingCapture = 0; Companions.Capture(u.Id); return; }
            _pendingCapture = u.Id; _pendingUntil = Time.time + 12f;
            var w = NearestWalkable(Vector2Int.RoundToInt(u.Cell), Vector2Int.RoundToInt(Entities.Self.Cell));
            W3WalkStart(w, "capture");
            Chat(Lang.T("Getting closer to tame it…"), "c8cbd6");
        }

        /// <summary>Wave 28 (item 74): celebratory animation and fanfare when pet capture (0x01a0 ZC_TRYCAPTURE_MONSTER) succeeds.</summary>
        private void OnPetCaptureResult(bool ok)
        {
            if (!ok)
            {
                Hud?.Notify(Lang.T("The taming failed."));
                return;
            }
            var self = Entities?.Self;
            Audio?.PlayPetCaptureSuccess(self?.Cell);
            if (Session != null) Effects?.MiscEffect(Session.AccountId, 0);
            if (self != null && _boot?.Map != null)
            {
                var pos = self.transform.localPosition + Vector3.up * 0.9f;
                Fx.Burst(_boot.Map.transform, pos, new Color(1f, 0.9f, 0.35f), 2.0f, 44);
                Fx.Burst(_boot.Map.transform, pos, new Color(0.55f, 1f, 0.65f), 1.5f, 28);
                Fx.Ring(_boot.Map.transform, self.transform.localPosition + Vector3.up * 0.08f, 1.4f, new Color(1f, 0.88f, 0.35f), 1.5f, "pet-capture");
                self.Overhead?.PopNumber(Lang.T("TAMED!"), new Color(1f, 0.92f, 0.35f));
            }
            if (Session != null) Emotes?.ShowOn(Session.AccountId, 21);
            Hud?.Notify(Lang.T("Pet captured! Its egg is in your inventory."), 4f);
            VR.Haptics.Both(VR.Haptics.Kind.LevelUp, 0.75f, 0.35f);
        }

        private void UpdatePending()
        {
            UpdatePendingSkill(); // a walk into a skill's range, then the cast (LokiGame.SkillRange.cs)
            if (_pendingCapture != 0 && Entities.Self != null)
            {
                var m = Entities.Get(_pendingCapture);
                if ((m == null && !W3WalkActive) || Time.time > _pendingUntil || Companions == null || !Companions.CaptureMode) _pendingCapture = 0; // out of view during a far walk: wait (w3walk)
                else if (m != null && Chebyshev(Entities.Self.Cell, m.Cell) <= 4f) { _pendingCapture = 0; Companions.Capture(m.Id); }
                else if (m != null && !Entities.Self.Walking && !W3WalkActive) { var w = NearestWalkable(Vector2Int.RoundToInt(m.Cell), Vector2Int.RoundToInt(Entities.Self.Cell)); W3WalkStart(w, "capture"); }
            }
            if ((_pendingPickup == 0 && _pendingNpc == 0) || Entities.Self == null) return;
            if (Time.time > _pendingUntil && !W3WalkActive) { _pendingPickup = _pendingNpc = 0; return; } // a far walk keeps them (w3walk)
            var self = Entities.Self;
            if (_pendingPickup != 0)
            {
                if (!Ground.Items.TryGetValue(_pendingPickup, out var it) || it == null) { if (!W3WalkActive) _pendingPickup = 0; return; } // out of view while a far walk goes round: wait (w3walk)
                if (Chebyshev(self.Cell, it.Cell) <= 2f || Vector2.Distance(self.DrawnCell, it.Cell) <= 2.2f) { Session.TakeItem(it.Id); _pendingPickup = 0; }
            }
            else if (_pendingNpc != 0)
            {
                var u = Entities.Get(_pendingNpc);
                if (u == null) { if (!W3WalkActive) _pendingNpc = 0; return; } // out of view while a far walk goes round: wait (w3walk)
                if (Chebyshev(self.Cell, u.Cell) <= 13) { Session.ContactNpc(u.Id); _pendingNpc = 0; }
            }
        }

        private static float Chebyshev(Vector2 a, Vector2 b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

        /// <summary>The walkable cell closest to `target` (preferring ones nearer to `from`), searching outwards up to 5 rings.</summary>
        private Vector2Int NearestWalkable(Vector2Int target, Vector2Int from)
        {
            var gat = _boot.Map.Current.Gat;
            for (int r = 0; r <= 5; r++)
            {
                Vector2Int best = target; float bestD = float.MaxValue; bool found = false;
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                        int x = target.x + dx, y = target.y + dy;
                        if (!gat.InBounds(x, y) || !gat.Get(x, y).Walkable) continue;
                        float d = Vector2.Distance(new Vector2(x, y), from);
                        if (d < bestD) { bestD = d; best = new Vector2Int(x, y); found = true; }
                    }
                if (found) return best;
            }
            return target;
        }

        // ---------------------------------------------------------------- combat feedback: per hit on the HitScheduler (LokiGame.W3Combat.cs, client wave 3)

        // ---------------------------------------------------------------- packets
        private void OnMapPacket(RoSession s, ushort id, byte[] b, int o, int n)
        {
            W3WalkPeek(id, b, o, n); // client wave 3b, w3walk: our own walks, stops and map changes (LokiGame.W3Walk.cs)
            W4FxPeek(id, b, o, n); // client wave 4, w4fx: 0x01d7 on a ground skill unit (LokiGame.W4Fx.cs)
            W4WorldPeek(id, b, o, n); if (HandleW4World(id, b, o, n)) return; // client wave 4 (w4world): 0x0446 x/y, the weather ids of 0x01f3 / 0x0284 (LokiGame.W4World.cs)
            if (Entities.Handle(id, b, o, n)) return;
            if (Player.Handle(id, b, o, n)) return;
            if (Cart != null && Cart.Handle(id, b, o, n)) return;       // before Inventory: 0x0b08/09/39/0b with invType 1 are the cart
            if (Storage != null && Storage.Handle(id, b, o, n)) return; // before Inventory: 0x0b08/09/39/0b with invType 2 are storage
            if (Inventory.Handle(id, b, o, n)) return;
            if (Quests.Handle(id, b, o, n)) return;
            if (Dialog.Handle(id, b, o, n)) { LogDialog(id); return; }
            if (Ground.Handle(id, b, o, n)) return;
            if (Prog.Handle(id, b, o, n)) return;
            if (Combat.Handle(id, b, o, n)) return;
            if (Refinery != null && Refinery.Handle(id, b, o, n)) return; // before Shops: it peeks at 0x00ca (refine UI short of zeny) without consuming it
            if (Crafting != null && Crafting.Handle(id, b, o, n)) return;
            if (Shops != null && Shops.Handle(id, b, o, n)) return;
            if (Menus != null && Menus.Handle(id, b, o, n)) return;
            if (Emotes != null && Emotes.Handle(id, b, o, n)) return;
            if (HandleWcxp(id, b, o, n)) return; // wave 28 (wcxp): the Codex bounty NPC's lines to us (0x02c1 "LCX1 ..."), out of the chat (LokiGame.Wcxp.cs)
            if (Social != null && Social.Handle(id, b, o, n)) return;
            if (Party != null && Party.Handle(id, b, o, n)) return;
            if (Guild != null && Guild.Handle(id, b, o, n)) return;
            if (id == 0x01a0 && n >= 3) OnPetCaptureResult(b[o + 2] == 1); // Wave 28 (item 74): pet capture animation and fanfare
            if (Companions != null && Companions.Handle(id, b, o, n)) return; // peeks at 0x0291 (mercenary contract over / killed) without consuming it
            if (Mail != null && Mail.Handle(id, b, o, n)) return;
            if (Achievements != null && Achievements.Handle(id, b, o, n)) return;
            if (Bank != null && Bank.Handle(id, b, o, n)) return;           // 0x0ae2 type 0 (Refinery passes it on)
            if (CheckIn != null && CheckIn.Handle(id, b, o, n)) return;     // 0x0ae2 type 7
            if (CashShop != null && CashShop.Handle(id, b, o, n)) return;
            if (Swap != null && Swap.Handle(id, b, o, n)) return;
            if (Trade != null && Trade.Handle(id, b, o, n)) return;
            if (Market != null && Market.Handle(id, b, o, n)) return;
            if (Hotkeys != null && Hotkeys.Handle(id, b, o, n)) return;
            if (Nav != null && Nav.Handle(id, b, o, n)) return;
            if (HandleWave1(id, b, o, n)) return; // progress bars, skill menus, equipment view, ammunition messages
            if (HandleWave2(id, b, o, n)) return; // client wave 2 (LokiGame.Wave2.cs)
            if (HandleW3Social(id, b, o, n)) return; // client wave 3: 0x0215, 0x07fd, 0x0b67, MVP 0x010a / 0x010c / 0x010d (LokiGame.W3Social.cs)
            if (HandleW4Items(id, b, o, n)) return; // client wave 4: the roulette 0x0a1a / 0x0a1c / 0x0a20 / 0x0a22 (LokiGame.W4Items.cs)
            if (HandleW4Social(id, b, o, n)) return; // client wave 4: channels, clans, PvP, /who, rankings, manner, Convex Mirror (LokiGame.W4Social.cs)
            if (HandleW4Units(id, b, o, n)) return; // client wave 4: 0x0a3b hat effects (LokiGame.W4Units.cs)
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x0acc: // ZC_NOTIFY_EXP2 aid.L amount.Q(signed) varId.W(1 base, 2 job) type.W(1 quest) — research/qol_packets.md §1
                {
                    r.Skip(2); uint aid = r.U32(); long amount = r.S64(); int var = r.U16(); int type = r.U16();
                    if (aid != Session.AccountId) break;
                    bool job = var == 2; long next = job ? Player.NextJobExp : Player.NextBaseExp;
                    var col = amount < 0 ? new Color(1f, 0.45f, 0.4f) : type == 1 ? new Color(0.85f, 0.55f, 1f) : job ? new Color(1f, 0.9f, 0.45f) : new Color(0.55f, 0.85f, 1f);
                    string sign = amount >= 0 ? "+" : "";
                    Entities.Self?.Overhead?.PopNumber(job ? Lang.F("{0}{1:N0} Job", sign, amount) : Lang.F("{0}{1:N0} Base", sign, amount), col);
                    Hud?.Chat.Add(W4LangExpLine(sign, amount, job, next > 0 ? amount * 100.0 / next : (double?)null, type == 1), amount < 0 ? "ff7a6a" : type == 1 ? "d9a0ff" : job ? "ffe070" : "9ad1ff"); // w4lang: whole lines (LokiGame.W4Lang.cs)
                    LastExp = (var, amount, type);
                    break;
                }
                case 0x0091: // ZC_NPCACK_MAPMOVE: map[16], x, y
                {
                    r.Skip(2); string map = StripExt(r.Str(16)); int x = r.U16(), y = r.U16();
                    Debug.Log($"[Game] map change → {map} {x},{y}");
                    EnterMap(map, x, y, 4);
                    Session.SendPacket(new PacketWriter(0x007d).ToArray()); // LoadEndAck for the new map
                    Hud?.Chat.Add(Lang.F("Entered {0}.", map), "9ad1ff");
                    break;
                }
                case 0x0139: // ZC_ATTACK_FAILURE_FOR_DISTANCE: targetAID.L targetX.W targetY.W x.W y.W range.W — walk in range (in legs when far), then ask again
                {
                    r.Skip(2); uint target = r.U32(); int tx = r.S16(), ty = r.S16(), sx = r.S16(), sy = r.S16(), range = r.S16();
                    W5MoveTooFar(target, tx, ty, sx, sy, range); // client wave 5, w5move: the attack chase (LokiGame.W5Move.cs, Game/AttackChase.cs)
                    break;
                }
                case 0x008e: { r.Skip(4); Chat(r.ChatLine(n - 4), null); break; }                               // ZC_NOTIFY_PLAYERCHAT (Social handles it when built; kept for the offline demo)
                case 0x008d: { r.Skip(4); r.Skip(4); Chat(r.ChatLine(n - 8), "e8e8e8"); break; }               // ZC_NOTIFY_CHAT: GID, "Name : text"
                case 0x009a: // ZC_BROADCAST: a "blue" prefix = blue text, "ssss" = a War of Emperium broadcast (clif.cpp:6724-6744)
                {
                    r.Skip(4); string t = r.Text(n - 4); string col = "ffe066";
                    if (t.StartsWith("blue")) { t = t.Substring(4); col = "8ad6ff"; }
                    else if (t.StartsWith("ssss")) { t = t.Substring(4); col = "ffb070"; Hud?.Notify(t, 6f); }
                    Chat(t, col); break;
                }
                case 0x01c3: { r.Skip(4); uint col = r.U32(); r.Skip(8); Chat(r.Text(n - 16), (col & 0xFFFFFF).ToString("x6")); break; } // ZC_BROADCAST2
                case 0x02c1: { r.Skip(4); r.Skip(4); uint bgr = r.U32(); Chat(r.Text(n - 12), Bgr(bgr)); break; }                   // NPC talk (clif_messagecolor_target, BGR)
                case 0x09de: { r.Skip(4); r.Skip(4); string from = r.Str(24); r.Skip(1); Chat(Lang.F("[from {0}] {1}", from, r.Text(n - 33)), "ffb3ff"); break; } // ZC_WHISPER
                case 0x0291: { r.Skip(2); int mid = r.U16(); Debug.Log($"[Msg] #{mid}"); Chat(ClientText.Msg(mid), "c8cbd6"); break; }      // ZC_MSG: 0-based msgstringtable id
                case 0x02d9: // ZC_CONFIG type.L flag.L — types 2/3 went to Companions; 5 = hide my costumes (clif.cpp:17755-17757)
                {
                    r.Skip(2); int type = r.S32(); bool on = r.S32() != 0;
                    if (type == 5) { CostumesHidden = on; PlayerPrefs.SetInt("loki.hidecostumes." + (Session.SelectedChar?.Name ?? ""), on ? 1 : 0); Debug.Log($"[Costume] hidden {on}"); Hud?.Wrist.Toast(on ? Lang.T("Your costumes are hidden from everyone.") : Lang.T("Your costumes are shown.")); if (ComfortUi != null && ComfortUi.Visible) ComfortUi.Refresh(); } // (client wave 14: the Graphics row shows it)
                    else Debug.Log($"[Config] type {type} = {on}");
                    break;
                }
                case 0x09cd: { r.Skip(2); int mid = r.U16(); uint bgr = r.U32(); Debug.Log($"[Msg] #{mid}"); Chat(ClientText.Msg(mid), Bgr(bgr)); break; } // ZC_MSG_COLOR (BGR)
                case 0x07e2: { r.Skip(2); int mid = r.U16(); int val = r.S32(); Chat(ClientText.Fill(ClientText.Msg(mid), val.ToString()), "c8cbd6"); break; } // ZC_MSG_VALUE: value fills the %d
                case 0x07e6: { r.Skip(2); int sk = r.U16(); int mid = r.S32(); Chat($"[{SkillDb.Get(sk).Name}] {ClientText.Msg(mid)}", "cdcdff"); break; }     // ZC_MSG_SKILL
                case 0x0192: // ZC_UPDATE_MAPINFO x.W y.W type.W map[16]: a cell changed type — WoE SE barricades (setwall), Ice Wall (clif.cpp:5365-5386)
                {
                    r.Skip(2); int cx = r.S16(), cy = r.S16(), ctype = r.S16(); string cmap = StripExt(r.Str(16));
                    var cur = _boot.Map.Current; var g = cur?.Gat;
                    if (g != null && string.Equals(cmap, cur.Name, System.StringComparison.OrdinalIgnoreCase) && g.InBounds(cx, cy) && ctype >= 0 && ctype <= 6)
                        g.Cells[cx + cy * g.Width].Type = (Loki.Formats.GatFile.CellType)ctype;
                    break;
                }
                case 0x0189: // ZC_NOTIFY_MAPINFO type.W (clif.cpp:6306-6321)
                {
                    r.Skip(2); int t = r.U16();
                    string m = t == 0 ? Lang.T("You can't teleport in this area.") : t == 1 ? Lang.T("This save point can't be memorized.") : t == 2 ? Lang.T("That skill can't be used in this area.") : Lang.T("That item can't be used in this area.");
                    Chat(m, t == 1 ? "ff7a6a" : "ffff66"); Hud?.Notify(m);
                    break;
                }
                case 0x0147: OnAutorunSkill(ref r); break; // ZC_AUTORUN_SKILL: an item (Fly Wing, Magnifier, scrolls…) lets us cast a skill
                case 0x07fe: { r.Skip(2); string bgm = r.Str(24); Debug.Log("[Game] NPC BGM " + bgm); Audio.PlayNamedBgm(bgm); break; }             // ZC_PLAY_NPC_BGM
                case 0x01d3: { r.Skip(2); string wav = r.Str(24); int act = r.U8(); r.Skip(4); uint aid = r.U32(); if (act == 0) Audio.PlayWav(wav, Entities.Get(aid)?.Cell); break; } // ZC_SOUND
                case 0x019b: { r.Skip(2); uint aid = r.U32(); uint eff = r.U32(); if (aid == Session.AccountId && eff == 1) Audio.PlayWav("effect\\st_job_level_up.wav"); if (!(aid == Session.AccountId && eff == 0 && CxpSuppressingLevelUpEffect)) Effects?.MiscEffect(aid, (int)eff); break; } // ZC_NOTIFY_EFFECT (0 base / 1 job level up)
                default:
                    if (_loggedIds.Add(id)) Debug.Log($"[Game] map packet 0x{id:x4} {PacketTable.NameOf(id)} ({n} bytes) [first of its kind]");
                    break;
            }
        }

        /// <summary>Chat for the item windows: their messages carry names with rich-text tags, which the chat log would show literally.</summary>
        private void ItemChat(string text, string color) => Chat(UiKit.StripTags(text), color);
        private void Chat(string text, string color)
        {
            Debug.Log("[Chat] " + text);
            Hud?.Chat.Add(text, color);
        }
        private static string Bgr(uint c) => ((c & 0xFF) << 16 | (c & 0xFF00) | (c >> 16) & 0xFF).ToString("x6");

        private void LogDialog(ushort id)
        {
        }

        /// <summary>The unit of a player in view by its exact name (the /trade console command; the smokes use it too).</summary>
        private Unit FindPlayer(string name)
        {
            foreach (var u in Entities.Units.Values) if (u != null && u.IsPlayerType && !u.IsSelf && u.Name == name) return u;
            return null;
        }


        /// <summary>Loki Online (SpacetimeDB): the login and character screens, and in the world the map link under Session.</summary>
        public Net.Stdb.StdbSession Stdb { get; private set; }

        /// <summary>A character picked on the Loki Online character screen enters the world: Session speaks the map protocol over
        /// the SpacetimeDB link from here on, exactly as it would to rAthena's map server.</summary>
        private void EnterWorldOnline(CharInfo ch)
        {
            Debug.Log($"[Game] entering Loki Online as {ch.Name}");
            var link = Session.EnterWorldOnline(ch, Stdb.AccountId, Stdb.Sex, Stdb.SendPacket, Stdb.CloseLink);
            Stdb.AttachLink(link);
        }


        // The SpacetimeDB session outlives the scene: closing the map link (Session.Dispose) tells the server this character
        // left; the connection itself closes when Loki quits.
        private void OnDestroy() { Session?.Dispose(); Stdb?.DetachHandlers(); }
    }
}
