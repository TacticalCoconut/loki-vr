//! skills/archer/sensitivekeen.cpp: class SkillSensitiveKeen : public WeaponSkillImpl (RA_SENSITIVEKEEN).

use crate::battle::calc::Damage;
use crate::battle::{BCT_ENEMY, DMG_SINGLE};
use crate::db::skills::*;
use crate::inventory::Item;
use crate::skill::area::{foreach_in_range, skill_area_sub, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::{clif_skill_damage, clif_skill_nodamage, DMGVAL_IGNORE};
use crate::skill::SD_ANIMATION;
use crate::skills::novice::{pc_checkskill, sce, status_get_amotion};
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::skillunit::consts::{UNT_ANKLESNARE, UNT_USED_TRAPS};
use crate::skillunit::data;
use crate::status::consts::{SC_CAMOUFLAGE, SC_CLOAKINGEXCEED, SC_STEALTHFIELD, SC__SHADOWFORM};
use crate::status::defs::{OPTION_CHASEWALK, OPTION_CLOAK, OPTION_HIDE};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSensitiveKeen;

impl SkillImpl for SkillSensitiveKeen {
    fn skill_id(&self) -> u16 {
        RA_SENSITIVEKEEN
    }

    /// SkillSensitiveKeen::calculateSkillRatio: `base_skillratio += 50 * skill_lv`.
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 50 * skill_lv as i32;
    }

    /// SkillSensitiveKeen::castendDamageId: "Only Hits Invisible Targets" (hidden, cloaked, chase-walking,
    /// camouflaged or in a Stealth Field: their Cloaking Exceed ends, then the weapon hit); a Shadow Form ends with a
    /// (100 - 10 * its level)% chance; a trap unit drops its trap item (unless spent) and goes.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let tsc = crate::battle::mobif::has_sc(target.kind);
        if target.kind != crate::map::BL_SKILL {
            // Only Hits Invisible Targets
            if tsc && (target.sc.option & (OPTION_HIDE | OPTION_CLOAK | OPTION_CHASEWALK) != 0 || sce(c, target, SC_CAMOUFLAGE).is_some() || sce(c, target, SC_STEALTHFIELD).is_some()) {
                sc::status_change_end(c, target.id, SC_CLOAKINGEXCEED, 0);
                weapon_castend_damage_id(c, id, src, &super::fresh(c, target), skill_lv, tick, *flag);
            }
            if let Some(sf) = sce(c, target, SC__SHADOWFORM) {
                if ((c.rnd() % 100) as i32) < 100 - sf.val1 * 10 {
                    // [100 - (Skill Level x 10)] %
                    sc::status_change_end(c, target.id, SC__SHADOWFORM, 0); // Should only end, no damage dealt.
                }
            }
        } else {
            let su = data::get_unit(c, target.id);
            let sg = su.as_ref().and_then(|su| data::skill_id2group(c, su.group_id));
            if let (Some(su), Some(sg)) = (su, sg) {
                if skill_get_inf2(sg.skill_id, &[INF2_ISTRAP]) {
                    if !(sg.unit_id == UNT_USED_TRAPS || (sg.unit_id == UNT_ANKLESNARE && sg.val2 != 0)) {
                        let mut item_tmp = Item::new(if sg.item_id != 0 { sg.item_id } else { super::removetrap::ITEMID_TRAP });
                        item_tmp.identify = 1;
                        if item_tmp.nameid != 0 {
                            crate::flooritem::map_addflooritem(c, &item_tmp, 1, target.map, target.x, target.y, 0, 0, 0, 4, 0, false, None, 0);
                        }
                    }
                    crate::skill::iface::skill_delunit(c, su.id());
                }
            }
        }
    }

    /// SkillSensitiveKeen::castendNoDamageId: the no-damage and the caster's damage packets, then
    /// skill_castend_damage_id on every character and skill unit around the caster.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        clif_skill_damage(c, src, src, tick, status_get_amotion(c, src), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
        let f = *flag | BCT_ENEMY;
        foreach_in_range(c, src, skill_get_splash(id, skill_lv) as i16, crate::map::BL_CHAR | crate::map::BL_SKILL, Wall::Config, |c, bl| skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id));
    }

    /// SkillSensitiveKeen::applyAdditionalEffects: an (8 * lv)% chance of a Warg Bite (the player's level, the
    /// maximum for others) with SD_ANIMATION.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        if ((c.rnd() % 100) as i32) < 8 * skill_lv as i32 {
            let lv = if src.kind == crate::map::BL_PC { pc_checkskill(c, src, RA_WUGBITE) } else { skill_get_max(RA_WUGBITE) };
            skill_castend_damage_id(c, src, target, RA_WUGBITE, lv as u16, tick, SD_ANIMATION);
        }
    }
}
