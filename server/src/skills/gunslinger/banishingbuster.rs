//! skills/gunslinger/banishingbuster.cpp: class SkillBanishingBuster : public WeaponSkillImpl (RL_BANISHING_BUSTER),
//! rAthena e985006.

use crate::battle::calc::Damage;
use crate::db::jobs::ids::{MAPID_SECONDMASK, MAPID_SOUL_LINKER};
use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::clif::skill_fail;
use crate::skills::novice::re_lvl_dmod;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::status::consts::*;
use crate::status::defs::BSF_REM_ON_BANISHING_BUSTER;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// conf/battle/skill.conf `dispel_song: no` (battle_config.dispel_song).
fn dispel_song() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::DISPEL_SONG) != 0
}

pub struct SkillBanishingBuster;

impl SkillImpl for SkillBanishingBuster {
    fn skill_id(&self) -> u16 {
        RL_BANISHING_BUSTER
    }

    /// WeaponSkillImpl::castendDamageId (not overridden).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillBanishingBuster::calculateSkillRatio: 1000 + 200% per level, RE_LVL_DMOD(100).
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        *skillratio += -100 + 1000 + 200 * skill_lv as i32;
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillBanishingBuster::applyAdditionalEffects: on a target with statuses that is not immune, unless it is a Soul
    /// Linker, with 50 + 5% per level: up to `level` statuses end, in status_db's iteration order, skipping those
    /// flagged NoBanishingBuster, the songs (dispel_song off, or a song still in its area: val4) and Assumptio on a
    /// monster; Berserk / Saturday Night Fever get val2 = 0 first. A player target also loses its
    /// BSF_REM_ON_BANISHING_BUSTER bonus scripts. A failure is a player's skill_fail.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        if !crate::battle::mobif::has_sc(target.kind) || sc::load(c, target.id).empty() {
            return;
        }
        if sc::status_isimmune(c, target) != 0 {
            return;
        }
        let soul_linker = target.kind == BL_PC && crate::status::data::sd_get(c, target.id).map(|st| st.class_ & MAPID_SECONDMASK == MAPID_SOUL_LINKER).unwrap_or(false);
        if soul_linker || ((c.rnd() % 100) as i32) >= 50 + 5 * skill_lv as i32 {
            if src.kind == BL_PC {
                skill_fail(c, src, self.skill_id());
            }
            return;
        }
        let mut n = skill_lv as i32;
        for it in crate::db::misc::status::status_db().iter() {
            let status = it.type_;
            if n <= 0 {
                break;
            }
            let Some(mut sce) = sc::get_sce(c, target.id, status) else { continue };
            if it.has_flag(SCF_NOBANISHINGBUSTER) {
                continue;
            }
            match status {
                SC_WHISTLE | SC_ASSNCROS | SC_POEMBRAGI | SC_APPLEIDUN | SC_HUMMING | SC_DONTFORGETME | SC_FORTUNE | SC_SERVICE4U => {
                    if !dispel_song() || sce.val4 == 0 {
                        // If in song area don't end it, even if config enabled
                        continue;
                    }
                }
                SC_ASSUMPTIO => {
                    if target.kind == BL_MOB {
                        continue;
                    }
                }
                _ => {}
            }
            if status == SC_BERSERK || status == SC_SATURDAYNIGHTFEVER {
                sce.val2 = 0;
                sc::put_sce(c, &sce);
            }
            sc::status_change_end(c, target.id, status, 0);
            n -= 1;
        }
        if target.kind == BL_PC {
            // Remove bonus_script by Banishing Buster
            crate::status::bonus::pc_bonus_script_clear(c, target.id, BSF_REM_ON_BANISHING_BUSTER);
        }
    }
}
