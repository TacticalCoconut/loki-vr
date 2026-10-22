namespace Loki.Net
{
    /// <summary>
    /// Counters of a presence / voice / data-check channel, for the log and the smokes (port wave H, hpresence): the same
    /// numbers whichever transport carries the channel, so a smoke never asks which server it runs on. Implemented by
    /// <see cref="PresenceRelayChannel"/> (Loki Host's relay) and <see cref="Stdb.StdbPresenceChannel"/> (the Loki Online module).
    /// </summary>
    public interface IPresenceStats
    {
        /// <summary>My poses handed to the transport; other players' poses, GONEs received; links made so far.</summary>
        int StatPosesSent { get; }
        int StatPosesReceived { get; }
        int StatGonesReceived { get; }
        int StatConnects { get; }
        /// <summary>The protocol version of the current link (PresenceWire: 1 poses, 2 poses + voice + data); 0 while not linked.</summary>
        int StatLinkVersion { get; }
        /// <summary>Links that fell back to version 1 (a relay from before voice); a transport without versions never does.</summary>
        int StatFallbacks { get; }
        /// <summary>My voice frames handed to the transport, dropped before it (the link stalled); others' frames, VOICE_ENDs received.</summary>
        int StatVoiceSent { get; }
        int StatVoiceSendDropped { get; }
        int StatVoiceFramesReceived { get; }
        int StatVoiceEndsReceived { get; }
        /// <summary>The far end compares game data (it said so on this link); my DATA messages sent, its answers received.</summary>
        bool StatChecksData { get; }
        int StatDataSent { get; }
        int StatDataAnswers { get; }
    }
}
