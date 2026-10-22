//! skills/elemental/tidalweapon.cpp: class SkillTidalWeapon : public SkillImpl (EL_TIDAL_WEAPON), rAthena e985006.

use crate::battle::calc::Damage;
use crate::battle::target::battle_get_master;
use crate::battle::DMG_SINGLE;
use crate::db::skills::*;
use crate::map::BL_ELEM;
use crate::skill::attack::skill_attack;
use crate::skill::clif::{clif_skill_damage, clif_skill_nodamage, DMGVAL_IGNORE};
use crate::skills::novice::{sce, status_get_amotion};
use crate::skills::SkillImpl;
use crate::status::consts::{SC_TIDAL_WEAPON, SC_TIDAL_WEAPON_OPTION};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillTidalWeapon;

impl SkillImpl for SkillTidalWeapon {
    fn skill_id(&self) -> u16 {
        EL_TIDAL_WEAPON
    }

    /// SkillTidalWeapon::calculateSkillRatio.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 1400;
    }

    /// SkillTidalWeapon::castendDamageId (elementals only): the skill animation on the master and the dummy damage
    /// packet on the elemental; a running pair (SC_TIDAL_WEAPON on the elemental or SC_TIDAL_WEAPON_OPTION on the
    /// target) ends (the option on the master, SC_TIDAL_WEAPON on the elemental); then 50%: the skill's own hit on
    /// the target, 50%: SC_TIDAL_WEAPON on the elemental and SC_TIDAL_WEAPON_OPTION (val1 = the elemental's id) on
    /// the master; last, the skill animation on the elemental.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        if src.kind != BL_ELEM {
            return;
        }
        let id = self.skill_id();
        let type_ = SC_TIDAL_WEAPON_OPTION;
        let type2 = SC_TIDAL_WEAPON;
        let master = battle_get_master(c, src);
        clif_skill_nodamage(c, Some(src), &master, id, skill_lv as i32, true);
        clif_skill_damage(c, src, src, tick, status_get_amotion(c, src), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
        // tsc_ele->getSCE(type2) / tsc->getSCE(type): status_get_sc(ele) / status_get_sc(target)
        if sce(c, src, type2).is_some() || sce(c, target, type_).is_some() {
            let master = battle_get_master(c, src);
            sc::status_change_end(c, master.id, type_, 0);
            sc::status_change_end(c, src.id, type2, 0);
        }
        if ((c.rnd() % 100) as i32) < 50 {
            skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
        } else {
            let dur = skill_get_time(id, skill_lv) as i64;
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            sc::sc_start(c, Some(&s), &s, type2, 100, skill_lv as i32, dur);
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let master = battle_get_master(c, &s);
            // ele->id
            sc::sc_start(c, Some(&s), &master, type_, 100, src.id as i32, dur);
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        clif_skill_nodamage(c, Some(&s), &s, id, skill_lv as i32, true);
    }
}
