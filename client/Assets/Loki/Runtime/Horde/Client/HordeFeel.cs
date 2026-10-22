using System.Collections.Generic;
using Loki.Effects;
using Loki.Sprites;
using Loki.UI;
using UnityEngine;

namespace Loki.Horde.Client
{
    /// <summary>
    /// Client wave 9 (h9feel): "every hit is seen and heard; nothing hitches". One entry point for the run's feedback, called by
    /// HordeGame (one-line hooks): the events become flashes, numbers, pops, pushes, flights and sounds; the sprites of everything
    /// the run can show are built ahead of their first use.
    ///
    /// Seen (HordeWorldDraw, HordeNumbers): the struck enemy flashes white (80 ms); the damage number is sized by the hit against
    /// the run's typical hit (a running average: a 3× hit is ~1.5× the size, a critical 1.3× more and yellow), coloured by the
    /// weapon's element (RO's element colours, World/Fx.cs), a damage-over-time tick dim; a knockback (the engine's Effect event)
    /// leans the enemy; a death pops with sparks; pickups fly; the boss / elite bar (HordeBossBar) is placed by the HUD.
    ///
    /// Heard (Audio/LokiAudio.cs PlayArenaWav: the arena's own listener, 28 sounds a second at most, 14 voices, the same wav not
    /// twice within 45 ms, all under Settings → Sound's effects volume): a weapon's FIRE and HIT sounds come from its Ragnarok
    /// skill, in this order: the official skilleffectinfolist's waveFileName / targetWaveFileName (Fx/GrfSkillEffects.cs,
    /// read from the player's own GRF), else the wav of the skill's effect / hit effect in the client's effect tables
    /// (Fx/EffectDb.cs, roBrowserLegacy's transcription), else a short neutral sound of the install chosen by the weapon's damage
    /// class, by name, verified to exist in the LATAM GRF's listing on 2026-10-04 (client/tools/clientdata/grf.py list …): melee
    /// attack_short_sword.wav / _hit_sword.wav (the client's own weapon table rows for a dagger, Audio/WeaponSounds.cs, so the
    /// Novice's knife sounds as a knife does in the world), ranged attack_bow1.wav / _hit_arrow.wav, magic effect\ef_hit2.wav.
    /// A monster's own ACT sounds play on its attack and death frames (HordeWorldDraw), as the world plays them. Pickups:
    /// effect\ef_coin.wav (EXP gems, quiet), _heal_effect.wav (heals), effect\itempokjuk.wav (chests), se_ding1.wav (the
    /// magnet), effect\ef_firehit.wav (bombs); a card taken: se_equip_item_01.wav; a boss: its own breath / stand sound when the
    /// install has one (baphomet_breath.wav, ork_warrior_breath.wav), else party_alarm.wav (RO's party alarm), elites the alarm
    /// quieter. The level-up ding (se_ding2.wav) and the job-change fanfare (levelup.wav) stay as configured (HordeGame.cs).
    /// Every file is checked to exist (LokiAudio.HasWav) before it is ever used; a missing one is simply silent, never guessed.
    ///
    /// Not hitching (HordeSprites): at the lobby, every monster of the content (waves, elites, bosses, adds, summons), every
    /// pickup item and my run character's Novice + first-job sprites are queued and built a few milliseconds a frame
    /// (<see cref="Prewarm"/>, <see cref="LateUpdate"/>); at a job choice the offered jobs are queued (<see cref="OnOffered"/>).
    /// All numbers here are Loki's own.
    /// </summary>
    public sealed class HordeFeel
    {
        /// <summary>The sprite pre-build (the smoke switches it off for its "before" measurement).</summary>
        public static bool PrewarmEnabled = true;
        /// <summary>Milliseconds a frame the pre-build may take: in the lobby (nothing else moves) and during a run (a late joiner, a job offer).</summary>
        public const float PrewarmLobbyMs = 8f, PrewarmRunMs = 3.5f;
        /// <summary>Client wave 17 fix (h17fix): during a run, a frame that follows one longer than this (ms: a hitch, such as the arena's
        /// first frames) builds nothing ahead, so the pre-build never makes a hitch longer. Loki's own number: over two frames at 90 Hz.</summary>
        public const float PrewarmHitchMs = 25f;
        public const string SwingMelee = "attack_short_sword.wav", HitMelee = "_hit_sword.wav", SwingRanged = "attack_bow1.wav", HitRanged = "_hit_arrow.wav", HitMagic = "effect\\ef_hit2.wav";
        public const string PickupExp = "effect\\ef_coin.wav", PickupHeal = "_heal_effect.wav", PickupChest = "effect\\itempokjuk.wav", PickupMagnet = "se_ding1.wav", PickupBomb = "effect\\ef_firehit.wav";
        public const string CardSound = "se_equip_item_01.wav", BossSting = "party_alarm.wav";
        /// <summary>Client wave 17 (h17read), the readability proposal's E1-E3 and C5. Every name is in the LATAM install's GRF listing
        /// (client/tools/clientdata/grf.py list, 2026-10-08, 2898 wavs) and is checked at run time (Has) before it is played:
        ///   E1 hurt: RO's own player-hit sound by MY job, as the world plays a hit on a player (Audio/WeaponSounds.JobHit: player_clothes /
        ///      player_wooden_male / player_metal); a big hit (over <see cref="HordeReadability.BigHitShare"/> of my max HP) louder and
        ///      higher, with <see cref="HurtBig"/> (a 0.24 s heavy hit of the client's effect tables) under it;
        ///   E2 an enemy fires: <see cref="EnemyFire"/> (the client's bow swing, quiet by itself) at the shooter; a shot passing close:
        ///      <see cref="ShotWhoosh"/> (RO's arrow flight, 0.34 s);
        ///   E3 an elite's / boss's wind-up begins: <see cref="WindUpLow"/> (RO's cast-begin sound) pitched down;
        ///   C5 standing in an enemy's area: <see cref="WarnTickWav"/> (the shortest crisp sound found, 0.10 s) quiet and high.</summary>
        public const string HurtBig = "_enemy_hit_normal1.wav", EnemyFire = "attack_bow1.wav", ShotWhoosh = "arrow.wav", WindUpLow = "effect\\ef_beginspell.wav", WarnTickWav = "_attack_dagger.wav";
        public int SoundsHurt, SoundsHurtBig, SoundsEnemyFire, SoundsWhoosh, SoundsWindUp, SoundsWarn;
        public string LastHurtSound { get; private set; } = "";
        private readonly Dictionary<int, float> _enemyFireLast = new Dictionary<int, float>();
        private float _whooshLast = -9f, _windUpLast = -9f;

