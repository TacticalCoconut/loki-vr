using Loki.Sprites;
using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 4 (w4account): the character-creation preview — the new character drawn with the game's own sprite code
    /// (<see cref="PlayerAppearance.Apply"/> on an <see cref="ActorSprite"/>, the same path as every player unit) from the player's
    /// game data: body of the chosen race and sex, the hair style's head sprite and the hair colour's palette. It stands beside
    /// the login panel (never over it: the panels draw with ZTest Always on a later queue, LokiUIOverlay.shader, and would hide a
    /// sprite behind them), faces the player and can be turned. Nothing here talks to a server.
    /// </summary>
    public sealed class CharPreview : MonoBehaviour
    {
        private ActorSprite _sprite; private PlayerLook _shown;
        /// <summary>The direction shown, in the client's convention (0 = facing the camera; 1..7 turn clockwise seen from above).</summary>
        public int Facing;
        public ActorSprite Sprite => _sprite;
        /// <summary>The head sprite path of the look shown, and whether the game data has it (the head is missing otherwise).</summary>
        public string HeadPath { get; private set; } = "";
        public bool HeadFound { get; private set; }
        public string HairPalettePath { get; private set; } = "";
        public bool HairPaletteFound { get; private set; }

        /// <param name="panel">The login panel's canvas (pixel units, <paramref name="pxToMetres"/> metres a pixel).</param>
        public static CharPreview Create(RectTransform panel, Vector2 localPx, float metresTall, float pxToMetres)
        {
            var go = new GameObject("CharPreview");
            go.transform.SetParent(panel, false);
            go.transform.localPosition = new Vector3(localPx.x, localPx.y, 0f);
            // a standing Novice is about 3 cells tall (≈ 105 px at ActorSprite.PixelsPerCell 35): metresTall for those 3 cells
            float cellMetres = metresTall / 3f;
            go.transform.localScale = Vector3.one * (cellMetres / pxToMetres);
            var p = go.AddComponent<CharPreview>();
            var sgo = new GameObject("sprite");
            sgo.transform.SetParent(go.transform, false);
            p._sprite = sgo.AddComponent<ActorSprite>();
            p._sprite.IsPlayerType = true;
            return p;
        }

        /// <summary>Draws a look (only when it changed). False when the game data isn't open.</summary>
        public bool Show(PlayerLook look)
        {
            var boot = LokiBootstrap.Instance;
            if (boot == null || boot.Assets == null || look == null) return false;
            if (_shown != null && _shown.SameAs(look)) return true;
            _shown = look.Clone();
            PlayerAppearance.Apply(_sprite, look, boot.Assets);
            HeadPath = RoNames.HeadPath(look.Hair, look.Job, look.Sex);
            HeadFound = boot.Assets.SpriteExists(HeadPath);
            // the palette file PlayerAppearance looks for (hair colour > 0; rb EntityView.js:644-658 uses none for 0)
            int idx = RoNames.HairIndex(look.Hair, look.Job, look.Sex); string sx = RoNames.Sex(look.Sex);
            const string HairPal = "¸Ó¸®"; // 머리 as Latin-1 bytes (CP949), as PlayerAppearance
            HairPalettePath = look.HairColor <= 0 ? "" : RoNames.IsDoram(look.Job)
                ? $"data\\palette\\{RoNames.Doram}\\{HairPal}\\{HairPal}{idx}_{sx}_{look.HairColor}.pal"
                : $"data\\palette\\{HairPal}\\{HairPal}{idx}_{sx}_{look.HairColor}.pal";
            HairPaletteFound = look.HairColor <= 0 || boot.Assets.Exists(HairPalettePath);
            _sprite.Play(ActorSprite.PlayerAction.Idle, true);
            return true;
        }

        public void Turn(int step) { Facing = (Facing + step) & 7; }

        private void Update()
        {
            // ActorSprite draws (Direction + CameraDirection) & 7 (ActorSprite.LateUpdate): keep the chosen facing whatever the map's view
            if (_sprite != null) _sprite.Direction = (Facing - ActorSprite.CameraDirection) & 7;
        }

        /// <summary>Whether a frame is on screen (the quad exists and holds a texture): smokes read it.</summary>
        public bool Drawn => _sprite != null && _sprite.CurrentTexture != null;
    }
}
