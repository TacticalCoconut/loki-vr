//! skills/taekwon/feelingthesunmoonandstars.cpp: class SkillFeelingtheSunMoonandStars : public SkillImpl (SG_FEEL).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::pc_taekwon::{clif_feel_info, clif_feel_req, feel_index};
use crate::skills::SkillImpl;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub struct SkillFeelingtheSunMoonandStars;

impl SkillImpl for SkillFeelingtheSunMoonandStars {
    fn skill_id(&self) -> u16 {
        SG_FEEL
    }

    /// SkillFeelingtheSunMoonandStars::castendNoDamageId: the confirmation prompt when the level's map is not set yet
    /// (clif_parse_FeelSaveOk memorises the current map), else the map it holds.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        //AuronX reported you CAN memorize the same map as all three. [Skotlex]
        if src.kind == BL_PC {
            let i = skill_lv.wrapping_sub(1) as usize;
            if feel_index(c, src.id, i) == 0 {
                clif_feel_req(c, src, skill_lv);
            } else {
                clif_feel_info(c, src, i as u8, 1);
            }
        }
    }
}
