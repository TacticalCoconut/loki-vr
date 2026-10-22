namespace Loki
{
    /// <summary>
    /// Client wave 23 (w23audio), the design's answer 36 (2026-10-11: "on the tutorial screen, the music is too loud").
    ///
    /// While the tutorial runs the map's music plays at Audio/LokiAudio.cs TutorialMusicGain (0.35) x the player's own music volume,
    /// gliding there and back in MusicFadeSeconds (1 s). The Setting (Settings → Audio "Music": VR/Comfort.cs MusicVolume, saved as
    /// "music") is not touched: it still scales the music during the tutorial, and the music is back at it when the tutorial ends.
    /// The music player asks <see cref="W23MusicQuiet"/> once a frame (LokiAudio.Update; the hook: LokiGame.cs, beside Audio.MapRoot).
    ///
    /// "The tutorial runs" = LokiGame.W18Tutorial.cs W18TutRunning (from W18TutBegin to W18TutFinish: the last page's button or
    /// "End tutorial"), and already the moments before it opens: a new character's first login (_w21StartSoon, set by
    /// W18TutOnEnteredMap) and the wait for the tutorial's own map (_w21Placing: LokiGame.W21Map.cs W21TutPlace), so that the
    /// tutorial map's track STARTS quiet instead of starting loud and sinking when the first page opens.
    ///
    /// The other half of this agent's work, the start ship's ambience (the design's answer 42), is one table in Audio/LokiAudio.cs
    /// (AmbientMutes) and needs nothing here. The smoke of both: LokiGame.W23AudioSmoke.cs (-lokiSmokeOnly w23audio).
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>The music is to play quietly: the tutorial runs, or is about to open (a first login, the wait for its own map).</summary>
        public bool W23MusicQuiet => W18TutRunning || _w21StartSoon || _w21Placing;
    }
}
