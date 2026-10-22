//! skills/archer/wargstrike.cpp: class SkillWargStrike : public WeaponSkillImpl (RA_WUGSTRIKE).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::CellChk;
use crate::path::{DIRX, DIRY};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::defs::OPTION_WUGRIDER;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillWargStrike;

impl SkillImpl for SkillWargStrike {
    fn skill_id(&self) -> u16 {
        RA_WUGSTRIKE
    }

    /// SkillWargStrike::calculateSkillRatio: `base_skillratio += -100 + 200 * skill_lv`.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += -100 + 200 * skill_lv as i32;
    }

    /// SkillWargStrike::castendDamageId: a warg rider jumps next to the target (on the target's side facing the
    /// rider: unit_movepos easy with the path check, clif_blown) and hits; anyone else hits when a walk path reaches
    /// the target.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // sd && pc_isridingwug(sd)
        if src.kind == crate::map::BL_PC && src.sc.option & OPTION_WUGRIDER != 0 {
            let dir = crate::map::calc_dir(target, src.x, src.y) as usize;
            if crate::unit::movepos(c, src.id, target.x + DIRX[dir], target.y + DIRY[dir], 1, true) {
                let s = super::fresh(c, src);
                crate::battle::clif::clif_blown(c, &s);
                weapon_castend_damage_id(c, id, &s, &super::fresh(c, target), skill_lv, tick, *flag);
            }
            return;
        }
        if crate::path::path_search(src.map, src.x, src.y, target.x, target.y, 1, CellChk::ChkNoReach).is_some() {
            weapon_castend_damage_id(c, id, src, target, skill_lv, tick, *flag);
        }
    }
}
