//! skills/swordman/servantweapondemolition.cpp: class SkillServantWeaponDemolition : public
//! SkillImplRecursiveDamageSplash (DK_SERVANT_W_DEMOL).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{re_lvl_dmod, sce, splash_damage_default};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_SERVANT_SIGN;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillServantWeaponDemolition;

impl SkillImpl for SkillServantWeaponDemolition {
    fn skill_id(&self) -> u16 {
        DK_SERVANT_W_DEMOL
    }

    crate::splash_skill_defaults!();

    /// SkillServantWeaponDemolition::modifyDamageData: at most one hit per servant (sd->servantball +
    /// sd->servantball_old).
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        if src.kind != BL_PC {
            return;
        }
        let st = crate::skill::state::get(c, src.id);
        let servants = st.servantball as i32 + st.servantball_old as i32;
        if servants < dmg.div_ as i32 {
            dmg.div_ = servants as i16;
        }
    }

    /// SkillServantWeaponDemolition::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        skill_castend_damage_id(c, src, target, id, skill_lv, tick, *flag);
    }

    /// SkillServantWeaponDemolition::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 500 * skill_lv as i32;
        *skillratio += 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillServantWeaponDemolition {
    /// SkillServantWeaponDemolition::splashDamage: Servant Weapon - Demol only hits if the target is marked with a
    /// sign by the attacking caster.
    fn splash_damage(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) -> i64 {
        if !sce(c, target, SC_SERVANT_SIGN).map(|e| e.val1 == src.id as i32).unwrap_or(false) {
            return 0;
        }
        splash_damage_default(c, self.skill_id(), src, target, skill_lv, tick, flag)
    }
}
