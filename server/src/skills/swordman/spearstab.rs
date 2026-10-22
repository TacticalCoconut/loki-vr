//! skills/swordman/spearstab.cpp: class SkillSpearStab : public SkillImpl (KN_SPEARSTAB), and map.cpp
//! map_foreachincell, the per-cell search it walks (its only user in the ported families; skill/area.rs is the
//! natural home of the map_foreachin* helpers).

use crate::battle::calc::Damage;
use crate::battle::{BCT_ENEMY, BF_WEAPON};
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::path::{DIRX, DIRY};
use crate::skill::area::{self, run_each};
use crate::skill::attack::{skill_attack, skill_blown, BLOWN_NONE};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::SD_ANIMATION;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillSpearStab;

impl SkillImpl for SkillSpearStab {
    fn skill_id(&self) -> u16 {
        KN_SPEARSTAB
    }

    /// SkillSpearStab::modifyDamageData: the knockback is done by castendDamageId.
    fn modify_damage_data(&self, _c: &Ctx, dmg: &mut Damage, _src: &Unit, _target: &Unit, _skill_lv: u16) {
        dmg.blewcount = 0;
    }

    /// SkillSpearStab::castendDamageId: all the enemies between the caster and the target are hit (4 cells from the
    /// target towards the caster), as well as the target; each hit one is knocked back.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            if target.id as i32 == area::temp(1) {
                return;
            }
            if skill_attack(c, BF_WEAPON, src, src, target, id, skill_lv, tick, SD_ANIMATION) != 0 {
                let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                skill_blown(c, &s, &t, area::temp(2) as i8 as i32, -1, BLOWN_NONE); // (the count is a `char`)
            }
        } else {
            let (mut x, mut y) = (target.x, target.y);
            let dir = crate::map::calc_dir(target, src.x, src.y) as usize;
            area::set_temp(1, target.id as i32);
            area::set_temp(2, skill_get_blewcount(id, skill_lv));
            // all the enemies between the caster and the target are hit, as well as the target
            if skill_attack(c, BF_WEAPON, src, src, target, id, skill_lv, tick, 0) != 0 {
                let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                skill_blown(c, &s, &t, area::temp(2) as i8 as i32, -1, BLOWN_NONE); // (the count is a `char`)
            }
            let f = *flag | BCT_ENEMY | 1;
            for _ in 0..4 {
                map_foreachincell(c, target.map, x, y, BL_CHAR, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
                x += DIRX[dir];
                y += DIRY[dir];
            }
        }
    }

    /// SkillSpearStab::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 20 * skill_lv as i32;
    }
}

/// map.cpp map_foreachincell: `f` on every unit of `kinds` on the cell (outside the map: none), collected first in
/// the block's order (the other kinds, then the monsters), run on those still on the map; returns the sum.
pub(crate) fn map_foreachincell(c: &Ctx, m: u16, x: i16, y: i16, kinds: u16, f: impl FnMut(&Ctx, &Unit) -> i32) -> i32 {
    let Some(mapdata) = crate::data::maps().get(m) else { return 0 };
    if x < 0 || y < 0 || x >= mapdata.xs || y >= mapdata.ys {
        return 0;
    }
    let ids: Vec<u32> = crate::map::units_in_area(c, m, x, y, x, y, kinds).into_iter().map(|u| u.id).collect();
    run_each(c, ids, f)
}
