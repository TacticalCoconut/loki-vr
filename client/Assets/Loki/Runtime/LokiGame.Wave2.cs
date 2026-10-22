using System.Collections.Generic;
using Loki.Game;
using Loki.Net;
using Loki.Sprites;
using Loki.UI;
using Loki.World;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 2 (2026-09-27): social and shops the server side has (chat rooms, ignore list, whisper windows, party booking,
    /// NPC point shops, search store, reputation, stylist, laphine, item merge, marriage / adoption), item notices (favourites,
    /// rental / bound / damaged, inventory expansion, viewpoint marks), combat feedback (combos, spirit spheres, charms, soul
    /// energy, shields, turning in place), world rendering and the modes (instances, battlegrounds, duels). The smokes of each item
    /// are in LokiGame.Wave2Smoke.cs (-lokiSmokeOnly w2…).
    /// </summary>
    public sealed partial class LokiGame
    {
        public ChatRooms ChatRooms { get; private set; }
        public Whispers Whispers { get; private set; }
        public PartyBooking Booking { get; private set; }
        public PointShop PointShop { get; private set; }
        public SearchStore SearchStore { get; private set; }
        public Reputation Reputation { get; private set; }
        public Stylist Stylist { get; private set; }
        public Laphine Laphine { get; private set; }
        public ItemMerge Merge { get; private set; }
        public Family Family { get; private set; }
        public ViewPoints ViewPoints { get; private set; }
        private uint _w2Cid;

        /// <summary>The server refuses walking while we sit in a chat room (pc_cant_act): the stick and the laser say so instead.</summary>
        private bool Wave2BlocksWalking => ChatRooms != null && ChatRooms.InRoom;

        private void BeginWave2(LokiBootstrap boot)
        {
            System.Func<Camera> head = () => boot.Rig.Head;
            System.Action<string, string> chat = (t, c) => Chat(t, c);
            System.Action<string> toast = m => Hud.Wrist.Toast(m);
            System.Func<string> me = () => Session.SelectedChar?.Name ?? "";
            ListWindow Win(string name) { var w = ListWindow.Create(boot.Rig.transform, name); w.RequestKeyboard = f => { Keyboard.Attach(f); Keyboard.Show(); }; return w; }

            ViewPoints = new ViewPoints(() => _boot.Map);
            if (MiniMap != null) MiniMap.MarkCells = ViewPoints.Cells;
            ChatRooms = new ChatRooms(Session, Entities, Win("ChatRoomWindow"), head, me, chat, line => Social.Say(line)) { Toast = toast };
            Social.AreaLine += (gid, line) => ChatRooms.OnAreaLine(line);
            Social.OwnLine += line => ChatRooms.OnOwnLine(line);
            Whispers = new Whispers(Session, Social, Win("WhisperWindow"), head, chat) { Toast = toast };
            Booking = new PartyBooking(Session, Win("BookingWindow"), head, chat, me, () => Player.BaseLevel, () => Entities.Self != null ? Entities.Self.Job : Session.SelectedChar?.Job ?? 0) { Toast = toast };
            Booking.OpenWhisper = n => Whispers.OpenChat(n);
            PointShop = new PointShop(Session, Win("PointShopWindow"), head, (t, c) => ItemChat(t, c), () => Dialog.DismissLocal());
            SearchStore = new SearchStore(Session, Win("SearchStoreWindow"), head, (t, c) => ItemChat(t, c));
            SearchStore.MarkShop = (x, y) => ViewPoints.Add(0xFFFFFFFF, 0, x, y, new Color(1f, 0.85f, 0.3f), 60f);
            Reputation = new Reputation(Win("ReputationWindow"), head, chat);
            Stylist = new Stylist(Session, Entities, Inventory, Win("StylistWindow"), head, () => Player.Zeny, () => Entities.Self != null ? Entities.Self.Job : Session.SelectedChar?.Job ?? 0, chat);
            Laphine = new Laphine(Session, Inventory, Win("LaphineWindow"), head, (t, c) => ItemChat(t, c));
            Merge = new ItemMerge(Session, Inventory, Win("MergeWindow"), head, (t, c) => ItemChat(t, c));
            Family = new Family(Session, Inventory, chat) { Toast = toast };
            // ZC_CONGRATULATION: rAthena leaves the look to the client; Loki plays EF_COLORPAPER (the effect table gives it the wedding sound)
            Family.WeddingEffect = gid => { Effects?.Spam("347", gid, null, Time.time, -1f, gid); var u = Entities.Get(gid); if (u != null) Fx.Burst(_boot.Map.transform, u.transform.localPosition + Vector3.up * 1.5f, new Color(1f, 0.75f, 0.85f), 1.6f, 40); };

            // the player menu: join their chat room, block / allow their whispers, open a whisper window, adopt them
            PlayerMenu.Extra = (u, add) =>
            {
                if (u == null) return;
                var room = ChatRooms.RoomOf(u.Id);
                if (room != null && room.Clickable) add(Lang.F("Join their chat room '{0}'", room.Title), Lang.T("Join room"), () => ChatRooms.AskJoin(room));
                string nm = u.Name ?? "";
                add(Lang.T("Keep a whisper window with them"), Lang.T("Whispers"), () => Whispers.OpenChat(nm));
                if (Whispers.IsIgnored(nm)) add(Lang.T("You block their whispers"), Lang.T("~whisper~Allow"), () => Whispers.Allow(nm));
                else add(Lang.T("Block their whispers"), Lang.T("Block"), () => Whispers.Block(nm));
                if (Family.Married && Family.Adoptable(u.Job)) add(Lang.T("Ask to adopt them (both parents: rings on, level 70+, one party)"), Lang.T("Adopt"), () => Family.AskAdopt(u.Id));
            };

            // the wrist menu's fifth row (w4lang: plurals and clauses are whole sentences; the lines of a summary are separate texts)
            Hud.Wrist.SetHub(WristMenu.Tab.Community, () => ChatRooms.InRoom ? Lang.F("Chat room  <color=#ffd060>{0}</color>", ChatRooms.MyTitle) : Lang.T("Chat rooms & whispers"),
                () => (ChatRooms.InRoom ? (ChatRooms.Members.Count == 2 ? Lang.F("You are in '{0}' with 1 other.", ChatRooms.MyTitle) : Lang.F("You are in '{0}' with {1} others.", ChatRooms.MyTitle, ChatRooms.Members.Count - 1)) + "\n" + Lang.T("What you say goes to the room only; you can't walk until you leave.") + "\n\n" : (ChatRooms.Rooms.Count == 1 ? Lang.T("1 chat room in sight — point the laser at a room's sign to join.") : Lang.F("{0} chat rooms in sight — point the laser at a room's sign to join.", ChatRooms.Rooms.Count)) + "\n\n")
                      + (Whispers.Unread > 0 ? (Whispers.Unread == 1 ? Lang.T("<color=#ffd060>1 unread whisper</color>") : Lang.F("<color=#ffd060>{0} unread whispers</color>", Whispers.Unread)) + "\n" : (Whispers.Chats.Count == 1 ? Lang.T("1 whisper conversation") : Lang.F("{0} whisper conversations", Whispers.Chats.Count)) + "\n")
                      + (Whispers.BlockAll ? Lang.T("You block every whisper.") + "\n" : Whispers.Ignored.Count > 0 ? (Whispers.Ignored.Count == 1 ? Lang.T("You block 1 player.") : Lang.F("You block {0} players.", Whispers.Ignored.Count)) + "\n" : "")
                      + W4SocialCommunityInfo(), // client wave 4: chat channels and the clan (LokiGame.W4Social.cs)
                new WristMenu.HubButton(() => ChatRooms.InRoom ? Lang.T("My chat room") : Lang.T("Chat rooms"), () => ChatRooms.Show()),
                new WristMenu.HubButton(Lang.T("Create a room"), () => ChatRooms.ShowCreate(), () => !ChatRooms.InRoom),
                new WristMenu.HubButton(Lang.T("Leave the room"), () => ChatRooms.Leave(), () => ChatRooms.InRoom),
                new WristMenu.HubButton(() => Whispers.Unread > 0 ? Lang.F("Whispers ({0})", Whispers.Unread) : Lang.T("Whispers"), () => Whispers.Show()),
                new WristMenu.HubButton(Lang.T("Ignore list"), () => Whispers.ShowIgnore()),
                new WristMenu.HubButton(Lang.T("Chat channels"), () => W4SocialChannels?.Show()), // client wave 4 (LokiGame.W4Social.cs)
                new WristMenu.HubButton(Lang.T("Clan chat"), () => Hud.Wrist.PrefillChat("/cl "), () => W4SocialClan != null && W4SocialClan.InClan));
            Hud.Wrist.SetHub(WristMenu.Tab.Booking, () => Lang.T("Party booking"),
                () => (Booking.Mine != null ? Lang.F("Your ad is up: Lv {0}, wants {1}.", Booking.Mine.Level, PartyBooking.JobList(Booking.Mine.Jobs)) + "\n" : Lang.T("You have no ad up.") + "\n")
                      + (Booking.JoinRequest.HasValue ? "\n" + Lang.F("<color=#ffd060>{0} asks to join your party.</color>", Booking.JoinRequest.Value.name) + "\n" : "")
                      + (Booking.LastJoinAnswer.Length > 0 ? "\n" + Booking.LastJoinAnswer + "\n" : "")
                      + "\n" + Lang.T("Who looks for a party, and for which jobs. Whisper to talk."),
                new WristMenu.HubButton(Lang.T("Find a party"), () => Booking.ShowSearch()),
                new WristMenu.HubButton(() => Booking.Mine != null ? Lang.T("My ad") : Lang.T("Advertise"), () => Booking.ShowMine()),
                new WristMenu.HubButton(Lang.T("Let them join"), () => Booking.AnswerJoin(true), () => Booking.JoinRequest.HasValue),
                new WristMenu.HubButton(Lang.T("Turn them down"), () => Booking.AnswerJoin(false), () => Booking.JoinRequest.HasValue));
            Hud.Wrist.SetHub(WristMenu.Tab.Family, () => Lang.T("More"),
                () => (Family.Married ? (Family.WearsRing ? Lang.F("Married to <color=#ffd060>{0}</color>", Family.Partner.Length > 0 ? Family.Partner : Lang.F("(char {0})", Family.PartnerCid))
                                                          : Lang.F("Married to <color=#ffd060>{0}</color> — wear the ring for the couple skills", Family.Partner.Length > 0 ? Family.Partner : Lang.F("(char {0})", Family.PartnerCid))) + "\n" : Lang.T("Not married.") + "\n")
                      + (Family.AdoptOffer.HasValue ? "\n" + Lang.F("<color=#ffd060>{0} and their partner want to adopt you.</color> You would become a baby class for good.", Family.AdoptOffer.Value.name) + "\n" : "")
                      + (Reputation.Received ? "\n" + Lang.F("Reputation: {0}", string.Join(", ", new List<long>(Reputation.Points.Keys).ConvertAll(k => $"{Reputation.NameOf(k)} {Reputation.Points[k]:N0}"))) + "\n" : ""),
                new WristMenu.HubButton(Lang.T("Accept adoption"), () => Family.AnswerAdopt(true), () => Family.AdoptOffer.HasValue),
                new WristMenu.HubButton(Lang.T("Decline adoption"), () => Family.AnswerAdopt(false), () => Family.AdoptOffer.HasValue),
                new WristMenu.HubButton(Lang.T("Reputation"), () => Reputation.Show()),
                new WristMenu.HubButton(() => Lang.F("Expand inventory ({0}/200)", Inventory.Capacity), () => Expansion?.Request()));
            Whispers.Changed += () => Hud.Wrist.MarkDirty(); ChatRooms.Changed += () => Hud.Wrist.MarkDirty(); Booking.Changed += () => Hud.Wrist.MarkDirty(); Family.Changed += () => Hud.Wrist.MarkDirty();
            BeginWave2Items(boot);
            BeginWave2Combat(boot);
            BeginWave2Modes(boot);
        }

        private bool HandleWave2(ushort id, byte[] b, int o, int n)
        {
            // client wave 4 (w4items): the point shop no longer reads its currency from the 0x009a broadcast (Game/PointShop.cs, re-audit §3)
            if (ChatRooms != null && ChatRooms.Handle(id, b, o, n)) return true;
            if (Whispers != null && Whispers.Handle(id, b, o, n)) return true;
            if (Booking != null && Booking.Handle(id, b, o, n)) return true;
            if (PointShop != null && PointShop.Handle(id, b, o, n)) return true;
            if (SearchStore != null && SearchStore.Handle(id, b, o, n)) return true;
            if (Reputation != null && Reputation.Handle(id, b, o, n)) return true;
            if (Stylist != null && Stylist.Handle(id, b, o, n)) { Dialog?.DismissLocal(); return true; }
            if (Laphine != null && Laphine.Handle(id, b, o, n)) { Dialog?.DismissLocal(); return true; }
            if (Merge != null && Merge.Handle(id, b, o, n)) return true;
            if (Family != null && Family.Handle(id, b, o, n)) return true;
            if (ViewPoints != null && ViewPoints.Handle(id, b, o, n)) return true;
            if (HandleWave2Items(id, b, o, n)) return true;
            if (HandleWave2Combat(id, b, o, n)) return true;
            if (HandleWave2Modes(id, b, o, n)) return true;
            return false;
        }

        private void OnMapChangeWave2(string map)
        {
            uint cid = Session.SelectedChar?.CharId ?? 0;
            if (cid != _w2Cid) { _w2Cid = cid; Whispers?.ResetForCharacter(); Booking?.ResetForCharacter(); Reputation?.ResetForCharacter(); Family?.ResetForCharacter(); Modes?.ResetForCharacter(); }
            ChatRooms?.OnMapChange(); PointShop?.Reset(); SearchStore?.Reset(); Stylist?.Reset(); Laphine?.Reset(); Merge?.Reset(); ViewPoints?.Clear();
            OnMapChangeWave2Combat(map);
            OnMapChangeWave2Modes(map);
            OnMapChangeWave2World(map);
            // CZ_BLOCKING_PLAY_CANCEL 0x0447 after the map has loaded, as the official client (roBrowser MapEngine.js:769-771) does: rAthena
            // answers with ZC_NOTIFY_ACTORINIT and the memorial dungeon info of our party (clif.cpp:20000-20024, clif_instance_info)
            if (Session != null && Session.State == RoSession.Stage.InGame) StartCoroutine(SendLoadConfirm());
        }
        private System.Collections.IEnumerator SendLoadConfirm() { yield return new WaitForSeconds(0.5f); if (Session != null && Session.State == RoSession.Stage.InGame) Session.SendPacket(new PacketWriter(0x0447).ToArray()); }
    }
}
