//! The skill-window packets of clif.cpp (rAthena e985006, PACKETVER 20211103, PACKETVER_RE_NUM >= 20190807):
//! clif_skillinfoblock (ZC_SKILLINFO_LIST 0x0b32), clif_addskill (ZC_ADD_SKILL 0x0b31), clif_deleteskill
//! (ZC_SKILLINFO_DELETE 0x0441), clif_skillup (ZC_SKILLINFO_UPDATE 0x010e), clif_skillinfo (ZC_SKILLINFO_UPDATE2
//! 0x0b33) and clif_parse_SkillUp (CZ_UPGRADE_SKILLLEVEL 0x0112). The slots come from skilltree.rs.

use crate::clif::{self, Target};
use crate::db::skills::{self as sk, skill_get_inf, skill_get_sp, skill_tree_get_max};
use crate::net::packets::*;
use crate::skilltree::{skill_get_range2_pc, Skills};
use crate::status::defs::SKILL_FLAG_PERMANENT;
use crate::world::Ctx;

/// ZC_SKILLINFO_LIST's size limit (clif.cpp "workaround for bugreport:5348": 8192 bytes).
const SKILLINFO_LIST_MAX: usize = 8192;

fn class_of(c: &Ctx, aid: u32) -> i32 {
    crate::pc::character_of(c, aid).map(|ch| ch.class as i32).unwrap_or(0)
}

/// The SKILLDATA entry of a slot (clif_skillinfoblock's loop body).
fn skilldata(c: &Ctx, aid: u32, class_: i32, id: u16, lv: u8, flag: u8) -> SKILLDATA {
    SKILLDATA {
        id,
        inf: skill_get_inf(id),
        level: lv as u16,
        sp: skill_get_sp(id, lv as u16) as u16,
        range2: skill_get_range2_pc(c, aid, id, lv as u16, false) as u16,
        upFlag: (flag == SKILL_FLAG_PERMANENT && (lv as i32) < skill_tree_get_max(id, class_)) as u8,
        level2: lv as u16,
    }
}

/// clif.cpp clif_skillinfoblock: the whole skill tree (every slot with its id set, in skill-index order).
pub fn clif_skillinfoblock(c: &Ctx, aid: u32) {
    if !crate::pc::is_active_session(c, aid) {
        return;
    }
    let Some(u) = crate::unit::get(c, aid) else { return };
    let Some(s) = Skills::load(c, aid) else { return };
    let class_ = class_of(c, aid);
    let slots: Vec<(u16, crate::skilltree::Slot)> = s.snapshot().into_iter().filter(|(_, sl)| sl.id != 0).collect();
    let mut p = PACKET_ZC_SKILLINFO_LIST { packetType: PACKET_ZC_SKILLINFO_LIST::ID as i16, packetLength: PACKET_ZC_SKILLINFO_LIST::SIZE as i16, skills: Vec::new() };
    let mut len = PACKET_ZC_SKILLINFO_LIST::SIZE;
    let mut have_call_partner_skill = false;
    let mut i = 0;
    while i < slots.len() {
        let sl = slots[i].1;
        // skip WE_CALLPARTNER and send it in special way
        if sl.id == sk::WE_CALLPARTNER {
            have_call_partner_skill = true;
            i += 1;
            continue;
        }
        // workaround for bugreport:5348
        if len + SKILLDATA::SIZE > SKILLINFO_LIST_MAX {
            break;
        }
        p.skills.push(skilldata(c, aid, class_, sl.id, sl.lv, sl.flag));
        len += SKILLDATA::SIZE;
        i += 1;
    }
    clif::send(c, &p.to_vec(), &u, Target::SelfOnly);
    // adoption fix
    if have_call_partner_skill {
        clif_addskill(c, aid, sk::WE_CALLPARTNER);
        clif_skillinfo(c, aid, sk::WE_CALLPARTNER, sk::INF_PASSIVE_SKILL as i32);
    }
    // workaround for bugreport:5348; send the remaining skills one by one to bypass packet size limit
    for &(_, sl) in &slots[i..] {
        if sl.id != sk::WE_CALLPARTNER || !have_call_partner_skill {
            clif_addskill(c, aid, sl.id);
            clif_skillinfo(c, aid, sl.id, sk::INF_PASSIVE_SKILL as i32);
        }
    }
}

