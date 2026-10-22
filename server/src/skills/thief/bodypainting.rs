//! skills/thief/bodypainting.cpp: class SkillBodyPainting : public SkillImpl (SC_BODYPAINT).

use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{foreach_in_range, skill_area_sub, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::*;
use crate::status::defs::{OPTION_CLOAK, OPTION_HIDE};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillBodyPainting;

impl SkillImpl for SkillBodyPainting {
    fn skill_id(&self) -> u16 {
        SC_BODYPAINT
    }

    /// SkillBodyPainting::castendNoDamageId: flag&1 (each unit around): hidden targets are revealed (Shadow Form
    /// ends at [100 - 10 x its level]%), then Attack Speed decrease and Blind; else the animation and the search
    /// around the target (map_foreachinallrange).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        if *flag & 1 != 0 {
            let has_sc = crate::battle::mobif::has_sc(target.kind);
            if has_sc && ((target.sc.option & (OPTION_HIDE | OPTION_CLOAK)) != 0 || sce(c, target, SC_CAMOUFLAGE).is_some() || sce(c, target, SC_STEALTHFIELD).is_some()) {
                for t in [SC_HIDING, SC_CLOAKING, SC_CLOAKINGEXCEED, SC_CAMOUFLAGE, SC_NEWMOON] {
                    sc::status_change_end(c, target.id, t, 0);
                }
                // [100 - (Skill Level x 10)] %
                if let Some(e) = sce(c, target, SC__SHADOWFORM) {
                    if ((c.rnd() % 100) as i32) < 100 - e.val1 * 10 {
                        sc::status_change_end(c, target.id, SC__SHADOWFORM, 0);
                    }
                }
            }
            // Attack Speed decrease and Blind happen to everyone around caster, not just hidden targets.
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            sc::sc_start(c, Some(src), &t, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
            let t = unit::get(c, target.id).unwrap_or(t);
            sc::sc_start(c, Some(src), &t, SC_BLIND, 53 + 2 * skill_lv as i32, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
        } else {
            clif_skill_nodamage(c, Some(src), target, id, 0, true);
            let f = *flag | BCT_ENEMY | 1;
            foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR, Wall::None, |c, bl| skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id));
        }
    }
}
