using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Loki.IO;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Pets, homunculi and mercenaries (research/pet_homun_merc_packets.md; rAthena e985006, PACKETVER 20211103) and the companion window.
    /// Pet: taming 0x019e → pointer pick → 0x019f → 0x01a0; hatching 0x01a6 egg list → 0x01a7 (only right after 0x01a6); 0x01a2 info;
    ///   0x01a4 state (0 = this unit is my pet, 1 intimacy 0-1000, 2 hunger 0-100, 3 accessory, 4 performance, 6 egg update); 0x01a3 feed;
    ///   0x01aa emotion / talk; menu 0x01a1 (1 feed, 2 perform, 3 return to egg, 4 unequip); rename 0x01a5; evolution 0x09fb → 0x09fc;
    ///   auto-feed 0x02d8 → 0x02d9 (type 2); accessory = equip 0x0998.
    /// Homunculus: 0x0230 state 0 carries its unit id (1 intimacy/100, 2 hunger), 0x0ba4 info (85 B, EXP as Q), 0x0ba5 HP/SP/EXP (Q),
    ///   0x0235 / 0x0239 skills, 0x022f feed; feed 0x022d {0, 1} (command 2 DELETES it: never sent), rename 0x0231, skills 0x0438 / 0x0366 /
    ///   0x0112; call / rest / resurrect = skills 243 / 244 / 247. Dies with 0x0080 type 1 on its id.
    /// Mercenary: 0x029b init (80 B, contract end as a Unix time), 0x029d skills, 0x02a2 changes; dismiss 0x029f {2}; leaves with 0x0080.
    /// Neither the homunculus nor the mercenary has server AI: Loki follows (0x0234), attacks (0x0233) and moves (0x0232) them itself
    /// (the official default AI, or the player's custom AI script: Companions.W4Ai.cs / W5Ai.cs).
    /// </summary>
    public sealed partial class Companions
    {
        public sealed class CSkill { public int Id, Inf, Level, Sp, Range; public string Name = ""; public bool Upgradable; }
        public event Action Changed;
        /// <summary>A companion unit id changed state (own pet / homunculus / mercenary identified, HP changed): redecorate that unit.</summary>
        public event Action<uint> UnitChanged;

        // ---------------- pet
        public uint PetId { get; private set; }
        public bool HasPet => PetId != 0;
        public string PetName = ""; public bool PetRenamed, PetInfoKnown, PetAutoFeed; public int PetLevel, PetHunger = -1, PetIntimacy = -1, PetAccessory, PetClass;
        public bool CaptureMode => Time.time < _captureUntil;
        private float _captureUntil, _petInitAt, _petEggUntil, _mercGoneUntil;
        /// <summary>Is this unit one of mine (pet, homunculus or mercenary)?</summary>
        public bool Owns(uint id) => id != 0 && (id == PetId || id == HomId || id == MercId || id == ElemId); // + the elemental (w4units)
        /// <summary>Eggs the server offered in 0x01a6 (client inventory indices); valid until one is chosen.</summary>
        public readonly List<int> EggChoices = new List<int>();
        public bool EggListOpen { get; private set; }
        public int LastCaptureResult = -1, LastPetFeed = -1, LastEvolution = -1, LastPerformance = -1, LastHomFeed = -1;

        // ---------------- homunculus
        public uint HomId { get; private set; }
        public bool HomOut { get; private set; }
        public string HomName = ""; public int HomFlags, HomLevel, HomHunger = -1, HomIntimacy = -1, HomAtk, HomMatk, HomHit, HomCrit, HomDef, HomMdef, HomFlee, HomAspd, HomSkillPoints, HomRange;
        public long HomHp, HomMaxHp, HomSp, HomMaxSp, HomExp, HomNextExp;
        public bool HomKnown, HomDead, HomAutoFeed;
        public bool HomRenamed => (HomFlags & 1) != 0;
        public bool HomResting => (HomFlags & 2) != 0;
        public bool HomActive => HomOut && !HomDead;
        public readonly List<CSkill> HomSkills = new List<CSkill>();

        // ---------------- mercenary
        public uint MercId { get; private set; }
        public bool HasMerc => MercId != 0;
        public string MercName = ""; public int MercLevel, MercAtk, MercMatk, MercHit, MercCrit, MercDef, MercMdef, MercFlee, MercAspd, MercFaith, MercRange;
        public long MercHp, MercMaxHp, MercSp, MercMaxSp, MercExpire, MercCalls, MercKills;
        public readonly List<CSkill> MercSkills = new List<CSkill>();

        private readonly RoSession _s; private readonly Inventory _inv; private readonly ListWindow _win; private readonly Func<Camera> _head;
        private readonly Action<string, string> _chat; private readonly Func<Sprites.Unit> _self; private readonly Func<uint, Sprites.Unit> _unit;
        public Action<uint, int> ShowEmotion; public Action<string> Toast;
        /// <summary>The gaze-locked monster (for "attack" and targeted companion skills), or null.</summary>
        public Func<Sprites.Unit> GazedMonster;

        public Companions(RoSession s, Inventory inv, ListWindow win, Func<Camera> head, Func<Sprites.Unit> self, Func<uint, Sprites.Unit> unit, Action<string, string> chat)
        {
            _s = s; _inv = inv; _win = win; _head = head; _self = self; _unit = unit; _chat = chat;
            _win.OnCloseClicked = Hide;
            _inv.Changed += () => { if (_win.Visible) Refresh(); };
        }

        /// <summary>Another character entered the game: forget the previous one's companions.</summary>
        public void ResetForCharacter()
        {
            PetId = 0; PetInfoKnown = false; PetName = ""; PetHunger = PetIntimacy = -1; PetAccessory = 0; EggChoices.Clear(); EggListOpen = false; _captureUntil = 0;
            HomId = 0; HomOut = false; HomKnown = false; HomDead = false; HomFlags = 0; HomSkills.Clear();
            MercId = 0; MercSkills.Clear(); W4AiReset(); ElemReset(); _win.Hide(); Changed?.Invoke();
        }

        /// <summary>Map change: the server re-sends every companion after LoadEndAck (pet 0x01a4 type 0, homunculus 0x0230 state 0, mercenary 0x029b).</summary>
        public void OnMapChange() { HomOut = false; W4AiReset(); _captureUntil = 0; }

        // ------------------------------------------------------------------ data (pet_db / homunculus_db copies, tools/clientdata/build_companion_tables.py)
        private static Dictionary<string, object> _pets, _homs;
        private static Dictionary<string, object> Load(string file)
        {
            try { return MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "data", "companions", file)))); }
            catch (Exception e) { Debug.LogWarning($"[Companions] {file}: {e.Message}"); return new Dictionary<string, object>(); }
        }
        public static Dictionary<string, object> PetData(int mob) { if (_pets == null) _pets = Load("pets.json"); return _pets.TryGetValue(mob.ToString(), out var o) ? MiniJson.AsObject(o) : null; }
        public static Dictionary<string, object> HomData(int cls) { if (_homs == null) _homs = Load("homun.json"); return _homs.TryGetValue(cls.ToString(), out var o) ? MiniJson.AsObject(o) : null; }
        private static HashSet<int> _eggIds;
        public static bool IsEgg(int item)
        {
            if (_eggIds == null) { _eggIds = new HashSet<int>(); if (_pets == null) _pets = Load("pets.json"); foreach (var v in _pets.Values) { var d = MiniJson.AsObject(v); if (d != null) _eggIds.Add(MiniJson.Int(d["egg"])); } }
            return _eggIds.Contains(item);
        }
        private static string ItemName(int id) => id > 0 ? GameDb.GetItem(id).Name : "?";

        // w4lang: the bands are only shown (this window, the wrist menu's Companions page), so they come in the current language
        public static string HungerBand(int h) => h < 0 ? "?" : h <= 10 ? Lang.T("~hunger~Very hungry") : h <= 25 ? Lang.T("~hunger~Hungry") : h <= 75 ? Lang.T("~hunger~Neutral") : h <= 90 ? Lang.T("~hunger~Satisfied") : Lang.T("~hunger~Stuffed");
        public static string PetIntimacyBand(int i) => i < 0 ? "?" : i == 0 ? Lang.T("~intimacy~Lost") : i < 100 ? Lang.T("~intimacy~Awkward") : i < 250 ? Lang.T("~intimacy~Shy") : i < 750 ? Lang.T("~intimacy~Neutral") : i < 910 ? Lang.T("~intimacy~Cordial") : Lang.T("~intimacy~Loyal");
        public static string HomIntimacyBand(int i) => i < 0 ? "?" : i <= 3 ? Lang.T("~intimacy~Hate with passion") : i <= 10 ? Lang.T("~intimacy~Hate") : i <= 100 ? Lang.T("~intimacy~Awkward") : i <= 250 ? Lang.T("~intimacy~Shy") : i <= 750 ? Lang.T("~intimacy~Neutral") : i <= 910 ? Lang.T("~intimacy~Cordial") : Lang.T("~intimacy~Loyal");

        // ------------------------------------------------------------------ packets
        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            if (HandleW4Elemental(id, b, o, n)) return true; // 0x081d / 0x081e (w4units: Companions.W4Elemental.cs)
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                // ---- pets
                case 0x019e: // ZC_START_CAPTURE
                    _captureUntil = Time.time + 30f;
                    _chat(Lang.T("Point at the monster (5 cells) and pull."), "ffd060"); Toast?.Invoke(Lang.T("Point at the monster to tame"));
                    Changed?.Invoke(); return true;
                case 0x01a0: // ZC_TRYCAPTURE_MONSTER result.B
                {
                    r.Skip(2); int res = r.U8(); LastCaptureResult = res; _captureUntil = 0;
                    _chat(res == 1 ? Lang.T("Tamed! Its egg is in your inventory.") : Lang.T("The taming failed."), res == 1 ? "a8e6a3" : "ffb070");
                    Changed?.Invoke(); return true;
                }
                case 0x01a6: // ZC_PETEGG_LIST len.W {index.W}
                {
                    r.Skip(4); EggChoices.Clear();
                    while (r.Remaining >= 2) EggChoices.Add(r.U16());
                    EggListOpen = true; Debug.Log($"[Pet] egg list: {EggChoices.Count}");
                    Show(Page.Pet); return true;
                }
                case 0x01a2: // ZC_PROPERTY_PET name[24] modified.B level.W fullness.W relationship.W accessory.W job.W
                {
                    r.Skip(2); PetName = r.Str(24); PetRenamed = r.U8() != 0; PetLevel = r.U16(); PetHunger = r.U16(); PetIntimacy = r.U16(); PetAccessory = r.U16(); PetClass = r.U16();
                    PetInfoKnown = true; Debug.Log($"[Pet] '{PetName}' class {PetClass} lv {PetLevel} hunger {PetHunger} intimacy {PetIntimacy} accessory {PetAccessory}");
                    Changed?.Invoke(); Refresh(); return true;
                }
                case 0x01a4: // ZC_CHANGESTATE_PET type.B id.L data.L
                {
                    r.Skip(2); int type = r.U8(); uint pid = r.U32(); int data = r.S32();
                    switch (type)
                    {
                        case 0: PetId = pid; EggListOpen = false; _petInitAt = Time.time; Debug.Log($"[Pet] my pet is unit {pid}"); UnitChanged?.Invoke(pid); break;
                        case 1: if (pid == PetId) PetIntimacy = data; break;
                        case 2: if (pid == PetId) PetHunger = data; break;
                        case 3: if (pid == PetId) PetAccessory = data; PetAccessorySeen?.Invoke(pid, data); break; // the accessory's view id (0 = none), not its item id (clif.cpp:8294-8296); any pet in view (area / on entry: clif.cpp:1752-1757, 5130-5135) → its look (w4units)
                        case 4: if (pid == PetId) { LastPerformance = data; _chat(data >= 4 ? Lang.F("{0} performs its special trick!", PetName) : Lang.F("{0} performs a trick!", PetName), "c8cbd6"); } break;
                        // hatching sends 0 → 5 → 6 in one burst; alone, 6 means "back into the egg" and the pet's 0x0080 type 0 follows (pet_return_egg, pet.cpp:952-974)
                        case 6: EggListOpen = false; if (pid == PetId && PetId != 0 && Time.time - _petInitAt > 1f) _petEggUntil = Time.time + 3f; break;
                    }
                    Changed?.Invoke(); Refresh(); return true;
                }
                case 0x01a3: // ZC_FEED_PET result.B item.L
                {
                    r.Skip(2); int res = r.U8(); int item = (int)r.U32(); LastPetFeed = res;
                    _chat(res == 1 ? Lang.F("You fed {0} {1}.", PetName, ItemName(item)) : Lang.F("You have no {0} to feed your pet.", ItemName(item)), res == 1 ? "a8e6a3" : "ffb070");
                    Changed?.Invoke(); return true;
                }
                case 0x01aa: // ZC_PET_ACT id.L data.L: an emotion, or a pettalktable line code (≥ 100: not drawn)
                {
                    r.Skip(2); uint pid = r.U32(); int data = r.S32();
                    if (data >= 0 && data < 100) ShowEmotion?.Invoke(pid, data);
                    return true;
                }
                case 0x09fc: // ZC_PET_EVOLUTION_RESULT result.L
                {
                    r.Skip(2); int res = r.S32(); LastEvolution = res;
                    string m = res == 6 ? Lang.T("Your pet evolved!") : res == 5 ? Lang.T("Evolution needs a Loyal pet without an accessory.") : res == 4 ? Lang.T("Some of the evolution materials are missing.") :
                               res == 2 ? Lang.T("That isn't a pet egg.") : res == 1 ? Lang.T("Your pet can't evolve into that.") : Lang.T("The evolution failed.");
                    _chat(m, res == 6 ? "a8e6a3" : "ffb070"); Toast?.Invoke(m);
                    Changed?.Invoke(); return true;
                }
                case 0x02d9: // ZC_CONFIG type.L flag.L (2 pet auto-feed, 3 homunculus auto-feed)
                {
                    r.Skip(2); int type = r.S32(); bool on = r.S32() != 0;
                    if (type != 2 && type != 3) return false; // 5 = show costumes (LokiGame), 0/1 equipment window / call
                    if (type == 2) PetAutoFeed = on; else HomAutoFeed = on;
                    Changed?.Invoke(); Refresh(); return true;
                }
                // ---- homunculus
                case 0x0230: // ZC_CHANGESTATE_MER type.B state.B id.L data.L
                {
                    r.Skip(3); int state = r.U8(); uint hid = r.U32(); int data = r.S32();
                    if (state == 0) { HomId = hid; HomOut = true; HomDead = false; Debug.Log($"[Homun] my homunculus is unit {hid}"); UnitChanged?.Invoke(hid); }
                    else if (state == 1) HomIntimacy = data;
                    else if (state == 2) HomHunger = data;
                    Changed?.Invoke(); Refresh(); return true;
                }
                case 0x0ba4: // ZC_PROPERTY_HOMUN (85)
                {
                    r.Skip(2); HomName = r.Str(24); HomFlags = r.U8(); HomLevel = r.U16(); HomHunger = r.U16(); HomIntimacy = r.U16();
                    HomAtk = r.U16(); HomMatk = r.U16(); HomHit = r.U16(); HomCrit = r.U16(); HomDef = r.U16(); HomMdef = r.U16(); HomFlee = r.U16(); int aspd = r.U16(); if (aspd > 0) HomAspd = aspd;
                    HomHp = r.S32(); HomMaxHp = r.S32(); HomSp = r.S32(); HomMaxSp = r.S32(); HomExp = r.S64(); HomNextExp = r.S64(); HomSkillPoints = r.U16(); HomRange = r.U16();
                    HomKnown = true; HomDead = (HomFlags & 4) == 0; // bit 2 = alive (clif.cpp:1839)
                    if (HomResting) HomOut = false;
                    Changed?.Invoke(); Refresh(); if (HomId != 0) UnitChanged?.Invoke(HomId); return true;
                }
                case 0x0ba5: // ZC_HO_PAR_CHANGE2 type.W value.Q (1 EXP, 5 HP, 7 SP)
                {
                    r.Skip(2); int type = r.U16(); long v = r.S64();
                    if (type == 1) HomExp = v; else if (type == 5) HomHp = v; else if (type == 7) HomSp = v;
                    Changed?.Invoke(); Refresh(); if (type == 5 && HomId != 0) UnitChanged?.Invoke(HomId); return true;
                }
                case 0x0235: // ZC_HOSKILLINFO_LIST len.W {id.W inf.W ?.W level.W sp.W range.W name[24] up.B}
                {
                    r.Skip(4); HomSkills.Clear();
                    while (r.Remaining >= 37) { var k = new CSkill { Id = r.U16(), Inf = r.U16() }; r.Skip(2); k.Level = r.U16(); k.Sp = r.U16(); k.Range = r.U16(); k.Name = r.Str(24); k.Upgradable = r.U8() != 0; HomSkills.Add(k); }
                    Changed?.Invoke(); Refresh(); return true;
                }
                case 0x0239: // ZC_HOSKILLINFO_UPDATE id.W level.W sp.W range.W up.B
                {
                    r.Skip(2); int sid = r.U16(); var k = HomSkills.FirstOrDefault(x => x.Id == sid);
                    if (k != null) { k.Level = r.U16(); k.Sp = r.U16(); k.Range = r.U16(); k.Upgradable = r.U8() != 0; }
                    Changed?.Invoke(); Refresh(); return true;
                }
                case 0x022f: // ZC_FEED_MER result.B item.L
                {
                    r.Skip(2); int res = r.U8(); int item = (int)r.U32(); LastHomFeed = res;
                    _chat(res == 1 ? Lang.F("You fed {0} {1}.", HomName, ItemName(item)) : Lang.F("You have no {0} for your homunculus.", ItemName(item)), res == 1 ? "a8e6a3" : "ffb070");
                    Changed?.Invoke(); return true;
                }
                // ---- mercenary
                case 0x0291: // ZC_MSG: peek only (LokiGame prints it). 1266 contract over / 1267 killed come right before the mercenary's 0x0080 type 0 (mercenary.cpp:291-321)
                {
                    r.Skip(2); int m = r.U16();
                    if ((m == 1266 || m == 1267) && HasMerc) _mercGoneUntil = Time.time + 3f;
                    return false;
                }
                case 0x029b: // ZC_MER_INIT (80)
                {
                    r.Skip(2); MercId = r.U32(); MercAtk = r.U16(); MercMatk = r.U16(); MercHit = r.U16(); MercCrit = r.U16(); MercDef = r.U16(); MercMdef = r.U16(); MercFlee = r.U16(); MercAspd = r.U16();
                    MercName = r.Str(24); MercLevel = r.U16(); MercHp = r.S32(); MercMaxHp = r.S32(); MercSp = r.S32(); MercMaxSp = r.S32(); MercExpire = r.U32(); MercFaith = r.U16(); MercCalls = r.U32(); MercKills = r.U32(); MercRange = r.U16();
                    Debug.Log($"[Merc] '{MercName}' unit {MercId} lv {MercLevel} contract ends {MercExpire}");
                    UnitChanged?.Invoke(MercId); Changed?.Invoke(); Refresh(); return true;
                }
                case 0x029d: // ZC_MER_SKILLINFO_LIST len.W {id.W inf.L level.W sp.W range.W name[24] up.B}
                {
                    r.Skip(4); MercSkills.Clear();
                    while (r.Remaining >= 37) { var k = new CSkill { Id = r.U16(), Inf = r.S32(), Level = r.U16(), Sp = r.U16(), Range = r.U16(), Name = r.Str(24) }; k.Upgradable = r.U8() != 0; MercSkills.Add(k); }
                    Changed?.Invoke(); Refresh(); return true;
                }
                case 0x02a2: // ZC_MER_PAR_CHANGE var.W value.L
                {
                    r.Skip(2); int v = r.U16(); int val = r.S32();
                    switch (v)
                    {
                        case 5: MercHp = val; break; case 6: MercMaxHp = val; break; case 7: MercSp = val; break; case 8: MercMaxSp = val; break;
                        case 41: MercAtk = val; break; case 43: MercMatk = val; break; case 45: MercDef = val; break; case 47: MercMdef = val; break;
                        case 49: MercHit = val; break; case 52: MercCrit = val; break; case 53: MercAspd = val; break; case 165: MercFlee = val; break;
                        case 189: MercKills = val; break; case 190: MercFaith = val; break;
                    }
                    Changed?.Invoke(); Refresh(); if ((v == 5 || v == 6) && MercId != 0) UnitChanged?.Invoke(MercId); return true;
                }
                default: return false;
            }
        }

        /// <summary>
        /// EntityManager's vanish notice. Type 0 (out of sight) is ambiguous: a companion left more than 14 cells behind goes out of sight and the server
        /// warps it back 3 s later with a plain spawn (unit.cpp:365-427, unit_warp), so a type 0 only ends ownership right after the packet that
        /// announces it: 0x01a4 type 6 for a pet going back into its egg, 0x0291 1266/1267 or our own dismissal for a mercenary. A resting homunculus
        /// is known from 0x0ba4's flags (sent before its 0x0080); type 1 on the homunculus is its death.
        /// </summary>
        public void OnVanished(uint id, int type)
        {
            if (id == 0) return;
            W4AiOnVanished(id); ElemOnVanished(id, type);
            if (id == PetId && Time.time < _petEggUntil) { PetId = 0; _petEggUntil = 0; Debug.Log("[Pet] back in its egg"); Changed?.Invoke(); Refresh(); }
            if (id == HomId && type == 1) { HomOut = false; HomDead = true; _chat(HomName.Length > 0 ? Lang.F("{0} died. Resurrect Homunculus brings it back.", HomName) : Lang.T("Your homunculus died. Resurrect Homunculus brings it back."), "ff8a7a"); Changed?.Invoke(); Refresh(); }
            if (id == MercId && Time.time < _mercGoneUntil) { ClearMerc(); }
        }
        /// <summary>The owner died: pc_dead always deletes the mercenary (pc.cpp:9852-9853; its 0x0080 may come before our own death packet).</summary>
        public void OnSelfDied() { if (HasMerc) ClearMerc(); }
        private void ClearMerc() { Debug.Log($"[Merc] '{MercName}' left"); MercId = 0; MercSkills.Clear(); _mercGoneUntil = 0; Changed?.Invoke(); Refresh(); }

        // ------------------------------------------------------------------ sending
        public void Capture(uint monster) { _captureUntil = 0; _s.SendPacket(new PacketWriter(0x019f).U32(monster).ToArray()); Debug.Log($"[Pet] try capture {monster}"); }
        public void Hatch(int invIndex) { if (!EggListOpen) return; EggListOpen = false; _s.SendPacket(new PacketWriter(0x01a7).U16((ushort)invIndex).ToArray()); Debug.Log($"[Pet] hatch egg {invIndex}"); }
        public void PetCommand(int cmd) { _s.SendPacket(new PacketWriter(0x01a1).U8((byte)cmd).ToArray()); Debug.Log($"[Pet] command {cmd}"); }
        public void RenamePet(string name) { if (!string.IsNullOrWhiteSpace(name)) _s.SendPacket(new PacketWriter(0x01a5).Str(name.Trim(), 24).ToArray()); }
        public void SetAutoFeed(int type, bool on) { _s.SendPacket(new PacketWriter(0x02d8).U32((uint)type).U32(on ? 1u : 0u).ToArray()); }
        public void EquipAccessory(int invIndex) { _s.Equip(invIndex, 0); } // 0x0998: the position is ignored for pet armour (clif.cpp:12143)
        /// <summary>0x09fb with no item list: the server takes the materials from pet_db (clif.cpp:21921-21934).</summary>
        public void Evolve(int targetEgg) { _s.SendPacket(new PacketWriter(0x09fb).U16(0).U32((uint)targetEgg).ToArrayWithLength()); Debug.Log($"[Pet] evolve into egg {targetEgg}"); }
        public void UseIncubator()
        {
            var inc = _inv.Items.Values.FirstOrDefault(e => e.ItemId == 643);
            if (inc == null) { _chat(Lang.T("Hatching needs a Pet Incubator."), "ffb070"); return; }
            _s.UseItem(inc.Index);
        }

        public void FeedHomunculus() { if (HomActive) _s.SendPacket(new PacketWriter(0x022d).U16(0).U8(1).ToArray()); }
        public void RenameHomunculus(string name) { if (HomActive && !string.IsNullOrWhiteSpace(name)) _s.SendPacket(new PacketWriter(0x0231).Str(name.Trim(), 24).ToArray()); }
        public void HomSkillUp(int skill) { _s.SendPacket(new PacketWriter(0x0112).U16((ushort)skill).ToArray()); }
        public void DismissMercenary() { if (!HasMerc) return; _mercGoneUntil = Time.time + 5f; _s.SendPacket(new PacketWriter(0x029f).U8(2).ToArray()); Debug.Log("[Merc] dismiss"); }

        /// <summary>A companion skill: self/support skills on the companion itself, attack skills on the gazed monster, ground skills at its cell.</summary>
        public void UseCompanionSkill(CSkill k, uint caster)
        {
            if (k == null || k.Level <= 0) return;
            var tgt = GazedMonster?.Invoke();
            if ((k.Inf & 2) != 0) { var c = tgt != null ? tgt.Cell : (_unit(caster)?.Cell ?? Vector2.zero); _s.UseSkillOnGround(k.Id, k.Level, Mathf.RoundToInt(c.x), Mathf.RoundToInt(c.y)); }
            else if ((k.Inf & 1) != 0) { if (tgt == null) { Toast?.Invoke(Lang.T("Look at a monster first.")); return; } _s.UseSkill(k.Id, k.Level, tgt.Id); }
            else _s.UseSkill(k.Id, k.Level, caster);
            Debug.Log($"[Companion] skill {k.Id} lv {k.Level} by {caster}");
        }

        // ---- the client-side AI (the server has none for homunculi and mercenaries): the official client's default AI.lua / AI_M.lua,
        //      ported in Game/AiCore/DefaultAi.cs (client wave 4, w4units; completed in wave 5), or the player's own AI\USER_AI script run
        //      by Loki's Lua (Companions.W5Ai.cs, client wave 5). The window's Attack / Standby give it the official commands (ATTACK_OBJECT_CMD, FOLLOW_CMD).
        /// <summary>The enemy the homunculus (else the mercenary) is after, 0 = none.</summary>
        public uint AssistTarget => W4AiEnemy();
        /// <summary>ATTACK_OBJECT_CMD to the homunculus and the mercenary (the official client's Alt + click on a target).</summary>
        public void Assist(uint target) { if (target != 0) W4AiCommand(W4AiCmd.AttackObject, target); }
        /// <summary>FOLLOW_CMD to both (the official [Standby] button / Alt+T: standby by the owner, pressed again: back to idle).</summary>
        public void Recall() => W4AiCommand(W4AiCmd.Follow, 0);
        /// <summary>CZ_REQUEST_MOVENPC: id.L + the 3-byte position (x 10 bits, y 10 bits, dir 4 bits, like CZ_REQUEST_MOVE).</summary>
        public void MoveTo(uint unit, int x, int y)
        {
            var w = new PacketWriter(0x0232).U32(unit);
            w.U8((byte)(x >> 2)); w.U8((byte)(((x & 3) << 6) | ((y >> 4) & 0x3F))); w.U8((byte)((y & 0xF) << 4));
            _s.SendPacket(w.ToArray());
        }

        public void Tick() => W4AiTick();

        // ------------------------------------------------------------------ the companion window
        public enum Page { Pet, Homunculus, Mercenary, Elemental }
        public Page Current { get; private set; } = Page.Pet;
        public bool Visible => _win.Visible;
        private static readonly string[] TabNames = { Lang.N("Pet"), Lang.N("Homunculus"), Lang.N("Mercenary"), Lang.N("Elemental") };
        private string _confirmKey; private float _confirmUntil;

        public void Show(Page p) { Current = p; _win.Input.SetText(""); _win.Open(_head()); if (p == Page.Pet && HasPet) PetCommand(0); Refresh(); }
        public void Hide() { _win.SetInput(false); _win.Hide(); }
        private bool Confirm(string key, string what)
        {
            if (_confirmKey == key && Time.time <= _confirmUntil) { _confirmKey = null; return true; }
            _confirmKey = key; _confirmUntil = Time.time + 4f; _chat(Lang.F("{0} — press again to confirm.", what), "ffd060"); Refresh();
            return false;
        }
        private string Armed(string key, string label) => _confirmKey == key && Time.time <= _confirmUntil ? Lang.T("Sure?") : label;
        private static string Bar(long v, long max) => max > 0 ? string.Format(Lang.Culture, "{0:N0} / {1:N0}", v, max) : "?"; // w4lang: numbers the current language's way

        public void Refresh()
        {
            if (!_win.Visible) return;
            var rows = new List<ListWindow.Row>();
            _win.SetTabs(Array.ConvertAll(TabNames, Lang.T), (int)Current, t => { Current = (Page)t; _win.Input.SetText(""); if (Current == Page.Pet && HasPet) PetCommand(0); Refresh(); });
            switch (Current)
            {
                case Page.Pet: RefreshPet(rows); break;
                case Page.Homunculus: RefreshHom(rows); break;
                case Page.Mercenary: RefreshMerc(rows); break;
                case Page.Elemental: RefreshElemental(rows); break; // w4units: Companions.W4Elemental.cs
            }
            _win.SetRows(rows);
        }

        private void RefreshPet(List<ListWindow.Row> rows)
        {
            _win.SetTitle(Lang.T("Pet"));
            if (EggListOpen)
            {
                _win.SetInput(false); _win.SetInfo(Lang.T("Choose the egg to hatch."));
                foreach (int idx in EggChoices)
                {
                    int i = idx; var e = _inv.Items.TryGetValue(idx, out var ee) ? ee : null;
                    int grade = e != null && e.Cards != null && e.Cards.Length > 2 ? e.Cards[2] : 0;
                    string g = grade == 1 ? Lang.T("~intimacy~Awkward") : grade == 2 ? Lang.T("~intimacy~Shy") : grade == 3 ? Lang.T("~intimacy~Neutral") : grade == 4 ? Lang.T("~intimacy~Cordial") : grade == 5 ? Lang.T("~intimacy~Loyal") : "";
                    rows.Add(new ListWindow.Row { Icon = e != null ? WristMenu.ItemIcon(e.Info) : null, Text = (e != null ? e.Info.Name : Lang.F("Egg #{0}", idx)) + (g.Length > 0 ? $"  <color=#9aa3b5>{g}</color>" : ""), A = Lang.T("Hatch"), OnA = () => Hatch(i) });
                }
                if (EggChoices.Count == 0) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>You carry no pet eggs.</color>") });
                _win.SetFooter((Lang.T("Close"), Hide));
                return;
            }
            if (!HasPet)
            {
                _win.SetInput(false); _win.SetInfo(Lang.T("No pet is out."));
                int eggs = _inv.Items.Values.Count(e => IsEgg(e.ItemId));
                rows.Add(new ListWindow.Row { Text = eggs > 1 ? Lang.F("You carry {0} pet eggs.", eggs) : eggs == 1 ? Lang.T("You carry 1 pet egg.") : Lang.T("<color=#9aa3b5>You carry no pet eggs.</color>"), A = Lang.T("Hatch"), OnA = UseIncubator, AEnabled = eggs > 0 && _inv.Items.Values.Any(e => e.ItemId == 643) });
                rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>Hatching needs a Pet Incubator. To tame a monster, use its taming item from the Items tab, then point at it.</color>") });
                _win.SetFooter((Lang.T("Close"), Hide));
                return;
            }
            var pd = PetData(PetClass);
            int food = pd != null ? MiniJson.Int(pd["food"]) : 0, equip = pd != null ? MiniJson.Int(pd["equip"]) : 0;
            bool canRename = !PetRenamed;
            _win.SetInput(canRename, Lang.T("new pet name"), null, () => { RenamePet(_win.Input.Text); _win.Input.SetText(""); }, null, 23);
            _win.SetInfo(canRename ? Lang.F("{0} — rename:", PetName) : $"{PetName}");
            rows.Add(new ListWindow.Row { Text = Lang.F("<color=#ffd060>{0}</color>  <color=#9aa3b5>Lv {1} {2}</color>", PetName, PetLevel, pd != null ? MiniJson.Str(pd["name"]) : ""), A = canRename ? Lang.T("Rename") : null, OnA = () => { RenamePet(_win.Input.Text); _win.Input.SetText(""); } });
            rows.Add(new ListWindow.Row
            {
                Text = (PetHunger > 90 ? Lang.F("Hunger {0}/100  <color=#9aa3b5>{1}</color>  <color=#ff8a7a>(feeding now lowers intimacy)</color>", PetHunger, HungerBand(PetHunger)) : Lang.F("Hunger {0}/100  <color=#9aa3b5>{1}</color>", PetHunger, HungerBand(PetHunger)))
                    + "\n" + Lang.F("<size=19>Food: {0} — you have {1}</size>", ItemName(food), _inv.Count(food)),
                A = Lang.T("Feed"), OnA = () => PetCommand(1), AEnabled = food > 0 && _inv.Count(food) > 0,
                B = pd != null && MiniJson.Bool(pd["autoFeed"]) ? (PetAutoFeed ? Lang.T("Auto: on") : Lang.T("Auto: off")) : null, OnB = () => SetAutoFeed(2, !PetAutoFeed),
            });
            rows.Add(new ListWindow.Row { Text = Lang.F("Intimacy {0}/1000  <color=#9aa3b5>{1}</color>", PetIntimacy, PetIntimacyBand(PetIntimacy)), A = Lang.T("Perform"), OnA = () => PetCommand(2) });
            var acc = equip > 0 ? _inv.Items.Values.FirstOrDefault(e => e.ItemId == equip) : null;
            rows.Add(new ListWindow.Row
            {
                Text = PetAccessory != 0 ? Lang.F("Accessory: {0}", ItemName(equip > 0 ? equip : PetAccessory)) : equip > 0 ? Lang.F("Accessory: none <color=#9aa3b5>({0} fits)</color>", ItemName(equip)) : Lang.T("<color=#9aa3b5>This pet takes no accessory.</color>"),
                A = PetAccessory != 0 ? Lang.T("Remove") : acc != null ? Lang.T("Equip") : null, OnA = () => { if (PetAccessory != 0) PetCommand(4); else if (acc != null) EquipAccessory(acc.Index); },
            });
            var evo = pd != null ? MiniJson.AsList(pd["evo"]) : null;
            if (evo != null)
                foreach (var eo in evo)
                {
                    var ev = MiniJson.AsObject(eo); int egg = MiniJson.Int(ev["egg"]); var tpd = PetData(MiniJson.Int(ev["target"]));
                    var items = MiniJson.AsList(ev["items"]); var parts = new List<string>(); bool have = true;
                    if (items != null) foreach (var it in items) { var p = MiniJson.AsList(it); int iid = MiniJson.Int(p[0]), amt = MiniJson.Int(p[1]); int got = _inv.Count(iid); if (got < amt) have = false; parts.Add($"{ItemName(iid)} {got}/{amt}"); }
                    bool ok = have && PetIntimacy >= 910 && PetAccessory == 0;
                    rows.Add(new ListWindow.Row { Text = (PetIntimacy < 910 ? Lang.F("Evolve into {0}  <color=#ff8a7a>(needs Loyal)</color>", tpd != null ? MiniJson.Str(tpd["name"]) : "?") : Lang.F("Evolve into {0}", tpd != null ? MiniJson.Str(tpd["name"]) : "?")) + $"\n<size=19>{string.Join(", ", parts)}</size>", A = Lang.T("Evolve"), OnA = () => Evolve(egg), AEnabled = ok });
                }
            _win.SetFooter((Armed("egg", Lang.T("To egg")), () => { if (Confirm("egg", Lang.F("Put {0} back into its egg", PetName))) PetCommand(3); }), (Lang.T("Close"), Hide));
        }

        private void RefreshHom(List<ListWindow.Row> rows)
        {
            _win.SetTitle(Lang.T("Homunculus"));
            if (!HomKnown && HomId == 0)
            {
                _win.SetInput(false); _win.SetInfo(Lang.T("No homunculus."));
                rows.Add(new ListWindow.Row { Text = Lang.F("Alchemists call one with Call Homunculus: a new one needs an Embryo (you have {0}), a resting one a Seed of Life (you have {1}).", _inv.Count(7142), _inv.Count(7140)) });
                _win.SetFooter((Lang.T("Call"), () => _s.UseSkill(243, 1, _s.AccountId)), (Lang.T("Close"), Hide));
                return;
            }
            string state = HomDead ? Lang.T("<color=#ff8a7a>dead</color>") : HomResting ? Lang.T("<color=#9aa3b5>resting</color>") : HomOut ? Lang.T("<color=#7fd67f>out</color>") : Lang.T("<color=#9aa3b5>not here</color>");
            _win.SetInput(HomActive && !HomRenamed, Lang.T("new name"), null, () => { RenameHomunculus(_win.Input.Text); _win.Input.SetText(""); }, null, 23);
            _win.SetInfo($"{HomName} — {state}");
            var cls = _unit(HomId)?.Job ?? 0; var hd = HomData(cls);
            double pct = HomNextExp > 0 ? HomExp * 100.0 / HomNextExp : 100;
            rows.Add(new ListWindow.Row { Text = (hd != null ? Lang.F("<color=#ffd060>{0}</color>  <color=#9aa3b5>Lv {1} {2} · EXP {3:0.0}%</color>", HomName, HomLevel, MiniJson.Str(hd["name"]), pct) : Lang.F("<color=#ffd060>{0}</color>  <color=#9aa3b5>Lv {1} · EXP {2:0.0}%</color>", HomName, HomLevel, pct)) + $"\n<size=19>HP {Bar(HomHp, HomMaxHp)} · SP {Bar(HomSp, HomMaxSp)}</size>", A = HomActive && !HomRenamed ? Lang.T("Rename") : null, OnA = () => { RenameHomunculus(_win.Input.Text); _win.Input.SetText(""); } });
            int food = hd != null ? MiniJson.Int(hd["food"]) : 537;
            rows.Add(new ListWindow.Row
            {
                Text = Lang.F("Hunger {0}/100  <color=#9aa3b5>{1}</color> · intimacy <color=#9aa3b5>{2}</color> ({3}/1000)", HomHunger, HungerBand(HomHunger), HomIntimacyBand(HomIntimacy), HomIntimacy)
                    + "\n" + Lang.F("<size=19>Food: {0} — you have {1}</size>", ItemName(food), _inv.Count(food)),
                A = Lang.T("Feed"), OnA = FeedHomunculus, AEnabled = HomActive && _inv.Count(food) > 0, B = HomAutoFeed ? Lang.T("Auto: on") : Lang.T("Auto: off"), OnB = () => SetAutoFeed(3, !HomAutoFeed),
            });
            rows.Add(new ListWindow.Row { Text = Lang.F("<size=19>Atk {0} · Matk {1} · Hit {2} · Crit {3} · Def {4} · Mdef {5} · Flee {6} · range {7}</size>", HomAtk, HomMatk, HomHit, HomCrit, HomDef, HomMdef, HomFlee, HomRange) + "\n" + Lang.F("Skill points {0}", HomSkillPoints) });
            rows.Add(W5AiRow(false)); // which AI runs, and its switch (client wave 5, w5ai: Companions.W5Ai.cs)
            foreach (var k in HomSkills)
            {
                var kk = k; var info = SkillDb.Get(k.Id);
                rows.Add(new ListWindow.Row
                {
                    Icon = WristMenu.SkillIcon(info), Text = k.Sp > 0 ? Lang.F("{0}  <color=#9aa3b5>Lv {1}/{2} · {3} SP</color>", info.Name ?? k.Name, k.Level, info.MaxLevel, k.Sp) : Lang.F("{0}  <color=#9aa3b5>Lv {1}/{2}</color>", info.Name ?? k.Name, k.Level, info.MaxLevel),
                    A = k.Upgradable ? Lang.T("Level up") : null, AEnabled = HomSkillPoints > 0, OnA = () => HomSkillUp(kk.Id),
                    B = k.Inf != 0 && k.Level > 0 ? Lang.T("Use") : null, BEnabled = HomActive, OnB = () => UseCompanionSkill(kk, HomId),
                });
            }
            if (HomDead) _win.SetFooter((Lang.T("Resurrect"), () => _s.UseSkill(247, 1, _s.AccountId)), (Lang.T("Close"), Hide));
            else if (!HomOut) _win.SetFooter((Lang.T("Call"), () => _s.UseSkill(243, 1, _s.AccountId)), (Lang.T("Close"), Hide));
            else _win.SetFooter((Lang.T("Rest"), () => _s.UseSkill(244, 1, _s.AccountId)), (Lang.T("Attack"), AttackGazed), (W4AiStandby ? Lang.T("Resume") : Lang.T("Standby"), () => { Recall(); Refresh(); }), (Lang.T("Close"), Hide));
        }

        public void AttackGazed() { var t = GazedMonster?.Invoke(); if (t != null) Assist(t.Id); else Toast?.Invoke(Lang.T("Look at a monster first.")); }

        private void RefreshMerc(List<ListWindow.Row> rows)
        {
            _win.SetTitle(Lang.T("Mercenary")); _win.SetInput(false);
            if (!HasMerc)
            {
                _win.SetInfo(Lang.T("No mercenary."));
                rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>Hire one by using a mercenary scroll (Bowman, Swordsman or Spearman scrolls from the mercenary guilds).</color>") });
                _win.SetFooter((Lang.T("Close"), Hide));
                return;
            }
            long left = MercExpire - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _win.SetInfo(left > 0 ? Lang.F("{0} — contract {1}:{2:00} left", MercName, left / 60, left % 60) : Lang.F("{0} — contract ending", MercName));
            rows.Add(new ListWindow.Row { Text = Lang.F("<color=#ffd060>{0}</color>  <color=#9aa3b5>Lv {1}</color>", MercName, MercLevel) + $"\n<size=19>HP {Bar(MercHp, MercMaxHp)} · SP {Bar(MercSp, MercMaxSp)}</size>" });
            rows.Add(new ListWindow.Row { Text = Lang.F("Loyalty {0} · hired {1}× · {2} kills", MercFaith, MercCalls, MercKills) + "\n" + Lang.F("<size=19>Atk {0} · Matk {1} · Hit {2} · Crit {3} · Def {4} · Mdef {5} · Flee {6}</size>", MercAtk, MercMatk, MercHit, MercCrit, MercDef, MercMdef, MercFlee) });
            rows.Add(W5AiRow(true)); // which AI runs, and its switch (client wave 5, w5ai: Companions.W5Ai.cs)
            foreach (var k in MercSkills)
            {
                var kk = k; var info = SkillDb.Get(k.Id);
                rows.Add(new ListWindow.Row { Icon = WristMenu.SkillIcon(info), Text = k.Sp > 0 ? Lang.F("{0}  <color=#9aa3b5>Lv {1} · {2} SP</color>", info.Name ?? k.Name, k.Level, k.Sp) : Lang.F("{0}  <color=#9aa3b5>Lv {1}</color>", info.Name ?? k.Name, k.Level), B = k.Inf != 0 ? Lang.T("Use") : null, OnB = () => UseCompanionSkill(kk, MercId) });
            }
            _win.SetFooter((Armed("fire", Lang.T("Dismiss")), () => { if (Confirm("fire", Lang.F("Dismiss {0}", MercName))) DismissMercenary(); }), (Lang.T("Attack"), AttackGazed), (W4AiStandby ? Lang.T("Resume") : Lang.T("Standby"), () => { Recall(); Refresh(); }), (Lang.T("Close"), Hide));
        }
    }
}
