//! skills/acolyte/heal.cpp: class SkillHeal : public SkillImpl (AL_HEAL).

use crate::battle::BF_MAGIC;
use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::attack::skill_attack;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::misc::skill_calc_heal;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_AKAITSUKI, SC_BERSERK, SC_BITESCAR, SC_KAITE, SC_SATURDAYNIGHTFEVER};
use crate::status::defs::{CLASS_BATTLEFIELD, MD_STATUSIMMUNE};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

use super::conf::heal_exp;

pub struct SkillHeal;

impl SkillImpl for SkillHeal {
    fn skill_id(&self) -> u16 {
        AL_HEAL
    }

    /// SkillHeal::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, bl: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        heal_castend_nodamage_id(c, self.skill_id(), src, bl, skill_lv, false);
    }

    /// SkillHeal::castendDamageId: the offensive heal on an undead target (skill_castend_nodamage_id redirects it).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        skill_attack(c, BF_MAGIC, src, src, target, self.skill_id(), skill_lv, tick, *flag);
    }
}

/// The body of SkillHeal::castendNoDamageId (heal.cpp), which highnessheal.cpp's SkillHighnessHeal repeats with two
/// differences (`highness`): the heal amount (skill_calc_heal), void on immune targets / the Emperium / battlefield
/// monsters, bounced back by Kaite, voided by Berserk / Saturday Night Fever, Bite Scar ended (Heal only), reversed
/// by Akaitsuki, and the healer's job exp.
///
/// `highness`: SkillHighnessHeal starts with `dstsd = BL_CAST(BL_PC, target)` and has no `status_change_end(bl,
/// SC_BITESCAR)`; SkillHeal starts with `dstsd = nullptr` (so in Heal `dstsd` is only ever set, to the caster, when
/// Kaite bounces the heal, and the `sd != dstsd` heal-exp branch can never run: kept). The Akaitsuki test reads the
/// original target's status even after the bounce.
pub(crate) fn heal_castend_nodamage_id(c: &Ctx, skill_id: u16, src: &Unit, bl: &Unit, skill_lv: u16, highness: bool) {
    let is_sd = src.kind == BL_PC;
    let tsc_bl = bl.id; // status_change *tsc = status_get_sc(bl) (the original target's)
    let has_tsc = crate::battle::mobif::has_sc(bl.kind);
    let mut dstsd: Option<u32> = if highness && bl.kind == BL_PC { Some(bl.id) } else { None };
    let sstatus = crate::status::status_get_status_data(c, src);
    let dstmd = bl.kind == BL_MOB;

    let mut heal = skill_calc_heal(c, src, Some(bl), skill_id, skill_lv, true);

    if sc::status_isimmune(c, bl) != 0
        || (dstmd && (crate::battle::bl::class_of(c, bl) == crate::skill::castend::MOBID_EMPERIUM || crate::battle::bl::class_type(c, bl) == CLASS_BATTLEFIELD))
    {
        heal = 0;
    }

    let mut target = bl.clone();
    if has_tsc && !sc::load(c, tsc_bl).empty() {
        if let Some(mut kaite) = sc::get_sce(c, tsc_bl, SC_KAITE).filter(|_| !crate::battle::mobif::has_mode(sstatus.mode, MD_STATUSIMMUNE)) {
            // Bounce back heal
            kaite.val2 -= 1;
            sc::put_sce(c, &kaite);
            if kaite.val2 <= 0 {
                sc::status_change_end(c, tsc_bl, SC_KAITE, 0);
            }
            if src.id == bl.id {
                heal = 0; // When you try to heal yourself under Kaite, the heal is voided.
            } else {
                target = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                dstsd = is_sd.then_some(src.id);
            }
        } else if sc::has_sce(c, tsc_bl, SC_BERSERK) || sc::has_sce(c, tsc_bl, SC_SATURDAYNIGHTFEVER) {
            heal = 0; // Needed so that it actually displays 0 when healing.
        }
    }

    if !highness {
        sc::status_change_end(c, target.id, SC_BITESCAR, 0);
    }
    let t = unit::get(c, target.id).unwrap_or(target);
    let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
    clif_skill_nodamage(c, Some(&s), &t, skill_id, heal, true);
    if has_tsc && sc::has_sce(c, tsc_bl, SC_AKAITSUKI) && heal != 0 {
        heal = (!heal).wrapping_add(1);
    }
    let mut heal_get_jobexp = crate::status::hp::status_heal(c, t.id, heal as i64, 0, 0, 0) as i64;

    if is_sd && dstsd.is_some() && heal > 0 && Some(src.id) != dstsd && heal_exp() > 0 {
        heal_get_jobexp = heal_get_jobexp * heal_exp() as i64 / 100;
        if heal_get_jobexp <= 0 {
            heal_get_jobexp = 1;
        }
        let t = unit::get(c, t.id).unwrap_or(t);
        crate::status::progress::pc_gainexp(c, src.id, Some(&t), 0, heal_get_jobexp as u64, 0);
    }
}
