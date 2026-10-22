//! Autospell (SA_AUTOSPELL's menu and the spell it keeps ready): clif.cpp clif_autospell (ZC_AUTOSPELLLIST, the
//! RENEWAL list), skill.cpp skill_autospell (the chosen spell: SC_AUTOSPELL), and skill.cpp skill_select_menu (the
//! SC_AUTOSHADOWSPELL choice).

use crate::db::skills::*;
use crate::net::packets::PACKET_ZC_AUTOSPELLLIST;
use crate::status::consts::*;
use crate::status::sc;
use crate::unit;
use crate::world::Ctx;

/// clif.cpp clif_autospell (PACKETVER_RE_NUM >= 20181031): the spells Autospell may keep ready at `skill_lv`,
/// then menuskill = SA_AUTOSPELL.
pub fn clif_autospell(c: &Ctx, aid: u32, skill_lv: u16) {
    // RENEWAL: { skill, required autospell level }
    const AUTOSPELL_SKILLS: [(u16, u16); 9] = [
        (MG_FIREBOLT, 0),
        (MG_COLDBOLT, 0),
        (MG_LIGHTNINGBOLT, 0),
        (MG_SOULSTRIKE, 3),
        (MG_FIREBALL, 3),
        (WZ_EARTHSPIKE, 6),
        (MG_FROSTDIVER, 6),
        (MG_THUNDERSTORM, 9),
        (WZ_HEAVENDRIVE, 9),
    ];
    let Some(u) = unit::get(c, aid) else { return };
    let mut skills = Vec::new();
    for (skill_id, required) in AUTOSPELL_SKILLS {
        if skill_lv > required && crate::skill::checkskill(c, aid, skill_id) != 0 {
            skills.push(skill_id as i32);
        }
    }
    let p = PACKET_ZC_AUTOSPELLLIST {
        packetType: PACKET_ZC_AUTOSPELLLIST::ID as i16,
        packetLength: (PACKET_ZC_AUTOSPELLLIST::SIZE + 4 * skills.len()) as i16,
        skills,
    };
    crate::clif::send(c, &p.to_vec(), &u, crate::clif::Target::SelfOnly);
    let (_, _, val2) = crate::itemui::menu(c, aid);
    crate::itemui::set_menu(c, aid, SA_AUTOSPELL as i32, skill_lv as i32, val2);
}

/// skill.cpp skill_autospell (RENEWAL): the chosen spell and its level (half of Autospell's, Soul Link: 10 for the
/// bolts) go into SC_AUTOSPELL.
pub fn skill_autospell(c: &Ctx, aid: u32, skill_id: u16) -> i32 {
    if skill_id == 0 || skill_db().get_index(skill_id, true) == 0 || skill_chk_guild(skill_id) {
        return 0;
    }
    let lv = crate::skill::checkskill(c, aid, skill_id) as u16;
    let skill_lv = crate::itemui::menu(c, aid).1 as u16;
    if skill_lv == 0 || lv == 0 {
        return 0; // Player must learn the skill before doing auto-spell [Lance]
    }
    let sage = sc::get_sce(c, aid, SC_SPIRIT).map(|e| e.val2 == SL_SAGE as i32).unwrap_or(false);
    let maxlv: u16 = if matches!(skill_id, MG_COLDBOLT | MG_FIREBOLT | MG_LIGHTNINGBOLT) && sage {
        10 // Soul Linker bonus. [Skotlex]
    } else {
        skill_lv / 2 // Half of Autospell's level unless player learned a lower level (capped below)
    };
    let maxlv = lv.min(maxlv);
    let Some(u) = unit::get(c, aid) else { return 0 };
    sc::sc_start4(c, Some(&u), &u, SC_AUTOSPELL, 100, skill_lv as i32, skill_id as i32, maxlv as i32, 0, skill_get_time(SA_AUTOSPELL, skill_lv) as i64);
    0
}

/// skill.cpp skill_select_menu: the Auto Shadow Spell choice (a plagiarised skill) goes into SC__AUTOSHADOWSPELL.
pub fn skill_select_menu(c: &Ctx, aid: u32, skill_id: u16) {
    use crate::status::defs::SKILL_FLAG_PLAGIARIZED;
    let mut aslvl = 0;
    if let Some(e) = sc::get_sce(c, aid, SC_STOP) {
        aslvl = e.val1;
        sc::status_change_end(c, aid, SC_STOP, 0);
    }
    let sk_idx = skill_get_index(skill_id);
    if skill_id == 0 || sk_idx == 0 {
        return;
    }
    let Some(u) = unit::get(c, aid) else { return };
    let slot = crate::skilltree::Skills::load(c, aid).map(|s| s.at(sk_idx)).unwrap_or_default();
    let id = slot.id;
    if !skill_get_inf2(skill_id, &[INF2_ISAUTOSHADOWSPELL]) || id == 0 || slot.flag != SKILL_FLAG_PLAGIARIZED {
        super::clif::skill_fail(c, &u, SC_AUTOSHADOWSPELL);
        return;
    }
    let mut lv = (aslvl + 5) / 2; // The level the skill will be autocasted
    lv = lv.min(slot.lv as i32);
    let prob = if aslvl >= 10 { 15 } else { 30 - 2 * aslvl }; // Probability at level 10 was increased to 15.
    sc::sc_start4(c, Some(&u), &u, SC__AUTOSHADOWSPELL, 100, id as i32, lv, prob, aslvl * 5, skill_get_time(SC_AUTOSHADOWSPELL, aslvl as u16) as i64);
}
