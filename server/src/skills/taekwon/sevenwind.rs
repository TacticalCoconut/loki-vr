//! skills/taekwon/sevenwind.cpp: class SkillSevenWind : public SkillImpl (TK_SEVENWIND).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::consts::*;
use crate::status::defs::*;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSevenWind;

impl SkillImpl for SkillSevenWind {
    fn skill_id(&self) -> u16 {
        TK_SEVENWIND
    }

    /// SkillSevenWind::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = match skill_get_ele(id, skill_lv) {
            ELE_EARTH => SC_EARTHWEAPON,
            ELE_WIND => SC_WINDWEAPON,
            ELE_WATER => SC_WATERWEAPON,
            ELE_FIRE => SC_FIREWEAPON,
            ELE_GHOST => SC_GHOSTWEAPON,
            ELE_DARK => SC_SHADOWWEAPON,
            ELE_HOLY => SC_ASPERSIO,
            _ => crate::status::consts::SC_NONE,
        };
        let time = skill_get_time(id, skill_lv) as i64;
        let ok = sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, time);
        let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
        let t = crate::unit::get(c, target.id).unwrap_or_else(|| t.clone());
        sc::sc_start(c, Some(src), &t, SC_SEVENWIND, 100, skill_lv as i32, time);
    }
}
