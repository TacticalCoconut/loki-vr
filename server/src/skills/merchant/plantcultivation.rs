//! skills/merchant/plantcultivation.cpp: class SkillPlantCultivation : public SkillImpl (CR_CULTIVATION).

use super::ids::*;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_PC};
use crate::mob::spawn::mob_once_spawn_sub;
use crate::skill::clif::{clif_skill_poseffect, skill_fail};
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::SkillImpl;
use crate::timer::{self, Func};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillPlantCultivation::SkillPlantCultivation() : SkillImpl(CR_CULTIVATION).
pub struct SkillPlantCultivation;

impl SkillImpl for SkillPlantCultivation {
    fn skill_id(&self) -> u16 {
        CR_CULTIVATION
    }

    /// SkillPlantCultivation::castendPos2 (players only): fails on an occupied cell (requirements kept), else the
    /// ground effect and a 50% roll: a mushroom (level 1) or a plant of the level-2 table, without a master, deleted
    /// after skill_get_time.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if src.kind != BL_PC {
            return;
        }
        if crate::map::count_oncell(c, src.map, x, y, BL_CHAR, 0) > 0 {
            skill_fail(c, src, id);
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        clif_skill_poseffect(c, src, id, skill_lv, x, y, tick);
        if c.rnd() % 100 < 50 {
            skill_fail(c, src, id);
        } else {
            let mob_id = if skill_lv == 1 {
                MOBID_BLACK_MUSHROOM + (c.rnd() % 2) as i32
            } else {
                let rand_val = c.rnd() % 100;
                if rand_val < 30 {
                    MOBID_GREEN_PLANT
                } else if rand_val < 55 {
                    MOBID_RED_PLANT
                } else if rand_val < 80 {
                    MOBID_YELLOW_PLANT
                } else if rand_val < 90 {
                    MOBID_WHITE_PLANT
                } else if rand_val < 98 {
                    MOBID_BLUE_PLANT
                } else {
                    MOBID_SHINING_PLANT
                }
            };
            let Some(mut md) = mob_once_spawn_sub(c, Some(src), src.map, x, y, Some("--ja--"), mob_id, Some(""), crate::status::defs::SZ_SMALL as u8, crate::mob::AI_NONE) else {
                return;
            };
            let t = skill_get_time(id, skill_lv);
            if t > 0 {
                if md.deletetimer != 0 {
                    timer::delete_checked(c, md.deletetimer, Func::MobTimerDelete);
                }
                md.deletetimer = timer::add(c, tick + t as Tick, Func::MobTimerDelete, md.id, 0);
                crate::mob::put(c, &md);
            }
            crate::mob::spawn::mob_spawn(c, md.id);
        }
    }
}
