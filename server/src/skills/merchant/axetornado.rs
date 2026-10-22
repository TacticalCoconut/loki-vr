//! skills/merchant/axetornado.cpp: class SkillAxeTornado : public SkillImplRecursiveDamageSplash (NC_AXETORNADO).

use crate::battle::calc::Damage;
use crate::battle::DMG_SINGLE;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::{clif_skill_damage, clif_skill_nodamage, DMGVAL_IGNORE};
use crate::skills::novice::{re_lvl_dmod, sce, status_get_amotion};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_AXE_STOMP;
use crate::status::status_get_status_data;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// SkillAxeTornado::SkillAxeTornado() : SkillImplRecursiveDamageSplash(NC_AXETORNADO).
pub struct SkillAxeTornado;

impl SkillImpl for SkillAxeTornado {
    fn skill_id(&self) -> u16 {
        NC_AXETORNADO
    }
    crate::splash_skill_defaults!();

    /// SkillAxeTornado::calculateSkillRatio: 200% + 180% per level + VIT x 2, +380% under SC_AXE_STOMP, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = status_get_status_data(c, src);
        *skillratio += -100 + 200 + 180 * skill_lv as i32 + sstatus.vit as i32 * 2;
        if sce(c, src, SC_AXE_STOMP).is_some() {
            *skillratio += 380;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillAxeTornado::castendNoDamageId: the skill packet, skill_castend_damage_id on the target (the splash), and a
    /// damage packet on the caster when it hit nothing (skill_area_temp[2] == 0).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        skill_castend_damage_id(c, src, &t, id, skill_lv, tick, *flag);
        if crate::skill::area::temp(2) == 0 {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            clif_skill_damage(c, &s, &s, tick, status_get_amotion(c, &s), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
        }
    }
}

impl RecursiveDamageSplash for SkillAxeTornado {}
