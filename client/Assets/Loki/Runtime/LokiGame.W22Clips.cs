using System.Collections.Generic;
using Loki.Game;
using Loki.Sprites;
using Loki.UI;
using Loki.VR;
using UnityEngine;
using UnityEngine.UI;
using Profile = Loki.VR.ControllerProfiles.Profile;

namespace Loki
{
    /// <summary>
    /// Client wave 22 (w22clips): points 53 and 59 of the design's notes after wave 17 (playtest notes).
    ///   Point 53  (design note: "Above every page of the tutorial, add a small, looping clip showing/explaining the action to be performed. This clip
    ///             should have 1.5s of inaction at the start and at the end of it, so it remains clear and understandable. Inaction does not
    ///             mean PAUSE THE VIDEO. It means nothing is being moved. So if a poring is in view, it continues playing its idle animation
    ///             because it is a video recording.") Over each step's text, in the guide's clip band (UI/TutorialGuide.cs Media), a picture
    ///             <see cref="TutorialClip.WidthPx"/> x <see cref="TutorialClip.HeightPx"/> px: the step's action seen through the eyes of
    ///             someone doing it, looping. NOT a stored video (frames of the game are the game's art: they could not be shipped): the
    ///             step's recorded SCRIPT (Game/ClipTimeline.cs, Resources/Clips/&lt;step&gt;.txt, recorded from a scripted run of the real
    ///             game by LokiGame.W22ClipsRec.cs) is played live over the player's own map by a second small camera (UI/TutorialClip.cs).
    ///             Each script begins and ends with <see cref="ClipTimeline.StillSeconds"/> s in which no hand moves and no button changes;
    ///             the world goes on living through them.
    ///   Point 59  (design note: "also add a ghostly 3D projection of the player's current controller (Valve index, Quest, etc), and have it simulate
    ///             what you're meant to do, as if it was those ghostly instructors that VR games usually use.") Beside the picture, the
    ///             controller(s) the step uses as a see-through 3D model of the profile in the player's hands (UI/ControllerGhost.cs: made
    ///             in code, one per family), acting the same script at the same moment (UI/TutorialClip.cs ClipGhostStand): the trigger,
    ///             the grip, the stick, the buttons, the finger, the wrist's turn and travel. A left-handed player gets the mirrored clip
    ///             and their own hand's controller; a step done with the look alone shows a ghost headset.
    ///   Which     the tutorial's step (<see cref="W18TutStepKey"/>); the look calibration started from Settings shows the calibration's.
    ///             A step with no script shows no band. Every script was recorded with Touch controllers; the ghost shows each input on
    ///             the player's own controller's part. Where a controller does a step ANOTHER way the step has a second script: the
    ///             Settings step through the menu (settings__ring.txt) for the controllers with no Settings button (Index, Vive, Windows
    ///             MR, the simple controller, hands); a family may have its own (&lt;step&gt;__&lt;family&gt;.txt; none today).
    ///   Cost      one camera of 448 x 252 px drawn 30 times a second, only while the guide shows a clip (TutorialClip.MeanRenderMs).
    /// Hooks (marked "client wave 22 (w22clips)"): LokiGame.cs (InitW22Clips after InitW20Tut, UpdateW22Clips before UpdateW18Tut, the
    /// smokes' dispatch), UI/TutorialGuide.cs (the band's room: the panel grows downward), World/SpectatorView.cs (its mask).
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>The band's parts, in the guide's pixels: the picture left, the ghost's plate right, each this size.</summary>
        public const float W22PictureW = TutorialClip.WidthPx, W22PictureH = TutorialClip.HeightPx, W22FramePx = 3f;
        /// <summary>The panel theme's own colours (UI/Theme.cs, client wave 21's kit): the ghost stands on a plate of the frame's dark wood (a
        /// see-through ghost needs a dark ground; the page is cream canvas), both parts in the frame's thin gold line.</summary>
        public static Color W22Plate => Theme.WoodDark;
        public static Color W22FrameColour => Theme.Gold;

