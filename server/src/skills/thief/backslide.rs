//! skills/thief/backslide.cpp: class SkillBackSlide : public SkillImpl (TF_BACKSLIDING).

use crate::db::skills::*;
use crate::skill::attack::{skill_blown, BLOWN_DONT_SEND_PACKET, BLOWN_IGNORE_NO_KNOCKBACK};
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillBackSlide;

impl SkillImpl for SkillBackSlide {
    fn skill_id(&self) -> u16 {
        TF_BACKSLIDING
    }

    /// SkillBackSlide::castendNoDamageId (RENEWAL): this is the correct implementation as per packet logging
    /// information. [Skotlex]
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, bl: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        // Backsliding makes you immune to being stopped for 200ms, but only if you don't have the endure effect yet
        // (unit_bl2ud(bl): every unit row has its unit_data here)
        if !crate::status::status_isendure(c, bl.id, tick, true) {
            if let Some(mut u) = unit::get(c, bl.id) {
                u.ud.endure_tick = tick + 200;
                unit::save(c, &u);
            }
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let b = unit::get(c, bl.id).unwrap_or_else(|| bl.clone());
        let blew_count = skill_blown(c, &s, &b, skill_get_blewcount(id, skill_lv), unit::getdir(&b) as i8, BLOWN_IGNORE_NO_KNOCKBACK | BLOWN_DONT_SEND_PACKET);
        let s = unit::get(c, src.id).unwrap_or(s);
        let b = unit::get(c, bl.id).unwrap_or(b);
        clif_skill_nodamage(c, Some(&s), &b, id, skill_lv as i32, true);
        if blew_count > 0 {
            crate::battle::clif::clif_blown(c, &s); // Always blow, otherwise it shows a casting animation. [Lemongrass]
        }
    }
}
