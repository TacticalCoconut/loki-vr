//! skills/npc/npcfatalmenace.cpp: class SkillNpcFatalMenace : public WeaponSkillImpl (NPC_FATALMENACE), rAthena
//! e985006. The warps are NPC_FATALMENACE timerskills (skill_attack queues the targets', skill/timerskill.rs runs
//! them).

use crate::battle::{BCT_ENEMY, DMG_SINGLE};
use crate::db::skills::*;
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::{clif_skill_damage, DMGVAL_IGNORE};
use crate::skill::timerskill::skill_addtimerskill;
use crate::skills::novice::status_get_amotion;
use crate::skills::{weapon_castend_damage_id, SkillImpl};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillNpcFatalMenace;

impl SkillImpl for SkillNpcFatalMenace {
    fn skill_id(&self) -> u16 {
        NPC_FATALMENACE
    }

    /// SkillNpcFatalMenace::castendDamageId: a random cell of the map is the destination (skill_area_temp[4] / [5]);
    /// the enemies around the target are hit (flag & 1: one of them: WeaponSkillImpl::castendDamageId) and the caster
    /// warps there 800 ms later.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // todo should it teleport the target ?
        if *flag & 1 != 0 {
            weapon_castend_damage_id(c, id, src, target, skill_lv, tick, *flag);
        } else {
            // map_search_freecell(src, 0, &x, &y, -1, -1, 0): a random cell of src's map; on failure src's cell
            let (x, y) = crate::mob::spawn::map_search_freecell(c, Some((src.map, src.x, src.y)), 0, 0, 0, -1, -1, 0, 50).unwrap_or((src.x, src.y));
            // Destination area
            area::set_temp(4, x as i32);
            area::set_temp(5, y as i32);
            let f = *flag | BCT_ENEMY | 1;
            foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, area::splash_target(src), Wall::Config, |c, bl| {
                area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id)
            });
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            skill_addtimerskill(c, &s, tick + 800, s.id, x, y, id, skill_lv, 0, *flag); // To teleport Self
            clif_skill_damage(c, &s, &s, tick, status_get_amotion(c, &s), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
        }
    }
}
