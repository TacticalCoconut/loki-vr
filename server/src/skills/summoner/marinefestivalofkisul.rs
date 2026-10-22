//! skills/summoner/marinefestivalofkisul.cpp: class SkillMarineFestivalofKisul : public SkillImpl
//! (SH_MARINE_FESTIVAL_OF_KI_SUL).

use super::{communes, solo_branch};
use crate::battle::BCT_PARTY;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::party_foreachsamemap_area_sub;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillMarineFestivalofKisul;

impl SkillImpl for SkillMarineFestivalofKisul {
    fn skill_id(&self) -> u16 {
        SH_MARINE_FESTIVAL_OF_KI_SUL
    }

    /// SkillMarineFestivalofKisul::castendNoDamageId: see [`festival_castend_nodamage_id`].
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        festival_castend_nodamage_id(c, self.skill_id(), src, target, skill_lv, tick, flag);
    }
}

/// The body SkillMarineFestivalofKisul::castendNoDamageId and SkillSandyFestivalofKisul::castendNoDamageId share: the
/// status on the target (twice as long with the Ki Sul communion) and its packet, or the party members within the
/// splash range (2 cells wider with the communion).
pub(crate) fn festival_castend_nodamage_id(c: &Ctx, id: u16, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
    let type_ = skill_get_sc(id) as i32;
    if solo_branch(c, src, *flag) {
        let mut time = skill_get_time(id, skill_lv);
        if communes(c, src, SH_COMMUNE_WITH_KI_SUL) {
            time *= 2;
        }
        sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, time as i64);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, true);
    } else {
        let mut range = skill_get_splash(id, skill_lv);
        if communes(c, src, SH_COMMUNE_WITH_KI_SUL) {
            range += 2;
        }
        let f = *flag | BCT_PARTY | 1;
        party_foreachsamemap_area_sub(c, src, range as i16, src, id, skill_lv, tick, f, skill_castend_nodamage_id);
    }
}
