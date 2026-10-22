//! skills/merchant/selfdestruction.cpp: class SkillSelfDestruction : public SkillImplRecursiveDamageSplash
//! (NC_SELFDESTRUCTION).

use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_PC, BL_SKILL};
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SD_SPLASH;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// SkillSelfDestruction::SkillSelfDestruction() : SkillImplRecursiveDamageSplash(NC_SELFDESTRUCTION).
pub struct SkillSelfDestruction;

impl SkillImpl for SkillSelfDestruction {
    fn skill_id(&self) -> u16 {
        NC_SELFDESTRUCTION
    }
    crate::splash_skill_defaults!();

    /// SkillSelfDestruction::castendNoDamageId (players only): off the Mado Gear, the skill packet, the splash around
    /// the target (map_foreachinrange, characters and skill units, flag | BCT_ENEMY | SD_SPLASH | 1), then SP 0 and
    /// the caster's unit groups removed.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if src.kind != BL_PC {
            return;
        }
        if src.sc.option & crate::status::defs::OPTION_MADOGEAR != 0 {
            // pc_setmadogear(sd, false) (type MADO_ROBOT)
            crate::script::buildins::script2::looks::pc_setmadogear(c, src.id, false, 0);
        }
        area::set_temp(1, 0);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, true);
        let f = *flag | BCT_ENEMY | SD_SPLASH | 1;
        foreach_in_range(c, &t, skill_get_splash(id, skill_lv) as i16, BL_CHAR | BL_SKILL, Wall::Config, |c, bl| area::skill_area_sub(c, bl, &s, id, skill_lv, tick, f, skill_castend_damage_id));
        crate::status::hp::status_set_sp(c, src.id, 0, 0);
        crate::skillunit::skill_clear_unitgroup(c, src.id);
    }
}

impl RecursiveDamageSplash for SkillSelfDestruction {}
