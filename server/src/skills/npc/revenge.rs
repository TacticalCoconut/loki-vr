//! skills/npc/revenge.cpp: class SkillRevenge : public SkillImpl (NPC_REVENGE).

use crate::db::skills::*;
use crate::map::BL_MOB;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillRevenge;

impl SkillImpl for SkillRevenge {
    fn skill_id(&self) -> u16 {
        NPC_REVENGE
    }

    /// SkillRevenge::castendNoDamageId: a slave takes as target (provoked) a random unit that targets its master
    /// (battle_gettargeted).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, _skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        // mob_data* md = BL_CAST(BL_MOB, src); status_data* sstatus = status_get_status_data(*src)
        if src.kind != BL_MOB {
            return;
        }
        let Some(mut md) = crate::mob::get(c, src.id) else { return };
        // not really needed... but adding here anyway ^^
        if md.master_id > 0 {
            // (mbl = map_id2bl(md->master_id)) == nullptr || (tbl = battle_gettargeted(mbl)) == nullptr
            let Some(mbl) = unit::get(c, md.master_id) else { return };
            let Some(tbl) = crate::battle::target::battle_gettargeted(c, &mbl) else { return };
            md.provoke_flag = tbl.id;
            crate::mob::put(c, &md);
            let range = crate::status::status_get_status_data(c, src).rhw.range as i32;
            crate::mob::ai::mob_target(c, md.id, &tbl, range);
        }
    }
}
