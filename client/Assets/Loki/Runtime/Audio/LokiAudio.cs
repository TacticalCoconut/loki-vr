using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Loki.Formats;
using Loki.IO;
using Loki.World;
using UnityEngine;
using UnityEngine.Networking;

namespace Loki.Audio
{
    /// <summary>
    /// Music and sound effects.
    ///  • BGM: data\mp3nametable.txt lines "map.rsw#bgm\\NN.mp3#" (first match wins, "//" comments) → &lt;client&gt;\BGM\NN.mp3,
    ///    streamed with UnityWebRequestMultimedia (AudioType.MPEG) and looped. ZC_PLAY_NPC_BGM overrides it (rAthena playBGM: file in /BGM/, ".mp3" optional).
    ///  • Sound effects: PCM WAVs from data\wav\ (RIFF/fmt/data, 8- or 16-bit), decoded here into AudioClips.
    ///  • The diorama is a miniature, so hearing is computed from the player's character in the map (in cells), like the
    ///    original client's camera, and panned left/right relative to the head — not true 3D at table scale.
    /// </summary>
    public sealed class LokiAudio : MonoBehaviour
    {
        public static LokiAudio Instance { get; private set; }
        public float MusicVolume = 0.45f, EffectsVolume = 0.9f;
        public float HearingCells = 18f;

        private RoFileSystem _fs; private string _clientRoot;
        private AudioSource _bgm; private string _bgmFile; private bool _bgmLoading; private Coroutine _bgmRoutine;
        public bool HordeBgmActive { get; private set; }
        private string _hordeBgmFile;
        private static readonly string[] HordeBattleTracks = { "bgm\\71.mp3", "bgm\\64.mp3", "bgm\\20.mp3", "bgm\\37.mp3", "bgm\\25.mp3" };
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        private readonly List<AudioSource> _pool = new List<AudioSource>();
        private Dictionary<string, string> _mp3Table;
        private AudioClip _click;
        private readonly List<Ambient> _ambient = new List<Ambient>();

        private sealed class Ambient { public Vector3 Pos; public float Range, Volume, Cycle, Next; public string File; public bool Muted; public float Gain = 1f; } // (client wave 25, w25misc: Gain, item 12)

        /// <summary>Cell position of the listener (the player's character) and the map root for panning.</summary>
        public Func<Vector2?> ListenerCell;
        public Transform MapRoot;

        public void Init(RoFileSystem fs, string clientRoot)
        {
            Instance = this; _fs = fs; _clientRoot = clientRoot;
            _bgm = gameObject.AddComponent<AudioSource>(); _bgm.loop = true; _bgm.spatialBlend = 0; _bgm.volume = MusicVolume * MusicGain; _bgm.playOnAwake = false;
            _click = MakeClick();
        }

        // ------------------------------------------------------------------ the tutorial's quiet music (client wave 23, w23audio)
        // design note, 2026-10-11 (answer 36 of playtest notes): "on the tutorial screen, the music is
        // too loud." While the tutorial runs (MusicQuiet: LokiGame.W23Audio.cs says when) the music source plays at TutorialMusicGain x
        // the player's own music volume. The Setting itself (MusicVolume = Settings → Audio "Music", VR/Comfort.cs ApplyAudio) is never
        // written here and still scales the music: the source's volume is MusicVolume x MusicGain, set once a frame (Update) and when a
        // track starts (LoadBgm). The gain glides to its new value in MusicFadeSeconds, in real time; one frame moves it by at most
        // MusicFadeMaxStep seconds' worth, so a long frame (a map load) is not heard as a jump.
        /// <summary>The music's share of the player's music volume while the tutorial runs.</summary>
        public const float TutorialMusicGain = 0.35f;
        /// <summary>The glide into and out of the quiet music, in seconds; the most one frame counts for, in seconds.</summary>
        public const float MusicFadeSeconds = 1f, MusicFadeMaxStep = 0.05f;
        /// <summary>True while the music is to play quietly (null: never). Set by LokiGame (LokiGame.W23Audio.cs W23MusicQuiet).</summary>
        public Func<bool> MusicQuiet;
        /// <summary>The music's gain now: 1 = the player's music volume, TutorialMusicGain while quiet, between the two during the glide.</summary>
        public float MusicGain { get; private set; } = 1f;
        /// <summary>What the music source plays at now (MusicVolume x MusicGain, x MusicFade since client wave 24: 1 but for the login
        /// screen's change; before the listener's master volume); a track is playing.</summary>
        public float MusicOutput => _bgm != null ? _bgm.volume : 0f;
        public bool MusicPlaying => _bgm != null && _bgm.isPlaying;

        // ------------------------------------------------------------------ the login screen's music (client wave 24, w24)
        // design note, 2026-10-12: "require the login music to be present in the login screen." The original client plays ONE
        // track on the screens before the world, the first of its music list: BGM\01.mp3, started by the login screen and again by the
        // character screen (the only source on this PC is the reimplementation roBrowserLegacy: src/Engine/GameEngine.js:169 and
        // src/Engine/CharEngine.js:68, both BGM.play('01.mp3')). Loki finds it the way ZC_PLAY_NPC_BGM's tracks are found
        // (PlayNamedBgm: the player's game folder, BGM\name.mp3): nothing is shipped; a game folder without the file: silence.
        // It plays at the player's music volume (MusicVolume x MusicGain, as every track). ENTERING THE WORLD it fades out in
        // LoginFadeSeconds and the map's track fades in over the same time (LeaveLoginBgm; a map without a track: the fade ends in
        // silence). Only this change fades: a track that follows another one in the world starts at once, as before. The tutorial's
        // quiet music (MusicGain above) multiplies in as always.
        /// <summary>The login screen's track: its name in the game folder's BGM folder (FLAGGED: the original client's first track).</summary>
        public const string LoginTrack = "01";
        /// <summary>The login screen's track fades out in this many seconds on entering the world; what follows fades in over the same.</summary>
        public const float LoginFadeSeconds = 1.5f;
        /// <summary>The login screen's track is asked for now (the screens before the world).</summary>
        public bool LoginMusic { get; private set; }
        /// <summary>The fade's factor on the music now: 1 except while the login screen's track gives way to the world's.</summary>
        public float MusicFade { get; private set; } = 1f;
        /// <summary>The file name of the track that sounds now ("" when none does).</summary>
        public string MusicFile => _bgm != null && _bgm.isPlaying && !string.IsNullOrEmpty(_playingFile) ? Path.GetFileName(_playingFile) : "";
        private string _playingFile = "", _loginFile = "", _nextFile; private AudioClip _nextClip; private bool _fadeOut, _playingLogin, _fadeInNext;

        private void CancelBgmLoad()
        {
            if (_bgmRoutine != null) { StopCoroutine(_bgmRoutine); _bgmRoutine = null; }
            _bgmLoading = false;
        }

        /// <summary>The screens before the world: the login track from the player's own game folder (none there: silence, no error).</summary>
        public void PlayLoginBgm()
        {
            HordeBgmActive = false; _hordeBgmFile = null;
            _loginFile = BgmPath("bgm\\" + LoginTrack + ".mp3");
            if (!File.Exists(_loginFile)) { Debug.Log("[Audio] login music: no BGM\\" + LoginTrack + ".mp3 in the game folder: silence"); return; }
            LoginMusic = true; _fadeOut = false; _nextClip = null;
            PlayBgmPath(_loginFile);
        }

        /// <summary>Entering the world, after the map's track was asked for (<paramref name="mapHasTrack"/>: it has one): the login
        /// screen's track fades out; the map's fades in when the fade is done (none: silence).</summary>
        public void LeaveLoginBgm(bool mapHasTrack)
        {
            if (!LoginMusic) return;
            LoginMusic = false;
            if (mapHasTrack && string.Equals(_bgmFile, _loginFile, StringComparison.OrdinalIgnoreCase)) { _playingLogin = false; return; } // (the map's own track is the same file: it plays on)
            if (!mapHasTrack) { CancelBgmLoad(); _bgmFile = null; } // (a login track still loading never starts in the world)
            if (_bgm.isPlaying) { _fadeOut = true; _fadeInNext = true; } // (the track that follows fades in, however long it takes to load)
        }

        private void StartClip(AudioClip clip, string file, bool fadeIn)
        {
            _fadeOut = false; _nextClip = null; _nextFile = null; _fadeInNext = false;
            MusicFade = fadeIn ? 0f : 1f;
            _playingFile = file; _playingLogin = LoginMusic && string.Equals(file, _loginFile, StringComparison.OrdinalIgnoreCase);
            _bgm.Stop(); _bgm.clip = clip; _bgm.volume = MusicVolume * MusicGain * MusicFade; _bgm.Play();
        }

