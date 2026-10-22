//! skills/taekwon/jumpkick.cpp: class SkillJumpKick : public SkillImpl (TK_JUMPKICK).

use crate::battle::calc::Damage;
use crate::battle::{BCT_ENEMY, BF_WEAPON};
use crate::db::jobs::ids::MAPID_SOUL_LINKER;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::attack::skill_attack;
use crate::skill::clif::{clif_skill_fail, fail::USESKILL_FAIL};
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::*;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillJumpKick;

impl SkillImpl for SkillJumpKick {
    fn skill_id(&self) -> u16 {
        TK_JUMPKICK
    }

    /// SkillJumpKick::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        // Different damage formulas depending on damage trigger
        if sce(c, src, SC_COMBO).map(|e| e.val1 == self.skill_id() as i32).unwrap_or(false) {
            *base_skillratio += -100 + 4 * crate::status::status_get_lv(c, src); // Tumble formula [4%*baselevel]
        } else if wd.miscflag != 0 {
            *base_skillratio += -100 + 4 * crate::status::status_get_lv(c, src); // Running formula [4%*baselevel]
            if sce(c, src, SC_SPURT).is_some() {
                // Spurt formula [8%*baselevel]
                *base_skillratio *= 2;
            }
        } else {
            *base_skillratio += -70 + 10 * skill_lv as i32;
        }
    }

    /// SkillJumpKick::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, _src: &Unit, target: &Unit, _skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        // debuff the following statuses (dstsd->class_ != MAPID_SOUL_LINKER: the whole class, not its second-job mask)
        if target.kind != BL_PC {
            return;
        }
        let class_ = crate::status::data::sd_get(c, target.id).map(|s| s.class_).unwrap_or(0);
        if class_ != MAPID_SOUL_LINKER && !sc::has_sce(c, target.id, SC_PRESERVE) {
            for t in [SC_SPIRIT, SC_ADRENALINE2, SC_KAITE, SC_KAAHI, SC_ONEHAND, SC_ASPDPOTION2] {
                sc::status_change_end(c, target.id, t, 0);
            }
            // New soul links confirmed to not dispell with this skill
            // but thats likely a bug since soul links can't stack and
            // soul cutter skill works on them. So ill add this here for now. [Rytech]
            for t in [SC_SOULGOLEM, SC_SOULSHADOW, SC_SOULFALCON, SC_SOULFAIRY] {
                sc::status_change_end(c, target.id, t, 0);
            }
        }
    }

    /// SkillJumpKick::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // Check if the target is an enemy; if not, skill should fail so the character doesn't unit_movepos (exploitable)
        if crate::battle::target::battle_check_target(c, src, target, BCT_ENEMY) > 0 {
            if crate::unit::movepos(c, src.id, target.x, target.y, 2, true) {
                let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
                skill_attack(c, BF_WEAPON, &s, &s, target, id, skill_lv, tick, *flag);
                let s = crate::unit::get(c, src.id).unwrap_or_else(|| s.clone());
                crate::battle::clif::clif_blown(c, &s);
            }
        } else if src.kind == BL_PC {
            clif_skill_fail(c, src, id, USESKILL_FAIL, 0, 0);
        }
    }
}
