//! skills/mercenary/mercenary_increaseagility.cpp: class SkillMercenaryIncreaseAgility : public SkillImpl (MER_INCAGI).

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

pub struct SkillMercenaryIncreaseAgility;

impl SkillImpl for SkillMercenaryIncreaseAgility {
    fn skill_id(&self) -> u16 {
        MER_INCAGI
    }

    /// SkillMercenaryIncreaseAgility::castendNoDamageId: the packet, then a player under Change Undead (HP above 1)
    /// takes the skill's damage (BF_MISC) instead of the status change.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        // dstsd != nullptr && tsc && tsc->getSCE(SC_CHANGEUNDEAD)
        if target.kind == BL_PC && sce(c, target, SC_CHANGEUNDEAD).is_some() {
            if crate::status::status_get_status_data(c, target).hp > 1 {
                skill_attack(c, BF_MISC, src, src, target, id, skill_lv, tick, *flag);
            }
            return;
        }
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        sc::sc_start(c, Some(src), &t, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
    }
}
