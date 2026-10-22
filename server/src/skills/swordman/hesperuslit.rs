//! skills/swordman/hesperuslit.cpp: class SkillHesperusLit : public WeaponSkillImpl (LG_HESPERUSLIT).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::state::pc_addspiritball;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::{SC_BANDING, SC_FORCEOFVANGUARD, SC_INSPIRATION};
use crate::status::defs::ELE_HOLY;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillHesperusLit;

impl SkillImpl for SkillHesperusLit {
    fn skill_id(&self) -> u16 {
        LG_HESPERUSLIT
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillHesperusLit::applyCounterAdditionalEffects: a Force of Vanguard player gains val3 spheres (each lasting
    /// skill_get_time(LG_FORCEOFVANGUARD, 1), val3 at most).
    fn apply_counter_additional_effects(&self, c: &Ctx, src: &Unit, _target: &Unit, _skill_lv: u16, _tick: Tick, _attack_type: &mut i32) {
        if src.kind != BL_PC {
            return;
        }
        let Some(e) = sce(c, src, SC_FORCEOFVANGUARD) else { return };
        for _ in 0..e.val3 {
            pc_addspiritball(c, src.id, skill_get_time(LG_FORCEOFVANGUARD, 1), e.val3);
        }
    }

    /// SkillHesperusLit::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        if sce(c, src, SC_INSPIRATION).is_some() {
            *skillratio += -100 + 450 * lv;
        } else {
            *skillratio += -100 + 300 * lv;
        }
        *skillratio += sstatus.vit as i32 / 6; // !TODO: What's the VIT bonus?
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillHesperusLit::applyAdditionalEffects: with a Banding of more than 5 members, Pinpoint Attack of a random
    /// learned level follows.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, _skill_lv: u16, tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        if pc_checkskill(c, src, LG_PINPOINTATTACK) > 0 && sce(c, src, SC_BANDING).map(|e| e.val2 > 5).unwrap_or(false) {
            // rnd_value<uint16>(1, pc_checkskill(sd, LG_PINPOINTATTACK))
            let lv = c.rnd_value(1, pc_checkskill(c, src, LG_PINPOINTATTACK)) as u16;
            skill_castend_damage_id(c, src, target, LG_PINPOINTATTACK, lv, tick, 0);
        }
    }

    /// SkillHesperusLit::modifyElement: holy with a Banding of more than 4 members.
    fn modify_element(&self, c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        if sce(c, src, SC_BANDING).map(|e| e.val2 > 4).unwrap_or(false) {
            *element = ELE_HOLY;
        }
    }
}
