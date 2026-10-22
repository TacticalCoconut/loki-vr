//! skills/merchant/slingitem.cpp: class SkillSlingItem : public SkillImpl (GN_SLINGITEM); class SkillSlingItemAttack :
//! public WeaponSkillImpl (GN_SLINGITEM_RANGEMELEEATK).

use crate::battle::calc::Damage;
use crate::battle::{BCT_ENEMY, BF_WEAPON};
use crate::db::items::group::itemdb_group_item_exists;
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_PC};
use crate::skill::area;
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::clif::fail::USESKILL_FAIL;
use crate::skill::clif::{clif_skill_nodamage, skill_fail_cause};
use crate::skills::novice::re_lvl_dmod;
use crate::skills::SkillImpl;
use crate::status::consts::*;
use crate::status::sc;
use crate::status::{status_get_lv, status_get_status_data};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// itemdb.hpp item group ids (script constants IG_BOMB / IG_THROWABLE).
pub const IG_BOMB: u16 = 476;
pub const IG_THROWABLE: u16 = 477;
/// itemdb.hpp ITEMID_* of the Genetic's throwables.
pub const ITEMID_APPLE_BOMB: u32 = 13260;
pub const ITEMID_COCONUT_BOMB: u32 = 13261;
pub const ITEMID_MELON_BOMB: u32 = 13262;
pub const ITEMID_PINEAPPLE_BOMB: u32 = 13263;
pub const ITEMID_BANANA_BOMB: u32 = 13264;
pub const ITEMID_BLACK_LUMP: u32 = 13265;
pub const ITEMID_BLACK_HARD_LUMP: u32 = 13266;
pub const ITEMID_VERY_HARD_LUMP: u32 = 13267;
pub const ITEMID_HP_INC_POTS_TO_THROW: u32 = 13275;
pub const ITEMID_HP_INC_POTM_TO_THROW: u32 = 13276;
pub const ITEMID_HP_INC_POTL_TO_THROW: u32 = 13277;
pub const ITEMID_SP_INC_POTS_TO_THROW: u32 = 13278;
pub const ITEMID_SP_INC_POTM_TO_THROW: u32 = 13279;
pub const ITEMID_SP_INC_POTL_TO_THROW: u32 = 13280;

/// sd->itemid (the pc_items row).
fn sd_itemid(c: &Ctx, aid: u32) -> u32 {
    crate::inventory::st(c, aid).map(|s| s.itemid).unwrap_or(0)
}

fn set_sd_itemid(c: &Ctx, aid: u32, v: u32) {
    if let Some(mut s) = crate::inventory::st(c, aid) {
        s.itemid = v;
        crate::inventory::put_st(c, &s);
    }
}

/// SkillSlingItem::SkillSlingItem() : SkillImpl(GN_SLINGITEM).
pub struct SkillSlingItem;

impl SkillImpl for SkillSlingItem {
    fn skill_id(&self) -> u16 {
        GN_SLINGITEM
    }

