//! Damage, healing, death and rewards of monsters (mob.cpp): mob_log_damage, mob_damage, mob_heal, mob_revive,
//! mob_dead (EXP distribution with party_exp_share, zeny, drops with first-loot priority, looted items, map drops,
//! MVP reward, kill events, quest objectives), mob_getdroprate, mob_data::get_mvp_player, the delayed drops
//! (mob_delay_item_drop), MVP tombs (mvptomb_create / destroy / delayspawn, npc.cpp run_tomb), mob_respawn and
//! mob_class_change; and the BL_MOB side of status.cpp status_damage / status_heal / status_revive.

use spacetimedb::{table, SpacetimeType, Table};

use super::{conf, get, put, DmgLog, MobData, DAMAGELOG_SIZE, MDLF_HOMUN, MDLF_NORMAL, MDLF_PET, MDLF_SELF};
use crate::db::mobs::{self, BossType};
use crate::inventory::Item;
use crate::map::{self, BL_CHAR, BL_ELEM, BL_HOM, BL_MER, BL_MOB, BL_PC, BL_PET};
use crate::status::consts::*;
use crate::status::sc;
use crate::timer::{self, Func};
use crate::unit::{self, Unit, CLR_DEAD, USW_FIXPOS, USW_RELEASE_TARGET};
use crate::world::{Ctx, Tick};

/// mmo.hpp MAX_EXP.
const MAX_EXP: u64 = i64::MAX as u64;

// ---------------------------------------------------------------------------------------------- damage log

/// mob.cpp mob_log_damage: adds damage (and damage tanked from the monster's normal attacks) to the damage log of
/// the character behind `src` (a player, a companion's or a special-AI summon's master; the monster itself for self
/// damage).
pub fn mob_log_damage(c: &Ctx, md_id: u32, src: &Unit, damage: i64, damage_tanked: i64) {
    let Some(mut md) = get(c, md_id) else { return };
    if damage < 0 {
        return; // Do nothing for absorbed damage.
    }
    if damage_tanked < 0 {
        return; // Just to make sure we don't subtract it (should not happen)
    }
    if damage == 0 && damage_tanked == 0 && src.kind & super::default_enemy_type(&md) == 0 {
        return; // Do not log non-damaging effects from non-enemies.
    }
    let mut char_id: u32 = 0;
    let mut flag = MDLF_NORMAL;
    match src.kind {
        BL_PC => char_id = src.char_id,
        BL_HOM => {
            flag = MDLF_HOMUN;
            // if( hd->master ) char_id = hd->master->status.char_id (homunculus.rs)
            char_id = crate::homunculus::mob_log_damage_char_id(c, src.id);
        }
        BL_MER => {
            // mer->master->status.char_id (mercenary/data.rs)
            if let Some(p) = crate::companion::master_of(c, src.id).and_then(|m| crate::pc::get(c, m)) {
                char_id = p.char_id;
            }
        }
        BL_PET => {
            flag = MDLF_PET;
            // pd->master->status.char_id (pet/data.rs)
            if let Some(pd) = crate::pet::data::get(c, src.id) {
                if let Some(p) = crate::pc::get(c, pd.master) {
                    char_id = p.char_id;
                }
            }
        }
        BL_MOB => {
            if let Some(md2) = get(c, src.id) {
                if md2.special_ai != 0 && md2.master_id != 0 {
                    if let Some(msd) = unit::get(c, md2.master_id).filter(|u| u.kind == BL_PC) {
                        char_id = msd.char_id;
                    }
                }
            }
        }
        BL_ELEM => {
            // ele->master->status.char_id (elemental/data.rs)
            if let Some(p) = crate::companion::master_of(c, src.id).and_then(|m| crate::pc::get(c, m)) {
                char_id = p.char_id;
            }
        }
        _ => {}       // For all unhandled types.
    }
    // Self damage increases tap bonus
    if char_id == 0 && src.id == md.id && damage > 0 {
        char_id = src.id;
        flag = MDLF_SELF;
    }
    if char_id == 0 {
        return;
    }
    if damage > 0 || damage_tanked > 0 {
        if let Some(sd) = crate::pc::charid2sd(c, char_id) {
            if let Some(mut pu) = unit::get(c, sd.id) {
                unit::mark_pc_combat(c, &mut pu, c.tick());
                unit::save(c, &pu);
            }
        }
    }
    // Check if the character is already in damage log
    if let Some(e) = md.dmglog.iter_mut().find(|e| e.id == char_id && e.flag == flag) {
        // Just add damage to it
        e.dmg = e.dmg.saturating_add(damage);
        e.dmg_tanked = e.dmg_tanked.saturating_add(damage_tanked);
        put(c, &md);
        return;
    }
    // Check if the damage log is full
    if md.dmglog.len() == DAMAGELOG_SIZE {
        md.dmglog.remove(0); // Remove oldest entry
    }
    // Add new character to damage log at last (newest) position
    md.dmglog.push(DmgLog { id: char_id, flag, dmg: damage, dmg_tanked: damage_tanked });
    put(c, &md);
}

/// mob.cpp mob_damage: logs the damage and shows the HP bar to everyone in the damage log within AREA_SIZE.
pub fn mob_damage(c: &Ctx, md_id: u32, src: Option<&Unit>, damage: i32) {
    if let Some(s) = src {
        // Store total damage...
        mob_log_damage(c, md_id, s, damage as i64, 0);
    }
    let Some(md) = get(c, md_id) else { return };
    let Some(u) = unit::get(c, md_id) else { return };
    if super::show_mob_info() & 3 != 0 {
        crate::clif::name_area(c, &u);
    }
    if crate::conf::monster_hp_bars_info() && !crate::npc::mapflag(c, u.map, crate::npc::MF_HIDEMOBHPBAR) {
        if md.special_ai == super::AI_ABR || md.special_ai == super::AI_BIONIC {
            super::clif::clif_summon_hp_bar(c, &md, &u);
        }
        // Must show hp bar to all char who already hit the mob.
        for entry in &md.dmglog {
            let Some(sd) = crate::pc::charid2sd(c, entry.id) else { continue };
            if !super::unit::check_distance_bl(&u, &sd, crate::conf::area_size() as i32) {
                continue;
            }
            super::clif::monster_hp_bar(c, &md, &sd);
        }
    }
}

/// mob.cpp mob_heal: the name / HP bar updates after a heal.
pub fn mob_heal(c: &Ctx, md_id: u32, _heal: u32) {
    let Some(md) = get(c, md_id) else { return };
    let Some(u) = unit::get(c, md_id) else { return };
    if super::show_mob_info() & 3 != 0 {
        crate::clif::name_area(c, &u);
    }
    if crate::conf::monster_hp_bars_info() && !crate::npc::mapflag(c, u.map, crate::npc::MF_HIDEMOBHPBAR) {
        if md.special_ai == super::AI_ABR || md.special_ai == super::AI_BIONIC {
            super::clif::clif_summon_hp_bar(c, &md, &u);
        }
        // Must show hp bar to all char who already hit the mob.
        for entry in &md.dmglog {
            if let Some(sd) = crate::pc::charid2sd(c, entry.id) {
                // Check if in range
                if super::unit::check_distance_bl(&u, &sd, crate::conf::area_size() as i32) {
                    super::clif::monster_hp_bar(c, &md, &sd);
                }
            }
        }
    }
}

// ---------------------------------------------------------------------------------------------- status_damage

