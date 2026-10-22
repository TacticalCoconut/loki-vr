using System;
using Loki.Net;
using Loki.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.Game
{
    /// <summary>
    /// PvP, GvG and duel map modes (client wave 4, w4social; rAthena e985006, PACKETVER 20211103):
    ///   ZC_MAPPROPERTY_R2 0x099b type.W flags.L (clif_map_property clif.cpp:6873-6904; Guild.cs reads it and raises MapPropertyChanged):
    ///     type 0 nothing, 1 FREEPVPZONE (a PvP map, a duel, a GvG dungeon), 3 AGITZONE (WoE running on a GvG map), 4 PKSERVERZONE
    ///     (clif.hpp:365-374: the client says msgstringtable 855 "You are in a PK area…"; rAthena e985006 never sends it);
    ///     flags bit 0 PARTY (attack non-party players: a PvP map or a duel), 1 GUILD (attack non-guild players), 2 SIEGE (emblems over
    ///     heads), 3 USE_SIMPLE_EFFECT, 4 DISABLE_LOCKON, 5 COUNT_PK (the PvP counter: MF_PVP), 6 NO_PARTY_FORMATION, 7 BATTLEFIELD,
    ///     8 DISABLE_COSTUMEITEM, 9 USECART, 10 SUNMOONSTAR_MIRACLE. Bit 7 is "getMapFlag(MF_BATTLEGROUND) << 7" (clif.cpp:6897), and a
    ///     "battleground 2" map (npc/mapflag/battleground.txt) stores 2 there, which lands on bit 8: <see cref="Battlefield"/> also reads 0x01d6. Sent at every map load (clif.cpp:10828-10847) and to the whole map
    ///     when a script turns PvP / GvG on or off (map.cpp:4755-4800); a duel's own (duel.cpp:176, 243, 269) carries bit 0 without bit 5.
    ///   ZC_NOTIFY_MAPPROPERTY2 0x01d6 type.W (clif_map_type clif.cpp:6909-6916): e_map_type (clif.hpp:376-402); rAthena sends only
    ///     MAPTYPE_BATTLEFIELD 19 (on a battleground map at load, clif.cpp:11073).
    ///   ZC_NOTIFY_RANKING 0x019a aid.L rank.L total.L (clif_pvpset clif.cpp:6921-6946): our rank among the players on a PvP map (every
    ///     PVP_CALCRANK_INTERVAL 1000 ms, pc.cpp:77, 12758-12790; to the area, so others' ranks arrive too; rank UINT32_MAX = hidden "--";
    ///     rank 0 total 0 when PvP stops, map.cpp:4618).
    ///   ZC_ACK_PVPPOINT 0x0210 cid.L aid.L win.L lose.L point.L — the answer to /pvpinfo CZ_REQ_PVPPOINT 0x020f cid.L aid.L
    ///     (clif.cpp:15619-15644: clif_PVPInfo, clif_parse_PVPInfo), shown with msgstringtable 853 "You have won %d times and have lost %d times in PVP. Current points %d.".
    /// The counter (rank / total) floats above the off hand while on a PvP map; name colours are applied by LokiGame.W4Social.cs.
    /// </summary>
    public sealed class PvpState
    {
        public const int FlagParty = 1 << 0, FlagGuild = 1 << 1, FlagSiege = 1 << 2, FlagNoLockOn = 1 << 4, FlagCountPk = 1 << 5, FlagBattlefield = 1 << 7;
        public int Type { get; private set; }
        public int Flags { get; private set; }
        public int MapType { get; private set; } = -1;
        /// <summary>A PvP map (MF_PVP): the counter and every non-party player attackable.</summary>
        public bool PvpMap => Type == 1 && (Flags & FlagCountPk) != 0;
        /// <summary>In a duel on a map without PvP: bit 0 without COUNT_PK (only a duel sets that on a non-PvP map, clif.cpp:6890).</summary>
        public bool Duel => Type == 1 && (Flags & FlagParty) != 0 && (Flags & FlagCountPk) == 0;
        /// <summary>Other guilds' players are attackable (a GvG map during WoE, or a battlefield).</summary>
        public bool Gvg => (Flags & FlagGuild) != 0 && Type == 3;
        public bool Siege => (Flags & FlagSiege) != 0;
        public bool Battlefield => (Flags & FlagBattlefield) != 0 || MapType == 19;
        public int Rank { get; private set; } = -1;
        public int Total { get; private set; } = -1;
        public (int win, int lose, int points)? Points { get; private set; }
        public event Action Changed;

        private readonly RoSession _s; private readonly Func<uint> _aid, _cid; private readonly Action<string, string, ChatKind> _chat;
        public PvpState(RoSession s, Func<uint> aid, Func<uint> cid, Action<string, string, ChatKind> chat) { _s = s; _aid = aid; _cid = cid; _chat = chat; }

        public void OnMapChange() { MapType = -1; Rank = Total = -1; Changed?.Invoke(); }

        /// <summary>0x099b arrived (Guild.cs parsed it): the new type and flags.</summary>
        public void SetProperty(int type, int flags)
        {
            bool wasPvp = PvpMap, wasDuel = Duel;
            Type = type; Flags = flags;
            if (!PvpMap) { Rank = Total = -1; }
            if (PvpMap && !wasPvp) _chat(Lang.T("This is a PvP map: every player outside your party can attack you."), "ff8a5a", ChatKind.System);
            else if (!PvpMap && wasPvp) _chat(Lang.T("PvP is off here now."), "c8cbd6", ChatKind.System);
            if (type == 4) _chat(ClientText.Msg(855), "ff9b9b", ChatKind.System); // PKSERVERZONE, colour 0x9B9BFF (BGR) per clif.hpp:371
            Debug.Log($"[Pvp] map property type {type} flags 0x{flags:x}: pvp {PvpMap} duel {Duel} gvg {Gvg} siege {Siege} (was pvp {wasPvp} duel {wasDuel})");
            Changed?.Invoke();
        }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x01d6: { r.Skip(2); MapType = r.S16(); Debug.Log($"[Pvp] map type {MapType}"); Changed?.Invoke(); return true; }
                case 0x019a:
                {
                    r.Skip(2); uint aid = r.U32(); uint rank = r.U32(); int total = r.S32();
                    if (aid == _aid())
                    {
                        Rank = rank == uint.MaxValue ? -1 : (int)rank; Total = total;
                        if (rank == 0 && total == 0) Rank = Total = -1;
                        Debug.Log($"[Pvp] rank {(rank == uint.MaxValue ? "--" : rank.ToString())} / {total}");
                        Changed?.Invoke();
                    }
                    else Debug.Log($"[Pvp] rank of {aid}: {(rank == uint.MaxValue ? "--" : rank.ToString())} / {total}");
                    return true;
                }
                case 0x0210:
                {
                    r.Skip(2); r.U32(); r.U32(); int win = r.S32(), lose = r.S32(), pts = r.S32();
                    Points = (win, lose, pts);
                    string m = ClientText.Fill(ClientText.Fill(ClientText.Fill(ClientText.Msg(853), win.ToString()), lose.ToString()), pts.ToString());
                    _chat(m, "ffd060", ChatKind.System);
                    Changed?.Invoke(); return true;
                }
            }
            return false;
        }

        /// <summary>/pvpinfo: CZ_REQ_PVPPOINT 0x020f cid.L aid.L (the server answers for the sender only, clif.cpp:15638-15644).</summary>
        public void RequestInfo() => _s.SendPacket(new PacketWriter(0x020f).U32(_cid()).U32(_aid()).ToArray());
    }

    /// <summary>The PvP counter ("rank / total" while on a PvP map), floating just above the off hand like the buff bar.</summary>
    public sealed class PvpCounter : MonoBehaviour
    {
        private Transform _hand; private Camera _head; private PvpState _pvp; private Text _text; private Func<bool> _menuOpen;
        public string Shown => _text != null && gameObject.activeSelf ? _text.text : "";

        public static PvpCounter Create(Transform parent, Transform hand, Camera head, PvpState pvp, Func<bool> menuOpen)
        {
            var rt = UiKit.Panel("PvpCounter", parent, new Vector2(300, 70), 0.00042f, true, PanelKind.ByName, HudLook.LabelLook); if (HudLook.LabelLook == PanelLook.SeeThrough) HudLook.Backing(rt); // client wave 21 (w21kit: a parchment card; w21tools: see-through by one constant, UI/HudLook.cs LabelLook: flagged)
            UnityEngine.Object.Destroy(rt.GetComponent<BoxCollider>()); // never blocks the laser
            var c = rt.gameObject.AddComponent<PvpCounter>();
            c._hand = hand; c._head = head; c._pvp = pvp; c._menuOpen = menuOpen;
            c._text = UiKit.Label(rt, "", Vector2.zero, new Vector2(290, 64), 34, TextAnchor.MiddleCenter, new Color(1f, 0.55f, 0.45f));
            pvp.Changed += c.Refresh;
            c.Refresh();
            return c;
        }
        public void SetHand(Transform hand) { _hand = hand; }

        private void Refresh()
        {
            if (this == null) return;
            bool show = _pvp.PvpMap;
            gameObject.SetActive(show);
            if (show) _text.text = _pvp.Rank > 0 && _pvp.Total > 0 ? $"PvP  {_pvp.Rank} / {_pvp.Total}" : _pvp.Total > 0 ? $"PvP  -- / {_pvp.Total}" : "PvP";
        }

        private void LateUpdate()
        {
            if (_hand == null || _head == null) return;
            var toHead = _head.transform.position - _hand.position; toHead.y = 0; if (toHead.sqrMagnitude > 1e-4f) toHead.Normalize();
            bool open = _menuOpen != null && _menuOpen();
            transform.position = _hand.position + Vector3.up * (open ? 0.49f + UI.WristMenu.ClassicGrewTop : 0.13f) + toHead * 0.03f; // client wave 11: above the larger wrist menu
            UiKit.FaceHead(transform, _head);
        }
    }
}
