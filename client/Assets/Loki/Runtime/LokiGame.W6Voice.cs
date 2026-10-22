using System.Collections.Generic;
using Loki.Net;
using Loki.UI;
using Loki.Voice;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 6, w6voice: voice between friends (design note, 2026-10-04: an always-open microphone with a noise gate, over the
    /// headset relay). The game's side of <see cref="IVoiceChannel"/>: once the headset channel exists (LokiGame.W3Presence.cs:
    /// the pose relay's connection is the voice channel too), a <see cref="VoiceChat"/> that sends this player's voice and plays
    /// the others'. Outside the RO protocol: nothing here reads or writes an RO packet, so the same code works on the SpacetimeDB
    /// module once a SpacetimeDB IVoiceChannel exists (Loki Online sessions get none today, as for the headsets).
    /// Hooks in LokiGame.cs: BeginW6Voice (the player menu's two rows, the notice window), EnterMap (OnMapChangeW6Voice),
    /// Update (UpdateW6Voice: the first-time notice, the Voice page's live meter), the smoke dispatch.
    /// The first time voice could go out, a notice says what is sent, to whom and how to switch it off; the microphone is not
    /// opened before it is answered.
    /// </summary>
    public sealed partial class LokiGame
    {
        public VoiceChat Voices { get; private set; }
        public ListWindow VoiceNotice { get; private set; }
        private float _w6NextMeter;

        private void BeginW6Voice(LokiBootstrap boot)
        {
            // the player menu: this player's voice volume and mute, after the rows client wave 2 added
            var before = PlayerMenu.Extra;
            PlayerMenu.Extra = (u, add) => { before?.Invoke(u, add); VoiceChat.PlayerMenuRows(u, add); };
            VoiceNotice = ListWindow.Create(boot.Rig.transform, "VoiceNoticeWindow");
            VoiceNotice.OnCloseClicked = () => W6VoiceNoticeAnswer(true); // closing it leaves voice on, as "Share my voice" does
        }

        /// <summary>Hook: EnterMap, after OnMapChangeW3Presence (which makes the channel on the first map).</summary>
        private void OnMapChangeW6Voice(string map)
        {
            if (Voices == null && Headsets != null && _presence is IVoiceChannel channel) StartVoice(channel);
            Voices?.OnMapChanged(map);
        }

        private void StartVoice(IVoiceChannel channel)
        {
            Voices = VoiceChat.Create(channel, _boot.Rig, _boot.Map, id => Entities != null ? Entities.Get(id) : null, Headsets, Session.AccountId);
            Voices.Playing = () => Session != null && Session.State == RoSession.Stage.InGame && Session.Leaving == RoSession.Leave.None;
            Voices.Say = (t, c) => Chat(t, c);
            Debug.Log($"[Voice] voice over the {(_presence != null ? _presence.Describe : "channel")}: {VoiceSettings.Summary()}; microphones listed {MicrophoneInput.Devices().Length}");
        }

        /// <summary>Hook: Update. The first-time notice, and the Voice page's level meter while that page is open.</summary>
        private void UpdateW6Voice()
        {
            var v = Voices;
            if (v == null) return;
            if (v.NoticePending && VoiceSettings.Share && v.Channel.VoiceState == VoiceLinkState.Ready && !VoiceNotice.Visible
                && Session != null && Session.State == RoSession.Stage.InGame && Entities.Self != null && (Tutorial == null || !Tutorial.Visible)
                && !W18TutRunning) // (client wave 18, w18tut: not over the interactive tutorial: it waits until the tutorial ends)
                ShowVoiceNotice();
            if (ComfortUi != null && ComfortUi.Visible && ComfortUi.Current == ComfortWindow.Page.Voice && Time.unscaledTime >= _w6NextMeter)
            {
                _w6NextMeter = Time.unscaledTime + 0.1f;
                ComfortUi.SetRowText(VoiceChat.MeterRow, VoiceChat.MeterText());
            }
        }

        /// <summary>The one notice (first time only): what is sent, to whom, how to switch it off. Until it is answered the microphone
        /// stays closed (VoiceChat.NoticePending).</summary>
        private void ShowVoiceNotice()
        {
            var w = VoiceNotice;
            w.SetTabs(null, 0, null); w.SetInput(false);
            w.SetTitle(Lang.T("Voice chat"));
            w.SetInfo(Lang.T("Shown once. Your microphone is not open yet."));
            w.SetRows(new List<ListWindow.Row>
            {
                new ListWindow.Row { Highlight = true, Text = Lang.T("Loki can carry your voice to your friends on this server.") },
                new ListWindow.Row { Text = Lang.T("Your microphone stays open; your voice is sent when you speak.") },
                new ListWindow.Row { Text = Lang.T("A noise gate keeps silence and background noise from being sent.") },
                new ListWindow.Row { Text = Lang.T("Everyone on this server hears it, on any map. Nothing is recorded.") },
                new ListWindow.Row { Text = Lang.T("The microphone icon over your wrist is green while your voice goes out.") },
                new ListWindow.Row { Text = Lang.T("To switch it off: Settings → Voice → Share my voice.") },
            }, true);
            w.SetFooter((Lang.T("Share my voice"), () => W6VoiceNoticeAnswer(true)), (Lang.T("Keep it off"), () => W6VoiceNoticeAnswer(false)));
            w.Open(_boot.Rig.Head);
            Debug.Log("[Voice] the first-time notice is shown (the microphone stays closed until it is answered)");
        }

        /// <summary>The notice's answer, saved: share (the default the design specified), or keep voice off.</summary>
        public void W6VoiceNoticeAnswer(bool share)
        {
            if (VoiceSettings.NoticeDone && !VoiceNotice.Visible) return;
            VoiceNotice.Hide();
            VoiceSettings.NoticeDone = true; VoiceSettings.Share = share;
            VoiceSettings.Save();
            Debug.Log($"[Voice] the first-time notice was answered: share my voice {share} (saved in {Loki.World.UserSettings.FilePath})");
            Chat(share ? Lang.T("Your voice is shared on this server. Settings → Voice turns it off.") : Lang.T("Your voice is not shared. Settings → Voice switches it on."), "9ad1ff");
        }
    }
}
