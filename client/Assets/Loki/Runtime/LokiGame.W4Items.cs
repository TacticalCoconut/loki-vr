using Loki.Game;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 4 (w4items): items and economy — the roulette window, the cart ↔ storage tabs, the cart change / decoration
    /// menus, cooking results, and the dual-wield positions after a job change. Only RO packets and client data: the same on any server.
    ///   Packets peeked at here (never consumed; the owners handle them): ZC_USE_SKILL 0x09cb of our own MC_CHANGECART (opens the cart
    ///   choice), ZC_NOTIFY_EFFECT2 0x01f3 on ourselves (cooking's result effect), ZC_SPRITE_CHANGE 0x01d7 LOOK_BASE of ourselves (a job
    ///   change: which weapons may go in the left hand), ZC_INVENTORY_START 0x0b08 type 0 (a fresh list: the server's positions are the
    ///   truth again).
    /// Hooks in LokiGame.cs: BeginW4Items (after BeginW3Social), HandleW4Items (the map packet dispatch, after HandleW3Social).
    /// </summary>
    public sealed partial class LokiGame
    {
        public Roulette Roulette { get; private set; }
        private bool _w4DualCan; private bool _w4DualKnown;

        private void BeginW4Items(LokiBootstrap boot)
        {
            System.Func<Camera> head = () => boot.Rig.Head;
            Roulette = new Roulette(Session, ListWindow.Create(boot.Rig.transform, "RouletteWindow"), head, ItemChat);
            foreach (var lw in boot.Rig.GetComponentsInChildren<ListWindow>(true)) if (lw.RequestKeyboard == null) lw.RequestKeyboard = f => { Keyboard.Attach(f); Keyboard.Show(); };
            if (Hud != null && Hud.Wrist != null) Hud.Wrist.OpenRoulette = () => { Hud.Wrist.Hide(); Roulette.Open(); };
            if (Cart != null)
            {
                Cart.Storage = Storage; Cart.BaseLevel = () => Player.BaseLevel; Cart.SelfAid = () => Session.AccountId;
                Cart.KnowsSkill = id => Prog.Skills.TryGetValue(id, out var k) && k.Level > 0;
                Cart.CastSkill = id => { if (Prog.Skills.TryGetValue(id, out var k) && k.Level > 0) { Debug.Log($"[Cart] use skill {id}"); Session.UseSkill(id, k.Level, Session.AccountId); } };
                if (Storage != null) { Storage.Cart = Cart; Cart.Changed += Storage.CartChanged; }
            }
            if (Refinery != null) Refinery.Zeny = () => Player.Zeny;
            Prog.Changed += W4ItemsDualCheck;
            Session.OnMapPacket += W4ItemsPeek;
        }

        private bool HandleW4Items(ushort id, byte[] b, int o, int n)
        {
            if (Roulette != null && Roulette.Handle(id, b, o, n)) return true;
            return false;
        }

        private void W4ItemsPeek(RoSession s, ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x09cb: // ZC_USE_SKILL skillId.W level.L target.L src.L result.B (packets_struct.hpp:4675-4683)
                {
                    if (n < 17) return;
                    r.Skip(2); int sk = r.U16(); r.S32(); r.U32(); uint src = r.U32();
                    if (src == s.AccountId && sk == Loki.Game.Cart.SkillChangeCart) Cart?.OnOwnSkillUsed(sk);
                    return;
                }
                case 0x01f3: // ZC_NOTIFY_EFFECT2 aid.L type.L (clif_specialeffect, clif.cpp:9704-9716)
                {
                    if (n < 10) return;
                    r.Skip(2); uint aid = r.U32(); int eff = r.S32();
                    if (aid == s.AccountId) Crafting?.OnSelfEffect(eff);
                    return;
                }
                case 0x01d7: // ZC_SPRITE_CHANGE LOOK_BASE of ours: a job change (W3SkillsListen, subscribed first, has already read the job)
                {
                    if (n < 15) return;
                    r.Skip(2); uint aid = r.U32(); int type = r.U8();
                    if (aid == s.AccountId && type == 0) W4ItemsDualCheck();
                    return;
                }
                case 0x0b08: // ZC_INVENTORY_START len.W invType.B: the inventory list that follows carries the server's own positions
                {
                    if (n < 5) return;
                    r.Skip(4); if (r.U8() != 0) return;
                    _w4DualCan = W4ItemsCanDualWield(); _w4DualKnown = true;
                    return;
                }
            }
        }

        private bool W4ItemsCanDualWield()
        {
            int job = W3SkillJob();
            int left = Prog.Skills.TryGetValue(Loki.Game.Inventory.SkillLeftHand, out var k) ? k.Level : 0;
            return Loki.Game.Inventory.CanDualWield(job, left);
        }

        /// <summary>A job change or a new skill list: when the dual-wield test changes, the held list's weapon positions follow
        /// (Inventory.ApplyDualWield); the next inventory list replaces them with the server's.</summary>
        private void W4ItemsDualCheck()
        {
            if (!_w4DualKnown || Session?.SelectedChar == null) return;
            bool can = W4ItemsCanDualWield();
            if (can == _w4DualCan) return;
            _w4DualCan = can;
            Debug.Log($"[Items] dual wield now {(can ? "possible" : "impossible")} (job {W3SkillJob()})");
            Inventory.ApplyDualWield(can);
        }
    }
}
