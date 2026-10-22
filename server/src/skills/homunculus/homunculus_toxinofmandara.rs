//! skills/homunculus/homunculus_toxinofmandara.cpp: class SkillToxinOfMandara : public SkillImplRecursiveDamageSplash
//! (MH_TOXIN_OF_MANDARA).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_TOXIN_OF_MANDARA;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillToxinOfMandara;

impl SkillImpl for SkillToxinOfMandara {
    fn skill_id(&self) -> u16 {
        MH_TOXIN_OF_MANDARA
    }

    crate::splash_skill_defaults!();

    /// SkillToxinOfMandara::calculateSkillRatio: -100 + 400 + 450 * lv * base level / 100 + DEX (!TODO upstream:
    /// confirm the base level and DEX bonus).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *base_skillratio += -100 + 400 + 450 * skill_lv as i32 * crate::status::status_get_lv(c, src) / 100 + sstatus.dex as i32;
    }

    /// SkillToxinOfMandara::applyAdditionalEffects: SC_TOXIN_OF_MANDARA (100%).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let id = self.skill_id();
        sc::sc_start(c, Some(src), target, SC_TOXIN_OF_MANDARA, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
    }
}

impl RecursiveDamageSplash for SkillToxinOfMandara {}
