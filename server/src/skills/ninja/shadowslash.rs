//! skills/ninja/shadowslash.cpp: class SkillShadowSlash : public WeaponSkillImpl (NJ_KIRIKAGE), rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_HIDING;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillShadowSlash;

impl SkillImpl for SkillShadowSlash {
    fn skill_id(&self) -> u16 {
        NJ_KIRIKAGE
    }

    /// SkillShadowSlash::calculateSkillRatio (RENEWAL: -50 + 150% per level).
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += -50 + 150 * skill_lv as i32;
    }

    /// SkillShadowSlash::castendDamageId: outside GvG and battlegrounds the caster jumps to a free cell next to the
    /// target (map_search_freecell(target, 0, &x, &y, 1, 1, 0): on failure x / y are the target's cell), Hiding ends,
    /// then WeaponSkillImpl's hit.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        if !crate::status::map_flag_gvg2(c, src.map) && !crate::npc::mapflag(c, src.map, crate::npc::MF_BATTLEGROUND) {
            // You don't move on GVG grounds.
            let (x, y) = crate::mob::spawn::map_search_freecell(c, Some((target.map, target.x, target.y)), 0, 0, 0, 1, 1, 0, 50).unwrap_or((target.x, target.y));
            if unit::movepos(c, src.id, x, y, 0, false) {
                if let Some(s) = unit::get(c, src.id) {
                    crate::battle::clif::clif_blown(c, &s);
                }
            }
        }
        sc::status_change_end(c, src.id, SC_HIDING, 0);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get_or_freed(c, target.id).unwrap_or_else(|| target.clone());
        weapon_castend_damage_id(c, self.skill_id(), &s, &t, skill_lv, tick, *flag);
    }
}
