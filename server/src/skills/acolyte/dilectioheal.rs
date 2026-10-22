//! skills/acolyte/dilectioheal.cpp: class SkillDilectioHeal : public SkillImpl (CD_DILECTIO_HEAL).

use crate::battle::BCT_PARTY;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::misc::skill_calc_heal;
use crate::skills::novice::party_foreachsamemap_area_sub;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillDilectioHeal;

impl SkillImpl for SkillDilectioHeal {
    fn skill_id(&self) -> u16 {
        CD_DILECTIO_HEAL
    }

    /// SkillDilectioHeal::castendNoDamageId: the animation on the target, then the skill again with the TARGET as
    /// its source (flag 1), which heals the target's party members in range (flag 3) or the target alone.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if *flag & 1 != 0 {
            party_heal(c, id, src, target, skill_lv, tick, *flag);
        } else {
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true); // Placed here to display animation on target only.
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            skill_castend_nodamage_id(c, &t, &t, id, skill_lv, tick, 1);
        }
    }
}

/// The `flag & 1` part SkillDilectioHeal and SkillMedialeVotum (medialevotum.cpp) share: the heal (skill_calc_heal,
/// shown as an AL_HEAL packet with no source) on the target when the source is no player / has no party / this is
/// the party's own recursion (flag & 2); else the recursion over the source's party members in range
/// (flag | BCT_PARTY | 3).
pub(crate) fn party_heal(c: &Ctx, skill_id: u16, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
    let sd = if src.kind == BL_PC { crate::pc::character_of(c, src.id) } else { None };
    match sd {
        Some(ch) if ch.party_id != 0 && flag & 2 == 0 => {
            let f = flag | BCT_PARTY | 3;
            party_foreachsamemap_area_sub(c, src, skill_get_splash(skill_id, skill_lv) as i16, src, skill_id, skill_lv, tick, f, skill_castend_nodamage_id);
        }
        _ => {
            let heal_amount = skill_calc_heal(c, src, Some(target), skill_id, skill_lv, true);
            clif_skill_nodamage(c, None, target, AL_HEAL, heal_amount, true);
            crate::status::hp::status_heal(c, target.id, heal_amount as i64, 0, 0, 0);
        }
    }
}
