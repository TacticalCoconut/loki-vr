//! skills/merchant/powerthrust.cpp: class SkillPowerThrust : public SkillImpl (BS_OVERTHRUST).

use crate::battle::BCT_PARTY;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::party_foreachsamemap_area_sub;
use crate::skills::SkillImpl;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// SkillPowerThrust::SkillPowerThrust() : SkillImpl(BS_OVERTHRUST).
pub struct SkillPowerThrust;

impl SkillImpl for SkillPowerThrust {
    fn skill_id(&self) -> u16 {
        BS_OVERTHRUST
    }

    /// SkillPowerThrust::castendNoDamageId: without a party (a monster caster, a party-less player, or a party
    /// member's recursive call: flag & 1) the status goes on the target when it is not a player or wields one of the
    /// skill's weapon types (val2 = 1 when cast on oneself), and the packet's source is the target itself; otherwise
    /// every party member in the splash range (SplashArea -1: AREA_SIZE) gets the recursive call.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // map_session_data* sd = BL_CAST(BL_PC, src)
        let sd = if src.kind == BL_PC { crate::pc::character_of(c, src.id) } else { None };
        match sd {
            Some(ch) if ch.party_id != 0 && *flag & 1 == 0 => {
                party_foreachsamemap_area_sub(c, src, skill_get_splash(id, skill_lv) as i16, src, id, skill_lv, tick, *flag | BCT_PARTY | 1, skill_castend_nodamage_id);
            }
            _ => {
                let weapontype = skill_get_weapontype(id);
                // map_session_data* dstsd = BL_CAST(BL_PC, target): !dstsd || pc_check_weapontype(dstsd, weapontype)
                if weapontype == 0 || target.kind != BL_PC || crate::status::sc_ext::pc_check_weapontype(c, target.id, weapontype) {
                    let ok = sc::sc_start2(c, Some(src), target, skill_get_sc(id) as i32, 100, skill_lv as i32, (src.id == target.id) as i32, skill_get_time(id, skill_lv) as i64);
                    let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                    clif_skill_nodamage(c, Some(&t), &t, id, skill_lv as i32, ok);
                }
            }
        }
    }
}
