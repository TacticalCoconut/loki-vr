//! skills/ninja/illusionshadow.cpp: class SkillIllusionShadow : public SkillImpl (KO_ZANZOU), rAthena e985006.

use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::area::{foreach_in_range, Wall};
use crate::skill::attack::{skill_blown, BLOWN_NONE};
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::timer::{self, Func};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// mob.hpp MOBID_ZANZOU (KO_KAGE).
const MOBID_ZANZOU: i32 = 2308;

pub struct SkillIllusionShadow;

/// status.cpp status_get_name for a player: `sd->fakename` when set, else the character's name.
fn player_name(c: &Ctx, u: &Unit) -> String {
    let fake = crate::atcmd_pc::get(c, u.id).fakename;
    if !fake.is_empty() {
        fake
    } else {
        u.name.clone()
    }
}

impl SkillImpl for SkillIllusionShadow {
    fn skill_id(&self) -> u16 {
        KO_ZANZOU
    }

    /// SkillIllusionShadow::castendNoDamageId: a player leaves a shadow (MOBID_ZANZOU, named after the player, special
    /// AI_ZANZOU, deleted after skill_get_time) on its cell; the monsters around that attacked the player attack the
    /// shadow instead; then the animation and the player's knock-back in the direction it faces.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.kind != BL_PC {
            return;
        }
        let name = player_name(c, src);
        let Some(mut md2) = crate::mob::spawn::mob_once_spawn_sub(c, Some(src), src.map, src.x, src.y, Some(&name), MOBID_ZANZOU, Some(""), crate::status::defs::SZ_SMALL as u8, crate::mob::AI_NONE) else { return };
        md2.master_id = src.id;
        md2.special_ai = crate::mob::AI_ZANZOU;
        if md2.deletetimer != 0 {
            timer::delete(c, md2.deletetimer);
        }
        md2.deletetimer = timer::add(c, c.tick() + skill_get_time(id, skill_lv) as i64, Func::MobTimerDelete, md2.id, 0);
        crate::mob::put(c, &md2);
        crate::mob::spawn::mob_spawn(c, md2.id);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        if let Some(shadow) = unit::get(c, md2.id) {
            // map_foreachinallrange(unit_changetarget, src, AREA_SIZE, BL_MOB, src, md2)
            foreach_in_range(c, &s, crate::conf::area_size(), BL_MOB, Wall::None, |c, bl| unit::changetarget(c, bl, &s, &shadow));
        }
        let s = unit::get(c, src.id).unwrap_or(s);
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, true);
        skill_blown(c, &s, &t, skill_get_blewcount(id, skill_lv), unit::getdir(&t) as i8, BLOWN_NONE);
    }
}
