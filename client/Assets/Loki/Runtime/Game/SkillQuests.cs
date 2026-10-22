using System.Collections.Generic;
using System.Text;

namespace Loki.Game
{
    /// <summary>
    /// Client wave 22 (w22data), point 64 of the design's wave 17 list ("Skills that are unlocked via quests should have their required quests
    /// named, and the method to start them explained"): the quest of every quest skill.
    ///   The facts   read from rAthena's own NPC scripts (the oracle's e985006), each entry with its file and lines in <see cref="Info.Source"/>:
    ///               who teaches it (the NPC's name as the server sends it, the same in every language), where (the map and cell, which
    ///               a player can walk or /navi to), what the script checks (the job level; "in the first job": the scripts also pass
    ///               anyone already in a second job) and what it takes (<see cref="Info.Items"/>, <see cref="Info.Zeny"/>).
    ///   The texts   <see cref="Info.QuestName"/> and <see cref="Info.HowToStart"/> are Lang keys (English here, pt-BR in
    ///               Resources/Lang/pt-BR.txt), written for Loki: nothing is copied from the scripts' dialogue or the game client.
    ///               The quests have no names in rAthena (no quest id), so each is named after its NPC. Job names are left out on
    ///               purpose (the skill sits in its job's tree; the player's own client data names the jobs).
    ///   The items   are ids: <see cref="ItemsText"/> names them from the player's own client data (GameDb), in the player's language.
    /// The interface is the one the 3D skill tree calls (<see cref="For"/>, <see cref="Info.QuestName"/>,
    /// <see cref="Info.HowToStart"/>); <see cref="Text"/> adds the item line for whoever wants the whole explanation.
    /// The quest skills are rAthena's skills with Flags IsQuest (db/re/skill_db.yml); the smoke w22data checks every one has an entry.
    /// On this server a rebirth's first-job change gives the first-job quest skills back without the quest (npc/re/jobs/1-1/*.txt, the
    /// Upper == 1 branch).
    /// </summary>
    public static class SkillQuests
    {
        public sealed class Info
        {
            /// <summary>Lang keys: the quest's name, and how to start it (who, where, what is needed).</summary>
            public string QuestName = "", HowToStart = "";
            /// <summary>The NPC as the server names it, its map and cell.</summary>
            public string Npc = "", Map = "";
            public int X, Y;
            /// <summary>What the NPC takes: (item id, amount) pairs; and Zeny.</summary>
            public int[] Items = new int[0];
            public int Zeny;
            /// <summary>Where it was read: rAthena file and lines.</summary>
            public string Source = "";
        }

        private static Dictionary<int, Info> _of;

        /// <summary>Null, or the quest that teaches this skill.</summary>
        public static Info For(int skillId) { Load(); return _of.TryGetValue(skillId, out var i) ? i : null; }

        /// <summary>Every quest skill of the table.</summary>
        public static IEnumerable<int> Skills { get { Load(); return _of.Keys; } }

        /// <summary>"Items: 10 Fire Arrow, 5 Royal Jelly · 10 000 Zeny" in the player's language, from the player's own item names; ""
        /// when the quest takes nothing.</summary>
        public static string ItemsText(Info q)
        {
            if (q == null || (q.Items.Length == 0 && q.Zeny == 0)) return "";
            var sb = new StringBuilder();
            for (int i = 0; i + 1 < q.Items.Length; i += 2)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(q.Items[i + 1]).Append(' ').Append(GameDb.GetItem(q.Items[i]).Name);
            }
            if (q.Zeny > 0) { if (sb.Length > 0) sb.Append(" · "); sb.Append(Lang.F("{0:N0} Zeny", q.Zeny)); }
            return Lang.F("Items: {0}", sb.ToString());
        }

        /// <summary>How to start the quest and, on a second line, what it takes; "" for a skill without a quest.</summary>
        public static string Text(int skillId)
        {
            var q = For(skillId);
            if (q == null) return "";
            string items = ItemsText(q);
            return items.Length > 0 ? Lang.T(q.HowToStart) + "\n" + items : Lang.T(q.HowToStart);
        }

        private static void Q(int skill, string npc, string map, int x, int y, string name, string how, string source, int zeny, params int[] items)
        {
            _of[skill] = new Info { QuestName = name, HowToStart = how, Npc = npc, Map = map, X = x, Y = y, Items = items, Zeny = zeny, Source = source };
        }

