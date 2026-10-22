//! skills/acolyte/flashcombo.cpp: class SkillFlashCombo : public SkillImpl (SR_FLASHCOMBO).

use crate::battle::BF_WEAPON;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::timerskill::skill_addtimerskill;
use crate::skill::SD_LEVEL;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillFlashCombo;

impl SkillImpl for SkillFlashCombo {
    fn skill_id(&self) -> u16 {
        SR_FLASHCOMBO
    }

    /// SkillFlashCombo::castendNoDamageId: the status on the caster, then Dragon Combo, Fallen Empire and Tiger
    /// Cannon as timerskills 0 / 750 / 1250 ms later (flag | SD_LEVEL); a player's attackabletime, canuseitem_tick
    /// and canact_tick move to the end of the chain.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        const COMBO: [u16; 3] = [SR_DRAGONCOMBO, SR_FALLENEMPIRE, SR_TIGERCANNON];
        const DELAY: [i64; 3] = [0, 750, 1250];
        if src.kind == BL_PC {
            // Disable attacking/acting/moving for skill's duration.
            if let Some(mut u) = unit::get(c, src.id) {
                u.ud.attackabletime = tick + DELAY[2];
                u.ud.canact_tick = tick + DELAY[2];
                unit::save(c, &u);
            }
            if let Some(mut it) = crate::inventory::st(c, src.id) {
                it.canuseitem_tick = tick + DELAY[2];
                crate::inventory::put_st(c, &it);
            }
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let ok = sc::sc_start(c, Some(&s), &s, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let s = unit::get(c, src.id).unwrap_or(s);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, ok);
        for i in 0..COMBO.len() {
            skill_addtimerskill(c, &s, tick + DELAY[i], target.id, 0, 0, COMBO[i], skill_lv, BF_WEAPON, *flag | SD_LEVEL);
        }
    }
}