        public TutorialClip W22Clip { get; private set; }
        public ClipGhostStand W22Ghost { get; private set; }
        public FloatingGhostStand W22FloatGhost { get; private set; }
        /// <summary>The script shown now ("": none), and how many times a script was started (smoke, log).</summary>
        public string W22ClipKey { get; private set; } = "";
        public int W22ClipStarts { get; private set; }
        public RawImage W22Picture => _w22Picture;
        public RectTransform W22GhostPlate => _w22Plate;
        private RawImage _w22Picture; private RectTransform _w22Plate, _w22ClipFrame, _w22GhostFrame; private Material _w22PictureMat;
        private Profile _w22Profile = (Profile)(-1); private bool _w22Left;
        private readonly Dictionary<string, ClipTimeline> _w22Cache = new Dictionary<string, ClipTimeline>();
        /// <summary>No clips (the recorder runs the tutorial without them: LokiGame.W22ClipsRec.cs).</summary>
        public bool W22ClipsOff { get; set; }
        /// <summary>The clip stands still at its moment (a test reads one moment of it: TutorialClip.Seek).</summary>
        public bool W22ClipHeld { get; set; }
        /// <summary>A test run's script in the place of the step's own (null: the step's own): the key of another step's script and the
        /// controller it is loaded for; the game itself loads it for the hand in force and plays it on the controller in the hands, as
        /// it does the step's own (client wave 23, the merge: w22clips D / E need ONE script with a face button, a stick and a trigger;
        /// the menu step's own has no trigger pull any more).</summary>

        private void InitW22Clips(LokiBootstrap boot)
        {
            var rig = boot.Rig; if (rig == null || W18TGuide == null) return;
            W22Clip = TutorialClip.Create(rig);
            W22Clip.Map = () => _boot.Map != null ? _boot.Map.transform : null;
            W22Clip.Gat = () => _boot.Map != null && _boot.Map.Current != null ? _boot.Map.Current.Gat : null;
            W22Clip.Assets = () => _boot.Assets;
            W22Clip.Self = () => Entities != null ? Entities.Self : null;
            W22Clip.RealPoring = () => _w18tDummy;
            W22Clip.RealOther = () => _wcxtLunatic; // (wave 28, wcodex: the Codex step's practice Lunatic)
            W22Clip.Units = () => Entities != null ? Entities.Units.Values : null;
            // the band's parts on the guide: the picture in its frame, the ghost's dark plate (a see-through ghost needs a dark ground)
            var media = W18TGuide.Media; float tw = TutorialGuide.WidthPx - 2f * TutorialGuide.Pad;
            float px = -tw * 0.5f + W22PictureW * 0.5f, gx = tw * 0.5f - W22PictureW * 0.5f;
            _w22ClipFrame = W22Raw(UiKit.Box(media, "clipFrame", new Vector2(px, 0f), new Vector2(W22PictureW, W22PictureH), W22FrameColour));
            var pgo = new GameObject("clipPicture", typeof(RectTransform)); pgo.layer = media.gameObject.layer; pgo.transform.SetParent(media, false);
            var prt = (RectTransform)pgo.transform; prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = new Vector2(px, 0f); prt.sizeDelta = new Vector2(W22PictureW - 2f * W22FramePx, W22PictureH - 2f * W22FramePx);
            _w22Picture = pgo.AddComponent<RawImage>(); _w22Picture.raycastTarget = false;
            var sh = Shader.Find("Loki/ClipView");
            if (sh != null) { _w22PictureMat = new Material(sh) { name = "LokiClipView" }; _w22Picture.material = _w22PictureMat; }
            else { Debug.LogWarning("[Clips] Loki/ClipView shader missing"); if (UiKit.Overlay != null) _w22Picture.material = UiKit.Overlay; }
            _w22GhostFrame = W22Raw(UiKit.Box(media, "ghostFrame", new Vector2(gx, 0f), new Vector2(W22PictureW, W22PictureH), W22FrameColour));
            _w22Plate = W22Raw(UiKit.Box(media, "ghostPlate", new Vector2(gx, 0f), new Vector2(W22PictureW - 2f * W22FramePx, W22PictureH - 2f * W22FramePx), W22Plate));
            W22Ghost = ClipGhostStand.Create(media, new Vector2(gx, 0f), new Vector2(W22PictureW - 24f, W22PictureH - 24f), TutorialGuide.MetresPerPx);
            W22FloatGhost = FloatingGhostStand.Create(rig.transform, () => rig.Head);
        }

        /// <summary>A box of the band drawn in the colour it was given (on the themed page a box is otherwise drawn as its light counterpart).</summary>
        private static RectTransform W22Raw(RectTransform box) { if (box.GetComponent<Image>() is ThemedImage ti) ti.Raw = true; return box; }

