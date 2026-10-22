//! skills/swordman/chargeattack.cpp: class SkillChargeAttack : public SkillImpl (KN_CHARGEATK).

use crate::battle::calc::Damage;
use crate::battle::BF_WEAPON;
use crate::db::skills::*;
use crate::map::CellChk;
use crate::path::{DIRX, DIRY};
use crate::skill::attack::{skill_attack, skill_blown, BLOWN_NONE};
use crate::skill::misc::skill_check_unit_movepos;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillChargeAttack;

impl SkillImpl for SkillChargeAttack {
    fn skill_id(&self) -> u16 {
        KN_CHARGEATK
    }

    /// SkillChargeAttack::castendDamageId (RENEWAL: the knockback distance is the skill's blewcount).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let path = crate::path::path_search_long(src.map, src.x, src.y, target.x, target.y, CellChk::ChkWall);
        let mut dist = skill_get_blewcount(id, skill_lv);
        let dir = crate::map::calc_dir(target, src.x, src.y);
        // teleport to target (if not on WoE grounds)
        if skill_check_unit_movepos(c, 5, src, target.x + DIRX[dir as usize], target.y + DIRY[dir as usize], 0, true) {
            if let Some(s) = unit::get(c, src.id) {
                crate::battle::clif::clif_blown(c, &s);
            }
        }
        // cause damage and knockback if the path to target was a straight one
        if path {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            if skill_attack(c, BF_WEAPON, &s, &s, &t, id, skill_lv, tick, dist) != 0 {
                if crate::npc::mapflag(c, s.map, crate::npc::MF_PVP) {
                    dist += 2; // Knockback is 4 on PvP maps
                }
                let s = unit::get(c, src.id).unwrap_or(s);
                let t = unit::get(c, target.id).unwrap_or(t);
                // (skill_blown's count is a `char`)
                skill_blown(c, &s, &t, dist as i8 as i32, dir as i8, BLOWN_NONE);
            }
        }
    }

    /// SkillChargeAttack::calculateSkillRatio (RENEWAL).
    fn calculate_skill_ratio(&self, _c: &Ctx, _wd: &Damage, _src: &Unit, _target: &Unit, _skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        *base_skillratio += 600;
    }
}
