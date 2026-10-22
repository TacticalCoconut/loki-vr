//! skills/mage/violentquake.cpp: class SkillViolentQuake : public SkillImpl (AG_VIOLENT_QUAKE), class
//! SkillViolentQuakeAttack : public SkillImpl (AG_VIOLENT_QUAKE_ATK), rAthena e985006.
//!
//! The attack class is not cast by a client: SkillViolentQuake::castendPos2 places its unit groups (skill_unitsetting
//! of AG_VIOLENT_QUAKE_ATK with the delay in `flag`; skillunit agent), whose timers hit through skill_attack, which
//! asks the class for the skill ratio.

use super::allbloom::random_cell;
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

/// AG_VIOLENT_QUAKE
pub struct SkillViolentQuake;

impl SkillImpl for SkillViolentQuake {
    fn skill_id(&self) -> u16 {
        AG_VIOLENT_QUAKE
    }

    /// SkillViolentQuake::castendNoDamageId (reached through castendPos2's Climax level 4 area: the status on each
    /// enemy).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let type_ = skill_get_sc(self.skill_id()) as i32;
        sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time2(self.skill_id(), skill_lv) as i64);
    }

    /// SkillViolentQuake::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let mut area = skill_get_splash(id, skill_lv);
        let unit_time = skill_get_time(id, skill_lv);
        let unit_interval = skill_get_unit_interval(id);
        let mut climax_lv: u16 = 0;

        // Grab Climax's effect level if active.
        if let Some(cl) = sce(c, src, SC_CLIMAX) {
            climax_lv = cl.val1 as u16;
        }

        // Fixes rising rocks spawn area to 7x7.
        if climax_lv == 5 {
            area = 3;
        }

        // Displays the earthquake.
        iface::skill_unitsetting(c, src, id, skill_lv, x, y, 0);

        let (x, y) = (x as i32, y as i32);
        if climax_lv == 4 {
            // Deals no damage and instead inflicts a status on the enemys in range.
            let i = skill_get_splash(id, skill_lv);
            let f = *flag | BCT_ENEMY | 1;
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            foreach_in_area(c, s.map, (x - i) as i16, (y - i) as i16, (x + i) as i16, (y + i) as i16, BL_CHAR, Wall::None, |c, bl| {
                area::skill_area_sub(c, bl, &s, id, skill_lv, tick, f, skill_castend_nodamage_id)
            });
        } else {
            // Spawn the rising rocks on random spots at separate intervals
            // (`i <= unit_time / unit_interval`: a zero interval would crash rAthena; here none spawn)
            let count = unit_time.checked_div(unit_interval).unwrap_or(0);
            let mut i = 1;
            while i <= count {
                let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                let tmpx = random_cell(c, x, area);
                let tmpy = random_cell(c, y, area);
                iface::skill_unitsetting(c, &s, AG_VIOLENT_QUAKE_ATK, skill_lv, tmpx, tmpy, flag.wrapping_add(i.wrapping_mul(unit_interval)));

                if climax_lv == 1 {
                    // Spwan a 2nd rising rock along with the 1st one.
                    let tmpx = random_cell(c, x, area);
                    let tmpy = random_cell(c, y, area);
                    iface::skill_unitsetting(c, &s, AG_VIOLENT_QUAKE_ATK, skill_lv, tmpx, tmpy, flag.wrapping_add(i.wrapping_mul(unit_interval)));
                }
                i += 1;
            }
        }
    }
}

/// AG_VIOLENT_QUAKE_ATK
pub struct SkillViolentQuakeAttack;

impl SkillImpl for SkillViolentQuakeAttack {
    fn skill_id(&self) -> u16 {
        AG_VIOLENT_QUAKE_ATK
    }

    /// SkillViolentQuakeAttack::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 200 + 1200 * skill_lv as i32 + 5 * sstatus.spl as i32;
        // (climax buff applied with pc_skillatk_bonus)
        re_lvl_dmod(c, src, skillratio, 100);
    }
}
