using System.Collections.Generic;

namespace Loki.Game
{
    /// <summary>
    /// Client wave 4 (w4fx): the text a skill failure shows. ZC_ACK_TOUSESKILL 0x0110 (14 bytes: skillId.W btype.L itemId.L flag.B cause.B;
    /// rAthena clif_skill_fail, clif.cpp:6008-6031; packets_struct.hpp PACKET_ZC_ACK_TOUSESKILL) carries only a cause number
    /// (enum useskill_fail_cause, clif.hpp:405-521); the official client turns it into a msgstringtable line in its code. Where that line is
    /// known it is shown in the client's own words (ClientText.Msg: the player's msgstringtable, English or Portuguese):
    ///  (in this order: the rAthena-documented lines below win over rB's generic btype rule)
    ///  • roBrowserLegacy's mapping (Engine/MapEngine/Skill.js:208-302 — a reimplementation, the only reference for these): btype ≠ 0 → 204
    ///    "Skill has failed." (Basic Skill: 159 + btype for btype &lt; 7, 383 for 7; Warp Portal 214; Steal 205; Envenom 207), then Tarot Card
    ///    of Fate → 204, else cause 1 → 202, 2 → 203, 3 → 808, 4 → 219, 5 → 233, 6 → 239, 7 → 246, 8 → 247, 9 → 580, 10 → 285, 13 → 1398, 83 → 661;
    ///  • rAthena's own message ids for two causes (clif.hpp MSI_USESKILL_FAIL_MADOGEAR = 1435, MSI_USESKILL_FAIL_CART = 1519: the client's
    ///    MSI names for causes 33 and 57);
    ///  • cause 71 NEED_ITEM: rAthena quotes the client's line, "[%s] required '%d' amount." (skill.cpp:9484, 9565), whose English is
    ///    msgstringtable 1536 "[%s] need %d"; the item named as the client does (the unidentified name, clif.hpp:478) is ours: its name.
    /// Every other cause has no known client line: Loki says what rAthena's enum name means (its own sentence, marked Loki below) rather than
    /// guess a msgstringtable id. Cause 0 (USESKILL_FAIL_LEVEL) with btype 0 shows nothing in rB; Loki says the skill failed (its own text).
    /// design notes lists every cause and its source.
    /// </summary>
    public static class SkillFail
    {
        /// <summary>Where a cause's text comes from: "rB", "rAthena MSI", "rAthena quote" or "Loki".</summary>
        public static string SourceOf(int cause, int btype, int skill)
        {
            if (cause == 33 || cause == 57) return "rAthena MSI";
            if (cause == 71) return "rAthena quote";
            if (Rb(cause, btype, skill) != 0) return "rB";
            return "Loki";
        }

        private const int NvBasic = 1, AlWarp = 27, TfSteal = 50, TfPoison = 52, TarotCard = 489;
        private static readonly Dictionary<int, int> RbCause = new Dictionary<int, int>
        {
            { 1, 202 }, { 2, 203 }, { 3, 808 }, { 4, 219 }, { 5, 233 }, { 6, 239 }, { 7, 246 }, { 8, 247 }, { 9, 580 }, { 10, 285 }, { 13, 1398 }, { 83, 661 },
        };

        /// <summary>rB Skill.js:208-282: the msgstringtable id, 0 when rB shows nothing.</summary>
        private static int Rb(int cause, int btype, int skill)
        {
            int error = 0;
            if (btype != 0)
                switch (skill)
                {
                    case NvBasic: error = btype < 7 ? 159 + btype : btype == 7 ? 383 : 0; break;
                    case AlWarp: error = 214; break;
                    case TfSteal: error = 205; break;
                    case TfPoison: error = 207; break;
                    default: error = 204; break;
                }
            if (skill == TarotCard) error = 204;
            else if (RbCause.TryGetValue(cause, out int m)) error = m;
            return error;
        }

