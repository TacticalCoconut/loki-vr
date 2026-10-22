using System.Collections.Generic;
using Loki.Net;
using Loki.Net.Packets;
using Loki.World;
using UnityEngine;

namespace Loki.Sprites
{
    /// <summary>Turns map-server entity packets into Units (spawn, idle, walk, stop, vanish, direction, names, damage).</summary>
    public sealed class EntityManager : MonoBehaviour
    {
        private readonly Dictionary<uint, Unit> _units = new Dictionary<uint, Unit>();
        private RoAssets _assets; private MapLoader _map;
        public uint SelfId;
        public Unit Self { get; private set; }
        /// <summary>Self died (ZC_NOTIFY_VANISH type 1 for our own id) / was revived (ZC_RESURRECTION).</summary>
        public event System.Action SelfDied, SelfRevived;
        public event System.Action<bool> SelfSitting;
        /// <summary>Damage shown: source, target, amount (0 = miss), critical.</summary>
        public event System.Action<Unit, Unit, int, bool> Damage;
        /// <summary>A unit other than ourselves was created (spawn / appear / walking-in packets) — for party HP bars, name tints…</summary>
        public event System.Action<Unit> UnitAdded;
        private readonly Dictionary<uint, (int effect, int color)> _markers = new Dictionary<uint, (int, int)>();
        public IReadOnlyDictionary<uint, Unit> Units => _units;
        /// <summary>Persistent look of our own character: set from char select (<see cref="SetSelfLook"/>), kept up to date by
        /// ZC_SPRITE_CHANGE, and re-applied by <see cref="CreateSelf"/> after every map change.</summary>
        private PlayerLook _selfLook;
        private CharInfo _selfLookSource;
        public PlayerLook SelfLook => _selfLook;

        public void Init(RoAssets assets, MapLoader map) { _assets = assets; _map = map; }

        public void Clear()
        {
            foreach (var u in _units.Values) if (u != null) Destroy(u.gameObject);
            _units.Clear(); Self = null; _markers.Clear();
        }

        /// <summary>
        /// Stores our character's look from the char-select CHARACTER_INFO. Only a CharInfo object not seen before replaces the
        /// stored look, so calling this again with the same SelectedChar on every map change keeps what ZC_SPRITE_CHANGE updated
        /// since (job change, equipment, dyes). Applies to Self at once when it exists.
        /// </summary>
        public void SetSelfLook(CharInfo c)
        {
            if (c == null || ReferenceEquals(c, _selfLookSource)) return;
            _selfLookSource = c;
            _selfLook = PlayerLook.FromCharInfo(c);
            if (Self != null) Self.SetLook(_selfLook, _assets);
        }

        /// <summary>Creates our own unit. When a look was stored (<see cref="SetSelfLook"/> / ZC_SPRITE_CHANGE) it wins over
        /// the job/sex/hair arguments, so the current job and equipment survive map changes.</summary>
        public Unit CreateSelf(uint id, int job, int sex, int hair, int x, int y, int serverDir)
        {
            SelfId = id;
            var look = _selfLook;
            if (look != null) { job = look.Job; sex = look.Sex; hair = look.Hair; }
            Self = Unit.Create(_map.transform, _map.Current.Gat, id, job, sex, hair, _assets, look);
            Self.IsSelf = true;
            Self.SetObjectType(0);
            Self.SetName("");
            Self.SetCell(x, y, Unit.FromServerDir(serverDir));
            if (_selfSpeed > 0) Self.Speed = _selfSpeed; // SP_SPEED survives the map change (client wave 3, w3move)
            HookSounds(Self);
            _units[id] = Self;
            return Self;
        }

        /// <summary>Look from the unit packets (research/appearance_layers.md §2): weapon/shield are item ids, accessory =
        /// head bottom, accessory2 = head top, accessory3 = head mid, body = 0/1 costume flag.</summary>
        private static PlayerLook LookFrom(int job, int sex, int head, uint weapon, uint shield, int acc, int acc2, int acc3, int headPal, int bodyPal, int robe, int body) =>
            new PlayerLook
            {
                Job = job, Sex = sex, Hair = head, Weapon = (int)weapon, Shield = (int)shield, HeadBottom = acc, HeadTop = acc2, HeadMid = acc3,
                HairColor = headPal, ClothesColor = bodyPal, Robe = robe, Body2 = body,
            };

        /// <summary>ZC_NOTIFY_VANISH for a unit other than ourselves: id, type (0 out of sight, 1 died, 2 logged out, 3 teleported) — before it is removed.</summary>
        public event System.Action<uint, int> Vanished;
        /// <summary>Party / guild / title names of a unit arrived (ZC_ACK_REQNAMEALL2 0x0a30).</summary>
        public event System.Action<Unit> NamesChanged;
        /// <summary>A unit already in view changed guild (unit packet GUID / GEmblemVer differ from what we had).</summary>
        public event System.Action<Unit> GuildChanged;

