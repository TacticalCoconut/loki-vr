using System;
using System.Collections.Generic;
using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 5 (w5ui): each character list row shows its character (UI/CharRowPreview.cs) in the free space at the row's left, before
    /// the text (BuildChars leaves 60 px there; the text keeps its box). Hooks: LoginPanel.Build (BuildW5Ui) and the end of RefreshChars
    /// (RefreshW5Previews), one line each.
    /// </summary>
    public sealed partial class LoginPanel
    {
        private readonly List<CharRowPreview> _w5Previews = new List<CharRowPreview>();
        private static readonly Vector2 W5PreviewBox = new Vector2(56, 74);

        private void BuildW5Ui()
        {
            float rowW = W - 120;
            foreach (var (row, _, _, _) in _charRows)
                _w5Previews.Add(CharRowPreview.Create(row, new Vector2(-rowW / 2 + 2 + W5PreviewBox.x / 2, 0), W5PreviewBox));
        }

        /// <summary>The rows' pictures for the page shown (rB's poses: standing, sitting while a deletion is scheduled, dead once due).</summary>
        private void RefreshW5Previews(List<Loki.Net.CharInfo> list, long now)
        {
            for (int r = 0; r < _w5Previews.Count; r++)
            {
                int i = _charPage * CharsPerPage + r;
                var c = i < list.Count ? list[i] : null;
                var pose = c == null || c.DeleteAt == 0 ? CharRowPreview.Pose.Idle : now < c.DeleteAt ? CharRowPreview.Pose.Sit : CharRowPreview.Pose.Dead;
                _w5Previews[r].Show(c, pose);
            }
        }

    }
}
