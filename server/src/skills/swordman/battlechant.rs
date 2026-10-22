//! skills/swordman/battlechant.cpp: class SkillBattleChant : public SkillImpl (PA_GOSPEL).

use crate::battle::BCT_SELF;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_poseffect;
use crate::skill::{iface, SKILL_NOCONSUME_REQ};
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_GOSPEL, SC_NONE};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillBattleChant;

impl SkillImpl for SkillBattleChant {
    fn skill_id(&self) -> u16 {
        PA_GOSPEL
    }

    /// SkillBattleChant::castendPos2: the caster's own Gospel ends (no requirement consumed); else the unit group goes
    /// on the caster's cell, someone else's Gospel on the caster ends, the caster's status keeps the group id (val3)
    /// and BCT_SELF (val4), and the music packet plays.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, _x: i16, _y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        // status_change_entry *sce = (sc && type != SC_NONE) ? sc->getSCE(type) : nullptr
        let e = if type_ != SC_NONE { sce(c, src, type_) } else { None };
        if e.as_ref().map(|e| e.val4 == BCT_SELF).unwrap_or(false) {
            sc::status_change_end(c, src.id, SC_GOSPEL, 0);
            *flag |= SKILL_NOCONSUME_REQ;
        } else {
            let Some(group_id) = iface::skill_unitsetting_group_id(c, src, id, skill_lv, src.x, src.y, 0) else { return };
            if e.is_some() {
                sc::status_change_end(c, src.id, type_, 0); // Was under someone else's Gospel. [Skotlex]
            }
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            sc::sc_start4(c, Some(&s), &s, type_, 100, skill_lv as i32, 0, group_id, BCT_SELF, skill_get_time(id, skill_lv) as i64);
            let s = unit::get(c, src.id).unwrap_or(s);
            clif_skill_poseffect(c, &s, id, skill_lv, 0, 0, tick); // PA_GOSPEL music packet
        }
    }
}
