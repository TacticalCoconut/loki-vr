//! skills/taekwon/spiritoftheassasin.cpp: class SkillSpiritoftheAssasin : public SkillImpl (SL_ASSASIN).

use crate::db::skills::*;
use crate::skills::taekwon::spirit_castend_nodamage_id;
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillSpiritoftheAssasin;

impl SkillImpl for SkillSpiritoftheAssasin {
    fn skill_id(&self) -> u16 {
        SL_ASSASIN
    }

    /// SkillSpiritoftheAssasin::castendNoDamageId: sc_start2(src, target, skill_get_sc, 100, skill_lv, SL_ASSASIN, skill_get_time); on success
    /// the nodamage packet and the caster's SC_SMA, else clif_skill_fail for a player caster.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        spirit_castend_nodamage_id(c, self.skill_id(), src, target, skill_lv, |_| {});
    }
}
