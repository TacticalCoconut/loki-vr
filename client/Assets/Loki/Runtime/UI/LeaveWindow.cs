using System;
using System.Collections.Generic;
using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// "Leave the game" (wrist menu → View): back to the character list, log out to the login screen, or quit Loki — the in-VR
    /// replacement for the original client's Esc menu. The server can refuse for 10 s after fighting (rAthena prevent_logout:
    /// clif_parse_Restart / clif_parse_QuitGame answer 0x018b {1}); the refusal shows on the info line.
    /// </summary>
    public sealed class LeaveWindow
    {
        public enum Choice { CharSelect, LogOut, Quit }
        private readonly ListWindow _win; private readonly Func<Camera> _head;
        private string _status; private bool _busy;
        /// <summary>The player picked one; the game sends the request and later calls <see cref="SetStatus"/> or leaves.</summary>
        public Action<Choice> Chosen;

        public LeaveWindow(ListWindow win, Func<Camera> head) { _win = win; _head = head; _win.OnCloseClicked = Hide; }
        public bool Visible => _win.Visible;
        public string Status => _status;
        public int RowCount => _win.RowCount;

        public void Show() { _status = null; _busy = false; _win.SetTabs(null, 0, null); _win.SetInput(false); _win.Open(_head()); Refresh(); }
        public void Hide() => _win.Hide();
        /// <summary>A line on the info bar (waiting for the server, or why it refused); busy greys the choices out.</summary>
        public void SetStatus(string text, bool busy) { _status = text; _busy = busy; Refresh(); }
        public void Press(int row) => _win.Press(row);

        public void Refresh()
        {
            if (!_win.Visible) return;
            _win.SetTitle(Lang.T("Leave the game"));
            _win.SetInfo(_status ?? Lang.T("Your character is saved on the server whichever you choose."));
            var rows = new List<ListWindow.Row>
            {
                new ListWindow.Row { Text = Lang.T("Character select  <color=#9aa3b5>play another character on this account</color>"), A = Lang.T("Go"), AEnabled = !_busy, OnA = () => Chosen?.Invoke(Choice.CharSelect) },
                new ListWindow.Row { Text = Lang.T("Log out  <color=#9aa3b5>back to the login screen</color>"), A = Lang.T("Log out"), AEnabled = !_busy, OnA = () => Chosen?.Invoke(Choice.LogOut) },
                new ListWindow.Row { Text = Lang.T("Quit  <color=#9aa3b5>close Loki</color>"), A = Lang.T("Quit"), AEnabled = !_busy, OnA = () => Chosen?.Invoke(Choice.Quit) },
            };
            _win.SetRows(rows, true);
            _win.SetFooter((Lang.T("Cancel"), Hide));
        }
    }
}
