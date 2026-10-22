using Loki.UI;
using Loki.World;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 24 (w24): the screens before the world. the design's list of 2026-10-12.
    ///   Item 1   "require the login music to be present in the login screen": Audio/LokiAudio.cs PlayLoginBgm (the game folder's
    ///            BGM\01.mp3, the original client's track of the login and the character screens), started here whenever the login
    ///            panel is shown at the start of a scene (the first start, after "Log out", after "Character select", after a lost
    ///            connection: each is a new scene). Entering the world it fades over to the map's track (LokiGame.cs OnEnteredMap).
    ///            A run that logs in by itself (a development build's -lokiUser) never shows the login screen and plays none.
    ///   Item 2   "require the login panel to be draggable and also the keyboard panel to be draggable". WHY THEY COULD NOT BE TAKEN:
    ///            (1) neither was in the list of panels taken by their edge (UI/PanelGrab.cs Named; the keyboard left out on
    ///            purpose in wave 13), and (2) no grab at all was offered before the world (LokiGame.W13Grab.cs W13GrabBusy: its
    ///            "!W13InGame"). Both are named now, and the grab runs while the login panel shows (<see cref="W24BeforeWorld"/>;
    ///            there only panels are taken: no backpack, no skill tree). The keyboard is taken wherever it shows (the login
    ///            screen, the chat, every text field). KEYS: a key is a button and a button under the laser is always the button
    ///            (UI/PanelGrab.cs OnLaser), so neither a trigger nor a grip on a key ever takes the keyboard; it is taken on its
    ///            frame and the band beyond it (and by a hand at its edge), like every panel. The keys are its children: they move
    ///            with it and keep their colliders. An upright hold keeps the keyboard's tilt (GrabHold.KeepPitch).
    ///            KEPT: let go, the place is kept in the head's level frame (UI/PanelPlacing.cs) and used the next time the panel
    ///            is placed: the login panel's and the keyboard's before the world per PC (UserSettings loginPlace / kbLoginPlace,
    ///            in PerPcFields), the keyboard's in the world with the account's settings like the other places (kbPlace).
    ///            The keyboard, once shown, stays where it is until it is closed (pressing another field no longer moves it).
    ///            RESET: Reposition's "Reset" (the menu above; LokiGame.W13View.cs W13ResetPlaces) puts all three back.
    ///   Item 3A  the ground guard and the low head's rule: UI/PanelPlacing.cs (the ground: <see cref="W24GroundUnder"/>). A
    ///            recentre places the login panel in front again (kept place or default) and the guard holds.
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>A let-go panel's place is kept once more this long after the let go (its easing out has ended by then).</summary>
        public const float W24KeepAgainAfter = 1f;
        /// <summary>The ground is looked for from this far above a point, and as far below it (metres).</summary>
        public const float W24GroundRay = 50f;
        /// <summary>The screens before the world are shown (the login panel is up).</summary>
        public bool W24BeforeWorld => Login != null && Login.Visible;
        /// <summary>Places kept so far (smoke, log).</summary>
        public int W24PlacesKept { get; private set; }
        private RectTransform _w24Kb, _w24Login; private Collider _w24Ground; private GameObject _w24GroundOf;
        private readonly float[] _w24KeepAt = { -1f, -1f };

        private void BeginW24Login(LokiBootstrap boot)
        {
            _w24Kb = Keyboard.transform as RectTransform; _w24Login = Login.transform as RectTransform;
            PanelPlacing.GroundUnder = W24GroundUnder;
            PanelPlacing.TableLevel = () => _boot != null && _boot.Rig != null && _boot.Rig.Diorama != null ? _boot.Rig.Diorama.position.y : (float?)null;
            Keyboard.PlaceKept = () => { var s = UserSettings.Prefs; return W24BeforeWorld ? (s.kbLoginPlaced ? s.kbLoginPlace : null) : (s.kbPlaced ? s.kbPlace : null); };
            Keyboard.gameObject.AddComponent<PanelGroundGuard>();
            Login.gameObject.AddComponent<PanelGroundGuard>().Also = W24Feet;
            boot.Rig.Recentred += () => { if (Login != null && Login.Visible) Login.PlaceInFront(); };
            if (!Login.Visible) return;
            Physics.SyncTransforms(); // (the map was loaded in this frame: its ground collider answers at once)
            Login.PlaceInFront(); // (again: the first placing was before the ground was known)
            Audio.PlayLoginBgm();
            Debug.Log($"[Login] before the world: backdrop '{(boot.Map.Current != null ? boot.Map.Current.Name : "-")}', login music {(Audio.LoginMusic ? "BGM\\" + Loki.Audio.LokiAudio.LoginTrack + ".mp3" : "none (not in the game folder)")}, login panel {(UserSettings.Prefs.loginPlaced ? "at its kept place" : "at its default place")}");
        }

        /// <summary>The creation character's feet while it is shown beside the login panel (it must be clear of the ground too).</summary>
        private Vector3? W24Feet() => Login != null && Login.Preview != null && Login.Preview.gameObject.activeInHierarchy ? Login.Preview.transform.position : (Vector3?)null;
        /// <summary>The ground guard of the login panel or the keyboard, at once (a drag's carry, a place about to be kept).</summary>
        private void W24GuardHeld(RectTransform t) { if (t == null) return; if (t == _w24Login) PanelPlacing.Guard(t, W24Feet()); else if (t == _w24Kb) PanelPlacing.Guard(t); }

        /// <summary>The height of the loaded map's ground under a world point (null: no ground there, no map).</summary>
        private float? W24GroundUnder(Vector3 p)
        {
            var m = _boot != null && _boot.Map != null ? _boot.Map.Current : null;
            if (m == null || m.Ground == null) return null;
            if (_w24GroundOf != m.Ground) { _w24GroundOf = m.Ground; _w24Ground = m.Ground.GetComponent<Collider>(); }
            if (_w24Ground == null || !_w24Ground.enabled || !m.Ground.activeInHierarchy) return null;
            return _w24Ground.Raycast(new Ray(new Vector3(p.x, p.y + W24GroundRay, p.z), Vector3.down), out var hit, W24GroundRay * 2f) ? hit.point.y : (float?)null;
        }

        /// <summary>LokiGame.W13Grab.cs W13GrabRelease: a panel was let go: the login panel's and the keyboard's places are kept.</summary>
        private void W24Released(RectTransform panel, string why)
        {
            if (panel == null || why != "let go") return;
            int k = panel == _w24Login ? 0 : panel == _w24Kb ? 1 : -1;
            if (k < 0) return;
            if (k == 0 && Login != null) Login.UserMoved = true;
            if (k == 1 && Keyboard != null) Keyboard.UserMoved = true;
            W24Keep(k); _w24KeepAt[k] = Time.unscaledTime + W24KeepAgainAfter;
        }

        private void W24Keep(int k)
        {
            var rt = k == 0 ? _w24Login : _w24Kb; var head = _boot.Rig.Head; var s = UserSettings.Prefs;
            if (rt == null || head == null) return;
            W24GuardHeld(rt); // (kept where it may stand)
            var place = PanelPlacing.Capture(rt, head); string what;
            if (k == 0) { s.loginPlace = place; s.loginPlaced = true; what = "the login panel (this PC)"; }
            else if (W24BeforeWorld) { s.kbLoginPlace = place; s.kbLoginPlaced = true; what = "the keyboard before the world (this PC)"; }
            else { s.kbPlace = place; s.kbPlaced = true; what = "the keyboard in the world (the account's settings)"; }
            UserSettings.Save(); W24PlacesKept++;
            Debug.Log($"[Place] kept: {what}: {place.x * 100f:0} cm right, {place.y * 100f:0} cm up, {place.z * 100f:0} cm ahead of the eyes");
        }

        /// <summary>Once a frame (LokiGame.W13Grab.cs UpdateW13Grab): a let-go panel's place kept once more after its easing.</summary>
        private void UpdateW24Login()
        {
            for (int k = 0; k < 2; k++)
            {
                if (_w24KeepAt[k] < 0f || Time.unscaledTime < _w24KeepAt[k]) continue;
                _w24KeepAt[k] = -1f;
                var rt = k == 0 ? _w24Login : _w24Kb;
                if (rt != null && PanelGrab.IsShown(rt) && !W13GrabSkip(rt)) W24Keep(k);
            }
        }

        /// <summary>Reposition's "Reset" (LokiGame.W13View.cs W13ResetPlaces, which saves): the keyboard's two places and the login
        /// panel's are forgotten; a keyboard that shows goes to its default place at once.</summary>
        private void W24ResetPlaces()
        {
            var s = UserSettings.Prefs; s.kbPlaced = false; s.kbLoginPlaced = false; s.loginPlaced = false;
            if (Login != null) Login.UserMoved = false;
            if (Keyboard != null) Keyboard.UserMoved = false;
            _w24KeepAt[0] = _w24KeepAt[1] = -1f;
            if (Keyboard != null && Keyboard.Visible) Keyboard.Place();
        }
    }
}
