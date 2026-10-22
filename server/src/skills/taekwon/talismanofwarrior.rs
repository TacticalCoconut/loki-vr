//! skills/taekwon/talismanofwarrior.cpp: class SkillTalismanOfWarrior : public SkillImpl (SOA_TALISMAN_OF_WARRIOR).

use crate::db::items::IT_WEAPON;
use crate::db::skills::*;
use crate::inventory::EQI_HAND_R;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_fail, clif_skill_nodamage, fail::USESKILL_FAIL_NEED_WEAPON};
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillTalismanOfWarrior;

impl SkillImpl for SkillTalismanOfWarrior {
    fn skill_id(&self) -> u16 {
        SOA_TALISMAN_OF_WARRIOR
    }

    /// SkillTalismanOfWarrior::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        // map_session_data* dstsd = BL_CAST(BL_PC, target)
        if target.kind == BL_PC {
            // int16 index = dstsd->equip_index[EQI_HAND_R]
            let index = crate::inventory::st(c, target.id).and_then(|s| s.equip_index.get(EQI_HAND_R).copied()).unwrap_or(-1);
            // dstsd->inventory_data[index] != nullptr && dstsd->inventory_data[index]->type == IT_WEAPON
            if index >= 0 && crate::inventory::inv_get(c, target.id, index as usize).map(|it| crate::inventory::data_of(&it.item).r#type == IT_WEAPON).unwrap_or(false) {
                let ok = sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
                let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, ok);
                return;
            }
        }
        // map_session_data* sd = BL_CAST(BL_PC, src)
        if src.kind == BL_PC {
            clif_skill_fail(c, src, id, USESKILL_FAIL_NEED_WEAPON, 0, 0);
        }
    }
}
