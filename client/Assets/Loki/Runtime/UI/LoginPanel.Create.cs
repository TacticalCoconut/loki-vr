using Loki.Net;
using Loki.Sprites;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 4 (w4account): character creation with a live preview. The screen sends what CH_MAKE_CHAR 0x0a39 carries for
    /// PACKETVER ≥ 20151001 (rA:char/char_clif.cpp:1250-1309: name, slot, hair colour, hair style, job and sex — the stats are 1),
    /// and the preview (<see cref="CharPreview"/>) draws that character beside the panel as the choices change. rAthena checks
    /// neither hair value (rA:char/char.cpp:1394-1525 char_make_new_char), so the ranges are the official client's: human hair
    /// styles 1-29 and colours 0-8, doram 1-6 and 0-7 (roBrowserLegacy UI/Components/CharCreate/CharCreateCommon.js:59-80, the
    /// race / gender creation window of 2015+ clients — a reimplementation, the only source); a style or colour the player's game
    /// data lacks is shown as missing in the preview. The job is Novice (0) or Summoner (4218) as the server allows
    /// (allowed_job_flag, char.cpp:1488-1491); the sex may differ from the account's (the packet's sex, char_clif.cpp:1273-1276).
    /// </summary>
    public sealed partial class LoginPanel
    {
        private VrTextField _name; private int _hair = 1, _hairColor = 0, _createSex = -1; private bool _doram;
        private Text _createInfo, _hairLabel, _colorLabel; private string _createInfoFor;
        private UiButton _raceHuman, _raceDoram, _sexMale, _sexFemale;
        /// <summary>The name of a creation sent and not answered yet (the list that brings it back returns to the character list).</summary>
        private string _createPending;
        private CharPreview _preview;
        public const int HumanHairMax = 29, HumanColorMax = 8, DoramHairMax = 6, DoramColorMax = 7;

        private void BuildCreate(RectTransform p)
        {
            float top = (H - 180) / 2;
            UiKit.Label(p, Lang.T("Name"), new Vector2(-W / 2 + 140, top - 60), new Vector2(200, 40), 26, TextAnchor.MiddleLeft, UiKit.Dim);
            _name = Field(p, new Vector2(60, top - 60), new Vector2(W - 380, 64), 28);
            _name.Placeholder = Lang.T("4–23 characters"); _name.OnSubmit = DoCreate;
            UiKit.Label(p, Lang.T("Race"), new Vector2(-W / 2 + 140, top - 150), new Vector2(200, 40), 26, TextAnchor.MiddleLeft, UiKit.Dim);
            _raceHuman = UiKit.Button(p, Lang.T("Human (Novice)"), new Vector2(-40, top - 150), new Vector2(300, 56), () => { _doram = false; ClampLook(); RefreshCreate(); }, 22);
            _raceDoram = UiKit.Button(p, Lang.T("Doram (Summoner)"), new Vector2(290, top - 150), new Vector2(300, 56), () => { _doram = true; ClampLook(); RefreshCreate(); }, 22);
            UiKit.Label(p, Lang.T("Sex"), new Vector2(-W / 2 + 140, top - 225), new Vector2(200, 40), 26, TextAnchor.MiddleLeft, UiKit.Dim);
            _sexMale = UiKit.Button(p, Lang.T("Male"), new Vector2(-40, top - 225), new Vector2(300, 56), () => { _createSex = 1; RefreshCreate(); }, 22);
            _sexFemale = UiKit.Button(p, Lang.T("Female"), new Vector2(290, top - 225), new Vector2(300, 56), () => { _createSex = 0; RefreshCreate(); }, 22);
            UiKit.Label(p, Lang.T("Hair style"), new Vector2(-W / 2 + 140, top - 300), new Vector2(200, 40), 26, TextAnchor.MiddleLeft, UiKit.Dim);
            UiKit.Button(p, "-", new Vector2(-110, top - 300), new Vector2(70, 56), () => StepHair(-1), 30);
            _hairLabel = UiKit.Label(p, "", new Vector2(0, top - 300), new Vector2(140, 56), 26, TextAnchor.MiddleCenter);
            UiKit.Button(p, "+", new Vector2(110, top - 300), new Vector2(70, 56), () => StepHair(+1), 30);
            UiKit.Label(p, Lang.T("Hair colour"), new Vector2(-W / 2 + 140, top - 375), new Vector2(200, 40), 26, TextAnchor.MiddleLeft, UiKit.Dim);
            UiKit.Button(p, "-", new Vector2(-110, top - 375), new Vector2(70, 56), () => StepColor(-1), 30);
            _colorLabel = UiKit.Label(p, "", new Vector2(0, top - 375), new Vector2(140, 56), 26, TextAnchor.MiddleCenter);
            UiKit.Button(p, "+", new Vector2(110, top - 375), new Vector2(70, 56), () => StepColor(+1), 30);
            UiKit.Label(p, Lang.T("Turn"), new Vector2(W / 2 - 280, top - 337), new Vector2(120, 40), 22, TextAnchor.MiddleCenter, UiKit.Dim);
            UiKit.Button(p, "◄", new Vector2(W / 2 - 360, top - 337), new Vector2(70, 56), () => { EnsurePreview(); _preview?.Turn(+1); }, 26);
            UiKit.Button(p, "►", new Vector2(W / 2 - 200, top - 337), new Vector2(70, 56), () => { EnsurePreview(); _preview?.Turn(-1); }, 26);
            _createInfo = UiKit.Label(p, "", new Vector2(0, top - 470), new Vector2(W - 160, 100), 21, TextAnchor.MiddleCenter);
            UiKit.Button(p, Lang.T("Create"), new Vector2(0, top - 570), new Vector2(W - 400, 70), DoCreate, 30);
            UiKit.Button(p, Lang.T("Back"), new Vector2(0, -top + 40), new Vector2(240, 56), () => Show(Screen.Chars), 24);
        }

        /// <summary>The "New character" button: a fresh form with the account's sex, the preview on.</summary>
        private void OpenCreate() { _name.SetText(""); _createSex = -1; Show(Screen.Create); }

        private int CreateSex => _createSex >= 0 ? _createSex : _s.Sex;
        private int HairMax => _doram ? DoramHairMax : HumanHairMax;
        private int ColorMax => _doram ? DoramColorMax : HumanColorMax;
        private void ClampLook() { _hair = Mathf.Clamp(_hair, 1, HairMax); _hairColor = Mathf.Clamp(_hairColor, 0, ColorMax); }
        /// <summary>- / + wrap around, as the official window's arrows do (rB CharCreateCommon.js:725-746).</summary>
        private void StepHair(int d) { _hair += d; if (_hair > HairMax) _hair = 1; if (_hair < 1) _hair = HairMax; RefreshCreate(); }
        private void StepColor(int d) { _hairColor += d; if (_hairColor > ColorMax) _hairColor = 0; if (_hairColor < 0) _hairColor = ColorMax; RefreshCreate(); }

        /// <summary>The look the server will store for these choices (CHARACTER_INFO head = hair style, headpalette = hair colour).</summary>
        public PlayerLook CreateLook => new PlayerLook { Job = _doram ? 4218 : 0, Sex = CreateSex, Hair = _hair, HairColor = _hairColor };

        private void EnsurePreview()
        {
            if (_preview != null) return;
            // beside the panel's right edge, feet near the bottom; ~0.45 m tall
            _preview = CharPreview.Create(_rt, new Vector2(W / 2 + 230, -H / 2 + 150), 0.45f, _rt.localScale.x);
        }

        private void HidePreview() { if (_preview != null && _preview.gameObject.activeSelf) _preview.gameObject.SetActive(false); }

        private void RefreshCreate()
        {
            if (_createInfo == null) return;
            _createInfoFor = _name.Text;
            ClampLook();
            var on = new Color(0.45f, 0.35f, 0.15f);
            _raceHuman.Normal = !_doram ? on : UiKit.ButtonBg; _raceDoram.Normal = _doram ? on : UiKit.ButtonBg; _raceHuman.SetHover(false); _raceDoram.SetHover(false);
            _sexMale.Normal = CreateSex == 1 ? on : UiKit.ButtonBg; _sexFemale.Normal = CreateSex == 0 ? on : UiKit.ButtonBg; _sexMale.SetHover(false); _sexFemale.SetHover(false);
            _hairLabel.text = string.Format("{0} / {1}", _hair, HairMax);
            _colorLabel.text = string.Format("{0} / {1}", _hairColor, ColorMax);
            bool previewOk = false;
            if (_screen == Screen.Create)
            {
                EnsurePreview();
                if (!_preview.gameObject.activeSelf) _preview.gameObject.SetActive(true);
                previewOk = _preview.Show(CreateLook);
            }
            string n = NormalizeName(_name.Text);
            // the server decides which letters a name may have; this only warns (stock rAthena refuses anything but A–Z a–z 0-9 space)
            bool plain = true; foreach (char c in n) if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == ' ')) { plain = false; break; }
            string missing = !previewOk ? "" : !_preview.HeadFound ? "  ·  " + Lang.T("<color=#ffd060>this hair style isn't in your game data</color>")
                           : !_preview.HairPaletteFound ? "  ·  " + Lang.T("<color=#ffd060>this hair colour isn't in your game data</color>") : "";
            _createInfo.text = Lang.F("Hair style {0}   ·   hair colour {1}   ·   {2}   ·   {3}", _hair, _hairColor, _doram ? "Doram" : Lang.T("Human"), CreateSex == 0 ? Lang.T("female") : Lang.T("male")) + missing + "\n" +
                               (plain ? Lang.T("<color=#9aa3b5>Stats start at 1 and are raised in game.</color>")
                                      : Lang.T("<color=#ffd060>Many servers accept only A–Z, a–z, digits and spaces in names: accents or symbols may be refused.</color>"));
            Debug.Log($"[Login] create preview: {(_doram ? "doram" : "human")} {(CreateSex == 0 ? "female" : "male")} hair {_hair} colour {_hairColor}: head {(previewOk ? (_preview.HeadFound ? "found" : "missing") : "not drawn")} ({_preview?.HeadPath}), palette {(previewOk ? (_preview.HairPaletteFound ? "found" : "missing") : "-")}");
        }

        private void TickCreate()
        {
            if (_name.Text != _createInfoFor) RefreshCreate();
        }

        private void DoCreate()
        {
            string n = NormalizeName(_name.Text);
            if (n.Length < 4) { Status(Lang.T("Names need at least 4 characters.")); return; }
            if (!RoText.CanEncode(n)) { Status(Lang.T("That name has letters the server's text can't carry (only Latin letters work).")); return; }
            int slot = 0; while (_s.Characters.Exists(c => c.Slot == slot)) slot++;
            if (slot >= Mathf.Max(_s.TotalSlots, 1)) { Status(Lang.T("No free character slot.")); return; }
            _createPending = n;
            Show(Screen.Connecting); Status(Lang.F("Creating {0}…", n));
            _s.CreateCharacter(n, slot, _hair, _hairColor, _doram ? 4218u : 0u, CreateSex);
        }

        // ---- smoke hooks
        public CharPreview Preview => _preview;
        public (int hair, int color, int sex, bool doram) CreateChoice => (_hair, _hairColor, CreateSex, _doram);
    }
}