        /// <summary>A step's script: the profile family's own if there is one, else the step's; mirrored for a left-handed player. Null: none.</summary>
        public ClipTimeline W22Load(string key, Profile profile, bool leftHanded)
        {
            if (string.IsNullOrEmpty(key)) return null;
            string family = ControllerGhost.FamilyOf(profile).ToString().ToLowerInvariant();
            string id = key + "|" + family + (leftHanded ? "|L" : "");
            if (_w22Cache.TryGetValue(id, out var have)) return have;
            ClipTimeline tl = null;
            // (a controller with no Settings button opens Settings through the menu: that step's other script, "__ring")
            var fam = ControllerGhost.FamilyOf(profile);
            bool noSettingsButton = fam != ControllerGhost.Family.TouchRing && fam != ControllerGhost.Family.Touch && fam != ControllerGhost.Family.Reverb;
            foreach (string name in noSettingsButton ? new[] { key + "__" + family, key + "__ring", key } : new[] { key + "__" + family, key })
            {
                var ta = Resources.Load<TextAsset>("Clips/" + name); if (ta == null) continue;
                tl = ClipTimeline.Parse(ta.text);
                if (tl == null) Debug.LogWarning($"[Clips] Resources/Clips/{name}: not a clip of format {ClipTimeline.FormatVersion}");
                else break;
            }
            if (tl != null && leftHanded) tl = tl.Mirrored();
            _w22Cache[id] = tl;
            return tl;
        }

        /// <summary>The step whose clip the guide should show now; null: none.</summary>
        private string W22Wanted()
        {
            if (W22ClipsOff || W18TGuide == null) return null;
            if (W18TutRunning) return _w22Lesson != null ? _w22Lesson.Key : W18TutStepKey; // (a casting way's lesson: its own clip; the columns' panel has a clip in each column instead)
            if (_w20CalStandalone) return "calibrate";
            return null;
        }

