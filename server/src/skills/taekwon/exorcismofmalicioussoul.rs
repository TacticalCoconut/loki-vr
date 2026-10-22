//! skills/taekwon/exorcismofmalicioussoul.cpp: class SkillExorcismOfMaliciousSoul : public SkillImplRecursiveDamageSplash (SOA_EXORCISM_OF_MALICIOUS_SOUL).

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skill::state::{self as skstate, pc_delsoulball};
use crate::skills::novice::{pc_checkskill, re_lvl_dmod, sce};
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::status::consts::{SC_SOULCURSE, SC_TOTEM_OF_TUTELARY};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillExorcismOfMaliciousSoul;

impl SkillImpl for SkillExorcismOfMaliciousSoul {
    fn skill_id(&self) -> u16 {
        SOA_EXORCISM_OF_MALICIOUS_SOUL
    }

    crate::splash_skill_defaults!();

    /// SkillExorcismOfMaliciousSoul::calculateSkillRatio.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        let sstatus = crate::status::status_get_status_data(c, src);
        let lv = skill_lv as i32;
        *skillratio += -100 + 150 * lv;
        *skillratio += pc_checkskill(c, src, SOA_SOUL_MASTERY) * 2;
        *skillratio += sstatus.spl as i32;
        if sce(c, target, SC_SOULCURSE).is_some() || sce(c, src, SC_TOTEM_OF_TUTELARY).is_some() {
            *skillratio += 100 * lv;
        }
        // map_session_data* sd = BL_CAST(BL_PC, src)
        if src.kind == BL_PC {
            *skillratio *= skstate::get(c, src.id).soulball_old as i32;
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }

    /// SkillExorcismOfMaliciousSoul::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // map_session_data* sd = BL_CAST(BL_PC, src)
        if src.kind == BL_PC {
            // Remove old souls if any exist.
            let soulball = skstate::with(c, src.id, |s| {
                s.soulball_old = s.soulball;
                s.soulball as i32
            });
            pc_delsoulball(c, src.id, soulball, false);
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        clif_skill_nodamage(c, Some(&s), target, id, skill_lv as i32, true);
        skill_castend_damage_id(c, &s, target, id, skill_lv, tick, *flag);
    }
}

impl RecursiveDamageSplash for SkillExorcismOfMaliciousSoul {}