/// status.cpp status_damage from `status = status_get_status_data(*target)` on, for a monster target. `dhp` is the
/// caller's original HP damage (for the damage log), `hp` / `sp` / `ap` the values after the absorbed-damage and
/// non-SP-target adjustments the shared prefix made. Returns hp + sp + ap.
#[allow(clippy::too_many_arguments)]
pub fn status_damage_mob(c: &Ctx, src: Option<&Unit>, target: u32, dhp: i64, hp: i32, sp: i32, ap: i32, walkdelay: Tick, flag: i32, skill_id: u16) -> i32 {
    let (mut hp, mut sp, mut ap, mut flag) = (hp, sp, ap, flag);
    let Some(md) = get(c, target) else { return 0 };
    let Some(tu) = unit::get(c, target) else { return 0 };
    let status = md.status.clone();
    if hp as u32 >= status.hp {
        if flag & 2 != 0 {
            return 0;
        }
        hp = status.hp as i32;
    }
    if sp as u32 > status.sp {
        if flag & 2 != 0 {
            return 0;
        }
        sp = status.sp as i32;
    }
    if ap as u32 > status.ap {
        if flag & 2 != 0 {
            return 0;
        }
        ap = status.ap as i32;
    }
    // If no damage is dealt and the damage was passive
    if hp == 0 && sp == 0 && ap == 0 && flag & 1 != 0 {
        return 0;
    }
    if status.hp == 0 {
        flag |= 8;
    }
    let scl = sc::load(c, target);
    if hp != 0 && crate::conf::invincible_nodamage() && src.is_some() && scl.has(SC_INVINCIBLE) {
        hp = 1;
    }
    // If the damage is not passive
    if flag & 1 == 0 {
        if !scl.empty() {
            for s in crate::db::misc::status::status_db().iter() {
                let t = s.type_;
                // For non-players, Wink Charm, Voice of Siren and Deep Sleep end only when damage was dealt
                if (t == SC_WINKCHARM || t == SC_VOICEOFSIREN || t == SC_DEEPSLEEP) && hp == 0 {
                    continue;
                }
                if sc::has_sce(c, target, t) && s.has_flag(SCF_REMOVEONDAMAGED) {
                    // A status change that gets broken by damage should still be considered when calculating if a
                    // status change can be applied or not (for the same attack).
                    let mut us = sc::unit_sc(c, target);
                    if unit::get(c, target).map(|u| u.sc.opt1 > crate::status::defs::OPT1_NONE).unwrap_or(false) && us.last_effect_timer == 0 {
                        us.last_effect_timer = timer::add(c, c.tick() + 10, Func::StatusClearLastEffect, target, 0);
                        us.last_effect = t;
                        sc::put_unit_sc(c, &us);
                    }
                    sc::status_change_end(c, target, t, 0);
                }
            }
            if let Some(mut sce) = sc::get_sce(c, target, SC_ENDURE) {
                if sce.val4 == 0 {
                    // Endure count is only reduced by non-players on non-gvg maps. val4 signals infinite endure.
                    if let Some(s) = src {
                        if s.kind != BL_PC && !crate::status::map_flag_gvg2(c, tu.map) && !crate::npc::mapflag(c, tu.map, crate::npc::MF_BATTLEGROUND) {
                            sce.val2 -= 1;
                            sc::put_sce(c, &sce);
                            if sce.val2 <= 0 {
                                sc::status_change_end(c, target, SC_ENDURE, 0);
                            }
                        }
                    }
                }
            }
            if sc::has_sce(c, target, SC_DANCING) && hp as u32 > status.max_hp / 4 {
                sc::status_change_end(c, target, SC_DANCING, 0);
            }
            if let Some(mut sce) = sc::get_sce(c, target, SC_CLOAKINGEXCEED) {
                sce.val2 -= 1;
                sc::put_sce(c, &sce);
                if sce.val2 <= 0 {
                    sc::status_change_end(c, target, SC_CLOAKINGEXCEED, 0);
                }
            }
            if let Some(mut sce) = sc::get_sce(c, target, SC_KAGEMUSYA) {
                sce.val3 -= 1;
                sc::put_sce(c, &sce);
                if sce.val3 <= 0 {
                    sc::status_change_end(c, target, SC_KAGEMUSYA, 0);
                }
            }
        }
        crate::skill::unit::unit_skillcastcancel(c, target, 2);
        // Remember last time the unit was hit by a source
        if src.is_some() {
            if let Some(mut u) = unit::get(c, target) {
                u.ud.dmg_tick = c.tick();
                unit::save(c, &u);
            }
        }
    }
    // (exp_calc_type 1 logs the real damage: exp_calc_type is 0)
    let mut md = get(c, target).unwrap_or(md);
    md.status.hp = md.status.hp.wrapping_sub(hp as u32);
    md.status.sp = md.status.sp.wrapping_sub(sp as u32);
    md.status.ap = md.status.ap.wrapping_sub(ap as u32);
    // Coma
    if flag & 16 != 0 {
        md.status.hp = 1;
        md.status.sp = 1;
        if sp == 0 {
            sp = 1; // To make sure the status bar is updated
        }
    }
    put(c, &md);
    let new_hp = md.status.hp;
    if hp != 0 && new_hp != 0 {
        if sc::has_sce(c, target, SC_BERSERK) && new_hp <= 100 {
            sc::status_change_end(c, target, SC_BERSERK, 0);
        }
        if sc::has_sce(c, target, SC_RAISINGDRAGON) && new_hp <= 1000 {
            sc::status_change_end(c, target, SC_RAISINGDRAGON, 0);
        }
        if sc::has_sce(c, target, SC_SATURDAYNIGHTFEVER) && new_hp <= 100 {
            sc::status_change_end(c, target, SC_SATURDAYNIGHTFEVER, 0);
        }
    }
    // Need to pass original HP damage for the mob damage log
    let dhp = dhp.clamp(i32::MIN as i64, i32::MAX as i64);
    mob_damage(c, target, src, dhp as i32);
    // Normal attack damage is logged in the monster's dmglog as attack damage
    if let Some(s) = src {
        if s.kind == BL_MOB && skill_id == 0 {
            if let Some(tu) = unit::get(c, target) {
                mob_log_damage(c, s.id, &tu, 0, dhp);
            }
        }
    }
    let status_hp = get(c, target).map(|m| m.status.hp).unwrap_or(0);
    if status_hp != 0 || flag & 8 != 0 {
        // Still lives or has been dead before this damage.
        if walkdelay > 0 && !crate::status::status_isendure(c, target, c.tick(), false) {
            super::battle_iface::unit_set_walkdelay(c, target, c.tick(), walkdelay, 0);
        }
        return hp + sp + ap;
    }
    if let Some(mut md) = get(c, target) {
        md.status.hp = 0;
        put(c, &md);
    }
    // NOTE: These dead functions should return: 0: Death cancelled, auto-revived. Non-zero: Standard death. Clear
    // status, cancel move/attack, etc. &2: Remove object from map. &4: Delete object from memory.
    let dflag = mob_dead(c, target, src, if flag & 4 != 0 { 3 } else { 0 });
    if dflag == 0 {
        return hp + sp + ap; // Death cancelled.
    }
    // Normal death: clear_unit_ondeath (skillunit); monsters have no regen data
    if crate::skillunit::conf::clear_unit_ondeath() & tu.kind != 0 {
        crate::skillunit::skill_clear_unitgroup(c, target);
    }
    let scl = sc::load(c, target);
    let tmap = unit::get(c, target).map(|u| u.map).unwrap_or(tu.map);
    if scl.has(SC_KAIZEL) && !crate::status::map_flag_gvg2(c, tmap) {
        // flag&8 = disable Kaizel
        let time = crate::db::skills::skill_get_time2(crate::db::skills::ids::SL_KAIZEL, scl.v1(SC_KAIZEL) as u16) as i64;
        crate::status::hp::status_revive(c, target, scl.v2(SC_KAIZEL) as u8, 0, 0);
        sc::status_change_clear(c, target, 0);
        if let Some(t) = unit::get(c, target) {
            crate::skill::clif::clif_skill_nodamage(c, Some(&t), &t, crate::db::skills::ids::ALL_RESURRECTION, 1, true);
        }
        if let Some(t) = unit::get(c, target) {
            sc::sc_start(c, src, &t, SC_KYRIE, 100, 10, time);
        }
        if let Some(mut md) = get(c, target) {
            md.rebirth = true;
            put(c, &md);
        }
        return hp + sp + ap;
    }
    // Disable Ultimate Sacrifice on GVG maps
    if scl.has(SC_ULTIMATE_S) && !crate::status::map_flag_gvg2(c, tmap) {
        crate::status::hp::status_revive(c, target, 100, 100, 0);
        sc::status_change_clear(c, target, 0);
        if let Some(t) = unit::get(c, target) {
            crate::skill::clif::clif_skill_nodamage(c, Some(&t), &t, crate::db::skills::ids::ALL_RESURRECTION, 1, true);
        }
        if let Some(mut md) = get(c, target) {
            md.rebirth = true;
            put(c, &md);
        }
        return hp + sp + ap;
    }
    if scl.has(SC_REBIRTH) && !get(c, target).map(|m| m.rebirth).unwrap_or(true) {
        // Ensure the monster has not already rebirthed before doing so.
        crate::status::hp::status_revive(c, target, scl.v2(SC_REBIRTH) as u8, 0, 0);
        sc::status_change_clear(c, target, 0);
        if let Some(mut md) = get(c, target) {
            md.rebirth = true;
            put(c, &md);
        }
        return hp + sp + ap;
    }
    sc::status_change_clear(c, target, 0);
    if dflag & 4 != 0 {
        // Delete from memory. (also invokes map removal code)
        super::unit::unit_free(c, target, CLR_DEAD);
    } else if dflag & 2 != 0 {
        // remove from map
        super::unit::unit_remove_map(c, target, CLR_DEAD);
    } else if let Some(mut u) = unit::get(c, target) {
        // Some death states that would normally be handled by unit_remove_map
        unit::stop_attack(c, &mut u);
        unit::stop_walking(c, &mut u, USW_FIXPOS | USW_RELEASE_TARGET, 0);
        unit::save(c, &u);
        crate::skill::unit::unit_skillcastcancel(c, target, 0);
        if let Some(u) = unit::get(c, target) {
            crate::clif::clearunit_area(c, &u, CLR_DEAD);
        }
        crate::skillunit::skill_unit_move_id(c, target, c.tick(), 4);
        if let Some(u) = unit::get(c, target) {
            crate::skill::timerskill::skill_cleartimerskill(c, &u);
        }
    }
    hp + sp + ap
}

/// status.cpp status_heal from `status = status_get_status_data(*bl)` on, for a monster. Returns hp + sp + ap.
pub fn status_heal_mob(c: &Ctx, bl: u32, hhp: i64, hsp: i64, hap: i64, flag: i32) -> i32 {
    let mut hp = hhp.clamp(i32::MIN as i64, i32::MAX as i64) as i32;
    let mut sp = hsp.clamp(i32::MIN as i64, i32::MAX as i64) as i32;
    let mut ap = hap.clamp(i32::MIN as i64, i32::MAX as i64) as i32;
    let mut flag = flag;
    let Some(md) = get(c, bl) else { return 0 };
    if md.status.hp == 0 {
        return 0;
    }
    let scl = sc::load(c, bl);
    let has_sc = !scl.empty();
    if hp < 0 {
        if hp == i32::MIN {
            hp += 1; // -INT_MIN == INT_MIN in some architectures!
        }
        crate::status::hp::status_damage(c, None, bl, -(hp as i64), 0, 0, 1, 0, 0);
        hp = 0;
    }
    let status = get(c, bl).map(|m| m.status).unwrap_or(md.status.clone());
    if hp != 0 {
        if has_sc && scl.has(SC_BERSERK) {
            if flag & 1 != 0 {
                flag &= !2;
            } else {
                hp = 0;
            }
        }
        if hp as u32 > status.max_hp.wrapping_sub(status.hp) {
            hp = status.max_hp.wrapping_sub(status.hp) as i32;
        }
    }
    if sp < 0 {
        if sp == i32::MIN {
            sp += 1;
        }
        crate::status::hp::status_damage(c, None, bl, 0, -(sp as i64), 0, 1, 0, 0);
        sp = 0;
    }
    if sp != 0 && sp as u32 > status.max_sp.wrapping_sub(status.sp) {
        sp = status.max_sp.wrapping_sub(status.sp) as i32;
    }
    if ap < 0 {
        if ap == i32::MIN {
            ap += 1;
        }
        crate::status::hp::status_damage(c, None, bl, 0, 0, -(ap as i64), 0, 1, 0);
        ap = 0;
    }
    if ap != 0 && ap as u32 > status.max_ap.wrapping_sub(status.ap) {
        ap = status.max_ap.wrapping_sub(status.ap) as i32;
    }
    if ap == 0 && sp == 0 && hp == 0 && flag & 4 == 0 {
        return 0;
    }
    let Some(mut md) = get(c, bl) else { return 0 };
    md.status.hp = md.status.hp.wrapping_add(hp as u32);
    md.status.sp = md.status.sp.wrapping_add(sp as u32);
    md.status.ap = md.status.ap.wrapping_add(ap as u32);
    put(c, &md);
    // Send HP update to client
    mob_heal(c, bl, hp as u32);
    hp + sp + ap
}

