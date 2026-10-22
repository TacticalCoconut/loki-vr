using System;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 11 (w11menu): a slider for the VR pointer (the UI kit had none; specified after the 2026-10-06 playtest for the
    /// gaze calibration: "just Provide a slider"). A track with a knob and a mark at the middle of its range. Three ways to move it:
    ///   the laser: a trigger pull on the track puts the knob there, and while the trigger stays held the knob follows the laser
    ///              (VR/DioramaRig.Ui.cs drags it; the value is kept, <see cref="Spec.Commit"/>, when the trigger is let go);
    ///   the stick: while the laser or the gaze is on the track, the pointer hand's stick pushed left / right steps it (it repeats,
    ///              faster after a moment); the world does not turn meanwhile;
    ///   the gaze:  looking at the track and pulling the trigger steps it once towards the side looked at (a gaze is not steady
    ///              enough to place a knob); a row's − / + buttons step it too (<see cref="Step"/>).
    /// The owner gives the range, the step and how to read / write the value (<see cref="Bind"/>).
    /// </summary>
    public sealed class UiSlider : MonoBehaviour, IUiStick
    {
        public sealed class Spec
        {
            public float Min, Max = 1f, Step = 0.1f;
            public Func<float> Get; public Action<float> Set;
            /// <summary>The value is final for now (the laser let go, a step was made): the owner saves it.</summary>
            public Action Commit;
        }

        /// <summary>Client wave 21 (w21kit): the track's groove on the theme's canvas.</summary>
        public static readonly Color TrackOnCanvas = new Color(0.33f, 0.19f, 0.10f, 0.20f);
        public UiButton Track { get; private set; }
        private RectTransform _rt, _knob; private Spec _spec; private float _width;
        private float _repeatAt, _heldFor; private bool _stickDirty;
        /// <summary>Steps made by the stick, the laser's placements and the gaze steps (smoke, log).</summary>
        public int StickSteps { get; private set; }
        public int Placed { get; private set; }
        public int GazeSteps { get; private set; }
        public float Value => _spec != null && _spec.Get != null ? _spec.Get() : 0f;
        public bool Bound => _spec != null;

        public static UiSlider Create(RectTransform parent, Vector2 pos, Vector2 size)
        {
            var b = UiKit.Button(parent, "", pos, size, null, 12);
            b.name = "slider";
            // client wave 21 (w21kit): on the theme's canvas the track is a leather-tinted groove, the rail dark ink, the knob gold
            bool themed = b.IsThemed;
            if (themed) { b.Normal = TrackOnCanvas; b.Hover = Theme.CellHover; }
            else { b.Normal = new Color(0.03f, 0.04f, 0.07f, 0.9f); b.Hover = new Color(0.16f, 0.22f, 0.38f, 1f); }
            b.SetHover(false);
            var s = b.gameObject.AddComponent<UiSlider>();
            s.Track = b; s._rt = (RectTransform)b.transform; s._width = size.x;
            b.Slider = s; b.Stick = s;
            UiKit.Box(s._rt, "rail", Vector2.zero, new Vector2(size.x - 24, 4), themed ? new Color(Theme.Ink.r, Theme.Ink.g, Theme.Ink.b, 0.8f) : new Color(0.55f, 0.6f, 0.72f, 0.9f));
            UiKit.Box(s._rt, "mid", Vector2.zero, new Vector2(3, size.y - 6), themed ? Theme.GoldDark : new Color(0.85f, 0.7f, 0.35f, 0.9f));
            s._knob = UiKit.Box(s._rt, "knob", Vector2.zero, new Vector2(16, size.y - 4), themed ? Theme.Ink : new Color(0.95f, 0.95f, 0.92f, 1f));
            if (themed) UiKit.Box(s._knob, "gold", Vector2.zero, new Vector2(10, size.y - 10), Theme.Lit); // a gold knob with a dark edge: gold alone is faint on cream
            return s;
        }

        public void Bind(Spec spec) { _spec = spec; Refresh(); }

        /// <summary>Client wave 13, item 5 (w13ring): a new place and width for the track (a settings row's text column).</summary>
        public void Place(Vector2 pos, float width)
        {
            if (_rt.anchoredPosition != pos) _rt.anchoredPosition = pos;
            if (Mathf.Abs(width - _width) < 0.5f) return;
            _width = width;
            Track.Resize(new Vector2(width, _rt.sizeDelta.y));
            var rail = (RectTransform)_rt.Find("rail"); if (rail != null) rail.sizeDelta = new Vector2(width - 24, rail.sizeDelta.y);
            Refresh();
        }

        /// <summary>The knob's place for the value now.</summary>
        public void Refresh()
        {
            if (_spec == null || _knob == null) return;
            float span = Mathf.Max(_spec.Max - _spec.Min, 1e-6f);
            float t = Mathf.Clamp01((Value - _spec.Min) / span);
            _knob.anchoredPosition = new Vector2((t - 0.5f) * Travel, 0);
        }
        private float Travel => _width - 24f;

        private float Snap(float v)
        {
            if (_spec == null) return v;
            float st = Mathf.Max(_spec.Step, 1e-6f);
            v = _spec.Min + Mathf.Round((v - _spec.Min) / st) * st;
            return Mathf.Clamp(v, _spec.Min, _spec.Max);
        }

        private bool Write(float v)
        {
            if (_spec == null || _spec.Set == null) return false;
            v = Snap(v);
            if (Mathf.Abs(v - Value) < 1e-6f) return false;
            _spec.Set(v); Refresh();
            return true;
        }

        /// <summary>One step down (-1) or up (+1), kept at once (the − / + buttons, a gaze step).</summary>
        public bool Step(int dir)
        {
            if (_spec == null) return false;
            bool moved = Write(Value + dir * _spec.Step);
            if (moved) _spec.Commit?.Invoke();
            return moved;
        }

        /// <summary>Where a ray meets the track's plane.</summary>
        public bool RayPoint(Ray ray, out Vector3 point)
        {
            point = default;
            var plane = new Plane(_rt.forward, _rt.position);
            if (!plane.Raycast(ray, out float enter)) return false;
            point = ray.GetPoint(enter);
            return true;
        }

        private float ValueAt(Vector3 world)
        {
            float x = _rt.InverseTransformPoint(world).x;
            float t = Mathf.Clamp01(x / Mathf.Max(Travel, 1f) + 0.5f);
            return _spec.Min + t * (_spec.Max - _spec.Min);
        }

        /// <summary>The trigger was pulled on the track: the laser places the knob (the rig then drags it), the gaze steps it once
        /// towards the side of the knob that is looked at.</summary>
        public void PressAt(Vector3 world, bool byLaser)
        {
            if (_spec == null || !Track.Interactable) return; // (client wave 25, w25tut: a track that cannot be pressed now takes nothing)
            if (byLaser) { Placed++; Write(ValueAt(world)); return; }
            float x = _rt.InverseTransformPoint(world).x;
            int dir = x > _knob.anchoredPosition.x ? 1 : -1;
            if (Step(dir)) GazeSteps++;
        }

        /// <summary>While the laser's trigger stays held: the knob follows it.</summary>
        public void DragTo(Vector3 world) { if (_spec != null && Track.Interactable) Write(ValueAt(world)); }
        public void Commit() { _spec?.Commit?.Invoke(); }

        /// <summary>The pointer hand's stick while the laser or the gaze is on the track: left / right steps, repeating.</summary>
        public bool OnStick(Vector2 stick, float dt)
        {
            if (_spec == null || !Track.Interactable) return false; // (client wave 25, w25tut: nor the stick: it turns the world, as off any slider)
            float x = stick.x;
            if (Mathf.Abs(x) < 0.5f)
            {
                _heldFor = 0f; _repeatAt = 0f;
                if (_stickDirty) { _stickDirty = false; _spec.Commit?.Invoke(); } // the stick came back: keep the value
                return Mathf.Abs(x) > 0.3f; // still off centre: not a turn yet
            }
            _heldFor += dt;
            if (_heldFor >= _repeatAt)
            {
                _repeatAt = _heldFor + (_heldFor > 1.2f ? 0.08f : 0.28f);
                if (Write(Value + Mathf.Sign(x) * _spec.Step)) { StickSteps++; _stickDirty = true; }
            }
            return true;
        }

        private void OnDisable() { _heldFor = 0f; _repeatAt = 0f; if (_stickDirty) { _stickDirty = false; _spec?.Commit?.Invoke(); } }
    }
}
