using UnityEngine;

namespace Loki.VR
{
    /// <summary>
    /// Client wave 14 (w14laser), from the design's headset test of wave 13 (design notes):
    ///   Point 20 + answer 14  WHERE THE LASER IS DRAWN. design note: "The pointer laser on the controller shouldn't be visible while I am not
    ///             pointing with it at the ground", then "When ground walking laser mode is off, show it only on panels and monsters (it
    ///             should fade in when you aim close to one). Otherwise, show it on ground, panels and monsters." The pointing hand's
    ///             line is drawn fully while it is on what its trigger acts on: a panel (a button, a panel's background, an edge band),
    ///             a unit or a ground item (<see cref="Sprites.Targetable"/>: monsters, NPCs, players, items), and the ground (a map
    ///             cell) while "Laser walking" is on (<see cref="Comfort.LaserWalk"/>) or a ground skill waits to be aimed
    ///             (<see cref="GroundAimWanted"/>: the line is how it is aimed). Elsewhere it fades in as it comes within
    ///             <see cref="LaserNearDeg"/> of a panel or a unit (fully drawn on it, not drawn at that angle or more) and is hidden.
    ///             With "Laser walking" off the cell cursor is not drawn either and the trigger on the ground walks nowhere
    ///             (LokiGame.cs OnFreeTrigger; the switch: LokiGame.W14Laser.cs). The ray itself and what it points at are as before.
    ///   Point 10 + answer 8   THE WALKING HAND'S LASER, on PANELS only (design note: "allow the player to have both controllers pointing at
    ///             things on UI panels ... so that I can click on the buttons very fast with both hands"; "Panel only for now"): a second
    ///             line from the walking hand (its aim pose, as the pointing hand's), drawn only while it is on a panel, and its trigger
    ///             presses the button under it (sliders: the knob goes to the point). That pull is then the panel's: the skill wheels
    ///             do not open from it (<see cref="LokiInput.Hand.ClaimForMenu"/>: until the trigger is let go). Anywhere else, and
    ///             on a panel's background with no button under it, that trigger keeps its jobs (the skill wheels; the ring's while
    ///             the ring is open, when this laser is off). Off while: the wheels are open, the ring is shown, that hand holds the
    ///             wand, opens a casting gesture or holds a panel. It never acts on the world, never takes an edge, never chooses
    ///             by the look, and skips the see-through minimap (the pointing hand's laser clicks that one: its point is read
    ///             from that laser's ray).
    /// </summary>
    public sealed partial class DioramaRig
    {
        /// <summary>The angle from a panel or a unit at which the laser starts to show (fully drawn on it), degrees.</summary>
        public const float LaserNearDeg = 8f;
        /// <summary>The fade's speed: from nothing to fully drawn in this many seconds at most.</summary>
        public const float LaserFadeSeconds = 0.08f;
        /// <summary>Under this the line is not drawn at all.</summary>
        public const float LaserAlphaMin = 0.02f;
        /// <summary>How far a panel or a unit may be for the fade-in (metres along the ray).</summary>
        public const float LaserNearRange = 20f;

        /// <summary>A ground skill waits to be aimed (LokiGame): the laser shows on the ground even with "Laser walking" off.</summary>
        public System.Func<bool> GroundAimWanted;
        /// <summary>How solid the pointing hand's line is now (0 to 1), why (smoke, log: "panel", "unit", "ground", "near", "none"), and the
        /// smallest angle to a panel or a unit when it was not on one (degrees; 180: none near).</summary>
        public float LaserAlpha { get; private set; } = 1f;
        public string LaserShownFor { get; private set; } = "none";
        public float LaserNearAngle { get; private set; } = 180f;
        /// <summary>The ground is drawn now (the line on it and the cell cursor).</summary>
        public bool GroundLaserShown { get; private set; } = true;

        private void UpdateLaserShown(Vector3 origin, Vector3 dir, bool onPanel, bool onUnit)
        {
            GroundLaserShown = Comfort.LaserWalk || (GroundAimWanted != null && GroundAimWanted());
            // client wave 18 (w18wand), point 11: the rule itself is LaserWant, shared with the wand's own line (LokiGame.W12Wand.cs)
            float want = LaserWant(origin, dir, onPanel, onUnit, PointedCell.HasValue, out var why, out float near);
            LaserShownFor = why; if (near >= 0f) LaserNearAngle = near;
            LaserAlpha = Mathf.MoveTowards(LaserAlpha, want, Time.unscaledDeltaTime / LaserFadeSeconds);
            if (!GroundLaserShown && _cursor.gameObject.activeSelf) _cursor.gameObject.SetActive(false);
            // the line's own colours (set just before) at this strength
            var a = _ray.startColor; a.a *= LaserAlpha; _ray.startColor = a;
            _ray.endColor = _laserCream ? new Color(a.r, a.g, a.b, UI.Theme.LaserEndAlpha * LaserAlpha) : new Color(1f, 0.9f, 0.4f, 0.1f * LaserAlpha); // (client wave 21: over cream the line keeps its colour and 35 % to its end)
        }

