//! skills/other/baby.cpp: class SkillBaby : public SkillImpl (WE_BABY).

use super::{pc_get_father, pc_get_mother, sd_status};
use crate::db::skills::*;
use crate::mob::unit::check_distance_bl;
use crate::skill::clif::skill_fail;
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::SkillImpl;
use crate::status::consts::SC_STUN;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillBaby;

impl SkillImpl for SkillBaby {
    fn skill_id(&self) -> u16 {
        WE_BABY
    }

    /// SkillBaby::castendNoDamageId: fails (requirements kept) when neither parent is online, when the baby is in a
    /// party and neither parent is in it, or when neither parent is within AREA_SIZE (check_distance_bl compares the
    /// coordinates only); else the baby is stunned (SCSTART_NORATEDEF, skill_get_time2) and each online parent gets
    /// the skill's status.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let Some(ch) = sd_status(c, src) else { return };
        let f_sd = pc_get_father(c, src);
        let m_sd = pc_get_mother(c, src);
        // Neither was found
        if f_sd.is_none() && m_sd.is_none() {
            skill_fail(c, src, id);
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        let party_of = |u: &Option<Unit>| u.as_ref().and_then(|u| crate::pc::character_of(c, u.id)).map(|p| p.party_id);
        // Not in same party
        // TODO : no check if sd->status.party_id == 0 ?
        if ch.party_id != 0 && (f_sd.is_none() || Some(ch.party_id) != party_of(&f_sd)) && (m_sd.is_none() || Some(ch.party_id) != party_of(&m_sd)) {
            skill_fail(c, src, id);
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        // Not in same screen
        let near = |u: &Option<Unit>| u.as_ref().map(|u| check_distance_bl(src, u, crate::conf::area_size() as i32)).unwrap_or(false);
        if !near(&f_sd) && !near(&m_sd) {
            skill_fail(c, src, id);
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        sc::status_change_start(c, Some(src), target, SC_STUN, 10000, skill_lv as i32, 0, 0, 0, skill_get_time2(id, skill_lv) as i64, crate::status::defs::SCSTART_NORATEDEF, 0);
        let type_ = skill_get_sc(id) as i32;
        for p in [&f_sd, &m_sd].into_iter().flatten() {
            let p = unit::get(c, p.id).unwrap_or_else(|| p.clone());
            sc::sc_start(c, Some(src), &p, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        }
    }
}
