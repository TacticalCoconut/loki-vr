//! skills/merchant/greed.cpp: class SkillGreed : public SkillImpl (BS_GREED), and skill.cpp skill_greed, the
//! map_foreachinallrange callback only BS_GREED uses.

use crate::db::skills::*;
use crate::map::{BL_ITEM, BL_PC};
use crate::skill::area::{foreach_in_range, Wall};
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillGreed::SkillGreed() : SkillImpl(BS_GREED).
pub struct SkillGreed;

impl SkillImpl for SkillGreed {
    fn skill_id(&self) -> u16 {
        BS_GREED
    }

    /// SkillGreed::castendNoDamageId: a player caster shows the skill, then picks up every floor item within the
    /// splash range of the target (map_foreachinallrange: no wall check), the target being the picker.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        // map_session_data* sd = BL_CAST(BL_PC, src)
        if src.kind == BL_PC {
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_ITEM, Wall::None, |c, bl| skill_greed(c, bl, target));
        }
    }
}

/// skill.cpp skill_greed (`map_foreachinallrange(skill_greed, target, range, BL_ITEM, target)`): `src` (the va_arg,
/// the skill's target) takes the floor item `bl` (pc_takeitem) when it is a player and `bl` a floor item.
pub fn skill_greed(c: &Ctx, bl: &Unit, src: &Unit) -> i32 {
    if src.kind == BL_PC && bl.kind == BL_ITEM {
        crate::flooritem::pc_takeitem(c, src.id, bl.id);
    }
    0
}
