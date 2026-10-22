//! skills/ninja/rapidthrow.cpp: class SkillRapidThrow : public SkillImplRecursiveDamageSplash (KO_MUCHANAGE), rAthena
//! e985006.

use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{self, foreach_in_area, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skills::{splash_castend_damage_id, RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRapidThrow;

impl SkillImpl for SkillRapidThrow {
    fn skill_id(&self) -> u16 {
        KO_MUCHANAGE
    }

    /// SkillImplRecursiveDamageSplash::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        splash_castend_damage_id(self, c, src, target, skill_lv, tick, flag);
    }

    /// SkillRapidThrow::castendPos2: the targets of the area are counted into skill_area_temp[0] (the zeny damage is
    /// split between them), then with (100 - 1000 / (DEX + LUK) x 5) x (level / 2 + 5) / 10 % (at least 0) the area
    /// is hit.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let sstatus = crate::status::status_get_status_data(c, src);
        let i = skill_get_splash(id, skill_lv) as i16;
        let dexluk = sstatus.dex as i32 + sstatus.luk as i32;
        let mut rate = (100 - (1000 / dexluk * 5)) * (skill_lv as i32 / 2 + 5) / 10;
        if rate < 0 {
            rate = 0;
        }
        let n = foreach_in_area(c, src.map, x - i, y - i, x + i, y + i, BL_CHAR, Wall::Config, |c, bl| {
            area::skill_area_sub(c, bl, src, id, skill_lv, tick, BCT_ENEMY, area::skill_area_sub_count)
        });
        area::set_temp(0, n);
        if ((c.rnd() % 100) as i32) < rate {
            let f = *flag | BCT_ENEMY | 1;
            foreach_in_area(c, src.map, x - i, y - i, x + i, y + i, BL_CHAR, Wall::Config, |c, bl| {
                area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id)
            });
        }
    }
}

impl RecursiveDamageSplash for SkillRapidThrow {}
