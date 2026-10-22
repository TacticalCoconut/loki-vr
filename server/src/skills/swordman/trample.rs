//! skills/swordman/trample.cpp: class SkillTrample : public SkillImpl (LG_TRAMPLE) and the file's static
//! skill_destroy_trap. The traps, their groups, skill_trap_splash and skill_delunit are the skillunit agent's
//! (skill/iface.rs until its merge: no skill unit exists before it, so nothing is destroyed yet).

use crate::battle::{BCT_SELF, DMG_SINGLE};
use crate::db::skills::*;
use crate::map::BL_SKILL;
use crate::skill::area::{foreach_in_range, Wall};
use crate::skill::clif::{clif_skill_damage, DMGVAL_IGNORE};
use crate::skill::iface;
use crate::skills::novice::status_get_amotion;
use crate::skills::SkillImpl;
use crate::status::consts::SC_SV_ROOTTWIST;
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// skill.hpp enum e_skill_unit_id (the traps skill_destroy_trap names).
pub const UNT_BLASTMINE: u16 = 0x8f;
pub const UNT_LANDMINE: u16 = 0x93;
pub const UNT_SHOCKWAVE: u16 = 0x94;
pub const UNT_SANDMAN: u16 = 0x95;
pub const UNT_FLASHER: u16 = 0x96;
pub const UNT_FREEZINGTRAP: u16 = 0x97;
pub const UNT_CLAYMORETRAP: u16 = 0x98;
pub const UNT_FIRINGTRAP: u16 = 0xd6;
pub const UNT_ICEBOUNDTRAP: u16 = 0xd7;
pub const UNT_CLUSTERBOMB: u16 = 0xd9;

/// skills/swordman/trample.cpp skill_destroy_trap (for Royal Guard's LG_TRAMPLE): a live trap's unit sets off its
/// splash (the explosive traps hit everything around), then is removed (traps aren't recovered).
fn skill_destroy_trap(c: &Ctx, su: &Unit, tick: Tick) -> i32 {
    // su->alive && (sg = su->group) && skill_get_inf2(sg->skill_id, INF2_ISTRAP)
    let Some((sg_skill_id, sg_skill_lv, sg_unit_id, sg_bl_flag)) = iface::skill_unit_alive_group(c, su) else { return 0 };
    if !skill_get_inf2(sg_skill_id, &[INF2_ISTRAP]) {
        return 0;
    }
    let splash = skill_get_splash(sg_skill_id, sg_skill_lv) as i16;
    match sg_unit_id {
        UNT_CLAYMORETRAP | UNT_FIRINGTRAP | UNT_ICEBOUNDTRAP => {
            // sg->bl_flag|BL_SKILL|~BCT_SELF (every BL_ bit set)
            let kinds = (sg_bl_flag | BL_SKILL as i32 | !BCT_SELF) as u16;
            foreach_in_range(c, su, splash, kinds, Wall::Config, |c, bl| iface::skill_trap_splash(c, bl, su, tick));
        }
        UNT_LANDMINE | UNT_BLASTMINE | UNT_SHOCKWAVE | UNT_SANDMAN | UNT_FLASHER | UNT_FREEZINGTRAP | UNT_CLUSTERBOMB => {
            if crate::skill::conf::skill_wall_check() && !skill_get_nk(sg_skill_id, &[NK_NODAMAGE]) {
                foreach_in_range(c, su, splash, sg_bl_flag as u16, Wall::Always, |c, bl| iface::skill_trap_splash(c, bl, su, tick));
            } else {
                foreach_in_range(c, su, splash, sg_bl_flag as u16, Wall::None, |c, bl| iface::skill_trap_splash(c, bl, su, tick));
            }
        }
        _ => {}
    }
    // Traps aren't recovered.
    iface::skill_delunit(c, su.id);
    0
}

pub struct SkillTrample;

impl SkillImpl for SkillTrample {
    fn skill_id(&self) -> u16 {
        LG_TRAMPLE
    }

    /// SkillTrample::castendNoDamageId: the dummy damage packet, a (25 + 25 * lv)% chance to destroy the traps
    /// around the target, and the target's Root Twist ends.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        clif_skill_damage(c, src, target, tick, status_get_amotion(c, src), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
        if ((c.rnd() % 100) as i32) < 25 + 25 * skill_lv as i32 {
            foreach_in_range(c, target, skill_get_splash(id, skill_lv) as i16, BL_SKILL, Wall::None, |c, bl| skill_destroy_trap(c, bl, tick));
        }
        sc::status_change_end(c, target.id, SC_SV_ROOTTWIST, 0);
    }
}
