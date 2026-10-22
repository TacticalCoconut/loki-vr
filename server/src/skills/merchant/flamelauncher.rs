//! skills/merchant/flamelauncher.cpp: class SkillFlameLauncher : public SkillImpl (NC_FLAMELAUNCHER).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::skill::area::{self, map_foreachindir, map_foreachinpath, skill_attack_area};
use crate::skill::conf;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::status::consts::SC_BURNING;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillFlameLauncher::SkillFlameLauncher() : SkillImpl(NC_FLAMELAUNCHER).
pub struct SkillFlameLauncher;

impl SkillImpl for SkillFlameLauncher {
    fn skill_id(&self) -> u16 {
        NC_FLAMELAUNCHER
    }

    /// SkillFlameLauncher::applyAdditionalEffects: SC_BURNING, 20% + 10% per level.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_BURNING, 20 + 10 * skill_lv as i32, skill_lv as i32, skill_get_time(self.skill_id(), skill_lv) as i64);
    }

    /// SkillFlameLauncher::calculateSkillRatio: +200% + 300% per level, RE_LVL_DMOD(150).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += 200 + 300 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 150);
    }

    /// SkillFlameLauncher::castendDamageId: skill_attack_area on the path towards the target (the eight-direction
    /// path with skill_eightpath_algorithm, else the line); unlike the breath skills, no fallback hit.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let f = *flag;
        area::set_temp(1, target.id as i32);
        let atk_type = skill_get_type(id);
        let splash = skill_get_splash(id, skill_lv) as i16;
        let maxcount = skill_get_maxcount(id, skill_lv);
        if conf::skill_eightpath_algorithm() {
            //Use official AoE algorithm
            map_foreachindir(c, src.map, src.x, src.y, target.x, target.y, splash, maxcount, 0, area::splash_target(src), |c, bl| skill_attack_area(c, bl, atk_type, src, src, id, skill_lv, tick, f, BCT_ENEMY));
        } else {
            map_foreachinpath(c, src.map, src.x, src.y, target.x, target.y, splash, maxcount, area::splash_target(src), |c, bl| skill_attack_area(c, bl, atk_type, src, src, id, skill_lv, tick, f, BCT_ENEMY));
        }
    }
}
