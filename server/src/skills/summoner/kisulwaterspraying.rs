//! skills/summoner/kisulwaterspraying.cpp: class SkillKisulWaterSpraying : public SkillImpl (SH_KI_SUL_WATER_SPRAYING).

use super::{communes, solo_branch};
use crate::battle::BCT_PARTY;
use crate::db::skills::*;
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{party_foreachsamemap_area_sub, pc_checkskill};
use crate::skills::SkillImpl;
use crate::status::hp::status_heal;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillKisulWaterSpraying;

impl SkillImpl for SkillKisulWaterSpraying {
    fn skill_id(&self) -> u16 {
        SH_KI_SUL_WATER_SPRAYING
    }

    /// SkillKisulWaterSpraying::castendNoDamageId: the heal on the target (500 * level + INT * 5, the mastery and the
    /// Ki Sul communion bonuses, times (100 + CRT) * base level / 10000; the heal packet as AL_HEAL), or the packet and
    /// the party members within the splash range (2 cells wider with the communion).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let lv = skill_lv as i32;
        if solo_branch(c, src, *flag) {
            // TODO: verify on official server, if this should be moved into skill_calc_heal
            let sstatus = crate::status::status_get_status_data(c, src);
            let mut heal: i32 = 500 * lv + sstatus.int_ as i32 * 5;
            heal += pc_checkskill(c, src, SH_MYSTICAL_CREATURE_MASTERY) * 100;
            if communes(c, src, SH_COMMUNE_WITH_KI_SUL) {
                heal += 250 * lv;
                heal += pc_checkskill(c, src, SH_MYSTICAL_CREATURE_MASTERY) * 50;
            }
            // heal * (100 + status_get_crt(src)) * status_get_lv(src) / 10000 in int32
            heal = heal.wrapping_mul(100 + sstatus.crt as i32).wrapping_mul(crate::status::status_get_lv(c, src)) / 10000;
            status_heal(c, target.id, heal as i64, 0, 0, 0);
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, None, &t, AL_HEAL, heal, true);
        } else {
            clif_skill_nodamage(c, Some(src), target, id, lv, true);
            let mut range = skill_get_splash(id, skill_lv);
            if communes(c, src, SH_COMMUNE_WITH_KI_SUL) {
                range += 2;
            }
            let f = *flag | BCT_PARTY | 1;
            party_foreachsamemap_area_sub(c, src, range as i16, src, id, skill_lv, tick, f, skill_castend_nodamage_id);
        }
    }
}