        /// <summary>Client wave 18 (w18wand), point 11 (design note: "If laser walking is turned off, the laser should not appear unless I am pointing
        /// close to a target. When I am holding the wand, just like it does with when I am not holding the wand."): point 20's rule for ANY
        /// line along a ray, as a strength to fade towards (0 to 1): fully drawn on a panel, a unit or a ground item, or a ground cell while
        /// the ground is shown (<see cref="GroundLaserShown"/>: "Laser walking" on, or a ground skill waiting to be aimed); elsewhere faded in
        /// within <see cref="LaserNearDeg"/> of a panel or a unit, nothing beyond. why: "panel", "unit", "ground", "near" or "none" (as
        /// <see cref="LaserShownFor"/>); near: the angle measured for the fade (degrees; -1 when the ray was on something).</summary>
        public float LaserWant(Vector3 origin, Vector3 dir, bool onPanel, bool onUnit, bool onCell, out string why, out float near)
        {
            near = -1f;
            bool ground = Comfort.LaserWalk || (GroundAimWanted != null && GroundAimWanted());
            if (onPanel) { why = "panel"; return 1f; }
            if (onUnit) { why = "unit"; return 1f; }
            if (onCell && ground) { why = "ground"; return 1f; }
            near = NearestAngle(origin, dir);
            float want = Mathf.Clamp01(1f - near / LaserNearDeg);
            why = want > 0f ? "near" : "none";
            return want;
        }

        /// <summary>The smallest angle between the ray and a shown panel or a unit / ground item ahead of it (degrees; 180: none).</summary>
        private static float NearestAngle(Vector3 origin, Vector3 dir)
        {
            float best = 180f;
            var panels = UI.UiKit.Panels;
            for (int i = 0; i < panels.Count; i++)
            {
                var p = panels[i];
                if (p == null || !p.gameObject.activeInHierarchy) continue;
                var cv = p.GetComponent<Canvas>(); if (cv != null && !cv.enabled) continue;
                var col = p.GetComponent<Collider>(); if (col == null || !col.enabled) continue;
                best = Mathf.Min(best, AngleTo(col, origin, dir));
            }
            var live = Sprites.Targetable.Live;
            for (int i = 0; i < live.Count; i++)
            {
                var t = live[i]; if (t == null) continue;
                var col = t.Col; if (col == null || !col.enabled || !col.gameObject.activeInHierarchy) continue;
                best = Mathf.Min(best, AngleTo(col, origin, dir));
            }
            return best;
        }

        /// <summary>The angle between the ray and the collider's point nearest to it (about: the point of the ray abreast of the collider's
        /// middle, then the collider's point nearest to that); 180 when it lies behind or beyond <see cref="LaserNearRange"/>.</summary>
        private static float AngleTo(Collider col, Vector3 origin, Vector3 dir)
        {
            var c = col.bounds.center; float along = Vector3.Dot(c - origin, dir);
            if (along <= 0.02f || along > LaserNearRange) return 180f;
            var q = col.ClosestPoint(origin + dir * along) - origin;
            return q.sqrMagnitude < 1e-10f ? 0f : Vector3.Angle(dir, q);
        }

        // ------------------------------------------------------------------ point 10: the walking hand's laser on panels
        private LineRenderer _offRay;
        private UI.UiButton _offHover;
        private float _offAlpha;
        private static readonly RaycastHit[] OffHits = new RaycastHit[96];
        /// <summary>The panel and the button the walking hand's laser is on (null: none), the point, and its presses so far (smoke, log).</summary>
        public Transform OffLaserPanel { get; private set; }
        // client wave 21 (w21kit): the laser is over a light surface of THE PANEL THEME (cream canvas, parchment)
        private bool _laserCream;
        private static bool OnCream(Transform panel) { if (panel == null) return false; var s = panel.GetComponentInParent<UI.UiSurface>(); return s != null && s.Light; }
        public UI.UiButton OffLaserButton { get; private set; }
        public Vector3 OffLaserPoint { get; private set; }
        public int OffClicks { get; private set; }
        /// <summary>The walking hand's line is drawn now; its ray this frame.</summary>
        public bool OffLaserDrawn => _offRay != null && _offRay.enabled;
        public Ray OffRay { get; private set; }
        /// <summary>The game may switch the walking hand's laser off (null: always allowed).</summary>
        public System.Func<bool> OffLaserAllowed;