        private Unit Ensure(uint id, int job, int sex, int hair, int objecttype, string name, PlayerLook look = null, uint guild = 0, int emblem = 0)
        {
            if (_units.TryGetValue(id, out var u) && u != null)
            {
                if (u.GuildId != guild || u.GuildEmblem != emblem) { u.GuildId = guild; u.GuildEmblem = emblem; GuildChanged?.Invoke(u); }
                if (look != null && RoNames.IsPlayer(job)) { if (!u.Look.SameAs(look) || u.Job != job || u.Sex != sex || u.Hair != hair) u.SetLook(look, _assets); }
                else if (u.Job != job || u.Sex != sex || u.Hair != hair) { u.Job = job; u.Sex = sex; u.Hair = hair; u.RefreshClassFlags(); u.ApplyLook(_assets); }
                if (!string.IsNullOrEmpty(name) && name != u.Name) u.SetName(name);
                return u;
            }
            u = Unit.Create(_map.transform, _map.Current.Gat, id, job, sex, hair, _assets, look);
            u.Name = name ?? ""; u.GuildId = guild; u.GuildEmblem = emblem;
            u.SetObjectType(objecttype);
            u.SetName(u.Name);
            HookSounds(u);
            if (_markers.TryGetValue(id, out var m) && u.Overhead != null) u.Overhead.SetQuestMarker(m.effect, m.color);
            _units[id] = u;
            UnitAdded?.Invoke(u);
            return u;
        }

        private void HookSounds(Unit u)
        {
            // an ACT frame event: a ".wav" plays as named, "atk" is the weapon swing (client wave 3, Audio/WeaponSounds.ActEvent)
            u.Sprite.Sound += ev => { var wav = Audio.WeaponSounds.ActEvent(u, ev); if (wav != null) Audio.LokiAudio.Instance?.PlayWav(wav, u.Cell); };
        }

        /// <summary>Client wave 3 (w3combat): the per-hit schedule 0x08c8 hands its damage to (set by LokiGame.W3Combat).</summary>
        public Game.HitScheduler Hits;

        /// <summary>Client wave 4 (w4fx): a killed monster's death after its last hit (Game/DeathSync.cs); its id is already free.</summary>
        private System.Collections.IEnumerator DieAfter(Unit u, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (u == null) yield break;
            u.Die();
            Destroy(u.gameObject, GrannyUnit.CorpseSeconds(u, 2f)); // w4granny: a 3-D body's death clip plays out (rB remove_delay 5 s)
        }

        /// <summary>Quest marker for an NPC (from ZC_QUEST_NOTIFY_EFFECT) — remembered if the NPC is not spawned yet.</summary>
        public void SetQuestMarker(uint npcId, int effect, int color)
        {
            _markers[npcId] = (effect, color);
            if (_units.TryGetValue(npcId, out var u) && u != null && u.Overhead != null) u.Overhead.SetQuestMarker(effect, color);
        }

        public Unit Get(uint id) => _units.TryGetValue(id, out var u) ? u : null;

        // ---- client wave 18 (w18tut), point 34: a unit only this client draws (the interactive tutorial's practice Poring:
        // LokiGame.W18Tutorial.cs). It is in Units like any other, so the look (Game/Targeting.cs looks only there), the laser and the
        // minimap take it as a unit; the server never hears of it: the tutorial answers its clicks (LokiGame.cs OnTargetClicked). Clear()
        // removes it with the map's units, as any other.
        public Unit AddLocal(uint id, int job, int objectType, string name, int x, int y, int dir)
        {
            var u = Ensure(id, job, 0, 0, objectType, name);
            u.SetCell(x, y, dir);
            return u;
        }
        public void RemoveLocal(uint id) { if (_units.TryGetValue(id, out var u)) { _units.Remove(id); if (u != null) Destroy(u.gameObject); } }