        /// <summary>Resolves the floating 3D controller/headset ghost demonstration for the current tutorial step and phase.</summary>
        private FloatingGhostStand.DemoKind W22ResolveFloatDemo(string key, out bool atMenu, out float sideDeg)
        {
            atMenu = false;
            sideDeg = 0f;
            if (W22ClipsOff || (!W18TutRunning && !_w20CalStandalone)) return FloatingGhostStand.DemoKind.None;
            if (_w20CalStandalone) return FloatingGhostStand.DemoKind.HeadCalibrate;

            var st = _w22Lesson ?? (_w18tSteps != null && _w18tIndex >= 0 && _w18tIndex < _w18tSteps.Count ? _w18tSteps[_w18tIndex] : null);
            int ph = _w18tPhase;
            bool menuAwake = W13Overhead != null && W13Overhead.Usable;
            if (st != null && ph >= 0 && ph < st.Phases.Count && st.Phases[ph].AtMenu) atMenu = true;
            float sign = Comfort.LeftHanded ? -1f : 1f;

            switch (key)
            {
                case "welcome":
                case "end":
                    sideDeg = 18f * sign;
                    return FloatingGhostStand.DemoKind.TriggerTap;
                case "calibrate":
                    sideDeg = 20f * sign;
                    return FloatingGhostStand.DemoKind.HeadCalibrate;
                case "laser":
                    sideDeg = 16f * sign;
                    return FloatingGhostStand.DemoKind.LaserSweep;
                case "look":
                    sideDeg = 18f * sign;
                    return FloatingGhostStand.DemoKind.HeadLook;
                case "act":
                    sideDeg = 16f * sign;
                    return FloatingGhostStand.DemoKind.TriggerTap;
                case "walk":
                    sideDeg = 16f * sign;
                    return FloatingGhostStand.DemoKind.LaserWalk;
                case "stick":
                    sideDeg = -16f * sign;
                    return FloatingGhostStand.DemoKind.WalkStick;
                case "turn":
                    sideDeg = 16f * sign;
                    return FloatingGhostStand.DemoKind.TurnStick;
                case "views":
                    sideDeg = -16f * sign;
                    return FloatingGhostStand.DemoKind.ViewHoldStick;
                case "overhead":
                    atMenu = menuAwake;
                    return FloatingGhostStand.DemoKind.HeadLookUp;
                case "resize":
                {
                    if (_w23ResizeFree)
                    {
                        if (ph == 0) return FloatingGhostStand.DemoKind.MapPanGripStick;
                        if (ph == 1) return FloatingGhostStand.DemoKind.MapScaleTwoGrips;
                        return FloatingGhostStand.DemoKind.ViewHoldStick;
                    }
                    if (ph == 0) return FloatingGhostStand.DemoKind.HeadLookUp;
                    if (ph == 1) { atMenu = true; return FloatingGhostStand.DemoKind.TriggerTap; }
                    int total = st != null ? st.Phases.Count : 5;
                    if (ph == total - 1 && View != null && View.Mode != ViewController.ViewMode.Follow)
                        return FloatingGhostStand.DemoKind.ViewHoldStick;
                    if (ph == total - 2 || (ph == total - 1 && (View == null || View.Mode == ViewController.ViewMode.Follow)))
                        return FloatingGhostStand.DemoKind.ShakeHand;
                    if (ph == 2 && total >= 6) return FloatingGhostStand.DemoKind.MapPanGripStick;
                    return FloatingGhostStand.DemoKind.MapScaleTwoGrips;
                }
                case "reposition":
                {
                    if (ph == 0) return FloatingGhostStand.DemoKind.HeadLookUp;
                    if (ph == 1) { atMenu = true; return FloatingGhostStand.DemoKind.TriggerTap; }
                    if (ph == 2) return FloatingGhostStand.DemoKind.CarryGrip;
                    return FloatingGhostStand.DemoKind.ShakeHand;
                }
                case "panel":
                {
                    sideDeg = -14f * sign;
                    if (ph == 0 || ph == 1) return FloatingGhostStand.DemoKind.CarryGrip;
                    if (ph == 2 || ph == 4) return FloatingGhostStand.DemoKind.GrabAndShake;
                    return FloatingGhostStand.DemoKind.GrabAndTiltWrist;
                }
                case "menu":
                {
                    sideDeg = -14f * sign;
                    int total = st != null ? st.Phases.Count : 4;
                    if (ph == 0 || ph == total - 1) return FloatingGhostStand.DemoKind.MenuButtonTap;
                    if (W23Ring == W23RingHow.Twist && ph == 1) return FloatingGhostStand.DemoKind.RingWristTwist;
                    if (W23Ring == W23RingHow.Point) return ph == 1 ? FloatingGhostStand.DemoKind.LaserSweep : FloatingGhostStand.DemoKind.TriggerTap;
                    return FloatingGhostStand.DemoKind.RingStickSelect;
                }
                case "codex":
                {
                    if (ph == 0 || ph == 1) { sideDeg = 18f * sign; return FloatingGhostStand.DemoKind.HeadLook; }
                    int total = st != null ? st.Phases.Count : 5;
                    if (ph == total - 1) { sideDeg = 16f * sign; return FloatingGhostStand.DemoKind.TriggerTap; }
                    if (ph == 2) { sideDeg = -14f * sign; return FloatingGhostStand.DemoKind.MenuButtonTap; }
                    sideDeg = -14f * sign;
                    return FloatingGhostStand.DemoKind.RingStickSelect;
                }
                case "settings":
                {
                    int total = st != null ? st.Phases.Count : 3;
                    if (ph == total - 1) { sideDeg = 16f * sign; return FloatingGhostStand.DemoKind.TriggerTap; }
                    if (ph == total - 2) { sideDeg = 16f * sign; return FloatingGhostStand.DemoKind.HoverSteady; }
                    if (ph == 0) { sideDeg = -14f * sign; return FloatingGhostStand.DemoKind.MenuButtonTap; }
                    if (ph == total - 3 && total >= 5) { sideDeg = 16f * sign; return FloatingGhostStand.DemoKind.TriggerTap; }
                    sideDeg = -14f * sign;
                    return FloatingGhostStand.DemoKind.RingStickSelect;
                }
                case "casting":
                    sideDeg = 18f * sign;
                    return FloatingGhostStand.DemoKind.TriggerTap;
                case "cast-pad":
                    sideDeg = 16f * sign;
                    if (ph == 0) return FloatingGhostStand.DemoKind.CastPadOpenTouch;
                    return W18TPadSlapOn ? FloatingGhostStand.DemoKind.CastPadSlap : FloatingGhostStand.DemoKind.CastPadPalmFlip;
                case "cast-flick":
                    sideDeg = 16f * sign;
                    return FloatingGhostStand.DemoKind.CastFlickTwoStep;
                case "cast-wand":
                    sideDeg = 16f * sign;
                    if (ph == 0) return FloatingGhostStand.DemoKind.WandTakeFromHip;
                    if (ph == 1) return FloatingGhostStand.DemoKind.WandWheelSweep;
                    return FloatingGhostStand.DemoKind.WandReturnToHip;
                case "cast-elbow":
                    return ph == 0 ? FloatingGhostStand.DemoKind.ElbowTap : FloatingGhostStand.DemoKind.ForearmLaser;
                case "cast-wheels":
                    return FloatingGhostStand.DemoKind.SkillWheelsHoldAndPick;
                case "cast-bubbles":
                    if (ph == 0 || ph == 1) return FloatingGhostStand.DemoKind.HeadLookUp;
                    atMenu = true;
                    return FloatingGhostStand.DemoKind.TriggerTap;
                case "cast-skills":
                {
                    int total = st != null ? st.Phases.Count : 3;
                    if (ph == 0 || ph == total - 1) { sideDeg = -14f * sign; return FloatingGhostStand.DemoKind.MenuButtonTap; }
                    sideDeg = -14f * sign;
                    return FloatingGhostStand.DemoKind.RingStickSelect;
                }
                default:
                    return FloatingGhostStand.DemoKind.TriggerTap;
            }
        }

