using System;
using System.Collections.Generic;
using Loki.World;
using UnityEngine;

namespace Loki.Voice
{
    /// <summary>
    /// The voice choices (client wave 6, w6voice), kept with the other settings per Windows user in loki-user.json
    /// (World/UserSettings.cs <c>settings.voice*</c>; the Settings window's "Voice" page). The values live in UserSettings.Prefs
    /// itself; <see cref="Save"/> writes the file through VR.Comfort.Save, as every other page does, which also redraws the
    /// open Settings window. Nothing here is rAthena-specific.
    /// </summary>
    public static class VoiceSettings
    {
        private static UserSettings.Settings P => UserSettings.Prefs;

        /// <summary>Send my voice (the microphone is open only while this is on, a voice link is up and I am in the world).</summary>
        public static bool Share { get => P.voiceShare; set => P.voiceShare = value; }
        /// <summary>Play friends' voices.</summary>
        public static bool Hear { get => P.voiceHear; set => P.voiceHear = value; }
        /// <summary>Also play friends who are on another map (off: only the ones on my map).</summary>
        public static bool HearOtherMaps { get => P.voiceHearOtherMaps; set => P.voiceHearOtherMaps = value; }
        /// <summary>The microphone as Unity lists it; "" = Windows' default recording device.</summary>
        public static string Mic { get => P.voiceMic ?? ""; set => P.voiceMic = value ?? ""; }
        public static float MicGain { get => Mathf.Clamp(P.voiceMicGain > 0 ? P.voiceMicGain : 1f, 0.25f, 4f); set => P.voiceMicGain = value; }
        /// <summary>0 off (everything is sent), 1 low, 2 medium, 3 high (VoiceGate.OpenDbFor).</summary>
        public static int Gate { get => Mathf.Clamp(P.voiceGate, 0, VoiceGate.Steps - 1); set => P.voiceGate = Mathf.Clamp(value, 0, VoiceGate.Steps - 1); }
        public static float Volume { get => Mathf.Clamp(P.voiceVolume, 0f, 2f); set => P.voiceVolume = Mathf.Clamp(value, 0f, 2f); }
        public static bool NoticeDone { get => P.voiceNoticeDone; set => P.voiceNoticeDone = value; }

        public static readonly float[] GainSteps = { 0.5f, 0.75f, 1f, 1.5f, 2f, 3f, 4f };
        public static readonly float[] VolumeSteps = { 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f };
        /// <summary>The steps of one speaker's own volume (the player menu's and the Voice page's "Volume" button goes round them).</summary>
        public static readonly float[] PeerVolumeSteps = { 0.5f, 0.75f, 1f, 1.5f, 2f };

        /// <summary>The saved choices for one speaker, or null when none were made and <paramref name="create"/> is false.</summary>
        public static UserSettings.VoicePeer Peer(uint aid, bool create = false, string name = null)
        {
            foreach (var p in P.voicePeers) if (p != null && p.aid == aid) { if (!string.IsNullOrEmpty(name)) p.name = name; return p; }
            if (!create) return null;
            var list = new List<UserSettings.VoicePeer>(P.voicePeers);
            var n = new UserSettings.VoicePeer { aid = aid, name = name ?? "" };
            list.Add(n); P.voicePeers = list.ToArray();
            return n;
        }
        public static float PeerVolume(uint aid) { var p = Peer(aid); return p != null && p.volume > 0 ? Mathf.Clamp(p.volume, 0.1f, 2f) : 1f; }
        public static bool PeerMuted(uint aid) { var p = Peer(aid); return p != null && p.muted; }

        /// <summary>Writes loki-user.json (every setting as it is shown) and redraws the open Settings window.</summary>
        public static void Save()
        {
            VR.Comfort.Load(); // Save writes every Comfort value back, so they must be the player's (the rig loaded them already)
            VR.Comfort.Save();
        }

        public static string Summary() =>
            $"share {Share}, hear {Hear}, other maps {HearOtherMaps}, microphone '{(Mic.Length > 0 ? Mic : "(default)")}', gain {MicGain:0.##}, gate step {Gate}, volume {Volume:0.##}, notice answered {NoticeDone}";
    }
}