        private readonly HordeGame _g;
        private readonly Dictionary<int, (string fire, string hit)> _weaponSounds = new Dictionary<int, (string, string)>();
        private readonly Dictionary<string, bool> _has = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, float> _fireLast = new Dictionary<int, float>();
        private readonly List<Unit> _warmUnits = new List<Unit>();
        private float _typical = 12f; private int _typicalN;
        private bool _prewarmed;
        // counters for the log and the smoke
        public int SoundsWeapon, SoundsHit, SoundsPickup, SoundsCard, SoundsBoss, SoundsMonster, SoundsEffect, Pushes, Magnets;
        public string LastWeaponSound { get; private set; } = "";
        public string LastBossSound { get; private set; } = "";
        public int JobsQueued { get; private set; }
        public bool Prewarmed => _prewarmed;

        public HordeFeel(HordeGame g) { _g = g; }

        private Audio.LokiAudio Audio => global::Loki.Audio.LokiAudio.Instance;

        private bool Has(string wav)
        {
            if (string.IsNullOrEmpty(wav)) return false;
            if (_has.TryGetValue(wav, out bool h)) return h;
            h = Audio != null && Audio.HasWav(wav);
            if (!h) Debug.Log($"[Horde] sound {wav} is not in the game data: not played");
            _has[wav] = h;
            return h;
        }

        private bool Play(string wav, float x, float y, float volume = 1f, float pitch = 1f) => Has(wav) && Audio.PlayArenaWav(wav, new Vector2(x, y), volume, pitch);

        private float _stormSoundLast;
        private float _lastChestTime = -10f;

