//! skills/ninja/releaseninjaspell.cpp: class SkillReleaseNinjaSpell : public SkillImpl (KO_KAIHOU), rAthena e985006.

use crate::battle::calc::Damage;
use crate::battle::BF_MAGIC;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::attack::skill_attack;
use crate::skill::state::{self as pcskill, CHARM_TYPE_NONE};
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillReleaseNinjaSpell;

/// `sd && sd->spiritcharm_type != CHARM_TYPE_NONE && sd->spiritcharm > 0`: (count, type).
fn held_charms(c: &Ctx, src: &Unit) -> Option<(i32, i32)> {
    if src.kind != BL_PC {
        return None;
    }
    let s = pcskill::get(c, src.id);
    (s.spiritcharm_type != CHARM_TYPE_NONE && s.spiritcharm > 0).then_some((s.spiritcharm as i32, s.spiritcharm_type))
}

impl SkillImpl for SkillReleaseNinjaSpell {
    fn skill_id(&self) -> u16 {
        KO_KAIHOU
    }

    /// SkillReleaseNinjaSpell::calculateSkillRatio: 200% per charm held, RE_LVL_DMOD(100), and the charms are spent
    /// right here (pc_delspiritcharm from inside the ratio, as rAthena does).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        if let Some((count, type_)) = held_charms(c, src) {
            *skillratio += -100 + 200 * count;
            re_lvl_dmod(c, src, skillratio, 100);
            pcskill::pc_delspiritcharm(c, src.id, count, type_);
        }
    }

    /// SkillReleaseNinjaSpell::castendDamageId: a magic hit.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_attack(c, BF_MAGIC, src, src, target, self.skill_id(), skill_lv, tick, *flag);
    }

    /// SkillReleaseNinjaSpell::modifyElement: the element of the charms held.
    fn modify_element(&self, c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        if let Some((_, type_)) = held_charms(c, src) {
            *element = type_;
        }
    }
}
