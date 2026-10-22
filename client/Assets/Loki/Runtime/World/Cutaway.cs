using System;
using Loki.Sprites;
using UnityEngine;

namespace Loki.World
{
    /// <summary>
    /// Keeps your character visible: every frame the shaders get a cone from the character's chest to your eyes, and
    /// ground walls and RSM models inside it and above the character's feet are not drawn (LokiRoomMask.cginc,
    /// LokiCutaway — forward passes only, so shadows are unchanged). On in the table and follow views, off in first person.
    /// Client wave 19 (w19world), point 45: the ground's floor tiles are never cut (a hole to the sky); its walls still are.
    /// </summary>
    [DefaultExecutionOrder(1000)] // after the view controller has placed the diorama this frame
    public sealed class Cutaway : MonoBehaviour
    {
        /// <summary>Cone radius at the character, how far above its feet cutting starts, and indoor ceiling height, in map cells.</summary>
        public float RadiusCells = 3f, FeetMarginCells = 0.5f, ChestCells = 1f, CeilingCells = 6.5f, HeadGuardMeters = 0.22f;
        private Func<Unit> _self; private Func<bool> _enabled; private Func<bool> _indoor; private Transform _map;
        private static readonly int ParamsId = Shader.PropertyToID("_LokiCutParams"), AId = Shader.PropertyToID("_LokiCutA"), BId = Shader.PropertyToID("_LokiCutB");

        public void Init(Transform mapRoot, Func<Unit> self, Func<bool> enabled, Func<bool> indoor = null) { _map = mapRoot; _self = self; _enabled = enabled; _indoor = indoor; Off(); }

        private static void Off() { Shader.SetGlobalVector(ParamsId, Vector4.zero); Shader.SetGlobalVector(BId, Vector4.zero); }

        private void LateUpdate()
        {
            var self = _self?.Invoke(); var cam = Camera.main;
            if (self == null || cam == null || _map == null) { Off(); return; }
            bool cone = _enabled == null || _enabled();
            bool indoor = cone && _indoor != null && _indoor();
            float cell = Mathf.Abs(_map.lossyScale.x); // map-local units are GAT cells
            var feet = self.transform.position;
            var chest = feet + Vector3.up * (ChestCells * cell);
            var eye = cam.transform.position;
            float minY = feet.y + FeetMarginCells * cell;
            float ceilY = indoor ? feet.y + CeilingCells * cell : 0f;
            Shader.SetGlobalVector(AId, chest);
            Shader.SetGlobalVector(BId, new Vector4(eye.x, eye.y, eye.z, HeadGuardMeters));
            Shader.SetGlobalVector(ParamsId, new Vector4(cone ? 1f : 0f, RadiusCells * cell, minY, ceilY));
        }

        private void OnDisable() => Off();
    }
}