        private static bool IsLoudStormWav(string wav)
        {
            if (string.IsNullOrEmpty(wav)) return false;
            return wav.IndexOf("thunder", System.StringComparison.OrdinalIgnoreCase) >= 0
                || wav.IndexOf("storm", System.StringComparison.OrdinalIgnoreCase) >= 0
                || wav.IndexOf("vermilion", System.StringComparison.OrdinalIgnoreCase) >= 0
                || wav.IndexOf("jupitel", System.StringComparison.OrdinalIgnoreCase) >= 0
                || wav.IndexOf("meteor", System.StringComparison.OrdinalIgnoreCase) >= 0
                || wav.IndexOf("wind", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsLoudStormSkill(WeaponDef wd, string wav)
        {
            if (wd != null && (wd.SkillId == 21 || wd.SkillId == 83 || wd.SkillId == 84 || wd.SkillId == 85 || wd.SkillId == 86 || wd.SkillId == 89))
                return true;
            return IsLoudStormWav(wav);
        }

        // ------------------------------------------------------------------ the run
        public void OnRunStarted(HordeRunInfo run)
        {
            var au = Audio; var ch = _g.Channel;
            if (au != null)
            {
                au.ArenaListenerCell = () => ch != null ? new Vector2(ch.MyX, ch.MyY) : (Vector2?)null;
                au.ArenaRoot = _g.Arena.Root;
            }
            if (_g.Fx != null) _g.Fx.SoundSink = (wav, pos) =>
            {
                bool storm = IsLoudStormWav(wav);
                if (storm)
                {
                    if (Time.time - _stormSoundLast < 0.35f) return false;
                    _stormSoundLast = Time.time;
                }
                SoundsEffect++;
                return au != null && au.PlayArenaWav(wav, new Vector2(pos.x, pos.z), storm ? 0.36f : 0.78f);
            };
            // the monsters' own ACT sounds: the same gate as the world's monsters (the Monster sounds setting, its budget a second nearest first, its tighter attenuation)
            _g.Draw.Sound = (wav, cell) => { if (Has(wav) && au != null && au.PlayArenaUnitWav(wav, cell)) SoundsMonster++; };
            _g.Draw.Whoosh = pos => { if (Time.time - _whooshLast >= 0.15f && Play(ShotWhoosh, pos.x, pos.y, 0.5f, 1.1f)) { _whooshLast = Time.time; SoundsWhoosh++; } }; // client wave 17 (h17read), E2
            _typical = 12f; _typicalN = 0; _fireLast.Clear(); _enemyFireLast.Clear(); _stormSoundLast = 0f; _lastChestTime = -10f;
            Prewarm();
        }

        public void OnLeave()
        {
            Audio?.ClearArena();
            _g.Sprites?.ClearQueue();
            foreach (var u in _warmUnits) if (u != null) Object.Destroy(u.gameObject);
            _warmUnits.Clear(); _prewarmed = false;
        }

        /// <summary>Queues the sprites the run can show (once per run); the lobby's countdown builds them. Safe to call before the arena exists.</summary>
        public void Prewarm()
        {
            if (_prewarmed || !PrewarmEnabled) return;
            var s = _g.Sprites; var c = _g.Content; if (s == null || c == null) return;
            _prewarmed = true;
            // client wave 17 fix (h17fix): in the order a run needs them, so what a short countdown leaves is what comes last (wave 17's
            // tables take ~10 s to build: more than w9feel's 10 s countdown): my run character's job (drawn from the first frame) and the
            // pickups (from the first kill) first
            var start = c.StartJob();
            if (start != null) QueueJob(start.JobId);
            foreach (var p in c.Pickups) if (p.ItemId > 0) s.QueueItem(p.ItemId);
            // the kinds in the order the waves bring them (a run that starts at once still finds the first kinds built first), then the
            // kinds no wave brings by rank (the bosses' adds before elites and bosses), the same order every time
            var first = new Dictionary<int, float>();
            foreach (var wv in c.Waves) if (!first.TryGetValue(wv.EnemyId, out float f) || wv.From < f) first[wv.EnemyId] = wv.From;
            float First(EnemyDef e) => first.TryGetValue(e.Id, out float fw) ? fw : float.MaxValue;
            var order = new List<EnemyDef>(c.Enemies);
            order.Sort((x, y) => { int k = First(x).CompareTo(First(y)); if (k == 0) k = ((int)x.Rank).CompareTo((int)y.Rank); return k != 0 ? k : x.Id.CompareTo(y.Id); });
            foreach (var e in order) s.QueueKind(e.MobId);
            foreach (var w in c.Weapons) if (w.SummonMobId > 0) s.QueueKind(w.SummonMobId);
            if (start != null) foreach (int next in start.Next) QueueJob(next);
            // the map objectives' sprites (h17obj: an Emperium, a chest; from 1:30 on) here with the rest: queued at the run's start
            // (HordeObjectives.OnRunStarted, now a no-op for them) they were built in the arena's first frames of every run
            var obj = c.Objectives;
            if (obj != null) { s.QueueKind(obj.EmperiumMobId); s.QueueItem(obj.ChestItemId); }
            Debug.Log($"[Horde] pre-build queued: {s.WarmQueued} sprite actions ({c.Enemies.Count} monster kinds, {c.Pickups.Count} items, {JobsQueued} jobs)");
        }

        // ------------------------------------------------------------------ wave 27 (wfix): the arena's code compiled ahead
        // Measured (w9feel, a profiler capture of a development build): the frame that drew the first enemies spent 13.4 ms in Mono.JIT (the
        // drawing code's first calls: the client's scripts are compiled when first run) and 8 ms more in Horde's LateUpdate; its rendering then
        // ran behind it and the next frame waited for it (13.3 ms). The lobby compiles the Horde code's methods ahead (after the sprite
        // pre-build, in its slices), so the first appearance runs code that is already compiled.
        /// <summary>Compile Horde's methods ahead in the lobby (on; a switch for measurements). w9feel, normal build: the frame after the first
        /// enemies 9.6 ms with it, 11.6-14.4 ms without (the first enemies' own frame 12.6 against 19.6-21.2 ms).</summary>
        public static bool JitAhead { get; set; } = true;
        private static List<System.RuntimeMethodHandle> _jit; private static int _jitAt;
        /// <summary>Methods compiled ahead so far, and in all (the smoke).</summary>
        public static int JitDone => _jitAt;
        public static int JitTotal => _jit != null ? _jit.Count : 0;

        private static void JitSlice(float budgetMs)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (_jit == null)
            {
                _jit = new List<System.RuntimeMethodHandle>();
                const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
                foreach (var t in typeof(HordeFeel).Assembly.GetTypes())
                {
                    if (t.Namespace == null || !t.Namespace.StartsWith("Loki.Horde") || t.ContainsGenericParameters) continue;
                    foreach (var m in t.GetMethods(all)) if (!m.IsAbstract && !m.ContainsGenericParameters) _jit.Add(m.MethodHandle);
                    foreach (var k in t.GetConstructors(all)) if (!k.IsStatic) _jit.Add(k.MethodHandle);
                }
            }
            while (_jitAt < _jit.Count && sw.Elapsed.TotalMilliseconds < budgetMs)
            {
                try { System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(_jit[_jitAt]); } catch (System.Exception) { }
                _jitAt++;
            }
        }

        /// <summary>A job offer: the offered jobs' sprites are built while the player chooses.</summary>
        public void OnOffered(HordeOffer o)
        {
            if (o == null || !o.IsJob || !PrewarmEnabled) return;
            foreach (var k in o.Cards) if (k.Kind == HordeCardKind.Job) QueueJob(k.DefId);
        }

        private void QueueJob(int job)
        {
            var s = _g.Sprites; if (s == null || _g.Arena == null) return;
            var src = _g.LookOf?.Invoke(_g.MyAid);
            var look = new PlayerLook { Job = job, Sex = src != null ? src.Sex : 1, Hair = src != null ? src.Hair : 1, HairColor = src != null ? src.HairColor : 0 };
            string key = $"{job}:{look.Sex}:{look.Hair}:{look.HairColor}";
            if (!s.NeedsUnit(key)) return;
            Unit u;
            try { u = Unit.Create(_g.Arena.transform, null, 0xFE000000u | (uint)job, job, look.Sex, look.Hair, s.Assets, look); }
            catch (System.Exception e) { Debug.LogWarning($"[Horde] job {job} sprite for the pre-build: {e.Message}"); return; }
            u.gameObject.SetActive(false); // never drawn: only its frames are built
            _warmUnits.Add(u);
            s.QueueUnit(u, key); JobsQueued++;
        }

        /// <summary>A slice of the pre-build each frame (the lobby's budget while no arena is shown, a smaller one during a run, none right
        /// after a hitch during a run).</summary>
        public void LateUpdate()
        {
            var s = _g.Sprites; if (s == null) return;
            if (!s.WarmBusy && _prewarmed && !_g.InArena && JitAhead) JitSlice(PrewarmLobbyMs); // wave 27 (wfix): the arena's code compiled ahead, after the frames
            if (!s.WarmBusy) return;
            // client wave 17 fix (h17fix): in the arena the run's budget from its first frame (the engine and the stand-in spawn enemies from
            // the first tick: the count-in holds only the stick), and nothing right after a hitch (PrewarmHitchMs: the map's load, the first
            // draw of every kind). Measured (w9feel, h17fix dev 1): the lobby's 8 ms in the run's first 3 s put an 8.9 ms slice into the
            // frame after the first appearance (16.1 ms in all)
            if (_g.InArena && Time.unscaledDeltaTime * 1000f > PrewarmHitchMs) return;
            s.PrewarmSlice(_g.InArena ? PrewarmRunMs : PrewarmLobbyMs);
            if (!s.WarmBusy) Debug.Log($"[Horde] pre-build done: {s.WarmDone} actions, {s.BuiltAhead} frames built ahead in {s.WarmMs:0} ms (longest slice {s.WarmSliceMaxMs:0.0} ms; the single most expensive frame {s.WarmOneMaxMs:0.0} ms: {s.WarmOneMaxWhat})");
        }

        // ------------------------------------------------------------------ the events
        public void OnFxEnded(HordeFxView fx, bool hit)
        {
            if (!_g.InArena || !hit || fx == null || fx.Kind != HordeFxKind.Shot) return;
            var w = _g.Channel.World; float t = (float)(w.RenderTick(_g.Channel.Now) - fx.StartTick) / Mathf.Max(1, w.TickRate); // where the shot is DRAWN (protocol 2's render tick)
            int me = _g.Run != null ? _g.Run.Slot : -1;
            bool mine = fx.OwnerSlot == me;
            string wav = WeaponSound(fx.DefId, false);
            var wd = _g.Content?.Weapon(fx.DefId);
            float vol = (mine ? 0.85f : 0.15f) * (IsLoudStormSkill(wd, wav) ? 0.42f : 1f);
            if (Play(wav, fx.X + fx.VX * t, fx.Y + fx.VY * t, vol)) SoundsHit++;
        }

        // ------------------------------------------------------------------ client wave 17 (h17read): E2, E3, C5
        /// <summary>A lasting effect began (HordeGame.OnFxBegan): an enemy's shot sounds at its shooter (E2: once a shooter per 0.2 s, a volley
        /// is one sound); an elite's / boss's telegraph (any enemy fx but a shot: its area warnings, h17boss's shapes) sounds its wind-up (E3).</summary>
        public void OnFxBegan(HordeFxView fx)
        {
            if (!_g.InArena || fx == null || fx.OwnerEnemy < 0) return;
            if (fx.Kind == HordeFxKind.EnemyShot)
            {
                if (_enemyFireLast.TryGetValue(fx.OwnerEnemy, out float last) && Time.time - last < 0.2f) return;
                _enemyFireLast[fx.OwnerEnemy] = Time.time;
                if (Play(EnemyFire, fx.X, fx.Y, 0.7f)) SoundsEnemyFire++;
                return;
            }
            // not h17boss's fx kind 15 (EnemyTrail: a patch every 0.5 s) nor 12 (EnemyMark) with no target (a mark's follow-up, already announced): COORD 23:58
            int kind = (int)fx.Kind; if (kind == 15 || (kind == 12 && fx.Target < 0)) return;
            var w = _g.Channel.World; var en = fx.OwnerEnemy < w.Enemies.Length ? w.Enemies[fx.OwnerEnemy] : null;
            var d = en != null && en.Alive ? _g.Content?.Enemy(en.DefId) : null;
            if (d != null && d.Rank >= EnemyRank.Elite) WindUpSound(fx.X, fx.Y);
        }

        /// <summary>E3: a low charge-up where an elite / boss winds up (at most one every 0.3 s: a boss's two or three areas at once are one).</summary>
        private void WindUpSound(float x, float y)
        {
            if (Time.time - _windUpLast < 0.3f) return;
            if (Play(WindUpLow, x, y, 0.8f, 0.7f)) { _windUpLast = Time.time; SoundsWindUp++; }
        }

        /// <summary>C5: a soft tick, my character stands in an enemy's area (HordeReadability calls it every WarnTickEvery seconds meanwhile).</summary>
        public void WarnTick(Vector2 my) { if (Play(WarnTickWav, my.x, my.y, 0.3f, 1.6f)) SoundsWarn++; }

        public void OnEvent(HordeEvent e)
        {
            if (!_g.InArena) return;
            var c = _g.Content; int me = _g.Run != null ? _g.Run.Slot : -1;
            switch (e.Kind)
            {
                case HordeEventKind.Hit:
                {
                    _g.Draw.Flash(e.Target);
                    bool crit = (e.Flags & HordeEventFlags.Crit) != 0, dot = (e.Flags & HordeEventFlags.Dot) != 0;
                    // the run's typical hit: a running average of the plain hits (the first few settle it quickly)
                    if (!dot) { _typicalN++; float k = _typicalN < 8 ? 0.35f : 0.04f; _typical = Mathf.Lerp(_typical, Mathf.Max(1, e.Value), k); }
                    float size = Mathf.Clamp(0.85f + 0.32f * Mathf.Log(Mathf.Max(1f, e.Value) / Mathf.Max(1f, _typical), 2f), 0.7f, 1.9f);
                    if (crit) size = Mathf.Min(2.3f, size * 1.3f);
                    if (dot) size *= 0.8f;
                    var wd = c?.Weapon(e.DefId);
                    Color col = crit ? HitNumbers.Yellow : dot ? new Color(0.8f, 0.65f, 1f, 0.9f) : wd != null && wd.Element != Element.Neutral ? Color.Lerp(World.Fx.ElementColor((int)wd.Element), Color.white, 0.25f) : HitNumbers.White;
                    _g.Numbers.Show(_g.Arena.Local(e.X, e.Y), crit ? e.Value + "!" : e.Value.ToString(), col, size);
                    // the hit's sound: the weapon's (a shot's impact sounds from OnFxEnded; swings, novas and zones here, but not every tick of a zone)
                    if (!dot && wd != null && wd.Kind != WeaponKind.Bolt)
                    {
                        string hwav = WeaponSound(e.DefId, false);
                        bool storm = IsLoudStormSkill(wd, hwav);
                        if (!storm || Time.time - _stormSoundLast >= 0.32f)
                        {
                            if (storm) _stormSoundLast = Time.time;
                            float hvol = (e.Slot == me ? (crit ? 0.9f : 0.75f) : 0.14f) * (storm ? 0.42f : 1f);
                            if (Play(hwav, e.X, e.Y, hvol, crit ? 0.92f : 1f)) SoundsHit++;
                        }
                    }
                    break;
                }
                case HordeEventKind.Swing: case HordeEventKind.Nova: case HordeEventKind.Strike: case HordeEventKind.Chain:
                {
                    var wd = c?.Weapon(e.DefId);
                    string fwav = WeaponSound(e.DefId, true);
                    bool storm = IsLoudStormSkill(wd, fwav);
                    float minGap = e.Slot != me ? 0.45f : (storm ? 0.35f : 0.12f);
                    // the weapon fires (once per weapon per minGap: a 5-bolt volley or multi-hit storm is one non-intrusive sound)
                    if (_fireLast.TryGetValue(e.DefId, out float last) && Time.time - last < minGap) break;
                    _fireLast[e.DefId] = Time.time;
                    float x = e.Kind == HordeEventKind.Chain ? e.X2 : e.X, y = e.Kind == HordeEventKind.Chain ? e.Y2 : e.Y;
                    float fvol = (e.Slot == me ? 0.9f : 0.15f) * (storm ? 0.38f : 1f);
                    if (Play(fwav, x, y, fvol)) { SoundsWeapon++; LastWeaponSound = fwav; }
                    break;
                }
                case HordeEventKind.EnemyDied:
                    _g.Draw.Died(c, e.DefId, e.X, e.Y, e.Target);
                    break;
                case HordeEventKind.Effect:
                    if (e.Value == (int)EffectKind.Knockback && e.Target >= 0 && e.Target < _g.Channel.World.Enemies.Length)
                    {
                        // the push's direction: away from the weapon's owner (the enemy's new place is X / Y)
                        var w = _g.Channel.World; var dir = Vector2.zero;
                        if (e.Slot >= 0 && e.Slot < w.Players.Length && w.Players[e.Slot].Present) dir = new Vector2(e.X - w.Players[e.Slot].X, e.Y - w.Players[e.Slot].Y);
                        if (dir.sqrMagnitude < 1e-4f) { var en = w.Enemies[e.Target]; dir = new Vector2(e.X - en.PrevX, e.Y - en.PrevY); }
                        if (dir.sqrMagnitude < 1e-4f) dir = Vector2.up;
                        _g.Draw.Push(e.Target, dir); Pushes++;
                    }
                    break;
                case HordeEventKind.PickupTaken:
                {
                    _g.Draw.PickupTaken(e.Target);
                    var pd = c?.Pickup(e.DefId);
                    if (pd != null && pd.Kind == PickupKind.Magnet) { _g.Draw.Magnet(_g.Channel.World, c, e.Slot); Magnets++; }
                    if (pd != null && pd.Kind != PickupKind.Exp && _g.Arena?.Root != null)
                    {
                        var lpos = _g.Arena.Local(e.X, e.Y);
                        var fxCol = pd.Kind == PickupKind.Heal ? new Color(0.35f, 1f, 0.55f, 0.85f)
                            : pd.Kind == PickupKind.Magnet ? new Color(0.45f, 0.88f, 1f, 0.85f)
                            : pd.Kind == PickupKind.Chest ? new Color(1f, 0.88f, 0.35f, 0.9f)
                            : pd.Kind == PickupKind.Bomb ? new Color(1f, 0.48f, 0.22f, 0.9f)
                            : new Color(0.95f, 0.85f, 1f, 0.85f);
                        float fxRadius = pd.Kind == PickupKind.Bomb ? 3.2f : pd.Kind == PickupKind.Magnet ? 2.4f : 1.35f;
                        World.Fx.Ring(_g.Arena.Root, lpos, fxRadius, fxCol, 0.42f);
                        World.Fx.Burst(_g.Arena.Root, lpos + Vector3.up * 0.25f, fxCol, Mathf.Clamp(fxRadius * 0.55f, 0.5f, 1.5f), pd.Kind == PickupKind.Bomb ? 18 : 10);
                    }
                    if (e.Slot != me) break;
                    string wav = pd == null ? PickupExp : pd.Kind == PickupKind.Heal ? PickupHeal : pd.Kind == PickupKind.Chest ? PickupChest : pd.Kind == PickupKind.Magnet ? PickupMagnet : pd.Kind == PickupKind.Bomb ? PickupBomb : PickupExp;
                    float vol = pd != null && pd.Kind == PickupKind.Exp ? Mathf.Clamp(0.35f + 0.1f * Mathf.Log(Mathf.Max(1, e.Value), 2f), 0.35f, 0.7f) : 0.9f;
                    if (Play(wav, e.X, e.Y, vol, pd != null && pd.Kind == PickupKind.Exp ? 1f + 0.04f * Mathf.Min(6, Mathf.Log(Mathf.Max(1, e.Value), 2f)) : 1f)) SoundsPickup++;
                    if (pd != null && pd.Kind != PickupKind.Exp)
                    {
                        var tag = _g.Players?.TagOf(me);
                        if (tag != null)
                        {
                            switch (pd.Kind)
                            {
                                case PickupKind.Heal:
                                {
                                    int healPct = Mathf.RoundToInt(pd.Value > 0f && pd.Value <= 1f ? pd.Value * 100f : pd.Value);
                                    tag.Gift(Lang.F("+{0}% HP!", healPct), new Color(0.45f, 1f, 0.55f));
                                    break;
                                }
                                case PickupKind.Magnet:
                                    tag.Gift(Lang.T("EXP Magnet! Pulls all gems"), new Color(0.55f, 0.9f, 1f));
                                    break;
                                case PickupKind.Chest:
                                    _lastChestTime = Time.unscaledTime;
                                    tag.Gift(Lang.T("Box: +1 Free Upgrade!"), new Color(1f, 0.88f, 0.38f));
                                    break;
                                case PickupKind.Bomb:
                                    tag.Gift(Lang.F("Bomb! {0} AoE Damage", Mathf.RoundToInt(pd.Value)), new Color(1f, 0.55f, 0.35f));
                                    break;
                                case PickupKind.Passive:
                                    tag.Gift(Lang.T(pd.Id == 242 || pd.Key == "proc_arrow_bundle" ? "+1 Projectile (10s)!" : "Buff Active!"), new Color(0.75f, 0.92f, 1f));
                                    break;
                                default:
                                    string pName = pd.ItemId > 0 ? UI.UiKit.StripTags(Game.GameDb.GetItem(pd.ItemId)?.Name ?? "") : pd.Key;
                                    if (!string.IsNullOrEmpty(pName))
                                        tag.Gift(Lang.T(pName), new Color(1f, 0.92f, 0.55f));
                                    break;
                            }
                        }
                    }
                    break;
                }
                case HordeEventKind.CardTaken:
                    if (e.Slot == me)
                    {
                        if (Play(CardSound, _g.Channel.MyX, _g.Channel.MyY, 0.8f)) SoundsCard++;
                        if (Time.unscaledTime - _lastChestTime <= 0.6f)
                        {
                            _lastChestTime = -10f;
                            var card = new HordeCard { Kind = (HordeCardKind)Mathf.RoundToInt(e.A), DefId = e.DefId, Level = e.Value };
                            if (c != null && card.Kind != 0)
                                _g.Players?.TagOf(me)?.Gift(Lang.F("Box: {0} ▲!", HordeText.CardTitle(c, card)), new Color(1f, 0.9f, 0.42f));
                        }
                    }
                    break;
                case HordeEventKind.PlayerHit:
                {
                    // client wave 17 (h17read), E1: "you were hurt", RO's own player-hit sound by my job; a big hit louder, higher, a heavy hit under it
                    if (e.Slot != me || (e.Flags & HordeEventFlags.Dodged) != 0) break;
                    var w = _g.Channel.World; var p = me >= 0 && me < w.Players.Length ? w.Players[me] : null;
                    string wav = global::Loki.Audio.WeaponSounds.JobHit(p != null ? p.JobId : 0);
                    bool big = p != null && p.MaxHp > 0 && e.Value > p.MaxHp * HordeReadability.BigHitShare;
                    float mx = _g.Channel.MyX, my = _g.Channel.MyY;
                    if (Play(wav, mx, my, big ? 1f : 0.75f, big ? 1.15f : 1f)) { SoundsHurt++; LastHurtSound = wav; }
                    if (big && Play(HurtBig, mx, my, 0.85f, 1.1f)) SoundsHurtBig++;
                    break;
                }
                case HordeEventKind.WindUp:
                    // client wave 17 (h17read), E3: an elite's / boss's wind-up begins (a charge)
                    if ((e.Flags & (HordeEventFlags.Elite | HordeEventFlags.Boss)) != 0) WindUpSound(e.X, e.Y);
                    break;
                case HordeEventKind.BossAppeared:
                {
                    var d = c?.Enemy(e.DefId);
                    bool boss = (e.Flags & (HordeEventFlags.Boss | HordeEventFlags.Final)) != 0;
                    string own = d != null ? MonsterSting(d.MobId) : null;
                    string wav = own ?? BossSting;
                    if (Play(wav, e.X, e.Y, boss ? 1f : 0.7f)) { SoundsBoss++; LastBossSound = wav; }
                    if (own != null && boss && Play(BossSting, e.X, e.Y, 0.6f)) SoundsBoss++; // the alarm under its roar: an event
                    break;
                }
            }
        }

        /// <summary>A boss's own "breath" / "stand" sound from the install, by its sprite name (the same name the world's loaders use), when it exists.</summary>
        private string MonsterSting(int mobId)
        {
            string n = RoNames.MobName(mobId); if (string.IsNullOrEmpty(n)) return null;
            n = n.ToLowerInvariant();
            foreach (var suffix in new[] { "_breath.wav", "_stand.wav", "_stand1.wav" }) if (Has(n + suffix)) return n + suffix;
            return null;
        }

        /// <summary>The weapon's fire (<paramref name="fire"/>) or hit sound, by the rules of the class summary; cached per weapon.</summary>
        public string WeaponSound(int weaponId, bool fire)
        {
            if (!_weaponSounds.TryGetValue(weaponId, out var s)) { s = Resolve(_g.Content?.Weapon(weaponId)); _weaponSounds[weaponId] = s; }
            return fire ? s.fire : s.hit;
        }

        private (string fire, string hit) Resolve(WeaponDef w)
        {
            if (w == null) return (null, null);
            string fire = null, hit = null, why = "";
            if (w.SkillId > 0)
            {
                // 1. the official skilleffectinfolist of the player's GRF (waveFileName on the caster, targetWaveFileName on the target)
                if (GrfSkillEffects.Official.TryGetValue(w.SkillId, out var o))
                {
                    if (o.TryGetValue("waveFileName", out var wf) && wf is string a && Has(WavName(a))) fire = WavName(a);
                    if (o.TryGetValue("targetWaveFileName", out var tf) && tf is string b && Has(WavName(b))) hit = WavName(b);
                    if (fire != null || hit != null) why = "official skilleffectinfolist";
                }
                // 2. the client's effect tables: the wav of the skill's effect (fire) and of its hit effect (hit)
                var sf = EffectDb.Skill(w.SkillId);
                if (sf != null)
                {
                    fire ??= FirstWav(sf.EffectId) ?? FirstWav(sf.EffectIdOnCaster);
                    hit ??= FirstWav(sf.HitEffectId) ?? FirstWav(sf.EffectId);
                    if (why == "" && (fire != null || hit != null)) why = "the effect tables (rB / ez2 / GRF)";
                }
            }
            // 3. a neutral sound: a heal (Potion Pitcher, Heal …) sounds as RO's heal does; a pure buff (no damage, no heal) is silent unless its effect
            //    has a wav; anything that hurts, by its damage class
            var l1 = w.Levels != null && w.Levels.Length > 0 ? w.Levels[0] : null;
            bool heals = l1 != null && (l1.Heal > 0 || l1.HealPct > 0f), hurts = l1 == null || l1.Damage > 0;
            if (heals) { fire ??= PickupHeal; hit ??= PickupHeal; }
            else if (hurts)
            {
                fire ??= w.Class == DamageClass.Ranged ? SwingRanged : w.Class == DamageClass.Magic ? null : SwingMelee;
                hit ??= w.Class == DamageClass.Ranged ? HitRanged : w.Class == DamageClass.Magic ? HitMagic : HitMelee;
            }
            if (!Has(fire)) fire = null; if (!Has(hit)) hit = null;
            Debug.Log($"[Horde] weapon {w.Key} (skill {w.SkillId}) sounds: fire {(fire ?? "-")}, hit {(hit ?? "-")}{(why != "" ? " (" + why + ")" : " (neutral by damage class)")}");
            return (fire, hit);
        }

        private static string WavName(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            s = s.Replace('/', '\\');
            if (!s.EndsWith(".wav", System.StringComparison.OrdinalIgnoreCase)) s += ".wav";
            return s;
        }

        private string FirstWav(string[] effectKeys)
        {
            if (effectKeys == null) return null;
            foreach (var key in effectKeys)
            {
                var parts = EffectDb.Effect(key); if (parts == null) continue;
                foreach (var p in parts)
                {
                    if (!p.Has("wav")) continue;
                    string wav = p.S("wav"); if (string.IsNullOrEmpty(wav)) continue;
                    if (wav.Contains("%d")) wav = wav.Replace("%d", "1");
                    wav = WavName(wav);
                    if (Has(wav)) return wav;
                }
            }
            return null;
        }
    }
}
