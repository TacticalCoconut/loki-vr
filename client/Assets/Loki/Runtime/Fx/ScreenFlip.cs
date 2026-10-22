using UnityEngine;

namespace Loki.Effects
{
    /// <summary>
    /// Client wave 5 (w5looks): Hallucination's upside-down view. rAthena sends EFST_ILLUSION 34 (SC_HALLUCINATION, status.yml:674-684; to
    /// the area unless battle_config display_hallucination is off: clif.cpp:6544, conf/battle/client.conf:104 "yes") and roBrowserLegacy (a
    /// reimplementation, the only source) turns the whole picture upside down while our own character has it: StatusIcons.update →
    /// ScreenEffectManager.parseStatus / cleanStatusEffect (UI/Components/StatusIcons/StatusIcons.js:153, 244; Renderer/ScreenEffectManager.js:
    /// 158-190) → VerticalFlip, a post-process that draws the frame with its texture coordinates inverted vertically
    /// (Renderer/Effects/Shaders/VerticalFlip.js). On another unit rB only sets entity.isHallucinating, which nothing reads
    /// (Engine/MapEngine/Entity.js:2295-2301): nothing is drawn for others.
    /// In a headset an upside-down picture is a strong comfort hazard, so Loki plays it only when the player turns on Settings → Comfort →
    /// "Hallucination turns the view upside down" (VR/Comfort.cs HallucinationFlip, off by default). Loki flips the head camera's projection
    /// (clip-space y negated, which is what inverting the frame's v coordinate shows) with the triangle winding inverted for the flipped
    /// view (GL.invertCulling); in a headset each eye's own projection is flipped (Camera.SetStereoProjectionMatrix). Checked on the
    /// desktop only: whether the OpenXR display honours a script's stereo projection is verified in the headset.
    /// </summary>
    public sealed class ScreenFlip : MonoBehaviour
    {
        /// <summary>Our own character has EFST_ILLUSION (set by LokiGame.W5Looks from 0x0983 / 0x0196 / 0x0984).</summary>
        public static bool Hallucinating;
        /// <summary>The flip is drawn now (hallucinating and the Comfort option on).</summary>
        public static bool Active => Hallucinating && VR.Comfort.HallucinationFlip;
        /// <summary>Frames rendered flipped (smoke).</summary>
        public static int FlippedFrames { get; private set; }
        /// <summary>The y scale of the projection used for the last frame (-1 flipped, 1 not; smoke).</summary>
        public static float LastProjectionY { get; private set; } = 1f;

        private static readonly Matrix4x4 Flip = Matrix4x4.Scale(new Vector3(1f, -1f, 1f));
        private Camera _cam; private bool _applied; private bool _culling;

        public static ScreenFlip Attach(Camera head)
        {
            if (head == null) return null;
            var f = head.GetComponent<ScreenFlip>();
            if (f == null) { f = head.gameObject.AddComponent<ScreenFlip>(); f._cam = head; }
            return f;
        }

        private void OnPreCull()
        {
            if (_cam == null) return;
            if (!Active)
            {
                if (_applied) { if (_cam.stereoEnabled) _cam.ResetStereoProjectionMatrices(); _cam.ResetProjectionMatrix(); _applied = false; }
                LastProjectionY = 1f;
                return;
            }
            // from the camera's own projection each frame (the headset's per-eye frustum, or the desktop field of view)
            if (_cam.stereoEnabled)
            {
                _cam.ResetStereoProjectionMatrices();
                var l = _cam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
                var r = _cam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right);
                _cam.SetStereoProjectionMatrix(Camera.StereoscopicEye.Left, Flip * l);
                _cam.SetStereoProjectionMatrix(Camera.StereoscopicEye.Right, Flip * r);
                LastProjectionY = Mathf.Sign((Flip * l).m11);
            }
            else
            {
                _cam.ResetProjectionMatrix();
                var p = Flip * _cam.projectionMatrix;
                _cam.projectionMatrix = p;
                LastProjectionY = Mathf.Sign(p.m11);
            }
            _applied = true; FlippedFrames++;
        }

        // a mirrored projection turns every triangle's winding: invert culling for this camera only
        private void OnPreRender() { if (_applied) { GL.invertCulling = true; _culling = true; } }
        private void OnPostRender() { if (_culling) { GL.invertCulling = false; _culling = false; } }

        private void OnDisable()
        {
            if (_culling) { GL.invertCulling = false; _culling = false; }
            if (_applied && _cam != null) { if (_cam.stereoEnabled) _cam.ResetStereoProjectionMatrices(); _cam.ResetProjectionMatrix(); _applied = false; }
        }
    }
}
