//! skills/acolyte/howlingoflion.cpp: class SkillHowlingOfLion : public SkillImplRecursiveDamageSplash
//! (SR_HOWLINGOFLION).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::area::splash_target;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::SD_ANIMATION;
use crate::skills::novice::{re_lvl_dmod, splash_damage_default};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::*;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillHowlingOfLion;

/// The song / dance statuses the howl ends on each target, in rAthena's order.
const ENDS: [i32; 19] = [
    SC_SWINGDANCE,
    SC_SYMPHONYOFLOVER,
    SC_MOONLITSERENADE,
    SC_RUSHWINDMILL,
    SC_ECHOSONG,
    SC_HARMONIZE,
    SC_NETHERWORLD,
    SC_VOICEOFSIREN,
    SC_DEEPSLEEP,
    SC_SIRCLEOFNATURE,
    SC_GLOOMYDAY,
    SC_GLOOMYDAY_SK,
    SC_SONGOFMANA,
    SC_DANCEWITHWUG,
    SC_SATURDAYNIGHTFEVER,
    SC_LERADSDEW,
    SC_MELODYOFSINK,
    SC_BEYONDOFWARCRY,
    SC_UNLIMITEDHUMMINGVOICE,
];

impl SkillImpl for SkillHowlingOfLion {
    fn skill_id(&self) -> u16 {
        SR_HOWLINGOFLION
    }

    crate::splash_skill_defaults!();

    /// SkillHowlingOfLion::calculateSkillRatio: 500% per level (RE_LVL_DMOD 100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 500 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillHowlingOfLion::castendNoDamageId: the animation, then the splash through skill_castend_damage_id.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        skill_castend_damage_id(c, src, &t, id, skill_lv, tick, *flag);
    }
}

impl RecursiveDamageSplash for SkillHowlingOfLion {
    /// SkillHowlingOfLion::splashDamage: ends the target's songs and dances, then the parent's hit with
    /// SD_ANIMATION.
    fn splash_damage(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) -> i64 {
        for t in ENDS {
            sc::status_change_end(c, target.id, t, 0);
        }
        let sflag = flag | SD_ANIMATION;
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        splash_damage_default(c, self.skill_id(), src, &t, skill_lv, tick, sflag)
    }

    /// SkillHowlingOfLion::getSplashTarget: splash_target(src).
    fn get_splash_target(&self, _c: &Ctx, src: &Unit) -> u16 {
        splash_target(src)
    }
}
