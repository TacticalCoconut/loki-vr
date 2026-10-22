//! skills/merchant/axestomp.cpp: class SkillAxeStomp : public SkillImplRecursiveDamageSplash (MT_AXE_STOMP).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::status::defs::W_2HAXE;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// SkillAxeStomp::SkillAxeStomp() : SkillImplRecursiveDamageSplash(MT_AXE_STOMP).
pub struct SkillAxeStomp;

impl SkillImpl for SkillAxeStomp {
    fn skill_id(&self) -> u16 {
        MT_AXE_STOMP
    }
    crate::splash_skill_defaults!();

    /// SkillAxeStomp::modifyDamageData: 3 hits with a two-handed axe (player casters).
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        // const map_session_data* sd = BL_CAST(BL_PC, &src); sd->status.weapon == W_2HAXE
        if src.kind == BL_PC && crate::pc::character_of(c, src.id).map(|ch| ch.weapon as i32 == W_2HAXE).unwrap_or(false) {
            dmg.div_ = 3;
        }
    }

    /// SkillAxeStomp::castendNoDamageId: the skill's status (SC_AXE_STOMP) on the target, the skill packet, then
    /// skill_castend_damage_id on the same target (the splash parent's castendDamageId: the area around it).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        sc::sc_start(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, true);
        skill_castend_damage_id(c, &s, &t, id, skill_lv, tick, *flag);
    }

    /// SkillAxeStomp::calculateSkillRatio: 450 + 1150 * lv %, + 5 * POW, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *base_skillratio += -100 + 450 + 1150 * skill_lv as i32;
        *base_skillratio += 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, base_skillratio, 100);
    }
}

impl crate::skills::RecursiveDamageSplash for SkillAxeStomp {}
