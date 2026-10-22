//! skills/acolyte/warpportal.cpp: class SkillWarpPortal : public SkillImpl (AL_WARP).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::pc_misc::MAX_MEMOPOINTS;
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::SC_CURSEDCIRCLE_ATKER;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillWarpPortal;

impl SkillImpl for SkillWarpPortal {
    fn skill_id(&self) -> u16 {
        AL_WARP
    }

    /// SkillWarpPortal::castendPos2: the destination menu (save point, then one memo point per level above 1); the
    /// portal itself is made by skill_castend_map once a destination is chosen.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, _x: i16, _y: i16, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if src.kind == BL_PC {
            if let Some(ch) = crate::pc::character_of(c, src.id) {
                // std::vector<std::string> maps( MAX_MEMOPOINTS + 1 ): four empty names first (clif_skill_warppoint
                // skips them), then the push_backs
                let mut maps: Vec<String> = vec![String::new(); MAX_MEMOPOINTS + 1];
                maps.push(ch.save_map.clone());
                let memo = crate::pc_misc::memo_points(c, ch.id);
                if skill_lv >= 2 {
                    maps.push(memo[0].map.clone());
                    if skill_lv >= 3 {
                        maps.push(memo[1].map.clone());
                        if skill_lv >= 4 {
                            maps.push(memo[2].map.clone());
                        }
                    }
                }
                let sd = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                crate::skillunit::clif::clif_skill_warppoint(c, &sd, id, skill_lv, &maps);
            }
        }
        if sce(c, src, SC_CURSEDCIRCLE_ATKER).is_some() {
            // Should only remove after the skill has been casted.
            sc::status_change_end(c, src.id, SC_CURSEDCIRCLE_ATKER, 0);
        }
        // not to consume item.
        *flag |= SKILL_NOCONSUME_REQ;
    }
}