/// status.cpp status_revive for a monster (the caller checked it is dead): HP / SP / AP by percentage, the
/// resurrection animation when on a map, then mob_revive.
pub fn status_revive_mob(c: &Ctx, bl: u32, per_hp: u8, per_sp: u8, per_ap: u8) -> i32 {
    let Some(mut md) = get(c, bl) else { return 0 };
    let st = md.status.clone();
    let mut hp = (st.max_hp as i64 * per_hp as i64 / 100) as u32;
    let mut sp = (st.max_sp as i64 * per_sp as i64 / 100) as u32;
    let mut ap = (st.max_ap as i64 * per_ap as i64 / 100) as u32;
    if hp > st.max_hp.wrapping_sub(st.hp) {
        hp = st.max_hp.wrapping_sub(st.hp);
    } else if per_hp != 0 && hp == 0 {
        hp = 1;
    }
    if sp > st.max_sp.wrapping_sub(st.sp) {
        sp = st.max_sp.wrapping_sub(st.sp);
    } else if per_sp != 0 && sp == 0 {
        sp = 1;
    }
    if ap > st.max_ap.wrapping_sub(st.ap) {
        ap = st.max_ap.wrapping_sub(st.ap);
    } else if per_ap != 0 && ap == 0 {
        ap = 1;
    }
    md.status.hp = md.status.hp.wrapping_add(hp);
    md.status.sp = md.status.sp.wrapping_add(sp);
    md.status.ap = md.status.ap.wrapping_add(ap);
    put(c, &md);
    if let Some(u) = unit::get(c, bl) {
        if u.on_map {
            // Animation only if character is already on a map.
            crate::status::clif::resurrection(c, &u);
        }
    }
    mob_revive(c, bl, hp);
    1
}

/// mob.cpp mob_revive: resurrects a monster in place (the damage already lost counts as self damage, so nobody
/// gets EXP for it).
pub fn mob_revive(c: &Ctx, id: u32, hp: u32) {
    let tick = c.tick();
    let Some(mut md) = get(c, id) else { return };
    super::mob_setstate(&mut md, super::MSS_IDLE);
    md.next_walktime = tick + (c.rnd() % 1000) as i64 + super::MIN_RANDOMWALKTIME;
    md.last_linktime = tick;
    md.last_pcneartime = 0;
    // We reset the damage log and then set the already lost damage as self damage so players don't get exp for it
    md.dmglog.clear();
    put(c, &md);
    if let Some(self_u) = unit::get(c, id) {
        mob_log_damage(c, id, &self_u, md.status.max_hp as i64 - hp as i64, 0);
    }
    let Some(mut u) = unit::get(c, id) else { return };
    if !u.on_map && !unit::addblock(c, &mut u) {
        return;
    }
    let u = unit::get(c, id).unwrap_or(u);
    crate::clif::spawn(c, &u, false);
    crate::skillunit::skill_unit_move_id(c, id, tick, 1);
    super::skill::mobskill_use(c, id, tick, mobs::MSC_SPAWN as i32, 0); // mob.cpp:3677
    let u = unit::get(c, id).unwrap_or(u);
    if super::show_mob_info() & 3 != 0 {
        crate::clif::name_area(c, &u);
    }
}

/// mob.cpp mob_respawn (timer: KAIZEL / NPC_REBIRTH revivals, phase 4 skills set it): status_revive(bl, data, 0).
pub fn mob_respawn(c: &Ctx, id: u32, data: i64) {
    if unit::get(c, id).is_none() {
        return;
    }
    crate::status::hp::status_revive(c, id, data as u8, 0, 0);
}

// ---------------------------------------------------------------------------------------------- drops

/// mob.hpp struct s_item_drop.
#[derive(SpacetimeType, Clone, Debug, PartialEq)]
pub struct ItemDrop {
    pub item: Item,
    pub mob_id: u16,
}

/// mob.hpp struct s_item_drop_list: mob_delayed_drops / mob_looted_drops, keyed by the monster's id (and whether
/// they are looted items), waiting for mob_delay_item_drop.
#[table(accessor = mob_drop_list)]
#[derive(Clone, Debug)]
pub struct MobDropList {
    /// md->id << 1 | loot.
    #[primary_key]
    pub key: u64,
    pub m: u16,
    pub x: i16,
    pub y: i16,
    pub first_charid: u32,
    pub second_charid: u32,
    pub third_charid: u32,
    pub items: Vec<ItemDrop>,
}

impl MobDropList {
    fn new(md: &Unit, first: u32, second: u32, third: u32) -> MobDropList {
        MobDropList { key: 0, m: md.map, x: md.x, y: md.y, first_charid: first, second_charid: second, third_charid: third, items: Vec::new() }
    }
}

fn put_drop_list(c: &Ctx, id: u32, loot: bool, mut list: MobDropList) {
    list.key = (id as u64) << 1 | loot as u64;
    if c.db().mob_drop_list().key().find(list.key).is_some() {
        c.db().mob_drop_list().key().update(list);
    } else {
        c.db().mob_drop_list().insert(list);
    }
}

/// mob.cpp mob_setdropitem: a drop of `qty` from a database / map / bonus drop entry (random options applied).
fn mob_setdropitem(c: &Ctx, nameid: u32, randomopt_group: u16, qty: i16, mob_id: u16) -> ItemDrop {
    let mut item = Item::new(nameid);
    item.amount = qty;
    item.identify = crate::db::items::itemdb_isidentified(nameid) as u8;
    mob_setdropitem_option(c, &mut item, randomopt_group);
    ItemDrop { item, mob_id }
}

/// mob.cpp mob_setdropitem_option: the drop's random option group, if any.
fn mob_setdropitem_option(c: &Ctx, item: &mut Item, randomopt_group: u16) {
    if let Some(g) = crate::db::items::randomopt::random_option_group(randomopt_group) {
        let mut rnd = |a: i32, b: i32| c.rnd_value(a, b);
        let opts = g.apply(&mut rnd);
        item.option = opts.iter().map(|o| crate::inventory::ItemOption { id: o.id, value: o.value, param: o.param as u8 }).collect();
    }
}

/// mob.cpp mob_item_drop: queues a drop, unless the first / second / third attacker online autoloots it (the drop
/// rate is at most sd->state.autoloot, or the item / its type is on the @alootid / @aloottype lists — atcmd agent's
/// atcmd_pc.rs): then party_share_loot gives it to the looter (or its party) at once, with the special item
/// announcement for items flagged BroadCast. `flag`: killed only by a homunculus / mercenary. `md_mob_id` / `md_m`:
/// the md->mob_id and md->m the item log reads.
fn mob_item_drop(c: &Ctx, md_mob_id: i32, md_m: u16, dlist: &mut MobDropList, mut ditem: ItemDrop, loot: bool, drop_rate: i32, flag: bool) {
    //Logs items, dropped by mobs [Lupus]
    crate::map_log::log_pick_mob(c, md_mob_id, md_m, if loot { crate::map_log::LOG_TYPE_LOOT } else { crate::map_log::LOG_TYPE_PICKDROP_MONSTER }, -(ditem.item.amount as i32), &ditem.item);
    let sd = [dlist.first_charid, dlist.second_charid, dlist.third_charid].iter().find_map(|&cid| crate::pc::charid2sd(c, cid));
    let test_autoloot = sd.as_ref().map(|sd| {
        (drop_rate <= crate::pc_misc::pcm(c, sd.id).autoloot || crate::atcmd_pc::pc_isautolooting(c, sd.id, ditem.item.nameid))
            && if flag {
                // (homunculus_autoloot ? (hom_idle_no_share == 0 || !pc_isidle_hom(sd)) : 0) ||
                // (mercenary_autoloot ? (mer_idle_no_share == 0 || !pc_isidle_mer(sd)) : 0): both switches are off
                // (homunc.conf, drops.conf), so a kill by a homunculus / mercenary alone never autoloots
                // (pc_isidle_hom / pc_isidle_mer: phase 7)
                conf::homunculus_autoloot() || conf::mercenary_autoloot()
            } else {
                conf::idle_no_autoloot() == 0 || crate::pc::last_tick(c) - crate::pc::get(c, sd.id).map(|p| p.idletime).unwrap_or(0) < conf::idle_no_autoloot()
            }
    });
    // (AUTOLOOT_DISTANCE is not defined: mob.hpp keeps it commented out)
    if let (Some(sd), Some(true)) = (sd, test_autoloot) {
        // Autoloot.
        let party_id = crate::party::party_id_of(c, sd.id);
        let p = crate::party::party_search(c, party_id);
        if crate::db::items::itemdb_search(ditem.item.nameid).flag.broadcast && p.as_ref().map(|p| p.party.item & 2 == 0).unwrap_or(true) {
            // Somehow, if party's pickup distribution is 'Even Share', no announcemet
            crate::atcmd_pc::intif_broadcast_obtain_special_item(c, sd.id, ditem.item.nameid, ditem.mob_id as u32, crate::atcmd_pc::ITEMOBTAIN_TYPE_MONSTER_ITEM);
        }
        if crate::flooritem::party_share_loot(c, party_id, sd.id, &mut ditem.item, sd.char_id) == 0 {
            return;
        }
    }
    dlist.items.push(ditem);
}

/// mob.cpp mob_process_drop_list: every item of the list falls; the first regular drop at the centre, looted ones
/// starting north, then SE -> W -> N around it.
fn mob_process_drop_list(c: &Ctx, list: &MobDropList, loot: bool) {
    // First regular drop always drops at center; looted drops start north instead
    let mut dir: i8 = if loot { crate::path::DIR_NORTH as i8 } else { crate::path::DIR_CENTER };
    for ditem in &list.items {
        crate::flooritem::map_addflooritem(c, &ditem.item, ditem.item.amount as i32, list.m, list.x, list.y, list.first_charid, list.second_charid, list.third_charid, 4, ditem.mob_id, !loot, Some(dir), BL_CHAR | BL_PET);
        // The drop location loops between three locations: SE -> W -> N -> SE
        dir = if dir <= crate::path::DIR_NORTH as i8 {
            crate::path::DIR_SOUTHEAST as i8
        } else if dir == crate::path::DIR_SOUTHEAST as i8 {
            crate::path::DIR_WEST as i8
        } else {
            crate::path::DIR_NORTH as i8
        };
    }
}

/// mob.cpp mob_delay_item_drop (timer, id = the monster's id): its regular drops, then its looted items.
pub fn mob_delay_item_drop(c: &Ctx, id: u32) {
    for loot in [false, true] {
        let key = (id as u64) << 1 | loot as u64;
        if let Some(list) = c.db().mob_drop_list().key().find(key) {
            mob_process_drop_list(c, &list, loot);
            c.db().mob_drop_list().key().delete(key);
        }
    }
}

