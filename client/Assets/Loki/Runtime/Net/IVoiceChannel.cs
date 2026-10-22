using System;

namespace Loki.Net
{
    /// <summary>One encoded voice frame of another player, as the channel received it: views into the channel's own buffer, valid
    /// only during the call (copy or decode at once). Nothing is allocated per frame.</summary>
    public struct VoiceFrame
    {
        /// <summary>The speaker's RO account id (stamped by the channel's far end, never claimed inside the frame).</summary>
        public uint Aid;
        public byte[] Buffer;
        /// <summary>The speaker's character name (UTF-8) and map (ASCII) inside <see cref="Buffer"/>.</summary>
        public int NameOffset, NameLength, MapOffset, MapLength;
        public ushort Seq;
        /// <summary>The codec (Voice/ImaAdpcm.cs: 1) and the flags (1 = first frame of a talk spurt, 2 = last).</summary>
        public byte Codec, Flags;
        public int DataOffset, DataLength;
    }

    public delegate void VoiceFrameHandler(ref VoiceFrame frame);

    /// <summary>Why voice can or cannot cross the channel now (the Voice page and the microphone icon say it).</summary>
    public enum VoiceLinkState
    {
        /// <summary>No link (yet): connecting, or the server has no relay at all.</summary>
        NoLink,
        /// <summary>Linked to a relay from before voice existed (an older Loki Host): headsets work, voice does not.</summary>
        OldRelay,
        /// <summary>Linked, and the host switched voice off (Loki Host → Setup).</summary>
        SwitchedOffByHost,
        Ready,
    }

    /// <summary>
    /// The voice channel (client wave 6, w6voice): friends' voices, outside the RO protocol, beside <see cref="IPresenceChannel"/>
    /// and <see cref="IAccountSession"/>. The game's voice code (Voice/VoiceChat.cs) talks to this interface only. Today
    /// <see cref="PresenceRelayChannel"/> implements it on the pose relay's own TCP connection (Loki Host, tools/lokihost/Core/
    /// PoseRelay.cs); a SpacetimeDB transport can replace it later without a change in the voice code: it has to deliver each
    /// speaker's frames in order with the speaker's account id, name and map, and say when a speaker stops.
    /// Players are identified by their RO account id, as for the headsets (map/pc.cpp:1444: a player's unit id is its account id).
    /// </summary>
    public interface IVoiceChannel
    {
        VoiceLinkState VoiceState { get; }
        /// <summary>
        /// How much later than Loki Host's relay this transport may hand a frame over, in 20 ms frames: each speaker's jitter buffer
        /// starts and stays that much deeper (Voice/VoiceStream.cs). 0 for the relay (it forwards at once, on its own thread).
        /// The Loki Online module: 3 (60 ms): there a frame waits behind the world's own work on the module's one core and is handed
        /// over on this client's main thread (port wave H, hpresence: measured, server/ARCHITECTURE.md).
        /// </summary>
        int ExtraJitterFrames { get; }
        /// <summary>
        /// One encoded frame of my voice (main thread; 50 a second while my gate is open). The channel copies it and sends it from
        /// its own thread; when the link is slow the frame is dropped (returns false), never queued for long: late speech is
        /// worth nothing. Ignored unless <see cref="VoiceState"/> is Ready.
        /// </summary>
        bool SendVoice(string map, ushort seq, byte codec, byte flags, byte[] data, int offset, int length);
        /// <summary>I stopped sharing my voice (switched off, left the world): the listeners free my stream.</summary>
        void StopVoice();
        /// <summary>
        /// Called for every voice frame of another player ON THE CHANNEL'S OWN THREAD, in order per speaker: it must not block and
        /// must not touch Unity objects (the voice code decodes into that speaker's jitter buffer, which the audio thread reads;
        /// a busy main thread then cannot make voices stutter).
        /// </summary>
        VoiceFrameHandler VoiceReceived { get; set; }
        /// <summary>Main thread (from the channel's Poll): that speaker stopped sharing or left the channel.</summary>
        event Action<uint> VoiceEnded;
        /// <summary>Main thread (from the channel's Poll): the link dropped; every speaker's stream is over.</summary>
        event Action VoiceLost;
    }
}
