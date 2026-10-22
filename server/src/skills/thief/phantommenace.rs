//! skills/thief/phantommenace.cpp: class SkillPhantomMenace : public WeaponSkillImpl (GC_PHANTOMMENACE).

use crate::battle::calc::Damage;
use crate::battle::{BCT_ENEMY, DMG_SINGLE};
use crate::db::skills::*;
use crate::map::BL_CHAR;
use crate::skill::area::{foreach_in_range, skill_area_sub, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::{clif_skill_damage, clif_skill_nodamage, DMGVAL_IGNORE};
use crate::skills::novice::{sce, status_get_amotion};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::{SC_CAMOUFLAGE, SC_CLOAKINGEXCEED, SC_STEALTHFIELD, SC__SHADOWFORM};
use crate::status::defs::{OPTION_CHASEWALK, OPTION_CLOAK, OPTION_HIDE};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillPhantomMenace;

impl SkillImpl for SkillPhantomMenace {
    fn skill_id(&self) -> u16 {
        GC_PHANTOMMENACE
    }

    /// SkillPhantomMenace::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 200;
    }

    /// SkillPhantomMenace::castendDamageId: only hits invisible targets (flag&1: a unit of the splash); Shadow Form
    /// ends at [100 - (Skill Level x 10)] % (should only end, no damage dealt).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        if *flag & 1 == 0 {
            return;
        }
        let has_sc = crate::battle::mobif::has_sc(target.kind);
        // Only Hits Invisible Targets
        if has_sc && ((target.sc.option & (OPTION_HIDE | OPTION_CLOAK | OPTION_CHASEWALK)) != 0 || sce(c, target, SC_CAMOUFLAGE).is_some() || sce(c, target, SC_STEALTHFIELD).is_some()) {
            sc::status_change_end(c, target.id, SC_CLOAKINGEXCEED, 0);
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            weapon_castend_damage_id(c, self.skill_id(), &s, &t, skill_lv, tick, *flag);
        }
        if has_sc {
            if let Some(e) = sce(c, target, SC__SHADOWFORM) {
                // [100 - (Skill Level x 10)] %
                if ((c.rnd() % 100) as i32) < 100 - e.val1 * 10 {
                    sc::status_change_end(c, target.id, SC__SHADOWFORM, 0); // Should only end, no damage dealt.
                }
            }
        }
    }

    /// SkillPhantomMenace::castendNoDamageId: a damage packet without damage, the animation, then the damage part on
    /// every character around the caster.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_damage(c, src, target, tick, status_get_amotion(c, src), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let f = *flag | BCT_ENEMY | 1;
        foreach_in_range(c, src, skill_get_splash(id, skill_lv) as i16, BL_CHAR, Wall::Config, |c, bl| skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
    }
}