/// mob.cpp mob_getdroprate: a drop rate after size, LUK, the killer's bonuses and cap, the RENEWAL_DROP level
/// penalty, and the 100% / minimum caps. `factor` scales the multiplicative bonuses and caps (10 for map drops).
pub fn mob_getdroprate(c: &Ctx, src: Option<&Unit>, mob: &mobs::MobDb, base_rate: i32, drop_modifier: i32, md: Option<&MobData>, factor: i32) -> i32 {
    let mut drop_rate = base_rate;
    if let Some(md) = md {
        if conf::mob_size_influence() {
            // Change drops depending on monsters size [Valaris]
            let size = md.special_size as i32;
            if size == crate::status::defs::SZ_MEDIUM && drop_rate >= 2 {
                drop_rate /= 2; // SZ_MEDIUM actually is small size modification... this is not a bug!
            } else if size == crate::status::defs::SZ_BIG {
                drop_rate *= 2;
            }
        }
    }
    if let Some(src) = src {
        let luk = crate::status::status_get_status_data(c, src).luk as i32;
        if conf::drops_by_luk() != 0 {
            // Drops affected by luk as a fixed increase [Valaris]
            drop_rate += (luk * conf::drops_by_luk() / 100) * factor;
        }
        if conf::drops_by_luk2() != 0 {
            // Drops affected by luk as a % increase [Skotlex]
            drop_rate += (0.5 + drop_rate as f64 * luk as f64 * conf::drops_by_luk2() as f64 / 10000.0) as i32 * factor;
        }
        if src.kind == BL_PC {
            // Player specific drop rate adjustments
            let mut drop_rate_bonus = 100;
            // In PK mode players get an additional drop chance bonus of 25% if there is a 20 level difference
            let base_level = crate::status::status_get_lv(c, src);
            if crate::conf::pk_mode() != 0 && mob.lv as i32 - base_level >= 20 {
                drop_rate_bonus += 25;
            }
            // Add class and race specific bonuses
            if let Some(sd) = crate::status::data::sd_get(c, src.id) {
                let ib = &sd.indexed_bonus;
                let at = |v: &Vec<i32>, i: usize| v.get(i).copied().unwrap_or(0);
                drop_rate_bonus += at(&ib.dropaddclass, mob.status.class_ as usize) + at(&ib.dropaddclass, crate::status::defs::CLASS_ALL as usize);
                drop_rate_bonus += at(&ib.dropaddrace, mob.status.race as usize) + at(&ib.dropaddrace, crate::status::defs::RC_ALL as usize);
            }
            if let Some(e) = sc::get_sce(c, src.id, SC_ITEMBOOST) {
                drop_rate_bonus += e.val1;
            }
            if let Some(e) = sc::get_sce(c, src.id, SC_PERIOD_RECEIVEITEM_2ND) {
                drop_rate_bonus += e.val1;
            }
            // (VIP is off: drop_rate_cap)
            let cap = conf::drop_rate_cap() * factor;
            drop_rate = (0.5 + drop_rate as f64 * drop_rate_bonus as f64 / 100.0) as i32;
            // Now limit the drop rate to never be exceed the cap (default: 90%), unless it is originally above it
            if drop_rate > cap && base_rate < cap {
                drop_rate = cap;
            }
        }
    }
    // RENEWAL_DROP
    drop_rate = apply_rate_i32(drop_rate, drop_modifier);
    // Cap it to 100%
    drop_rate = drop_rate.min(10000 * factor);
    // If the monster's drop rate can become 0
    if conf::drop_rate0item() {
        drop_rate = drop_rate.max(0);
    } else {
        // If not - cap to 0.01% or 0.001% drop rate - as on official servers
        drop_rate = drop_rate.max(1);
    }
    drop_rate
}

/// common/utils.hpp apply_rate for int32 values.
fn apply_rate_i32(val: i32, rate: i32) -> i32 {
    if rate == 100 {
        val
    } else if val > 100000 {
        val / 100 * rate
    } else {
        val * rate / 100
    }
}

/// common/utils.hpp apply_rate for 64-bit EXP.
fn apply_rate_u64(val: u64, rate: i32) -> u64 {
    if rate == 100 {
        val
    } else if val > 100000 {
        (val / 100).wrapping_mul(rate as u64)
    } else {
        val.wrapping_mul(rate as u64) / 100
    }
}

/// common/utils.hpp apply_rate on a double.
fn apply_rate_f(val: f64, rate: i32) -> f64 {
    if rate == 100 {
        val
    } else if val > 100000.0 {
        val / 100.0 * rate as f64
    } else {
        val * rate as f64 / 100.0
    }
}

/// common/utils.hpp apply_rate2(val, rate, 1) with val = t_exp and rate = the double share.
fn apply_rate2_share(val: u64, per: f64) -> f64 {
    if per == 1.0 {
        val as f64
    } else if val > 100000 {
        (val / 1) as f64 * per
    } else {
        val as f64 * per / 1.0
    }
}

/// cap_value(double, 1, MAX_EXP) then (t_exp).
fn cap_exp(v: f64) -> u64 {
    if v >= MAX_EXP as f64 {
        MAX_EXP
    } else if v <= 1.0 {
        1
    } else {
        v as u64
    }
}

/// pc.cpp pc_level_penalty_mod (RENEWAL_EXP / RENEWAL_DROP): the rate for the level difference between the monster
/// and the player (100 without a player, on maps that turn the penalty off, or for MD_FIXEDITEMDROP drops).
pub fn pc_level_penalty_mod(c: &Ctx, sd: Option<&Unit>, type_: u16, md: &MobData) -> i32 {
    use crate::db::jobs::{PENALTY_DROP, PENALTY_EXP, PENALTY_MVP_DROP};
    let Some(sd) = sd else { return 100 };
    if (type_ == PENALTY_DROP && crate::npc::mapflag(c, sd.map, super::MF_NORENEWALDROPPENALTY)) || (type_ == PENALTY_EXP && crate::npc::mapflag(c, sd.map, super::MF_NORENEWALEXPPENALTY)) {
        return 100;
    }
    let Some(db) = super::db(c, md) else { return 100 };
    let monster_level = md.level;
    if (type_ == PENALTY_DROP || type_ == PENALTY_MVP_DROP) && db.status.has_mode(mobs::MD_FIXEDITEMDROP) {
        return 100;
    }
    let base_level = crate::status::status_get_lv(c, sd);
    crate::db::jobs::pc_level_penalty_rate(type_, monster_level - base_level) as i32
}

/// pc.cpp pc_level_penalty_mod(sd, type, mob) without a monster (md == nullptr): the database entry's level
/// (@mobinfo, @whodrops).
pub fn pc_level_penalty_mod_db(c: &Ctx, sd: Option<&Unit>, type_: u16, mob: &mobs::MobDb) -> i32 {
    use crate::db::jobs::{PENALTY_DROP, PENALTY_EXP, PENALTY_MVP_DROP};
    let Some(sd) = sd else { return 100 };
    if (type_ == PENALTY_DROP && crate::npc::mapflag(c, sd.map, super::MF_NORENEWALDROPPENALTY)) || (type_ == PENALTY_EXP && crate::npc::mapflag(c, sd.map, super::MF_NORENEWALEXPPENALTY)) {
        return 100;
    }
    if (type_ == PENALTY_DROP || type_ == PENALTY_MVP_DROP) && mob.status.has_mode(mobs::MD_FIXEDITEMDROP) {
        return 100;
    }
    let base_level = crate::status::status_get_lv(c, sd);
    crate::db::jobs::pc_level_penalty_rate(type_, mob.lv as i32 - base_level) as i32
}

/// mob.cpp mob_data::get_mvp_player: the MVP (highest damage dealt + tanked among the players still on the map and
/// alive) of an MVP monster.
fn get_mvp_player(c: &Ctx, md: &MobData, mu: &Unit, _first_sd: Option<&Unit>) -> Option<Unit> {
    // There cannot be an MVP player if the monster is not an MVP
    if super::get_bosstype(md) != BossType::Mvp {
        return None;
    }
    // (mvp_to_loot_priority is off)
    let mut mvp_damage: i64 = 0;
    let mut mvp_sd = None;
    for entry in &md.dmglog {
        let Some(sd) = crate::pc::charid2sd(c, entry.id) else { continue }; // skip players that are offline
        if sd.map != mu.map {
            continue; // skip players not on this map
        }
        if crate::pc::isdead(c, sd.id) {
            continue; // skip dead players
        }
        if mvp_damage >= entry.dmg + entry.dmg_tanked {
            continue;
        }
        mvp_damage = entry.dmg.saturating_add(entry.dmg_tanked);
        mvp_sd = Some(sd);
    }
    mvp_sd
}

// ---------------------------------------------------------------------------------------------- mob_dead

/// A player's killer / kill ids (map_session_data killerrid / killedrid / killedgid: pc_readparam / pc_setparam
/// SP_KILLERRID, SP_KILLEDRID, SP_KILLEDGID). Reset when the character enters the world.
#[table(accessor = pc_kill)]
#[derive(Clone, Debug, Default)]
pub struct PcKill {
    #[primary_key]
    pub account_id: u32,
    pub killerrid: i32,
    pub killedrid: i32,
    pub killedgid: i32,
}

/// pc_readparam SP_KILLERRID / SP_KILLEDRID / SP_KILLEDGID.
pub fn kill_param(c: &Ctx, aid: u32, typ: i32) -> Option<i64> {
    let r = c.db().pc_kill().account_id().find(aid).unwrap_or(PcKill { account_id: aid, ..Default::default() });
    match typ {
        SP_KILLERRID => Some(r.killerrid as i64),
        SP_KILLEDRID => Some(r.killedrid as i64),
        SP_KILLEDGID => Some(r.killedgid as i64),
        _ => None,
    }
}

/// pc_setparam SP_KILLERRID / SP_KILLEDRID / SP_KILLEDGID (always true for these).
pub fn set_kill_param(c: &Ctx, aid: u32, typ: i32, val: i64) -> Option<bool> {
    let mut r = c.db().pc_kill().account_id().find(aid).unwrap_or(PcKill { account_id: aid, ..Default::default() });
    match typ {
        SP_KILLERRID => r.killerrid = val as i32,
        SP_KILLEDRID => r.killedrid = val as i32,
        SP_KILLEDGID => r.killedgid = val as i32,
        _ => return None,
    }
    if c.db().pc_kill().account_id().find(aid).is_some() {
        c.db().pc_kill().account_id().update(r);
    } else {
        c.db().pc_kill().insert(r);
    }
    Some(true)
}

/// A new map_session_data starts with killer ids 0.
pub fn reset_kill_params(c: &Ctx, aid: u32) {
    c.db().pc_kill().account_id().delete(aid);
}

