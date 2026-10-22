//! skills/merchant/aidpotion.cpp: class SkillAidPotion : public SkillImpl (AM_POTIONPITCHER).
//!
//! aidberserkpotion.cpp (AM_BERSERKPITCHER) is the same code with one more check (the target's base level against
//! the potion's equip level) and one packet condition less: both are [`potion_pitcher`] here, with those two
//! differences behind its `berserk` switch.

use crate::db::skills::*;
use crate::map::{BL_HOM, BL_PC};
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::SkillImpl;
use crate::status::consts::*;
use crate::status::hp as shp;
use crate::status::status_get_status_data;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// SkillAidPotion::SkillAidPotion() : SkillImpl(AM_POTIONPITCHER).
pub struct SkillAidPotion;

impl SkillImpl for SkillAidPotion {
    fn skill_id(&self) -> u16 {
        AM_POTIONPITCHER
    }

    /// SkillAidPotion::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, flag: &mut i32) {
        potion_pitcher(c, self.skill_id(), false, src, target, skill_lv, flag);
    }
}

/// SkillAidPotion::castendNoDamageId / SkillAidBerserkPotion::castendNoDamageId: a player throws the potion its
/// requirement names for this level (the item's script runs with potion_flag 1, recording the heal instead of doing
/// it), scaled by Potion Pitcher / Learning Potion / Alchemist Spirit, the item group and skill heal bonuses, HPlus
/// and the target's VIT / INT / recovery skills and healing penalties; a monster caster heals a fixed amount.
pub(crate) fn potion_pitcher(c: &Ctx, skill_id: u16, berserk: bool, src: &Unit, target: &Unit, skill_lv: u16, flag: &mut i32) {
    let sd = src.kind == BL_PC;
    let sstatus = status_get_status_data(c, src);
    let dstsd = target.kind == BL_PC;
    let tstatus = status_get_status_data(c, target);
    let chk = |u: &Unit, id: u16| crate::skills::novice::pc_checkskill(c, u, id);

    let mut hp: i32 = 0;
    let mut sp: i32 = 0;
    if super::is_emperium(c, target) {
        *flag |= SKILL_NOCONSUME_REQ;
        return;
    }
    if sd {
        let aid = src.id;
        let mut bonus: i32 = 100;
        let require = crate::skill::condition::skill_get_requirement(c, src, skill_id, skill_lv);
        let x = (skill_lv % 11) as usize - 1;
        let itemid = require.itemid.get(x).copied().unwrap_or(0);
        let j = crate::inventory::pc_search_inventory(c, aid, itemid);
        if j < 0 || itemid == 0 {
            skill_fail(c, src, skill_id);
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        // sd->inventory_data[j] == nullptr || sd->inventory.u.items_inventory[j].amount < require.amount[x]
        let slot = crate::inventory::inv_get(c, aid, j as usize);
        let idata = slot.as_ref().and_then(|s| crate::db::items::itemdb_exists(s.item.nameid));
        let (Some(slot), Some(idata)) = (slot, idata) else {
            skill_fail(c, src, skill_id);
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        };
        if (slot.item.amount as i32) < require.amount.get(x).copied().unwrap_or(0) {
            skill_fail(c, src, skill_id);
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        if berserk && dstsd {
            // aidberserkpotion.cpp: dstsd->status.base_level < (uint32)sd->inventory_data[j]->elv
            let blv = crate::pc::character_of(c, target.id).map(|ch| ch.base_level as u32).unwrap_or(0);
            if blv < idata.elv as u32 {
                skill_fail(c, src, skill_id);
                *flag |= SKILL_NOCONSUME_REQ;
                return;
            }
        }
        shp::set_potion_flag(1);
        shp::set_potion_hpsp(0, 0);
        shp::set_potion_per(0, 0);
        shp::set_potion_target(Some(target.id));
        // run_script(sd->inventory_data[j]->script,0,sd->id,0)
        crate::inventory::run_item_script(c, aid, slot.item.nameid, crate::inventory::ItemScript::Use, 0);
        shp::set_potion_flag(0);
        shp::set_potion_target(None);
        let (potion_hp, potion_sp, potion_per_hp, potion_per_sp) = shp::potion_values();
        let s_now = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        if let Some(e) = crate::skills::novice::sce(c, &s_now, SC_SPIRIT) {
            if e.val2 == SL_ALCHEMIST as i32 {
                bonus += crate::pc::character_of(c, aid).map(|ch| ch.base_level as i32).unwrap_or(0);
            }
        }
        let pitch = 100 + chk(src, AM_POTIONPITCHER) * 10 + chk(src, AM_LEARNINGPOTION) * 5;
        if potion_per_hp > 0 || potion_per_sp > 0 {
            // tstatus->max_hp (uint32) * potion_per_hp / 100
            hp = (tstatus.max_hp as u32).wrapping_mul(potion_per_hp as u32).wrapping_div(100) as i32;
            hp = hp.wrapping_mul(pitch).wrapping_mul(bonus) / 10000;
            if dstsd {
                // dstsd->status.max_sp
                let max_sp = crate::pc::character_of(c, target.id).map(|ch| ch.max_sp).unwrap_or(0);
                sp = (max_sp as u32).wrapping_mul(potion_per_sp as u32).wrapping_div(100) as i32;
                sp = sp.wrapping_mul(pitch).wrapping_mul(bonus) / 10000;
            }
        } else {
            if potion_hp > 0 {
                hp = potion_hp.wrapping_mul(pitch).wrapping_mul(bonus) / 10000;
                hp = hp.wrapping_mul(100 + tstatus.vit as i32 * 2) / 100;
                if dstsd {
                    hp = hp.wrapping_mul(100 + chk(target, SM_RECOVERY) * 10) / 100;
                }
            }
            if potion_sp > 0 {
                sp = potion_sp.wrapping_mul(pitch).wrapping_mul(bonus) / 10000;
                sp = sp.wrapping_mul(100 + tstatus.int_ as i32 * 2) / 100;
                if dstsd {
                    sp = sp.wrapping_mul(100 + chk(target, MG_SRECOVERY) * 10) / 100;
                }
            }
        }
        if let Some(sdst) = crate::status::data::sd_get(c, aid) {
            let b = super::pc_get_itemgroup_bonus_group(&sdst.lists.itemgrouphealrate, super::IG_POTION);
            if b != 0 {
                hp += hp.wrapping_mul(b) / 100;
            }
            let b = super::pc_get_itemgroup_bonus_group(&sdst.lists.itemgroupsphealrate, super::IG_POTION);
            if b != 0 {
                sp += sp.wrapping_mul(b) / 100;
            }
        }
        let j = crate::skill::misc::pc_skillheal_bonus(c, aid, skill_id);
        if j != 0 {
            hp += hp.wrapping_mul(j) / 100;
            sp += sp.wrapping_mul(j) / 100;
        }
    } else {
        //Maybe replace with potion_hp, but I'm unsure how that works [Playtester]
        hp = match skill_lv {
            1 => 45,
            2 => 105,
            3 => 175,
            _ => 325,
        };
        hp = (hp + (c.rnd() % (skill_lv as u32 * 20 + 1)) as i32) * (150 + skill_lv as i32 * 10) / 100;
        hp = hp.wrapping_mul(100 + tstatus.vit as i32 * 2) / 100;
        if dstsd {
            hp = hp.wrapping_mul(100 + chk(target, SM_RECOVERY) * 10) / 100;
        }
    }
    if dstsd {
        let j = crate::skill::misc::pc_skillheal2_bonus(c, target.id, skill_id);
        if j != 0 {
            hp += hp.wrapping_mul(j) / 100;
            sp += sp.wrapping_mul(j) / 100;
        }
    }
    // Final heal increased by HPlus.
    // Is this the right place for this??? [Rytech]
    // Can HPlus also affect SP recovery???
    if sd && sstatus.hplus > 0 {
        hp += hp.wrapping_mul(sstatus.hplus as i32) / 100;
        sp += sp.wrapping_mul(sstatus.hplus as i32) / 100;
    }
    let t_now = unit::get(c, target.id).unwrap_or_else(|| target.clone());
    let tsc = crate::battle::bl::sc(c, &t_now);
    if let Some(tsc) = tsc.as_ref().filter(|s| !s.empty()) {
        let mut penalty: u8 = 0;
        if tsc.get(SC_WATER_INSIGNIA).map(|e| e.val1 == 2).unwrap_or(false) {
            hp += hp / 10;
            sp += sp / 10;
        }
        if let Some(e) = tsc.get(SC_CRITICALWOUND) {
            penalty = penalty.wrapping_add(e.val2 as u8);
        }
        if tsc.get(SC_DEATHHURT).map(|e| e.val3 != 0).unwrap_or(false) {
            penalty = penalty.wrapping_add(20);
        }
        if tsc.has(SC_NORECOVER_STATE) {
            penalty = 100;
        }
        if penalty > 0 {
            hp -= hp.wrapping_mul(penalty as i32) / 100;
            sp -= sp.wrapping_mul(penalty as i32) / 100;
        }
    }
    // RENEWAL: Heal effectiveness is 3x for Homunculus
    if target.kind == BL_HOM {
        hp = hp.wrapping_mul(3);
    }
    clif_skill_nodamage(c, Some(src), &t_now, skill_id, skill_lv as i32, true);
    // aidpotion.cpp: `if( hp > 0 || sp <= 0 )`; aidberserkpotion.cpp: `if( hp > 0 )`
    if hp > 0 || (!berserk && sp <= 0) {
        clif_skill_nodamage(c, None, &t_now, AL_HEAL, hp, true);
    }
    if sp > 0 {
        clif_skill_nodamage(c, None, &t_now, MG_SRECOVERY, sp, true);
    }
    if let Some(tsc) = tsc.as_ref() {
        // RENEWAL
        if tsc.has(SC_EXTREMITYFIST) {
            sp = 0;
        }
        if tsc.has(SC_NORECOVER_STATE) {
            hp = 0;
            sp = 0;
        }
    }
    shp::status_heal(c, target.id, hp as i64, sp as i64, 0, 0);
}
