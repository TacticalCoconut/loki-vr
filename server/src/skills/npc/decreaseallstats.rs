//! skills/npc/decreaseallstats.cpp: class SkillDecreaseAllStats : public SkillImpl (NPC_ALL_STAT_DOWN), rAthena
//! e985006.

use crate::battle::DMG_SINGLE;
use crate::db::skills::*;
use crate::skill::clif::{clif_skill_damage, clif_skill_nodamage, DMGVAL_IGNORE};
use crate::skills::novice::status_get_amotion;
use crate::skills::SkillImpl;
use crate::status::defs::{SCSTART_NOAVOID, SCSTART_NORATEDEF, SCSTART_NOTICKDEF};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillDecreaseAllStats;

impl SkillImpl for SkillDecreaseAllStats {
    fn skill_id(&self) -> u16 {
        NPC_ALL_STAT_DOWN
    }

    /// SkillDecreaseAllStats::castendNoDamageId: the skill's status at 100% that no resistance changes, the skill
    /// packet and a dummy damage packet.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        sc::status_change_start(c, Some(src), target, skill_get_sc(id) as i32, 10000, skill_lv as i32, 0, 0, 0, skill_get_time(id, skill_lv) as i64, SCSTART_NOAVOID | SCSTART_NOTICKDEF | SCSTART_NORATEDEF, 0);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, true);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        clif_skill_damage(c, &s, &t, tick, status_get_amotion(c, &s), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
    }
}
