//! skills/ninja/illusionbewitch.cpp: class SkillIllusionBewitch : public SkillImpl (KO_GENWAKU), rAthena e985006.

use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_MOB, BL_PC};
use crate::skill::area::{foreach_in_range, Wall};
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skill::misc::skill_check_unit_movepos;
use crate::skills::SkillImpl;
use crate::status::consts::SC_CONFUSION;
use crate::status::defs::{MD_IGNOREMAGIC, MD_IGNOREMELEE, MD_IGNOREMISC, MD_IGNORERANGED, SCSTART_NORATEDEF};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillIllusionBewitch;

impl SkillImpl for SkillIllusionBewitch {
    fn skill_id(&self) -> u16 {
        KO_GENWAKU
    }

    /// SkillIllusionBewitch::castendNoDamageId: on an enemy player or monster that is not immune to every attack type,
    /// a player's cast succeeds with (45 + 5 x level) - INT / 10 %; both get Confusion (25% / 75%, rate not reduced),
    /// then the caster moves to the target's cell and the target (unless knock-back immune) to the caster's, and the
    /// units that attacked the caster attack the target instead.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let tstatus = crate::status::status_get_status_data(c, target);
        let is_sd = src.kind == BL_PC;
        let all_ignore = MD_IGNOREMELEE | MD_IGNOREMAGIC | MD_IGNORERANGED | MD_IGNOREMISC;
        if (target.kind == BL_PC || target.kind == BL_MOB) && !crate::battle::mobif::has_mode(tstatus.mode, all_ignore) && crate::battle::target::battle_check_target(c, src, target, BCT_ENEMY) > 0 {
            let (x, y) = (src.x, src.y);
            // [(Base chance of success) - (Intelligence Objectives / 10)]%.
            if is_sd && ((c.rnd() % 100) as i32) > (45 + 5 * skill_lv as i32) - tstatus.int_ as i32 / 10 {
                skill_fail(c, src, id);
                return;
            }
            // Confusion is still inflicted (but rate isn't reduced), no matter map type.
            let time = skill_get_time(id, skill_lv) as i64;
            sc::status_change_start(c, Some(src), src, SC_CONFUSION, 2500, skill_lv as i32, 0, 0, 0, time, SCSTART_NORATEDEF, 0);
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            sc::status_change_start(c, Some(&s), &t, SC_CONFUSION, 7500, skill_lv as i32, 0, 0, 0, time, SCSTART_NORATEDEF, 0);
            let t = unit::get(c, target.id).unwrap_or(t);
            if skill_check_unit_movepos(c, 5, &s, t.x, t.y, 0, false) {
                let s = unit::get(c, src.id).unwrap_or(s);
                clif_skill_nodamage(c, Some(&s), &s, id, skill_lv as i32, true);
                crate::battle::clif::clif_blown(c, &s);
                if crate::battle::knockback::unit_blown_immune(c, &t, 0x1) == 0 {
                    unit::movepos(c, t.id, x, y, 0, false);
                    let t = unit::get(c, target.id).unwrap_or(t);
                    if t.kind == BL_PC && crate::status::sc_ext::pc_issit(c, t.id) {
                        crate::clif::sitting(c, &t); // Avoid sitting sync problem
                    }
                    crate::battle::clif::clif_blown(c, &t);
                    // map_foreachinallrange(unit_changetarget, src, AREA_SIZE, BL_CHAR, src, target)
                    foreach_in_range(c, &s, crate::conf::area_size(), BL_CHAR, Wall::None, |c, bl| unit::changetarget(c, bl, &s, &t));
                }
            }
        }
    }
}
