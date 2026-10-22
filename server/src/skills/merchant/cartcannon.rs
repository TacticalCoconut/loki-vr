//! skills/merchant/cartcannon.cpp: class SkillCartCannon : public SkillImplRecursiveDamageSplash (GN_CARTCANNON).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{pc_checkskill, re_lvl_dmod};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::SC_BIONIC_WOODENWARRIOR;
use crate::status::status_get_status_data;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillCartCannon::SkillCartCannon() : SkillImplRecursiveDamageSplash(GN_CARTCANNON).
pub struct SkillCartCannon;

impl SkillImpl for SkillCartCannon {
    fn skill_id(&self) -> u16 {
        GN_CARTCANNON
    }
    crate::splash_skill_defaults!();

    /// modifyDamageData: `dmg.div_ = 2` while the caster has SC_BIONIC_WOODENWARRIOR.
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, _target: &Unit, _skill_lv: u16) {
        if crate::skills::novice::sce(c, src, SC_BIONIC_WOODENWARRIOR).is_some() {
            dmg.div_ = 2;
        }
    }

    /// SkillCartCannon::calculateSkillRatio: (250% + 20% per Cart Remodeling level) per level + 2 x INT / (6 - Cart
    /// Remodeling), RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = status_get_status_data(c, src);
        let rc = pc_checkskill(c, src, GN_REMODELING_CART);
        *skillratio += -100 + (250 + 20 * rc) * skill_lv as i32 + 2 * sstatus.int_ as i32 / (6 - rc);
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// modifyHitRate: +4 per level of GN_REMODELING_CART (players).
    fn modify_hit_rate(&self, c: &Ctx, hit_rate: &mut i16, src: &Unit, _target: &Unit, _skill_lv: u16) {
        let lv = pc_checkskill(c, src, GN_REMODELING_CART);
        if src.kind == BL_PC && lv != 0 {
            *hit_rate = (*hit_rate as i32 + lv * 4) as i16;
        }
    }

    /// modifyElement: a player's arrow element while it attacks with arrows (sd->state.arrow_atk).
    fn modify_element(&self, c: &Ctx, _dmg: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, element: &mut i32, _flag: i32) {
        if src.kind == BL_PC && crate::battle::state::get(c, src.id).arrow_atk {
            if let Some(sd) = crate::status::data::sd_get(c, src.id) {
                *element = sd.bonus.arrow_ele;
            }
        }
    }
}

impl RecursiveDamageSplash for SkillCartCannon {
    /// SkillCartCannon::splashSearch: the skill packet on the target, then the parent's search.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
        crate::skills::splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }
}
