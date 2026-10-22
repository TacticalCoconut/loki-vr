//! skills/summoner/spiritofsavage.cpp: class SkillSpiritofSavage : public SkillImpl (SU_SVG_SPIRIT).

use super::add_hp_ratio;
use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::area::{self, splash_target};
use crate::skills::novice::pc_checkskill;
use crate::skills::npc::acidbreath::{map_foreachinpath, skill_attack_area};
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSpiritofSavage;

impl SkillImpl for SkillSpiritofSavage {
    fn skill_id(&self) -> u16 {
        SU_SVG_SPIRIT
    }

    /// SkillSpiritofSavage::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 150 + 150 * skill_lv as i32;
        if src.kind == BL_PC && pc_checkskill(c, src, SU_SPIRITOFLIFE) != 0 {
            add_hp_ratio(c, src, base_skillratio);
        }
    }

    /// SkillSpiritofSavage::castendDamageId: skill_area_temp[1] = the target; skill_attack_area on the units of the
    /// path from the caster to the target (map_foreachinpath: splash wide, maxcount long).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        area::set_temp(1, target.id as i32);
        let f = *flag;
        let atk_type = skill_get_type(id);
        map_foreachinpath(c, src.map, src.x, src.y, target.x, target.y, skill_get_splash(id, skill_lv) as i16, skill_get_maxcount(id, skill_lv), splash_target(src), |c, bl| {
            skill_attack_area(c, bl, atk_type, src, src, id, skill_lv, tick, f, BCT_ENEMY)
        });
    }
}
