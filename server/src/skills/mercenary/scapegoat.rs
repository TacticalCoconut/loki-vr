//! skills/mercenary/mercenary_scapegoat.cpp: class SkillMercenaryScapegoat : public SkillImpl (MER_SCAPEGOAT).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::status::hp::{status_damage, status_heal};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMercenaryScapegoat;

impl SkillImpl for SkillMercenaryScapegoat {
    fn skill_id(&self) -> u16 {
        MER_SCAPEGOAT
    }

    /// SkillMercenaryScapegoat::castendNoDamageId: the master is healed by the mercenary's HP (heal effect), then
    /// the mercenary takes its max HP as damage (`status_damage(src, src, max_hp, 0, 0, 1, skill_id)`: dhp, dsp,
    /// walkdelay 0, flag 1).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, _skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        // s_mercenary_data* mer = BL_CAST(BL_MER, src); if( mer && mer->master )
        let Some(mer) = super::mer_data(c, src) else { return };
        let Some(master) = super::mer_master(c, src) else { return };
        status_heal(c, master.id, mer.battle_status.hp as i64, 0, 0, 2);
        status_damage(c, Some(src), src.id, mer.battle_status.max_hp as i64, 0, 0, 0, 1, self.skill_id());
    }
}
