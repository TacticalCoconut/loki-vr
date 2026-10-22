//! skills/mercenary/mercenary_brandishspear.cpp: class SkillMercenaryBrandishSpear : public SkillImpl (ML_BRANDISH).
//! Unlike the Knight's renewal class it keeps the old layered area (up to four directional strips whose depth goes
//! into the damage flag); the depth bits are only set for a player caster (`sd ? n : 0`), so a mercenary's strips all
//! carry 0 and every target takes the inner strip's ratio (rAthena's behaviour, kept).

use crate::battle::calc::Damage;
use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::area::{self, map_foreachindir};
use crate::skill::attack::skill_attack;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::SD_ANIMATION;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillMercenaryBrandishSpear;

impl SkillImpl for SkillMercenaryBrandishSpear {
    fn skill_id(&self) -> u16 {
        ML_BRANDISH
    }

    /// SkillMercenaryBrandishSpear::calculateSkillRatio: 100% + 20% per level, plus a share of it per strip depth
    /// (wd->miscflag 0: the inner strip).
    fn calculate_skill_ratio(&self, _c: &Ctx, wd: &Damage, _src: &Unit, _target: &Unit, skill_lv: u16, base_skillratio: &mut i32, _mflag: i32) {
        let lv = skill_lv as i32;
        let ratio = 100 + 20 * lv;
        *base_skillratio += -100 + ratio;
        if lv > 3 && wd.miscflag == 0 {
            *base_skillratio += ratio / 2;
        }
        if lv > 6 && wd.miscflag == 0 {
            *base_skillratio += ratio / 4;
        }
        if lv > 9 && wd.miscflag == 0 {
            *base_skillratio += ratio / 8;
        }
        if lv > 6 && wd.miscflag == 1 {
            *base_skillratio += ratio / 2;
        }
        if lv > 9 && wd.miscflag == 1 {
            *base_skillratio += ratio / 4;
        }
        if lv > 9 && wd.miscflag == 2 {
            *base_skillratio += ratio / 2;
        }
    }

    /// SkillMercenaryBrandishSpear::castendDamageId: coded apart for it needs the flag passed to the damage
    /// calculation; the original target keeps the animation.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if area::temp(1) != target.id as i32 {
            skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag | SD_ANIMATION);
        } else {
            skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, *flag);
        }
    }

    /// SkillMercenaryBrandishSpear::castendNoDamageId: the strips in the target's direction, the farthest first
    /// (level 10: offset maxcount-1, 7: maxcount-2, 4: maxcount-3, each 1 cell deep), then the inner strip (length
    /// maxcount-3 from the caster).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        // map_session_data* sd = BL_CAST(BL_PC, src)
        let sd = src.kind == BL_PC;
        area::set_temp(1, target.id as i32);
        let splash = skill_get_splash(id, skill_lv) as i16;
        let maxcount = skill_get_maxcount(id, skill_lv);
        let kinds = area::splash_target(src);
        let strip = |length: i32, offset: i32, depth: i32| {
            let f = *flag | BCT_ENEMY | if sd { depth } else { 0 };
            // src->x / target->x are read at each call (an earlier strip's knockback may have moved them)
            let s = unit::get_or_freed(c, src.id).unwrap_or_else(|| src.clone());
            let t = unit::get_or_freed(c, target.id).unwrap_or_else(|| target.clone());
            map_foreachindir(c, s.map, s.x, s.y, t.x, t.y, splash, length, offset, kinds, |c, bl| {
                area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_damage_id)
            });
        };
        if skill_lv >= 10 {
            strip(1, maxcount - 1, 3);
        }
        if skill_lv >= 7 {
            strip(1, maxcount - 2, 2);
        }
        if skill_lv >= 4 {
            strip(1, maxcount - 3, 1);
        }
        strip(maxcount - 3, 0, 0);
    }
}