        /// <summary>The line to show (never empty: a cause rB leaves silent gets Loki's own sentence).</summary>
        public static string Text(int cause, int skill, int btype, int itemId)
        {
            string s = SkillDb.Get(skill).Name;
            // rAthena's own documentation of the client's line comes first: rB's generic "btype ≠ 0 → 204" would turn NEED_ITEM (btype = the
            // amount) into "Skill has failed." (rB Skill.js:217-221)
            switch (cause)
            {
                case 33: return ClientText.Msg(1435); // MSI_USESKILL_FAIL_MADOGEAR (clif.hpp:573)
                case 57: return ClientText.Msg(1519); // MSI_USESKILL_FAIL_CART (clif.hpp:576)
                case 71: return ClientText.Fill(ClientText.Fill(ClientText.Msg(1536), itemId > 0 ? GameDb.GetItem(itemId).Name : "?"), btype.ToString());
            }
            int rb = Rb(cause, btype, skill);
            if (rb != 0) return ClientText.Msg(rb);
            // Loki's own sentences: what rAthena's cause means (clif.hpp:405-521), one whole sentence each with the skill's name
            string f = Lang.T(LokiText(cause)); // w4lang: LokiText's sentences are table lines (Lang.N)
            string item = itemId > 0 ? GameDb.GetItem(itemId).Name : "";
            return string.Format(f, s, btype, item);
        }