        // ------------------------------------------------------------------ music
        /// <summary>map name → absolute path of a replacement track (loki.json "bgmOverrides").</summary>
        private readonly Dictionary<string, string> _overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public void SetOverrides(LokiConfig.BgmOverride[] list)
        {
            _overrides.Clear();
            if (list == null) return;
            foreach (var o in list)
            {
                if (o == null || string.IsNullOrEmpty(o.map) || string.IsNullOrEmpty(o.file)) continue;
                _overrides[o.map] = Path.Combine(Application.streamingAssetsPath, o.file.Replace('\\', '/'));
            }
            if (_overrides.Count > 0) Debug.Log($"[Audio] {_overrides.Count} BGM override(s): " + string.Join(", ", _overrides.Keys));
        }

        private string ResolveMapBgmPath(string mapName)
        {
            if (string.IsNullOrEmpty(mapName)) return null;
            if (_overrides.TryGetValue(mapName, out var ov) && File.Exists(ov)) return ov;
            LoadMp3Table();
            if (_mp3Table != null && _mp3Table.TryGetValue(mapName.ToLowerInvariant() + ".rsw", out var rel))
            {
                string path = BgmPath(rel);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        /// <summary>Starts dedicated Horde Mode music, stopping any previous map BGM and blocking regular map BGM until <see cref="StopHordeBgm"/>.</summary>
        public bool PlayHordeBgm(string arenaMap, string worldMap = null)
        {
            HordeBgmActive = true;
            LoginMusic = false; _playingLogin = false; _fadeOut = false; _fadeInNext = false; _nextClip = null; _nextFile = null;
            MusicFade = 1f;
            string arenaFile = ResolveMapBgmPath(arenaMap);
            string worldFile = ResolveMapBgmPath(worldMap);
            string chosen = arenaFile;
            bool sameAsCurrent = !string.IsNullOrEmpty(chosen) &&
                (string.Equals(chosen, _playingFile, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(chosen, _bgmFile, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(chosen, worldFile, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(chosen) || sameAsCurrent)
            {
                foreach (var rel in HordeBattleTracks)
                {
                    string cand = BgmPath(rel);
                    if (File.Exists(cand) && !string.Equals(cand, _playingFile, StringComparison.OrdinalIgnoreCase) && !string.Equals(cand, worldFile, StringComparison.OrdinalIgnoreCase))
                    {
                        chosen = cand;
                        break;
                    }
                }
            }
            if (string.IsNullOrEmpty(chosen)) chosen = arenaFile;
            _hordeBgmFile = chosen;
            CancelBgmLoad();
            if (_bgm != null) _bgm.Stop();
            _bgmFile = null;
            _playingFile = "";
            if (string.IsNullOrEmpty(chosen)) return false;
            return PlayBgmPath(chosen);
        }

        /// <summary>Exits Horde Mode BGM lock and resumes normal map BGM.</summary>
        public void StopHordeBgm(string worldMap)
        {
            HordeBgmActive = false;
            _hordeBgmFile = null;
            CancelBgmLoad();
            _bgmFile = null;
            _playingFile = "";
            if (!string.IsNullOrEmpty(worldMap)) PlayMapBgm(worldMap);
        }

        /// <summary>The map's track (true: it has one and the file is there; client wave 24: the answer, for LeaveLoginBgm).</summary>
        public bool PlayMapBgm(string mapName)
        {
            if (HordeBgmActive) return true;
            if (_overrides.TryGetValue(mapName, out var ov) && File.Exists(ov)) return PlayBgmPath(ov);
            LoadMp3Table();
            if (_mp3Table != null && _mp3Table.TryGetValue(mapName.ToLowerInvariant() + ".rsw", out var rel)) return PlayBgmFile(rel);
            Debug.Log($"[Audio] no BGM entry for {mapName}");
            return false;
        }

        /// <summary>From ZC_PLAY_NPC_BGM: a name in the BGM folder, extension optional.</summary>
        public void PlayNamedBgm(string name)
        {
            if (HordeBgmActive || string.IsNullOrEmpty(name)) return;
            if (!name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) name += ".mp3";
            PlayBgmFile("bgm\\" + name);
        }

        private string BgmPath(string rel) => Path.Combine(_clientRoot ?? "", rel.Replace("\\\\", "\\").Replace('\\', Path.DirectorySeparatorChar));
        private bool PlayBgmFile(string rel) => PlayBgmPath(BgmPath(rel));

        /// <summary>True: the track plays, loads, or is the one asked for already (client wave 24: the answer).</summary>
        private bool PlayBgmPath(string file)
        {
            if (string.Equals(file, _bgmFile, StringComparison.OrdinalIgnoreCase) && (_bgm.isPlaying || _bgmLoading)) return true;
            _bgmFile = file;
            if (!File.Exists(file)) { Debug.LogWarning("[Audio] BGM file missing: " + file); return false; }
            CancelBgmLoad();
            _bgmRoutine = StartCoroutine(LoadBgm(file));
            return true;
        }

        private IEnumerator LoadBgm(string file)
        {
            string uri = new Uri(file).AbsoluteUri;
            string ext = Path.GetExtension(file).ToLowerInvariant();
            var type = ext == ".ogg" ? AudioType.OGGVORBIS : ext == ".wav" ? AudioType.WAV : AudioType.MPEG;
            using (var req = UnityWebRequestMultimedia.GetAudioClip(uri, type))
            {
                ((DownloadHandlerAudioClip)req.downloadHandler).streamAudio = false;
                _bgmLoading = true;
                yield return req.SendWebRequest();
                _bgmLoading = false;
                _bgmRoutine = null;
                if (req.result != UnityWebRequest.Result.Success) { Debug.LogWarning($"[Audio] BGM load failed {file}: {req.error}"); yield break; }
                var clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null || clip.length <= 0f) { Debug.LogWarning("[Audio] BGM decoded empty: " + file); yield break; }
                if (_bgmFile != file) yield break; // superseded while loading
                // (client wave 23, w23audio: a track that starts during the tutorial starts quiet. Client wave 24, w24: the login
                // screen's track still sounding fades out first and this one fades in after it: Update; any other change is at once)
                if (!HordeBgmActive && _playingLogin && _bgm.isPlaying && !string.Equals(file, _loginFile, StringComparison.OrdinalIgnoreCase)) { _nextClip = clip; _nextFile = file; _fadeOut = true; }
                else StartClip(clip, file, !HordeBgmActive && _fadeInNext);
                Debug.Log($"[Audio] BGM {Path.GetFileName(file)} {clip.length:F0}s {clip.frequency}Hz x{clip.channels}");
            }
        }

        private void LoadMp3Table()
        {
            if (_mp3Table != null) return;
            _mp3Table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var data = _fs?.Read("data\\mp3nametable.txt");
            if (data == null) { Debug.LogWarning("[Audio] data\\mp3nametable.txt not found"); return; }
            var text = System.Text.Encoding.GetEncoding(28591).GetString(data);
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("//")) continue;
                var p = line.Split('#');
                if (p.Length < 2 || p[0].Length == 0 || p[1].Length == 0) continue;
                if (!_mp3Table.ContainsKey(p[0])) _mp3Table[p[0]] = p[1];
            }
            Debug.Log($"[Audio] mp3nametable: {_mp3Table.Count} maps");
        }

        // ------------------------------------------------------------------ effects
        /// <summary>Plays data\wav\&lt;name&gt; as heard from the player's character; cell = where it happens (null = no attenuation).
        /// Client wave 9 (h9feel): the same wav is not started again within <see cref="SameWavGap"/> (a crowd's identical sounds pile up into one).</summary>
        public void PlayWav(string name, Vector2? cell = null, float volume = 1f)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (_lastPlayed.TryGetValue(name, out float last) && Time.time - last < SameWavGap) { DroppedSameWav++; return; }
            var clip = Clip(name);
            if (clip == null) return;
            float vol = volume * EffectsVolume, pan = 0f;
            if (cell.HasValue && !Attenuate(cell.Value, HearingCells, ref vol, ref pan)) return;
            _lastPlayed[name] = Time.time;
            var src = FreeSource();
            src.clip = clip; src.volume = vol; src.panStereo = pan; src.Play();
        }

        private AudioClip _petFanfare, _itemUseFallback;
        private static readonly string[] PetCaptureWavs = { "effect\\pet_success.wav", "effect\\st_job_level_up.wav", "effect\\ef_angel.wav", "levelup.wav" };
        private static readonly string[] PotionUseWavs = { "_heal_effect.wav", "effect\\ef_potion.wav", "_cure_effect.wav", "se_equip_item_01.wav" };
        private static readonly Dictionary<AudioClip, float> _clipRms = new Dictionary<AudioClip, float>();

        /// <summary>Plays an item/potion use sound effect for <paramref name="itemId"/> from the player's install (or a synthesized potion chime if absent).</summary>
        public void PlayItemUse(int itemId, Vector2? cell = null)
        {
            string preferred = null;
            if (itemId == 506 || itemId == 525) preferred = "_cure_effect.wav"; // Green Potion / Panacea
            else if (itemId == 601 || itemId == 602) preferred = "effect\\ef_teleportation.wav"; // Fly Wing / Butterfly Wing
            else if (itemId == 645 || itemId == 656 || itemId == 657) preferred = "effect\\ef_incagility.wav"; // Concentration / Awakening / Berserk Potion
            if (preferred != null && Clip(preferred) != null)
            {
                PlayWav(preferred, cell, 0.85f);
                return;
            }
            foreach (var w in PotionUseWavs)
            {
                if (Clip(w) != null)
                {
                    PlayWav(w, cell, 0.85f);
                    return;
                }
            }
            if (_itemUseFallback == null) _itemUseFallback = MakeItemUseClip();
            PlayClip(_itemUseFallback, 0.7f);
        }

        private static AudioClip MakeItemUseClip()
        {
            const int sr = 44100;
            const float dur = 0.32f;
            int n = Mathf.RoundToInt(sr * dur);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float f = t < 0.14f ? Mathf.Lerp(420f, 660f, t / 0.14f) : Mathf.Lerp(660f, 880f, (t - 0.14f) / 0.18f);
                float env = Mathf.Min(1f, t / 0.01f) * Mathf.Exp(-7.5f * t / dur);
                data[i] = Mathf.Clamp(env * (0.6f * Mathf.Sin(2f * Mathf.PI * f * t) + 0.25f * Mathf.Sin(4f * Mathf.PI * f * t)), -1f, 1f);
            }
            var c = AudioClip.Create("item_use", n, 1, sr, false);
            c.SetData(data, 0);
            return c;
        }

