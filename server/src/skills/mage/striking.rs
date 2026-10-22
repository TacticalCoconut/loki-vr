//! skills/mage/striking.cpp: class SkillStriking : public SkillImpl (SO_STRIKING).

use crate::battle::target::battle_check_target;
use crate::battle::{BCT_PARTY, BCT_SELF};
use crate::db::items::IT_WEAPON;
use crate::db::skills::*;
use crate::inventory::EQI_HAND_R;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_fail, clif_skill_nodamage, fail::USESKILL_FAIL_TOTARGET};
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillStriking;

impl SkillImpl for SkillStriking {
    fn skill_id(&self) -> u16 {
        SO_STRIKING
    }

    /// SkillStriking::castendNoDamageId: on the caster or a party member, the skill's status (val1 = lv, val2 = the
    /// ATK bonus `20 * lv * weapon level` of a player target's right-hand weapon) and the packet with its result;
    /// any other target: USESKILL_FAIL_TOTARGET to a player caster.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;

        if battle_check_target(c, src, target, BCT_SELF | BCT_PARTY) > 0 {
            let mut bonus = 0;

            // map_session_data* dstsd = BL_CAST(BL_PC, target);
            if let Some(dstsd) = crate::battle::bl::sd(c, target) {
                // index = dstsd->equip_index[EQI_HAND_R]; index >= 0 && dstsd->inventory_data[index] && type == IT_WEAPON
                if let Some((_, data)) = crate::battle::bl::equip(c, &dstsd, EQI_HAND_R) {
                    if data.r#type == IT_WEAPON {
                        bonus = (20 * skill_lv as i32) * data.weapon_level as i32;
                    }
                }
            }

            // the status first (argument evaluation), then the packet with its result
            let ok = sc::sc_start2(c, Some(src), target, type_, 100, skill_lv as i32, bonus, skill_get_time(id, skill_lv) as i64);
            let target = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(src), &target, id, skill_lv as i32, ok);
        } else if src.kind == BL_PC {
            clif_skill_fail(c, src, id, USESKILL_FAIL_TOTARGET, 0, 0);
        }
    }
}