/// One member of mob_dead's party EXP table (pt[]).
struct PartyShare {
    id: u32,
    base_exp: u64,
    job_exp: u64,
    zeny: i32,
}

/// mob.cpp mob_dead: the monster died (type&1: no drops, &2: no EXP). Rewards, events, then how status_damage
/// removes it: 3 = remove from map (respawn set), 5 = free it (no respawn data), 0 never.
pub fn mob_dead(c: &Ctx, id: u32, src: Option<&Unit>, type_: i32) -> i32 {
    let tick = c.tick();
    let Some(mut md) = get(c, id) else { return 0 };
    let Some(mu) = unit::get(c, id) else { return 0 };
    let m = mu.map;
    let Some(db) = super::db(c, &md) else { return 0 };
    let mut sd: Option<Unit> = src.filter(|s| s.kind == BL_PC).cloned();
    let mut first_sd: Option<Unit> = sd.clone();
    // guardian_data: the castle's guardian slot is saved empty (woe/guardian.rs)
    crate::woe::guardian::mob_dead(c, id);
    if src.is_some() {
        // Use Dead skill only if not killed by Script or Command
        md.status.hp = 1;
        super::mob_setstate(&mut md, super::MSS_DEAD);
        put(c, &md);
        super::ai::mobskill_use(c, id, tick, -1);
        md = get(c, id).unwrap_or(md);
        md.status.hp = 0;
        put(c, &md);
    }
    if let Some(s) = src {
        if s.kind == BL_MOB {
            super::ai::mob_unlocktarget(c, s.id, tick);
        }
    }
    let md = get(c, id).unwrap_or(md);
    // filter out entries not eligible for exp distribution
    let mut tmpsd: Vec<Option<Unit>> = vec![None; md.dmglog.len()];
    struct DmgEntry {
        sd: Unit,
        damage: i64,
    }
    let mut lootdmg: Vec<DmgEntry> = Vec::new();
    let mut count: i32 = 0;
    let mut total_damage: i64 = 0;
    let mut dmgbltypes: u16 = 0;
    for (i, entry) in md.dmglog.iter().enumerate() {
        total_damage = total_damage.saturating_add(entry.dmg);
        if entry.flag == MDLF_SELF {
            // Self damage counts as exp tap
            count += 1;
            continue;
        }
        let Some(tsd) = crate::pc::charid2sd(c, entry.id) else { continue }; // skip players that are offline
        if tsd.map != m {
            continue; // skip players not on this map
        }
        count += 1; // Only logged into same map chars are counted for the total.
        if crate::pc::isdead(c, tsd.id) {
            continue; // skip dead players
        }
        match entry.flag {
            MDLF_NORMAL => dmgbltypes |= BL_PC,
            MDLF_HOMUN => {
                // Skip homunculus' share if inactive
                if !crate::homunculus::data::is_active_of(c, tsd.id) {
                    continue;
                }
                dmgbltypes |= BL_HOM;
            }
            MDLF_PET => {
                // Skip pet's share if inactive (pet.rs)
                if crate::pet::pet_id_of(c, tsd.id) == 0 || crate::pet::data::of_master(c, tsd.id).is_none() {
                    continue;
                }
                dmgbltypes |= crate::map::BL_PET;
            }
            _ => {}
        }
        tmpsd[i] = Some(tsd.clone()); // record as valid damage-log entry
        // Gather data to determine loot priority; slave damage is added to the player's damage
        match lootdmg.iter_mut().find(|e| e.sd.id == tsd.id) {
            Some(e) => e.damage += entry.dmg,
            None => lootdmg.push(DmgEntry { sd: tsd, damage: entry.dmg }),
        }
    }
    let (mut second_sd, mut third_sd): (Option<Unit>, Option<Unit>) = (None, None);
    if !lootdmg.is_empty() {
        // Officially, the first player in the damage log gets 30% of total damage as bonus for loot priority
        lootdmg[0].damage += total_damage * conf::first_attack_loot_bonus() / 100;
        // Sort list by damage now and determine top 3 damage dealers (std::sort; stable for these short lists)
        lootdmg.sort_by(|a, b| b.damage.cmp(&a.damage));
        first_sd = Some(lootdmg[0].sd.clone());
        if lootdmg.len() > 1 {
            second_sd = Some(lootdmg[1].sd.clone());
        }
        if lootdmg.len() > 2 {
            third_sd = Some(lootdmg[2].sd.clone());
        }
    }
    // determines, if the monster was killed by homunculus' / mercenary damage only
    let homkillonly = dmgbltypes & BL_HOM != 0 && dmgbltypes & !BL_HOM == 0;
    let merckillonly = dmgbltypes & BL_MER != 0 && dmgbltypes & !BL_MER == 0;
    // Determine MVP (need to do it here so that it's not influenced by first attacker bonus below)
    let mvp_sd = get_mvp_player(c, &md, &mu, first_sd.as_ref());
    // (exp_calc_type 2: first-attacker 200% share: exp_calc_type is 0)
    let pvp = crate::npc::mapflag(c, m, crate::npc::MF_PVP);
    if type_ & 2 == 0 // No exp
        && (!pvp || crate::conf::pvp_exp()) // Pvp no exp rule [MouseJstr]
        && (md.master_id == 0 || md.special_ai == 0) // Only player-summoned mobs do not give exp. [Skotlex]
        && (!crate::npc::mapflag(c, m, crate::npc::MF_NOBASEEXP) || !crate::npc::mapflag(c, m, crate::npc::MF_NOJOBEXP))
    {
        // Experience calculation.
        let mut bonus: i32 = 100; // Bonus on top of your share (common to all attackers).
        let mut pt: Vec<PartyShare> = Vec::new();
        if let Some(s) = &sd {
            // RENEWAL: the killer's SC_RICHMANKIM
            if let Some(e) = sc::get_sce(c, s.id, SC_RICHMANKIM) {
                bonus += e.val2;
            }
            // Star Gladiator: SC_MIRACLE makes every monster a star target, else the Hatred targets (sd->hate_mob) on
            // their day, with the matching SG_*_BLESS skill (mob.cpp:3093-3101, pc_taekwon.rs)
            bonus += crate::pc_taekwon::mob_dead_bless_bonus(c, s.id, super::status::status_get_class(c, &md));
        }
        // (mobs_level_up is off)
        let mut md_rebirth_count = count;
        for (i, entry) in md.dmglog.iter().enumerate() {
            let Some(tsd) = &tmpsd[i] else { continue };
            let mut flag = true;
            let mut zeny: i32 = 0;
            // exp_calc_type 0: Aegis's exp formula based on total damage (eAthena's when total_damage is 0)
            let mut per: f64 = if total_damage == 0 { entry.dmg as f64 / md.status.max_hp as f64 } else { entry.dmg as f64 / total_damage as f64 };
            // To prevent exploits
            if per > 1.0 {
                per = 1.0;
            }
            // Exclude rebirth tap from this calculation
            md_rebirth_count -= md.rebirth as i32;
            let mut cnt = md_rebirth_count;
            if cnt > 1 && conf::exp_bonus_attacker() != 0 {
                // Exp bonus per additional attacker.
                if cnt > conf::exp_bonus_max_attacker() {
                    cnt = conf::exp_bonus_max_attacker();
                }
                per += per * ((cnt - 1) * conf::exp_bonus_attacker()) as f64 / 100.0;
            }
            md_rebirth_count = cnt;
            // (mob_size_influence is off)
            if entry.flag == MDLF_PET {
                per *= conf::pet_attack_exp_rate() as f64 / 100.0;
            }
            if conf::zeny_from_mobs() && md.level != 0 {
                // zeny calculation moblv + random moblv [Valaris]
                zeny = ((md.level + (c.rnd() as i32).rem_euclid(md.level)) as f64 * per * bonus as f64 / 100.0) as i32;
                if super::get_bosstype(&md) == BossType::Mvp {
                    zeny *= (c.rnd() % 250) as i32;
                }
            }
            let mut base_exp: u64 = if crate::npc::mapflag(c, m, crate::npc::MF_NOBASEEXP) || db.base_exp == 0 {
                0
            } else {
                let mut exp = apply_rate2_share(db.base_exp, per);
                exp = apply_rate_f(exp, bonus);
                exp = apply_rate_f(exp, crate::npc::mapflag_value(c, m, crate::npc::MF_BEXP));
                cap_exp(exp)
            };
            let mut job_exp: u64 = if crate::npc::mapflag(c, m, crate::npc::MF_NOJOBEXP) || db.job_exp == 0 {
                0
            } else {
                let mut exp = apply_rate2_share(db.job_exp, per);
                exp = apply_rate_f(exp, bonus);
                exp = apply_rate_f(exp, crate::npc::mapflag_value(c, m, crate::npc::MF_JEXP));
                cap_exp(exp)
            };
            // (hom_idle_no_share is off)
            let party_id = crate::party::party_id_of(c, tsd.id);
            if party_id > 0 {
                match pt.iter_mut().find(|p| p.id == party_id) {
                    None => {
                        // Possibly add party.
                        if let Some(p) = crate::party::party_search(c, party_id) {
                            if p.party.exp != 0 {
                                pt.push(PartyShare { id: party_id, base_exp, job_exp, zeny });
                                flag = false;
                            }
                        }
                    }
                    Some(p) => {
                        // Add to total
                        p.base_exp = p.base_exp.saturating_add(base_exp).min(MAX_EXP);
                        p.job_exp = p.job_exp.saturating_add(job_exp).min(MAX_EXP);
                        p.zeny += zeny; // zeny share [Valaris]
                        flag = false;
                    }
                }
            }
            // RENEWAL: homunculi receive homunculus_exp_gain % of the base EXP (mob.cpp:3199-3201, homunculus.rs)
            crate::homunculus::mob_dead_gainexp(c, tsd.id, base_exp);
            if flag {
                if (base_exp != 0 || job_exp != 0) && (entry.flag != MDLF_PET || conf::pet_attack_exp_to_master()) {
                    // RENEWAL_EXP
                    let rate = pc_level_penalty_mod(c, Some(tsd), crate::db::jobs::PENALTY_EXP, &md);
                    if rate != 100 {
                        if base_exp != 0 {
                            base_exp = apply_rate_u64(base_exp, rate).clamp(1, MAX_EXP);
                        }
                        if job_exp != 0 {
                            job_exp = apply_rate_u64(job_exp, rate).clamp(1, MAX_EXP);
                        }
                    }
                    crate::status::progress::pc_gainexp(c, tsd.id, Some(&mu), base_exp, job_exp, 0);
                }
                if zeny != 0 {
                    // zeny from mobs [Valaris]
                    crate::inventory::pc_getzeny(c, tsd.id, zeny as i64, crate::map_log::LOG_TYPE_PICKDROP_MONSTER, 0);
                }
            }
        }
        count = md_rebirth_count;
        for p in &pt {
            // Party share.
            let md_for_penalty = md.clone();
            let mut penalty = |aid: u32| {
                let u = unit::get(c, aid);
                pc_level_penalty_mod(c, u.as_ref(), crate::db::jobs::PENALTY_EXP, &md_for_penalty)
            };
            crate::party::party_exp_share(c, p.id, &mu, p.base_exp, p.job_exp, p.zeny, &mut penalty);
        }
    }
    // Looted items have an independent drop position and also don't show special effects when dropped, so we need
    // put them into a separate list
    let charid = |u: &Option<Unit>| u.as_ref().map(|u| u.char_id).unwrap_or(0);
    let mut lootlist = MobDropList::new(&mu, charid(&first_sd), charid(&second_sd), charid(&third_sd));
    // Process items looted by the mob
    if md.looter {
        for li in &md.lootitems {
            // mob_setlootitem: the item as it was, its mob id the one that dropped it
            let ditem = ItemDrop { item: li.item.clone(), mob_id: li.mob_id };
            mob_item_drop(c, md.mob_id, m, &mut lootlist, ditem, true, 10000, homkillonly || merckillonly);
        }
    }
    if type_ & 1 == 0
        && !crate::npc::mapflag(c, m, crate::npc::MF_NOMOBLOOT)
        && !md.rebirth
        && (md.special_ai == 0 // Non special mob
            || conf::alchemist_summon_reward() == 2 // All summoned give drops
            || (md.special_ai == super::AI_SPHERE && conf::alchemist_summon_reward() == 1))
    // Marine Sphere Drops items.
    {
        // Item Drop
        // RENEWAL_DROP
        let penalty_sd = first_sd.clone().or(second_sd.clone()).or(third_sd.clone());
        let drop_modifier = pc_level_penalty_mod(c, penalty_sd.as_ref(), crate::db::jobs::PENALTY_DROP, &md);
        let mut dlist = MobDropList::new(&mu, charid(&first_sd), charid(&second_sd), charid(&third_sd));
        // These trigger for the killer of the monster
        if let Some(s) = &sd {
            // process script-granted extra drop bonuses (bAddMonsterDropItem & co.)
            if let Some(ssd) = crate::status::data::sd_get(c, s.id) {
                for it in &ssd.lists.add_drop {
                    if it.nameid == 0 && it.group == 0 {
                        continue;
                    }
                    let race = md.status.race as i32;
                    let class_ = md.status.class_ as i32;
                    if ((it.race as i32) < crate::status::defs::RC_NONE_ && it.race as i32 == -md.mob_id) // Race < RC_NONE_, use mob_id
                        || (it.race as i32 == crate::status::defs::RC_ALL || it.race as i32 == race) // Matched race
                        || (it.class_ as i32 == crate::status::defs::CLASS_ALL || it.class_ as i32 == class_)
                    // Matched class
                    {
                        // Check if the bonus item drop rate should be multiplied with mob level/10 [Lupus]
                        let drop_rate = if it.rate < 0 {
                            // It's negative, then it should be multiplied. with mob_level/10: rate = base_rate * (mob_level/10) + 1
                            let r = (-it.rate) * md.level / 10 + 1;
                            r.clamp(crate::conf::item_drop_adddrop_min().max(1), crate::conf::item_drop_adddrop_max().min(10000))
                        } else {
                            it.rate // it's positive, then it goes as it is
                        };
                        if (c.rnd() % 10000) as i32 >= drop_rate {
                            continue;
                        }
                        let (nameid, rate) = if it.nameid > 0 {
                            (it.nameid, drop_rate)
                        } else {
                            let Some(entry) = crate::itemgroup::get_random_entry(c, it.group, 1, crate::db::items::group::GROUP_ALGORITHM_DROP) else { continue };
                            (entry.nameid, entry.adj_rate as i32 * drop_rate / 10000)
                        };
                        let ditem = mob_setdropitem(c, nameid, 0, 1, md.mob_id as u16);
                        mob_item_drop(c, md.mob_id, m, &mut dlist, ditem, false, rate, homkillonly || merckillonly);
                    }
                }
                // process script-granted zeny bonus (get_zeny_num) [Skotlex]
                if ssd.bonus.get_zeny_num != 0 && ((c.rnd() % 100) as i32) < ssd.bonus.get_zeny_rate {
                    let mut i = if ssd.bonus.get_zeny_num > 0 { ssd.bonus.get_zeny_num } else { -md.level * ssd.bonus.get_zeny_num };
                    if i == 0 {
                        i = 1;
                    }
                    crate::inventory::pc_getzeny(c, s.id, 1 + (c.rnd() % i as u32) as i64, crate::map_log::LOG_TYPE_PICKDROP_MONSTER, 0);
                }
            }
        }
        // Regular mob drops drop after script-granted drops
        for entry in &db.dropitem {
            if entry.nameid == 0 {
                continue;
            }
            let Some(it) = crate::db::items::itemdb_exists(entry.nameid) else { continue };
            let drop_rate = mob_getdroprate(c, src, db, entry.rate as i32, drop_modifier, Some(&md), 1);
            // attempt to drop the item
            if (c.rnd() % 10000) as i32 >= drop_rate {
                continue;
            }
            if let (Some(f), true) = (&first_sd, crate::db::items::itemdb_type(entry.nameid) == crate::db::items::IT_PETEGG) {
                // pet_create_egg(first_sd, entry->nameid): the egg with a new pet's data (pet.rs)
                crate::pet::pet_create_egg(c, f.id, entry.nameid);
                continue;
            }
            let ditem = mob_setdropitem(c, entry.nameid, entry.randomopt_group, 1, md.mob_id as u16);
            // A Rare Drop Global Announce by Lupus
            if let Some(f) = &first_sd {
                if (entry.rate as i32) <= conf::rare_drop_announce() {
                    let msg = crate::data::msg_txt(541).replacen("%s", &f.name, 1).replacen("%s", &md.name, 1).replacen("%s", &it.ename, 1).replacen("%0.02f", &format!("{:.2}", drop_rate as f64 / 100.0), 1).replace("%%", "%");
                    let b = crate::net::wire::latin1(&msg);
                    crate::clif::broadcast(c, None, &b, b.len() + 1, 0, crate::clif::Target::AllClient);
                }
            }
            // Announce first, or else ditem will be freed. [Lance] By popular demand, use base drop rate for
            // autoloot code. [Skotlex]
            let rate = if conf::autoloot_adjust() { drop_rate } else { entry.rate as i32 };
            mob_item_drop(c, md.mob_id, m, &mut dlist, ditem, false, rate, homkillonly || merckillonly);
        }
        // Ore Discovery (triggers if owner has loot priority, does not require to be the killer)
        if let Some(f) = &first_sd {
            if crate::skill::checkskill(c, f.id, crate::db::skills::ids::BS_FINDINGORE) > 0 {
                if let Some(entry) = crate::itemgroup::get_random_entry(c, crate::data::constant("IG_ORE").unwrap_or(0) as u16, 1, crate::db::items::group::GROUP_ALGORITHM_DROP) {
                    let ditem = mob_setdropitem(c, entry.nameid, 0, 1, md.mob_id as u16);
                    mob_item_drop(c, md.mob_id, m, &mut dlist, ditem, false, entry.adj_rate as i32, homkillonly || merckillonly);
                }
            }
        }
        // Process map specific drops
        // If it is an instance map, we check for map specific drops of the original map (mob.cpp:3375-3380)
        let drop_m = crate::instance_map::instance_src_map(c, m).unwrap_or(m);
        if let Some(mapdrops) = mobs::map_drop_db().find(drop_m as i16) {
            // Process map wide drops
            for (_, d) in &mapdrops.globals {
                let final_rate: u32 = if conf::enable_bonus_map_drops() { mob_getdroprate(c, first_sd.as_ref(), db, d.rate as i32, drop_modifier, Some(&md), 10) as u32 } else { d.rate };
                if c.rnd_chance(final_rate as i32, 100000) {
                    // 'Cheat' for autoloot command: rate is changed from n/100000 to n/10000
                    let map_drops_rate = (final_rate as i32 / 10).max(1);
                    let ditem = mob_setdropitem(c, d.nameid, d.randomopt_group, 1, md.mob_id as u16);
                    mob_item_drop(c, md.mob_id, m, &mut dlist, ditem, false, map_drops_rate, homkillonly || merckillonly);
                }
            }
            // Process map drops for this specific mob
            if let Some(specific) = mapdrops.specific_for(md.mob_id as u16) {
                for (_, d) in specific {
                    let final_rate: u32 = if conf::enable_bonus_map_drops() { mob_getdroprate(c, first_sd.as_ref(), db, d.rate as i32, drop_modifier, Some(&md), 10) as u32 } else { d.rate };
                    if c.rnd_chance(final_rate as i32, 100000) {
                        let map_drops_rate = (final_rate as i32 / 10).max(1);
                        let ditem = mob_setdropitem(c, d.nameid, d.randomopt_group, 1, md.mob_id as u16);
                        mob_item_drop(c, md.mob_id, m, &mut dlist, ditem, false, map_drops_rate, homkillonly || merckillonly);
                    }
                }
            }
        }
        // There are drop items.
        if !dlist.items.is_empty() || !lootlist.items.is_empty() {
            put_drop_list(c, id, false, dlist);
            put_drop_list(c, id, true, lootlist);
            mob_delay_item_drop(c, id);
        }
    } else if !lootlist.items.is_empty() {
        // Loot MUST drop!
        put_drop_list(c, id, true, lootlist);
        mob_delay_item_drop(c, id);
    }
    // MVP Reward
    if let Some(mvp) = &mvp_sd {
        let mut log_mvp_nameid: u32 = 0;
        let mut log_mvp_exp: u64 = 0;
        super::clif::mvp_effect(c, mvp);
        // mapflag: noexp check [Lorky]
        if db.mexp > 0 && !(crate::npc::mapflag(c, m, crate::npc::MF_NOBASEEXP) || type_ & 2 != 0) {
            log_mvp_exp = db.mexp;
            // RENEWAL_EXP
            let penalty = pc_level_penalty_mod(c, Some(mvp), crate::db::jobs::PENALTY_MVP_EXP, &md);
            log_mvp_exp = apply_rate_u64(log_mvp_exp, penalty).min(MAX_EXP);
            if conf::exp_bonus_attacker() > 0 && count > 1 {
                if count > conf::exp_bonus_max_attacker() {
                    count = conf::exp_bonus_max_attacker();
                }
                log_mvp_exp = log_mvp_exp.wrapping_add(log_mvp_exp.wrapping_mul((conf::exp_bonus_attacker() * (count - 1)) as u64) / 100);
            }
            log_mvp_exp = log_mvp_exp.clamp(1, MAX_EXP);
            super::clif::mvp_exp(c, mvp, log_mvp_exp);
            crate::status::progress::pc_gainexp(c, mvp.id, Some(&mu), log_mvp_exp, 0, 0);
        }
        if !(crate::npc::mapflag(c, m, crate::npc::MF_NOMVPLOOT) || type_ & 1 != 0) {
            // Create a copy of the MVP drops vector; order might be random depending on item_drop_mvp_mode
            let mut mdrop = db.mvpitem.clone();
            if conf::item_drop_mvp_mode() == 1 {
                c.rnd_vector_order(&mut mdrop); // Random order
            }
            // RENEWAL_DROP
            let penalty = pc_level_penalty_mod(c, Some(mvp), crate::db::jobs::PENALTY_MVP_DROP, &md);
            for entry in &mdrop {
                if entry.nameid == 0 {
                    continue;
                }
                let Some(i_data) = crate::db::items::itemdb_exists(entry.nameid) else { continue };
                let mut temp = entry.rate as i32;
                temp = apply_rate_i32(temp, penalty).clamp(0, 10000);
                if temp != 10000 {
                    if temp <= 0 && !conf::drop_rate0item() {
                        temp = 1;
                    }
                    if (c.rnd() % 10000) as i32 >= temp {
                        continue; // if ==0, then it doesn't drop
                    }
                }
                let mut item = Item::new(entry.nameid);
                item.identify = crate::db::items::itemdb_isidentified(entry.nameid) as u8;
                super::clif::mvp_item(c, mvp, entry.nameid);
                log_mvp_nameid = item.nameid;
                // A Rare MVP Drop Global Announce by Lupus
                if temp <= conf::rare_drop_announce() {
                    let msg = crate::data::msg_txt(541).replacen("%s", &mvp.name, 1).replacen("%s", &md.name, 1).replacen("%s", &i_data.ename, 1).replacen("%0.02f", &format!("{:.2}", temp as f64 / 100.0), 1).replace("%%", "%");
                    let b = crate::net::wire::latin1(&msg);
                    crate::clif::broadcast(c, None, &b, b.len() + 1, 0, crate::clif::Target::AllClient);
                }
                mob_setdropitem_option(c, &mut item, entry.randomopt_group);
                let flag = crate::inventory::pc_additem(c, mvp.id, &mut item, 1, crate::map_log::LOG_TYPE_PICKDROP_PLAYER);
                if flag != 0 {
                    crate::clif_items::additem(c, mvp.id, 0, 0, flag);
                    let (sid, tid) = (charid(&second_sd), charid(&third_sd));
                    if let Some(mvpu) = unit::get(c, mvp.id) {
                        crate::flooritem::map_addflooritem(c, &item, 1, mvpu.map, mvpu.x, mvpu.y, mvpu.char_id, sid, tid, 1, 0, true, Some(crate::path::DIR_CENTER), map::BL_NUL);
                    }
                }
                if i_data.flag.broadcast {
                    crate::atcmd_pc::intif_broadcast_obtain_special_item(c, mvp.id, item.nameid, md.mob_id as u32, crate::atcmd_pc::ITEMOBTAIN_TYPE_MONSTER_ITEM);
                }
                //Logs items, MVP prizes [Lupus]
                crate::map_log::log_pick_mob(c, md.mob_id, m, crate::map_log::LOG_TYPE_MVP, -1, &item);
                // If item_drop_mvp_mode is not 2, then only one item should be granted
                if conf::item_drop_mvp_mode() != 2 {
                    break;
                }
            }
        }
        crate::map_log::log_mvpdrop(c, mvp.id, md.mob_id, log_mvp_nameid, log_mvp_exp);
    }
    if type_ & 2 != 0 && sd.is_none() && md.mob_id == mobs::MOBID_EMPERIUM as i32 {
        // Emperium destroyed by script. Discard top damage dealer.
        first_sd = None;
    }
    let scl = sc::load(c, id);
    let rebirth = scl.has(SC_KAIZEL) || scl.has(SC_ULTIMATE_S) || (scl.has(SC_REBIRTH) && !md.rebirth);
    if !rebirth {
        // Only trigger event on final kill
        if let Some(s) = src {
            // allowed type
            if matches!(s.kind, BL_PET | BL_HOM | BL_MER | BL_ELEM | BL_MOB) {
                sd = super::battle_iface::battle_get_master(c, s).filter(|m| m.kind == BL_PC);
            }
        }
        if let Some(s) = &sd {
            // TK_MISSION [Skotlex] (mob.cpp:3557-3573, pc_taekwon.rs)
            crate::pc_taekwon::mob_dead_mission(c, s.id, md.mob_id);
            let party_id = crate::party::party_id_of(c, s.id);
            let qm = crate::quest::QuestMob {
                mob_id: md.mob_id as u16,
                level: md.level,
                race: md.status.race as i8,
                size: md.status.size,
                def_ele: md.status.def_ele as i8,
            };
            if party_id != 0 {
                let r = crate::conf::area_size();
                for p in map::units_in_area(c, mu.map, mu.x - r, mu.y - r, mu.x + r, mu.y + r, BL_PC) {
                    crate::quest::update_objective_sub(c, p.id, party_id as i32, &qm);
                }
            } else if crate::quest::avail_quests(c, s.id) != 0 {
                crate::quest::update_objective(c, s.id, &qm);
            }
            // Achievements (the extras agent's achievement.rs)
            if crate::achievement::mobexists(md.mob_id as u32) {
                // battle_config.achievement_mob_share: no (conf/battle/monster.conf)
                let achievement_mob_share: i32 = crate::battle_config::get(crate::battle_config::Bc::ACHIEVEMENT_MOB_SHARE);
                if achievement_mob_share > 0 && party_id > 0 {
                    let r = crate::conf::area_size();
                    for p in map::units_in_area(c, mu.map, mu.x - r, mu.y - r, mu.x + r, mu.y + r, BL_PC) {
                        crate::achievement::achievement_update_objective_sub(c, p.id, party_id, md.mob_id as i32);
                    }
                } else {
                    crate::achievement::achievement_update_objective(c, Some(s.id), crate::db::misc::achievement::AG_BATTLE, &[md.mob_id as i32]);
                }
            }
            // The master or Mercenary can increase the kill count, if the monster level is greater or equal than half
            // the baselevel of the master (mercenary.rs)
            if let Some(merc) = crate::mercenary::data::of_master(c, s.id) {
                let mob_lv = super::db(c, &md).map(|d| d.lv as i32).unwrap_or(0);
                let base_level = crate::pc::character_of(c, s.id).map(|ch| ch.base_level as i32).unwrap_or(0);
                if src.map(|x| x.kind == BL_PC || x.kind == BL_MER).unwrap_or(false) && mob_lv >= base_level / 2 {
                    crate::mercenary::mercenary_kills(c, merc.id);
                }
            }
        }
        if !md.npc_event.is_empty() && !md.npc_killmonster {
            if let (Some(s), true) = (&sd, conf::mob_npc_event_type() != 0) {
                set_kill_param(c, s.id, SP_KILLEDGID, md.id as i64);
                set_kill_param(c, s.id, SP_KILLEDRID, md.mob_id as i64);
                set_kill_param(c, s.id, SP_KILLERRID, s.id as i64);
                crate::script::npc_run::npc_event(c, s.id, &md.npc_event, 0);
            } else if let Some(f) = &first_sd {
                set_kill_param(c, f.id, SP_KILLEDGID, md.id as i64);
                set_kill_param(c, f.id, SP_KILLEDRID, md.mob_id as i64);
                set_kill_param(c, f.id, SP_KILLERRID, sd.as_ref().map(|s| s.id).unwrap_or(0) as i64);
                crate::script::npc_run::npc_event(c, f.id, &md.npc_event, 0);
            } else {
                crate::script::npc_run::npc_event_do(c, &md.npc_event);
            }
        } else if let Some(f) = first_sd.as_ref().filter(|_| !md.npc_killmonster) {
            set_kill_param(c, f.id, SP_KILLEDGID, md.id as i64);
            set_kill_param(c, f.id, SP_KILLEDRID, md.mob_id as i64);
            crate::script::npc_run::npc_script_event(c, f.id, crate::script::npc_run::NPCE_KILLNPC);
        }
    }
    let Some(mut md) = get(c, id) else { return 5 };
    if md.deletetimer != 0 {
        timer::delete(c, md.deletetimer);
        md.deletetimer = 0;
        put(c, &md);
    }
    // Only loops if necessary (e.g. a poring would never need to loop)
    if md.can_summon {
        super::spawn::mob_deleteslave(c, id, m);
    }
    let Some(mu) = unit::get(c, id) else { return 5 };
    if !rebirth {
        if crate::clif::pcdb_checkid(mu.vd.class) {
            // Player mobs are not removed automatically by the client: first we set them dead, then we delay the
            // outsight effect
            crate::clif::clearunit_area(c, &mu, CLR_DEAD);
            super::clif::clearunit_delayed(c, &mu, unit::CLR_OUTSIGHT, tick + 3000);
        } else {
            // We give the client some time to breath and this allows it to display anything it'd like with the dead
            // corpse, for example the soul drain effect
            super::clif::clearunit_delayed(c, &mu, CLR_DEAD, tick + 250);
        }
    }
    let md = get(c, id).unwrap_or(md);
    let Some(sp) = (if md.spawn != 0 { c.db().spawn_data().id().find(md.spawn) } else { None }) else {
        // Tell status_damage to remove it from memory. If the unit is currently in a walk script, it will be removed
        // there
        return if mu.ud.walk_script { 3 } else { 5 }; // Note: Actually, it's 4. Oh well...
    };
    // MvP tomb [GreenBox]
    if conf::mvp_tomb_enabled() && sp.boss && !crate::npc::mapflag(c, mu.map, super::MF_NOTOMB) {
        let killer = mvp_sd.as_ref().map(|u| u.name.clone()).or(first_sd.as_ref().map(|u| u.name.clone()));
        mvptomb_create(c, id, killer.as_deref(), c.tick() / 1000);
    }
    if !rebirth {
        super::spawn::mob_setdelayspawn(c, id); // Set respawning.
    }
    3 // Remove from map.
}

