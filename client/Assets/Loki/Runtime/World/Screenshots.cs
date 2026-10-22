using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Loki.World
{
    /// <summary>
    /// Client wave 4 (w4world): a screenshot of what the headset shows, as a PNG in the player's Pictures\Loki folder
    /// (Environment.SpecialFolder.MyPictures + "Loki", made when missing; <c>-lokiPicturesDir &lt;folder&gt;</c> sends test shots elsewhere).
    /// A camera at the head's pose with the headset eye's vertical field of view renders once into a 1920 × 1080 texture (the eye's own
    /// image is square-ish and two of them; a photo is one 16:9 picture), with the wrist menu that pressed the button hidden for that
    /// render. On the desktop (no headset) the head camera is the window's camera: the same code, the same picture.
    /// File names: Loki_yyyy-MM-dd_HH-mm-ss.png (a counter when two land in one second).
    /// </summary>
    public static class Screenshots
    {
        public const int Width = 1920, Height = 1080;

        public static string Folder
        {
            get
            {
                var a = Environment.GetCommandLineArgs();
                for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-lokiPicturesDir" && !string.IsNullOrWhiteSpace(a[i + 1])) return a[i + 1];
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Loki");
            }
        }
        /// <summary>The player's Pictures\Loki whatever -lokiPicturesDir says (smoke: the default is logged, not written to).</summary>
        public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Loki");

        /// <summary>Vertical field of view of what the head camera shows: the left eye's projection when a headset renders, else the camera's.</summary>
        public static float HeadFov(Camera head)
        {
            if (head != null && head.stereoEnabled)
            {
                var p = head.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
                if (p.m11 > 1e-4f) return 2f * Mathf.Atan(1f / p.m11) * Mathf.Rad2Deg;
            }
            return head != null ? head.fieldOfView : 60f;
        }

        /// <summary>Renders the head's view and writes the PNG; returns its path (null on failure, with the reason in <paramref name="error"/>).</summary>
        public static string Take(Camera head, IEnumerable<GameObject> hide, out string error)
        {
            error = null;
            if (head == null) { error = Lang.T("no head camera"); return null; }
            string dir = Folder;
            var hidden = new List<GameObject>();
            if (hide != null) foreach (var g in hide) if (g != null && g.activeSelf) { g.SetActive(false); hidden.Add(g); }
            var go = new GameObject("screenshot-camera");
            RenderTexture rt = null; Texture2D tex = null;
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.CopyFrom(head);
                cam.stereoTargetEye = StereoTargetEyeMask.None;
                cam.transform.SetPositionAndRotation(head.transform.position, head.transform.rotation);
                cam.fieldOfView = HeadFov(head);
                rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt; cam.aspect = (float)Width / Height; cam.enabled = false;
                cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt;
                tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); tex.Apply(false);
                RenderTexture.active = prev;
                cam.targetTexture = null;
                Directory.CreateDirectory(dir);
                string name = "Loki_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string path = Path.Combine(dir, name + ".png");
                for (int n = 2; File.Exists(path); n++) path = Path.Combine(dir, name + "_" + n + ".png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Debug.Log($"[Screenshot] {path} ({Width} x {Height}, field of view {cam.fieldOfView:0.#}°{(head.stereoEnabled ? ", headset eye" : "")})");
                return path;
            }
            catch (Exception e) { error = e.Message; Debug.LogWarning("[Screenshot] failed: " + e); return null; }
            finally
            {
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (tex != null) UnityEngine.Object.Destroy(tex);
                UnityEngine.Object.Destroy(go);
                foreach (var g in hidden) if (g != null) g.SetActive(true);
            }
        }
    }
}
