using Loki.World;
using UnityEngine;

namespace Loki.Sprites
{
    /// <summary>
    /// Client wave 2: what a player drags or carries along — the cart, the falcon and the warg (research/world_rendering.md §F).
    ///  • Cart: status EFST_ON_PUSH_CART (673) with val1 = cart type 1..13 (the server's NEW_CARTS; pc.cpp:11273-11290), sent to the area,
    ///    so other players' carts show too. Sprite data\sprite\이팩트\&lt;name&gt; by type, Super Novices always the 0 cart (roBrowser
    ///    DBManager.getCartPath, EntityRender.js:416); the owner's action and direction, frame 0 while standing or sitting. roBrowser draws it
    ///    as a layer with screen-space pixel offsets for its pitched camera; in VR that lifts or sinks it, so here it is its own sprite on
    ///    the ground 1.1 cells behind the owner (40 px / 35 px per cell, roBrowser's side offset) — a Loki choice, not a reference's.
    ///  • Falcon: option bit 0x10; 이팩트\매 (Hunter 11 / 4034), 매2 (Sniper 4012), owl (Ranger 4056 / 4062 / 4098), windhawk_hawk (Windhawk
    ///    4257 / 4270 / 4278), else 매 (DBManager.js:1245-1277); idle / walk groups 0 / 1, over the owner at rb's altitude + 5 cells.
    ///  • Warg: option bit 0x100000; 몬스터\워그 (Ranger), 이팩트\windhawk_wolf (Windhawk 4257), else 워그; walks beside the owner.
    /// </summary>
    public sealed class UnitFollowers : MonoBehaviour
    {
        public const int OptionFalcon = 0x10, OptionWug = 0x100000;
        public const float FalconAltitude = 5f;
        private static readonly string[] CartNames =
        {
            "½´³ë¼Õ¼ö·¹",             // 0 슈노손수레 (Super Novice)
            "¼Õ¼ö·¹",                                   // 1 손수레
            "¼Õ¼ö·¹1", "¼Õ¼ö·¹2", "¼Õ¼ö·¹3", "¼Õ¼ö·¹4",
            "¼Õ¼ö·¹5", "¼Õ¼ö·¹6", "¼Õ¼ö·¹7", "¼Õ¼ö·¹8",
            "¼±¹°»óÀÚÄ«Æ®", // 10 선물상자카트
            "Æ÷¸µ½ÆÀºÄ«Æ®", // 11 포링싣은카트
            "Æ÷¸µÄ«Æ®",                         // 12 포링카트
            "¸¶µµÄ«Æ®",                         // 13 마도카트
        };
        private const string Falcon = "¸Å", Falcon2 = "¸Å2", Warg = "¿ö±×"; // 매, 매2, 워그

        private Unit _owner; private RoAssets _assets;
        private ActorSprite _cart, _falcon, _wug; private int _cartType;
        public int CartType => _cartType;
        public bool HasCart => _cart != null; public bool HasFalcon => _falcon != null; public bool HasWug => _wug != null;
        public static string CartPath(int type, int job) => "data\\sprite\\" + RoNames.Effect + "\\" + CartNames[Mathf.Clamp(IsSuperNovice(job) ? 0 : type, 0, 13)];
        private static bool IsSuperNovice(int job) => job == 23 || job == 4045 || job == 4190 || job == 4191;

        public static UnitFollowers Of(Unit u, RoAssets assets)
        {
            var f = u.GetComponent<UnitFollowers>();
            if (f == null) { f = u.gameObject.AddComponent<UnitFollowers>(); f._owner = u; f._assets = assets; }
            return f;
        }

        public void SetCart(int type)
        {
            _cartType = type;
            if (type <= 0) { Drop(ref _cart); return; }
            string path = CartPath(type, _owner.Job);
            if (!_assets.Sprite(path, out var spr, out var act)) { Debug.LogWarning("[Followers] no cart sprite " + path); Drop(ref _cart); return; }
            if (_cart == null) _cart = Make("cart");
            _cart.SetBody(spr, act); _cart.IsPlayerType = true; // idle / sit frames follow the head-turn rule: frame 0 (roBrowser EntityRender.js:980-982)
            Debug.Log($"[Followers] cart {type} for {_owner.Id}");
        }