        private void BuildOffLaser(Shader sh)
        {
            var go = new GameObject("OffPointerRay");
            go.transform.SetParent(transform, false);
            _offRay = go.AddComponent<LineRenderer>();
            _offRay.useWorldSpace = true; _offRay.positionCount = 2; _offRay.startWidth = 0.004f; _offRay.endWidth = 0.002f;
            _offRay.material = new Material(sh != null ? sh : Shader.Find("Sprites/Default")) { name = "LokiLaserOff", renderQueue = LaserQueue }; // (drawn over the panels, as the other)
            _offRay.sortingOrder = LaserSortingOrder;
            _offRay.enabled = false;
        }

        private void UpdateOffLaser(LokiInput.Hand l)
        {
            if (_offRay == null) return;
            var hand = OffHand;
            Vector3 origin = hand.position, dir = hand.forward;
            if (Comfort.LaserFromAim && l.TryAim(out var ap, out var ar)) { origin = transform.TransformPoint(ap); dir = transform.rotation * (ar * Vector3.forward); }
            OffRay = new Ray(origin, dir);
            bool allowed = !InputHeld && !l.WandHeld && !l.CastHeld && !l.GrabClaimed && !(BlockWorldClicks != null && BlockWorldClicks()) && (OffLaserAllowed == null || OffLaserAllowed());
            Transform panel = null; UI.UiButton button = null; Vector3 at = default;
            if (allowed)
            {
                // the nearest panel collider on the ray, whatever of the world lies in front of it (point 18's rule); not the see-through minimap
                int n = Physics.RaycastNonAlloc(OffRay, OffHits, 50f, UI.LookSelect.GazeMask, QueryTriggerInteraction.Ignore), best = -1;
                for (int i = 0; i < n; i++)
                {
                    if (best >= 0 && OffHits[i].distance >= OffHits[best].distance) continue;
                    var c = OffHits[i].collider;
                    if (c.GetComponent<UI.UiButton>() != null || c.GetComponent<UI.UiBlocker>() != null) best = i;
                }
                if (best >= 0)
                {
                    at = OffHits[best].point;
                    panel = UI.UiButton.PanelOf(OffHits[best].collider.transform);
                    button = OffHits[best].collider.GetComponent<UI.UiButton>();
                    // the button on top of that panel under the ray (a row and its Use button: VR/DioramaRig.Ui.cs TopButton's rule)
                    UI.UiButton top = null;
                    for (int i = 0; i < n; i++)
                    {
                        var b = OffHits[i].collider.GetComponent<UI.UiButton>();
                        if (b == null || !b.isActiveAndEnabled || b.Panel != panel) continue;
                        if (UI.UiButton.Outranks(b, top)) top = b;
                    }
                    if (top != null) button = top;
                }
            }
            OffLaserPanel = panel; OffLaserButton = button; OffLaserPoint = at;
            if (button != _offHover)
            {
                if (_offHover != null && _offHover != _hoverButton) _offHover.SetHover(false);
                _offHover = button;
                if (button != null) { button.SetHover(true); Haptics.Pulse(Haptics.Kind.Ui, false, 0.08f, 0.015f); }
            }
            bool pull = allowed && l.PullDown;
            if (pull && button != null)
            {
                OffClicks++;
                l.ClaimForMenu(); // this pull is the panel's: no skill wheels from it until the trigger is let go
                var sl = button.Slider;
                if (sl != null && sl.Track == button && button.Interactable) { sl.PressAt(at, true); sl.Commit(); Audio.LokiAudio.Instance?.PlayUi(); }
                else button.Click();
                Haptics.Pulse(Haptics.Kind.Ui, false, 0.25f, 0.03f);
                Debug.Log($"[Diorama] the walking hand's trigger on '{button.name}' of '{(panel != null ? panel.name : "?")}'");
            }
            // drawn only on a panel, with a short fade
            _offAlpha = Mathf.MoveTowards(_offAlpha, panel != null ? 1f : 0f, Time.unscaledDeltaTime / LaserFadeSeconds);
            bool draw = _offAlpha > LaserAlphaMin;
            if (_offRay.enabled != draw) _offRay.enabled = draw;
            if (draw)
            {
                if (panel != null) { _offRay.SetPosition(0, origin); _offRay.SetPosition(1, at); }
                else _offRay.SetPosition(0, origin); // (fading out: the end stays where it was)
                bool cream = OnCream(panel); // (client wave 21, w21kit: as the pointing hand's line)
                var c0 = button != null ? (cream ? UI.Theme.LaserOnButton : new Color(0.5f, 0.9f, 1f, 0.95f)) : cream ? UI.Theme.LaserIdle : new Color(1f, 0.9f, 0.4f, 0.9f);
                c0.a *= _offAlpha; _offRay.startColor = c0; _offRay.endColor = new Color(c0.r, c0.g, c0.b, (cream ? UI.Theme.LaserEndAlpha : 0.1f) * _offAlpha);
            }
        }
    }
}
