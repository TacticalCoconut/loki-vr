using Loki.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.Horde.Client
{
    /// <summary>
    /// Client wave 21 (w21horde): THE PANEL THEME in Horde (design note, 2026-10-10: "Everything should take the new look"; the picture:
    /// the UI theme palette; the kit: UI/Theme.cs, UI/UiKit.cs). The one place for the few
    /// things Horde's pieces share on top of the kit:
    ///   IN A RUN the HUD sits over the action, so it is the theme's SEE-THROUGH look (UiKit.Dress ... PanelLook.SeeThrough: the gold line
    ///     and the corner scrolls, nothing behind) and its lettering stands on PLANKS: slim strips of the frame's darkest wood
    ///     (<see cref="Plank"/>) only as large as the letters need. Waves 8-20 drew two dark blocks of 560 x 400 px behind everything; a
    ///     plank is at least as dark (luminance 0.0045 against the blocks' 0.0050) and at least as opaque (0.80 against 0.78), so no
    ///     letter reads worse on it than on the block, and the rest of the block is open: the enemies behind it are seen.
    ///   BARS (mine, the boss's, an elite's, a runner's tag) keep their grooves and their colours (green / amber / red are meanings) and
    ///     get the theme's thin gold edge (<see cref="GoldRim"/>).
    ///   CARDS (the choice) are the kit's parchment cards; a card's KIND is told by the colour of the band its tag stands on
    ///     (<see cref="KindColor"/>: weapon terracotta, passive blue, stat green, job gold, heal rose; all pale, the tag in dark ink).
    /// Every colour here is one of the kit's or derived from them; the numbers are in design notes
    /// </summary>
    public static class HordeLook
    {
        /// <summary>A plank behind the HUD's lettering: the frame's darkest wood (the kit's see-through edge: WoodDark x 0.6), 80 % opaque.</summary>
        public static readonly Color Plank = new Color(Theme.WoodDark.r * 0.6f, Theme.WoodDark.g * 0.6f, Theme.WoodDark.b * 0.6f, 0.80f);
        /// <summary>A held slot's fill (under an icon) and an empty slot's (the rim alone says the slot is there).</summary>
        public static readonly Color SlotHeld = new Color(Theme.WoodDark.r * 0.6f, Theme.WoodDark.g * 0.6f, Theme.WoodDark.b * 0.6f, 0.80f), SlotEmpty = new Color(0f, 0f, 0f, 0f);
        /// <summary>The thin gold edge of a see-through piece (a bar, a slot); an empty slot's is faint.</summary>
        public static readonly Color Edge = new Color(Theme.Gold.r, Theme.Gold.g, Theme.Gold.b, 0.95f), EdgeFaint = new Color(Theme.Gold.r, Theme.Gold.g, Theme.Gold.b, 0.45f);
        /// <summary>The numbers of a card ("Damage 28 → 39") on parchment: the kit's Accent darkened towards its darkest wood until it reads
        /// 10.8 : 1 (the dark cards' gold numbers read 9.9 : 1; Accent itself 7.7 : 1).</summary>
        public static readonly Color NumberInk = Color.Lerp(Theme.Accent, Theme.WoodDark, 0.45f);
        /// <summary>A card's name and sentences, the HUD's dark lettering on parchment: the kit's darkest wood (14.9 : 1 on a card; the
        /// kit's Ink 13.9; the dark cards' white read 14.1).</summary>
        public static readonly Color DeepInk = Theme.WoodDark;

        /// <summary>The colour of the band a card's tag stands on, by its kind: told apart at a glance, never red (the enemies' warning).
        /// Brand-new abilities use bright gold-amber / sky-cyan bands, while level-ups of owned skills use calmer terracotta / slate bands.</summary>
        public static Color KindColor(HordeCardKind k)
        {
            switch (k)
            {
                case HordeCardKind.NewWeapon: return Rgb(0xFFE082); // bright warm gold-amber for a brand-new ability
                case HordeCardKind.WeaponBranch: return Rgb(0xFFD180); // radiant amber for an evolved branch
                case HordeCardKind.WeaponTranscend: return Rgb(0xFFB74D); // celestial orange-gold for weapon transcendence
                case HordeCardKind.WeaponLevel: return Rgb(0xE8C8B0); // calm terracotta for leveling up an owned weapon
                case HordeCardKind.NewPassive: return Rgb(0x90CAF9); // bright sky-blue for a brand-new passive ability
                case HordeCardKind.PassiveTranscend: return Rgb(0xCE93D8); // celestial amethyst-gold for passive transcendence
                case HordeCardKind.PassiveLevel: return Rgb(0xC5D5E8); // calm slate-blue for leveling up an owned passive
                case HordeCardKind.Stat: return Rgb(0xC6E0B8); // green
                case HordeCardKind.Job: return Theme.GoldLight; // gold
                case HordeCardKind.Heal: return Rgb(0xF2C6CF); // rose
                default: return Theme.Cell;
            }
        }
        /// <summary>A NEW weapon / passive / job / branch / transcendence (not a plain level of one I hold): its band carries the gold seal.</summary>
        public static bool IsNew(HordeCardKind k) => k == HordeCardKind.NewWeapon || k == HordeCardKind.NewPassive || k == HordeCardKind.Job || k == HordeCardKind.WeaponBranch || k == HordeCardKind.WeaponTranscend || k == HordeCardKind.PassiveTranscend;

        private static Color Rgb(int v) => new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f);

        /// <summary>The theme's thin edge around <paramref name="around"/> in gold (the kit's soft square's rim, UI/Theme.cs SoftRim),
        /// <paramref name="grow"/> px outside it. Drawn as it is on any surface.</summary>
        public static Image GoldRim(RectTransform around, float grow = 2f, bool faint = false)
        {
            var rt = UiKit.Box(around, "th:rim", Vector2.zero, Vector2.zero, faint ? EdgeFaint : Edge);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(-grow, -grow); rt.offsetMax = new Vector2(grow, grow);
            var img = rt.GetComponent<Image>(); img.sprite = Theme.SoftRim; img.type = Image.Type.Sliced;
            if (img is ThemedImage ti) ti.Raw = true;
            return img;
        }

        /// <summary>A plank: a soft strip of the darkest wood behind lettering of the see-through HUD.</summary>
        public static RectTransform PlankBox(RectTransform parent, string name, Vector2 pos, Vector2 size)
        {
            var rt = UiKit.Soft(parent, name, pos, size, Plank);
            if (rt.GetComponent<Image>() is ThemedImage ti) ti.Raw = true;
            return rt;
        }

        /// <summary>The see-through frame of the theme (the gold line and the corner scrolls) around a rectangle of a HUD piece: an empty
        /// box dressed by the kit. <paramref name="metresPerPx"/>: the piece's scale (the corner scroll is 1.6 cm).</summary>
        public static RectTransform SeeFrame(RectTransform parent, string name, Vector2 pos, Vector2 size, float metresPerPx)
        {
            var rt = UiKit.Box(parent, name, pos, size, new Color(1, 1, 1, 0));
            rt.GetComponent<Image>().enabled = false;
            UiKit.Dress(rt, size, metresPerPx, PanelLook.SeeThrough);
            return rt;
        }

        /// <summary>A plain coloured box whose colour is data (a bar's fill, a mark): drawn as it is on any surface.</summary>
        public static RectTransform Raw(RectTransform box) { if (box != null && box.GetComponent<Image>() is ThemedImage ti) ti.Raw = true; return box; }
    }
}
