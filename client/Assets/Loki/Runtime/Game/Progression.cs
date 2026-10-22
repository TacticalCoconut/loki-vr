using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Loki.Net;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>Skill names/descriptions (client English skillinfolist + skilldescript) and status-effect names (stateiconinfo).
    /// Client wave 5 (w5ui): also the Portuguese name and description (skills.tsv columns 7 and 8, made by the first-run builder from the
    /// player's GRF: skillinfolist_ptbr.lub and the Portuguese skilldescript.lub, tools/clientdata/build_skilltree.py); the language
    /// setting picks the column (ClientText.Portuguese, as for msgstringtable), the English one when the GRF has no Portuguese text.</summary>
    public static class SkillDb
    {
        public sealed class Skill
        {
            public int Id, MaxLevel; public string Aegis; public int[] Sp = new int[0];
            /// <summary>skills.tsv columns 3 / 6 (English) and 7 / 8 (Portuguese; "" when the GRF has none).</summary>
            public string NameEn = "", DescriptionEn = "", NamePt = "", DescriptionPt = "";
            /// <summary>The name in the language the player chose (Portuguese when the GRF has it, else English).</summary>
            public string Name { get => ClientText.Portuguese && NamePt.Length > 0 ? NamePt : NameEn; set => NameEn = value ?? ""; }
            public string Description { get => ClientText.Portuguese && DescriptionPt.Length > 0 ? DescriptionPt : DescriptionEn; set => DescriptionEn = value ?? ""; }
        }
        /// <summary>Wave 28 (wtextc): a status's English and Portuguese title (efst.tsv columns 3 and 5; the text pack's efst.tsv over them) and kind.</summary>
        private static Dictionary<int, Skill> _skills; private static Dictionary<int, (string nameEn, string namePt, string kind)> _efst;

        public static Skill Get(int id)
        {
            Load();
            return _skills.TryGetValue(id, out var s) ? s : new Skill { Id = id, Aegis = "SKILL_" + id, Name = "Skill #" + id, Description = "" };
        }
        /// <summary>Client wave 5 (w5ui): whether the client data has this skill (a skills.tsv row).</summary>
        public static bool Has(int id) { Load(); return _skills.ContainsKey(id); }
        /// <summary>Whether the client has a name/icon for this status (stateiconinfo.lub). Internal ones such as
        /// EFST_POSTDELAY (after-cast delay) have none and the original client does not list them.</summary>
        public static bool EfstNamed(int id) { Load(); return _efstNamed.Contains(id); }
        private static readonly HashSet<int> _efstNamed = new HashSet<int>();
        /// <summary>A status's title in the game's language (wave 28: English only reads the English title; the EFST name when the data has none) and kind.</summary>
        public static (string name, string kind) Efst(int id)
        {
            Load();
            if (!_efst.TryGetValue(id, out var e)) return ("Effect #" + id, "");
            return (ClientText.Portuguese && e.namePt.Length > 0 ? e.namePt : e.nameEn, e.kind);
        }
        /// <summary>Wave 28: every skill of the table (the no-Portuguese check reads them).</summary>
        public static IEnumerable<Skill> All() { Load(); return _skills.Values; }
        /// <summary>Wave 28: every status title in the game's language (the no-Portuguese check).</summary>
        public static IEnumerable<string> EfstNames() { Load(); foreach (var k in _efst.Keys) yield return Efst(k).name; }

        private static void Load()
        {
            if (_skills != null) return;
            _skills = new Dictionary<int, Skill>(); _efst = new Dictionary<int, (string, string, string)>();
            string dir = Path.Combine(Application.streamingAssetsPath, "data");
            try
            {
                foreach (var line in File.ReadLines(Loki.Data.ClientData.File("skills.tsv"), Encoding.UTF8))
                {
                    var c = line.Split('\t'); if (c.Length < 6 || !int.TryParse(c[0], out int id)) continue;
                    var sp = new List<int>(); foreach (var x in c[4].Split(',')) if (int.TryParse(x, out int v)) sp.Add(v);
                    _skills[id] = new Skill { Id = id, Aegis = c[1], Name = c[2].Length > 0 ? c[2] : c[1], MaxLevel = int.TryParse(c[3], out int m) ? m : 0, Sp = sp.ToArray(), Description = c[5].Replace("\\n", "\n"),
                                              NamePt = c.Length > 6 ? c[6] : "", DescriptionPt = c.Length > 7 ? c[7].Replace("\\n", "\n") : "" }; // client wave 5 (w5ui)
                }
                foreach (var line in File.ReadLines(Loki.Data.ClientData.File("efst.tsv"), Encoding.UTF8))
                {
                    var c = line.Split('\t'); if (c.Length < 4 || !int.TryParse(c[0], out int id)) continue;
                    string name = c[2].Length > 0 ? c[2] : c[1].Replace("EFST_", "").Replace('_', ' ');
                    _efst[id] = (name, c.Length > 4 ? c[4] : "", c[3]); // wave 28 (wtextc): column 5, the Portuguese title
                    if (c[2].Length > 0) _efstNamed.Add(id); // (the player's data decides which statuses are listed; the pack only words them)
                }
                ApplyPack();
                Debug.Log($"[SkillDb] {_skills.Count} skills, {_efst.Count} status effects");
            }
            catch (Exception e) { Debug.LogError("[SkillDb] load failed: " + e.Message); }
        }

        /// <summary>Wave 28 (wtextc): the text pack's skills.tsv (id, name_en, name_pt, desc_en, desc_pt) and efst.tsv (id, title_en,
        /// title_pt) over the player's tables: a non-empty pack field wins.</summary>
        private static void ApplyPack()
        {
            var ts = Loki.Data.TextPack.Table("skills.tsv");
            if (ts != null)
            {
                int applied = 0, unknown = 0; string first = null;
                foreach (var c in ts.Rows)
                {
                    if (!int.TryParse(c[0], out int id) || !_skills.TryGetValue(id, out var s)) { unknown++; if (first == null) first = c[0]; continue; }
                    s.NameEn = Loki.Data.TextPack.Pick(c[1], s.NameEn); s.NamePt = Loki.Data.TextPack.Pick(c[2], s.NamePt);
                    s.DescriptionEn = Loki.Data.TextPack.Pick(c[3], s.DescriptionEn); s.DescriptionPt = Loki.Data.TextPack.Pick(c[4], s.DescriptionPt);
                    applied++;
                }
                Loki.Data.TextPack.Report(ts, applied, unknown, first);
            }
            var te = Loki.Data.TextPack.Table("efst.tsv");
            if (te != null)
            {
                int applied = 0, unknown = 0; string first = null;
                foreach (var c in te.Rows)
                {
                    if (!int.TryParse(c[0], out int id) || !_efst.TryGetValue(id, out var e)) { unknown++; if (first == null) first = c[0]; continue; }
                    _efst[id] = (Loki.Data.TextPack.Pick(c[1], e.nameEn), Loki.Data.TextPack.Pick(c[2], e.namePt), e.kind);
                    applied++;
                }
                Loki.Data.TextPack.Report(te, applied, unknown, first);
            }
        }
    }

    /// <summary>
    /// Character stats and skills, from rAthena e985006 at PACKETVER 20211103 (see tools/…/skills_stats_packets.md):
    ///   0x00bd ZC_STATUS (44)  0x0141 ZC_COUPLESTATUS (14)  0x00be ZC_STATUS_CHANGE (5)  0x00bc ZC_STATUS_CHANGE_ACK (6)  0x013a (4)
    ///   0x00b0 derived stats (ATK1/2 41/42, MATK1/2 43/44, DEF1/2 45/46, MDEF1/2 47/48, HIT 49, FLEE1/2 50/51, CRIT 52, ASPD 53)
    ///   0x0b32 skill list (15-byte SKILLDATA), 0x0b31 add (17), 0x0b33 update (17), 0x010e skill up (11), 0x0441 delete (4)
    ///   0x043d cooldown, 0x0983/0x0196/0x0984 status icons
    /// </summary>
    public sealed class Progression
    {
        public sealed class SkillEntry
        {
            public int Id, Inf, Level, Sp, Range; public bool Upgradable; public float CooldownUntil;
            public bool Passive => Inf == 0;
            public bool Ground => (Inf & 0x02) != 0;
            public bool SelfCast => (Inf & 0x04) != 0;
            public bool Support => (Inf & 0x10) != 0;
            public bool AttackSkill => (Inf & 0x01) != 0;
            public SkillDb.Skill Info => SkillDb.Get(Id);
        }
        public sealed class Effect { public int Type, Val1; public float EndsAt; public bool Timed; public string Name => SkillDb.Efst(Type).name; public string Kind => SkillDb.Efst(Type).kind; public bool Listed => SkillDb.EfstNamed(Type); }

        // stats (index 0..5 = STR AGI VIT INT DEX LUK)
        public readonly int[] Base = new int[6], Bonus = new int[6], Cost = new int[6];
        public int StatusPoints, AttackRange = 1;
        public int Atk1, Atk2, Matk1, Matk2, Def1, Def2, Mdef1, Mdef2, Hit, Flee1, Flee2, Crit, Aspd;
        public static readonly string[] StatNames = { "STR", "AGI", "VIT", "INT", "DEX", "LUK" };

        // 4th-job trait stats (map.hpp _sp: SP_POW..SP_CRT 219-224, SP_PATK..SP_CRATE 225-230, SP_TRAITPOINT 231, SP_AP 232,
        // SP_MAXAP 233, SP_UPOW..SP_UCRT 247-252). clif_initialstatus sends all of them to every Renewal character (clif.cpp:4176-4198):
        // 0x0141 for the six traits (base + bonus), 0x00b0 for points / AP / derived, 0x00be for the cost of the next point.
        // Raising one: CZ_ADVANCED_STATUS_CHANGE 0x0b24 type.W amount.W → pc_traitstatusup (pc.cpp:9034-9083) answers 0x00be (new cost),
        // 0x00b0 SP_TRAITPOINT and 0x00bc ZC_STATUS_CHANGE_ACK type ok value.
        public readonly int[] TraitBase = new int[6], TraitBonus = new int[6], TraitCost = new int[6];
        public int TraitPoints, Ap, MaxAp, PAtk, SMatk, Res, MRes, HPlus, CRate;
        public static readonly string[] TraitNames = { "POW", "STA", "WIS", "SPL", "CON", "CRT" };
        public const int SP_POW = 219, SP_UPOW = 247;
        /// <summary>Whether the character has anything trait-related (a 4th job): trait points, AP or a trait above 0.</summary>
        public bool HasTraits { get { if (TraitPoints > 0 || MaxAp > 0) return true; foreach (var v in TraitBase) if (v > 0) return true; return false; } }

        /// <summary>The last ZC_STATUS_CHANGE_ACK (stat id, ok, value) — smoke test.</summary>
        public (int sp, bool ok, int value) LastStatusAck = (0, false, 0);

        public readonly SortedDictionary<int, SkillEntry> Skills = new SortedDictionary<int, SkillEntry>();
        /// <summary>The level the player picked for a skill (skill window "Lv" button); absent = the highest learned level.
        /// rAthena casts at the level the client asks for, capped at the learned one (clif_parse_skill_toid: skill_lv = min(pc_checkskill, skill_lv)).</summary>
        public readonly Dictionary<int, int> ChosenLevel = new Dictionary<int, int>();
        /// <summary>The level a cast of this skill uses: the chosen one, kept within 1..learned.</summary>
        public int CastLevel(SkillEntry k) => k == null ? 1 : ChosenLevel.TryGetValue(k.Id, out int lv) && lv >= 1 && lv <= k.Level ? lv : Math.Max(1, k.Level);
        /// <summary>Steps the chosen level down by one (wrapping from 1 back to the learned level).</summary>
        public int CycleLevel(SkillEntry k)
        {
            if (k == null || k.Level <= 1) return 1;
            int lv = CastLevel(k) - 1; if (lv < 1) lv = k.Level;
            if (lv == k.Level) ChosenLevel.Remove(k.Id); else ChosenLevel[k.Id] = lv;
            Changed?.Invoke();
            return lv;
        }
        public readonly Dictionary<int, Effect> Effects = new Dictionary<int, Effect>();
        public event Action Changed;
        public event Action<string> Message;
        /// <summary>A status icon on ourselves started (on = true, with its val1 — the cart type for EFST 673) or ended.</summary>
        public event Action<int, bool, int> EffectChanged;
        /// <summary>Client wave 2: a status icon on ANY unit in view (aid, type, on, val1) — other players' carts (EFST 673 is DisplayPc +
        /// SendVal1, so the area gets it), night on ourselves; val1 is 0 for 0x0196.</summary>
        public event Action<uint, int, bool, int> AnyEffectChanged;
        // client wave 25 (w25misc), item 13: a skill of ours went up a level (0x010e: rAthena sends it only from pc_skillup, pc.cpp:9169)
        /// <summary>One of our own skills (not a guild skill) went up a level: skill id, its new level.</summary>
        public event Action<int, int> SkillLevelUp;
        private uint _self;

        public void SetSelf(uint aid) { _self = aid; }

        /// <summary>Clears all character-specific progression state when leaving or switching characters so nothing leaks locally across characters.</summary>
        public void Reset()
        {
            var prevEffects = Effects.Count > 0 ? new List<int>(Effects.Keys) : null;
            Skills.Clear();
            ChosenLevel.Clear();
            Effects.Clear();
            if (prevEffects != null)
            {
                foreach (var t in prevEffects)
                {
                    EffectChanged?.Invoke(t, false, 0);
                    if (_self != 0) AnyEffectChanged?.Invoke(_self, t, false, 0);
                }
            }
            Array.Clear(Base, 0, Base.Length); Array.Clear(Bonus, 0, Bonus.Length); Array.Clear(Cost, 0, Cost.Length);
            Array.Clear(TraitBase, 0, TraitBase.Length); Array.Clear(TraitBonus, 0, TraitBonus.Length); Array.Clear(TraitCost, 0, TraitCost.Length);
            StatusPoints = 0; TraitPoints = 0; Ap = 0; MaxAp = 0; AttackRange = 1;
            Atk1 = Atk2 = Matk1 = Matk2 = Def1 = Def2 = Mdef1 = Mdef2 = Hit = Flee1 = Flee2 = Crit = Aspd = 0;
            PAtk = SMatk = Res = MRes = HPlus = CRate = 0;
            LastStatusAck = (0, false, 0);
            Changed?.Invoke();
        }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            int end = o + n;
            switch (id)
            {
                case 0x00bd:
                {
                    r.Skip(2); StatusPoints = r.U16();
                    for (int i = 0; i < 6; i++) { Base[i] = r.U8(); Cost[i] = r.U8(); }
                    Atk1 = r.S16(); Atk2 = r.S16(); Matk1 = r.S16(); Matk2 = r.S16(); Def1 = r.S16(); Def2 = r.S16(); Mdef1 = r.S16(); Mdef2 = r.S16();
                    Hit = r.S16(); Flee1 = r.S16(); Flee2 = r.S16(); Crit = r.S16(); Aspd = r.S16();
                    Changed?.Invoke(); return true;
                }
                case 0x0141:
                {
                    r.Skip(2); int type = (int)r.U32(); int def = r.S32(), plus = r.S32();
                    if (type >= 13 && type <= 18) { Base[type - 13] = def; Bonus[type - 13] = plus; Changed?.Invoke(); }
                    else if (type >= SP_POW && type < SP_POW + 6) { TraitBase[type - SP_POW] = def; TraitBonus[type - SP_POW] = plus; Changed?.Invoke(); }
                    return true;
                }
                case 0x00be:
                {
                    r.Skip(2); int sid = r.U16(); int v = r.U8();
                    if (sid >= 32 && sid <= 37) { Cost[sid - 32] = v; Changed?.Invoke(); }
                    else if (sid >= SP_UPOW && sid < SP_UPOW + 6) { TraitCost[sid - SP_UPOW] = v; Changed?.Invoke(); }
                    return true;
                }
                case 0x00bc:
                {
                    r.Skip(2); int sp = r.U16(); bool ok = r.U8() != 0; int val = r.U8();
                    if (ok && sp >= 13 && sp <= 18) Base[sp - 13] = Math.Max(Base[sp - 13], val);
                    else if (ok && sp >= SP_POW && sp < SP_POW + 6) TraitBase[sp - SP_POW] = Math.Max(TraitBase[sp - SP_POW], val); // values over 255 follow as 0x0141
                    else if (!ok) Message?.Invoke(sp >= SP_POW && sp < SP_POW + 6 ? Lang.T("Not enough trait points for that.") : Lang.T("Not enough status points for that."));
                    LastStatusAck = (sp, ok, val);
                    Changed?.Invoke(); return true;
                }
                case 0x013a: { r.Skip(2); AttackRange = r.S16(); Changed?.Invoke(); return true; }
                case 0x0b32:
                {
                    r.Skip(4);
                    Skills.Clear();
                    while (end - r.Position >= 15) { var s = ReadSkill(ref r); Skills[s.Id] = s; }
                    Changed?.Invoke(); return true;
                }
                case 0x0b31: case 0x0b33:
                {
                    r.Skip(2); var s = ReadSkill(ref r);
                    if (Skills.TryGetValue(s.Id, out var old)) s.CooldownUntil = old.CooldownUntil;
                    Skills[s.Id] = s; Changed?.Invoke(); return true;
                }
                case 0x010e:
                {
                    r.Skip(2); int sid = r.U16(); int lv = r.U16(), sp = r.U16(), range = r.U16(); bool up = r.U8() != 0;
                    if (sid >= 10000 && sid < 10100) { Message?.Invoke(Lang.F("{0} is now level {1}.", SkillDb.Get(sid).Name, lv)); return true; } // guild skill (guild.cpp:1765): lives in the guild window, not the skill tree
                    if (!Skills.TryGetValue(sid, out var s)) Skills[sid] = s = new SkillEntry { Id = sid };
                    s.Level = lv; s.Sp = sp; s.Range = range; s.Upgradable = up;
                    Message?.Invoke(Lang.F("{0} is now level {1}.", s.Info.Name, lv));
                    Changed?.Invoke(); SkillLevelUp?.Invoke(sid, lv); return true; // (client wave 25, w25misc: item 13's sound)
                }
                case 0x0441: { r.Skip(2); Skills.Remove(r.U16()); Changed?.Invoke(); return true; }
                case 0x043d: { r.Skip(2); int sid = r.U16(); uint ms = r.U32(); if (Skills.TryGetValue(sid, out var s)) s.CooldownUntil = Time.time + ms / 1000f; Changed?.Invoke(); return true; }
                case 0x0983:
                {
                    r.Skip(2); int type = r.S16(); uint aid = r.U32(); int flag = r.U8(); uint total = r.U32(); uint remain = r.U32(); int val1 = r.Remaining >= 4 ? r.S32() : 0;
                    if (aid == _self) { SetEffect(type, flag != 0, remain, val1); EffectChanged?.Invoke(type, flag != 0, val1); }
                    AnyEffectChanged?.Invoke(aid, type, flag != 0, val1);
                    return true;
                }
                case 0x0196:
                {
                    r.Skip(2); int type = r.S16(); uint aid = r.U32(); int flag = r.U8();
                    if (aid == _self) { SetEffect(type, flag != 0, 9999, 0); EffectChanged?.Invoke(type, flag != 0, 0); }
                    AnyEffectChanged?.Invoke(aid, type, flag != 0, 0);
                    return true;
                }
                case 0x0984:
                {
                    r.Skip(2); uint aid = r.U32(); int type = r.U16(); uint dur = r.U32(); r.Skip(4); int val1 = r.Remaining >= 4 ? r.S32() : 0;
                    if (aid == _self) { SetEffect(type, true, dur, val1); EffectChanged?.Invoke(type, true, val1); }
                    AnyEffectChanged?.Invoke(aid, type, true, val1);
                    return true;
                }
                default: return false;
            }
        }

        /// <summary>0x00b0 derived-stat vars the PlayerState does not keep.</summary>
        public void OnParChange(int var, long value)
        {
            int v = (int)value;
            switch (var)
            {
                case 9: StatusPoints = v; break;
                case 41: Atk1 = v; break; case 42: Atk2 = v; break; case 43: Matk1 = v; break; case 44: Matk2 = v; break;
                case 45: Def1 = v; break; case 46: Def2 = v; break; case 47: Mdef1 = v; break; case 48: Mdef2 = v; break;
                case 49: Hit = v; break; case 50: Flee1 = v; break; case 51: Flee2 = v; break; case 52: Crit = v; break; case 53: Aspd = v; break;
                case 225: PAtk = v; break; case 226: SMatk = v; break; case 227: Res = v; break; case 228: MRes = v; break; case 229: HPlus = v; break; case 230: CRate = v; break;
                case 231: TraitPoints = v; break; case 232: Ap = v; break; case 233: MaxAp = v; break;
                default: return;
            }
            Changed?.Invoke();
        }

        private void SetEffect(int type, bool on, uint ms, int val1 = 0)
        {
            if (!on) Effects.Remove(type);
            else Effects[type] = new Effect { Type = type, Timed = ms != 9999, EndsAt = Time.time + ms / 1000f, Val1 = val1 };
            Changed?.Invoke();
        }

        /// <summary>Item 13: computes the active HP shield capacity as a fraction (0..1) of MaxHP from active status effects.</summary>
        public float ComputeShieldFraction(long hp, long maxHp, long sp, long maxSp)
        {
            if (maxHp <= 0 || Effects.Count == 0) return 0f;
            float frac = 0f;
            foreach (var ef in Effects.Values)
            {
                if (ef.Timed && ef.EndsAt > 0f && Time.time > ef.EndsAt) continue;
                switch (ef.Type)
                {
                    case 19: // EFST_KYRIE (Kyrie Eleison: 12%..30% of MaxHP based on skill level)
                    {
                        int lv = Skills.TryGetValue(73, out var k) && k.Level > 0 ? k.Level : 5;
                        frac += Mathf.Clamp(0.10f + 0.02f * lv, 0.12f, 0.30f);
                        break;
                    }
                    case 31: // EFST_ENERGYCOAT (Energy Coat: up to 30% reduction scaled by current SP%)
                    {
                        float spRatio = maxSp > 0 ? Mathf.Clamp01(sp / (float)maxSp) : 1f;
                        frac += 0.30f * spRatio;
                        break;
                    }
                    case 110: // EFST_ASSUMPTIO
                        frac += 0.25f;
                        break;
                    case 153: // EFST_KAITE
                    case 155: // EFST_KAUPE
                        frac += 0.20f;
                        break;
                    case 290: // EFST_NEUTRALBARRIER
                        frac += 0.25f;
                        break;
                    case 327: // EFST_MILLENNIUMSHIELD
                        frac += 0.30f;
                        break;
                    case 391: // EFST_FORCEOFVANGUARD
                    case 427: // EFST_GENTLETOUCH_REVITALIZE
                        frac += 0.20f;
                        break;
                    default:
                        if (ef.Val1 > 0 && (ef.Name.IndexOf("Shield", StringComparison.OrdinalIgnoreCase) >= 0 || ef.Name.IndexOf("Barrier", StringComparison.OrdinalIgnoreCase) >= 0 || ef.Name.IndexOf("Guard", StringComparison.OrdinalIgnoreCase) >= 0))
                            frac += Mathf.Clamp01(ef.Val1 / (float)maxHp);
                        break;
                }
            }
            return Mathf.Clamp01(frac);
        }

        private static SkillEntry ReadSkill(ref PacketReader r)
        {
            // SKILLDATA (PACKETVER_RE_NUM >= 20190807): id.W inf.L level.W sp.W range2.W upFlag.B level2.W
            var s = new SkillEntry { Id = r.U16(), Inf = r.S32(), Level = r.U16(), Sp = r.U16(), Range = r.U16() };
            s.Upgradable = r.U8() != 0; r.Skip(2);
            return s;
        }

        /// <summary>ASPD as shown in RO: rAthena sets amotion = AMOTION_ZERO_ASPD(2000) − aspd × AMOTION_INTERVAL(10) (status.cpp:4618), so aspd = (2000 − amotion) / 10.</summary>
        public int AspdDisplay => Aspd > 0 ? Mathf.Max(0, (2000 - Aspd) / 10) : 0;

        public static string FailText(int cause, int skillId)
        {
            string s = SkillDb.Get(skillId).Name;
            switch (cause)
            {
                case 0: return $"{s}: requirements not met.";
                case 1: return "Not enough SP.";
                case 2: return "Not enough HP.";
                case 3: return $"{s}: missing a required item.";
                case 4: return "Skill is still on cooldown.";
                case 5: return "Not enough zeny.";
                case 6: return $"{s} can't be used with this weapon.";
                case 7: return "Requires a Red Gemstone.";
                case 8: return "Requires a Blue Gemstone.";
                case 9: return "You're carrying too much.";
                case 11: return $"{s}: invalid target.";
                case 23: return $"{s}: conditions not met.";
                case 26: return $"{s}: can't be used here.";
                case 28: return "Too close to a wall.";
                case 71: return $"{s}: missing a required item.";
                case 72: return $"{s}: requires specific equipment.";
                case 87: return $"{s} can't be used on this map.";
                case 95: return $"{s} needs a weapon.";
                default: return $"{s} failed (reason {cause}).";
            }
        }
    }
}
