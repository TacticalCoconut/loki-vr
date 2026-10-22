//! skills/archer/rokicapriccio.cpp: class SkillRokiCapriccio : public SkillImpl (TR_ROKI_CAPRICCIO).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_CONFUSION, SC_HANDICAPSTATE_MISFORTUNE};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillRokiCapriccio;

impl SkillImpl for SkillRokiCapriccio {
    fn skill_id(&self) -> u16 {
        TR_ROKI_CAPRICCIO
    }

    /// SkillRokiCapriccio::castendNoDamageId: on the recursive call (flag & 1) Confusion (4% per level) and
    /// Misfortune (5% per level, doubled with a partner: flag & 2); the caster's call remembers the song, checks for a
    /// partner and walks the characters around it (skills/archer/mod.rs trouvere_area_song).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        super::trouvere_area_song(c, id, src, target, skill_lv, tick, flag, |c, src, target, skill_lv, flag| {
            // Need official success chances.
            let mut success_chance: u16 = 5 * skill_lv;
            if flag & 2 != 0 {
                success_chance *= 2;
            }
            // Is it a chance to inflect so and so, or seprate chances for inflicting each status? [Rytech]
            sc::sc_start(c, Some(src), target, SC_CONFUSION, 4 * skill_lv as i32, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
            let t = super::fresh(c, target);
            sc::sc_start(c, Some(src), &t, SC_HANDICAPSTATE_MISFORTUNE, success_chance as i32, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
        });
    }
}