        /// <summary>Wave 28 (item 74): plays the pet capture celebratory fanfare from the player's install (or a synthesized major-triad fanfare if absent).</summary>
        public void PlayPetCaptureSuccess(Vector2? cell = null)
        {
            foreach (var w in PetCaptureWavs)
            {
                if (HasWav(w))
                {
                    PlayWav(w, cell, 1f);
                    return;
                }
            }
            if (_petFanfare == null) _petFanfare = MakePetFanfare();
            PlayClip(_petFanfare, 0.8f);
        }

        private AudioClip _bountyFanfare;
        private float _lastBountyFanfareAt = -10f;

        /// <summary>Plays the custom Codex Bounty completion/claim fanfare (page-turn swish + warm scholarly harp-brass arpeggio, distinct from levelup.wav).</summary>
        public void PlayBountyFanfare()
        {
            if (Time.unscaledTime - _lastBountyFanfareAt < 0.25f) return;
            _lastBountyFanfareAt = Time.unscaledTime;
            if (_bountyFanfare == null) _bountyFanfare = MakeBountyFanfare();
            PlayClip(_bountyFanfare, 0.78f);
        }

        private static AudioClip MakeBountyFanfare()
        {
            const int sr = 44100;
            const float dur = 1.35f;
            int n = Mathf.RoundToInt(sr * dur);
            var data = new float[n];
            // Warm scholarly F-Lydian / D-major seventh harp-brass fanfare: D5 (587.33) -> F#5 (739.99) -> A5 (880.00) -> C#6 (1108.73) -> D6 (1174.66)
            float[] notes = { 587.33f, 739.99f, 880.00f, 1108.73f, 1174.66f };
            float[] starts = { 0.06f, 0.16f, 0.26f, 0.36f, 0.48f };
            float[] lengths = { 0.28f, 0.28f, 0.28f, 0.28f, 0.85f };
            var rng = new System.Random(1017);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float s = 0f;
                // Soft parchment page-turn swish in the first 0.16 s
                if (t < 0.16f)
                {
                    float wEnv = Mathf.Sin((t / 0.16f) * Mathf.PI);
                    float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                    s += wEnv * noise * 0.08f;
                }
                for (int k = 0; k < notes.Length; k++)
                {
                    float dt = t - starts[k];
                    if (dt < 0f || dt > lengths[k]) continue;
                    float env = Mathf.Min(1f, dt / 0.012f) * Mathf.Exp(-2.8f * dt / lengths[k]);
                    float f = notes[k];
                    s += env * (0.58f * Mathf.Sin(2f * Mathf.PI * f * dt)
                              + 0.24f * Mathf.Sin(4f * Mathf.PI * f * dt)
                              + 0.12f * Mathf.Sin(3f * Mathf.PI * f * dt)
                              + 0.06f * Mathf.Sin(6f * Mathf.PI * f * dt));
                    if (k == notes.Length - 1)
                    {
                        // Warm fifth harmony underneath the final sustain (A5 880 Hz + D5 587.33 Hz)
                        s += env * 0.28f * Mathf.Sin(2f * Mathf.PI * 880.00f * dt);
                        s += env * 0.22f * Mathf.Sin(2f * Mathf.PI * 587.33f * dt);
                    }
                }
                data[i] = Mathf.Clamp(s * 0.44f, -1f, 1f);
            }
            var c = AudioClip.Create("codex_bounty_fanfare", n, 1, sr, false);
            c.SetData(data, 0);
            return c;
        }
        private static AudioClip MakePetFanfare()
        {
            const int sr = 44100;
            const float dur = 1.15f;
            int n = Mathf.RoundToInt(sr * dur);
            var data = new float[n];
            // Rising celebratory arpeggio: C5 (523.25) -> E5 (659.25) -> G5 (783.99) -> C6 (1046.50)
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.50f };
            float[] starts = { 0.00f, 0.13f, 0.26f, 0.39f };
            float[] lengths = { 0.22f, 0.22f, 0.22f, 0.72f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float s = 0f;
                for (int k = 0; k < notes.Length; k++)
                {
                    float dt = t - starts[k];
                    if (dt < 0f || dt > lengths[k]) continue;
                    float env = Mathf.Min(1f, dt / 0.015f) * Mathf.Exp(-3.2f * dt / lengths[k]);
                    float f = notes[k];
                    s += env * (0.65f * Mathf.Sin(2f * Mathf.PI * f * dt) + 0.25f * Mathf.Sin(4f * Mathf.PI * f * dt) + 0.10f * Mathf.Sin(6f * Mathf.PI * f * dt));
                }
                data[i] = Mathf.Clamp(s * 0.45f, -1f, 1f);
            }
            var c = AudioClip.Create("pet_fanfare", n, 1, sr, false);
            c.SetData(data, 0);
            return c;
        }

        // ------------------------------------------------------------------ unit sounds (client wave 9, h9feel)
        // design note, 2026-10-04 23:55, playing on the deployed build: "There are too many sound effects everywhere. Every death sound, every
        // passive mob sound, it's way, way too much." Those are the monsters' and other players' ACT frame sounds (idle / move / attack /
        // die wavs, Audio/WeaponSounds.cs ActEvent), one per unit the table shows (view range 30 cells), with no limit until now and heard
        // 15 % loud at 18 cells (PlayWav's floor). Now (every number here is Loki's own design, not the original client's):
        //  - the same wav never starts twice within SameWavGap (160 ms): a pack's eight identical growls are one growl;
        //  - a budget of UnitBudget starts a second for the sounds of units other than the player (6 at "low", the default; 12 at
        //    "normal"), the NEAREST units first: a frame's requests are kept until LateUpdate, sorted by distance, and the ones over the
        //    budget are dropped silently;
        //  - a tighter attenuation for those units: full volume within UnitFullCells (3), linearly to silence at UnitHearingCells (10 at
        //    "low", 14 at "normal"; the player's own actions keep PlayWav's hearing: HearingCells 18 with a 15 % floor);
        //  - the Settings → Sound row "Monster sounds" (Off / Low / Normal, default Low): MonsterSounds, loaded / saved by VR/Comfort.cs.
        // The Horde arena's monster sounds (Horde/Client/HordeFeel.cs) go through the same gate (PlayArenaUnitWav), from the arena's listener.
        /// <summary>0 off, 1 low (the default), 2 normal: the sounds of units other than the player (monsters, other players' actions).</summary>
        public static int MonsterSounds = 1;
        public static readonly string[] MonsterSoundsNames = { "off", "low", "normal" };
        public const float SameWavGap = 0.16f;
        /// <summary>Wave 28 (item 51): longer gap and voice cap for identical monster/unit wavs (e.g. wolf death howls) so packs do not stack.</summary>
        public const float UnitSameWavGap = 0.65f;
        /// <summary>Longer cooldown and normalization target for loud enemy death sounds (especially wolves) in Horde mode.</summary>
        public const float ArenaDeathSameWavGap = 1.65f;
        public const float WolfSameWavGap = 2.8f;
        public const float ArenaAnyDeathGap = 0.08f;
        public const float TargetUnitRms = 0.085f;
        public const int MaxSameUnitVoices = 2;
        public const float OverlapUnitVoiceGain = 0.5f;
        public const int UnitBudgetLow = 6, UnitBudgetNormal = 12;
        public const float UnitFullCells = 3f, UnitHearingLowCells = 10f, UnitHearingNormalCells = 14f;
        public int UnitBudget => MonsterSounds >= 2 ? UnitBudgetNormal : UnitBudgetLow;
        public float UnitHearingCells => MonsterSounds >= 2 ? UnitHearingNormalCells : UnitHearingLowCells;
        public int UnitPlayed { get; private set; }
        public int UnitDroppedBudget { get; private set; }
        public int UnitDroppedFar { get; private set; }
        public int UnitDroppedOff { get; private set; }
        public int DroppedSameWav { get; private set; }
        /// <summary>Unit sounds started in the current second (the smoke reads the budget).</summary>
        public int UnitInWindow => Time.time > _unitWindow ? 0 : _unitInWindow;
        private float _unitWindow; private int _unitInWindow;
        private float _lastArenaDeathAt = -10f;
        private readonly Dictionary<string, float> _lastPlayed = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private struct UnitRequest { public string Name; public float Dist, Vol, Pan; public AudioClip Clip; public bool Arena; }
        private readonly List<UnitRequest> _unitQueue = new List<UnitRequest>();

        private static bool IsWolfUnitWav(string name)
        {
            return !string.IsNullOrEmpty(name) && name.IndexOf("wolf", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsLoudOrDeathUnitWav(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("die", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("dead", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("wolf", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Normalizes unit/death clips whose decoded RMS loudness exceeds <see cref="TargetUnitRms"/> so overly loud monster death cries (e.g. wolves) match quieter monsters.</summary>
        public static float NormalizedClipGain(AudioClip clip, string name, bool arena)
        {
            float g = 1f;
            if (clip != null && _clipRms.TryGetValue(clip, out float rms) && rms > TargetUnitRms)
                g = Mathf.Clamp(TargetUnitRms / rms, 0.35f, 1f);
            if (!string.IsNullOrEmpty(name))
            {
                if (IsWolfUnitWav(name)) g *= arena ? 0.68f : 0.78f;
                else if (IsLoudOrDeathUnitWav(name) && arena) g *= 0.72f;
            }
            return g;
        }

        /// <summary>A unit's ACT sound in the world: the player's own actions play as before (PlayWav); another unit's are queued for this frame's
        /// LateUpdate (nearest first, within the budget), attenuated tightly, under the Monster sounds setting. True = accepted into the queue.</summary>
        public bool PlayUnitWav(string name, Vector2 cell, bool mine)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (mine) { PlayWav(name, cell); return true; }
            if (MonsterSounds <= 0) { UnitDroppedOff++; return false; }
            var l = ListenerCell?.Invoke();
            float d = l.HasValue ? Vector2.Distance(l.Value, cell) : 0f;
            return QueueUnit(name, cell, d, false);
        }

        /// <summary>The same for a monster of the Horde arena, heard from the arena's listener (PlayArenaWav's panning and budget rules too).</summary>
        public bool PlayArenaUnitWav(string name, Vector2 cell)
        {
            if (string.IsNullOrEmpty(name) || ArenaListenerCell == null) return false;
            if (MonsterSounds <= 0) { UnitDroppedOff++; return false; }
            var l = ArenaListenerCell();
            float d = l.HasValue ? Vector2.Distance(l.Value, cell) : 0f;
            return QueueUnit(name, cell, d, true);
        }

        private int ActiveVoicesOf(AudioClip clip, bool arena, bool duckExisting = false)
        {
            if (clip == null) return 0;
            var pool = arena ? _arenaPool : _pool;
            int n = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                var s = pool[i];
                if (s != null && s.isPlaying && s.clip == clip)
                {
                    n++;
                    if (duckExisting) s.volume *= 0.75f;
                }
            }
            return n;
        }

        private bool QueueUnit(string name, Vector2 cell, float d, bool arena)
        {
            if (EffectsVolume <= 0.001f) { ArenaSilent++; return false; }
            float hearing = UnitHearingCells;
            if (d > hearing) { UnitDroppedFar++; return false; }
            bool isWolf = IsWolfUnitWav(name);
            bool loudDeath = IsLoudOrDeathUnitWav(name);
            float gap = isWolf ? WolfSameWavGap : (arena && loudDeath ? ArenaDeathSameWavGap : UnitSameWavGap);
            if (_lastPlayed.TryGetValue(name, out float last) && Time.time - last < gap) { DroppedSameWav++; return false; }
            if (Time.time > _unitWindow) { _unitWindow = Time.time + 1f; _unitInWindow = 0; }
            if (_unitInWindow + _unitQueue.Count >= UnitBudget * 2) { UnitDroppedBudget++; return false; } // (the queue itself is bounded: no pile-up in one frame)
            var clip = Clip(name); if (clip == null) return false;
            int maxVoices = (isWolf || (arena && loudDeath)) ? 1 : MaxSameUnitVoices;
            if (ActiveVoicesOf(clip, arena) >= maxVoices) { DroppedSameWav++; return false; }
            float vol = d <= UnitFullCells ? 1f : Mathf.Clamp01(1f - (d - UnitFullCells) / Mathf.Max(0.1f, hearing - UnitFullCells)); // (wave 27, wfix: the effects volume is applied when it starts, LateUpdate)
            if (arena) vol *= 0.85f;
            vol *= NormalizedClipGain(clip, name, arena);
            float pan = 0f; var cam = Camera.main; var root = arena ? ArenaRoot : MapRoot;
            if (cam != null && root != null)
            {
                var world = root.TransformPoint(arena ? new Vector3(cell.x, 0, cell.y) : new Vector3(cell.x + 0.5f, 0, cell.y + 0.5f));
                var dir = world - cam.transform.position; dir.y = 0;
                if (dir.sqrMagnitude > 1e-8f) pan = Mathf.Clamp(Vector3.Dot(cam.transform.right, dir.normalized), -1f, 1f) * 0.7f;
            }
            _unitQueue.Add(new UnitRequest { Name = name, Dist = d, Vol = vol, Pan = pan, Clip = clip, Arena = arena });
            return true;
        }

        /// <summary>This frame's unit sounds: the nearest first, within the second's budget; the rest dropped.</summary>
        private void LateUpdate()
        {
            MuffleTick(); // client wave 23 (w23tree), item 24: after everybody has set the sources' volumes this frame (the end of this file)
            if (_unitQueue.Count == 0) return;
            if (Time.time > _unitWindow) { _unitWindow = Time.time + 1f; _unitInWindow = 0; }
            _unitQueue.Sort((a, b) => a.Dist.CompareTo(b.Dist));
            foreach (var r in _unitQueue)
            {
                // wave 27 (wfix): the effects volume as it is NOW. A sound queued earlier in this frame started at the old volume after the
                // volume was set to 0 (w9feel: 1 monster sound in the first frame at volume 0)
                if (EffectsVolume <= 0.001f) { ArenaSilent++; continue; }
                if (_unitInWindow >= UnitBudget) { UnitDroppedBudget++; continue; }
                bool isWolf = IsWolfUnitWav(r.Name);
                bool loudDeath = IsLoudOrDeathUnitWav(r.Name);
                float gap = isWolf ? WolfSameWavGap : (r.Arena && loudDeath ? ArenaDeathSameWavGap : UnitSameWavGap);
                if (_lastPlayed.TryGetValue(r.Name, out float last) && Time.time - last < gap) { DroppedSameWav++; continue; }
                int maxVoices = (isWolf || (r.Arena && loudDeath)) ? 1 : MaxSameUnitVoices;
                int active = ActiveVoicesOf(r.Clip, r.Arena, true);
                if (active >= maxVoices) { DroppedSameWav++; continue; }
                float overlapGain = active > 0 ? OverlapUnitVoiceGain : 1f;
                _lastPlayed[r.Name] = Time.time;
                if (r.Arena && loudDeath) _lastArenaDeathAt = Time.time;
                _unitInWindow++; UnitPlayed++;
                var src = r.Arena ? FreeArenaSource() : FreeSource();
                src.clip = r.Clip; src.volume = r.Vol * overlapGain * EffectsVolume; src.panStereo = r.Pan; src.pitch = 1f; src.Play();
            }
            _unitQueue.Clear();
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void PlayUi() { var src = FreeSource(); src.clip = _click; src.volume = 0.35f * EffectsVolume; src.panStereo = 0; src.Play(); }
        /// <summary>Client wave 20 (w20tut), point 43: a clip made in code (the tutorial's check mark: UI/CheckMarkFx.cs), unplaced, under the
        /// effects volume. Clips played so far (log, checks).</summary>
        public void PlayClip(AudioClip clip, float volume)
        {
            if (clip == null) return;
            var src = FreeSource(); src.clip = clip; src.volume = volume * EffectsVolume; src.panStereo = 0; src.pitch = 1f; src.Play(); ClipsPlayed++;
        }
        public int ClipsPlayed { get; private set; }

        public AudioClip Clip(string name)
        {
            if (_clips.TryGetValue(name, out var c)) return c;
            if (name != null && name.StartsWith("synth:", StringComparison.OrdinalIgnoreCase))
            {
                c = UI.TreeSynthAudio.Make(name);
                if (c != null) _clips[name] = c;
                return c;
            }
            string path = name.StartsWith("data\\", StringComparison.OrdinalIgnoreCase) ? name : "data\\wav\\" + name;
            var bytes = _fs?.Read(RoPath.Normalize(path));
            c = bytes != null ? DecodeWav(bytes, name) : null;
            if (c == null && bytes == null) Debug.Log("[Audio] missing wav " + path);
            _clips[name] = c;
            return c;
        }

        private bool Attenuate(Vector2 cell, float range, ref float vol, ref float pan)
        {
            var l = ListenerCell?.Invoke();
            if (!l.HasValue) return true;
            float d = Vector2.Distance(l.Value, cell);
            if (d > range) return false;
            vol *= Mathf.Clamp01(1f - d / range) * 0.85f + 0.15f;
            var cam = Camera.main;
            if (cam != null && MapRoot != null)
            {
                var world = MapRoot.TransformPoint(new Vector3(cell.x + 0.5f, 0, cell.y + 0.5f));
                var dir = world - cam.transform.position; dir.y = 0;
                if (dir.sqrMagnitude > 1e-8f) pan = Mathf.Clamp(Vector3.Dot(cam.transform.right, dir.normalized), -1f, 1f) * 0.7f;
            }
            return true;
        }

        private AudioSource FreeSource()
        {
            foreach (var s in _pool) if (!s.isPlaying) return s;
            if (_pool.Count >= 24) { var oldest = _pool[0]; _pool.RemoveAt(0); _pool.Add(oldest); oldest.Stop(); return oldest; }
            var n = NewVoice(); // (client wave 23, item 24: a voice is an object of its own, with its own low-pass)
            _pool.Add(n); return n;
        }

        // ------------------------------------------------------------------ the arena's listener (client wave 9, h9feel)
        // The Horde arena is a second map on the table while the RO world waits 5 km under it: client wave 8 silenced the world's placed
        // sounds by moving the one listener out of hearing (LokiGame.W8Horde.cs). The arena hears through its OWN listener (the run
        // character's predicted cell, panned against the arena's root), with a per-second budget and its own voices, so that a horde of
        // hits never starves the world's pool or the UI, and the same wav is not stacked within a few milliseconds. Every arena sound is
        // under Settings → Sound's effects volume (EffectsVolume), like every other effect.
        /// <summary>The arena listener's cell (null = no arena: PlayArenaWav plays nothing) and the arena's map root (panning).</summary>
        public Func<Vector2?> ArenaListenerCell;
        public Transform ArenaRoot;
        /// <summary>How far the arena's listener hears, in cells (Loki's own number; the table shows ~30 cells across).</summary>
        public float ArenaHearingCells = 24f;
        /// <summary>Arena sounds a second at most, and voices the arena may hold at once (Loki's own bounds).</summary>
        public int ArenaBudgetPerSecond = 28, ArenaVoices = 14;
        /// <summary>The same wav is not started again within this many seconds (a swarm of identical hits becomes one louder hit).</summary>
        public float ArenaSameWavGap = 0.045f;
        public int ArenaPlayed { get; private set; }
        public int ArenaDroppedBudget { get; private set; }
        public int ArenaDroppedSame { get; private set; }
        public int ArenaSilent { get; private set; }
        public int ArenaOutOfHearing { get; private set; }
        /// <summary>Arena sounds started in the current second (the smoke reads the budget).</summary>
        public int ArenaInWindow => Time.time > _arenaWindow ? 0 : _arenaInWindow;
        private float _arenaWindow; private int _arenaInWindow;
        private readonly Dictionary<string, float> _arenaLast = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private readonly List<AudioSource> _arenaPool = new List<AudioSource>();

        /// <summary>True when data\wav\&lt;name&gt; exists in the player's install or is a synthesized tree sound ('synth:*').</summary>
        public bool HasWav(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name.StartsWith("synth:", StringComparison.OrdinalIgnoreCase) || _clips.ContainsKey(name)) return true;
            if (_fs == null) return false;
            string path = name.StartsWith("data\\", StringComparison.OrdinalIgnoreCase) ? name : "data\\wav\\" + name;
            return _fs.Exists(RoPath.Normalize(path));
        }

        /// <summary>Plays data\wav\&lt;name&gt; as heard from the arena's listener; cell = where it happens in the ARENA's cells (null = unplaced).
        /// False when it was not started (no arena, effects volume 0, out of hearing, the budget or the same-wav gap).</summary>
        public bool PlayArenaWav(string name, Vector2? cell = null, float volume = 1f, float pitch = 1f)
        {
            if (string.IsNullOrEmpty(name) || ArenaListenerCell == null) return false;
            if (EffectsVolume <= 0.001f || volume <= 0.001f) { ArenaSilent++; return false; }
            if (Time.time > _arenaWindow) { _arenaWindow = Time.time + 1f; _arenaInWindow = 0; }
            if (_arenaInWindow >= ArenaBudgetPerSecond) { ArenaDroppedBudget++; return false; }
            if (_arenaLast.TryGetValue(name, out float last) && Time.time - last < ArenaSameWavGap) { ArenaDroppedSame++; return false; }
            var clip = Clip(name);
            if (clip == null) return false;
            float vol = volume * EffectsVolume, pan = 0f;
            if (cell.HasValue)
            {
                var l = ArenaListenerCell();
                if (l.HasValue)
                {
                    float d = Vector2.Distance(l.Value, cell.Value);
                    if (d > ArenaHearingCells) { ArenaOutOfHearing++; return false; }
                    vol *= Mathf.Clamp01(1f - d / ArenaHearingCells) * 0.8f + 0.2f;
                    var cam = Camera.main;
                    if (cam != null && ArenaRoot != null)
                    {
                        var world = ArenaRoot.TransformPoint(new Vector3(cell.Value.x, 0, cell.Value.y));
                        var dir = world - cam.transform.position; dir.y = 0;
                        if (dir.sqrMagnitude > 1e-8f) pan = Mathf.Clamp(Vector3.Dot(cam.transform.right, dir.normalized), -1f, 1f) * 0.7f;
                    }
                }
            }
            _arenaInWindow++; ArenaPlayed++; _arenaLast[name] = Time.time;
            var src = FreeArenaSource();
            src.clip = clip; src.volume = vol; src.panStereo = pan; src.pitch = Mathf.Clamp(pitch, 0.5f, 2f); src.Play();
            return true;
        }

        /// <summary>The arena's own voices: a free one, else the one that has played longest (stolen: a horde never waits for silence).</summary>
        private AudioSource FreeArenaSource()
        {
            foreach (var s in _arenaPool) if (!s.isPlaying) return s;
            if (_arenaPool.Count >= Math.Max(1, ArenaVoices)) { var oldest = _arenaPool[0]; _arenaPool.RemoveAt(0); _arenaPool.Add(oldest); oldest.Stop(); return oldest; }
            var n = NewVoice(); // (client wave 23, item 24)
            _arenaPool.Add(n); return n;
        }

        /// <summary>The arena is gone: its voices stop, its listener is forgotten.</summary>
        public void ClearArena()
        {
            ArenaListenerCell = null; ArenaRoot = null;
            foreach (var s in _arenaPool) if (s != null && s.isPlaying) s.Stop();
            _arenaLast.Clear();
        }

        // ------------------------------------------------------------------ map ambience (RSW sound objects)
        // Client wave 23 (w23audio), the design's answer 42 (2026-10-11): "On the initial map that the player arrives in
        // inside of a ship, remove the wood creaking noises that play on the ambiance." A new character starts on iz_int or one of its
        // copies iz_int01-04 (rAthena conf/char_athena.conf:115 start_point). Read from the player's data.grf (2026-10-11): each of the
        // five maps' RSW holds exactly three sound sources, se_noise02.wav twice and se_noise03.wav once (volume 0.90, range 100 = 20
        // cells, cycle 4 s; the wavs are 5.29 s and 5.48 s long), and 15 other maps use one or both files (treasure01 / 02, yuno,
        // pay_dun03, gl_prison ...). Which of the two creaks cannot be read from the data, so BOTH were muted at first, on the ship
        // only, by this ONE table: a pattern for the map's name and the files that never start there.
        // the design's answer afterwards (2026-10-11): "keep the other room's sound": only se_noise02.wav is muted (its source at 20.4 27.8
        // is 3 cells from the start point 18 26, the only one within hearing there); se_noise03.wav (54.6 29.5, the other room)
        // plays again. The start room then has no placed sound; the other room has its own.
        // client wave 25 (w25misc), item 12 (design note, 2026-10-12: "On the ship lower the volume of the creaking ambiance both inside and
        // outside"): the mute became a GAIN per file. Read again from the player's data.grf (2026-10-14): iz_int and iz_int01-04 hold three
        // sources each, se_noise02.wav in the start cabin (20.4 27.8, 3 cells from the start point) and in the hold (58.5 43.7), se_noise03.wav
        // in the hold (54.6 29.5; the wounded swordsman's room, whose stairs lead out); all three now play at ShipCreakGain (the cabin's
        // was muted, the hold's se_noise03.wav was at full volume). Outside, the island the ship lands on (int_land, int_land01-04: rAthena
        // npc/re/warps/cities/izlude.txt #ship_out) has NO placed sound (0 sound objects in its five RSW files): the creak heard there was the
        // ship's own, started before the warp and left playing (a voice ran its 5.5 s on after the map changed): an old map's ambient voices
        // now fade out when another map's ambience is set (SetMapAmbience, AmbientLeaveSeconds).
        /// <summary>The ship's creaking, muted inside and outside the starting ship (design note: remove/mute the creaking noises inside and outside).</summary>
        public const float ShipCreakGain = 0f;
        /// <summary>Ambient files played at a gain on some maps: a regular expression for the map's whole name (case ignored), the wav names,
        /// the gain (0 = never started). Every other map and file: 1.</summary>
        public static readonly (string Maps, string[] Files, float Gain)[] AmbientGains =
        {
            ("(?:.*@)?(?:iz_int|int_land)(0[1-4])?", new[] { "se_noise02.wav", "se_noise03.wav" }, ShipCreakGain),
        };
        /// <summary>The gain <see cref="AmbientGains"/> gives this wav on this map (1: not listed).</summary>
        public static float AmbientGain(string map, string file)
        {
            if (string.IsNullOrEmpty(map) || string.IsNullOrEmpty(file)) return 1f;
            foreach (var m in AmbientGains)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(map, "^(?:" + m.Maps + ")$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)) continue;
                foreach (var f in m.Files) if (string.Equals(f, file.Trim(), StringComparison.OrdinalIgnoreCase)) return Mathf.Clamp01(m.Gain);
            }
            return 1f;
        }
        /// <summary>True when <see cref="AmbientGains"/> keeps this wav from ever starting on this map (a gain of 0).</summary>
        public static bool AmbientMuted(string map, string file) => AmbientGain(map, file) <= 0f;
        /// <summary>Client wave 25 (w25misc), item 12: an old map's ambient voices fade out over this many seconds when another map's
        /// ambience is set (FLAGGED).</summary>
        public const float AmbientLeaveSeconds = 0.4f;
        /// <summary>Sources of the map set that play at a gain under 1 (not muted); the last ambient voice started: its wav, the volume it was
        /// given and the gain in it; voices of an old map faded out at a map change (log, checks).</summary>
        public int AmbientLoweredSources { get; private set; }
        public string AmbientLastFile { get; private set; } = "";
        public float AmbientLastVolume { get; private set; }
        public float AmbientLastGain { get; private set; } = 1f;
        public int AmbientVoicesLeft { get; private set; }
        /// <summary>Voices of an old map still fading out now (checks).</summary>
        public int AmbientLeaving => _ambientLeaving.Count;
        private readonly List<(AudioSource Src, AudioClip Clip, float From, float At)> _ambientLeaving = new List<(AudioSource, AudioClip, float, float)>();
        private readonly List<(AudioSource Src, AudioClip Clip)> _ambientVoices = new List<(AudioSource, AudioClip)>();
        /// <summary>The map whose ambience is set; its sources, how many of them the table mutes; ambient sounds started since the map was
        /// set, and starts of muted sources that were within hearing and so were kept from playing (log, checks).</summary>
        public string AmbientMap { get; private set; } = "";
        public int AmbientSources => _ambient.Count;
        public int AmbientMutedSources { get; private set; }
        public int AmbientStarted { get; private set; }
        public int AmbientSuppressed { get; private set; }
        /// <summary>Ambient starts of one wav since the map was set.</summary>
        public int AmbientStartedOf(string file) => file != null && _ambientStarts.TryGetValue(file, out int n) ? n : 0;
        private readonly Dictionary<string, int> _ambientStarts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        /// <summary>A voice of the world's pool is playing this wav right now (a decoded clip is named as it was asked for: Clip → DecodeWav).</summary>
        public bool Sounding(string wav)
        {
            foreach (var s in _pool) if (s != null && s.isPlaying && s.clip != null && string.Equals(s.clip.name, wav, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        /// <summary>The map's sources: the wav, the cell it sounds from, how far it is heard (cells), muted by the table.</summary>
        public IEnumerable<(string File, Vector2 Cell, float Range, bool Muted, float Gain)> AmbientSourceList
        {
            get { foreach (var a in _ambient) yield return (a.File, new Vector2(a.Pos.x - 0.5f, a.Pos.z - 0.5f), a.Range, a.Muted, a.Gain); }
        }

        /// <summary>The map's placed sounds (<paramref name="map"/>: the name of the map the RSW belongs to, for <see cref="AmbientGains"/>).</summary>
        public void SetMapAmbience(RswFile rsw, GndFile gnd, string map = null)
        {
            // client wave 25 (w25misc), item 12: another map: the old map's ambient voices still sounding fade out (AmbientLeaveSeconds)
            if (!string.Equals(map ?? "", AmbientMap, StringComparison.OrdinalIgnoreCase))
                foreach (var v in _ambientVoices) if (v.Src != null && v.Src.isPlaying && v.Src.clip == v.Clip) { _ambientLeaving.Add((v.Src, v.Clip, v.Src.volume, Time.unscaledTime)); AmbientVoicesLeft++; }
            _ambientVoices.Clear();
            _ambient.Clear(); _ambientStarts.Clear(); AmbientMap = map ?? ""; AmbientMutedSources = 0; AmbientLoweredSources = 0; AmbientStarted = 0; AmbientSuppressed = 0;
            if (rsw == null) return;
            var centre = new Vector3(gnd.Width, 0, gnd.Height);
            foreach (var s in rsw.Sounds)
            {
                var p = RoSpace.ToLoki(s.Position) + centre;
                float gain = AmbientGain(map, s.File); bool muted = gain <= 0f; if (muted) AmbientMutedSources++; else if (gain < 1f) AmbientLoweredSources++;
                _ambient.Add(new Ambient { Pos = p, Range = Mathf.Max(1f, s.Range * RoSpace.Scale), Volume = Mathf.Clamp01(s.Volume <= 0 ? 1f : s.Volume), Cycle = s.Cycle, File = s.File, Next = 0, Muted = muted, Gain = gain });
            }
            Debug.Log($"[Audio] {_ambient.Count} ambient sound source(s)" + (AmbientMutedSources > 0 ? $", {AmbientMutedSources} muted on {map}" : "") + (AmbientLoweredSources > 0 ? $", {AmbientLoweredSources} lowered on {map}" : "") + (AmbientMutedSources + AmbientLoweredSources > 0 ? " (Audio/LokiAudio.cs AmbientGains)" : ""));
        }

        private void Update()
        {
            // client wave 23 (w23audio): the tutorial's quiet music: the gain glides, the player's music volume still scales it
            float gainTo = MusicQuiet != null && MusicQuiet() ? TutorialMusicGain : 1f;
            if (MusicGain != gainTo) MusicGain = Mathf.MoveTowards(MusicGain, gainTo, (1f - TutorialMusicGain) / MusicFadeSeconds * Mathf.Min(Time.unscaledDeltaTime, MusicFadeMaxStep));
            // client wave 24 (w24): the login screen's track gives way to the world's: out, then what follows in
            float fadeStep = Mathf.Min(Time.unscaledDeltaTime, MusicFadeMaxStep) / LoginFadeSeconds;
            if (_fadeOut)
            {
                MusicFade = Mathf.MoveTowards(MusicFade, 0f, fadeStep);
                if (MusicFade <= 0f)
                {
                    if (_nextClip != null) StartClip(_nextClip, _nextFile, true);
                    else { _fadeOut = false; _bgm.Stop(); _playingLogin = false; _playingFile = ""; }
                }
            }
            else if (MusicFade < 1f && (_bgm.isPlaying || !_fadeInNext)) MusicFade = Mathf.MoveTowards(MusicFade, 1f, fadeStep); // (in the silence before the world's track it waits at 0)
            if (HordeBgmActive && !_bgmLoading && !string.IsNullOrEmpty(_hordeBgmFile) && _bgm != null && !_bgm.isPlaying)
            {
                if (_bgm.clip != null && string.Equals(_playingFile, _hordeBgmFile, StringComparison.OrdinalIgnoreCase))
                {
                    MusicFade = 1f;
                    _bgm.volume = MusicVolume * MusicGain * MusicFade;
                    _bgm.Play();
                }
                else if (File.Exists(_hordeBgmFile))
                {
                    _bgmFile = null;
                    PlayBgmPath(_hordeBgmFile);
                }
            }
            if (_bgm != null) _bgm.volume = MusicVolume * MusicGain * MusicFade;
            // client wave 25 (w25misc), item 12: an old map's ambient voices fade out (a voice taken for another sound meanwhile is left alone)
            for (int i = _ambientLeaving.Count - 1; i >= 0; i--)
            {
                var (src, clip, from, at) = _ambientLeaving[i]; float k = 1f - (Time.unscaledTime - at) / AmbientLeaveSeconds;
                if (src == null || !src.isPlaying || src.clip != clip) { _ambientLeaving.RemoveAt(i); continue; }
                if (k <= 0f) { src.Stop(); _ambientLeaving.RemoveAt(i); } else src.volume = from * k;
            }
            if (_ambient.Count == 0) return;
            float now = Time.time;
            foreach (var a in _ambient)
            {
                if (now < a.Next) continue;
                var cell = new Vector2(a.Pos.x - 0.5f, a.Pos.z - 0.5f);
                float vol = a.Volume * EffectsVolume * 0.8f, pan = 0f;
                if (a.Muted)
                {
                    // client wave 23 (w23audio): a muted source keeps its beat (the wav is not even read: the beat of a source without
                    // a clip, below) and never starts; a start it would have made within hearing is counted
                    a.Next = now + Mathf.Max(a.Cycle > 0 ? a.Cycle : 0f, 5f) + 0.05f;
                    if (Attenuate(cell, a.Range, ref vol, ref pan)) AmbientSuppressed++;
                    continue;
                }
                var clip = Clip(a.File);
                a.Next = now + Mathf.Max(a.Cycle > 0 ? a.Cycle : 0f, clip != null ? clip.length : 5f) + 0.05f;
                if (clip == null || !Attenuate(cell, a.Range, ref vol, ref pan)) continue;
                vol *= a.Gain; // client wave 25 (w25misc), item 12: the ship's creaking lowered (AmbientGains)
                var src = FreeSource(); src.clip = clip; src.volume = vol; src.panStereo = pan; src.Play();
                AmbientStarted++; _ambientStarts[a.File] = AmbientStartedOf(a.File) + 1;
                AmbientLastFile = a.File; AmbientLastVolume = vol; AmbientLastGain = a.Gain; _ambientVoices.RemoveAll(v => v.Src == null || !v.Src.isPlaying || v.Src.clip != v.Clip); _ambientVoices.Add((src, clip)); // (client wave 25: the checks, the fade at a map change)
            }
        }

        // ------------------------------------------------------------------ WAV decoding
        /// <summary>RIFF WAVE, PCM (format 1), 8-bit unsigned or 16-bit signed, any channel count.</summary>
        public static AudioClip DecodeWav(byte[] b, string name)
        {
            try
            {
                if (b.Length < 44 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F' || b[8] != 'W' || b[9] != 'A' || b[10] != 'V' || b[11] != 'E') return null;
                int p = 12, fmt = 0, ch = 0, rate = 0, bits = 0, dataOff = -1, dataLen = 0;
                while (p + 8 <= b.Length)
                {
                    int len = BitConverter.ToInt32(b, p + 4);
                    if (b[p] == 'f' && b[p + 1] == 'm' && b[p + 2] == 't') { fmt = BitConverter.ToUInt16(b, p + 8); ch = BitConverter.ToUInt16(b, p + 10); rate = BitConverter.ToInt32(b, p + 12); bits = BitConverter.ToUInt16(b, p + 22); }
                    else if (b[p] == 'd' && b[p + 1] == 'a' && b[p + 2] == 't' && b[p + 3] == 'a') { dataOff = p + 8; dataLen = Math.Min(len, b.Length - dataOff); break; }
                    if (len < 0) break;
                    p += 8 + len + (len & 1);
                }
                if (fmt != 1 || ch <= 0 || rate <= 0 || dataOff < 0 || (bits != 8 && bits != 16)) { Debug.Log($"[Audio] unsupported wav {name}: fmt {fmt} bits {bits}"); return null; }
                int bytesPer = bits / 8, frames = dataLen / (bytesPer * ch);
                if (frames <= 0) return null;
                var samples = new float[frames * ch];
                if (bits == 16) for (int i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(b, dataOff + i * 2) / 32768f;
                else for (int i = 0; i < samples.Length; i++) samples[i] = (b[dataOff + i] - 128) / 128f;
                double sumSq = 0.0;
                int activeSamples = 0;
                for (int i = 0; i < samples.Length; i++)
                {
                    float s = samples[i];
                    if (Mathf.Abs(s) > 0.005f) { sumSq += s * s; activeSamples++; }
                }
                float rms = activeSamples > 0 ? Mathf.Sqrt((float)(sumSq / activeSamples)) : 0f;
                var clip = AudioClip.Create(name, frames, ch, rate, false);
                clip.SetData(samples, 0);
                _clipRms[clip] = rms;
                return clip;
            }
            catch (Exception e) { Debug.LogWarning($"[Audio] wav decode failed {name}: {e.Message}"); return null; }
        }

        // ------------------------------------------------------------------ client wave 23 (w23tree), item 24: the skill trees muffle the other sounds
        // design note (2026-10-11): "(Toggleable on the settings) While the skill trees are open, all other sound sources become muffled. This is
        // to accentuate the spawning animation's sound effect, and to make it easy to hear the sound effects coming from the skill
        // preview animations". While Muffled is asked for (LokiGame.W22Tree.cs: the trees are open and the Setting is On) every sound
        // that is not the trees' own is QUIETER (what its source plays x MuffleGain) and DULLER (a low-pass at MuffleCutoffHz on its
        // source), gliding in and out over MuffleGlide seconds.
        //   HOW   Unity's filter components work on the source of their OWN object, and every source of the pools used to sit on this
        //         one object. So a pool's voice is now a child object with its AudioSource, its AudioLowPassFilter and its MuffleGain
        //         (NewVoice; both off until the muffle). The music's source keeps its place on this object and gets its two here.
        //         Sources of other systems (the rain's ...) are looked for when the muffle begins and once a second while it lasts,
        //         and get theirs on their own object. A FRIEND'S VOICE (Voice/VoiceSpeaker.cs) is never touched (design note, 2026-10-12:
        //         "Don't muffle voices.").
        //   THE TREES' OWN SOUNDS (a tree's rise, a node's grow, a preview's skill sound) play through voices of their own
        //         (PlayTreeWav), which are never touched. The UI's tick (PlayUi) is not muffled either (FLAGGED: every menu's tick,
        //         not only the trees').
        //   THE GAIN is a FACTOR OF ITS OWN (MuffleFactor: 1 to MuffleGain) and NOBODY'S VOLUME LINE IS TOUCHED: it is not written
        //         into a source's volume at all. MuffleGain (the end of this file) is a filter beside the low-pass: it multiplies the
        //         SAMPLES its source plays, on the audio thread, gliding inside a block. So what is heard of the music is
        //         (its source's volume: MusicVolume, times whatever its own lines in Init / LoadBgm / Update multiply in) x MuffleFactor,
        //         and of an effect (the volume its player gave it) x MuffleFactor. (The first build of this wrote the factor into
        //         the volumes after everybody else each frame: measured, the music's source was at its full volume whenever it was
        //         read between Update and LateUpdate, and so would the audio thread have heard it.)
        public const float MuffleGain = 0.55f, MuffleCutoffHz = 1800f, MuffleGlide = 0.3f; // (client wave 24, w24, item 13: design note, 2026-10-12: "The sound pass on the skill tree is a little bit too aggressive. It needs to be a little bit less aggressive.": until then 0.35 and 900 Hz; FLAGGED: the orchestrator's numbers)
        /// <summary>Friends' voices are muffled too. False: design note, 2026-10-12: "Don't muffle voices."</summary>
        public const bool MuffleVoices = false;
        /// <summary>The muffle is asked for now; how far it is in (0: none, 1: whole); the factor on what the others play now.</summary>
        public bool Muffled { get; set; }
        public float Muffle { get; private set; }
        public float MuffleFactor => Mathf.Lerp(1f, MuffleGain, Muffle);
        /// <summary>Sounds of the trees' own started so far, the last one's name (smoke, log).</summary>
        public int TreeWavsPlayed { get; private set; }
        public string LastTreeWav { get; private set; } = "";

        private sealed class Other { public AudioSource Src; public AudioLowPassFilter Lp; public MuffleGain Gain; public bool Pool; }
        private readonly List<Other> _others = new List<Other>();
        private readonly List<AudioSource> _treeVoices = new List<AudioSource>();
        private float _othersLooked = -10f; private bool _bgmTracked, _muffleWasOn;

        /// <summary>A voice of a pool: an object of its own with its source, its low-pass and its gain.</summary>
        private AudioSource NewVoice()
        {
            var go = new GameObject("voice"); go.transform.SetParent(transform, false);
            var n = go.AddComponent<AudioSource>(); n.playOnAwake = false; n.spatialBlend = 0f;
            Track(n, true);
            return n;
        }
        /// <summary>A source the muffle works on from now on: its low-pass and its gain (made if it has none) stay off until the muffle.</summary>
        private void Track(AudioSource src, bool pool)
        {
            foreach (var o in _others) if (o.Src == src) return;
            var lp = src.GetComponent<AudioLowPassFilter>(); if (lp == null) { lp = src.gameObject.AddComponent<AudioLowPassFilter>(); lp.cutoffFrequency = 22000f; lp.enabled = false; }
            var g = src.GetComponent<MuffleGain>(); if (g == null) { g = src.gameObject.AddComponent<MuffleGain>(); g.enabled = false; }
            _others.Add(new Other { Src = src, Lp = lp, Gain = g, Pool = pool });
        }

        /// <summary>A sound of the SKILL TREES' OWN: data\wav\&lt;name&gt; under the effects volume, panned (-1 left, 1 right) or 3D-placed at
        /// <paramref name="worldPos"/>, through the trees' own voices: never muffled. False: the install has no such file.</summary>
        public bool PlayTreeWav(string name, float volume = 1f, float pan = 0f, Vector3? worldPos = null)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var clip = Clip(name); if (clip == null) return false;
            AudioSource src = null;
            foreach (var s in _treeVoices) if (s != null && !s.isPlaying) { src = s; break; }
            if (src == null)
            {
                if (_treeVoices.Count >= 6) { src = _treeVoices[0]; _treeVoices.RemoveAt(0); _treeVoices.Add(src); src.Stop(); }
                else { var go = new GameObject("tree voice"); go.transform.SetParent(transform, false); src = go.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 0f; _treeVoices.Add(src); }
            }
            if (worldPos.HasValue)
            {
                src.transform.position = worldPos.Value;
                src.spatialBlend = 1f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 0.25f;
                src.maxDistance = 6f;
            }
            else
            {
                src.spatialBlend = 0f;
            }
            src.clip = clip; src.volume = volume * EffectsVolume; src.panStereo = Mathf.Clamp(pan, -1f, 1f); src.pitch = 1f; src.Play();
            TreeWavsPlayed++; LastTreeWav = name;
            return true;
        }

        /// <summary>Stops any currently playing skill preview sounds (3D-placed tree voices) immediately on unhover, close, or deselection.</summary>
        public void StopPreviewWavs()
        {
            foreach (var s in _treeVoices) if (s != null && s.isPlaying && s.spatialBlend > 0.5f) s.Stop();
        }

        private void MuffleTick()
        {
            float target = Muffled ? 1f : 0f;
            if (Muffle != target) Muffle = Mathf.MoveTowards(Muffle, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, MuffleGlide));
            bool on = Muffle > 0.0005f;
            if (!on && !_muffleWasOn) return; // (off and everything given back: nothing is touched)
            if (!_bgmTracked && _bgm != null) { Track(_bgm, false); _bgmTracked = true; }
            if (on && Time.unscaledTime - _othersLooked > 1f)
            {
                // every other system's source that sounds now (the rain's ...): not the trees' voices, not a friend's voice
                _othersLooked = Time.unscaledTime;
                foreach (var s in FindObjectsOfType<AudioSource>())
                {
                    if (s == null || _treeVoices.Contains(s)) continue;
                    if (!MuffleVoices && s.GetComponent<Loki.Voice.VoiceSpeaker>() != null) continue;
                    Track(s, false);
                }
            }
            float gain = MuffleFactor, cutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(22000f), Mathf.Log(MuffleCutoffHz), Muffle));
            for (int i = _others.Count - 1; i >= 0; i--)
            {
                var o = _others[i];
                if (o.Src == null) { _others.RemoveAt(i); continue; }
                bool dull = on && o.Src.clip != _click; // (the UI's tick stays clear)
                if (o.Gain != null)
                {
                    o.Gain.Gain = dull ? gain : 1f;
                    // a source that is silent now, or whose gain comes on now, starts AT the factor: a sound never fades into it
                    if (dull && (!o.Gain.enabled || !o.Src.isPlaying)) o.Gain.Snap();
                    if (o.Gain.enabled != dull) o.Gain.enabled = dull;
                }
                if (o.Lp != null) { if (dull) o.Lp.cutoffFrequency = cutoff; if (o.Lp.enabled != dull) o.Lp.enabled = dull; }
            }
            _muffleWasOn = on;
        }


        private static AudioClip MakeClick()
        {
            int rate = 44100, n = rate / 40;
            var s = new float[n];
            for (int i = 0; i < n; i++) { float t = i / (float)rate; s[i] = Mathf.Sin(2 * Mathf.PI * 1800f * t) * Mathf.Exp(-t * 180f) * 0.6f; }
            var c = AudioClip.Create("click", n, 1, rate, false); c.SetData(s, 0); return c;
        }
    }
    /// <summary>Client wave 23 (w23tree), item 24: the muffle's gain. A filter on a source's own object (as Unity's AudioLowPassFilter
    /// beside it): every sample the source plays x <see cref="Gain"/>, on the audio thread; a new factor is reached by a straight glide
    /// inside ONE audio block, so a step is never heard. The source's own volume is not read and not written: whoever sets it keeps it.
    /// Off (the component disabled): Unity leaves it out of the chain. Made and driven by LokiAudio.MuffleTick only.</summary>
    public sealed class MuffleGain : MonoBehaviour
    {
        private float _gain = 1f, _now = 1f;
        /// <summary>The factor asked for (written by the game's thread, read by the audio thread).</summary>
        public float Gain { get => _gain; set => _gain = Mathf.Clamp01(value); }
        /// <summary>Audio blocks worked on so far (the smoke: the filter really stands in its source's chain).</summary>
        public int Blocks { get; private set; }
        /// <summary>The next block starts AT the factor asked for (no glide: a sound that begins muffled).</summary>
        public void Snap() { _now = _gain; }
        private void OnAudioFilterRead(float[] data, int channels) { Work(data, channels); Blocks++; }
        /// <summary>One block: from the factor the last block ended at to the one asked for, a straight line over the block's frames.</summary>
        public void Work(float[] data, int channels)
        {
            float to = _gain, from = _now; int n = data.Length;
            if (from == to) { if (to != 1f) for (int i = 0; i < n; i++) data[i] *= to; return; }
            int frames = channels > 0 ? n / channels : 0;
            if (frames > 0)
            {
                float step = (to - from) / frames; int k = 0;
                for (int f = 0; f < frames; f++) { from += step; for (int c = 0; c < channels; c++) data[k++] *= from; }
            }
            _now = to;
        }
    }
}
