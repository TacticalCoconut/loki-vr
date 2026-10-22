//! skills/elemental/rocklauncher.cpp: class SkillRockLauncher (EL_ROCK_CRUSHER) and class SkillRockLauncherAttack
//! (EL_ROCK_CRUSHER_ATK), both : public SkillImpl, rAthena e985006.

use crate::battle::calc::Damage;
use crate::battle::target::battle_get_master;
use crate::battle::{BF_MAGIC, BF_WEAPON, DMG_SINGLE};
use crate::db::skills::*;
use crate::skill::attack::skill_attack;
use crate::skill::clif::{clif_skill_damage, clif_skill_nodamage, DMGVAL_IGNORE};
use crate::skills::novice::status_get_amotion;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_ROCK_CRUSHER, SC_ROCK_CRUSHER_ATK};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

// EL_ROCK_CRUSHER
pub struct SkillRockLauncher;

impl SkillImpl for SkillRockLauncher {
    fn skill_id(&self) -> u16 {
        EL_ROCK_CRUSHER
    }

    /// SkillRockLauncher::applyAdditionalEffects: 50% SC_ROCK_CRUSHER for EL_ROCK_CRUSHER's duration.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_ROCK_CRUSHER, 50, skill_lv as i32, skill_get_time(EL_ROCK_CRUSHER, skill_lv) as i64);
    }

    /// SkillRockLauncher::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 700;
    }

    /// SkillRockLauncher::castendDamageId: the skill animation on the master, the dummy damage packet on the
    /// elemental itself, then 50%: the skill's own magic hit, 50%: an EL_ROCK_CRUSHER_ATK weapon hit.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let master = battle_get_master(c, src);
        clif_skill_nodamage(c, Some(src), &master, id, skill_lv as i32, true);
        clif_skill_damage(c, src, src, tick, status_get_amotion(c, src), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
        if ((c.rnd() % 100) as i32) < 50 {
            skill_attack(c, BF_MAGIC, src, src, target, id, skill_lv, tick, *flag);
        } else {
            skill_attack(c, BF_WEAPON, src, src, target, EL_ROCK_CRUSHER_ATK, skill_lv, tick, *flag);
        }
    }
}

// EL_ROCK_CRUSHER_ATK
pub struct SkillRockLauncherAttack;

impl SkillImpl for SkillRockLauncherAttack {
    fn skill_id(&self) -> u16 {
        EL_ROCK_CRUSHER_ATK
    }

    /// SkillRockLauncherAttack::applyAdditionalEffects: 50% SC_ROCK_CRUSHER_ATK for EL_ROCK_CRUSHER's duration.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_ROCK_CRUSHER_ATK, 50, skill_lv as i32, skill_get_time(EL_ROCK_CRUSHER, skill_lv) as i64);
    }

    /// SkillRockLauncherAttack::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 200;
    }
}
