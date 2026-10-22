//! skills/ninja/shadowtrampling.cpp: class SkillShadowTrampling : public SkillImpl (KG_KAGEHUMI), rAthena e985006.

use crate::battle::{BCT_ENEMY, DMG_SINGLE};
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_PC, BL_SKILL};
use crate::skill::area::{self, foreach_in_range, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::{clif_skill_damage, clif_skill_nodamage, DMGVAL_IGNORE};
use crate::skills::novice::{sce, status_get_amotion};
use crate::skills::SkillImpl;
use crate::status::consts::*;
use crate::status::defs::{OPTION_CLOAK, OPTION_HIDE};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillShadowTrampling;

impl SkillImpl for SkillShadowTrampling {
    fn skill_id(&self) -> u16 {
        KG_KAGEHUMI
    }

    /// SkillShadowTrampling::castendNoDamageId: the cast (flag without 1) runs this again (flag | 1) on every unit in
    /// the splash area, then the caster's animation; each hidden / cloaked / camouflaged / shadow-formed / Marionette
    /// / Harmonize player found loses those states (Shadow Form resists with 10% per its level) and gets the skill's
    /// status.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        if *flag & 1 != 0 {
            if target.kind != BL_PC {
                return;
            }
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            // tsc != nullptr (a player always has one)
            let hidden = t.sc.option & (OPTION_CLOAK | OPTION_HIDE) != 0
                || sce(c, &t, SC_CAMOUFLAGE).is_some()
                || sce(c, &t, SC__SHADOWFORM).is_some()
                || sce(c, &t, SC_MARIONETTE).is_some()
                || sce(c, &t, SC_HARMONIZE).is_some();
            if hidden {
                sc::status_change_end(c, t.id, SC_HIDING, 0);
                sc::status_change_end(c, t.id, SC_CLOAKING, 0);
                sc::status_change_end(c, t.id, SC_CLOAKINGEXCEED, 0);
                sc::status_change_end(c, t.id, SC_CAMOUFLAGE, 0);
                sc::status_change_end(c, t.id, SC_NEWMOON, 0);
                if let Some(e) = sce(c, &t, SC__SHADOWFORM) {
                    // [100 - (Skill Level x 10)] %
                    if ((c.rnd() % 100) as i32) < 100 - e.val1 * 10 {
                        sc::status_change_end(c, t.id, SC__SHADOWFORM, 0);
                    }
                }
                sc::status_change_end(c, t.id, SC_MARIONETTE, 0);
                sc::status_change_end(c, t.id, SC_HARMONIZE, 0);
                let t = unit::get(c, target.id).unwrap_or(t);
                sc::sc_start(c, Some(src), &t, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
            }
        } else {
            let f = *flag | BCT_ENEMY | 1;
            foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_CHAR | BL_SKILL, Wall::Config, |c, bl| {
                area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id)
            });
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            clif_skill_damage(c, &s, &s, tick, status_get_amotion(c, &s), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
            let t = unit::get_or_freed(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, true);
        }
    }
}
