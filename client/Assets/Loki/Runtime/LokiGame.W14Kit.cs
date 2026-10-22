using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Loki.Game;
using Loki.Net;
using Loki.UI;
using Loki.World;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 14, deploy B (w14kit), point 24 of the design's headset list: what a Character preset button does (Game/CharPresets.cs is the
    /// data and the list; the record is World/UserSettings.cs CharPreset, per character "Name#charId").
    ///
    /// GM ONLY. rAthena tells a client nothing about its account's group, and an @command from an account without the rights is said
    /// ALOUD as public chat (atcommand.cpp is_atcommand: "return false" = a normal message), so the tab is shown only after a check: a
    /// whisper to myself (clif_parse_WisMessage: a plain text is whispered; to myself it comes back as ZC_WHISPER, whose isAdmin byte is
    /// "group level == 99", clif.cpp clif_wis_message), kept out of the chat (SocialHub.TakeWhisper / TakeWhisperAck). A refused whisper
    /// (I ignore everyone) or no answer: not a GM for this session (the check runs again at the next opening of the Commands window).
    ///
    /// A press (W14KSetUp), every step a GM command sent as a chat line (the server's answers are kept out of the chat while it runs:
    /// SocialHub.TakeServerLine, and logged), in rAthena's terms:
    ///   1. the previous preset's items taken back: of each, min(given, owned now − owned before) (@delitem: pc_delitem unequips a worn
    ///      piece first), so the character's own items are never taken;
    ///   2. the first preset of a character records its job, levels and quick bar 1 first (No preset puts them back);
    ///   3. client wave 18 (w18gear), point 3 of the design's notes after wave 17 ("I am supposed to keep the same level ... Or at the very least,
    ///      the minimum level necessary to be the class that I choose"): the first preset reads the character's job-change levels (@stats)
    ///      and skill points; @resetskill; the job's PATH (its earlier jobs, data column 14: @jobchange, @joblevelup to the job level each is
    ///      left at, so rAthena records real job-change levels, pc.cpp:10881-10893), @jobchange to the job; the base level KEPT, raised only
    ///      to the job's minimum (rAthena's job change rules, data column 14), the job level KEPT (pc_jobchange sets it to 1, pc.cpp:10920) and raised only as far as the job's own tier's quick-bar skills need (design note: "raise the job level enough to learn its skills": W14KLearn),
    ///      both within the job's maximum; @resetstat, then the status points of that base level spent towards the preset's profile with the
    ///      real stat raise (0x00bb / 0x0b24: CharPresets.FitStats); @resetskill, the free skill points set to those of that job level
    ///      (@skpoint: the earlier jobs' + one a job level, data column 15) and spent on the preset's skills with the real skill raise
    ///      (0x0112: W14KNextSkills); then the extras the learned skills allow (@cart 1 with Pushcart; @mount_peco for the dragon with Dragon
    ///      Training, for the Mado Gear; @option 0 0 option+16 for the falcon with Falcon Mastery) (wave 14 set the job's maximum levels,
    ///      @str … @crt without points and @allskill instead);
    ///   4. the items: what is not owned yet of the equipment (a piece the character already owns is its own: worn, never given or taken;
    ///      client wave 18: a piece the character may not wear at its levels is left out, EquipRules / pc_isequip),
    ///      the consumables / ammunition / materials in full (@item), recorded BEFORE they are sent; then each piece worn in the data's
    ///      order (CZ_REQ_WEAR_EQUIP; the second dagger in the left hand, the ammunition in its slot);
    ///   5. quick bar 1 (rows 1-4, 36 slots): the data's skills the character now has (not passive), at their level, then the kit's
    ///      potions and Fly Wings in the last slots; the bar shows row 1.
    /// No preset: step 1, @resetskill, the recorded job (client wave 18: through its path at its recorded job-change levels) and levels,
    /// @resetstat, @resetskill and the skill points it had, quick bar 1 as it was; the record goes.
    /// </summary>
    public sealed partial class LokiGame
    {
        public CharPresets Presets { get; private set; }
        /// <summary>The GM check's result for this character (null: not known yet).</summary>
        public bool? W14KIsGm { get; private set; }
        public bool W14KBusy { get; private set; }
        public string W14KStep { get; private set; } = "";
        /// <summary>The server lines kept out of the chat by the last preset, and those that say a command failed.</summary>
        public int W14KQuietLines { get; private set; }
        public int W14KRefusals { get; private set; }
        private const string W14KProbeText = "Loki GM check";
        private bool _w14kProbing, _w14kQuiet; private float _w14kProbeAt; private string _w14kProbedFor = "";
        private HashSet<int>[] _w14kOffered = new HashSet<int>[2];

        private string W14KChar => Session?.SelectedChar != null ? Session.SelectedChar.Name + "#" + Session.SelectedChar.CharId : "";
        private int W14KSex => Session?.SelectedChar != null ? Session.SelectedChar.Sex : 1;

        /// <summary>Hook in LokiGame.cs, right after InitW11Menu.</summary>
        private void InitW14Kit()
        {
            if (Commands == null || Pickers == null) return;
            Presets = new CharPresets
            {
                Sex = () => W14KSex,
                CurrentJob = () => Entities?.Self != null ? Entities.Self.Job : -1,
                Offered = (job, sex) => W14KOffered(sex).Contains(job),
                ActiveKey = () => UserSettings.CharPresetOf(W14KChar)?.preset ?? "",
                HasRecord = () => UserSettings.CharPresetOf(W14KChar) != null,
                Busy = () => W14KBusy,
                Progress = () => W14KStep,
                Apply = p => { if (!W14KBusy) StartCoroutine(W14KSetUp(p)); },
                NoPreset = () => { if (!W14KBusy) StartCoroutine(W14KSetUp(null)); },
                Close = () => Commands.Hide(),
                Redraw = () => Commands.Refresh(),
            };
            Commands.PresetsShown = () => W14KIsGm == true;
            Commands.FillPresets = win => Presets.Fill(win);
            Commands.Opened = W14KCheckGm;
            if (Social != null)
            {
                Social.TakeServerLine = line =>
                {
                    if (!_w14kQuiet) return false;
                    W14KQuietLines++; _w14kLines.Add(line); // client wave 18 (w18gear): the @stats answer is read from them
                    if (line.EndsWith(" failed.") || line.Contains("unable") || line.Contains("Unable") || line.Contains("can not") || line.Contains("cannot")) W14KRefusals++;
                    return true;
                };
                Social.TakeWhisper = (from, text, admin) =>
                {
                    if (!_w14kProbing || text != W14KProbeText || from != (Session?.SelectedChar?.Name ?? "")) return false;
                    _w14kProbing = false; W14KIsGm = admin;
                    Debug.Log($"[Presets] GM check: the whisper to myself came back, admin {admin}: the Presets tab is {(admin ? "shown" : "hidden")}");
                    Commands.Refresh();
                    return true;
                };
                Social.TakeWhisperAck = res =>
                {
                    if (!_w14kProbing) return false;
                    if (res != 0) { _w14kProbing = false; W14KIsGm = false; Debug.Log($"[Presets] GM check: the whisper to myself was refused (result {res}): no Presets tab"); Commands.Refresh(); }
                    return true;
                };
            }
        }

        /// <summary>The jobs the Jobs picker offers to a sex (its rule: GmPickers.JobRows), computed once per sex.</summary>
        private HashSet<int> W14KOffered(int sex)
        {
            int s = sex == 0 ? 0 : 1;
            if (_w14kOffered[s] == null) _w14kOffered[s] = new HashSet<int>(Pickers.JobRows(s).Where(r => r.LeftOut.Length == 0).Select(r => r.Job));
            return _w14kOffered[s];
        }

        /// <summary>The GM check (a whisper to myself), once per character and session; again when the last one had no answer.</summary>
        private void W14KCheckGm()
        {
            if (Session == null || Session.State != RoSession.Stage.InGame || Session.SelectedChar == null) return;
            string me = W14KChar;
            if (W14KIsGm.HasValue && _w14kProbedFor == me) return;
            if (_w14kProbing && Time.realtimeSinceStartup - _w14kProbeAt < 5f) return;
            _w14kProbing = true; _w14kProbeAt = Time.realtimeSinceStartup; _w14kProbedFor = me; W14KIsGm = null;
            Debug.Log("[Presets] GM check: a whisper to myself");
            Session.Whisper(Session.SelectedChar.Name, W14KProbeText);
        }

        private void W14KSend(string line) { Debug.Log("[Presets] > " + line); Hud?.Chat.Submit?.Invoke(line); }

        private IEnumerator W14KWait(System.Func<bool> cond, float seconds) { float t = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < t && !cond()) yield return null; }

        private void W14KStepTo(string s) { W14KStep = s; Debug.Log("[Presets] step: " + s); Commands?.Refresh(); }

        /// <summary>A preset (or No preset when <paramref name="p"/> is null): see the class summary.</summary>
        private IEnumerator W14KSetUp(CharPresets.Preset p)
        {
            if (Session == null || Session.State != RoSession.Stage.InGame || Entities?.Self == null || Hotkeys == null) yield break;
            string key = W14KChar; int sex = W14KSex;
            var rec = UserSettings.CharPresetOf(key);
            if (p == null && rec == null) { Chat(Lang.T("No preset now."), "9ad1ff"); yield break; }
            int base0 = Player.BaseLevel, job0 = Player.JobLevel; // client wave 18 (w18gear), point 3: the levels a preset keeps
            W14KBusy = true; _w14kQuiet = true; W14KQuietLines = 0; W14KRefusals = 0; _w14kLines.Clear(); W14KSkipped.Clear();
            Debug.Log($"[Presets] {(p != null ? $"preset {p.Key} (job {p.Job}, {p.Style})" : "no preset")} for {key}: lv {base0} / {job0}; record {(rec != null ? $"'{rec.preset}', {rec.items.Length / 3} item kinds, job before {rec.origJob}" : "none")}");
            // 1. the previous preset's items
            if (rec != null && rec.items != null && rec.items.Length >= 3)
            {
                W14KStepTo(Lang.T("Taking back the last preset's items…"));
                var expect = new List<(int id, int left)>();
                for (int i = 0; i + 2 < rec.items.Length; i += 3)
                {
                    int id = rec.items[i], given = rec.items[i + 1], before = rec.items[i + 2], have = Inventory.Count(id);
                    int n = Mathf.Min(given, Mathf.Max(0, have - before));
                    if (n <= 0) continue;
                    W14KSend($"@delitem {id} {n}"); expect.Add((id, have - n));
                    yield return new WaitForSecondsRealtime(0.05f);
                }
                yield return W14KWait(() => expect.All(e => Inventory.Count(e.id) <= e.left), 5f);
                rec.items = new int[0]; rec.preset = ""; UserSettings.SetCharPreset(rec); UserSettings.Save();
            }
            var self = Entities.Self;
            if (p == null)
            {
                // No preset: the job and levels of before (client wave 18: through the job's path at the job-change levels it had, so the
                // server gates its skill tree as before), stats to spend again, the skill points it had, quick bar 1 as it was
                W14KStepTo(Lang.T("Back to your character…"));
                yield return W14KCmd("@resetskill");
                var home = CharPresets.All.FirstOrDefault(x => x.Job == rec.origJob);
                if (rec.origJob >= 0 && home != null)
                {
                    var walk = home.Walk.Select((w, i) => (w.job, W14KChangeLevel(rec, w.job, i + 1 < home.Walk.Count ? home.Walk[i + 1].job : rec.origJob, w.jobLevel))).ToList();
                    bool ok = true; yield return W14KWalk(walk, r => ok = r);
                    if (!ok) { yield return W14KDone(Lang.T("The server refused the job change.")); yield break; }
                }
                if (rec.origJob >= 0 && Entities.Self.Job != rec.origJob) { yield return W14KCmd($"@jobchange {rec.origJob}"); yield return W14KWait(() => Entities.Self != null && Entities.Self.Job == rec.origJob, 5f); }
                yield return new WaitForSecondsRealtime(0.3f);
                yield return W14KLevels(rec.origBase, rec.origJobLevel);
                yield return W14KCmd("@resetstat");
                yield return W14KCmd("@resetskill");
                if (rec.origSkill >= 0) yield return W14KPool(rec.origSkill - W14KBasicSkill());
                var bar = rec.origBar ?? new int[0];
                for (int s = 0; s < 36; s++)
                {
                    int o = s * 3; bool sk = o + 2 < bar.Length && bar[o] != 0; int id = o + 2 < bar.Length ? bar[o + 1] : 0, cnt = o + 2 < bar.Length ? bar[o + 2] : 0;
                    W14KSlot(s, sk, id, cnt);
                }
                Hotkeys.ShowRow(0);
                UserSettings.ClearCharPreset(key); UserSettings.Save();
                yield return W14KDone(Lang.T("Preset removed: your job and levels are back."));
                yield break;
            }
            // 2. the first preset: what the character was
            bool first = rec == null;
            if (rec == null)
            {
                var bar = new int[36 * 3];
                for (int s = 0; s < 36; s++) { var h = Hotkeys.At(s / 9, s % 9); bar[s * 3] = h.IsSkill ? 1 : 0; bar[s * 3 + 1] = h.Id; bar[s * 3 + 2] = h.Count; }
                rec = new UserSettings.CharPreset { character = key, origJob = self.Job, origBase = base0, origJobLevel = job0, origBar = bar };
                UserSettings.SetCharPreset(rec); UserSettings.Save();
                Debug.Log($"[Presets] first preset of {key}: job {rec.origJob}, levels {rec.origBase} / {rec.origJobLevel} and quick bar 1 recorded");
            }
            // 3. job, levels (client wave 18, w18gear, point 3: the character's own, the base level raised only to the job's minimum), stats and
            //    skills (the points of those levels, spent as a player spends them), extras
            W14KStepTo(Lang.F("Setting up {0}…", SkillTreeDb.JobName(p.Job, sex)));
            if (first)
            {
                // the job-change levels rAthena keeps for this character (No preset walks back through them): @stats (atcommand.cpp:9850-9932)
                int n0 = _w14kLines.Count;
                W14KSend("@stats");
                yield return W14KWait(() => _w14kLines.Skip(n0).Any(l => l.StartsWith("JobChangeLvl (4th)")), 4f);
                rec.origChange = new int[3];
                foreach (var l in _w14kLines.Skip(n0))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(l, @"^JobChangeLvl \((2nd|3rd|4th)\) - (\d+)");
                    if (m.Success) rec.origChange[m.Groups[1].Value == "2nd" ? 0 : m.Groups[1].Value == "3rd" ? 1 : 2] = int.Parse(m.Groups[2].Value);
                }
            }
            yield return W14KCmd("@resetskill");
            if (first)
            {
                // spent + free as pc_calc_skillpoint counts them (pc.cpp:2518-2540): @resetskill gave back every spent point but Basic Skill's
                // of a non-Novice (pc.cpp:9495-9497)
                rec.origSkill = Player.SkillPoint + W14KBasicSkill(); UserSettings.SetCharPreset(rec); UserSettings.Save();
                Debug.Log($"[Presets] {key} before: {rec.origSkill} skill points, job-change levels {string.Join(" / ", rec.origChange)}");
            }
            {
                bool ok = true; yield return W14KWalk(p.Walk, r => ok = r);
                if (!ok) { yield return W14KDone(Lang.T("The server refused the job change.")); yield break; }
            }
            if (Entities.Self.Job != p.Job)
            {
                yield return W14KCmd($"@jobchange {p.Job}");
                yield return W14KWait(() => Entities.Self != null && Entities.Self.Job == p.Job, 5f);
                if (Entities.Self == null || Entities.Self.Job != p.Job) { yield return W14KDone(Lang.T("The server refused the job change.")); yield break; }
            }
            yield return new WaitForSecondsRealtime(0.3f);
            var (baseT, jobT) = CharPresets.LevelsFor(p, base0, job0);
            yield return W14KLevels(baseT, jobT);
            Debug.Log($"[Presets] {p.Key}: levels {base0} / {job0} -> {Player.BaseLevel} / {Player.JobLevel} (kept; the job's minimum base {p.MinBase}, maximum {p.BaseLevel} / {p.JobLevel})");
            // stats: the status points of this base level (pc_resetstate: the statpoint table + the transcendent 52, pc.cpp:9358-9363), raised as a
            // player raises them (CZ_STATUS_CHANGE 0x00bb → pc_statusup, pc.cpp:8872; traits CZ_ADVANCED_STATUS_CHANGE 0x0b24 → pc_traitstatusup)
            W14KStepTo(Lang.T("Spending the points…"));
            yield return W14KCmd("@resetstat");
            yield return W14KWait(() => Prog.Base.Take(6).All(v => v == 1), 3f);
            W14KStatPointsSeen = Player.StatusPoint; W14KTraitPointsSeen = Prog.TraitPoints;
            var stats = CharPresets.FitStats(p, Player.StatusPoint);
            Session.BeginPacketBatch();
            for (int i = 0; i < 6; i++) if (stats[i] > 1) Session.StatusUp(i, stats[i] - 1);
            Session.EndPacketBatch();
            yield return W14KWait(() => Enumerable.Range(0, 6).All(i => Prog.Base[i] >= stats[i]), 3f);
            if (p.Stats.Skip(6).Any(v => v > 0))
            {
                var traits = CharPresets.FitTraits(p, Prog.TraitPoints);
                Session.BeginPacketBatch();
                for (int i = 0; i < 6; i++) if (traits[i] > 0) Session.TraitUp(Progression.SP_POW + i, traits[i]);
                Session.EndPacketBatch();
                yield return W14KWait(() => Enumerable.Range(0, 6).All(i => Prog.TraitBase[i] >= traits[i]), 3f);
            }
            Debug.Log($"[Presets] {p.Key}: stats {string.Join(" ", Prog.Base)} (profile {string.Join(" ", p.Stats.Take(6))}), {Player.StatusPoint} status points left; traits {string.Join(" ", Prog.TraitBase)}, {Prog.TraitPoints} left");
            // skills: the points of this job level (the earlier jobs' + one a job level), spent on the preset's skills as the skill window does
            yield return W14KCmd("@resetskill");
            int pool = CharPresets.SkillPointsFor(p, Player.JobLevel);
            if (pool >= 0) yield return W14KPool(pool - W14KBasicSkill());
            yield return W14KLearn(p);
            Debug.Log($"[Presets] {p.Key}: {Prog.Skills.Values.Where(k => k.Level > 0).Sum(k => k.Level)} skill levels, {Player.SkillPoint} points left; bar skills learned: {string.Join(" ", p.Bar.Where(id => Prog.Skills.TryGetValue(id, out var k) && k.Level > 0).Select(id => id + ":" + Prog.Skills[id].Level))}");
            // Populate the quick bar skills immediately so they appear on the menu without waiting for item creation/equipping
            var earlySkills = p.Bar.Where(id => Prog.Skills.TryGetValue(id, out var k) && k.Level > 0 && !k.Passive).Take(36).ToList();
            Session.BeginPacketBatch();
            for (int s = 0; s < 36; s++)
            {
                if (s < earlySkills.Count) W14KSlot(s, true, earlySkills[s], Prog.Skills[earlySkills[s]].Level);
                else W14KSlot(s, false, 0, 0);
            }
            Session.EndPacketBatch();
            Hotkeys.ShowRow(0);
            int opt = Entities.Self != null ? Entities.Self.Option : 0;
            foreach (var e in p.Extras)
            {
                // client wave 18: only what this character's skills allow (@mount_peco needs Dragon Training, atcommand.cpp:5016; the cart and
                // the falcon go with Pushcart / Falcon Mastery; the Mado Gear needs no skill, atcommand.cpp:5045-5069)
                if (e == "mount:dragon") { if ((opt & (Sprites.UnitLooks.OptDragon1 | Sprites.UnitLooks.OptDragon2 | Sprites.UnitLooks.OptDragon3 | Sprites.UnitLooks.OptDragon4 | Sprites.UnitLooks.OptDragon5)) == 0 && W14KHas(2007)) W14KSend("@mount_peco"); }
                else if (e == "mount:mado") { if ((opt & 0x400000) == 0) W14KSend("@mount_peco"); }
                else if (e == "falcon") { if ((opt & 0x10) == 0 && W14KHas(127)) W14KSend($"@option 0 0 {opt | 0x10}"); }
                else if (e == "@cart 1") { if (W14KHas(39)) W14KSend(e); }
                else if (e.StartsWith("@")) W14KSend(e);
                yield return new WaitForSecondsRealtime(0.05f);
            }
            // 4. the items: recorded before they are sent; client wave 18: equipment this character may not wear at these levels is left out
            //    (EquipRules: rAthena's pc_isequip, the base level first: EquipLevelMin)
            W14KStepTo(Lang.T("Giving the items…"));
            var gives = CharPresets.Gives(p);
            foreach (var (id, loc) in p.Equip)
            {
                if (loc == 0x8000 || W14KSkipped.Contains(id)) continue; // ammunition: checked when worn (it needs the weapon in hand)
                string why = EquipRules.WhyNot(id, GameDb.GetItem(id).Type, false, Entities.Self.Job, sex, Player.BaseLevel, 0, 0);
                if (why == null) continue;
                W14KSkipped.Add(id); gives.Remove(id);
                Debug.Log($"[Presets] {p.Key}: {id} {GameDb.GetItem(id).English} left out: {why} (lv {Player.BaseLevel})");
            }
            var equipIds = new HashSet<int>(p.Equip.Where(x => x.loc != 0x8000).Select(x => x.id));
            var items = new List<int>(); var want = new List<(int id, int total)>();
            foreach (var kv in gives)
            {
                int before = Inventory.Count(kv.Key);
                int give = equipIds.Contains(kv.Key) ? Mathf.Max(0, kv.Value - before) : kv.Value; // equipment the character owns is its own
                items.Add(kv.Key); items.Add(give); items.Add(before);
                if (give > 0) want.Add((kv.Key, before + give));
            }
            rec.items = items.ToArray(); UserSettings.SetCharPreset(rec); UserSettings.Save();
            foreach (var (id, total) in want) { W14KSend($"@item {id} {total - Inventory.Count(id)}"); yield return new WaitForSecondsRealtime(0.02f); }
            yield return W14KWait(() => want.All(w => Inventory.Count(w.id) >= w.total), 6f);
            var used = new HashSet<int>();
            foreach (var (id, loc) in p.Equip)
            {
                if (W14KSkipped.Contains(id)) continue;
                var it = Inventory.Items.Values.FirstOrDefault(x => x.ItemId == id && x.Wearing == 0 && !used.Contains(x.Index) && x.Identified);
                if (it == null) { Debug.Log($"[Presets] nothing unworn of item {id} to wear"); continue; }
                string why = EquipRules.WhyNot(it, Inventory, Player.BaseLevel); // client wave 18: e.g. arrows without a bow in hand
                if (why != null) { Debug.Log($"[Presets] {id} {it.Info.English} not worn: {why}"); continue; }
                used.Add(it.Index);
                // client wave 18 (w18gear): an item rAthena puts on ONE side (a dual-wielder's dagger, an accessory: EquipmentWindow.SideChoice)
                // goes to the RIGHT side unless the data names one; asked for both, rAthena takes the free side (pc.cpp:12108-12115), so a weapon
                // the character kept in its right hand (a level too low for the preset's two-handed one) sent the first dagger left
                uint at = loc != 0 ? loc : EquipmentWindow.SideChoice(it)?.r ?? it.Location;
                Session.Equip(it.Index, at);
                var e = it; yield return W14KWait(() => e.Wearing != 0, 2f);
                Debug.Log($"[Presets] wore {id} at 0x{at:x}: {(e.Wearing != 0 ? $"worn (0x{e.Wearing:x})" : "NOT worn")}");
            }
            // 5. quick bar 1
            W14KStepTo(Lang.T("Filling the quick bar…"));
            var barItems = p.BarItems.Where(id => Inventory.Count(id) > 0).Take(9).ToList();
            var skills = p.Bar.Where(id => Prog.Skills.TryGetValue(id, out var k) && k.Level > 0 && !k.Passive).Take(36 - barItems.Count).ToList();
            Session.BeginPacketBatch();
            for (int s = 0; s < 36; s++)
            {
                int itemAt = s - (36 - barItems.Count);
                if (s < skills.Count) W14KSlot(s, true, skills[s], Prog.Skills[skills[s]].Level);
                else if (itemAt >= 0) W14KSlot(s, false, barItems[itemAt], 0);
                else W14KSlot(s, false, 0, 0);
            }
            Session.EndPacketBatch();
            Hotkeys.ShowRow(0);
            rec.preset = p.Key; UserSettings.SetCharPreset(rec); UserSettings.Save();
            Debug.Log($"[Presets] {p.Key} ready: job {Entities.Self?.Job}, lv {Player.BaseLevel} / {Player.JobLevel}, {skills.Count} skills and {barItems.Count} items on quick bar 1, {want.Count} item kinds given, {W14KSkipped.Count} left out");
            if (W14KSkipped.Count > 0) Chat(Lang.F("Above your level, left out: {0}", string.Join(", ", W14KSkipped.Select(id => GameDb.GetItem(id).Name))), "9ad1ff");
            yield return W14KDone(Lang.F("Preset ready: {0} · {1}", SkillTreeDb.JobName(p.Job, sex), CharPresets.StyleName(p)));
        }

        /// <summary>@baselevelup / @joblevelup by the difference to these levels (a negative one lowers them), waited for.</summary>
        private IEnumerator W14KLevels(int baseLv, int jobLv)
        {
            int db = baseLv - Player.BaseLevel;
            if (db != 0) { W14KSend($"@baselevelup {db}"); yield return W14KWait(() => Player.BaseLevel != baseLv - db, 3f); }
            int dj = jobLv - Player.JobLevel;
            if (dj != 0) { W14KSend($"@joblevelup {dj}"); yield return W14KWait(() => Player.JobLevel != jobLv - dj, 3f); }
        }

        // ------------------------------------------------------------------ client wave 18 (w18gear), point 3
        /// <summary>The server lines kept out of the chat by the running preset (the @stats answer is read from them).</summary>
        private readonly List<string> _w14kLines = new List<string>();
        /// <summary>The status and trait points the server gave the last preset's base level (after @resetstat: pc_resetstate), for the log
        /// and the smoke.</summary>
        public int W14KStatPointsSeen { get; private set; }
        public int W14KTraitPointsSeen { get; private set; }
        /// <summary>The equipment the last preset left out (above the character's level: rAthena's pc_isequip), by item id.</summary>
        public List<int> W14KSkipped { get; } = new List<int>();

        /// <summary>A GM command, waited for: every command sent this way answers with one line (clif_displaymessage, sent after the command's
        /// own updates for @resetskill / @resetstat / @skpoint / @jobchange), so once it arrived the server's answers to it, and to everything
        /// sent before it, are in (one TCP stream; the server takes the commands in order).</summary>
        private IEnumerator W14KCmd(string line, float seconds = 4f)
        {
            int n = W14KQuietLines;
            W14KSend(line);
            yield return W14KWait(() => W14KQuietLines > n, seconds);
        }

        /// <summary>Basic Skill's level (NV_BASIC 1): @resetskill keeps it for a character that is not a Novice (pc.cpp:9495-9497).</summary>
        private int W14KBasicSkill() => Prog.Skills.TryGetValue(1, out var k) ? k.Level : 0;
        private bool W14KHas(int skill) => Prog.Skills.TryGetValue(skill, out var k) && k.Level > 0;

        /// <summary>The free skill points set to <paramref name="free"/> (@skpoint by the difference, atcommand.cpp:2873-2914), after a W14KCmd
        /// so the count shown is the server's.</summary>
        private IEnumerator W14KPool(int free)
        {
            free = Mathf.Max(0, free);
            int d = free - Player.SkillPoint;
            if (d != 0) { yield return W14KCmd($"@skpoint {d}"); yield return W14KWait(() => Player.SkillPoint == free, 2f); }
            Debug.Log($"[Presets] free skill points {Player.SkillPoint} (wanted {free})");
        }

        /// <summary>Walks a job's path: each job of it (@jobchange: the job level becomes 1, pc.cpp:10920) raised to the job level it is left at.
        /// The next change then records that level as rAthena's job-change level of its tier (pc_jobchange, pc.cpp:10881-10893), the levels the
        /// server gates the skill tree by (pc_calc_skilltree_normalize_job_sub, pc.cpp:2893-2976), as for a real character.</summary>
        private IEnumerator W14KWalk(IList<(int job, int jobLevel)> walk, System.Action<bool> done)
        {
            foreach (var (job, lv) in walk)
            {
                if (Entities.Self.Job != job)
                {
                    yield return W14KCmd($"@jobchange {job}");
                    yield return W14KWait(() => Entities.Self != null && Entities.Self.Job == job, 5f);
                    if (Entities.Self == null || Entities.Self.Job != job) { Debug.Log($"[Presets] the path: @jobchange {job} refused"); done(false); yield break; }
                }
                int d = lv - Player.JobLevel;
                if (d != 0) { yield return W14KCmd($"@joblevelup {d}"); yield return W14KWait(() => Player.JobLevel == lv, 3f); }
                Debug.Log($"[Presets] the path: job {job} at job level {Player.JobLevel}");
            }
            done(true);
        }

        /// <summary>No preset's path: the job level a job of the path is left at = the job-change level the character had for the change it
        /// makes into <paramref name="next"/> (pc_jobchange's rule, pc.cpp:10881-10893: a 4th job's, else a 3rd job's, else a 2nd job's), the
        /// path's own level when that was not known (0: rAthena itself then assumes the job's maximum, pc.cpp:2915-2919).</summary>
        private static int W14KChangeLevel(UserSettings.CharPreset rec, int job, int next, int own)
        {
            long a = EquipRules.MapIdOf(job), b = EquipRules.MapIdOf(next);
            if (a < 0 || b < 0 || rec.origChange == null || rec.origChange.Length < 3) return own;
            long fourth = (long)EquipRules.Const("JOBL_FOURTH"), third = (long)EquipRules.Const("JOBL_THIRD"), two = (long)(EquipRules.Const("JOBL_2_1") | EquipRules.Const("JOBL_2_2"));
            int at = (b & fourth) != 0 && (a & fourth) == 0 ? 2 : (b & third) != 0 && (a & third) == 0 ? 1 : (b & two) != 0 && (a & two) == 0 ? 0 : -1;
            return at >= 0 && rec.origChange[at] > 0 ? rec.origChange[at] : own;
        }

        /// <summary>Spends the free skill points as a player does in the skill window (CZ_UPGRADE_SKILLLEVEL 0x0112 → pc_skillup, pc.cpp:9129-9178,
        /// one packet a level; the server opens the next tier only once the earlier one's points are spent): <see cref="W14KNextSkills"/> picks
        /// each batch, the server's own answers (its skill list, its free points) decide the next.
        /// Client wave 18 (w18gear), the design's answer to the flag "a level-1 character gets no skills of the chosen job's tier": "raise the job level
        /// enough to learn its skills". So first the job's OWN tier's quick-bar skills (<see cref="W14KTier"/>) are learned, each at level 1 with
        /// its prerequisites, the earlier tiers' gates filled from their own points; when the free points run out before they are all learned,
        /// the job level goes up (one skill point a job level: pc_checkjoblevelup pc.cpp:8333-8334, @joblevelup atcommand.cpp:1761-1764; a skill
        /// with a skill_tree JobLevel rule is listed only from that job level on: pc_calc_skilltree pc.cpp:2722-2727, 2857), up to the job's
        /// maximum; the levels the tier did not use go back at the end (@joblevelup -n takes free points first, atcommand.cpp:1774-1783), never
        /// below the level the character had. Then the rest of the points as before.</summary>
        private IEnumerator W14KLearn(CharPresets.Preset p)
        {
            var blocked = new HashSet<int>(); int rounds = 0;
            var (own, tier) = W14KTier(p);
            int from = Player.JobLevel, raised = 0;
            while (tier.Any(s => !W14KHas(s)) && rounds++ < 400)
            {
                var batch = Player.SkillPoint > 0 ? W14KNextSkills(p, blocked, own, tier) : new List<int>();
                if (batch.Count > 0) { yield return W14KSkillBatch(batch, blocked); continue; }
                if (Player.JobLevel >= p.JobLevel) break;
                int up = Mathf.Clamp(W14KTierCost(p, tier) - Player.SkillPoint, 1, p.JobLevel - Player.JobLevel), target = Player.JobLevel + up;
                yield return W14KCmd($"@joblevelup {up}");
                yield return W14KWait(() => Player.JobLevel == target, 3f);
                raised += up; blocked.Clear(); // a new job level may open what was held back
                Debug.Log($"[Presets] {p.Key}: job level {Player.JobLevel} to learn the tier's skills ({tier.Count(s => !W14KHas(s))} of {tier.Count} not learned yet)");
            }
            if (raised > 0 && Player.SkillPoint > 0)
            {
                int back = Mathf.Min(raised, Player.SkillPoint), target = Player.JobLevel - back;
                yield return W14KCmd($"@joblevelup {-back}");
                yield return W14KWait(() => Player.JobLevel == target, 3f);
            }
            W14KJobRaise = (from, Player.JobLevel, Player.SkillPoint, tier.Count(W14KHas), tier.Count);
            Debug.Log($"[Presets] {p.Key}: the job's own tier: {tier.Count(W14KHas)} of {tier.Count} quick-bar skills learned [{string.Join(" ", tier)}]; job level {from} -> {Player.JobLevel}, {Player.SkillPoint} free");
            blocked.Clear(); rounds = 0;
            while (Player.SkillPoint > 0 && rounds++ < 400)
            {
                var batch = W14KNextSkills(p, blocked);
                if (batch.Count == 0) break;
                yield return W14KSkillBatch(batch, blocked);
            }
        }

        /// <summary>One skill-up batch sent and waited for (the free points drop by its size, or stop moving); a batch the server raised none
        /// of is blocked.</summary>
        private IEnumerator W14KSkillBatch(List<int> batch, HashSet<int> blocked)
        {
            int before = Player.SkillPoint, seen = before;
            Session.BeginPacketBatch();
            foreach (int id in batch) Session.SkillUp(id);
            Session.EndPacketBatch();
            float t0 = Time.realtimeSinceStartup, moved = t0;
            while (Time.realtimeSinceStartup - t0 < 4f)
            {
                if (Player.SkillPoint != seen) { seen = Player.SkillPoint; moved = Time.realtimeSinceStartup; }
                if (seen <= before - batch.Count) break;
                if (seen != before && Time.realtimeSinceStartup - moved > 0.2f) break;
                if (seen == before && Time.realtimeSinceStartup - t0 > 2f) break;
                yield return null;
            }
            yield return null; // the skill list follows each level (clif_skillinfoblock, pc.cpp:9175-9176)
            if (Player.SkillPoint == before) { foreach (int id in batch) blocked.Add(id); Debug.Log($"[Presets] skills {string.Join(" ", batch.Distinct())}: the server raised none"); }
        }

        /// <summary>Client wave 18 (w18gear): the last preset's job level before and after its own tier's skills (raised only as far as they
        /// needed), the free points then, and how many of that tier's quick-bar skills were learned of how many (log, smoke).</summary>
        public (int from, int to, int free, int learned, int of) W14KJobRaise { get; private set; }

        /// <summary>The skills of the job's OWN tier (the skill window's grouping, SkillTreeModel.Build: the job's inherit chain, a transcendent
        /// or baby job in the tier of the job it varies; the client data's grids, SkillTreeDb.ViewTreeOf) and the preset's quick-bar skills
        /// among them.</summary>
        public (HashSet<int> own, List<int> bar) W14KTier(CharPresets.Preset p)
        {
            var chain = SkillTreeDb.Chain(p.Job); chain.Reverse();
            var last = new List<int>();
            for (int i = 0; i < chain.Count; i++)
            {
                var pj = i > 0 ? SkillTreeDb.GetJob(chain[i - 1]) : null; var cj = SkillTreeDb.GetJob(chain[i]);
                bool variant = pj != null && cj != null && (cj.Const == pj.Const + "_H" || cj.Const == pj.Const + "_B");
                if (!variant) last = new List<int>();
                last.Add(chain[i]);
            }
            var own = new HashSet<int>();
            foreach (int j in last) { var t = SkillTreeDb.ViewTreeOf(j); if (t != null) own.UnionWith(t.Values); }
            return (own, p.Bar.Where(s => own.Contains(s) && SkillDb.Has(s)).ToList());
        }

        /// <summary>The points the tier's quick-bar skills not learned yet still cost (each at level 1 with its prerequisites, one plan: the skill
        /// window's planner, shared prerequisites counted once).</summary>
        private int W14KTierCost(CharPresets.Preset p, List<int> tier)
        {
            int job = Entities.Self != null ? Entities.Self.Job : p.Job;
            int Owned(int id) => Prog.Skills.TryGetValue(id, out var k) ? k.Level : 0;
            var plan = new SkillPlan();
            foreach (int s in tier) if (Owned(s) == 0) { var next = plan.Stage(s, job, Owned, int.MaxValue, out _); if (next != null) plan = next; }
            return plan.Cost(Owned);
        }

        /// <summary>The next skill-up batch (one id a level; empty: nothing more to do): A) the first of the preset's skills (its quick bar's, in
        /// their order) not learned yet whose chain fits the free points and can start now (it at level 1, its prerequisites at the levels it
        /// needs: the skill window's planner, Game/SkillPlan.cs); B) the first of them the server lets this character raise, to its maximum;
        /// C) anything else the server lets it raise, the lowest id first (an earlier tier's points: rAthena opens the next tier only once they
        /// are spent, pc.cpp:2893-2976; Basic Skill first of all).</summary>
        /// <summary>The preset's own quick-bar skills followed by any skills from its line's transcendent 2nd job preset of the same style
        /// (so 3rd and 4th class presets unlock and learn their transcendent 2nd class skills before maxing earlier skills).</summary>
        private List<int> W14KTargetSkills(CharPresets.Preset p)
        {
            var res = new List<int>(p.Bar);
            if (Presets == null) return res;
            foreach (int j in SkillTreeDb.Chain(p.Job))
            {
                if (!((j >= 4008 && j <= 4013) || (j >= 4015 && j <= 4021))) continue;
                var tp = CharPresets.All.FirstOrDefault(x => x.Job == j && x.Style == p.Style) ?? CharPresets.All.FirstOrDefault(x => x.Job == j);
                if (tp != null) foreach (int s in tp.Bar) if (!res.Contains(s)) res.Add(s);
            }
            return res;
        }

        public List<int> W14KNextSkills(CharPresets.Preset p, HashSet<int> blocked, HashSet<int> own = null, List<int> only = null)
        {
            var batch = new List<int>();
            int job = Entities.Self != null ? Entities.Self.Job : p.Job, points = Player.SkillPoint;
            if (points <= 0) return batch;
            int Owned(int id) => Prog.Skills.TryGetValue(id, out var k) ? k.Level : 0;
            bool CanRaise(int id) => !blocked.Contains(id) && Prog.Skills.TryGetValue(id, out var k) && k.Upgradable;
            void Levels(int id) { int n = Mathf.Clamp(SkillDb.Get(id).MaxLevel - Owned(id), 1, points); for (int i = 0; i < n; i++) batch.Add(id); }
            var targets = only ?? W14KTargetSkills(p);
            // client wave 18: with only (the job's own tier's skills, W14KLearn's first part): A for those alone, no B, and C only while the
            // job's own tier is still closed (an earlier tier's gate)
            foreach (int s in targets)
            {
                if (blocked.Contains(s) || Owned(s) > 0 || !SkillDb.Has(s)) continue;
                // (with only: the whole chain whatever it costs, the job level comes after: its earlier tiers' prerequisites are bought from
                // those tiers' own points before anything else fills their gate)
                var plan = new SkillPlan().Stage(s, job, Owned, only != null ? int.MaxValue : points, out var why);
                if (plan == null || why != SkillPlan.Refusal.None) continue;
                var order = plan.UpgradeOrder(job, Owned, points);
                if (order == null || order.Count == 0 || order.Any(blocked.Contains) || !CanRaise(order[0])) continue;
                return order;
            }
            if (only == null) foreach (int s in targets) if (CanRaise(s)) { Levels(s); return batch; }
            if (only != null && own != null && own.Any(Prog.Skills.ContainsKey)) return batch; // the job's own tier is open: nothing else now
            foreach (var k in Prog.Skills.Values) if (CanRaise(k.Id) && SkillTreeDb.ExtraOf(k.Id).Type != "Quest") { Levels(k.Id); return batch; }
            return batch;
        }

        /// <summary>Slot s (0-35) of quick bar 1 (rows 1-4), sent only when it changes.</summary>
        private void W14KSlot(int s, bool isSkill, int id, int count)
        {
            var h = Hotkeys.At(s / 9, s % 9);
            if (h.Id == id && (id == 0 || (h.IsSkill == isSkill && h.Count == count))) return;
            Hotkeys.SetAt(s / 9, s % 9, isSkill, id, count);
        }

        private IEnumerator W14KDone(string line)
        {
            W14KStep = "";
            yield return new WaitForSecondsRealtime(1.5f); // the server's last answers are kept out of the chat too
            _w14kQuiet = false; W14KBusy = false;
            Debug.Log($"[Presets] done: '{line}'; {W14KQuietLines} server lines kept out of the chat, {W14KRefusals} of them refusals");
            Chat(W14KRefusals > 0 ? line + " " + Lang.F("The server refused {0} steps.", W14KRefusals) : line, W14KRefusals > 0 ? "ffb070" : "9ad1ff");
            Commands?.Refresh();
        }
    }
}
