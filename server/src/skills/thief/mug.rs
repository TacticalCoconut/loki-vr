//! skills/thief/mug.cpp: class SkillMug : public SkillImpl (RG_STEALCOIN).

use crate::battle::ATK_DEF;
use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC};
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skills::novice::pc_checkskill;
use crate::skills::SkillImpl;
use crate::status::defs::{BF_LONG, BF_NORMAL, BF_WEAPON};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillMug;

impl SkillImpl for SkillMug {
    fn skill_id(&self) -> u16 {
        RG_STEALCOIN
    }

    /// SkillMug::castendNoDamageId: a player on a monster only. The chance (per mille, rnd_chance_official) is
    /// 10 x Mug level + DEX / 2 + LUK / 2 + 2 x (base level - target level); a success marks the monster, gives
    /// rnd(8..10 x target level) + level x target level / 10 Zeny, triggers a 0-damage event (the monster may switch
    /// to the caster) and sends the amount as the animation's level.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        if src.kind != BL_PC || target.kind != BL_MOB {
            return;
        }
        let id = self.skill_id();
        let Some(ch) = crate::pc::character_of(c, src.id) else { return };
        let target_lv = crate::status::status_get_lv(c, target);
        let sstatus = crate::status::status_get_status_data(c, src); // sd->battle_status
        let mut rate = 10 * pc_checkskill(c, src, RG_STEALCOIN);
        rate += sstatus.dex as i32 / 2;
        rate += sstatus.luk as i32 / 2;
        rate += 2 * (ch.base_level as i32 - target_lv);
        if !c.rnd_chance_official(rate, 1000) {
            skill_fail(c, src, id);
            return;
        }
        if let Some(mut md) = crate::mob::get(c, target.id) {
            md.steal_coin_flag = true;
            crate::mob::put(c, &md);
        }
        // Zeny Steal Amount
        let mut amount = c.rnd_value(8 * target_lv, 10 * target_lv);
        amount += (skill_lv as i32 * target_lv) / 10;
        crate::inventory::pc_getzeny(c, src.id, amount as i64, crate::map_log::LOG_TYPE_STEAL, 0);
        // This triggers a 0 damage event and might make the monster switch target to caster
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        crate::battle::weapon_attack::battle_damage(c, Some(&s), &t, 0, 1, skill_lv, 0, ATK_DEF, (BF_WEAPON | BF_LONG | BF_NORMAL) as u16, true, tick, false, false);
        // Client uses skill_lv to show how many Zeny were stolen
        let s = unit::get(c, src.id).unwrap_or(s);
        let t = unit::get_or_freed(c, target.id).unwrap_or(t);
        clif_skill_nodamage(c, Some(&s), &t, id, amount, true);
    }
}
