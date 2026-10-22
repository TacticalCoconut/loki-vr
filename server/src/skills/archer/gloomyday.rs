//! skills/archer/gloomyday.cpp: class SkillGloomyDay : public SkillImpl (WM_GLOOMYDAY).

use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::pc_checkskill;
use crate::skills::SkillImpl;
use crate::status::consts::SC_GLOOMYDAY_SK;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillGloomyDay;

impl SkillImpl for SkillGloomyDay {
    fn skill_id(&self) -> u16 {
        WM_GLOOMYDAY
    }

    /// SkillGloomyDay::castendNoDamageId: the no-damage packet, then SC_GLOOMYDAY_SK on a player who knows one of the
    /// boosted skills (Brandish Spear, Spiral Pierce, Smite, Shield Boomerang, Shield Chain, Shield Press), else the
    /// skill's own status.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let t = super::fresh(c, target);
        let time = skill_get_time(id, skill_lv) as i64;
        // pc_checkskill(dstsd, …) answers 0 when the target is not a player
        if target.kind == crate::map::BL_PC
            && (pc_checkskill(c, &t, KN_BRANDISHSPEAR) != 0
                || pc_checkskill(c, &t, LK_SPIRALPIERCE) != 0
                || pc_checkskill(c, &t, CR_SHIELDCHARGE) != 0
                || pc_checkskill(c, &t, CR_SHIELDBOOMERANG) != 0
                || pc_checkskill(c, &t, PA_SHIELDCHAIN) != 0
                || pc_checkskill(c, &t, LG_SHIELDPRESS) != 0)
        {
            // !TODO: Which skills aren't boosted anymore?
            sc::sc_start(c, Some(src), &t, SC_GLOOMYDAY_SK, 100, skill_lv as i32, time);
            return;
        }
        sc::sc_start(c, Some(src), &t, type_, 100, skill_lv as i32, time);
    }
}