        /// <returns>true when handled</returns>
        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case PacketTable.Map.idle_unitType:
                {
                    var p = packet_idle_unit.Parse(ref r, o + n);
                    if (p.AID == SelfId) return true;
                    var u = Ensure(p.AID, p.job, p.sex, p.head, p.objecttype, p.name,
                        LookFrom(p.job, p.sex, p.head, p.weapon, p.shield, p.accessory, p.accessory2, p.accessory3, p.headpalette, p.bodypalette, p.robe, p.body), p.GUID, (ushort)p.GEmblemVer);
                    u.Sprite.HeadDir = HeadDirOf(p.headDir);
                    u.Speed = p.speed;
                    ApplyHp(u, p.HP, p.maxHP);
                    var (x, y, d) = UnpackPos(p.PosDir);
                    u.SetCell(x, y, Unit.FromServerDir(d));
                    if (p.state == 2) u.Sprite.Play((int)ActorSprite.PlayerAction.Sit, true); // sitting (vd->dead_sit 2)
                    else if (p.state == 1) u.Die(); // dead (vd->dead_sit 1)
                    ApplyOption(u, p.effectState);
                    ApplyStates(u, p.bodyState, p.healthState, p.virtue);
                    return true;
                }
                case PacketTable.Map.spawn_unitType:
                {
                    var p = packet_spawn_unit.Parse(ref r, o + n);
                    if (p.AID == SelfId) return true;
                    var u = Ensure(p.AID, p.job, p.sex, p.head, p.objecttype, p.name,
                        LookFrom(p.job, p.sex, p.head, p.weapon, p.shield, p.accessory, p.accessory2, p.accessory3, p.headpalette, p.bodypalette, p.robe, p.body), p.GUID, (ushort)p.GEmblemVer);
                    u.Sprite.HeadDir = HeadDirOf(p.headDir);
                    u.Speed = p.speed;
                    ApplyHp(u, p.HP, p.maxHP);
                    var (x, y, d) = UnpackPos(p.PosDir);
                    u.SetCell(x, y, Unit.FromServerDir(d));
                    ApplyOption(u, p.effectState);
                    ApplyStates(u, p.bodyState, p.healthState, p.virtue);
                    return true;
                }
                case PacketTable.Map.unit_walkingType:
                {
                    var p = packet_unit_walking.Parse(ref r, o + n);
                    if (p.AID == SelfId) return true;
                    var u = Ensure(p.AID, p.job, p.sex, p.head, p.objecttype, p.name,
                        LookFrom(p.job, p.sex, p.head, p.weapon, p.shield, p.accessory, p.accessory2, p.accessory3, p.headpalette, p.bodypalette, p.robe, p.body), p.GUID, (ushort)p.GEmblemVer);
                    u.Sprite.HeadDir = HeadDirOf(p.headDir);
                    u.Speed = p.speed;
                    ApplyHp(u, p.HP, p.maxHP);
                    if (u.Dead && p.HP > 0) u.Revive();
                    // client wave 3 (w3move): the server's path, sub-cell and start tick (packets_struct.hpp:758-830; clif.cpp:1369-1467)
                    var (x0, y0, x1, y1, sx0, sy0) = UnpackMoveSub(p.MoveData);
                    u.WalkTo(x0, y0, x1, y1, sx0, sy0, p.moveStartTime, true, IsPetLootWalk(u, x0, y0, x1, y1));
                    ApplyOption(u, p.effectState);
                    ApplyStates(u, p.bodyState, p.healthState, p.virtue);
                    return true;
                }
                case 0x0229: // ZC_STATE_CHANGE aid.L bodyState.W healthState.W option.L isPKModeOn.B (research/qol_packets.md §4)
                {
                    // opt1 / opt2 as clif_changeoption_target writes them (clif.cpp:4378-4432); opt3 never travels here
                    r.Skip(2); uint aid = r.U32(); int opt1 = r.U16(); int opt2 = r.U16(); int option = r.S32();
                    if (_units.TryGetValue(aid, out var u) && u != null) { ApplyOption(u, option); var st = UnitState.Of(u); st.SetBody(opt1); st.SetHealth(opt2); }
                    return true;
                }
                case 0x028a: // ZC_NPC_SHOWEFST_UPDATE gid.L effectState.L level.L showEFST.L (packets.hpp:1105-1112): clif_changeoption2 (clif.cpp:4437-4461),
                {            // sent instead of 0x0229 when a NonPlayer status ends (status.cpp:14085-14086); showEFST = opt3 (w3status)
                    r.Skip(2); uint gid = r.U32(); int option = r.S32(); r.S32(); int opt3 = r.S32();
                    if (_units.TryGetValue(gid, out var u) && u != null) { ApplyOption(u, option); UnitState.Of(u).SetVirtue(opt3); }
                    return true;
                }
                case PacketTable.Map.ZC_NOTIFY_VANISH:
                {
                    var p = PACKET_ZC_NOTIFY_VANISH.Parse(ref r, o + n);
                    if (p.gid == SelfId)
                    {
                        if (p.type == 1 && Self != null) { Self.Die(); SelfDied?.Invoke(); }
                        return true;
                    }
                    Vanished?.Invoke(p.gid, p.type);
                    if (_units.TryGetValue(p.gid, out var u) && u != null)
                    {
                        // CLR_DEAD (1): a monster's corpse fades, but a dead player stays on the ground — the server keeps the unit and
                        // ZC_RESURRECTION (0x0148) alone brings it back (clif_resurrection sends no spawn packet); it leaves for real with
                        // CLR_RESPAWN / CLR_OUTSIGHT when it respawns or warps.
                        if (p.type == 1 && u.IsPlayerType) { u.Die(); return true; }
                        if (p.type == 1)
                        {
                            float hold = Game.DeathSync.Delay(Hits, p.gid);
                            if (hold > 0f) { u.HaltWalkInPlace(); StartCoroutine(DieAfter(u, hold)); }
                            else { u.Die(); Destroy(u.gameObject, GrannyUnit.CorpseSeconds(u, 2f)); }
                        } // died: play death then remove — after its last hit has shown (client wave 4, w4fx: Game/DeathSync.cs)
                        else Destroy(u.gameObject);
                        _units.Remove(p.gid);
                    }
                    return true;
                }
                case PacketTable.Map.ZC_STOPMOVE:
                {
                    var p = PACKET_ZC_STOPMOVE.Parse(ref r, o + n);
                    if (_units.TryGetValue(p.AID, out var u) && u != null)
                    {
                        // a slide, not a snap (Unit.Stop); a pull of our own character (traps, rA skill.cpp:6443-6444) gets the comfort treatment
                        var st = u.SlideTarget; bool sliding = st.HasValue && (st.Value - new Vector2(p.xPos, p.yPos)).sqrMagnitude < 0.0025f; // the 0x0088 after a 0x01ff
                        bool big = u == Self && !sliding && Vector2.Distance(u.PositionAt(Time.time), new Vector2(p.xPos, p.yPos)) >= 1.5f;
                        u.Stop(p.xPos, p.yPos, big && SnapSelfSlides != null && SnapSelfSlides());
                        if (u == Self) SelfSitting?.Invoke(false);
                        if (big) SelfSlide?.Invoke(0x0088);
                    }
                    return true;
                }
                case PacketTable.Map.ZC_CHANGE_DIRECTION:
                {
                    var p = PACKET_ZC_CHANGE_DIRECTION.Parse(ref r, o + n);
                    if (_units.TryGetValue(p.srcId, out var u) && u != null) { u.Sprite.Direction = Unit.FromServerDir(p.dir); u.Sprite.HeadDir = HeadDirOf(p.headDir); }
                    return true;
                }
                case PacketTable.Map.sendLookType: // 0x01d7 ZC_SPRITE_CHANGE: AID.L type.B val.L val2.L (15 bytes, appearance_layers.md §1)
                {
                    var p = PACKET_ZC_SPRITE_CHANGE.Parse(ref r, o + n);
                    OnSpriteChange(p.AID, p.type, (int)p.val, (int)p.val2);
                    return true;
                }
                case PacketTable.Map.ZC_NOTIFY_PLAYERMOVE:
                {
                    var p = PACKET_ZC_NOTIFY_PLAYERMOVE.Parse(ref r, o + n);
                    var (x0, y0, x1, y1, sx0, sy0) = UnpackMoveSub(p.moveData);
                    if (Self != null)
                    {
                        Self.WalkTo(x0, y0, x1, y1, sx0, sy0, p.moveStartTime, true);
                        Self.Sprite.HeadDir = 0; // unit_walktoxy_sub: sd->head_dir = DIR_NORTH (rA unit.cpp:299-302)
                    }
                    return true;
                }
                case PacketTable.Map.ZC_NOTIFY_ACT:
                {
                    var p = PACKET_ZC_NOTIFY_ACT.Parse(ref r, o + n);
                    if (_units.TryGetValue((uint)p.srcID, out var src) && src != null)
                    {
                        if (p.type == 2) { src.Sprite.Play(2, true); if (src == Self) SelfSitting?.Invoke(true); }      // sit
                        else if (p.type == 3) { if (src.Dead) src.Revive(); else src.Sprite.Play(0, true); if (src == Self) SelfSitting?.Invoke(false); } // stand
                        // pick up item: the motion, then idle (roBrowserLegacy Engine/MapEngine/Entity.js:894-913, next IDLE). Not while the unit
                        // walks: rAthena's pc_takeitem does not stop a walk (pc.cpp:6189-6262) and Loki picks up the items walked over (asked by
                        // design note, 2026-10-01), so a walking unit keeps its walk instead of sliding on in the pickup pose (rB never meets this: its
                        // player stops before picking up)
                        else if (p.type == 1) { if (src.IsPlayerType && !src.Walking && !src.Dead) src.Sprite.PlayThen((int)ActorSprite.PlayerAction.PickUp, 0f, (int)ActorSprite.PlayerAction.Idle); }
                        else { src.PlayAttack(p.srcSpeed); Hits?.PaceAttack(src, p.srcSpeed); } // attack: PCs 5/10/11 by weapon (rb WeaponAction) then the battle stance after attackMT (srcSpeed) ms, monsters 2 then idle; paced (client wave 3)
                    }
                    bool hit = p.type == 0 || p.type == 4 || p.type == 8 || p.type == 9 || p.type == 10 || p.type == 13; // e_damage_type: normal, endure, multi-hit(+endure), critical, multi-hit critical
                    _units.TryGetValue((uint)p.targetID, out var dst);
                    // client wave 3 (w3combat): numbers, flinches, hit sounds, arrows / monster projectiles and rumble land per hit on the
                    // HitScheduler (Game/HitSchedule.cs); Damage stays the packet-arrival event (companion assist, smoke counters)
                    if ((hit || p.type == 11) && Hits != null) Hits.OnNotifyAct(p, src, dst);
                    else if (hit && dst != null && p.damage > 0 && p.type != 4 && p.type != 9) dst.PlayHurt(); // no scheduler (not wired): hurt at once as before
                    if (hit && dst != null) Damage?.Invoke(src, dst, p.damage + Mathf.Max(0, p.damage2), p.type == 10 || p.type == 13);
                    else if (p.type == 11 && dst != null) Damage?.Invoke(src, dst, -1, false); // lucky dodge
                    return true;
                }
                case PacketTable.Map.ZC_ACK_REQNAMEALL:
                {
                    // ZC_ACK_REQNAMEALL2: name, party, guild and guild-position title (the reply to CZ_REQNAME2 0x0368, also pushed to the area on guild changes);
                    // for a castle's guardians and Emperium the "title" is the castle name (clif.cpp:10028-10037). Negative gid = one's own disguise.
                    var p = PACKET_ZC_ACK_REQNAMEALL.Parse(ref r, o + n);
                    uint gid = p.gid < 0 ? (uint)(-p.gid) : (uint)p.gid;
                    if (_units.TryGetValue(gid, out var u) && u != null)
                    {
                        u.Name = p.name;
                        u.PartyName = p.party_name ?? ""; u.GuildName = p.guild_name ?? ""; u.GuildTitle = p.position_name ?? ""; u.TitleId = Mathf.Max(0, p.title_id);
                        NamesChanged?.Invoke(u);
                    }
                    return true;
                }
                case PacketTable.Map.ZC_ACK_REQNAMEALL_NPC:
                {
                    var p = PACKET_ZC_ACK_REQNAMEALL_NPC.Parse(ref r, o + n);
                    if (_units.TryGetValue((uint)p.gid, out var u) && u != null && p.name != u.Name) u.SetName(p.name); // also a renamed pet (clif_name_area, pet.cpp:1503)
                    return true;
                }
                case 0x0977: // ZC_NOTIFY_MONSTER_HP: id.L hp.L maxhp.L (rAthena clif_monster_hp_bar)
                {
                    r.Skip(2); uint id2 = r.U32(); int hp = r.S32(), max = r.S32();
                    if (_units.TryGetValue(id2, out var u) && u != null) ApplyHp(u, hp, max);
                    return true;
                }
                case 0x0148: // ZC_RESURRECTION: gid.L type.W
                {
                    r.Skip(2); uint gid = r.U32();
                    if (gid == SelfId && Self != null) { Self.Revive(); SelfRevived?.Invoke(); }
                    else if (_units.TryGetValue(gid, out var u) && u != null) u.Revive();
                    return true;
                }
                case 0x01ff: // ZC_HIGHJUMP id.L x.W y.W (clif_slide, rA clif.cpp:10096-10116): knockback, High Jump, backslide ...
                {
                    r.Skip(2); uint aid = r.U32(); int x = r.U16(), y = r.U16();
                    if (_units.TryGetValue(aid, out var u) && u != null) OnSlide(u, 0x01ff, x, y);
                    return true;
                }
                case 0x08d2: // ZC_FASTMOVE id.L x.W y.W (clif_snap, rA clif.cpp:19939-19955): Body Relocation, Fallen Angel, All in the Sky
                {
                    r.Skip(2); uint aid = r.U32(); int x = r.U16(), y = r.U16();
                    if (_units.TryGetValue(aid, out var u) && u != null) OnSlide(u, 0x08d2, x, y);
                    return true;
                }
                case 0x007f: // ZC_NOTIFY_TIME time.L: the server's gettick(), the answer to CZ_REQUEST_TIME2 0x0360 (rA clif.cpp:11188-11209)
                {
                    r.Skip(2); uint t = r.U32();
                    ServerClock.OnNotifyTime(t);
                    ClockSample?.Invoke(t);
                    return true;
                }
                case 0x00b0: // ZC_PAR_CHANGE varID.W count.L: peek at SP_SPEED (0), our walk speed (rA clif.cpp:3646-3648); PlayerState keeps the rest
                {
                    r.Skip(2); int v = r.U16(); int val = r.S32();
                    if (v == 0 && val > 0) { _selfSpeed = val; if (Self != null) Self.SetSpeed(val); }
                    return false;
                }
                case 0x01b0: // ZC_NPCSPRITE_CHANGE GID.L type.B class.L (client wave 3, w3social: W3SocialReclass below)
                {
                    r.Skip(2); uint gid = r.U32(); r.U8(); int cls = r.S32();
                    if (_units.TryGetValue(gid, out var u) && u != null && !u.IsSelf) W3SocialReclass(u, cls);
                    return true;
                }
                default: return false;
            }
        }

        // ------------------------------------------------------------------ client wave 3 (w3move): slides, clock, speed
        private int _selfSpeed;
        /// <summary>Our own unit was moved by a knockback / fast move (0x01ff, 0x08d2) or pulled 1.5+ cells (0x0088): the packet id.
        /// LokiGame flashes the comfort vignette in the Follow / first-person views.</summary>
        public event System.Action<int> SelfSlide;
        /// <summary>true: our own unit's slides happen at once (Comfort "snap knockback" in Follow / first person).</summary>
        public System.Func<bool> SnapSelfSlides;
        /// <summary>The cells of the items on the floor (for a pet's loot walk, see <see cref="IsPetLootWalk"/>).</summary>
        public System.Func<IEnumerable<Vector2Int>> FloorItemCells;
        /// <summary>A ZC_NOTIFY_TIME sample arrived (smoke log).</summary>
        public event System.Action<uint> ClockSample;
        /// <summary>Knockback / fast-move slides seen (smoke): unit id, packet id, target cell.</summary>
        public event System.Action<uint, int, int, int> Slid;

        /// <summary>
        /// 0x01ff: rAthena's note on the client, "moves(instant) ... even when the target cell isn't walkable. If the char is sitting
        /// it stays that way" (rA clif.cpp:10096-10101); roBrowserLegacy snaps (Engine/MapEngine/Entity.js:442-454). Loki slides
        /// instead so that a knockback reads as one, fast: 25 ms per cell, 80-200 ms (a Loki choice, no official figure is known).
        /// 0x08d2: roBrowserLegacy's fastMoveTo, 15 ms per cell, facing the destination (Entity.js:461-484, EntityWalk.js
        /// fastMoveTo; a reimplementation). Our own unit: at once when the Comfort option says so in Follow / first person, and the
        /// event for the vignette.
        /// </summary>
        private void OnSlide(Unit u, int packet, int x, int y)
        {
            float d = Vector2.Distance(u.PositionAt(Time.time), new Vector2(x, y));
            float ms = packet == 0x08d2 ? 15f * d : Mathf.Clamp(25f * d, 80f, 200f);
            if (u == Self && SnapSelfSlides != null && SnapSelfSlides()) ms = 0f;
            // client wave 5 (w5move): a fast-moving Monk / Gunslinger holds its attack / skill frame with after-images, and every
            // fast move ends idle (rB onEntityFastMove + finishWalk: Sprites/FastMoveLook.cs)
            int hold = packet == 0x08d2 ? FastMoveLook.HoldGroup(u) : -1;
            u.Slide(x, y, ms, packet == 0x08d2, false, hold, packet == 0x08d2);
            if (hold >= 0 && u.Sliding) FastMoveTrail.Begin(u);
            Slid?.Invoke(u.Id, packet, x, y);
            if (u == Self) SelfSlide?.Invoke(packet);
        }

        /// <summary>
        /// Whether a pet's walk is its loot walk, the one walk of a pet that rAthena makes with the easy path:
        /// unit_walktobl(pd, item, 1, 1) (rA pet.cpp:1855), whose destination is the cell next to the item on the pet's side
        /// (unit_can_reach_bl, unit.cpp:3063-3083: item - sign(item - pet)). Pets follow their master with A* (pet.cpp:1794, flag 0).
        /// Other easy-path walks cannot be told apart from the packets (a player's slave going back to it, mob.cpp:1558;
        /// battleground guardians following allies, mob.cpp:2050; running and monster escapes, unit.cpp:1090, 1154, straight lines
        /// where A* gives the same steps unless a diagonal passes a wall corner): they use A*.
        /// </summary>
        private bool IsPetLootWalk(Unit u, int x0, int y0, int x1, int y1)
        {
            if (u.ObjectType != 7 || FloorItemCells == null) return false;
            foreach (var c in FloorItemCells())
                if (c.x - System.Math.Sign(c.x - x0) == x1 && c.y - System.Math.Sign(c.y - y0) == y1 && (c.x != x1 || c.y != y1)) return true;
            return false;
        }

        /// <summary>ZC_SPRITE_CHANGE for any unit, ourselves included. Players get the changed field and a full layer rebuild;
        /// NPCs/monsters only react to LOOK_BASE (class change → new body sprite).</summary>
        private void OnSpriteChange(uint aid, int type, int val, int val2)
        {
            _units.TryGetValue(aid, out var u);
            if (SelfId != 0 && aid == SelfId)
            {
                // the persistent record is updated even while Self does not exist (between Clear and CreateSelf)
                if (_selfLook == null) { if (u == null) return; _selfLook = u.Look.Clone(); }
                _selfLook.ApplyChange(type, val, val2);
                if (u != null && !u.Look.SameAs(_selfLook)) u.SetLook(_selfLook, _assets);
                return;
            }
            if (u == null) return;
            if (u.IsPlayerType || (type == PlayerLook.LookBase && RoNames.IsPlayer(val)))
            {
                var look = u.Look.Clone(); // Unit.ApplyLook keeps Look.Job/Sex/Hair equal to the unit's
                if (look.ApplyChange(type, val, val2)) u.SetLook(look, _assets);
                return;
            }
            if (type == PlayerLook.LookBase && u.Job != val) { u.Job = val; u.RefreshClassFlags(); u.ApplyLook(_assets); }
        }

        /// <summary>A unit's e_option bits changed (hide/cloak, Sight/Ruwach auras, cart…): (unit, old, new).</summary>
        public event System.Action<Unit, int, int> OptionChanged;
        /// <summary>
        /// e_option bits (status.hpp:3024-3060). Hidden, cloaked, chase-walking or invisible units: the server keeps a unit that hides
        /// in view in everyone's list (0x0229 goes to the whole area, clif.cpp:4378-4432) and keeps sending its moves to everyone
        /// (clif_ally_only, clif.cpp:2085-2086, holds them back from enemies only when update_enemy_position is off, clif.cpp:451-453;
        /// it is on by default, conf/battle/client.conf:123), and roBrowserLegacy then draws it with alpha 0 (EntityState.js:431-449; black with
        /// Maya Purple's intravision, not done here). Client wave 3 (w3status): other units follow that (alpha 0, no name plate, not
        /// pointable); our own character stays translucent (0.45, the earlier Loki look) so the player still sees where they are.
        /// the design chose invisible over faint for others (2026-09-30): as the official client, and hidden enemies stay hidden in PvP / WoE.
        /// </summary>
        private void ApplyOption(Unit u, int option)
        {
            const int Hide = 0x2, Cloak = 0x4, Invisible = 0x40, ChaseWalk = 0x4000;
            // client wave 4 (w4units): Camouflage / Stealth Field / Doram hide hide a unit like the option bits (rA status.cpp:2327), and our
            // own intravision (Maya Purple) draws other hidden units black instead of invisible — never GM invisibility (rB
            // EntityState.js:431-449: INVISIBLE always alpha 0); see Sprites/UnitEfstLooks.cs
            bool gmInvisible = (option & Invisible) != 0;
            bool ghost = (option & (Hide | Cloak | Invisible | ChaseWalk)) != 0 || UnitEfstLooks.IsHiddenByStatus(u);
            bool black = ghost && !u.IsSelf && !gmInvisible && UnitEfstLooks.Intravision;
            u.Sprite.SetTint(ActorSprite.TintFactor.Effect, black ? new Color(0f, 0f, 0f, 1f) : new Color(1f, 1f, 1f, !ghost ? 1f : u.IsSelf ? 0.45f : 0f));
            if (!u.IsSelf) SetUnseen(u, ghost);
            int old = u.Option; u.Option = option;
            if (((old ^ option) & UnitLooks.LookMask) != 0 && u.IsPlayerType) u.ApplyLook(_assets); // mounting / dismounting swaps the body (client wave 2); costume outfits, the orc head, the dragon's colour (w4units, Sprites/UnitLooks.cs)
            if (old != option) OptionChanged?.Invoke(u, old, option);
        }

        /// <summary>w3status: a unit drawn with alpha 0 also loses its name plate and its pointer collider (the gaze skips it: Targeting).</summary>
        private static void SetUnseen(Unit u, bool unseen)
        {
            u.Unseen = unseen; // w4units: also true while drawn black by intravision (not pointable either)
            var plate = u.transform.Find("overhead"); // the Overhead's canvas child (UI/Overhead.cs Build); the component itself sits on the unit
            if (plate != null && plate.gameObject.activeSelf == unseen) plate.gameObject.SetActive(!unseen);
            var col = u.GetComponent<BoxCollider>(); if (col != null && col.enabled == unseen) col.enabled = !unseen;
        }

        /// <summary>Client wave 4 (w4units): the unit's visibility again (a hiding status started / ended, our intravision changed).</summary>
        public void RefreshVisibility(Unit u) { if (u != null) ApplyOption(u, u.Option); }
        public void RefreshAllVisibility() { foreach (var u in _units.Values) if (u != null) ApplyOption(u, u.Option); }

        /// <summary>w3status: opt1 / opt2 / opt3 of an entry packet (bodyState / healthState / virtue: clif.cpp:1083, 1111-1112) → UnitState.</summary>
        private static void ApplyStates(Unit u, int opt1, int opt2, int opt3)
        {
            var st = UnitState.Of(u);
            st.SetBody(opt1 & 0xFFFF); st.SetHealth(opt2 & 0xFFFF); st.SetVirtue(opt3);
        }

        // ------------------------------------------------------------------ client wave 3 (w3social): ZC_NPCSPRITE_CHANGE 0x01b0
        /// <summary>A 0x01b0 was applied (smoke): unit id, new class, true = the unit was rebuilt (its kind changed).</summary>
        public event System.Action<uint, int, bool> W3SocialReclassed;

        /// <summary>
        /// ZC_NPCSPRITE_CHANGE 0x01b0 GID.L type.B class.L (rA packets.hpp:1464-1470, clif_class_change clif.cpp:1469-1494: type is
        /// always 0 "Unused"; never sent with a player class, pcdb_checkid refuses it at clif.cpp:1472-1475). Senders in e985006: the
        /// script command classchange (script.cpp:15163-15196, to the area or to one player with bc_self: the lasagna NPCs spawn as
        /// HIDDEN_WARP_NPC and turn into a visible NPC for one player, lasagna_npcs.txt:404/447/540), a monster's class change
        /// (mob_class_change mob.cpp:3803-3846: it is stopped first, so no walk is lost) and a homunculus evolving / mutating
        /// (homunculus.cpp:629, 679). roBrowserLegacy (a reimplementation) only sets entity.job = value (Entity.js:1388-1400).
        /// Loki draws the unit as if it had appeared with the new class: the same kind (NPC, monster, companion) swaps its body sprite in
        /// place (a homunculus keeps its bars, a monster its HP bar and ailments); a change of kind, or to or from the classes Loki draws
        /// as a portal / door (45, 139) or not at all (111, 2337), rebuilds the unit at its cell with its name and option bits, as a fresh
        /// spawn of that class would look (the name plate, pointer collider and portal marker all depend on the class:
        /// Unit.SetObjectType / SetupInteraction).
        /// </summary>
        private void W3SocialReclass(Unit u, int cls)
        {
            if (u.Job == cls) { W3SocialReclassed?.Invoke(u.Id, cls, false); return; }
            // the classes Loki draws specially (a portal 45, a door 139, nothing 111 / 2337) get a new unit even within one kind: the portal
            // marker and the "Portal" / "Door" plate depend on the class (Unit.SetupInteraction)
            bool Special(int c) => c == Unit.WarpClass || c == Unit.HiddenWarpClass || c == Unit.HiddenNpcClass || c == 2337;
            if (!Special(u.Job) && !Special(cls) && W3SocialKindOf(cls, u.ObjectType) == u.Kind)
            {
                u.Job = cls; u.RefreshClassFlags(); u.ApplyLook(_assets);
                W3SocialReclassed?.Invoke(u.Id, cls, false);
                return;
            }
            uint id = u.Id; string name = u.Name; int objecttype = u.ObjectType, option = u.Option; int dir = u.Sprite != null ? u.Sprite.Direction : 0;
            var cell = Vector2Int.RoundToInt(u.PositionAt(Time.time)); uint guild = u.GuildId; int emblem = u.GuildEmblem;
            _units.Remove(id); Destroy(u.gameObject);
            var n = Ensure(id, cls, 0, 0, objecttype, name, null, guild, emblem);
            n.SetCell(cell.x, cell.y, dir);
            if (option != 0) ApplyOption(n, option); // a hidden / cloaked unit stays so
            W3SocialReclassed?.Invoke(id, cls, true);
        }

        /// <summary>The kind Unit.SetObjectType gives a unit of class <paramref name="cls"/> sent with <paramref name="objecttype"/> (Unit.cs:108-122).</summary>
        private static Unit.UnitKind W3SocialKindOf(int cls, int objecttype)
        {
            if (cls == Unit.WarpClass || cls == Unit.HiddenWarpClass) return Unit.UnitKind.Warp;
            if (cls == Unit.HiddenNpcClass || cls == 2337) return Unit.UnitKind.Other;
            switch (objecttype)
            {
                case 0x0: return Unit.UnitKind.Player;
                case 0x1: case 0x6: case 0xC: return Unit.UnitKind.Npc;
                case 0x5: case 0xD: case 0xE: return Unit.UnitKind.Monster;
                case 0x7: case 0x8: case 0x9: case 0xA: return Unit.UnitKind.Companion;
                default: return Unit.UnitKind.Other;
            }
        }

        /// <summary>Unit packet / ZC_CHANGE_DIRECTION head direction: 0 straight, 1 and 2 turned (rAthena sd->head_dir).</summary>
        private static int HeadDirOf(int headDir) => headDir < 0 ? 0 : headDir > 2 ? 2 : headDir;

        /// <summary>Monster HP bars only while damaged (as the client does); -1 values mean "unknown".</summary>
        private static void ApplyHp(Unit u, int hp, int max)
        {
            if (u.Overhead == null || u.Kind != Unit.UnitKind.Monster) return;
            if (max <= 0 || hp < 0 || hp >= max) u.Overhead.SetHp(-1f);
            else u.Overhead.SetHp(hp / (float)max);
        }

        public static (int x, int y, int dir) UnpackPos(byte[] b)
        {
            int x = (b[0] << 2) | (b[1] >> 6);
            int y = ((b[1] & 0x3F) << 4) | (b[2] >> 4);
            return (x, y, b[2] & 0x0F);
        }
        /// <summary>MoveData with the sub-cell byte 5: (sx0 &lt;&lt; 4) | sy0 (rA clif.cpp:181-190 WBUFPOS2).</summary>
        public static (int x0, int y0, int x1, int y1, int sx0, int sy0) UnpackMoveSub(byte[] b)
        {
            var (x0, y0, x1, y1) = UnpackMove(b);
            return (x0, y0, x1, y1, b[5] >> 4, b[5] & 0x0F);
        }
        public static (int x0, int y0, int x1, int y1) UnpackMove(byte[] b)
        {
            int x0 = (b[0] << 2) | (b[1] >> 6);
            int y0 = ((b[1] & 0x3F) << 4) | (b[2] >> 4);
            int x1 = ((b[2] & 0x0F) << 6) | (b[3] >> 2);
            int y1 = ((b[3] & 0x03) << 8) | b[4];
            return (x0, y0, x1, y1);
        }
    }
}
