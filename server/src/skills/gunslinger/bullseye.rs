//! skills/gunslinger/bullseye.cpp: class SkillBullseye : public WeaponSkillImpl (GS_BULLSEYE), rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::SC_COMA;
use crate::status::defs::{MD_STATUSIMMUNE, RC_BRUTE, RC_DEMIHUMAN, RC_PLAYER_DORAM, RC_PLAYER_HUMAN, SCSTART_NONE};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillBullseye;

/// Brute, demi-human, human player or doram player (the races Bull's Eye works well against).
fn bullseye_race(race: i32) -> bool {
    race == RC_BRUTE || race == RC_DEMIHUMAN || race == RC_PLAYER_HUMAN || race == RC_PLAYER_DORAM
}

impl SkillImpl for SkillBullseye {
    fn skill_id(&self) -> u16 {
        GS_BULLSEYE
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillBullseye::calculateSkillRatio: + 400% against brutes / demi-humans / players that are not status immune
    /// (only works well against brute/demihumans non bosses).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, _src: &Unit, target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let tstatus = crate::status::status_get_status_data(c, target);
        if bullseye_race(tstatus.race as i32) && !crate::battle::mobif::has_mode(tstatus.mode, MD_STATUSIMMUNE) {
            *base_skillratio += 400;
        }
    }

    /// SkillBullseye::applyAdditionalEffects: 0.1% coma rate on those races.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        let tstatus = crate::status::status_get_status_data(c, target);
        if bullseye_race(tstatus.race as i32) {
            sc::status_change_start(c, Some(src), target, SC_COMA, 10, skill_lv as i32, 0, src.id as i32, 0, 0, SCSTART_NONE, 0);
        }
    }
}
