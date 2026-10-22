using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 4b (w4lang): the language, right on the login screen (a friend who gets English or Portuguese from Windows can
    /// switch before logging in; in game it is Settings → Language). The button names the OTHER language, in that language
    /// ("English" / "Português"); a press is the same choice as the Settings page's (Lang.Set: saved per Windows user, every fixed text
    /// relabelled at once). Hook: LoginPanel.Build (one line).
    /// </summary>
    public sealed partial class LoginPanel
    {
        private UiButton _w4langBtn;

        private void BuildW4Lang()
        {
            // top right, beside the title (the title's text is centred and ends well before it)
            _w4langBtn = UiKit.Button(_rt, "", new Vector2(W / 2 - 115, H / 2 - 50), new Vector2(190, 50), () => Lang.Set(Lang.Portuguese ? Lang.En : Lang.Pt), 22);
            _w4langBtn.name = "btn:Language";
            RefreshW4Lang();
            Lang.Changed += RefreshW4Lang;
        }

        private void RefreshW4Lang() { if (_w4langBtn != null) _w4langBtn.SetLabel(Lang.Portuguese ? "English" : "Português"); }

        private void OnDestroy() { Lang.Changed -= RefreshW4Lang; }

        public string LanguageButtonLabel => _w4langBtn != null && _w4langBtn.Text != null ? _w4langBtn.Text.text : "";
    }
}