use crate::npc::spawn_data as _;
use crate::unit::unit as _;

// ---------------------------------------------------------------------------------------------- MVP tombs

/// The tomb data of an MVP tomb NPC (npc_data::u.tomb).
#[table(accessor = mvp_tomb)]
#[derive(Clone, Debug)]
pub struct MvpTomb {
    #[primary_key]
    pub npc_id: u32,
    pub md_id: u32,
    pub mob_id: i32,
    /// time() of the kill (seconds).
    pub kill_time: i64,
    pub killer_name: String,
    pub spawn_timer: u64,
}

/// mob.cpp mvptomb_create: a "Tomb" NPC where the MVP died, shown after mvp_tomb_delay.
pub fn mvptomb_create(c: &Ctx, md_id: u32, killer: Option<&str>, time: i64) {
    let Some(mut md) = get(c, md_id) else { return };
    if md.tomb_nid != 0 {
        mvptomb_destroy(c, &mut md);
        put(c, &md);
    }
    let Some(mu) = unit::get(c, md_id) else { return };
    let id = super::spawn::npc_get_new_npc_id(c);
    if id == 0 {
        return;
    }
    md.tomb_nid = id;
    put(c, &md);
    let name: String = crate::data::msg_txt(656).chars().take(super::NAME_LENGTH - 1).collect();
    // nd->exname is left empty in rAthena; the npc table needs a unique one
    let exname = format!("#mvptomb{id}");
    let nd = crate::npc::Npc {
        id,
        exname,
        name: name.clone(),
        subtype: crate::npc::NPCTYPE_TOMB,
        map: mu.map as i32,
        x: mu.x,
        y: mu.y,
        class: 565,
        src_id: 0,
        xs: -1,
        ys: -1,
        warp_map: -1,
        warp_x: 0,
        warp_y: 0,
        trigger_on_hidden: false,
        state: crate::npc::NPCVIEW_ENABLE,
        is_invisible: false,
        path: String::new(),
        script_file: 0,
        script_start: 0,
        script_end: 0,
        script_line: 0,
        shop: Vec::new(),
        shop_discount: false,
        itemshop_nameid: 0,
        pointshop_str: String::new(),
        owner_char: 0,
        touch_order: 0,
    };
    use crate::npc::npc as _;
    c.db().npc().insert(nd);
    let tick = c.tick();
    let u = Unit {
        id,
        kind: map::BL_NPC,
        map: mu.map,
        x: mu.x,
        y: mu.y,
        on_map: true, // map_addnpc + map_addblock
        name,
        vd: crate::npc::npc_viewdata(565), // status_set_viewdata(nd, nd->class_)
        ud: unit::UnitData { dir: mu.ud.dir, sx: 8, sy: 8, attackabletime: tick, canact_tick: tick, canmove_tick: tick, endure_tick: tick, ..Default::default() },
        sc: Default::default(),
        speed: crate::npc::DEFAULT_NPC_WALK_SPEED,
        mode: crate::unit::MD_CANMOVE,
        level: 0,
        char_id: 0,
        head_dir: 0,
        font: 0,
        manner: 0,
        karma: false,
        guild_id: 0,
        emblem_id: 0,
        hp: 0,
        max_hp: 0,
        dead: false,
        size: 0,
        boss: 0,
        invisible: false,
        npc_owner_char: 0,
    };
    c.db().unit().insert(u);
    c.db().mvp_tomb().insert(MvpTomb {
        npc_id: id,
        md_id,
        mob_id: md.mob_id,
        kill_time: time,
        killer_name: killer.map(|k| k.chars().take(super::NAME_LENGTH - 1).collect()).unwrap_or_default(),
        spawn_timer: 0,
    });
    mvptomb_setdelayspawn(c, id);
}

