//! skills/summoner/colorsofhyunrok.cpp: class SkillColorsofHyunrok : public SkillImpl (SH_COLORS_OF_HYUN_ROK).

use super::communes;
use crate::db::skills::*;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::SkillImpl;
use crate::status::consts::*;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillColorsofHyunrok;

impl SkillImpl for SkillColorsofHyunrok {
    fn skill_id(&self) -> u16 {
        SH_COLORS_OF_HYUN_ROK
    }

    /// SkillColorsofHyunrok::castendNoDamageId: level 7 ends the six colors and the Catnip Meteor buff; levels 1-6
    /// start the Catnip Meteor buff (with the Hyun Rok communion) and the level's color (the element of Catnip Meteor,
    /// Hyunrok Breeze and Hyunrok Cannon).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if skill_lv == 7 {
            for t in [
                SC_COLORS_OF_HYUN_ROK_1,
                SC_COLORS_OF_HYUN_ROK_2,
                SC_COLORS_OF_HYUN_ROK_3,
                SC_COLORS_OF_HYUN_ROK_4,
                SC_COLORS_OF_HYUN_ROK_5,
                SC_COLORS_OF_HYUN_ROK_6,
                // The skill also ends the buff that increases Catnip Meteor damage
                SC_COLORS_OF_HYUN_ROK_BUFF,
            ] {
                sc::status_change_end(c, src.id, t, 0);
            }
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            clif_skill_nodamage(c, Some(&s), &s, id, skill_lv as i32, true);
        } else {
            let mut type_ = skill_get_sc(id) as i32;
            // Buff to increase Catnip Meteor damage
            if communes(c, src, SH_COMMUNE_WITH_HYUN_ROK) {
                sc::sc_start(c, Some(src), target, SC_COLORS_OF_HYUN_ROK_BUFF, 100, 1, skill_get_time(id, skill_lv) as i64);
            }
            // Endows elemental property to Catnip Meteor, Hyunrok Breeze and Hyunrok Cannon skills
            match skill_lv {
                1 => type_ = SC_COLORS_OF_HYUN_ROK_1,
                2 => type_ = SC_COLORS_OF_HYUN_ROK_2,
                3 => type_ = SC_COLORS_OF_HYUN_ROK_3,
                4 => type_ = SC_COLORS_OF_HYUN_ROK_4,
                5 => type_ = SC_COLORS_OF_HYUN_ROK_5,
                6 => type_ = SC_COLORS_OF_HYUN_ROK_6,
                _ => {}
            }
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            sc::sc_start(c, Some(src), &t, type_, 100, skill_lv as i32, skill_get_time(id, skill_lv) as i64);
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            clif_skill_nodamage(c, Some(&s), &s, id, skill_lv as i32, true);
        }
    }
}
