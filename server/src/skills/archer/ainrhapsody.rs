//! skills/archer/ainrhapsody.cpp: class SkillAinRhapsody : public SkillImpl (TR_AIN_RHAPSODY).

use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillAinRhapsody;

impl SkillImpl for SkillAinRhapsody {
    fn skill_id(&self) -> u16 {
        TR_AIN_RHAPSODY
    }

    /// SkillAinRhapsody::castendNoDamageId: on the recursive call `sc_start4(src, target, skill_get_sc(getSkillId()),
    /// 100, skill_lv, 0, flag, 0, skill_get_time(getSkillId(), skill_lv))`; the caster's call remembers the song,
    /// checks for a partner (flag 2) and walks the characters around it (skills/archer/mod.rs trouvere_area_song).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        super::trouvere_area_song(c, id, src, target, skill_lv, tick, flag, |c, src, target, skill_lv, flag| {
            sc::sc_start4(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, 0, flag, 0, skill_get_time(id, skill_lv) as i64);
        });
    }
}