/// mob.cpp mvptomb_setdelayspawn.
fn mvptomb_setdelayspawn(c: &Ctx, npc_id: u32) {
    let Some(mut t) = c.db().mvp_tomb().npc_id().find(npc_id) else { return };
    if t.spawn_timer != 0 {
        timer::delete(c, t.spawn_timer);
    }
    t.spawn_timer = timer::add(c, c.tick() + conf::mvp_tomb_delay(), Func::MvpTombDelaySpawn, npc_id, 0);
    c.db().mvp_tomb().npc_id().update(t);
}

/// mob.cpp mvptomb_delayspawn (timer): the tomb appears.
pub fn mvptomb_delayspawn(c: &Ctx, tid: u64, npc_id: u32) {
    let Some(mut t) = c.db().mvp_tomb().npc_id().find(npc_id) else { return };
    if t.spawn_timer != tid {
        log::error!("mvptomb_delayspawn: Timer mismatch: {} != {}", tid, t.spawn_timer);
        return;
    }
    t.spawn_timer = 0;
    c.db().mvp_tomb().npc_id().update(t);
    if let Some(u) = unit::get(c, npc_id) {
        crate::clif::spawn(c, &u, false);
    }
}

/// mob.cpp mvptomb_destroy.
pub fn mvptomb_destroy(c: &Ctx, md: &mut MobData) {
    use crate::npc::npc as _;
    let nid = md.tomb_nid;
    if let Some(u) = unit::get(c, nid).filter(|u| u.kind == map::BL_NPC) {
        crate::clif::clearunit_area(c, &u, unit::CLR_OUTSIGHT);
        // map_delblock, the map's NPC list, map_deliddb
        if let Some(t) = c.db().mvp_tomb().npc_id().find(nid) {
            if t.spawn_timer != 0 {
                timer::delete(c, t.spawn_timer);
            }
        }
        c.db().mvp_tomb().npc_id().delete(nid);
        c.db().npc().id().delete(nid);
        c.db().unit().id().delete(nid);
    }
    md.tomb_nid = 0;
}