        public void SetOption(int option)
        {
            bool falcon = (option & OptionFalcon) != 0, wug = (option & OptionWug) != 0;
            if (falcon && _falcon == null)
            {
                int j = _owner.Job;
                string name = j == 4012 ? Falcon2 : (j == 4056 || j == 4062 || j == 4098) ? "owl" : (j == 4257 || j == 4270 || j == 4278) ? "windhawk_hawk" : Falcon;
                string path = "data\\sprite\\" + RoNames.Effect + "\\" + name;
                if (_assets.Sprite(path, out var spr, out var act)) { _falcon = Make("falcon"); _falcon.SetBody(spr, act); Debug.Log($"[Followers] falcon for {_owner.Id}"); }
                else Debug.LogWarning("[Followers] no falcon sprite " + path);
            }
            else if (!falcon) Drop(ref _falcon);
            if (wug && _wug == null)
            {
                string path = _owner.Job == 4257 ? "data\\sprite\\" + RoNames.Effect + "\\windhawk_wolf" : "data\\sprite\\" + RoNames.Monster + "\\" + Warg;
                if (_assets.Sprite(path, out var spr, out var act)) { _wug = Make("warg"); _wug.SetBody(spr, act); Debug.Log($"[Followers] warg for {_owner.Id}"); }
                else Debug.LogWarning("[Followers] no warg sprite " + path);
            }
            else if (!wug) Drop(ref _wug);
        }

        private ActorSprite Make(string name)
        {
            var go = new GameObject(name + ":" + _owner.Id);
            go.transform.SetParent(_owner.transform.parent, false); // the map, not the unit: followers stand on their own cells
            return go.AddComponent<ActorSprite>();
        }
        private static void Drop(ref ActorSprite s) { if (s != null) Destroy(s.gameObject); s = null; }
        private void OnDestroy() { Drop(ref _cart); Drop(ref _falcon); Drop(ref _wug); }

        private static readonly Vector2[] Facing = { new Vector2(0, -1), new Vector2(-1, -1), new Vector2(-1, 0), new Vector2(-1, 1), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(1, -1) };

        private void LateUpdate()
        {
            if (_owner == null) return;
            int dir = _owner.Sprite.Direction & 7; var f = Facing[dir].normalized; var side = new Vector2(f.y, -f.x);
            int group = _owner.Sprite.ActionGroup; bool walking = _owner.Walking; // client wave 10 (w10move): the cart / wug / falcon are drawn beside the DRAWN body (Unit.DrawnCell), not the logical cell
            if (_cart != null) Place(_cart, _owner.DrawnCell - f * 1.1f, dir, group, 0f); // the owner's action (roBrowser EntityRender.js:791-796)
            if (_wug != null) Place(_wug, _owner.DrawnCell + side * 1.1f - f * 0.3f, dir, walking ? 1 : 0, 0f);
            // the falcon flies 5 cells above the ground (roBrowser Entity.js:297-299 and EntityWalk.js:768-771: altitude + 5, where
            // Altitude.getCellHeight is positive upwards and damage numbers use the same axis at +2..+7)
            if (_falcon != null) Place(_falcon, _owner.DrawnCell + side * 0.35f, dir, walking ? 1 : 0, FalconAltitude);
        }

        private void Place(ActorSprite s, Vector2 cell, int dir, int group, float lift)
        {
            var gat = LokiBootstrap.Instance != null && LokiBootstrap.Instance.Map.Current != null ? LokiBootstrap.Instance.Map.Current.Gat : null;
            float h = 0f;
            if (gat != null) { int cx = Mathf.Clamp(Mathf.FloorToInt(cell.x), 0, gat.Width - 1), cy = Mathf.Clamp(Mathf.FloorToInt(cell.y), 0, gat.Height - 1); h = -gat.Get(cx, cy).AverageHeight * RoSpace.Scale; }
            s.transform.localPosition = new Vector3(cell.x + 0.5f, h + lift, cell.y + 0.5f);
            s.Direction = dir;
            s.Tint = _owner.Sprite.Tint;
            if (s.ActionGroup != group) s.Play(group, true);
        }
    }
}
