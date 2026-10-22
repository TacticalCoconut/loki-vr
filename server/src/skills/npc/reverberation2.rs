//! skills/npc/reverberation2.cpp: its two classes, class SkillReverberation2 : public SkillImpl (NPC_REVERBERATION)
//! and class SkillReverberationAttack : public SkillImplRecursiveDamageSplash (NPC_REVERBERATION_ATK).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::area;
use crate::skill::iface::skill_unitsetting;
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

// NPC_REVERBERATION
pub struct SkillReverberation2;

impl SkillImpl for SkillReverberation2 {
    fn skill_id(&self) -> u16 {
        NPC_REVERBERATION
    }

    /// SkillReverberation2::castendPos2: the skill's ground unit (skill_unitsetting: skillunit agent, skill/iface.rs).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}

// NPC_REVERBERATION_ATK
pub struct SkillReverberationAttack;

impl SkillImpl for SkillReverberationAttack {
    fn skill_id(&self) -> u16 {
        NPC_REVERBERATION_ATK
    }

    crate::splash_skill_defaults!();

    /// SkillReverberationAttack::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 400 + 200 * skill_lv as i32;
    }
}

impl RecursiveDamageSplash for SkillReverberationAttack {
    /// SkillReverberationAttack::getSplashTarget: splash_target(src).
    fn get_splash_target(&self, _c: &Ctx, src: &Unit) -> u16 {
        area::splash_target(src)
    }

    /// SkillReverberationAttack::splashSearch: no target is the original one (skill_area_temp[1] = 0: every target
    /// gets the animation), then SkillImplRecursiveDamageSplash::splashSearch.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        area::set_temp(1, 0);
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