        /// <summary>Once a frame (LokiGame.cs Update, before the tutorial draws its guide): the right clip plays, in step with its ghost.</summary>
        private void UpdateW22Clips()
        {
            if (W22Clip == null) return;
            string key = W22Wanted();
            var profile = ComfortWindow.Active(); if (profile == Profile.None) profile = Profile.Index; // (as the tutorial's own lines: LokiGame.W18Tutorial.cs W18THow)
            bool left = Comfort.LeftHanded;

            // Update the 3D controller ghost instructor floating in front of the player
            if (W22FloatGhost != null)
            {
                bool smoke = false;
                if (smoke)
                {
                    W22FloatGhost.Clear();
                }
                else
                {
                    string stepKey = _w20CalStandalone ? "calibrate" : (_w22Lesson != null ? _w22Lesson.Key : W18TutStepKey);
                    var demo = W22ResolveFloatDemo(stepKey, out bool atMenu, out float sideDeg);
                    W22FloatGhost.Step(demo, profile, left, atMenu, sideDeg, Time.unscaledDeltaTime);
                }

                // During normal VR play, hide the flat ghost plate on the tutorial panel and center the video clip
                // so the controller ghost demonstrations float in front of the player instead of on the tutorial panel.
                bool showPanelGhost = smoke;
                if (_w22GhostFrame != null && _w22GhostFrame.gameObject.activeSelf != showPanelGhost) _w22GhostFrame.gameObject.SetActive(showPanelGhost);
                if (_w22Plate != null && _w22Plate.gameObject.activeSelf != showPanelGhost) _w22Plate.gameObject.SetActive(showPanelGhost);
                if (W22Ghost != null && W22Ghost.gameObject.activeSelf != showPanelGhost) W22Ghost.gameObject.SetActive(showPanelGhost);
                if (_w22ClipFrame != null && _w22Picture != null)
                {
                    float tw = TutorialGuide.WidthPx - 2f * TutorialGuide.Pad;
                    float wantX = showPanelGhost ? (-tw * 0.5f + W22PictureW * 0.5f) : 0f;
                    if (Mathf.Abs(_w22ClipFrame.anchoredPosition.x - wantX) > 0.5f)
                    {
                        _w22ClipFrame.anchoredPosition = new Vector2(wantX, 0f);
                        _w22Picture.rectTransform.anchoredPosition = new Vector2(wantX, 0f);
                    }
                }
            }

            var tl = key != null ? W22Load(key, profile, left) : null;
            if (tl == null)
            {
                if (W22ClipKey.Length > 0 || W22Clip.Playing) { W22Clip.Stop(); W22Ghost.Clear(); W22ClipKey = ""; Debug.Log("[Clips] no clip shown"); }
                if (W18TGuide.MediaPx != 0f) W18TGuide.MediaPx = 0f;
                return;
            }
            if (W18TGuide.MediaPx != W22PictureH) W18TGuide.MediaPx = W22PictureH;
            if (key != W22ClipKey || profile != _w22Profile || left != _w22Left || W22Clip.Clip != tl)
            {
                W22ClipKey = key; _w22Profile = profile; _w22Left = left; W22ClipStarts++;
                W22Clip.Focus = W22FocusOf(key); // (client wave 22: a casting way's lesson is framed on its hands and its tool, as its column's picture is)
                W22Clip.Play(tl, profile); W22Ghost.Show(tl, profile);
                _w22Picture.texture = W22Clip.Texture;
                Debug.Log($"[Clips] step '{key}': a clip of {tl.Length:0.0} s ({(tl.UsesLeft ? "L" : "")}{(tl.UsesRight ? "R" : "")}{(!tl.UsesLeft && !tl.UsesRight ? "the look" : "")}{(left ? ", mirrored" : "")}), the ghost a {ControllerGhost.FamilyOf(profile)} ({ControllerProfiles.NameOf(profile)}), fit {W22Ghost.FitScale:0.00}");
            }
            // the picture is drawn only while the guide is there to show it
            bool shown = W18TGuide.Visible;
            if (W22Clip.Paused != (!shown || W22ClipHeld)) W22Clip.Paused = !shown || W22ClipHeld;
            if (W22Clip.enabled != shown) W22Clip.enabled = shown;
            W22Ghost.At(W22Clip.ClipTime);
        }
    }
}
