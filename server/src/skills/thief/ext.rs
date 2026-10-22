//! The functions of other rAthena files that only the thief family's classes reach (ported once, here):
//! clif.cpp clif_poison_list / clif_autoshadowspell_list, skill.cpp skill_poisoningweapon.

use crate::db::items::group::itemdb_group_item_exists;
use crate::db::skills::*;
use crate::net::packets::*;
use crate::skill::clif::fail::*;
use crate::status::consts::SC_STOP;
use crate::status::defs::SKILL_FLAG_PLAGIARIZED;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::Ctx;

/// itemdb.hpp e_item_group IG_POISON (the script constant rAthena exports).
fn ig_poison() -> u16 {
    crate::data::constant("IG_POISON").unwrap_or(0) as u16
}

/// clif.cpp clif_poison_list: ZC_MAKINGARROW_LIST of every inventory slot (index order, equipped or not) whose item
/// is in the IG_POISON group; sent with menuskill GC_POISONINGWEAPON / skill_lv when not empty, else the
/// USESKILL_FAIL_GUILLONTINE_POISON failure.
pub fn clif_poison_list(c: &Ctx, sd: &Unit, skill_lv: u16) {
    let group = ig_poison();
    let mut p = PACKET_ZC_MAKINGARROW_LIST { packetType: PACKET_ZC_MAKINGARROW_LIST::ID as i16, ..Default::default() };
    for r in crate::inventory::inv_list(c, sd.id) {
        if itemdb_group_item_exists(group, r.item.nameid) {
            p.items.push(PACKET_ZC_MAKINGARROW_LIST_sub { itemId: crate::db::items::client_nameid(r.item.nameid) });
        }
    }
    if !p.items.is_empty() {
        crate::clif::send(c, &p.to_vec(), sd, crate::clif::Target::SelfOnly);
        crate::itemui::update(c, sd.id, |s| {
            s.menuskill_id = GC_POISONINGWEAPON as i32;
            s.menuskill_val = skill_lv as i32;
        });
    } else {
        crate::skill::clif::clif_skill_fail(c, sd, GC_POISONINGWEAPON, USESKILL_FAIL_GUILLONTINE_POISON, 0, 0);
    }
}

/// clif.cpp clif_autoshadowspell_list (PACKETVER >= 20081210): 0442 ZC_SKILL_SELECT_REQUEST (why 1 =
/// WHY_SC_AUTOSHADOWSPELL) with every plagiarised skill (skill slot order) that has INF2_ISAUTOSHADOWSPELL; nothing
/// when the menu is already open; SC_STOP ends and USESKILL_FAIL_IMITATION_SKILL_NONE when there is none.
pub fn clif_autoshadowspell_list(c: &Ctx, sd: &Unit) {
    if crate::itemui::menu(c, sd.id).0 == SC_AUTOSHADOWSPELL as i32 {
        return;
    }
    let mut p = PACKET_ZC_SKILL_SELECT_REQUEST { packetType: PACKET_ZC_SKILL_SELECT_REQUEST::ID as i16, flag: 1, ..Default::default() };
    if let Some(s) = crate::skilltree::Skills::load(c, sd.id) {
        // for( i = 0; i < MAX_SKILL; i++ ): the occupied slots in index order
        for (_, slot) in s.snapshot() {
            if slot.flag == SKILL_FLAG_PLAGIARIZED && slot.id > 0 && skill_get_inf2(slot.id, &[INF2_ISAUTOSHADOWSPELL]) {
                p.skillIds.push(slot.id as i16);
            }
        }
    }
    let count = p.skillIds.len();
    if count > 0 {
        crate::clif::send(c, &p.to_vec(), sd, crate::clif::Target::SelfOnly);
        crate::itemui::update(c, sd.id, |s| {
            s.menuskill_id = SC_AUTOSHADOWSPELL as i32;
            s.menuskill_val = count as i32;
        });
    } else {
        sc::status_change_end(c, sd.id, SC_STOP, 0);
        crate::skill::clif::clif_skill_fail(c, sd, SC_AUTOSHADOWSPELL, USESKILL_FAIL_IMITATION_SKILL_NONE, 0, 0);
    }
}

