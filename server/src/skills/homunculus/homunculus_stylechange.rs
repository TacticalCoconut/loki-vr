//! skills/homunculus/homunculus_stylechange.cpp: class SkillStyleChange : public SkillImpl (MH_STYLE_CHANGE).

use super::hd_of;
use crate::db::skills::*;
use crate::homunculus::data::{MH_MD_FIGHTING, MH_MD_GRAPPLING};
use crate::skills::SkillImpl;
use crate::status::consts::SC_STYLE_CHANGE;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillStyleChange;

impl SkillImpl for SkillStyleChange {
    fn skill_id(&self) -> u16 {
        MH_STYLE_CHANGE
    }

    /// SkillStyleChange::castendNoDamageId: toggles a running SC_STYLE_CHANGE between fighting and grappling (val1,
    /// in place: no packet, Aegis shows no message), or starts it in fighting mode for ever. No skill packet.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, _skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        if hd_of(c, src).is_none() {
            return;
        }
        if let Some(mut sce) = sc::get_sce(c, src.id, SC_STYLE_CHANGE) {
            // in preparation for other bl usage
            sce.val1 = if sce.val1 == MH_MD_FIGHTING { MH_MD_GRAPPLING } else { MH_MD_FIGHTING };
            sc::put_sce(c, &sce);
            // (commented out upstream: the fighting / grappling message to the master)
        } else {
            sc::sc_start(c, Some(src), src, SC_STYLE_CHANGE, 100, MH_MD_FIGHTING, sc::INFINITE_TICK);
        }
    }
}
