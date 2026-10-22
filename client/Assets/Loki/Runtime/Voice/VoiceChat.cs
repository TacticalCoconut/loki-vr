using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Loki.Net;
using Loki.UI;
using Loki.VR;
using Loki.World;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.Voice
{
    /// <summary>
    /// Voice between friends (client wave 6, w6voice; design note, 2026-10-04: an always-open microphone with a noise gate, over the
    /// headset relay). Outside the RO protocol: this class talks to <see cref="IVoiceChannel"/> only (today the pose relay's TCP
    /// connection, later a SpacetimeDB transport), never to RO packets.
    ///
    /// My voice: while "Share my voice" is on, the channel can carry voice, I am in the world and the first-time notice was
    /// answered, the microphone is open (<see cref="MicrophoneInput"/>; closed again the moment any of these stops being true).
    /// Each 20 ms frame goes through the gain, the high-pass and the noise gate (<see cref="VoiceGate"/>); only while the gate is
    /// open is it encoded (<see cref="ImaAdpcmCodec"/>) and handed to the channel with my map's name. The microphone icon over
    /// the walking wrist is shown while the microphone is open: grey while nothing leaves, green while my voice goes out.
    ///
    /// Friends' voices: every frame the channel receives is decoded on the channel's thread into that speaker's
    /// <see cref="VoiceStream"/> and played by a <see cref="VoiceSpeaker"/>. Who is heard: everyone on the channel, on any map
    /// ("Hear friends on other maps" off: my map only), unless muted here or "Hear friends' voices" is off. From where: on my map
    /// the speaker's floating headset when it is shown, else its RO character when the server shows it — spatialised; otherwise
    /// (another map, or out of sight) plain and a little quieter (<see cref="AwayGain"/>). While someone talks, their headset's
    /// lights and name, or their character's name, glow.
    /// Echo: there is no echo cancellation (the gate only): with open speakers a friend's voice can come back through my
    /// microphone. design notes says how to measure whether it is needed.
    /// </summary>
    [DefaultExecutionOrder(610)] // after HeadsetAvatars (600): the headsets are where this frame draws them
    public sealed class VoiceChat : MonoBehaviour
    {
        /// <summary>The level of a voice that is not placed in the room (another map, or out of sight): a little quieter.</summary>
        public const float AwayGain = 0.7f;
        /// <summary>A speaker silent for this long gives its AudioSource back (it is made again with its next word).</summary>
        public const float FreeAfterIdleSeconds = 120f;
        /// <summary>The RMS level above which a playing voice counts as "talking" (the glow): about −50 dB.</summary>
        public const float TalkingLevel = 0.003f;
        /// <summary>The height of a character's head above its feet, in cells (a standing sprite is about 2.3 cells tall: HeadsetAvatars' beam).</summary>
        public const float CharacterHeadCells = 1.8f;

        public static VoiceChat Instance { get; private set; }

        public IVoiceChannel Channel { get; private set; }
        public string CurrentMap { get; private set; }
        /// <summary>The character is in the world (not disconnected, not leaving).</summary>
        public Func<bool> Playing;
        /// <summary>Client wave 8 (w8game): every voice plain (not placed at a character or headset): the Horde arena hides the world.</summary>
        public static bool PlainOnly;
        /// <summary>The hook for push-to-talk (not built: the microphone is always open behind the gate): false holds my voice back
        /// as a closed gate does.</summary>
        public bool TalkEnabled = true;
        /// <summary>Where my voice comes from. The game leaves it null (the real microphone); a smoke run sets a generated signal and
        /// never opens the microphone (LokiGame.W6Voice.cs).</summary>
        public Func<IVoiceInput> InputFactory;
        /// <summary>The first-time notice is not needed in this run (smoke runs other than the voice smoke's notice check).</summary>
        public bool SkipNotice;
        /// <summary>Lines for the chat log (the microphone opened, could not be opened).</summary>
        public Action<string, string> Say;

        // ---- my voice
        public bool MicOpen => _input != null;
        public string MicText { get; private set; } = "";
        public string MicError { get; private set; }
        /// <summary>True for a quarter of a second after each frame that left: the icon's green.</summary>
        public bool Sending => Time.unscaledTime - _lastSent < 0.25f && _lastSent > 0f;
        public bool GateOpen => _gate.IsOpen;
        /// <summary>The level of the last microphone frame after the gain and the high-pass (dB full scale).</summary>
        public float LevelDb => _gate.LevelDb;
        /// <summary>The level shown by the meter: the highest of the last moments, falling 30 dB a second.</summary>
        public float MeterDb { get; private set; } = VoiceGate.SilenceDb;
        /// <summary>Frames sent, frames the gate held back, frames the channel did not take (no link, or its ring was full), talk spurts.</summary>
        public int FramesSent { get; private set; }
        public int FramesGated { get; private set; }
        public int FramesRefused { get; private set; }
        public int Spurts { get; private set; }
        public VoiceGate Gate => _gate;
        public bool NoticePending => !VoiceSettings.NoticeDone && !SkipNotice;
        /// <summary>Why my voice is not going out right now ("" while the microphone is open): for the log and the Voice page.</summary>
        public string Holding { get; private set; } = "not started";

        private IVoiceInput _input; private float _retryAt; private string _openedMic;
        private readonly VoiceGate _gate = new VoiceGate();
        private readonly ImaAdpcmCodec _codec = new ImaAdpcmCodec();
        private readonly float[] _frame = new float[VoiceStream.FrameSamples];
        private readonly short[] _pcm = new short[VoiceStream.FrameSamples];
        private readonly byte[] _enc = new byte[ImaAdpcmCodec.FrameBytes];
        private ushort _seq; private float _lastSent; private bool _talking, _saidOpen;

        // ---- friends' voices
        public sealed class Speaker
        {
            public readonly uint Aid;
            public readonly VoiceStream Stream;
            /// <summary>The name / map bytes of the last frame (set by the channel's thread: a new array only when they change).</summary>
            internal volatile byte[] NameRaw, MapRaw;
            internal byte[] NameSeen, MapSeen;
            public string Name = "", Map = "";
            public VoiceSpeaker Player;
            /// <summary>"headset", "character" or "plain": where its voice is placed this frame.</summary>
            public string Anchor = "plain";
            public bool SameMap, Heard, Talking, Ended;
            internal int EndedTick; internal float TalkingUntil;
            public Speaker(uint aid) { Aid = aid; Stream = new VoiceStream(aid); }
        }
        private readonly ConcurrentDictionary<uint, Speaker> _byAid = new ConcurrentDictionary<uint, Speaker>();
        private readonly ConcurrentQueue<Speaker> _fresh = new ConcurrentQueue<Speaker>();
        private readonly List<Speaker> _speakers = new List<Speaker>();
        public IReadOnlyList<Speaker> Speakers => _speakers;
        /// <summary>Voice sources made and freed so far (a killed speaker's source must be freed).</summary>
        public int SourcesMade { get; private set; }
        public int SourcesFreed { get; private set; }

        private DioramaRig _rig; private MapLoader _map; private Func<uint, Sprites.Unit> _unit; private HeadsetAvatars _headsets; private uint _self;
        private RectTransform _icon; private Image[] _iconParts; private float _nextLog;
        private static readonly Color IconIdle = Loki.UI.HudLook.Mark(0.75f) /* client wave 21 (w21tools): the theme's cream at the same alpha (0.78 / 0.8 / 0.86 before) */, IconSending = new Color(0.5f, 0.84f, 0.5f, 1f);

        public static VoiceChat Create(IVoiceChannel channel, DioramaRig rig, MapLoader map, Func<uint, Sprites.Unit> unitByAid, HeadsetAvatars headsets, uint selfAid)
        {
            var v = new GameObject("VoiceChat").AddComponent<VoiceChat>();
            v.Channel = channel; v._rig = rig; v._map = map; v._unit = unitByAid; v._headsets = headsets; v._self = selfAid;
            channel.VoiceReceived = v.OnFrame;
            channel.VoiceEnded += v.OnEnded;
            channel.VoiceLost += v.OnLost;
            v.BuildIcon();
            Instance = v;
            return v;
        }

        /// <summary>We entered a map: my frames carry its name from now on; where each friend's voice is placed is worked out again.</summary>
        public void OnMapChanged(string map) { CurrentMap = map; }

        public bool TryGet(uint aid, out Speaker s) => _byAid.TryGetValue(aid, out s);

        // ---------------------------------------------------------------------------------------------- friends' voices: the channel's thread

        private void OnFrame(ref VoiceFrame f)
        {
            if (f.Aid == _self) return;
            if (!_byAid.TryGetValue(f.Aid, out var sp))
            {
                sp = new Speaker(f.Aid);
                sp.Stream.SetExtraTarget(Channel != null ? Channel.ExtraJitterFrames : 0); // the transport's own lateness (IVoiceChannel)
                // not listened to until the main thread has decided (mute, the switches, the map): nothing of a muted friend is
                // ever buffered, not even its first frames
                sp.Stream.Accept = false;
                // its name and map first, then it is handed over: the main thread never sees a speaker without them
                sp.NameRaw = Copy(f.Buffer, f.NameOffset, f.NameLength); sp.MapRaw = Copy(f.Buffer, f.MapOffset, f.MapLength);
                _byAid[f.Aid] = sp; _fresh.Enqueue(sp);
            }
            else
            {
                if (!Same(sp.NameRaw, f.Buffer, f.NameOffset, f.NameLength)) sp.NameRaw = Copy(f.Buffer, f.NameOffset, f.NameLength);
                if (!Same(sp.MapRaw, f.Buffer, f.MapOffset, f.MapLength)) sp.MapRaw = Copy(f.Buffer, f.MapOffset, f.MapLength);
            }
            sp.Ended = false;
            sp.Stream.Push(f.Seq, f.Codec, f.Flags, f.Buffer, f.DataOffset, f.DataLength);
        }

        private static bool Same(byte[] a, byte[] b, int o, int n)
        {
            if (a == null || a.Length != n) return false;
            for (int i = 0; i < n; i++) if (a[i] != b[o + i]) return false;
            return true;
        }
        private static byte[] Copy(byte[] b, int o, int n) { var r = new byte[n]; Buffer.BlockCopy(b, o, r, 0, n); return r; }

        private void OnEnded(uint aid)
        {
            if (_byAid.TryGetValue(aid, out var sp)) { sp.Ended = true; sp.EndedTick = Environment.TickCount; }
        }

        private void OnLost()
        {
            foreach (var sp in _speakers) { sp.Ended = true; sp.EndedTick = Environment.TickCount; }
            CloseMic("the link to the relay dropped");
        }

        // ---------------------------------------------------------------------------------------------- every frame

        private void LateUpdate()
        {
            if (Channel == null || _rig == null) return;
            float now = Time.unscaledTime;
            UpdateMine(now);
            UpdateSpeakers(now);
            UpdateIcon();
        }

        // ---- my voice

        /// <summary>Why no voice can be sent, by VoiceLinkState (NoLink, OldRelay, SwitchedOffByHost; Ready is not used).</summary>
        private static readonly string[] NoLinkText = { "no voice link (NoLink)", "no voice link (OldRelay)", "no voice link (SwitchedOffByHost)", "" };

        private void UpdateMine(float now)
        {
            var link = Channel.VoiceState;
            string hold = !VoiceSettings.Share ? "Share my voice is off"
                : link != VoiceLinkState.Ready ? NoLinkText[(int)link] // (fixed texts: this runs every frame)
                : CurrentMap == null ? "not on a map yet"
                : Playing != null && !Playing() ? "not in the world"
                : NoticePending ? "the first-time notice is not answered yet"
                : null;
            if (hold != null) { CloseMic(hold); return; }
            if (_input != null && (!_input.Running || _openedMic != VoiceSettings.Mic)) CloseMic(_openedMic != VoiceSettings.Mic ? "another microphone was chosen" : "the microphone stopped");
            if (_input == null && !OpenMic(now)) return;
            Holding = "";
            _gate.SetStep(VoiceSettings.Gate);
            float gain = VoiceSettings.MicGain;
            while (_input.TryRead(_frame))
            {
                if (gain != 1f) for (int i = 0; i < _frame.Length; i++) _frame[i] *= gain;
                var r = _gate.Process(_frame, 0, _frame.Length);
                if (_gate.LevelDb > MeterDb) MeterDb = _gate.LevelDb;
                if (r == VoiceGate.Result.Closed || !TalkEnabled)
                {
                    FramesGated++;
                    if (_talking) { _talking = false; _gate.ForceClose(); } // held back mid-word (TalkEnabled went false): the next frame sent starts a new spurt
                    continue;
                }
                if (r == VoiceGate.Result.First || !_talking) { _codec.ResetEncoder(); Spurts++; r = VoiceGate.Result.First; }
                _talking = r != VoiceGate.Result.Last;
                for (int i = 0; i < _frame.Length; i++)
                {
                    float x = _frame[i] * 32767f;
                    _pcm[i] = x >= 32767f ? (short)32767 : x <= -32768f ? (short)-32768 : (short)Mathf.RoundToInt(x); // too loud clips, never wraps
                }
                int n = _codec.Encode(_pcm, 0, _enc, 0);
                byte flags = r == VoiceGate.Result.First ? PresenceWire.VoiceFlagFirst : r == VoiceGate.Result.Last ? PresenceWire.VoiceFlagLast : (byte)0;
                if (Channel.SendVoice(CurrentMap, _seq++, _codec.Id, flags, _enc, 0, n)) { FramesSent++; _lastSent = now; }
                else FramesRefused++;
            }
            MeterDb = Mathf.Max(VoiceGate.SilenceDb, MeterDb - 30f * Time.unscaledDeltaTime);
        }

        private bool OpenMic(float now)
        {
            if (now < _retryAt) return false;
            IVoiceInput input;
            if (InputFactory != null) input = InputFactory();
            else
            {
                var mic = new MicrophoneInput(VoiceSettings.Mic);
                if (!mic.Start())
                {
                    // try again in a while (a headset that is still starting, a microphone plugged in later), say it once
                    if (MicError != mic.Error) { MicError = mic.Error; Debug.LogWarning($"[Voice] the microphone '{(VoiceSettings.Mic.Length > 0 ? VoiceSettings.Mic : "(Windows' default)")}' could not be opened: {mic.Error}"); Say?.Invoke(Lang.F("Voice: the microphone failed ({0}).", mic.Error), "ffb070"); }
                    Holding = "the microphone could not be opened: " + mic.Error; MicText = "";
                    _retryAt = now + 5f;
                    return false;
                }
                input = mic;
            }
            _input = input; _openedMic = VoiceSettings.Mic; MicError = null; MicText = input.Describe;
            _gate.Reset(); _codec.ResetEncoder(); _talking = false;
            Debug.Log($"[Voice] my microphone is open: {input.Describe}; gate step {VoiceSettings.Gate} (opens at {(VoiceSettings.Gate == 0 ? "any level" : VoiceGate.OpenDbFor(VoiceSettings.Gate) + " dB")}), gain {VoiceSettings.MicGain:0.##}");
            if (!_saidOpen) { _saidOpen = true; Say?.Invoke(Lang.T("Voice on: friends here hear you (Settings → Voice)."), "9ad1ff"); }
            return true;
        }

        private void CloseMic(string why)
        {
            if (Holding != why) Holding = why;
            if (_input == null) return;
            try { _input.Dispose(); } catch (Exception e) { Debug.LogException(e); }
            _input = null; MicText = "";
            _gate.Reset(); _talking = false; MeterDb = VoiceGate.SilenceDb;
            Channel.StopVoice(); // the listeners free my stream
            Debug.Log($"[Voice] my microphone is closed: {why} ({FramesSent} frames sent so far)");
        }

        // ---- friends' voices

        private void UpdateSpeakers(float now)
        {
            while (_fresh.TryDequeue(out var f)) _speakers.Add(f);
            if (_speakers.Count == 0) return;
            var m = _map != null ? _map.transform : null;
            Vector3 head = _rig.Head.transform.position;
            bool hear = VoiceSettings.Hear, others = VoiceSettings.HearOtherMaps; float volume = VoiceSettings.Volume;
            for (int i = _speakers.Count - 1; i >= 0; i--)
            {
                var sp = _speakers[i];
                var raw = sp.NameRaw;
                if (!ReferenceEquals(raw, sp.NameSeen)) { sp.NameSeen = raw; try { sp.Name = new UTF8Encoding(false, true).GetString(raw); } catch (ArgumentException) { sp.Name = "?"; } }
                raw = sp.MapRaw;
                if (!ReferenceEquals(raw, sp.MapSeen)) { sp.MapSeen = raw; sp.Map = Encoding.ASCII.GetString(raw); }

                // an ended speaker (it switched off, left, the link dropped): let what is buffered play out, then free the source;
                // a frame that came after the end means it talks again
                if (sp.Ended)
                {
                    if (sp.Stream.LastPushTick - sp.EndedTick > 0) sp.Ended = false;
                    else if (!sp.Stream.Playing && sp.Stream.Buffered == 0) { Free(sp, i, "it stopped sharing or left"); continue; }
                }
                else if (!sp.Stream.Playing && sp.Stream.LastPushTick != 0 && Environment.TickCount - sp.Stream.LastPushTick > FreeAfterIdleSeconds * 1000f) { Free(sp, i, "silent for " + FreeAfterIdleSeconds + " s"); continue; }

                sp.SameMap = CurrentMap != null && string.Equals(sp.Map, CurrentMap, StringComparison.OrdinalIgnoreCase);
                bool muted = VoiceSettings.PeerMuted(sp.Aid);
                sp.Heard = hear && !muted && (sp.SameMap || PlainOnly || others);
                // where it comes from: the floating headset, else the character the RO server shows, else nowhere
                Vector3 pos = head; string anchor = "plain";
                if (sp.Heard && sp.SameMap && !PlainOnly) // client wave 8 (w8game): PlainOnly
                {
                    if (_headsets != null && _headsets.TryGet(sp.Aid, out var a) && a.Visible) { pos = a.Head.position; anchor = "headset"; }
                    else
                    {
                        var u = _unit?.Invoke(sp.Aid);
                        if (u != null && u.isActiveAndEnabled && m != null) { pos = u.transform.position + m.up * (CharacterHeadCells * m.lossyScale.x); anchor = "character"; }
                    }
                }
                sp.Anchor = anchor;
                sp.Stream.Gain = volume * VoiceSettings.PeerVolume(sp.Aid) * (anchor == "plain" ? AwayGain : 1f);
                sp.Stream.Accept = sp.Heard;
                if (sp.Player == null)
                {
                    sp.Player = VoiceSpeaker.Create(transform, sp.Stream); SourcesMade++;
                    Debug.Log($"[Voice] first voice from '{sp.Name}' (AID {sp.Aid}) on {sp.Map}: a voice source is made ({(sp.Heard ? "heard" : "not heard: " + (muted ? "muted" : !hear ? "Hear friends' voices is off" : "another map"))})");
                }
                sp.Player.Place(anchor != "plain", pos);

                // the glow while it talks (held a little so it does not flicker between words)
                if (sp.Stream.Playing && sp.Stream.Level > TalkingLevel) sp.TalkingUntil = now + 0.25f;
                bool talking = now < sp.TalkingUntil;
                if (talking != sp.Talking || talking) Glow(sp, talking);
                sp.Talking = talking;
            }
        }

        private void Glow(Speaker sp, bool on)
        {
            if (_headsets != null) _headsets.SetTalking(sp.Aid, on);
            var u = _unit?.Invoke(sp.Aid);
            if (u != null && u.Overhead != null) u.Overhead.SetVoiceGlow(on && sp.SameMap);
        }

        private void Free(Speaker sp, int index, string why)
        {
            if (sp.Talking) Glow(sp, false);
            if (sp.Player != null) { Destroy(sp.Player.gameObject); sp.Player = null; SourcesFreed++; }
            _speakers.RemoveAt(index);
            _byAid.TryRemove(sp.Aid, out _);
            Debug.Log($"[Voice] '{sp.Name}' (AID {sp.Aid}): {why}; its voice source is freed ({sp.Stream.Played} frames played, {sp.Stream.Underruns} underruns, {sp.Stream.Gaps} lost, {sp.Stream.LateSkipped} late)");
        }

        // ---- the microphone icon over the walking wrist

        private void BuildIcon()
        {
            _icon = UiKit.Panel("VoiceMic", _rig.transform, new Vector2(72, 72), 0.0004f, false);
            // a microphone from boxes: the capsule, its cradle, the stem, the foot
            _iconParts = new[]
            {
                UiKit.Box(_icon, "capsule", new Vector2(0, 12), new Vector2(18, 34), IconIdle).GetComponent<Image>(),
                UiKit.Box(_icon, "cradle-left", new Vector2(-15, 4), new Vector2(4, 22), IconIdle).GetComponent<Image>(),
                UiKit.Box(_icon, "cradle-right", new Vector2(15, 4), new Vector2(4, 22), IconIdle).GetComponent<Image>(),
                UiKit.Box(_icon, "cradle-bottom", new Vector2(0, -9), new Vector2(34, 4), IconIdle).GetComponent<Image>(),
                UiKit.Box(_icon, "stem", new Vector2(0, -17), new Vector2(4, 14), IconIdle).GetComponent<Image>(),
                UiKit.Box(_icon, "foot", new Vector2(0, -25), new Vector2(22, 4), IconIdle).GetComponent<Image>(),
            };
            _icon.gameObject.SetActive(false);
        }

        /// <summary>The icon is shown while my microphone is open; green while my voice is going out.</summary>
        public bool IconShown => _icon != null && _icon.gameObject.activeSelf;
        public bool IconGreen { get; private set; }

        private void UpdateIcon()
        {
            if (_icon == null) return;
            bool show = MicOpen;
            if (_icon.gameObject.activeSelf != show) _icon.gameObject.SetActive(show);
            if (!show) { IconGreen = false; return; }
            var hand = _rig.OffHand; var head = _rig.Head;
            if (hand != null && head != null)
            {
                var toHead = head.transform.position - hand.position; toHead.y = 0; if (toHead.sqrMagnitude > 1e-4f) toHead.Normalize();
                _icon.position = hand.position + Vector3.up * 0.035f + toHead * 0.03f; // under the status icons (BuffBar: 7 cm up)
                UiKit.FaceHead(_icon, head);
            }
            bool green = Sending;
            if (green != IconGreen || _iconParts[0].color != (green ? IconSending : IconIdle))
            {
                IconGreen = green;
                var c = green ? IconSending : IconIdle;
                for (int i = 0; i < _iconParts.Length; i++) _iconParts[i].color = c;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            try { _input?.Dispose(); } catch (Exception e) { Debug.LogException(e); }
            _input = null;
            if (Channel != null) { Channel.VoiceReceived = null; Channel.VoiceEnded -= OnEnded; Channel.VoiceLost -= OnLost; }
        }

        // ---------------------------------------------------------------------------------------------- the Settings window's "Voice" page

        private static string Dim(string s) => $"<color=#9aa3b5>{s}</color>";
        public static string GateName(int step) => step <= 0 ? Lang.T("~gate~Off") : step == 1 ? Lang.T("~gate~Low") : step == 2 ? Lang.T("~gate~Medium") : Lang.T("~gate~High");
        /// <summary>The row of the page that holds the live meter (ComfortWindow refreshes that row alone while the page is open).</summary>
        public const int MeterRow = 5;

        /// <summary>The Voice page's rows; <paramref name="set"/> saves and redraws (ComfortWindow.Set).</summary>
        public static void SettingsRows(List<ListWindow.Row> rows, Action<Action> set)
        {
            var v = Instance;
            var state = v != null ? v.Channel.VoiceState : VoiceLinkState.NoLink;
            rows.Add(new ListWindow.Row
            {
                Highlight = true,
                Text = v == null ? Lang.T("Voice: needs a server with Loki Host's relay.")
                    : state == VoiceLinkState.Ready ? Lang.T("Voice: connected. Everyone on this server hears you.")
                    : state == VoiceLinkState.OldRelay ? Lang.T("Voice: this Loki Host is from before voice. Ask the host to update.")
                    : state == VoiceLinkState.SwitchedOffByHost ? Lang.T("Voice: switched off on this server by its host.")
                    : Lang.T("Voice: not connected (no relay here?)."),
            });
            // client wave 13, item 5 (w13ring): every row by UI/SettingRow.cs (the current state lit, every choice shown); item 17: short texts
            rows.Add(SettingRow.Toggle(Lang.T("Share my voice"), Lang.T("Open mic behind a noise gate"), () => VoiceSettings.Share, x => set(() => VoiceSettings.Share = x)));
            {
                // the microphones: Windows' default, then each device Unity lists (the one chosen stays listed while it is unplugged)
                var mics = new List<string> { "" }; foreach (var d in MicrophoneInput.Devices()) if (!string.IsNullOrEmpty(d) && !mics.Contains(d)) mics.Add(d);
                if (!mics.Contains(VoiceSettings.Mic)) mics.Add(VoiceSettings.Mic);
                var labels = mics.ConvertAll(m => m.Length > 0 ? m : Lang.T("Windows' default")).ToArray();
                rows.Add(SettingRow.Choice(Lang.T("Microphone"), v != null && v.MicOpen ? Lang.T("~mic~Open") : v != null && v.MicError != null ? Lang.F("Could not open: {0}", v.MicError) : Lang.T("~mic~Closed"),
                    labels, () => Mathf.Max(0, mics.IndexOf(VoiceSettings.Mic)), i => set(() => VoiceSettings.Mic = mics[i])));
            }
            rows.Add(SettingRow.Slider(Lang.T("Microphone gain"), null, VoiceSettings.GainSteps[0], VoiceSettings.GainSteps[VoiceSettings.GainSteps.Length - 1], VR.Comfort.FineScale, () => VoiceSettings.MicGain, x => VoiceSettings.MicGain = x, SettingRow.Percent, () => set(() => { })));
            int gate = VoiceSettings.Gate;
            rows.Add(SettingRow.Choice(Lang.T("Noise gate"), gate == 0 ? Lang.T("Everything is sent") : Lang.F("Sent from {0} dB up", Mathf.RoundToInt(VoiceGate.OpenDbFor(gate))),
                new[] { GateName(0), GateName(1), GateName(2), GateName(3) }, () => VoiceSettings.Gate, i => set(() => VoiceSettings.Gate = i)));
            rows.Add(new ListWindow.Row { Text = MeterText() }); // MeterRow
            rows.Add(SettingRow.Toggle(Lang.T("Friends' voices"), null, () => VoiceSettings.Hear, x => set(() => VoiceSettings.Hear = x)));
            rows.Add(SettingRow.Toggle(Lang.T("Friends on other maps"), null, () => VoiceSettings.HearOtherMaps, x => set(() => VoiceSettings.HearOtherMaps = x)));
            rows.Add(SettingRow.Slider(Lang.T("Voice volume"), null, VoiceSettings.VolumeSteps[0], VoiceSettings.VolumeSteps[VoiceSettings.VolumeSteps.Length - 1], VR.Comfort.FineScale, () => VoiceSettings.Volume, x => VoiceSettings.Volume = x, SettingRow.Percent, () => set(() => { })));
            // the friends heard in this session, then the ones with a saved choice
            var seen = new HashSet<uint>();
            if (v != null) foreach (var sp in v._speakers) { seen.Add(sp.Aid); rows.Add(PeerRow(sp.Aid, sp.Name, sp.Talking, sp.SameMap ? null : sp.Map, set)); }
            foreach (var p in UserSettings.Prefs.voicePeers)
                if (p != null && seen.Add(p.aid) && (p.muted || Mathf.Abs(p.volume - 1f) > 0.01f)) rows.Add(PeerRow(p.aid, p.name, false, null, set));
        }

        private static ListWindow.Row PeerRow(uint aid, string name, bool talking, string otherMap, Action<Action> set)
        {
            // client wave 13, item 5 (w13ring): one stepper "◄ ● 100 % ►" from Muted through each volume (choosing a volume unmutes)
            string label = string.IsNullOrEmpty(name) ? "#" + aid : name;
            string what = talking ? "<color=#7fd67f>" + Lang.T("talking") + "</color>" : null;
            string where = string.IsNullOrEmpty(otherMap) ? null : Lang.F("on {0}", otherMap);
            var steps = VoiceSettings.PeerVolumeSteps;
            var labels = new string[steps.Length + 1]; labels[0] = Lang.T("Muted");
            for (int i = 0; i < steps.Length; i++) labels[i + 1] = SettingRow.Percent(steps[i]);
            int Current()
            {
                if (VoiceSettings.PeerMuted(aid)) return 0;
                float vol = VoiceSettings.PeerVolume(aid); int best = 0;
                for (int i = 1; i < steps.Length; i++) if (Mathf.Abs(steps[i] - vol) < Mathf.Abs(steps[best] - vol)) best = i;
                return best + 1;
            }
            var row = SettingRow.Choice(label, what != null && where != null ? what + "  " + where : what ?? where, labels, Current, i => set(() =>
            {
                var p = VoiceSettings.Peer(aid, true, name);
                if (i == 0) p.muted = true; else { p.muted = false; p.volume = steps[i - 1]; }
                Debug.Log($"[Voice] '{name}' (AID {aid}) muted {p.muted}, volume {p.volume:0.##}");
            }));
            row.Stepper = true; // a friend's row is always one stepper (its name is the row's text)
            return row;
        }

        public static void ToggleMute(uint aid, string name) { var p = VoiceSettings.Peer(aid, true, name); p.muted = !p.muted; Debug.Log($"[Voice] '{name}' (AID {aid}) muted {p.muted}"); }
        public static void NextPeerVolume(uint aid, string name)
        {
            var p = VoiceSettings.Peer(aid, true, name);
            var steps = VoiceSettings.PeerVolumeSteps; int best = 0;
            for (int i = 1; i < steps.Length; i++) if (Mathf.Abs(steps[i] - p.volume) < Mathf.Abs(steps[best] - p.volume)) best = i;
            p.volume = steps[(best + 1) % steps.Length];
            Debug.Log($"[Voice] '{name}' (AID {aid}) volume {p.volume:0.##}");
        }

        /// <summary>The next microphone: Windows' default, then each device Unity lists, round and round.</summary>
        public static string NextMic(string current)
        {
            var d = MicrophoneInput.Devices();
            if (d.Length == 0) return "";
            int i = Array.IndexOf(d, current);
            return i + 1 < d.Length ? d[i + 1] : "";
        }

        private const int MeterCells = 24; private const float MeterFloorDb = -72f;
        private static readonly StringBuilder _sb = new StringBuilder(256);
        /// <summary>The live meter: the microphone's level as a bar from −72 to 0 dB (3 dB a mark), the mark where the gate opens in
        /// yellow, and what happens to the voice right now.</summary>
        public static string MeterText()
        {
            var v = Instance;
            if (v == null || !v.MicOpen) return Lang.F("Level: {0}", Dim(v != null && VoiceSettings.Share && v.Holding.Length > 0 ? Lang.T("the microphone is closed (no voice link)") : Lang.T("the microphone is closed")));
            int gate = VoiceSettings.Gate;
            float level = v.MeterDb, open = VoiceGate.OpenDbFor(gate);
            int filled = Mathf.Clamp(Mathf.RoundToInt((level - MeterFloorDb) / -MeterFloorDb * MeterCells), 0, MeterCells);
            int mark = gate == 0 ? -1 : Mathf.Clamp(Mathf.RoundToInt((open - MeterFloorDb) / -MeterFloorDb * MeterCells), 0, MeterCells - 1);
            _sb.Length = 0;
            _sb.Append(v.GateOpen ? "<color=#7fd67f>" : "<color=#c8cbd6>");
            for (int i = 0; i < filled; i++) _sb.Append(i == mark ? "</color><color=#ffd060>|</color>" + (v.GateOpen ? "<color=#7fd67f>" : "<color=#c8cbd6>") : "|");
            _sb.Append("</color><color=#5a6070>");
            for (int i = filled; i < MeterCells; i++) _sb.Append(i == mark ? "</color><color=#ffd060>¦</color><color=#5a6070>" : "·");
            _sb.Append("</color>");
            string bar = _sb.ToString();
            string what = v.Sending ? "<color=#7fd67f>" + Lang.T("your voice is going out") + "</color>" : Dim(gate == 0 ? Lang.T("nothing is going out") : Lang.T("the gate is closed: nothing is going out"));
            return level <= MeterFloorDb ? Lang.F("Level: silent  {0}  {1}", bar, what) : Lang.F("Level: {0} dB  {1}  {2}", Mathf.RoundToInt(level), bar, what);
        }

        // ---------------------------------------------------------------------------------------------- the player menu's rows

        /// <summary>Two rows for the player menu (Game/PlayerMenu.cs Extra): this player's voice volume and mute, saved for this Windows user.</summary>
        public static void PlayerMenuRows(Sprites.Unit u, Action<string, string, Action> add)
        {
            if (u == null || !u.IsPlayerType || u.IsSelf) return;
            uint aid = u.Id; string name = u.Name ?? "";
            bool muted = VoiceSettings.PeerMuted(aid);
            add(Lang.F("Their voice: {0} %", Mathf.RoundToInt(VoiceSettings.PeerVolume(aid) * 100)), Lang.T("Volume"), () => { NextPeerVolume(aid, name); VoiceSettings.Save(); });
            add(muted ? Lang.T("You muted their voice") : Lang.T("Mute their voice"), muted ? Lang.T("Unmute") : Lang.T("Mute"), () => { ToggleMute(aid, name); VoiceSettings.Save(); });
        }
    }
}
