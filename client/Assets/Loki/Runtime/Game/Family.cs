using System;
using System.Collections.Generic;
using Loki.Net;
using Loki.Sprites;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Marriage, adoption and divorce (client wave 2; rAthena e985006 pc.cpp:1672-1769, 12811-12866, clif.cpp:9439-9517, 17565-17650,
    /// research/stylist_laphine_merge_mail_marriage.md §E). Marrying has no client packet in this build (ZC_REQ_COUPLE / ZC_START_COUPLE are
    /// commented out): NPC scripts (`marriage`) or @marry do it, and ZC_CONGRATULATION 0x01ea gid.L plays the wedding effect around the
    /// couple. Nothing names the partner at login: @marry's rings (2634 / 2635, card0 = 0x00FF) carry the partner's char id in
    /// card2 | card3 &lt;&lt; 16, resolved with CZ_REQNAME_BYGID2 0x0369 cid.L → ZC_ACK_REQNAME_BYGID 0x0af7 flag.W cid.L name[24];
    /// ZC_COUPLENAME 0x01e6 name[24] comes to the area when Call Partner is cast. ZC_DIVORCE 0x0205 name[24] (to both, only when both are online).
    /// Adoption: a married parent (both parents in one party with the child, rings worn, base level ≥ 70) sends CZ_REQ_JOIN_BABY 0x01f9 aid.L;
    /// the child gets ZC_REQ_BABY 0x01f6 fatherAid.L motherAid.L name[24] and answers CZ_JOIN_BABY 0x01f7 fatherAid.L motherAid.L accept.L
    /// (the server then turns it into a baby class; no success packet); ZC_BABYMSG 0x0216 type.L tells the parent why not (0 already a child,
    /// 1 level 70, 2 the child is married). Other refusals are silent.
    /// </summary>
    public sealed class Family
    {
        public const int RingMale = 2634, RingFemale = 2635;
        public uint PartnerCid { get; private set; }
        public string Partner { get; private set; } = "";
        /// <summary>An adoption offer waiting for our answer: (father aid, mother aid, name of the parent who asked).</summary>
        public (uint father, uint mother, string name)? AdoptOffer;
        public int LastBabyMsg = -1; public int Weddings; public uint LastWeddingGid;
        public string LastDivorce = "";
        public event Action Changed;
        public Action<string> Toast; public Action<uint> WeddingEffect;
        private readonly RoSession _s; private readonly Inventory _inv; private readonly Action<string, string> _chat;
        private uint _asked;

        public Family(RoSession s, Inventory inv, Action<string, string> chat)
        {
            _s = s; _inv = inv; _chat = chat;
            _inv.Changed += CheckRings;
        }

        public void ResetForCharacter() { PartnerCid = 0; Partner = ""; AdoptOffer = null; _asked = 0; Changed?.Invoke(); }

        /// <summary>@marry's forged ring names the partner (atcommand.cpp:6695-6713: card[0] CARD0_FORGE, card[2..3] the partner's char id).</summary>
        private void CheckRings()
        {
            uint cid = 0;
            foreach (var e in _inv.Items.Values)
                if ((e.ItemId == RingMale || e.ItemId == RingFemale) && e.Cards != null && e.Cards[0] == 0x00FF) { cid = (uint)(e.Cards[2] & 0xFFFF) | ((uint)(e.Cards[3] & 0xFFFF) << 16); if (cid != 0) break; }
            if (cid != 0 && cid != PartnerCid) { PartnerCid = cid; if (_asked != cid) { _asked = cid; _s.SendPacket(new PacketWriter(0x0369).U32(cid).ToArray()); Debug.Log($"[Family] ring names partner cid {cid}"); } }
            bool anyRing = false; foreach (var e in _inv.Items.Values) if (e.ItemId == RingMale || e.ItemId == RingFemale) { anyRing = true; break; }
            if (!anyRing && PartnerCid != 0 && LastDivorce.Length > 0) { PartnerCid = 0; Partner = ""; Changed?.Invoke(); }
        }
        public bool WearsRing { get { foreach (var e in _inv.Items.Values) if ((e.ItemId == RingMale || e.ItemId == RingFemale) && e.Wearing != 0) return true; return false; } }
        public bool Married => Partner.Length > 0 || PartnerCid != 0;

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x01ea:
                {
                    r.Skip(2); uint gid = r.U32(); Weddings++; LastWeddingGid = gid;
                    WeddingEffect?.Invoke(gid);
                    Debug.Log($"[Family] wedding effect on {gid}");
                    return true;
                }
                case 0x0af7:
                {
                    r.Skip(2); int flag = r.U16(); uint cid = r.U32(); string nm = r.Str(24);
                    if (cid == PartnerCid && nm.Length > 0) { Partner = nm; Debug.Log($"[Family] partner is {nm}"); Changed?.Invoke(); }
                    return false; // client wave 4: a peek — the ranking lists resolve their CIDs with the same answer (LokiGame.W4Social.cs consumes it)
                }
                case 0x01e6:
                {
                    r.Skip(2); string nm = r.Str(24);
                    Debug.Log($"[Family] couple name {nm}");
                    return true;
                }
                case 0x0205:
                {
                    r.Skip(2); string nm = r.Str(24); LastDivorce = nm;
                    _chat(Lang.F("You and {0} are divorced.", nm), "ffb070"); Toast?.Invoke(Lang.F("Divorced from {0}.", nm));
                    PartnerCid = 0; Partner = ""; _asked = 0;
                    Debug.Log($"[Family] divorced from {nm}");
                    Changed?.Invoke(); return true;
                }
                case 0x01f6:
                {
                    r.Skip(2); uint father = r.U32(), mother = r.U32(); string nm = r.Str(24);
                    AdoptOffer = (father, mother, nm);
                    _chat(Lang.F("{0} and their partner want to adopt you — answer in the wrist menu (More). You would become a baby class for good.", nm), "ffd060");
                    Toast?.Invoke(Lang.F("{0} wants to adopt you.", nm));
                    Debug.Log($"[Family] adoption offer from {nm} ({father}, {mother})");
                    Changed?.Invoke(); return true;
                }
                case 0x0216:
                {
                    r.Skip(2); LastBabyMsg = r.S32();
                    string m = LastBabyMsg == 0 ? Lang.T("You already have a child.") : LastBabyMsg == 1 ? Lang.T("Both parents must be base level 70 or higher.") : LastBabyMsg == 2 ? Lang.T("Married characters can't be adopted.") : Lang.F("Adoption refused ({0}).", LastBabyMsg);
                    _chat(m, "ffb070"); Toast?.Invoke(m);
                    Debug.Log($"[Family] baby message {LastBabyMsg}");
                    Changed?.Invoke(); return true;
                }
                default: return false;
            }
        }

        /// <summary>Jobs the server lets be adopted (pc.cpp:1709-1714): Novice and the first jobs, Super Novice, Expanded Super Novice.</summary>
        public static bool Adoptable(int job) => (job >= 0 && job <= 6) || job == 23 || job == 4190;
        public void AskAdopt(uint babyAid) { _s.SendPacket(new PacketWriter(0x01f9).U32(babyAid).ToArray()); _chat(Lang.T("Adoption request sent. (Both parents need their rings on, level 70+, and one party with the child.)"), "c8cbd6"); Debug.Log($"[Family] adopt request {babyAid}"); }
        public void AnswerAdopt(bool accept)
        {
            if (AdoptOffer == null) return;
            var q = AdoptOffer.Value; AdoptOffer = null;
            _s.SendPacket(new PacketWriter(0x01f7).U32(q.father).U32(q.mother).U32(accept ? 1u : 0u).ToArray());
            _chat(accept ? Lang.T("You accepted the adoption.") : Lang.T("You declined the adoption."), "c8cbd6");
            Debug.Log($"[Family] adoption answer {accept}");
            Changed?.Invoke();
        }
    }
}
