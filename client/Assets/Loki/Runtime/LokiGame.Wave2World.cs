using Loki.Sprites;
using Loki.World;
using UnityEngine;

namespace Loki
{
    /// <summary>Client wave 2, world rendering: animated models (MapLoader + RsmAnimator), map point lights, fog, map effects and day /
    /// night (WorldLook), plus carts, falcons and wargs on the units (UnitCompanions.cs).</summary>
    public sealed partial class LokiGame
    {
        public WorldLook World { get; private set; }
        /// <summary>The last map change pointed at a map the client data doesn't have (the old scene stayed up).</summary>
        private bool _w2MapMissing;

        private void EnsureWorldLook()
        {
            if (World != null) return;
            World = gameObject.AddComponent<WorldLook>();
            World.Init(_boot.Map.transform, Sky, Effects, () => Entities?.Self != null ? Entities.Self.transform.localPosition : (Vector3?)null,
                       () => View != null && View.Mode != VR.ViewController.ViewMode.Table);
            // night: EFST_SKE (160) on ourselves
            Prog.EffectChanged += (type, on, val1) => { if (type == 160) World.SetNight(on); };
            // carts (EFST_ON_PUSH_CART 673 with the cart type in val1) on anyone, falcons and wargs from the option bits
            Prog.AnyEffectChanged += (aid, type, on, val1) =>
            {
                if (type != 673) return;
                int cart = on ? (val1 > 0 ? val1 : _w2Carts.TryGetValue(aid, out int prev) ? prev : 1) : 0; // 0x0196 carries no type: keep the last one
                if (cart > 0) _w2Carts[aid] = cart; else _w2Carts.Remove(aid);
                var u = Entities.Get(aid); if (u != null) UnitFollowers.Of(u, _boot.Assets).SetCart(cart);
            };
            Entities.UnitAdded += u => { if (_w2Carts.TryGetValue(u.Id, out int c)) UnitFollowers.Of(u, _boot.Assets).SetCart(c); };
            Entities.OptionChanged += (u, old, now) => { if (((old ^ now) & (UnitFollowers.OptionFalcon | UnitFollowers.OptionWug)) != 0) UnitFollowers.Of(u, _boot.Assets).SetOption(now); };
            Entities.Vanished += (id, type) => { if (type != 1) _w2Carts.Remove(id); };
        }
        private readonly System.Collections.Generic.Dictionary<uint, int> _w2Carts = new System.Collections.Generic.Dictionary<uint, int>();

        private void OnMapChangeWave2World(string map)
        {
            EnsureWorldLook();
            if (_w2MapMissing) return;
            var m = _boot.Map.Current;
            World.OnMap(map, m, m != null && Room != null && Room.IsIndoor(map));
            // our own unit was made again: put its cart back (the server keeps the status and doesn't always resend it)
            foreach (var aid in new System.Collections.Generic.List<uint>(_w2Carts.Keys)) if (aid != Session.AccountId) _w2Carts.Remove(aid);
            if (Entities.Self != null && _w2Carts.TryGetValue(Session.AccountId, out int mine)) UnitFollowers.Of(Entities.Self, _boot.Assets).SetCart(mine);
        }
    }
}
