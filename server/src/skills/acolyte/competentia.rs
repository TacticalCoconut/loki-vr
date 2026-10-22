//! skills/acolyte/competentia.cpp: class SkillCompetentia : public SkillImpl (CD_COMPETENTIA).

use crate::battle::BCT_PARTY;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::party_foreachsamemap_area_sub;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillCompetentia;

impl SkillImpl for SkillCompetentia {
    fn skill_id(&self) -> u16 {
        CD_COMPETENTIA
    }

    /// SkillCompetentia::castendNoDamageId: 20% of max HP and SP per level (each shown as a Heal / SP Recovery
    /// packet) and the status, on the target alone or on every party member in range.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let sd = if src.kind == BL_PC { crate::pc::character_of(c, src.id) } else { None };
        match sd {
            Some(ch) if ch.party_id != 0 && *flag & 1 == 0 => {
                let f = *flag | BCT_PARTY | 1;
                party_foreachsamemap_area_sub(c, src, skill_get_splash(id, skill_lv) as i16, src, id, skill_lv, tick, f, skill_castend_nodamage_id);
            }
            _ => {
                let tstatus = crate::status::status_get_status_data(c, target);
                // (uint32 arithmetic, stored in int32)
                let hp_amount = (tstatus.max_hp.wrapping_mul(20 * skill_lv as u32) / 100) as i32;
                let sp_amount = (tstatus.max_sp.wrapping_mul(20 * skill_lv as u32) / 100) as i32;
                clif_skill_nodamage(c, None, target, AL_HEAL, hp_amount, true);
                crate::status::hp::status_heal(c, target.id, hp_amount as i64, 0, 0, 0);
                let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                clif_skill_nodamage(c, None, &t, MG_SRECOVERY, sp_amount, true);
                crate::status::hp::status_heal(c, target.id, 0, sp_amount as i64, 0, 0);
                let t = unit::get(c, target.id).unwrap_or(t);
                let ok = sc::sc_start(c, Some(src), &t, skill_get_sc(id) as i32, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
                let t = unit::get(c, target.id).unwrap_or(t);
                clif_skill_nodamage(c, Some(&t), &t, id, skill_lv as i32, ok);
            }
        }
    }
}
