//! skills/ninja/ragingfiredragon.cpp: class SkillRagingFireDragon : public SkillImpl (NJ_BAKUENRYU), rAthena e985006.

use super::charms_of_type;
use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::iface::skill_unitsetting;
use crate::skill::state::CHARM_TYPE_FIRE;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRagingFireDragon;

impl SkillImpl for SkillRagingFireDragon {
    fn skill_id(&self) -> u16 {
        NJ_BAKUENRYU
    }

    /// SkillRagingFireDragon::calculateSkillRatio: 50 + 150% per level, + 100% per fire charm.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 50 + 150 * skill_lv as i32;
        let charms = charms_of_type(c, src, CHARM_TYPE_FIRE);
        if charms > 0 {
            *base_skillratio += 100 * charms;
        }
    }

    /// SkillRagingFireDragon::castendDamageId: place units around target.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
        skill_unitsetting(c, &s, id, skill_lv, target.x, target.y, 0);
    }

    /// SkillRagingFireDragon::castendPos2: flag 1 keeps the ammo (it is deleted when the group is).
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        *flag |= 1; // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
        skill_unitsetting(c, src, self.skill_id(), skill_lv, x, y, 0);
    }
}
