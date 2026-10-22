//! skills/acolyte/cursedcircle.cpp: class SkillCursedCircle : public SkillImpl (SR_CURSEDCIRCLE).

use crate::battle::{BCT_ENEMY, DMG_SINGLE};
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_MOB, BL_PC};
use crate::skill::area;
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::{clif_skill_damage, clif_skill_nodamage, DMGVAL_IGNORE};
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::status_get_amotion;
use crate::skills::npc::npccursedcircle::map_forcountinrange;
use crate::skills::SkillImpl;
use crate::status::consts::SC_CURSEDCIRCLE_ATKER;
use crate::status::defs::CLASS_BOSS;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillCursedCircle;

impl SkillImpl for SkillCursedCircle {
    fn skill_id(&self) -> u16 {
        SR_CURSEDCIRCLE
    }

    /// SkillCursedCircle::castendNoDamageId: the enemies around the caster are held (flag & 1: one of them, not a
    /// boss; a monster also forgets its target; the `flag |= SKILL_NOCONSUME_REQ` there only changes the recursive
    /// call's flag, which never consumes anyway). The splash is map_forcountinrange with the spheres the cast used as
    /// the limit (15 for non-players), but the count is the sum of skill_castend_nodamage_id's return values, 0 on its
    /// normal path: the limit rarely stops it, and the caster spends / SC_CURSEDCIRCLE_ATKER gets that sum (rAthena's
    /// quirk, kept).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let type_ = skill_get_sc(id) as i32;
        if *flag & 1 != 0 {
            if crate::battle::bl::class_type(c, target) == CLASS_BOSS {
                return;
            }
            if sc::sc_start2(c, Some(src), target, type_, 100, skill_lv as i32, src.id as i32, skill_get_time(id, skill_lv) as i64) {
                if target.kind == BL_MOB {
                    // mob_unlocktarget((TBL_MOB*)target, gettick())
                    crate::mob::ai::mob_unlocktarget(c, target.id, c.tick());
                }
                let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                crate::status::sc_ext::clif_bladestop(c, &s, target.id, true);
                *flag |= SKILL_NOCONSUME_REQ;
            }
            return;
        }
        let sd = src.kind == BL_PC;
        clif_skill_damage(c, src, target, tick, status_get_amotion(c, src), 0, DMGVAL_IGNORE, 1, id, skill_lv as i32, DMG_SINGLE);
        // Assume 15 spiritballs in non-characters
        let max = if sd { crate::skill::state::get(c, src.id).spiritball_old as i32 } else { 15 };
        let f = *flag | BCT_ENEMY | 1;
        let count = map_forcountinrange(c, src, skill_get_splash(id, skill_lv) as i16, max, BL_CHAR, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id));
        if sd {
            crate::skill::state::pc_delspiritball(c, src.id, count, 0);
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let ok = sc::sc_start2(c, Some(&s), &s, SC_CURSEDCIRCLE_ATKER, 100, skill_lv as i32, count, skill_get_time(id, skill_lv) as i64);
        let s = unit::get(c, src.id).unwrap_or(s);
        clif_skill_nodamage(c, Some(&s), &s, id, skill_lv as i32, ok);
    }
}
