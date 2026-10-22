//! skills/merchant/upgradeweapon.cpp: class SkillUpgradeWeapon : public SkillImpl (WS_WEAPONREFINE). The list and the
//! refine are itemui's (itemui/refine.rs: clif_item_refine_list, clif_parse_WeaponRefine, skill_weaponrefine).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillUpgradeWeapon::SkillUpgradeWeapon() : SkillImpl(WS_WEAPONREFINE).
pub struct SkillUpgradeWeapon;

impl SkillImpl for SkillUpgradeWeapon {
    fn skill_id(&self) -> u16 {
        WS_WEAPONREFINE
    }

    /// SkillUpgradeWeapon::castendNoDamageId: a player caster gets the list of the weapons it can refine.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, _skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        // map_session_data* sd = BL_CAST(BL_PC, src)
        if src.kind == BL_PC {
            crate::itemui::refine::clif_item_refine_list(c, src.id);
        }
    }
}
