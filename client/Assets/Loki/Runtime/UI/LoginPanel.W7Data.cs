using System;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 7 (w7data): two small lines at the bottom right of the login screen, beside "Quit" — the install's patch
    /// ("Ragnarok Online data: patch 1486": what a player and the host compare when something looks different), and, only when
    /// the start found something, what it found (tables kept from the last good set, an archive that was skipped, start-up checks
    /// that failed). The game hands the texts over (LokiGame.W7Data.cs); nothing here blocks the login.
    /// </summary>
    public sealed partial class LoginPanel
    {
        /// <summary>[0] the patch line, [1] the notice ("" when all is well); set by the game.</summary>
        public Func<string[]> W7DataLines;
        private Text _w7Patch, _w7Note;

        private void BuildW7Data(RectTransform p, float top)
        {
            _w7Patch = UiKit.Label(p, "", new Vector2(332, -top + 56), new Vector2(390, 26), 16, TextAnchor.MiddleCenter, UiKit.Dim);
            _w7Patch.resizeTextForBestFit = true; _w7Patch.resizeTextMinSize = 11; _w7Patch.resizeTextMaxSize = 16;
            _w7Note = UiKit.Label(p, "", new Vector2(332, -top + 26), new Vector2(390, 36), 14, TextAnchor.MiddleCenter, new Color(1f, 0.72f, 0.55f));
            _w7Note.resizeTextForBestFit = true; _w7Note.resizeTextMinSize = 10; _w7Note.resizeTextMaxSize = 14; // a long notice shrinks instead of being cut
            Lang.Changed += RefreshW7Data; // the texts are made by the game in the player's language: made again when it changes
        }

        /// <summary>Redraws the two lines (the game sets <see cref="W7DataLines"/> after the panel exists).</summary>
        public void RefreshW7Data()
        {
            if (_w7Patch == null) return;
            string[] l = null;
            try { l = W7DataLines != null ? W7DataLines() : null; } catch (Exception e) { Debug.LogWarning("[Login] game data lines: " + e.Message); }
            _w7Patch.text = l != null && l.Length > 0 ? Esc(l[0]) : "";
            _w7Note.text = l != null && l.Length > 1 ? Esc(l[1]) : "";
        }

        // ---- smoke hooks
        public string W7DataPatchText => _w7Patch != null ? _w7Patch.text : "";
        public string W7DataNoteText => _w7Note != null ? _w7Note.text : "";
    }
}
