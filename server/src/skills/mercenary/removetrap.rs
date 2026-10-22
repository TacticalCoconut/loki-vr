//! skills/mercenary/mercenary_removetrap.cpp: class SkillMercenaryRemoveTrap : public SkillImpl (MA_REMOVETRAP). Its
//! range is checked from the master's position (unit.cpp unit_skilluse_id2: skill/unit.rs).

use crate::db::skills::*;
use crate::map::BL_SKILL;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::iface;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillMercenaryRemoveTrap;

impl SkillImpl for SkillMercenaryRemoveTrap {
    fn skill_id(&self) -> u16 {
        MA_REMOVETRAP
    }

    /// SkillMercenaryRemoveTrap::castendNoDamageId: mercenaries can remove any trap (any skill unit whose group's
    /// skill has INF2_ISTRAP; no trap item is given back).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        // skill_unit* su = BL_CAST(BL_SKILL, target); su && (sg = su->group) && (skill_group = skill_db.find(sg->skill_id))
        if target.kind != BL_SKILL {
            return;
        }
        let Some(sg) = crate::skillunit::group_of_unit(c, target.id) else { return };
        let Some(skill_group) = skill_db().find(sg.skill_id) else { return };
        if skill_group.has_inf2(INF2_ISTRAP) {
            clif_skill_nodamage(c, Some(src), target, self.skill_id(), skill_lv as i32, true);
            iface::skill_delunit(c, target.id);
        }
    }
}