        private static void Load()
        {
            if (_of != null) return;
            _of = new Dictionary<int, Info>();
            const string S = "npc/quests/skills/";

            // ---- Novice (npc/re/jobs/novice/academy.txt; the classic novice_skills.txt is not loaded: npc/scripts_jobs.conf:63)
            Q(142, "Therapist", "iz_ac01", 59, 43, Lang.N("Skill quest: Therapist"),
              Lang.N("Talk to Therapist in the Izlude academy (iz_ac01 59, 43) and ask for the skill."),
              "npc/re/jobs/novice/academy.txt:2064-2129", 0);
            Q(143, "Instructor Argos", "izlude", 140, 260, Lang.N("Skill quest: Instructor Argos"),
              Lang.N("Before your first job, talk to Instructor Argos in Izlude (izlude 140, 260). Wait 20 seconds, then talk again."),
              "npc/re/jobs/novice/academy.txt:2414-2554 (quest 15001, db/re/quest_db.yml:11014-11016)", 0);

            // ---- Swordman
            Q(144, "Knight De Thomas", "izlude_in", 118, 175, Lang.N("Skill quest: Knight De Thomas"),
              Lang.N("Talk to Knight De Thomas in Izlude, indoors (izlude_in 118, 175). Needs job level 35 in the first job. Asks for items."),
              S + "swordman_skills.txt:20-46, npc/re/quests/skills/swordman_skills.txt:15", 0, 713, 200, 1058, 1);
            Q(145, "Leon Von Frich", "prt_in", 75, 88, Lang.N("Skill quest: Leon Von Frich"),
              Lang.N("Talk to Leon Von Frich in Prontera, indoors (prt_in 75, 88). Needs job level 25 in the first job. Asks for items."),
              S + "swordman_skills.txt:152-179", 0, 1752, 10, 1751, 10, 532, 1, 962, 30, 526, 5);
            Q(146, "Juan", "prt_in", 94, 57, Lang.N("Skill quest: Juan"),
              Lang.N("Talk to Juan in Prontera, indoors (prt_in 94, 57). Needs job level 30 in the first job. Asks for items."),
              S + "swordman_skills.txt:305-345", 0, 924, 35, 958, 10, 957, 10, 518, 10);

            // ---- Mage
            Q(157, "Great Wizard", "geffen_in", 151, 119, Lang.N("Skill quest: Great Wizard"),
              Lang.N("Talk to Great Wizard in Geffen, indoors (geffen_in 151, 119). Needs job level 35 in the first job. Asks for items."),
              S + "mage_skills.txt:18-122", 0, 746, 3, 730, 1, 935, 5, 943, 1);

            // ---- Archer
            Q(147, "Roberto", "moc_ruins", 118, 99, Lang.N("Skill quest: Roberto"),
              Lang.N("Talk to Roberto in the Morocc ruins (moc_ruins 118, 99). Needs job level 30 in the first job. Asks for items."),
              S + "archer_skills.txt:18-54", 0, 907, 20, 921, 7, 906, 41, 1019, 13, 501, 1);
            Q(148, "Jason", "payon", 103, 63, Lang.N("Skill quest: Jason"),
              Lang.N("Talk to Jason in Payon (payon 103, 63). Needs job level 35 in the first job. Asks for items."),
              S + "archer_skills.txt:144-227", 0, 721, 2, 942, 3, 962, 10, 925, 10, 532, 36);

            // ---- Acolyte
            Q(156, "Cleric", "prt_church", 173, 23, Lang.N("Skill quest: Cleric"),
              Lang.N("Talk to Cleric in the Prontera church (prt_church 173, 23). Needs job level 30 in the first job. Asks for items."),
              S + "acolyte_skills.txt:18-82", 0, 727, 1, 991, 1, 2608, 1);

            // ---- Merchant
            Q(155, "Necko", "alberta", 83, 96, Lang.N("Skill quest: Necko"),
              Lang.N("Talk to Necko in Alberta (alberta 83, 96). Needs job level 15 in the first job. Asks for items."),
              S + "merchant_skills.txt:18-82", 0, 722, 7, 532, 1, 921, 50);
            Q(154, "Charlron", "alberta", 119, 221, Lang.N("Skill quest: Charlron"),
              Lang.N("Talk to Charlron in Alberta (alberta 119, 221). Needs job level 30 in the first job. Asks for items."),
              S + "merchant_skills.txt:182-226", 0, 1019, 50, 998, 10, 919, 20);
            // one of three lists at random each talk (361-524): the largest amounts are shown
            Q(153, "Gershaun", "alberta", 232, 103, Lang.N("Skill quest: Gershaun"),
              Lang.N("Talk to Gershaun in Alberta (alberta 232, 103). Needs job level 35 in the first job. Asks for items; the amounts change a little each time."),
              S + "merchant_skills.txt:335-524, npc/re/cities/alberta.txt:47", 0, 533, 2, 998, 23, 938, 32, 601, 23, 962, 6);
            Q(2544, "Cart Remodel Expert", "alberta", 47, 37, Lang.N("Skill quest: Cart Remodel Expert"),
              Lang.N("Talk to Cart Remodel Expert in Alberta (alberta 47, 37). Asks for one item."),
              "npc/re/quests/skills/merchant_skills.txt:10-82", 0, 6606, 1);
            Q(2535, "Purchasing Team", "alberta_in", 58, 52, Lang.N("Skill quest: Purchasing Team"),
              Lang.N("Talk to Purchasing Team in Alberta, indoors (alberta_in 58, 52). Needs the Vending skill. Costs Zeny."),
              "npc/merchants/buying_shops.txt:104-228", 10000);

            // ---- Thief (one NPC, four skills; the first also needs a bag made by another NPC)
            Q(149, "Alcouskou", "moc_prydb1", 154, 128, Lang.N("Skill quest: Alcouskou and the Bag Seller"),
              Lang.N("Talk to Alcouskou in the Thief guild (moc_prydb1 154, 128). Needs job level 25 in the first job. Asks for items, then for a bag from the Bag Seller in Payon (payon 91, 77), who asks for three more."),
              S + "thief_skills.txt:19-151, 434-474", 0, 7041, 5, 952, 1, 1055, 1, 1025, 1);
            Q(150, "Alcouskou", "moc_prydb1", 154, 128, Lang.N("Skill quest: Alcouskou"),
              Lang.N("Talk to Alcouskou in the Thief guild (moc_prydb1 154, 128). Needs job level 35 in the first job. Asks for items."),
              S + "thief_skills.txt:19, 155-173", 0, 940, 20);
            Q(151, "Alcouskou", "moc_prydb1", 154, 128, Lang.N("Skill quest: Alcouskou"),
              Lang.N("Talk to Alcouskou in the Thief guild (moc_prydb1 154, 128). Needs job level 20 in the first job. Asks for items."),
              S + "thief_skills.txt:19, 232-265", 0, 912, 1, 948, 1, 908, 5);
            Q(152, "Alcouskou", "moc_prydb1", 154, 128, Lang.N("Skill quest: Alcouskou"),
              Lang.N("Talk to Alcouskou in the Thief guild (moc_prydb1 154, 128). Needs job level 15 in the first job. Asks for items."),
              S + "thief_skills.txt:19, 322-347", 0, 910, 2, 911, 2);

            // ---- Knight: the knights stand together in Prontera (prontera 66-78, 351-358; the script moves on at each: 311-458)
            Q(1001, "Knight", "prt_in", 85, 99, Lang.N("Skill quest: Knight"),
              Lang.N("Talk to Knight in Prontera, indoors (prt_in 85, 99). Then visit the knights in Prontera (prontera 72, 352), come back and bring items."),
              S + "knight_skills.txt:16-289 (start), 311-458 (the knights), 126-183 (items), 93-115", 0, 530, 5, 748, 3);

            // ---- Crusader: the last step can fail and is tried again (466-539, 767-781)
            Q(1002, "Ford", "geffen", 110, 117, Lang.N("Skill quest: Ford"),
              Lang.N("Talk to Ford in Geffen (geffen 110, 117). Then the Soldier (gef_fild13 297, 242) and the Pastor in the Prontera church (prt_church 87, 127), who ask for items."),
              S + "crusader_skills.txt:16-105 (start), 280-539 (Soldier), 581-745 (Pastor)", 0, 501, 1, 938, 20, 619, 1, 909, 5, 514, 1, 1003, 1, 7053, 3, 713, 3);

            // ---- Wizard
            Q(1006, "Meow", "gef_tower", 115, 36, Lang.N("Skill quest: Meow"),
              Lang.N("Talk to Meow in the Geffen tower (gef_tower 115, 36), then to Simon Mayace in Prontera, indoors (prt_in 82, 98). Bring items back to Meow."),
              S + "wizard_skills.txt:17-298, 356-422", 0, 991, 10, 993, 10, 990, 10, 992, 10);

            // ---- Hunter
            Q(1009, "Arpesto", "pay_arche", 109, 169, Lang.N("Skill quest: Arpesto"),
              Lang.N("Talk to Arpesto in the archer village of Payon (pay_arche 109, 169). Needs job level 40. Asks for items."),
              S + "hunter_skills.txt:27-344 (job level: 240), 92-215", 0, 724, 5, 7115, 5, 537, 30);

            // ---- Assassin: two NPCs named Assassin stand side by side; the second quest needs the first (ASSN_SK: 236, 587)
            Q(1004, "Assassin", "in_moc_16", 14, 27, Lang.N("Skill quest: Assassin"),
              Lang.N("Talk to Assassin in the Assassin guild (in_moc_16 14, 27) and ask to learn it."),
              S + "assassin_skills.txt:17-237", 0);
            Q(1003, "Assassin", "in_moc_16", 23, 27, Lang.N("Skill quest: the second Assassin"),
              Lang.N("After the first Assassin's lesson, talk to the Assassin beside him (in_moc_16 23, 27). Needs the skill it improves. Sends you for a jewel in the Sphinx or the pyramid."),
              S + "assassin_skills.txt:289-697 (start: 587-697, the skill needed: 647), 739-983 (the errands), 477-542", 0);

            // ---- Rogue: no items; an arena test through the guild's waiting room (in_rogue 88, 119: 1341-1354)
            Q(1005, "Haijara Greg", "in_rogue", 355, 179, Lang.N("Skill quest: Haijara Greg"),
              Lang.N("Talk to Haijara Greg in the Rogue guild (in_rogue 355, 179). A chain of talks with his three brothers in the guild, and a test in an arena."),
              S + "rogue_skills.txt:134-508, 544-1035 (the brothers), 1038-1354 (the arena)", 0);

            // ---- Sage: one NPC, two lessons; only ONE of the four elements is taught (SAG_SK 100 after it: 454)
            Q(1007, "Mischna", "yuno_in03", 176, 24, Lang.N("Skill quest: Mischna"),
              Lang.N("Talk to Mischna in Juno, indoors (yuno_in03 176, 24). Asks for items."),
              S + "sage_skills.txt:17-319", 0, 904, 10, 947, 7, 1013, 12, 946, 10, 7433, 4);
            string lesson2 = Lang.N("Skill quest: Mischna's second lesson");
            string element = Lang.N("After her first lesson, talk to Mischna again (yuno_in03 176, 24). Needs the endow skill of the same element. Asks for items. She teaches only one of the four elements.");
            Q(1018, "Mischna", "yuno_in03", 176, 24, lesson2, element, S + "sage_skills.txt:338-455 (fire: skill 280, item 990)", 0, 990, 20, 1089, 1, 1088, 1);
            Q(1017, "Mischna", "yuno_in03", 176, 24, lesson2, element, S + "sage_skills.txt:338-455 (earth: skill 283, item 993)", 0, 993, 20, 1089, 1, 1088, 1);
            Q(1019, "Mischna", "yuno_in03", 176, 24, lesson2, element, S + "sage_skills.txt:338-455 (wind: skill 282, item 992)", 0, 992, 20, 1089, 1, 1088, 1);
            Q(1008, "Mischna", "yuno_in03", 176, 24, lesson2, element, S + "sage_skills.txt:338-455 (water: skill 281, item 991)", 0, 991, 20, 1089, 1, 1088, 1);

            // ---- Bard: the drink costs 1 000 Zeny at the Bartender (1168-1208); the Geffen Bard quest is npc/quests/bard_quest.txt (32)
            Q(1010, "Young Man", "prontera", 174, 328, Lang.N("Skill quest: Young Man"),
              Lang.N("Bring a drink from the Bartender in Morocc (morocc_in 166, 76) to Young Man in Prontera (prontera 174, 328). Needs job level 40 and the Bard quest of Geffen (Bard, geffen 132, 38). Then the Bard beside the Bartender sends you to Niflheim for eggs."),
              S + "bard_skills.txt:30-207 (job level: 199), 382-793 (the Geffen quest: 663), 907-1041 + 1251-1274 (the eggs), 541-575", 1000, 12112, 1, 574, 5);

            // ---- Dancer
            Q(1011, "Canell", "comodo", 204, 172, Lang.N("Skill quest: Canell"),
              Lang.N("Talk to Canell in Comodo (comodo 204, 172). Needs job level 40. Asks for an item, then sends you to Aelle in Prontera (prontera 183, 333), who asks for more."),
              S + "dancer_skills.txt:18-363 (job level: 19), 403-620 (Aelle)", 0, 747, 1, 970, 1, 512, 1, 513, 3, 538, 5, 736, 1);

            // ---- Blacksmith
            Q(1012, "Akkie", "geffen", 178, 72, Lang.N("Skill quest: Akkie"),
              Lang.N("Talk to Akkie in Geffen (geffen 178, 72). Needs job level 30. Asks for items and Zeny over several visits."),
              S + "blacksmith_skills.txt:16-417 (job level: 66; items: 217-271, 317-335, 367-389)", 500, 999, 2, 1003, 8, 971, 1, 613, 2);
            Q(1013, "Goodman", "geffen", 172, 52, Lang.N("Skill quest: Goodman"),
              Lang.N("Talk to Goodman in Geffen (geffen 172, 52). Then fill your bag until less than 500 weight is free, and come back."),
              S + "blacksmith_skills.txt:471-638 (the weight: 575)", 0);

            // ---- Alchemist: no items; on this server a Creator's job change gives it too (npc/jobs/2-2a/Creator.txt:46, 115)
            Q(238, "Kellasus", "lhz_in01", 224, 140, Lang.N("Skill quest: Kellasus"),
              Lang.N("Talk to Kellasus in Lighthalzen, indoors (lhz_in01 224, 140). A chain of talks with people around Lighthalzen."),
              S + "alchemist_skills.txt:1152-1719 (start: 1694-1719; the skill: 1208), 1745-2109 (the others)", 0);

            // ---- Priest: a chance to fail; a failed try takes one of each item (325-326)
            Q(1014, "Sister Linus", "prt_church", 111, 112, Lang.N("Skill quest: Sister Linus"),
              Lang.N("Talk to Sister Linus in the Prontera church (prt_church 111, 112). Needs your revive skill. Asks for items; the lesson can fail and be tried again."),
              S + "priest_skills.txt:17-313 (the skill needed: 264; the chance: 288-301)", 0, 523, 30, 717, 20);

            // ---- Monk: two skills, one chain (MONK_SK)
            Q(1015, "Monk", "monk_test", 316, 69, Lang.N("Skill quest: Monk and Apprentice Monk"),
              Lang.N("Talk to Monk in the Monk abbey (monk_test 316, 69), then to Apprentice Monk outside (prt_monk 270, 198). Go back to the Apprentice carrying nothing at all."),
              S + "monk_skills.txt:214, 380-416 (start), 16, 171-200, 91-150 (Weight == 0: 92)", 0);
            Q(1016, "Monk", "monk_test", 316, 69, Lang.N("Skill quest: Monk"),
              Lang.N("After the Apprentice Monk's lesson, talk to Monk in the Monk abbey (monk_test 316, 69). Asks for items."),
              S + "monk_skills.txt:314-360, 266-283, 225-255", 0, 905, 20, 711, 3);

            // ---- Any job: the gym pass (not in a job tree; one item each level, to level 10)
            Q(681, "Ripped Cabus", "payon", 173, 141, Lang.N("Skill quest: Ripped Cabus"),
              Lang.N("Talk to Ripped Cabus in Payon (payon 173, 141). Takes one item for each level, up to level 10."),
              "npc/other/gympass.txt:20-82", 0, 7776, 1);
        }
    }
}
