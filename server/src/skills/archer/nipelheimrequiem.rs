//! skills/archer/nipelheimrequiem.cpp: class SkillNipelheimRequiem : public SkillImpl (TR_NIPELHEIM_REQUIEM).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_CURSE, SC_HANDICAPSTATE_DEPRESSION};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillNipelheimRequiem;

impl SkillImpl for SkillNipelheimRequiem {
    fn skill_id(&self) -> u16 {
        TR_NIPELHEIM_REQUIEM
    }

    /// SkillNipelheimRequiem::castendNoDamageId: on the recursive call (flag & 1) Curse (4% per level) and
    /// Depression (5% per level, doubled with a partner: flag & 2); the caster's call remembers the song, checks for a
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
            sc::sc_start(c, Some(src), target, SC_CURSE, 4 * skill_lv as i32, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
            let t = super::fresh(c, target);
            sc::sc_start(c, Some(src), &t, SC_HANDICAPSTATE_DEPRESSION, success_chance as i32, skill_lv as i32, skill_get_time2(id, skill_lv) as i64);
        });
    }
}