/// clif.cpp clif_addskill: ZC_ADD_SKILL (a skill enters the tree, e.g. with an item).
pub fn clif_addskill(c: &Ctx, aid: u32, skill_id: u16) {
    let idx = sk::skill_get_index(skill_id);
    if idx == 0 {
        return;
    }
    let Some(s) = Skills::load(c, aid) else { return };
    let sl = s.at(idx);
    if sl.id == 0 {
        return;
    }
    let Some(u) = crate::unit::get(c, aid) else { return };
    let mut d = skilldata(c, aid, class_of(c, aid), skill_id, sl.lv, sl.flag);
    d.id = skill_id;
    d.level2 = s.checkskill(skill_id) as u16; // pc_checkskill
    let p = PACKET_ZC_ADD_SKILL { packetType: PACKET_ZC_ADD_SKILL::ID as i16, skill: d };
    clif::send(c, &p.to_vec(), &u, Target::SelfOnly);
}

/// clif.cpp clif_deleteskill: ZC_SKILLINFO_DELETE, then the whole list unless `skip_infoblock`.
pub fn clif_deleteskill(c: &Ctx, aid: u32, skill_id: u16, skip_infoblock: bool) {
    let idx = sk::skill_get_index(skill_id);
    if idx == 0 {
        return;
    }
    let Some(u) = crate::unit::get(c, aid) else { return };
    let p = PACKET_ZC_SKILLINFO_DELETE { packetType: PACKET_ZC_SKILLINFO_DELETE::ID, skillID: skill_id };
    clif::send(c, &p.to_vec(), &u, Target::SelfOnly);
    if !skip_infoblock {
        clif_skillinfoblock(c, aid);
    }
}

/// clif.cpp clif_skillup: ZC_SKILLINFO_UPDATE (a skill level went up).
pub fn clif_skillup(c: &Ctx, aid: u32, skill_id: u16, lv: u16, range: u16, upgradable: bool) {
    if sk::skill_get_index(skill_id) == 0 {
        return;
    }
    let Some(u) = crate::unit::get(c, aid) else { return };
    let p = PACKET_ZC_SKILLINFO_UPDATE {
        packetType: PACKET_ZC_SKILLINFO_UPDATE::ID,
        skillId: skill_id,
        level: lv,
        sp: skill_get_sp(skill_id, lv) as u16,
        range2: range,
        upFlag: upgradable as u8,
    };
    clif::send(c, &p.to_vec(), &u, Target::SelfOnly);
}

/// clif.cpp clif_skillinfo: ZC_SKILLINFO_UPDATE2 (one skill's data; `inf` INF_PASSIVE_SKILL = the skill's own).
pub fn clif_skillinfo(c: &Ctx, aid: u32, skill_id: u16, inf: i32) {
    let idx = sk::skill_get_index(skill_id);
    if idx == 0 {
        return;
    }
    let Some(s) = Skills::load(c, aid) else { return };
    let Some(u) = crate::unit::get(c, aid) else { return };
    let sl = s.at(idx);
    let level = sl.lv as u16;
    let p = PACKET_ZC_SKILLINFO_UPDATE2 {
        packetType: PACKET_ZC_SKILLINFO_UPDATE2::ID as i16,
        id: skill_id,
        inf: if inf == sk::INF_PASSIVE_SKILL as i32 { skill_get_inf(skill_id) } else { inf },
        level,
        sp: skill_get_sp(skill_id, level) as u16,
        range2: skill_get_range2_pc(c, aid, skill_id, level, false) as u16,
        upFlag: (sl.flag == SKILL_FLAG_PERMANENT && (level as i32) < skill_tree_get_max(skill_id, class_of(c, aid))) as u8,
        level2: level,
    };
    clif::send(c, &p.to_vec(), &u, Target::SelfOnly);
}

/// clif.cpp clif_parse_SkillUp: CZ_UPGRADE_SKILLLEVEL (0x0112 <skill id>.W).
pub fn parse_skill_up(c: &Ctx, aid: u32, pkt: &[u8]) {
    let pos = packet_pos(0x0112).first().copied().unwrap_or(2) as usize;
    let Some(b) = pkt.get(pos..pos + 2) else { return };
    let skill_id = u16::from_le_bytes([b[0], b[1]]);
    crate::skilltree::pc_skillup(c, aid, skill_id);
}