    /// SkillSlingItem::castendNoDamageId: a player throws its equipped ammo (sd->itemid = it): a bomb at an enemy is a
    /// GN_SLINGITEM_RANGEMELEEATK weapon hit (the Pineapple Bomb hits every character on the target's cell), at a
    /// non-enemy it fails (USESKILL_FAIL for the attack skill); a throwable potion starts its research status and
    /// heals; any other throwable runs its item script on a player target. The skill packet is then sent twice ("I
    /// think it is to show the animation").
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let dstsd = target.kind == BL_PC;
        if src.kind == BL_PC {
            let aid = src.id;
            let i = crate::inventory::st(c, aid).and_then(|s| s.equip_index.get(crate::inventory::EQI_AMMO).copied()).unwrap_or(-1);
            if i < 0 {
                return; // No ammo.
            }
            let ammo_id = crate::inventory::inv_get(c, aid, i as usize).map(|r| r.item.nameid).unwrap_or(0);
            if ammo_id == 0 {
                return;
            }
            set_sd_itemid(c, aid, ammo_id);
            if itemdb_group_item_exists(IG_BOMB, ammo_id) {
                if crate::battle::target::battle_check_target(c, src, target, BCT_ENEMY) > 0 {
                    // Only attack if the target is an enemy.
                    if ammo_id == ITEMID_PINEAPPLE_BOMB {
                        let f = *flag | BCT_ENEMY | 1;
                        area::map_foreachincell(c, target.map, target.x, target.y, BL_CHAR, |c, bl| area::skill_area_sub(c, bl, src, GN_SLINGITEM_RANGEMELEEATK, skill_lv, tick, f, skill_castend_damage_id));
                    } else {
                        crate::skill::attack::skill_attack(c, BF_WEAPON, src, src, target, GN_SLINGITEM_RANGEMELEEATK, skill_lv, tick, *flag);
                    }
                } else {
                    //Otherwise, it fails, shows animation and removes items.
                    skill_fail_cause(c, src, GN_SLINGITEM_RANGEMELEEATK, USESKILL_FAIL);
                }
            } else if itemdb_group_item_exists(IG_THROWABLE, ammo_id) {
                let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                let research = |c: &Ctx, t: &Unit, type_: i32, v2: i32| {
                    sc::sc_start2(c, Some(src), t, type_, 100, 2, v2, 500000);
                };
                match ammo_id {
                    ITEMID_HP_INC_POTS_TO_THROW => {
                        // MaxHP +(500 + Thrower BaseLv * 10 / 3) and heals 1% MaxHP
                        research(c, &t, SC_PROMOTE_HEALTH_RESERCH, 1);
                        crate::status::hp::status_percent_heal(c, target.id, 1, 0, 0);
                    }
                    ITEMID_HP_INC_POTM_TO_THROW => {
                        // MaxHP +(1500 + Thrower BaseLv * 10 / 3) and heals 2% MaxHP
                        research(c, &t, SC_PROMOTE_HEALTH_RESERCH, 2);
                        crate::status::hp::status_percent_heal(c, target.id, 2, 0, 0);
                    }
                    ITEMID_HP_INC_POTL_TO_THROW => {
                        // MaxHP +(2500 + Thrower BaseLv * 10 / 3) and heals 5% MaxHP
                        research(c, &t, SC_PROMOTE_HEALTH_RESERCH, 3);
                        crate::status::hp::status_percent_heal(c, target.id, 5, 0, 0);
                    }
                    ITEMID_SP_INC_POTS_TO_THROW => {
                        // MaxSP +(Thrower BaseLv / 10 - 5)% and recovers 2% MaxSP
                        research(c, &t, SC_ENERGY_DRINK_RESERCH, 1);
                        crate::status::hp::status_percent_heal(c, target.id, 0, 2, 0);
                    }
                    ITEMID_SP_INC_POTM_TO_THROW => {
                        // MaxSP +(Thrower BaseLv / 10)% and recovers 4% MaxSP
                        research(c, &t, SC_ENERGY_DRINK_RESERCH, 2);
                        crate::status::hp::status_percent_heal(c, target.id, 0, 4, 0);
                    }
                    ITEMID_SP_INC_POTL_TO_THROW => {
                        // MaxSP +(Thrower BaseLv / 10 + 5)% and recovers 8% MaxSP
                        research(c, &t, SC_ENERGY_DRINK_RESERCH, 3);
                        crate::status::hp::status_percent_heal(c, target.id, 0, 8, 0);
                    }
                    _ => {
                        if dstsd {
                            // run_script(sd->inventory_data[i]->script, 0, dstsd->id, fake_nd->id)
                            let fake = crate::script::fake_npc_id(c);
                            crate::inventory::run_item_script(c, target.id, ammo_id, crate::inventory::ItemScript::Use, fake);
                        }
                    }
                }
            }
        }
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get_or_freed(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, true);
        clif_skill_nodamage(c, Some(&s), &t, id, skill_lv as i32, true); // This packet is received twice actually, I think it is to show the animation.
    }
}

/// SkillSlingItemAttack::SkillSlingItemAttack() : WeaponSkillImpl(GN_SLINGITEM_RANGEMELEEATK).
pub struct SkillSlingItemAttack;

impl SkillImpl for SkillSlingItemAttack {
    fn skill_id(&self) -> u16 {
        GN_SLINGITEM_RANGEMELEEATK
    }

