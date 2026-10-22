//! skills/archer/unchainedserenade.cpp: class SkillUnchainedSerenade : public WeaponSkillImpl (BA_DISSONANCE). A
//! song: Renewal's are instant (skill_castend_song; its hit is skill_apply_songs' skill_attack BF_MAGIC).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillUnchainedSerenade;

impl SkillImpl for SkillUnchainedSerenade {
    fn skill_id(&self) -> u16 {
        BA_DISSONANCE
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillUnchainedSerenade::calculateSkillRatio (Renewal): `base_skillratio += 10 + skill_lv * 50`, then a player's
    /// ratio is scaled by its job level / 10.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 10 + skill_lv as i32 * 50;
        if let Some(job_level) = super::job_level(c, src) {
            *base_skillratio = *base_skillratio * job_level / 10;
        }
    }

    /// SkillUnchainedSerenade::castendNoDamageId: `#ifdef RENEWAL skill_castend_song(src, getSkillId(), skill_lv, tick); #endif`.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        super::song_castend_nodamage(c, self.skill_id(), src, skill_lv, tick);
    }

    /// SkillUnchainedSerenade::castendPos2: its whole body is `#ifndef RENEWAL` (the pre-Renewal song unit): empty
    /// here.
    fn castend_pos2(&self, _c: &Ctx, _src: &Unit, _x: i16, _y: i16, _skill_lv: u16, _tick: Tick, _flag: &mut i32) {}
}