        /// <summary>Loki's text for a cause with no known client line: {0} the skill, {1} btype (a count / a skill id), {2} the item's name.</summary>
        public static string LokiText(int cause)
        {
            switch (cause)
            {
                case 0: return Lang.N("{0} failed.");                                                   // USESKILL_FAIL_LEVEL (rB: silent)
                case 11: return Lang.N("{0} can't be used on that target.");                            // TOTARGET
                case 12: return Lang.N("{0}: you carry too many Ancillas.");                            // ANCILLA_NUMOVER
                case 14: return Lang.N("{0} needs an Ancilla.");                                        // ANCILLA
                case 15: return Lang.N("{0} can't be placed that close to another.");                   // DUPLICATE_RANGEIN
                case 16: return Lang.N("{0} needs another skill first.");                               // NEED_OTHER_SKILL
                case 17: return Lang.N("{0} needs a partner.");                                         // NEED_HELPER
                case 18: return Lang.N("{0} can't be used in that direction.");                         // INVALID_DIR
                case 19: return Lang.N("{0}: you can't summon any more.");                              // SUMMON
                case 20: return Lang.N("{0}: nothing summoned.");                                       // SUMMON_NONE
                case 21: return Lang.N("{0}: no skill to imitate.");                                    // IMITATION_SKILL_NONE
                case 22: return Lang.N("{0} can't be used again yet.");                                 // DUPLICATE
                case 23: return Lang.N("{0} can't be used in this state.");                             // CONDITION
                case 24: return Lang.N("{0} needs a Paintbrush.");                                      // PAINTBRUSH
                case 25: return Lang.N("{0} can't be used while riding a dragon.");                     // DRAGON
                case 26: return Lang.N("{0} can't be used on that spot.");                              // POS
                case 27: return Lang.N("{0}: your partner's SP is too low.");                           // HELPER_SP_INSUFFICIENT
                case 28: return Lang.N("{0} can't be used that close to a wall.");                      // NEER_WALL
                case 29: return Lang.N("{0} needs at least 1% base experience.");                       // NEED_EXP_1PERCENT
                case 30: return Lang.N("{0}: the chorus members' SP is too low.");                      // CHORUS_SP_INSUFFICIENT
                case 31: return Lang.N("{0} can only follow Weapon Blocking.");                         // GC_WEAPONBLOCKING
                case 32: return Lang.N("{0} needs a poisoned weapon.");                                 // GC_POISONINGWEAPON
                case 34: return Lang.N("{0} needs Kunai equipped.");                                    // NEED_EQUIPMENT_KUNAI
                case 35: return Lang.N("{0} can only target players.");                                 // TOTARGET_PLAYER
                case 36: return Lang.N("{0} can't be used on a target of that size.");                  // SIZE
                case 37: return Lang.N("{0} needs Cannonballs.");                                       // CANONBALL
                case 40: return Lang.N("{0} needs the Madogear to hover.");                             // MADOGEAR_HOVERING
                case 43: return Lang.N("{0} needs a Guillotine poison.");                               // GUILLONTINE_POISON
                case 50: return Lang.N("{0} can't be used while riding the Madogear.");                 // MADOGEAR_RIDE
                case 51: return Lang.N("{0} needs a spellbook.");                                       // SPELLBOOK
                case 52: return Lang.N("{0}: this spellbook is too difficult.");                        // SPELLBOOK_DIFFICULT_SLEEP
                case 53: return Lang.N("{0}: not enough spellbook preservation points.");               // SPELLBOOK_PRESERVATION_POINT
                case 54: return Lang.N("{0}: you are reading a spellbook.");                            // SPELLBOOK_READING
                case 60: return Lang.N("{0}: no more spells can be memorized.");                        // NO_MORE_SPELL
                case 70: return Lang.N("{0} failed.");                                                  // MANUAL_NOTIFY
                case 72: return Lang.N("{0} needs {2} equipped.");                                      // NEED_EQUIPMENT (itemId)
                case 73: return Lang.N("{0} must follow the right combo skill.");                       // COMBOSKILL (btype = that skill)
                case 74: return Lang.N("{0} needs {1} spirit sphere(s).");                              // SPIRITS (btype = count)
                case 75: return Lang.N("{0} needs Fury (Critical Explosion).");                         // EXPLOSIONSPIRITS
                case 76: return Lang.N("{0}: your HP is too high.");                                    // HP_TOOMANY
                case 77: return Lang.N("{0} needs a Banding partner.");                                 // NEED_ROYAL_GUARD_BANDING
                case 78: return Lang.N("{0} needs a different weapon class.");                          // NEED_EQUIPPED_WEAPON_CLASS
                case 79: return Lang.N("{0} needs a summoned elemental.");                              // EL_SUMMON
                case 80: return Lang.N("{0}: your relationship is not close enough.");                  // RELATIONGRADE
                case 81: return Lang.N("{0} needs the Fighter style.");                                 // STYLE_CHANGE_FIGHTER
                case 82: return Lang.N("{0} needs the Grappler style.");                                // STYLE_CHANGE_GRAPPLER
                case 84: return Lang.N("{0} needs more bullets.");                                      // NEED_MORE_BULLET
                case 85: return Lang.N("{0} needs {1} coin(s).");                                       // COINS (btype = count)
                case 86: return Lang.N("{0} needs the spirit communion.");                              // SH_TEMPORARY_COMMUNION
                case 87: return Lang.N("{0} can't be used on this map.");                               // MAP
                case 88: return Lang.N("{0}: the summon's SP is too low.");                             // NEED_SUMMON_SP
                case 89: return Lang.N("{0}: another shop is already open here.");                      // SAME_VEND
                case 90: return Lang.N("{0} needs bullets.");                                           // NEED_BULLETS
                case 91: return Lang.N("{0} needs arrows.");                                            // NEED_ARROWS
                case 92: return Lang.N("{0} needs a party.");                                           // NEED_PARTY
                case 93: return Lang.N("{0} can only be used by the party leader.");                    // NEED_PARTY_LEADER
                case 94: return Lang.N("{0}: your partner's SP is too low.");                           // NEED_PARTNER_SP
                case 95: return Lang.N("{0} needs a weapon.");                                          // NEED_WEAPON
                case 96: return Lang.N("{0} needs energy spheres.");                                    // NEED_ENERGY_SPHERE
                case 97: return Lang.N("{0} can only follow Weapon Blocking.");                         // NEED_WEAPON_BLOCKING
                case 98: return Lang.N("{0}: too many traps are set.");                                 // MAX_TRAPS
                case 99: return Lang.N("{0} needs a magic spellbook.");                                 // NEED_MAGIC_SPELLBOOK
                case 100: return Lang.N("{0}: not enough AP.");                                         // AP_INSUFFICIENT
                case 101: return Lang.N("{0} needs Servant Weapons.");                                  // NEED_SERVANT_WEAPON
                case 102: return Lang.N("{0} needs Twinkling Galaxy.");                                 // NEED_TWINKLING_GALAXY
                case 103: return Lang.N("{0} needs soul energy.");                                      // NEED_SOUL_ENGERY
                case 104: return Lang.N("{0} needs an amulet.");                                        // NEED_AMULET
                case 105: return Lang.N("{0} needs a gatling gun or a shotgun.");                       // NEED_GATLING_SHOTGUT
                case 106: return Lang.N("{0} needs a rifle or a revolver.");                            // NEED_RIFLE_REVOLVER
                case 107: return Lang.N("{0} needs a grenade launcher or a rifle.");                    // NEED_GLAUNCHER_RIFLE
                case 108: return Lang.N("{0} needs a gatling gun or a revolver.");                      // NEED_GATLING_REVOLVER
                case 109: return Lang.N("{0} needs a shotgun or a grenade launcher.");                  // NEED_SHOTGUN_GLAUNCHER
                case 110: return Lang.N("{0} needs a shield.");                                         // NEED_SHIELD
                case 111: return Lang.N("{0} can't reach a target in the shadow space.");               // TARGET_SHADOW_SPACE
                default: return Lang.N("{0} failed.");
            }
        }
    }
}
