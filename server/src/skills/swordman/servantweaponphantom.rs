//! skills/swordman/servantweaponphantom.cpp: class SkillServantWeaponPhantom : public SkillImplRecursiveDamageSplash
//! (DK_SERVANT_W_PHANTOM).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::attack::{skill_blown, BLOWN_NONE};
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::misc::skill_check_unit_movepos;
use crate::skills::novice::{re_lvl_dmod, sce};
use crate::skills::{splash_search_default, RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_HANDICAPSTATE_DEEPBLIND, SC_SERVANT_SIGN};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillServantWeaponPhantom;

impl SkillImpl for SkillServantWeaponPhantom {
    fn skill_id(&self) -> u16 {
        DK_SERVANT_W_PHANTOM
    }

    crate::splash_skill_defaults!();

    /// SkillServantWeaponPhantom::modifyDamageData: at most one hit per servant (sd->servantball +
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

    /// SkillServantWeaponPhantom::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        *skillratio += -100 + 200 + 300 * skill_lv as i32 + 5 * sstatus.pow as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillServantWeaponPhantom::applyAdditionalEffects.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        sc::sc_start(c, Some(src), target, SC_HANDICAPSTATE_DEEPBLIND, 30 + 10 * skill_lv as i32, skill_lv as i32, skill_get_time(self.skill_id(), skill_lv) as i64);
    }
}

impl RecursiveDamageSplash for SkillServantWeaponPhantom {
    /// SkillServantWeaponPhantom::splashSearch: the caster jumps to the target, the servants' animation plays; no
    /// damage without the caster's Servant Sign on the target.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        let id = self.skill_id();
        // Jump to the target before attacking.
        if skill_check_unit_movepos(c, 5, src, target.x, target.y, 0, true) {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let dir = (crate::map::calc_dir(target, s.x, s.y) + 4) % 8;
            skill_blown(c, &s, &s, 1, dir as i8, BLOWN_NONE);
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        clif_skill_nodamage(c, Some(&s), target, id, skill_lv as i32, true); // Trigger animation on servants.
        crate::battle::clif::clif_blown(c, &s);
        // Deal no damage if no Servant Sign on Enemy
        if !sce(c, target, SC_SERVANT_SIGN).map(|e| e.val1 == s.id as i32).unwrap_or(false) {
            return;
        }
        splash_search_default(self, c, &s, target, skill_lv, tick, flag);
    }
}
