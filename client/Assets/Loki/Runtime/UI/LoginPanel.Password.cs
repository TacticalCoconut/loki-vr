using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Wave H (haccount): the account's password, changed from the character screen where the server can do it
    /// (<see cref="Loki.Net.IAccountSession.CanChangePassword"/>: Loki Online's change_password reducer; rAthena has no packet
    /// for it, so the button is not shown there). The current password proves the change; both travel hashed, like a login's.
    /// Afterwards every other PC's saved login to the account ends and this one stays.
    /// </summary>
    public sealed partial class LoginPanel
    {
        private VrTextField _pwOld, _pwNew, _pwAgain; private Text _pwNote; private UiButton _pwOpen, _pwDo;

        private void BuildPasswordButton(RectTransform p, float top)
        {
            _pwOpen = UiKit.Button(p, Lang.T("Password…"), new Vector2(30, -top + 40), new Vector2(290, 60), () => Show(Screen.Password), 24);
        }

        private void RefreshPasswordButton() { if (_pwOpen != null) _pwOpen.gameObject.SetActive(_s != null && _s.CanChangePassword); }

        private void BuildPassword(RectTransform p)
        {
            float top = (H - 180) / 2;
            UiKit.Label(p, Lang.T("Current password"), new Vector2(-W / 2 + 190, top - 80), new Vector2(300, 40), 24, TextAnchor.MiddleLeft, UiKit.Dim);
            _pwOld = Field(p, new Vector2(120, top - 80), new Vector2(W - 500, 60), 26);
            UiKit.Label(p, Lang.T("New password"), new Vector2(-W / 2 + 190, top - 170), new Vector2(300, 40), 24, TextAnchor.MiddleLeft, UiKit.Dim);
            _pwNew = Field(p, new Vector2(120, top - 170), new Vector2(W - 500, 60), 26);
            UiKit.Label(p, Lang.T("New password again"), new Vector2(-W / 2 + 190, top - 260), new Vector2(300, 40), 24, TextAnchor.MiddleLeft, UiKit.Dim);
            _pwAgain = Field(p, new Vector2(120, top - 260), new Vector2(W - 500, 60), 26);
            foreach (var f in new[] { _pwOld, _pwNew, _pwAgain }) { f.Masked = true; f.MaxLength = 32; }
            _pwOld.OnSubmit = () => _pwNew.Focus(); _pwNew.OnSubmit = () => _pwAgain.Focus(); _pwAgain.OnSubmit = DoChangePassword;
            _pwNote = UiKit.Label(p, "", new Vector2(0, top - 350), new Vector2(W - 160, 80), 21, TextAnchor.MiddleCenter, UiKit.Dim);
            _pwDo = UiKit.Button(p, Lang.T("Change the password"), new Vector2(0, top - 450), new Vector2(W - 500, 70), DoChangePassword, 26);
            UiKit.Button(p, Lang.T("Back"), new Vector2(0, -top + 40), new Vector2(240, 56), () => Show(Screen.Chars), 24);
        }

        private void RefreshPassword()
        {
            foreach (var f in new[] { _pwOld, _pwNew, _pwAgain }) f.SetText("");
            _pwNote.text = Lang.T("Other PCs that are logged in to this account will have to log in again with the new password.");
            _pwDo.Interactable = true;
        }

        private void DoChangePassword()
        {
            if (_s == null || !_s.CanChangePassword) return;
            string old = _pwOld.Text, nw = _pwNew.Text;
            if (old.Length == 0) { PasswordNote(Lang.T("Type the current password.")); return; }
            if (nw.Length < _s.PasswordMinLength) { PasswordNote(Lang.F("The password needs at least {0} characters.", _s.PasswordMinLength)); return; }
            if (nw != _pwAgain.Text) { PasswordNote(Lang.T("The two new passwords are not the same.")); return; }
            if (nw == old) { PasswordNote(Lang.T("The new password is the same as the current one.")); return; }
            _pwDo.Interactable = false; PasswordNote(Lang.T("Changing the password…"));
            _s.ChangePassword(old, nw);
        }

        private void PasswordNote(string text) { _pwNote.text = text; Status(text); }

        private void OnPasswordChanged(bool ok, string text)
        {
            if (_pwDo != null) _pwDo.Interactable = true;
            Status(text);
            if (!ok) { if (_pwNote != null) _pwNote.text = text; return; }
            // the login the panel would reconnect with is the new password now (a server that keeps this PC's login is
            // reconnected to through that: no password is kept for it, LoginPanel.cs NoteLogin)
            if (_lastLogin.user != null && !_lastLogin.resume) _lastLogin = (_lastLogin.server, _lastLogin.user, _pwNew.Text, false);
            foreach (var f in new[] { _pwOld, _pwNew, _pwAgain }) f.SetText("");
            if (_screen == Screen.Password) Show(Screen.Chars);
        }

        // ---- smoke hooks
        public bool PasswordButtonShown => _pwOpen != null && _pwOpen.gameObject.activeInHierarchy;
        public string PasswordNoteText => _pwNote != null ? _pwNote.text : "";
    }
}
