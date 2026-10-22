//! skills/acolyte/bssacramenti.cpp: class SkillBenedictioSanctissimiSacramenti : public SkillImpl (PR_BENEDICTIO).

use crate::battle::{BCT_ENEMY, BF_MAGIC};
use crate::db::skills::{BCT_ALL, *};
use crate::map::{BL_CHAR, BL_PC};
use crate::skill::area::{self, foreach_in_area, Wall};
use crate::skill::attack::skill_attack;
use crate::skill::castend::{skill_castend_damage_id, skill_castend_nodamage_id};
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::defs::RC_DEMON;
use crate::status::sc::{self, battle_check_undead};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillBenedictioSanctissimiSacramenti;

impl SkillImpl for SkillBenedictioSanctissimiSacramenti {
    fn skill_id(&self) -> u16 {
        PR_BENEDICTIO
    }

    /// SkillBenedictioSanctissimiSacramenti::castendNoDamageId: the holy armor on everyone but undead and demons.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let tstatus = crate::status::status_get_status_data(c, target);
        if !battle_check_undead(tstatus.race as i32, tstatus.def_ele as i32) && tstatus.race as i32 != RC_DEMON {
            let ok = sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
        }
    }

    /// SkillBenedictioSanctissimiSacramenti::castendDamageId: should attack undead and demons. [Skotlex]
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let tstatus = crate::status::status_get_status_data(c, target);
        if battle_check_undead(tstatus.race as i32, tstatus.def_ele as i32) || tstatus.race as i32 == RC_DEMON {
            skill_attack(c, BF_MAGIC, src, src, target, self.skill_id(), skill_lv, tick, *flag);
        }
    }

    /// SkillBenedictioSanctissimiSacramenti::castendPos2: the players in the area get the status, then every
    /// enemy in it is hit (map_foreachinallarea twice).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        area::set_temp(1, src.id as i32);
        let i = skill_get_splash(id, skill_lv) as i16;
        let f = *flag | BCT_ALL | 1;
        foreach_in_area(c, src.map, x - i, y - i, x + i, y + i, BL_PC, Wall::None, |c, t| area::skill_area_sub(c, t, src, id, skill_lv, tick, f, skill_castend_nodamage_id));
        let f = *flag | BCT_ENEMY | 1;
        foreach_in_area(c, src.map, x - i, y - i, x + i, y + i, BL_CHAR, Wall::None, |c, t| area::skill_area_sub(c, t, src, id, skill_lv, tick, f, skill_castend_damage_id));
    }
}
