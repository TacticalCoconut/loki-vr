//! skills/swordman/cannonspear.cpp: class SkillCannonSpear : public SkillImplRecursiveDamageSplash (LG_CANNONSPEAR).

use crate::battle::calc::Damage;
use crate::battle::DMG_SINGLE;
use crate::db::skills::*;
use crate::skill::area;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::{clif_skill_damage, clif_skill_nodamage, DMGVAL_IGNORE};
use crate::skills::novice::{re_lvl_dmod, sce, status_get_amotion};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_SPEAR_SCAR;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillCannonSpear;

impl SkillImpl for SkillCannonSpear {
    fn skill_id(&self) -> u16 {
        LG_CANNONSPEAR
    }

    crate::splash_skill_defaults!();

    /// SkillCannonSpear::castendNoDamageId: the splash through skill_castend_damage_id; the dummy damage packet on the
    /// caster when nothing was hit.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        skill_castend_damage_id(c, src, target, id, skill_lv, tick, *flag);
        if area::temp(2) == 0 {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            clif_skill_damage(c, &s, &s, tick, status_get_amotion(c, &s), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
        }
    }

    /// SkillCannonSpear::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + skill_lv as i32 * (120 + sstatus.str as i32);
        if sce(c, src, SC_SPEAR_SCAR).is_some() {
            *skillratio += 400;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

impl RecursiveDamageSplash for SkillCannonSpear {}
