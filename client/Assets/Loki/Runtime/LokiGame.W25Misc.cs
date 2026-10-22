using System.Collections.Generic;
using Loki.Net;
using Loki.Sprites;
using Loki.World;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 25 (w25misc): the design's next list of 2026-10-14 (MAS doc loki-next-list-2026-10-14 = playtest notes),
    /// the items that are not the tutorial's:
    ///  1  THE LOGIN SKY (design note: "The login screen doesn't have uh, skybox nor any of the cloud effects. It's just the Valkyrie map on a black
    ///     void"). Cause: the sky is set for a map only when the world's map is entered (LokiGame.cs OnMapChange: Sky.Apply), and until
    ///     then the head camera clears to its solid colour (World/SkyController.cs Apply is what turns the camera to the skybox); the map's
    ///     own sky clouds (mapsky.tsv: valkyrie 153,204,255, old cloud type 2 = white) are drawn around our character only
    ///     (World/Weather.cs LateUpdate ticks nothing without a position), and before the world there is none. Now the login backdrop gets
    ///     its map's sky the moment the screens before the world start (<see cref="W25LoginSky"/>), and the weather's centre is the
    ///     backdrop's middle while there is no character (<see cref="W25LoginCloudCentre"/>, LokiGame.W4World.cs).
    ///  8  PLAY DEAD'S SOUND (design note: "a small sound effect, like very benign, to the play dead skill, and also to its preview on the skill
    ///     tree ... avoid it to be something like a person moaning"). Neither the effect tables nor the install name a sound for
    ///     NV_TRICKDEAD (143), so its use and its preview were silent. One added sound from the player's own game data, played when a
    ///     unit lies down with the skill (0x09cb with skill 143 that STARTED the status: rAthena StatusSkillImpl sends the same packet when
    ///     it ends it, skills/skill_impl.cpp:57-73), and on its preview (LokiGame.W22Tree.cs W24SoundsOf).
    ///  13 A SOUND WHEN A SKILL IS LEVELLED UP (design note: "Let a sound effect when I level up a skill."): on 0x010e (rAthena sends it only from
    ///     pc_skillup, pc.cpp:9169; Game/Progression.cs SkillLevelUp), one sound from the player's game data, at most one in
    ///     <see cref="W25SkillUpGap"/> (a plan of several levels sends several packets at once), none while a character preset raises its
    ///     skills (LokiGame.W14Kit.cs W14KBusy: those raises are not announced one by one either).
    /// The sounds are files of the player's own install (data\wav), never shipped; a file the install lacks is silence.
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>Item 13: the skill level-up sound, its share of the effects volume, the least time between two (FLAGGED: the design named no
        /// sound; effect\sign_up.wav: 0.70 s, a short rising sign in the game data, used nowhere else by Loki).</summary>
        public const string W25SkillUpWav = "effect\\sign_up.wav";
        public const float W25SkillUpVolume = 0.7f, W25SkillUpGap = 0.5f;
        /// <summary>Item 8: Play Dead (NV_TRICKDEAD), its sound and its share of the effects volume (FLAGGED: the design specified "very benign";
        /// effect\sign_down.wav: 0.70 s, the falling counterpart of the level-up sign, no voice).</summary>
        public const int W25PlayDead = 143;
        public const string W25PlayDeadWav = "effect\\sign_down.wav";
        public const float W25PlayDeadVolume = 0.6f;
        /// <summary>Item 8: sounds Loki adds to skills whose effect tables name none (the skill's use in the world and its preview).</summary>
        public static readonly Dictionary<int, string> W25AddedSkillSounds = new Dictionary<int, string> { { W25PlayDead, W25PlayDeadWav } };

        /// <summary>Sounds started (checks, log): level-ups, Play Dead in the world; the map whose sky the login backdrop got ("" none).</summary>
        public int W25SkillUpSounds { get; private set; }
        public int W25PlayDeadSounds { get; private set; }
        public string W25LoginSkyMap { get; private set; } = "";
        private float _w25SkillUpAt = -10f;

        private void BeginW25Misc(LokiBootstrap boot)
        {
            Prog.SkillLevelUp += W25OnSkillUp;
            if (Combat != null) Combat.SkillNoDamage += W25OnNoDamage;
            W25LoginSky();
        }

        // ------------------------------------------------------------------ item 1: the login backdrop's sky
        /// <summary>The screens before the world: the backdrop map's sky (its own colours, stars and clouds: World/SkyController.cs), as
        /// the world's map gets it when it is entered. Entering the world replaces it with that map's (LokiGame.cs OnMapChange).</summary>
        private void W25LoginSky()
        {
            var m = _boot != null && _boot.Map != null ? _boot.Map.Current : null;
            if (m == null || Sky == null || (Session != null && Session.State == RoSession.Stage.InGame)) return;
            Sky.Apply(m.Name, Room != null && Room.IsIndoor(m.Name), m.Rsw, m.LightDirection);
            W25LoginSkyMap = m.Name;
            Debug.Log($"[Sky] login backdrop '{m.Name}': its sky set before the world ({Sky.Theme}{Sky.OwnSky}); the weather's centre: {W25LoginCloudCentre}");
        }

        /// <summary>The weather's centre while there is no character of ours: the login backdrop's middle (the cell at the table's centre,
        /// LokiBootstrap.LoginBackdrop; the start map's middle when the backdrop is the start map), in the map's own cells; null in the
        /// world (our character is the centre there).</summary>
        public Vector3? W25LoginCloudCentre
        {
            get
            {
                var m = _boot != null && _boot.Map != null ? _boot.Map.Current : null;
                if (m == null || m.Gat == null || (Session != null && Session.State == RoSession.Stage.InGame) || string.IsNullOrEmpty(_boot.BackdropMap) || m.Name != _boot.BackdropMap) return null;
                var bd = LokiBootstrap.LoginBackdrop;
                int x = m.Name == bd.Map ? bd.X : m.Gat.Width / 2, y = m.Name == bd.Map ? bd.Y : m.Gat.Height / 2;
                if (!m.Gat.InBounds(x, y)) return null;
                return new Vector3(x + 0.5f, -m.Gat.Get(x, y).AverageHeight * RoSpace.Scale, y + 0.5f);
            }
        }

        // ------------------------------------------------------------------ item 13: a skill levelled up
        private void W25OnSkillUp(int skill, int level)
        {
            if (W14KBusy) return; // a character preset raising its skills (LokiGame.W14Kit.cs)
            if (Time.unscaledTime - _w25SkillUpAt < W25SkillUpGap) return;
            _w25SkillUpAt = Time.unscaledTime;
            if (Audio == null || !Audio.HasWav(W25SkillUpWav)) { Debug.Log($"[Skill] level up of {skill} (level {level}): no {W25SkillUpWav} in this install: silent"); return; }
            Audio.PlayWav(W25SkillUpWav, null, W25SkillUpVolume); W25SkillUpSounds++;
        }

        // ------------------------------------------------------------------ item 8: Play Dead
        private void W25OnNoDamage(int skill, int level, uint target, uint src, bool ok)
        {
            if (skill != W25PlayDead || !ok || Entities == null) return;
            uint who = src != 0 ? src : target;
            var u = Entities.Get(who); if (u == null) return;
            // the same packet ends the status: only a unit that now LIES (the status came first: rAthena evaluates sc_start / status_change_end
            // before clif_skill_nodamage sends this) gets the sound
            bool lies = u.IsSelf ? Prog.Effects.ContainsKey(UnitEfstLooks.EfstTrickDead) : UnitEfstLooks.Of(u).LastLook == "play dead";
            if (!lies || Audio == null || !Audio.HasWav(W25PlayDeadWav)) return;
            Audio.PlayWav(W25PlayDeadWav, u.Cell, W25PlayDeadVolume); W25PlayDeadSounds++;
        }

        /// <summary>Item 8: a skill's preview with no sound of its own in the tables gets the one Loki adds (LokiGame.W22Tree.cs W24SoundsOf).</summary>
        private void W25AddedSkillSound(int skill, List<(float at, string wav)> sounds)
        {
            if (sounds == null || sounds.Count > 0 || Audio == null || !W25AddedSkillSounds.TryGetValue(skill, out var wav) || !Audio.HasWav(wav)) return;
            sounds.Add((UI.SkillDiorama.TCast, wav));
        }
    }
}
