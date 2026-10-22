using System;
using System.Collections.Generic;
using System.Linq;
using Loki.Game;
using Loki.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Wave 28 (wcxp): the Codex bounties in the book (LokiGame.Wcxp.cs; the server's state: Game/CodexBounty.cs). design note: "To claim all
    /// bounties you have to open the codex and click a button on each entry that has been completed"; the orchestrator: the list slot
    /// marks them so they are easy to find, and a complete bracket has its own button on its progress line. English only, as the whole
    /// Codex. Drawn only while the server has bounties (a server without them shows the book exactly as before). The marks (FLAGGED):
    ///   list slot     after the level: "◆" (a bounty open, the theme's Accent), "Claim" (completed, bold, the Good ink), "✓" (claimed);
    ///   creature page in the picture's frame, right of the creature: "◆ Bounty / +3% EXP" (open: the share the player's level would
    ///                 get now), a CLAIM BUTTON "Claim / +1,165 EXP" (completed: the amount its kill fixed; "+485 Zeny" when a claim pays
    ///                 Zeny: the character at its job's highest level), "✓ Bounty / claimed", or "Scan for / a bounty";
    ///   list page     its foot, one thing at a time: design note (2026-10-16) "Claim all on this page" while any of its creatures waits for its
    ///                 Claim ("Claim all on this page +2,330 EXP": one Claim per completed bounty, each paid by the server alone); else
    ///                 a complete bracket's button "Claim Lv 1-10 bonus +6,915 EXP"; else the brackets of its creatures, "Lv 1-10: 3/38"
    ///                 ("✓" when the bonus is paid); a bracket with no member shows nothing;
    ///   title page    "Brackets done: N / M" on the "Jump to level" line (the level jumps are 20 levels wide, the brackets 10).
    /// </summary>
    public sealed partial class CodexBook
    {
        /// <summary>design note (2026-10-17 item 50): rich golden highlights for pages and entries with unclaimed completed bounties.</summary>
        public static readonly Color BountyGold = new Color(0.95f, 0.76f, 0.20f, 1f);
        public static readonly Color BountyGoldRow = new Color(0.95f, 0.76f, 0.20f, 0.38f);
        public static readonly Color BountyGoldHover = new Color(1.00f, 0.84f, 0.32f, 0.62f);
        public static readonly Color BountyGoldPage = new Color(0.95f, 0.76f, 0.20f, 0.20f);

        /// <summary>The character's bounties as the server tells them (set by LokiGame.Wcxp.cs); null or not Supported: nothing drawn.</summary>
        public CodexBounty Bounties { get; set; }
        /// <summary>The Claim buttons (LokiGame.Wcxp.cs sends them to the server): a creature's bounty, a bracket's bonus.</summary>
        public Action<int> ClaimBounty, ClaimBracket;
        /// <summary>Claim buttons pressed; "Claim all on this page" pressed (smoke).</summary>
        public int ClaimPresses { get; private set; }
        public int ClaimAllPresses { get; private set; }
        private bool BountiesShown => Bounties != null && Bounties.Supported;

        /// <summary>Resolves the active bounty ID for a creature or any of its canonical versions (ready &gt; open &gt; claimed).</summary>
        public int ResolveBountyId(int id)
        {
            if (!BountiesShown) return id;
            if (Bounties.IsReady(id)) return id;
            var vers = CodexData.VersionsOf(id);
            for (int i = 0; i < vers.Count; i++) if (Bounties.IsReady(vers[i])) return vers[i];
            if (Bounties.IsOpen(id)) return id;
            for (int i = 0; i < vers.Count; i++) if (Bounties.IsOpen(vers[i])) return vers[i];
            if (Bounties.IsClaimed(id)) return id;
            for (int i = 0; i < vers.Count; i++) if (Bounties.IsClaimed(vers[i])) return vers[i];
            return id;
        }
        /// <summary>True when this creature (or any of its collapsed versions) has a completed, unclaimed bounty ready to claim.</summary>
        public bool HasUnclaimedBounty(int id) => BountiesShown && Bounties.IsReady(ResolveBountyId(id));

        /// <summary>True when a list page has at least one unclaimed completed creature bounty or unclaimed bracket bonus.</summary>
        public bool ListPageHasUnclaimedBounty(List<int> ids, int first, int count)
        {
            if (!BountiesShown || ids == null) return false;
            for (int k = first; k < first + count && k < ids.Count; k++)
            {
                if (HasUnclaimedBounty(ids[k]) && _journal != null && _journal.Tier(ids[k]) != CodexJournal.TierNone) return true;
                var cr = CodexData.GetCreature(ids[k]);
                if (cr != null && Bounties.Bracket(CodexBountyWire.BracketOf(cr.Level)).state == 1) return true;
            }
            return false;
        }

        /// <summary>The server's bounties changed: the lists again, and the spread on show drawn again (not while it turns or opens).</summary>
        public void BountiesChanged()
        {
            JournalChanged();
            if (IsOpen && !Animating && !Opening) ShowSpread(Current);
        }

        private string AmountText(long exp) => Bounties.ZenyPerMillion > 0
            ? string.Format("+{0:N0} Zeny", CodexBountyWire.Zeny(exp, Bounties.ZenyPerMillion)) : string.Format("+{0:N0} EXP", exp);

        /// <summary>The list slot's note mark (after "Lvl: N"): rich text, "" when there is none.</summary>
        private string BountyMark(int id)
        {
            if (!BountiesShown) return "";
            int bid = ResolveBountyId(id);
            if (Bounties.IsReady(bid)) return "  <color=#" + Theme.Hex(BountyGold) + "><b>★ Claim</b></color>";
            if (Bounties.IsClaimed(bid)) return "  <color=#" + Theme.Hex(Theme.Good) + ">✓</color>";
            if (Bounties.IsOpen(bid)) return "  <color=#" + Theme.Hex(Theme.Accent) + ">◆</color>";
            return "";
        }

        /// <summary>The creature page's bounty mark or Claim button, in the picture's frame right of the creature (the page's top-left
        /// corner holds the book's Index button: dev 6's picture showed it over a line there).</summary>
        private void DrawBountyLine(RectTransform c, int id)
        {
            int bid = ResolveBountyId(id);
            if (!BountiesShown || (!CodexData.Metable(id) && !CodexData.Metable(bid) && !Bounties.Any(bid))) return;
            float x = CW / 2 - 70f, y = Top - 252f;
            if (Bounties.IsReady(bid))
            {
                var b = Btn(c, "Claim\n" + AmountText(Bounties.ReadyAmount(bid)), x, y - 10f, 124f, 84f, null, 24);
                b.name = "btn:claim:" + id;
                b.Normal = BountyGoldRow; b.Hover = BountyGoldHover; b.SetHover(false);
                if (b.Text != null) b.Text.color = BountyGold;
                b.SetAction(() => { if (!b.Interactable) return; b.Interactable = false; ClaimPresses++; ClaimBounty?.Invoke(bid); });
                return;
            }
            var cr = CodexData.GetCreature(bid) ?? CodexData.GetCreature(id);
            string s; Color ink;
            if (Bounties.IsClaimed(bid)) { s = "✓ Bounty\nclaimed"; ink = Theme.Good; }
            else if (Bounties.IsOpen(bid))
            {
                long flatExp = CodexBountyWire.FlatExp(cr != null ? cr.Level : 1, cr != null ? cr.BossType : 0);
                s = "◆ Bounty\n" + AmountText(flatExp); ink = Theme.Accent;
            }
            else { s = "Scan for\na bounty"; ink = Faint; }
            var t = Txt(c, s, x, y, 124f, 64f, 24, TextAnchor.UpperRight, ink, Bounties.IsOpen(bid), 24);
            t.name = "bountyline";
        }

        /// <summary>The list page's foot: the brackets its creatures belong to, as the server counts them; a complete one's Claim button.</summary>
        private void DrawBracketFoot(RectTransform c, List<int> ids, int first, int count)
        {
            if (!BountiesShown) return;
            var seen = new List<int>(); var waiting = new List<int>(); long sum = 0;
            for (int k = first; k < first + count && k < ids.Count; k++)
            {
                int bid = ResolveBountyId(ids[k]);
                if (Bounties.IsReady(bid) && _journal != null && _journal.Tier(ids[k]) != CodexJournal.TierNone && !waiting.Contains(bid))
                {
                    waiting.Add(bid);
                    sum += Bounties.ReadyAmount(bid);
                }
                var cr = CodexData.GetCreature(ids[k]); if (cr == null) continue;
                int b = CodexBountyWire.BracketOf(cr.Level);
                if (!seen.Contains(b) && Bounties.Bracket(b).members > 0) seen.Add(b);
            }
            if (waiting.Count > 0)
            {
                // design note: "Claim all on this page": one Claim per completed bounty listed here (the server pays each once)
                var all = Btn(c, "Claim all on this page " + AmountText(sum), 0f, Top - 708f, CW, 52f, null, 24);
                all.name = "btn:claimall";
                all.Normal = BountyGoldRow; all.Hover = BountyGoldHover; all.SetHover(false);
                if (all.Text != null) all.Text.color = BountyGold;
                all.SetAction(() =>
                {
                    if (!all.Interactable) return;
                    all.Interactable = false; ClaimAllPresses++;
                    foreach (var id in waiting) { ClaimPresses++; ClaimBounty?.Invoke(id); }
                });
                return;
            }
            if (seen.Count == 0) return;
            int ready = seen.FirstOrDefault(b => Bounties.Bracket(b).state == 1);
            if (ready > 0)
            {
                var v = Bounties.Bracket(ready);
                var btn = Btn(c, $"Claim Lv {CodexBountyWire.BracketFrom(ready)}-{CodexBountyWire.BracketTo(ready)} bonus {AmountText(v.bonus)}", 0f, Top - 708f, CW, 52f, null, 24);
                btn.name = "btn:claimbracket:" + ready;
                btn.Normal = BountyGoldRow; btn.Hover = BountyGoldHover; btn.SetHover(false);
                if (btn.Text != null) btn.Text.color = BountyGold;
                btn.SetAction(() => { if (!btn.Interactable) return; btn.Interactable = false; ClaimPresses++; ClaimBracket?.Invoke(ready); });
                return;
            }
            var parts = seen.Select(b => { var v = Bounties.Bracket(b); return $"Lv {CodexBountyWire.BracketFrom(b)}-{CodexBountyWire.BracketTo(b)}: {v.claimed}/{v.members}" + (v.state == 2 ? " ✓" : ""); });
            string s = (seen.Count == 1 ? "Bounties " : "") + string.Join(" · ", parts);
            var t = Txt(c, s, 0f, Top - 708f, CW, 40f, 24, TextAnchor.MiddleCenter, Faint, false, 24);
            t.name = "bracketfoot";
        }

        /// <summary>The title page's count of brackets done (their bonus paid), on the "Jump to level" line.</summary>
        private void DrawBracketSum(RectTransform c)
        {
            if (!BountiesShown) return;
            int all = 0, done = 0;
            foreach (var b in Bounties.Brackets) { var v = Bounties.Bracket(b); if (v.members <= 0) continue; all++; if (v.state == 2) done++; }
            if (all == 0) return;
            var t = Txt(c, $"Brackets done: {done} / {all}", 0f, Top - 548f, CW, 40f, 24, TextAnchor.MiddleRight, Faint, false, 24);
            t.name = "bracketsum";
        }
    }
}
