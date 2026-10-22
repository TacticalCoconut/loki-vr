//! skills/homunculus/homunculus_silentbreeze.cpp: class SkillSilentBreeze : public SkillImpl (MH_SILENT_BREEZE).

use super::{fresh, hd_of};
use crate::db::skills::*;
use crate::skills::SkillImpl;
use crate::status::consts::{SC_CONFUSION, SC_DEEPSLEEP, SC_HALLUCINATION, SC_HARMONIZE, SC_MANDRAGORA, SC_SILENCE, SC_SLEEP, SC_VOICEOFSIREN};
use crate::status::defs::SCSTART_NONE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// The status changes Silent Breeze removes from the target.
const SCS: [i32; 7] = [SC_MANDRAGORA, SC_HARMONIZE, SC_DEEPSLEEP, SC_VOICEOFSIREN, SC_SLEEP, SC_CONFUSION, SC_HALLUCINATION];

pub struct SkillSilentBreeze;

impl SkillImpl for SkillSilentBreeze {
    fn skill_id(&self) -> u16 {
        MH_SILENT_BREEZE
    }

    /// SkillSilentBreeze::castendNoDamageId: silences the homunculus and the target (rate 10000, SCSTART_NONE), heals
    /// the target 5 * the homunculus' level + status_base_matk_min (RENEWAL: of the *target's* type with the
    /// homunculus' battle_status and level, as rAthena passes them) with flag 3, and ends the target's sleep /
    /// confusion family. (rAthena dereferences BL_CAST(BL_HOM, src) without a check: only a homunculus casts it; any
    /// other caster ends here.)
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let Some(hd) = hd_of(c, src) else { return };
        // tsc = status_get_sc(target), taken before the status changes below
        let has_tsc = crate::battle::mobif::has_sc(target.kind);
        let lv = crate::status::status_get_lv(c, src);
        let heal = 5 * lv + crate::skill::misc::status_base_matk_min(c, target, &hd.battle_status, lv) as i32;
        let dur = skill_get_time(id, skill_lv) as i64;
        // Silences the homunculus and target
        sc::status_change_start(c, Some(src), &fresh(c, src), SC_SILENCE, 10000, skill_lv as i32, 0, 0, 0, dur, SCSTART_NONE, 0);
        sc::status_change_start(c, Some(&fresh(c, src)), &fresh(c, target), SC_SILENCE, 10000, skill_lv as i32, 0, 0, 0, dur, SCSTART_NONE, 0);
        // Recover the target's HP
        crate::status::hp::status_heal(c, target.id, heal as i64, 0, 0, 3);
        // Removes these SC from target
        if has_tsc {
            for t in SCS {
                if sc::has_sce(c, target.id, t) {
                    sc::status_change_end(c, target.id, t, 0);
                }
            }
        }
    }
}
