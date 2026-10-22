using System;
using System.Collections.Generic;
using Loki.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 3b (w3data): the VR panel shown while the first-run builder (Data/ClientDataBuilder) makes the game's tables
    /// from the player's own Ragnarok Online files — on the first start after the RO folder was chosen, and again when the GRF
    /// changed. It needs nothing from the tables (OS font, built-in shaders), lists every step with its state and goes away by
    /// itself when the game can start.
    /// Client wave 7 (w7data): it also shows the install's patch ("Ragnarok Online data: patch N"), names the tables that were
    /// kept from the last good set and why (<see cref="SetNotice"/>), and has a second form (<see cref="ShowWait"/>) for a start
    /// that waits because the game files are still being written.
    /// </summary>
    public sealed class DataSetupPanel : MonoBehaviour
    {
        private const float W = 1100, H = 1020, WaitH = 520; // H: 1020 since client wave 20 (w20rooms): a 14th step row ("Indoor rooms") clears the bar (980 fit 13)
        /// <summary>Client wave 4b (w4lang): the step names of Data/ClientDataBuilder.cs (English there: that file is also built by the .NET
        /// harness, which has no Lang) as table keys; the panel shows each one in the current language. A new step needs its line here.</summary>
        private static readonly string[] StepNameKeys = { Lang.N("Messages and map names"), Lang.N("Skills and skill trees"), Lang.N("Random options"), Lang.N("World map"),
            Lang.N("Sprite names"), Lang.N("Status icons"), Lang.N("Items and quests"), Lang.N("Achievements and titles"), Lang.N("Emotes and buff icons"),
            Lang.N("Equipment looks"), Lang.N("Monster attack effects"), Lang.N("Sky and navigation"), Lang.N("Hat effects"),
            Lang.N("Indoor rooms") }; // (client wave 20, w20rooms: the room table, Data/RoomTools.cs)
        public static string StepName(string english) => Lang.Again(english);
        private Camera _head; private bool _placedTracked;
        private Text _title, _status, _notice, _waitText;
        private readonly List<Text> _rows = new List<Text>();
        private RectTransform _bar;
        private UiButton _again, _quit;
        public static DataSetupPanel Current { get; private set; }
        /// <summary>True for the waiting form (no step list).</summary>
        public bool Waiting { get; private set; }
        public string StatusText => _status != null ? _status.text : "";
        public string NoticeText => _notice != null ? _notice.text : "";
        public string WaitText => _waitText != null ? _waitText.text : "";
        public bool WaitButtonsShown => _again != null && _again.gameObject.activeSelf;

        /// <param name="patch">the install's patch line ("Ragnarok Online data: patch 1486"), already in the player's language; null for none</param>
        public static DataSetupPanel Show(Transform parent, Camera head, string folder, string why, string patch = null)
        {
            var rt = UiKit.Panel("DataSetupPanel", parent, new Vector2(W, H), 0.0007f);
            var p = rt.gameObject.AddComponent<DataSetupPanel>();
            p._head = head; Current = p;
            p.Build(rt, folder, why, patch);
            p.PlaceInFront();
            return p;
        }

        /// <summary>The waiting form: the game files are still being written (a patch in progress); <see cref="SetWait"/> says which
        /// file and for how long, <see cref="ShowWaitButtons"/> offers "Try again" / "Quit" once the wait's bound is reached.</summary>
        public static DataSetupPanel ShowWait(Transform parent, Camera head, string folder, Action tryAgain, Action quit)
        {
            var rt = UiKit.Panel("DataSetupPanel", parent, new Vector2(W, WaitH), 0.0007f);
            var p = rt.gameObject.AddComponent<DataSetupPanel>();
            p._head = head; p.Waiting = true; Current = p;
            float top = WaitH / 2;
            p._title = UiKit.TitlePlate(rt, Lang.T("Waiting for the game files"), 36, W - 160).Text; // client wave 21 (w21kit): on the gold plate (the same 36 px letters)
            UiKit.Label(rt,
                Lang.T("Your Ragnarok Online files were changed a moment ago, or are still being changed (the official patcher?). Loki never reads half-written game files: it starts by itself once they have been still for a minute.") +
                "\n" + Lang.F("Folder:  <color=#ffd060>{0}</color>", Esc(folder)),
                new Vector2(0, top - 165), new Vector2(W - 80, 160), 22, TextAnchor.UpperLeft, UiKit.Dim);
            p._waitText = UiKit.Label(rt, "", new Vector2(0, top - 310), new Vector2(W - 80, 100), 26, TextAnchor.MiddleCenter, new Color(1f, 0.8f, 0.5f));
            p._again = UiKit.Button(rt, Lang.T("Try again"), new Vector2(-180, -top + 60), new Vector2(300, 66), () => tryAgain?.Invoke(), 28);
            p._quit = UiKit.Button(rt, Lang.T("Quit"), new Vector2(180, -top + 60), new Vector2(300, 66), () => quit?.Invoke(), 28);
            p._again.gameObject.SetActive(false); p._quit.gameObject.SetActive(false);
            p.PlaceInFront();
            return p;
        }

        public void SetWait(string text) { if (_waitText != null) _waitText.text = Esc(text); }
        public void ShowWaitButtons() { if (_again != null) { _again.gameObject.SetActive(true); _quit.gameObject.SetActive(true); } }

        private static string Esc(string s) => (s ?? "").Replace("<", "<​");

        private void Build(RectTransform rt, string folder, string why, string patch)
        {
            float top = H / 2;
            _title = UiKit.TitlePlate(rt, Lang.T("Preparing the game data"), 36, W - 160).Text; // client wave 21 (w21kit): on the gold plate (the same 36 px letters)
            UiKit.Label(rt,
                Lang.F("Loki reads its item, skill, map and text tables from your own Ragnarok Online files; they are never shipped with Loki. This happens once, and again when the game is patched ({0}).", Esc(why)) +
                "\n" + Lang.F("Folder:  <color=#ffd060>{0}</color>", Esc(folder)) +
                (string.IsNullOrEmpty(patch) ? "" : "\n" + "<color=#ffd060>" + Esc(patch) + "</color>"),
                new Vector2(0, top - 155), new Vector2(W - 80, 150), 22, TextAnchor.UpperLeft, UiKit.Dim);
            float y = top - 260;
            foreach (var s in ClientDataBuilder.Steps)
            {
                _rows.Add(UiKit.Label(rt, "", new Vector2(0, y), new Vector2(W - 100, 36), 24, TextAnchor.MiddleLeft));
                y -= 38;
            }
            UiKit.Box(rt, "barbg", new Vector2(0, -top + 215), new Vector2(W - 100, 22), new Color(0.2f, 0.22f, 0.3f, 1f));
            _bar = UiKit.Box(rt, "bar", new Vector2(0, -top + 215), new Vector2(0, 22), new Color(0.55f, 0.8f, 0.45f, 1f));
            // client wave 7 (w7data): the tables kept from the last good set, each with its reason
            _notice = UiKit.Label(rt, "", new Vector2(0, -top + 140), new Vector2(W - 80, 110), 20, TextAnchor.UpperLeft, new Color(1f, 0.72f, 0.55f));
            _notice.resizeTextForBestFit = true; _notice.resizeTextMinSize = 13; _notice.resizeTextMaxSize = 20; // several tables with long reasons shrink instead of being cut
            _status = UiKit.Label(rt, "", new Vector2(0, -top + 50), new Vector2(W - 80, 50), 22, TextAnchor.MiddleCenter, new Color(1f, 0.8f, 0.5f));
            _status.resizeTextForBestFit = true; _status.resizeTextMinSize = 14; _status.resizeTextMaxSize = 22;
            Refresh(-1, null, new List<string>(), false);
        }

        /// <summary>Shows the builder's progress: step <paramref name="index"/> running, <paramref name="failed"/> the tools that failed.</summary>
        public void Refresh(int index, string status, List<string> failed, bool done)
        {
            if (Waiting) return;
            var steps = ClientDataBuilder.Steps;
            for (int i = 0; i < steps.Length; i++)
            {
                bool bad = failed.Contains(steps[i].Tool);
                string mark = bad ? Lang.T("<color=#ff7a6a>failed</color>") : i < index || done ? Lang.T("<color=#a8e6a3>done</color>") : i == index ? Lang.T("<color=#ffd060>working…</color>") : Lang.T("<color=#7f8799>waiting</color>");
                _rows[i].text = $"{StepName(steps[i].Name)}   {mark}";
            }
            float frac = done ? 1f : Mathf.Clamp01(index / (float)steps.Length);
            _bar.sizeDelta = new Vector2((W - 100) * frac, 22);
            _bar.anchoredPosition = new Vector2(-(W - 100) * (1 - frac) / 2, _bar.anchoredPosition.y);
            if (status != null) _status.text = Esc(status);
        }

        /// <summary>The notice under the bar (w7data): which tables were kept from before and why, and that Loki tries again at the next start.</summary>
        public void SetNotice(string text) { if (_notice != null) _notice.text = Esc(text); }

        public void PlaceInFront()
        {
            if (_head == null) return;
            var f = _head.transform.forward; f.y = 0; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
            transform.position = _head.transform.position + f * 0.95f + Vector3.up * 0.02f;
            UiKit.FaceHead(transform, _head);
        }

        private void LateUpdate()
        {
            if (!_placedTracked && _head != null && _head.transform.localPosition.sqrMagnitude > 0.01f) { _placedTracked = true; PlaceInFront(); }
        }

        public void Close() { if (Current == this) Current = null; Destroy(gameObject); }

        private void OnDestroy() { if (Current == this) Current = null; }
    }
}