    /// WeaponSkillImpl::castendDamageId (inherited).
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        crate::skills::weapon_castend_damage_id(c, self.skill_id(), src, target, skill_lv, tick, *flag);
    }

    /// SkillSlingItemAttack::applyAdditionalEffects: the thrown bomb's statuses (sd->itemid: Coconut stun + bleeding,
    /// Melon SC_MELON_BOMB, Banana sit-down + SC_BANANA_BOMB), then sd->itemid = 0.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        if src.kind != BL_PC {
            return;
        }
        let sstatus = status_get_status_data(c, src);
        let tstatus = status_get_status_data(c, target);
        let job = crate::pc::character_of(c, src.id).map(|ch| ch.job_level as i32).unwrap_or(0);
        let lv = skill_lv as i32;
        // Starting SCs here instead of do it in skill_additional_effect to simplify the code.
        match sd_itemid(c, src.id) {
            ITEMID_COCONUT_BOMB => {
                sc::sc_start(c, Some(src), target, SC_STUN, 5 + job / 2, lv, (1000 * job / 3) as i64);
                let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                sc::sc_start2(c, Some(src), &t, SC_BLEEDING, 3 + job / 2, lv, src.id as i32, (1000 * status_get_lv(c, src) / 4 + job / 3) as i64);
            }
            ITEMID_MELON_BOMB => {
                sc::sc_start4(c, Some(src), target, SC_MELON_BOMB, 100, lv, 20 + job, 10 + job / 2, 0, (1000 * status_get_lv(c, src) / 4) as i64);
            }
            ITEMID_BANANA_BOMB => {
                let conf = crate::script::battle_flags::battle_get_value("banana_bomb_duration");
                let duration = (if conf != 0 { conf } else { 1000 * job / 4 }) as u16;
                let rate = status_get_lv(c, src) + job + sstatus.dex as i32 / 6 - status_get_lv(c, target) - tstatus.agi as i32 / 4 - tstatus.luk as i32 / 5;
                sc::sc_start(c, Some(src), target, SC_BANANA_BOMB_SITDOWN, rate, lv, duration as i64);
                let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
                sc::sc_start(c, Some(src), &t, SC_BANANA_BOMB, 100, lv, 30000);
            }
            _ => {}
        }
        set_sd_itemid(c, src.id, 0);
    }

    /// SkillSlingItemAttack::calculateSkillRatio: by the thrown item (sd->itemid), RE_LVL_DMOD(100) for players.
    fn calculate_skill_ratio(&self, c: &Ctx, _wd: &Damage, src: &Unit, _target: &Unit, _skill_lv: u16, skillratio: &mut i32, _mflag: i32) {
        if src.kind != BL_PC {
            return;
        }
        let s = status_get_status_data(c, src);
        let (str_, agi, dex) = (s.str as i32, s.agi as i32, s.dex as i32);
        match sd_itemid(c, src.id) {
            ITEMID_APPLE_BOMB => *skillratio += 200 + str_ + dex,
            ITEMID_COCONUT_BOMB | ITEMID_PINEAPPLE_BOMB => *skillratio += 700 + str_ + dex,
            ITEMID_MELON_BOMB => *skillratio += 400 + str_ + dex,
            ITEMID_BANANA_BOMB => *skillratio += 777 + str_ + dex,
            ITEMID_BLACK_LUMP => *skillratio += -100 + (str_ + agi + dex) / 3,
            ITEMID_BLACK_HARD_LUMP => *skillratio += -100 + (str_ + agi + dex) / 2,
            ITEMID_VERY_HARD_LUMP => *skillratio += -100 + str_ + agi + dex,
            _ => {}
        }
        re_lvl_dmod(c, src, skillratio, 100);
    }
}

#[cfg(test)]
mod tests {
    #[test]
    fn constants_match() {
        assert_eq!(crate::data::constant("IG_BOMB"), Some(super::IG_BOMB as i64));
        assert_eq!(crate::data::constant("IG_THROWABLE"), Some(super::IG_THROWABLE as i64));
        let item = |id: u32| crate::db::items::itemdb_exists(id).map(|d| d.name.clone()).unwrap_or_default();
        for (id, n) in [
            (super::ITEMID_APPLE_BOMB, "Apple_Bomb"),
            (super::ITEMID_COCONUT_BOMB, "Coconut_Bomb"),
            (super::ITEMID_MELON_BOMB, "Melon_Bomb"),
            (super::ITEMID_PINEAPPLE_BOMB, "Pineapple_Bomb"),
            (super::ITEMID_BANANA_BOMB, "Banana_Bomb"),
            (super::ITEMID_BLACK_LUMP, "Black_Lump"),
            (super::ITEMID_BLACK_HARD_LUMP, "Black_Hard_Lump"),
            (super::ITEMID_VERY_HARD_LUMP, "Very_Hard_Lump"),
            (super::ITEMID_HP_INC_POTS_TO_THROW, "HP_Inc_PotS_To_Throw"),
            (super::ITEMID_SP_INC_POTL_TO_THROW, "SP_Inc_PotL_To_Throw"),
        ] {
            assert_eq!(item(id), n, "item {id}");
        }
    }
}