/// itemdb.hpp ITEMID_PARALYSE … ITEMID_VENOMBLEED (the Guillotine Cross poisons, 12717..12724).
pub const ITEMID_PARALYSE: u32 = 12717;
pub const ITEMID_LEECHESEND: u32 = 12718;
pub const ITEMID_OBLIVIONCURSE: u32 = 12719;
pub const ITEMID_DEATHHURT: u32 = 12720;
pub const ITEMID_TOXIN: u32 = 12721;
pub const ITEMID_PYREXIA: u32 = 12722;
pub const ITEMID_MAGICMUSHROOM: u32 = 12723;
pub const ITEMID_VENOMBLEED: u32 = 12724;

/// clif.cpp color_table[COLOR_WHITE] (0xFFFFFF, the same in BGR).
const COLOR_WHITE: u32 = 0xFFFFFF;

/// skill.cpp skill_poisoningweapon: enchant weapon with poison (clif_parse_SelectArrow's answer to
/// clif_poison_list). The chosen poison is consumed; SC_POISONINGWEAPON is restarted (val1 = GC_RESEARCHNEWPOISON's
/// level, val2 = the poison status, val3 = the chance 2 + 2 x level), the poison's own status goes on the caster
/// (the caster's bonus, no icon), and msg 721 in white.
pub fn skill_poisoningweapon(c: &Ctx, sd: &Unit, nameid: u32) {
    use crate::status::consts::*;
    let aid = sd.id;
    if nameid == 0 || crate::inventory::pc_delitem(c, aid, crate::inventory::pc_search_inventory(c, aid, nameid) as i32, 1, 0, 0, crate::map_log::LOG_TYPE_CONSUME) != 0 {
        // (LOG_TYPE_CONSUME)
        crate::skill::clif::skill_fail(c, sd, GC_POISONINGWEAPON);
        return;
    }
    //uint16 msg = 1443; //Official is using msgstringtable.txt
    let (type_, msg) = match nameid {
        ITEMID_PARALYSE => (SC_PARALYSE, "Paralyze"),
        ITEMID_PYREXIA => (SC_PYREXIA, "Pyrexia"),
        ITEMID_DEATHHURT => (SC_DEATHHURT, "Deathhurt"),
        ITEMID_LEECHESEND => (SC_LEECHESEND, "Leech End"),
        ITEMID_VENOMBLEED => (SC_VENOMBLEED, "Venom Bleed"),
        ITEMID_TOXIN => (SC_TOXIN, "Toxin"),
        ITEMID_MAGICMUSHROOM => (SC_MAGICMUSHROOM, "Magic Mushroom"),
        ITEMID_OBLIVIONCURSE => (SC_OBLIVIONCURSE, "Oblivion Curse"),
        _ => {
            crate::skill::clif::skill_fail(c, sd, GC_POISONINGWEAPON);
            return;
        }
    };
    sc::status_change_end(c, aid, SC_POISONINGWEAPON, 0); // End the status so a new poison can be applied (if changed)
    let menuskill_val = crate::itemui::menu(c, aid).1;
    let chance = 2 + 2 * menuskill_val; // 2 + 2 * skill_lv
    let time = skill_get_time(GC_POISONINGWEAPON, menuskill_val as u16) as i64;
    let u = unit::get(c, aid).unwrap_or_else(|| sd.clone());
    let research = crate::skill::checkskill(c, aid, GC_RESEARCHNEWPOISON) as i32;
    //in Aegis it store the level of GC_RESEARCHNEWPOISON in val1
    sc::sc_start4(c, Some(&u), &u, SC_POISONINGWEAPON, 100, research, type_, chance, 0, time);
    let u = unit::get(c, aid).unwrap_or(u);
    use crate::status::defs::{SCSTART_NOAVOID, SCSTART_NOICON};
    sc::status_change_start(c, Some(&u), &u, type_, 10000, menuskill_val, 0, 0, 0, time, SCSTART_NOAVOID | SCSTART_NOICON, 0); // Apply bonus to caster
    // sprintf(output, msg_txt(&sd,721), msg); clif_messagecolor(&sd, color_table[COLOR_WHITE], output, false, SELF)
    let output = crate::data::format_msg(crate::data::msg_txt(721), &[&msg]);
    let u = unit::get(c, aid).unwrap_or(u);
    crate::clif::messagecolor_target(c, &u, COLOR_WHITE, &crate::net::wire::latin1(&output), false, crate::clif::Target::SelfOnly, None);
}
