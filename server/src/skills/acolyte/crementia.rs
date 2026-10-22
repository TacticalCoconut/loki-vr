//! skills/acolyte/crementia.cpp: class SkillCrementia : public SkillImpl (AB_CLEMENTIA).

use crate::battle::BCT_PARTY;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{party_foreachsamemap_area_sub, pc_checkskill};
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillCrementia;

impl SkillImpl for SkillCrementia {
    fn skill_id(&self) -> u16 {
        AB_CLEMENTIA
    }

    /// SkillCrementia::castendNoDamageId: Blessing at the caster's AL_BLESSING level (the maximum for non-players)
    /// + job level / 10 (50 / 10), on the target alone or every party member in range.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        let sd = if src.kind == BL_PC { crate::pc::character_of(c, src.id) } else { None };
        let bless_lv = match &sd {
            Some(ch) => pc_checkskill(c, src, AL_BLESSING) + ch.job_level as i32 / 10,
            None => skill_get_max(AL_BLESSING) + 50 / 10,
        };
        match sd {
            Some(ch) if ch.party_id != 0 && *flag & 1 == 0 => {
                let f = *flag | BCT_PARTY | 1;
                party_foreachsamemap_area_sub(c, src, skill_get_splash(id, skill_lv) as i16, src, id, skill_lv, tick, f, skill_castend_nodamage_id);
            }
            _ => {
                let ok = sc::sc_start(c, Some(src), target, type_, 100, bless_lv, skill_get_time(id, skill_lv) as i64);
                let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                clif_skill_nodamage(c, Some(&t), &t, id, skill_lv as i32, ok);
            }
        }
    }
}