/// npc.cpp run_tomb: the dialog of an MVP tomb (monster name, time of death, killer).
pub fn run_tomb(c: &Ctx, aid: u32, npc_id: u32) {
    let Some(t) = c.db().mvp_tomb().npc_id().find(npc_id) else { return };
    let Some(sd) = unit::get(c, aid) else { return };
    // strftime("%H:%M", localtime(kill_time)): the server's local time zone
    let local = t.kill_time + crate::script::npc_run::LOCAL_TZ_OFFSET_SECS;
    let secs = local.rem_euclid(86400);
    let time = format!("{:02}:{:02}", secs / 3600, secs % 3600 / 60);
    let mob_name = super::db_find(c, t.mob_id as i32).map(|m| m.name.clone()).unwrap_or_default();
    let say = |s: String| crate::clif::scriptmes(c, &sd, npc_id, &crate::net::wire::latin1(&s));
    say(crate::data::msg_txt(657).replacen("%s", &mob_name, 1)); // [ ^EE0000%s^000000 ]
    say(crate::data::msg_txt(658).to_string()); // Has met its demise
    say(crate::data::msg_txt(659).replacen("%s", &time, 1)); // Time of death : ^EE0000%s^000000
    say(crate::data::msg_txt(660).to_string()); // Defeated by
    let killer = if t.killer_name.is_empty() { "Unknown".to_string() } else { t.killer_name.clone() };
    say(crate::data::msg_txt(661).replacen("%s", &killer, 1)); // [^EE0000%s^000000]
    crate::clif::scriptclose(c, &sd, npc_id);
}

// ---------------------------------------------------------------------------------------------- class change

/// mob.cpp mob_class_change: the monster becomes another monster (Metamorphosis / scripts), keeping its HP rate
/// unless monster_class_change_full_recover.
pub fn mob_class_change(c: &Ctx, id: u32, mob_id: i32) -> i32 {
    let Some(mut md) = get(c, id) else { return 0 };
    let Some(u) = unit::get(c, id) else { return 0 };
    if !u.on_map {
        return 0;
    }
    if mob_id == 0 || mobs::mobdb_checkid(mob_id) == 0 {
        return 0;
    }
    // Disable class changing for some targets...
    if md.guardian {
        return 0; // Guardians/Emperium
    }
    if super::status::status_get_race2(c, &md).contains(&super::RC2_TREASURE) {
        return 0; // Treasure Boxes
    }
    if md.special_ai > super::AI_ATTACK {
        return 0; // Marine Spheres and Floras.
    }
    if super::rtdb::mob_is_clone(c, md.mob_id) != 0 {
        return 0; // Clones
    }
    if md.mob_id == mob_id {
        return 0; // Nothing to change.
    }
    let hp_rate = super::spawn::get_percentage(md.status.hp as i64, md.status.max_hp as i64);
    md.mob_id = mob_id;
    let Some(db) = super::db_find(c, mob_id) else { return 0 };
    md.name = super::spawn::truncate_name(if conf::override_mob_names() == 1 { &db.name } else { &db.jname });
    put(c, &md);
    sc::status_change_end(c, id, SC_KEEPING, 0); // End before calling status_calc_mob().
    sc::status_change_end(c, id, SC_BARRIER, 0);
    if let Some(mut u) = unit::get(c, id) {
        unit::stop_attack(c, &mut u);
        unit::stop_walking(c, &mut u, unit::USW_NONE, 0);
        unit::save(c, &u);
    }
    crate::skill::unit::unit_skillcastcancel(c, id, 0); // mob.cpp:3844
    let mut md = get(c, id).unwrap_or(md);
    super::status::status_set_viewdata(c, &mut md, mob_id);
    put(c, &md);
    if let Some(u) = unit::get(c, id) {
        super::clif::class_change(c, &u, u.vd.class, crate::clif::Target::Area, None);
    }
    super::status::status_calc_mob(c, id, crate::status::SCO_FIRST);
    let mut md = get(c, id).unwrap_or(md);
    if conf::monster_class_change_recover() {
        md.dmglog.clear();
    } else {
        md.status.hp = (md.status.max_hp as i64 * hp_rate / 100) as u32;
        if md.status.hp < 1 {
            md.status.hp = 1;
        }
    }
    md.skilldelay = vec![0; super::MAX_MOBSKILL];
    if !md.looter && db.status.has_mode(mobs::MD_LOOTER) {
        md.looter = true;
    }
    // Targets should be cleared no morph
    md.target_id = 0;
    md.attacked_id = 0;
    md.norm_attacked_id = 0;
    put(c, &md);
    // Need to update name display.
    if let Some(u) = unit::get(c, id) {
        crate::clif::name_area(c, &u);
    }
    0
}
