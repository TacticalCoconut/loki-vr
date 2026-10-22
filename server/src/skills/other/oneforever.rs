//! skills/other/oneforever.cpp: class SkillOneForever : public SkillImpl (WE_ONEFOREVER).

use super::{pc_get_child, pc_get_partner};
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::npc::{mapflag, MF_BATTLEGROUND, MF_PVP};
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::SC_HELLPOWER;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillOneForever;

impl SkillImpl for SkillOneForever {
    fn skill_id(&self) -> u16 {
        WE_ONEFOREVER
    }

    /// SkillOneForever::castendNoDamageId (a player caster only): fails (requirements kept) with no partner or child
    /// online and a non-player target; fails on GvG and battleground maps ("No reviving in WoE grounds!"); a dead
    /// target that is the partner or the child is revived with 30 % HP (100 % HP / SP with restart_full_recover),
    /// unless it is undead, under Hell Power, or has a negative PvP score on a PvP map.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if src.kind != BL_PC {
            return;
        }
        let tstatus = crate::status::status_get_status_data(c, target);
        let dstsd = (target.kind == BL_PC).then_some(target.id);
        let p_sd = pc_get_partner(c, src).map(|u| u.id);
        let c_sd = pc_get_child(c, src).map(|u| u.id);
        if p_sd.is_none() && c_sd.is_none() && dstsd.is_none() {
            // Fail if no family members are found
            skill_fail(c, src, id);
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        if crate::status::map_flag_gvg2(c, target.map) || mapflag(c, target.map, MF_BATTLEGROUND) {
            // No reviving in WoE grounds!
            skill_fail(c, src, id);
            return;
        }
        if sc::status_isdead(c, target) {
            let (mut per, mut sper) = (30u8, 0u8);
            if sc::battle_check_undead(tstatus.race as i32, tstatus.def_ele as i32) {
                return;
            }
            if sce(c, target, SC_HELLPOWER).is_some() {
                return;
            }
            // dstsd->pvp_point / dstsd->special_state: rAthena dereferences dstsd here (a dead non-player target
            // would crash it); a non-player target stops here.
            let Some(did) = dstsd else { return };
            if mapflag(c, target.map, MF_PVP) && crate::battle::state::get(c, did).pvp_point < 0 {
                return;
            }
            if crate::status::data::sd_get(c, did).map(|s| s.special_state.restart_full_recover).unwrap_or(false) {
                per = 100;
                sper = 100;
            }
            // Only family members can be revived
            if (dstsd == p_sd || dstsd == c_sd) && sc_revive(c, target.id, per, sper) {
                let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, true);
            }
        }
    }
}

/// status.cpp status_revive(bl, per_hp, per_sp) != 0.
fn sc_revive(c: &Ctx, bl: u32, per_hp: u8, per_sp: u8) -> bool {
    crate::status::hp::status_revive(c, bl, per_hp, per_sp, 0) != 0
}
