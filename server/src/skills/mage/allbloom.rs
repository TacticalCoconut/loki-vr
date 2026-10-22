//! skills/mage/allbloom.cpp: class SkillAllBloom : public SkillImpl (AG_ALL_BLOOM), class SkillAllBloomAttack :
//! public SkillImpl (AG_ALL_BLOOM_ATK), class SkillAllBloomAttack2 : public SkillImpl (AG_ALL_BLOOM_ATK2), rAthena
//! e985006.
//!
//! The two attack classes are not cast by a client: SkillAllBloom::castendPos2 places their unit groups
//! (skill_unitsetting of AG_ALL_BLOOM_ATK / AG_ALL_BLOOM_ATK2 with the delay in `flag`; skillunit agent), whose
//! timers hit through skill_attack, which asks these classes for the skill ratio.

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_area, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::iface;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::SkillImpl;
use crate::status::consts::SC_CLIMAX;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// `x - area + rnd() % (area * 2 + 1)` stored in AllBloom's / ViolentQuake's `uint16 tmpx`, then passed as
/// skill_unitsetting's int16 (the int math, truncated). `area * 2 + 1` is never 0 for a database splash (>= 0); a
/// broken value would crash rAthena (division by zero), here the random part is 0.
pub(crate) fn random_cell(c: &Ctx, center: i32, area: i32) -> i16 {
    let r = (c.rnd() as i32).checked_rem(area.wrapping_mul(2).wrapping_add(1)).unwrap_or(0);
    center.wrapping_sub(area).wrapping_add(r) as u16 as i16
}

/// AG_ALL_BLOOM
pub struct SkillAllBloom;

impl SkillImpl for SkillAllBloom {
    fn skill_id(&self) -> u16 {
        AG_ALL_BLOOM
    }

    /// SkillAllBloom::castendNoDamageId (reached through castendPos2's Climax level 4 area: the status on each enemy).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let type_ = skill_get_sc(self.skill_id()) as i32;
        sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillAllBloom::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let area = skill_get_splash(id, skill_lv);
        let mut unit_time = skill_get_time(id, skill_lv);
        let mut unit_interval = skill_get_unit_interval(id);
        let mut climax_lv: u16 = 0;

        // Grab Climax's effect level if active.
        if let Some(cl) = sce(c, src, SC_CLIMAX) {
            climax_lv = cl.val1 as u16;
        }

        if climax_lv == 1 {
            // Rose buds spawn at double the speed.
            unit_time /= 2;
            unit_interval /= 2;
        }

        // Displays the flower garden.
        iface::skill_unitsetting(c, src, id, skill_lv, x, y, 0);

        let (x, y) = (x as i32, y as i32);
        let mut i: i32;
        if climax_lv == 4 {
            // Deals no damage and instead inflicts a status on the enemys in range.
            i = skill_get_splash(id, skill_lv);
            let f = *flag | BCT_ENEMY | 1;
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            foreach_in_area(c, s.map, (x - i) as i16, (y - i) as i16, (x + i) as i16, (y + i) as i16, BL_CHAR, Wall::None, |c, bl| {
                area::skill_area_sub(c, bl, &s, id, skill_lv, tick, f, skill_castend_nodamage_id)
            });
        } else {
            // Spawn the rose buds on random spots at separate intervals
            // (`i <= unit_time / unit_interval`: a zero interval would crash rAthena; here none spawn)
            let count = unit_time.checked_div(unit_interval).unwrap_or(0);
            i = 1;
            while i <= count {
                let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                let tmpx = random_cell(c, x, area);
                let tmpy = random_cell(c, y, area);
                iface::skill_unitsetting(c, &s, AG_ALL_BLOOM_ATK, skill_lv, tmpx, tmpy, flag.wrapping_add(i.wrapping_mul(unit_interval)));

                if id == AG_ALL_BLOOM && climax_lv == 2 {
                    // Spwan a 2nd rose bud along with the 1st one.
                    let tmpx = random_cell(c, x, area);
                    let tmpy = random_cell(c, y, area);
                    iface::skill_unitsetting(c, &s, AG_ALL_BLOOM_ATK, skill_lv, tmpx, tmpy, flag.wrapping_add(i.wrapping_mul(unit_interval)));
                }
                i += 1;
            }
        }

        // One final attack the size of the flower garden is dealt after
        // all rose buds explode if Climax level 5 is active.
        // (`i` is the loop's value after its end: one past the last bud)
        if climax_lv == 5 {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            iface::skill_unitsetting(c, &s, AG_ALL_BLOOM_ATK2, skill_lv, x as i16, y as i16, flag.wrapping_add(i.wrapping_mul(unit_interval)));
        }
    }
}

/// AG_ALL_BLOOM_ATK
pub struct SkillAllBloomAttack;

impl SkillImpl for SkillAllBloomAttack {
    fn skill_id(&self) -> u16 {
        AG_ALL_BLOOM_ATK
    }

    /// SkillAllBloomAttack::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 200 + 1200 * skill_lv as i32 + 5 * sstatus.spl as i32;
        // (climax buff applied with pc_skillatk_bonus)
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

/// AG_ALL_BLOOM_ATK2
pub struct SkillAllBloomAttack2;

impl SkillImpl for SkillAllBloomAttack2 {
    fn skill_id(&self) -> u16 {
        AG_ALL_BLOOM_ATK2
    }

    /// SkillAllBloomAttack2::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += -100 + 85000;
        // Skill not affected by Baselevel and SPL
    }
}
