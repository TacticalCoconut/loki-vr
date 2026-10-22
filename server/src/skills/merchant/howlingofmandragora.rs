//! skills/merchant/howlingofmandragora.cpp: class SkillHowlingOfMandragora : public SkillImpl (GN_MANDRAGORA).

use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_MOB};
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::status::status_get_status_data;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// SkillHowlingOfMandragora::SkillHowlingOfMandragora() : SkillImpl(GN_MANDRAGORA).
pub struct SkillHowlingOfMandragora;

impl SkillImpl for SkillHowlingOfMandragora {
    fn skill_id(&self) -> u16 {
        GN_MANDRAGORA
    }

    /// SkillHowlingOfMandragora::castendNoDamageId: each target of the splash (flag & 1, not a monster, not already
    /// under the status) gets it with a [25 + 10 x level - (VIT + LUK) / 5]% chance (at least 10%) and loses
    /// (25 + 5 x level)% of its max SP; the first call walks the splash (map_foreachinallrange) and sends the skill
    /// packet on the caster.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            let type_ = skill_get_sc(id) as i32;
            let tstatus = status_get_status_data(c, target);
            let mut rate = 25 + (10 * skill_lv as i32) - (tstatus.vit as i32 + tstatus.luk as i32) / 5;
            if rate < 10 {
                rate = 10;
            }
            if target.kind == BL_MOB || sce(c, target, type_).is_some() {
                return; // Don't activate if target is a monster or zap SP if target already has Mandragora active.
            }
            if ((c.rnd() % 100) as i32) < rate {
                sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
                let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                let max_sp = status_get_status_data(c, &t).max_sp as i64;
                crate::status::hp::status_zap(c, target.id, 0, max_sp * (25 + 5 * skill_lv as i64) / 100, 0);
            }
        } else {
            let f = *flag | BCT_ENEMY | 1;
            foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR, Wall::None, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id));
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            clif_skill_nodamage(c, Some(&s), &s, id, skill_lv as i32, true);
        }
    }
}
