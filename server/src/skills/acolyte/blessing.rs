//! skills/acolyte/blessing.cpp: class SkillBlessing : public SkillImpl (AL_BLESSING).

use crate::battle::BF_MISC;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::attack::skill_attack;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::SC_CHANGEUNDEAD;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillBlessing;

impl SkillImpl for SkillBlessing {
    fn skill_id(&self) -> u16 {
        AL_BLESSING
    }

    /// SkillBlessing::castendNoDamageId: a player under Change Undead (Dark Priest's curse) takes a hit instead of the
    /// blessing (while above 1 HP).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, bl: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        clif_skill_nodamage(c, Some(src), bl, id, skill_lv as i32, true);
        // dstsd != nullptr && tsc && tsc->getSCE(SC_CHANGEUNDEAD)
        if bl.kind == BL_PC && sce(c, bl, SC_CHANGEUNDEAD).is_some() {
            let tstatus = crate::status::status_get_status_data(c, bl);
            if tstatus.hp > 1 {
                let t = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
                skill_attack(c, BF_MISC, src, src, &t, id, skill_lv, tick, *flag);
            }
            return;
        }
        let t = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
        sc::sc_start(c, Some(src), &t, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
    }
}
