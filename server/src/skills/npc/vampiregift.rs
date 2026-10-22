//! skills/npc/vampiregift.cpp: class SkillVampireGift : public SkillImplRecursiveDamageSplash (NPC_VAMPIRE_GIFT).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::splash_damage_default;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillVampireGift;

impl SkillImpl for SkillVampireGift {
    fn skill_id(&self) -> u16 {
        NPC_VAMPIRE_GIFT
    }

    crate::splash_skill_defaults!();

    /// SkillVampireGift::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += ((skill_lv as i32 - 1) % 5 + 1) * 100;
    }

    /// SkillVampireGift::castendNoDamageId: skill_castend_damage_id on the caster itself (the splash around it).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_castend_damage_id(c, src, src, self.skill_id(), skill_lv, tick, *flag);
    }
}

impl RecursiveDamageSplash for SkillVampireGift {
    /// SkillVampireGift::splashDamage: SkillImplRecursiveDamageSplash::splashDamage, then the caster heals the damage
    /// dealt (the heal display is clif_skill_nodamage(nullptr, *src, AL_HEAL, heal)).
    fn splash_damage(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) -> i64 {
        let heal = splash_damage_default(c, self.skill_id(), src, target, skill_lv, tick, flag) as i32;
        if heal > 0 {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            clif_skill_nodamage(c, None, &s, AL_HEAL, heal, true);
            crate::status::hp::status_heal(c, src.id, heal as i64, 0, 0, 0);
        }
        heal as i64
    }
}
