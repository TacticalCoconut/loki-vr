//! skill.cpp skill_unitsetting and what it needs: skill_get_time3, skill_unitsetmapcell, skill_cell_overlap,
//! skill_maelstrom_suction, and the song / dance overlap (dissonance) functions skill_dance_overlap(_sub),
//! skill_dance_overlap_revert and skill_dance_switch.

use crate::battle::{BCT_ALL, BCT_ENEMY, BCT_SELF};
use crate::db::skills::{self as sk, *};
use crate::map::{BL_PC, BL_SKILL};
use crate::map_cell::{self, CELL_CHKLANDPROTECTOR, CELL_CHKMAELSTROM, CELL_CHKREACH, CELL_CHKWATER, CELL_GETTYPE};
use crate::status::consts::*;
use crate::status::defs::{ELE_DARK, ELE_FIRE, ELE_POISON, ELE_WATER, ELE_WIND};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::Ctx;

use super::consts::*;
use super::data::{self, SkillUnit};
use super::group::{self, InitUnit};
use super::{conf, iface, util};

/// itemdb.hpp ITEMID_TRAP / ITEMID_TRAP_ALLOY.
pub const ITEMID_TRAP: u32 = 1065;
pub const ITEMID_TRAP_ALLOY: u32 = 7940;

/// skill.cpp skill_get_time3: the skill's duration, adjusted by the map's skill_duration map flag.
pub fn skill_get_time3(c: &Ctx, m: u16, skill_id: u16, skill_lv: u16) -> i64 {
    let time = skill_get_time(skill_id, skill_lv);
    if time == 0 {
        return 0;
    }
    if let Some(per) = data::map_skill_duration(c, m, skill_id) {
        return (time / 100).wrapping_mul(per) as i64;
    }
    time as i64
}

/// map.hpp map_flag_vs.
fn map_flag_vs(c: &Ctx, m: u16) -> bool {
    crate::npc::mapflag_vs(c, m)
}

/// skill.cpp skill_unitsetting: sets the ground skill's unit group on (x, y). `flag` &1: the unit's second form
/// (a Fire Pillar going off, a Wall of Thorn on fire); the xx_METEOR skills take their duration in `flag`.
/// Returns the new group's id.
pub fn skill_unitsetting(c: &Ctx, src: &Unit, skill_id: u16, skill_lv: u16, x: i16, y: i16, flag: i32) -> Option<i32> {
    let mut src = src.clone();
    let mut flag = flag;
    // mapdata of the caller's src (skill.cpp:5768), taken before AL_WARP's flag&1 swaps src for the caster: the unit
    // loop's bounds check reads it
    let Some(md) = crate::data::maps().get(src.map) else { return None };
    let Some(skill) = skill_db().find(skill_id) else {
        log::error!("skill_unitsetting: unknown skill {skill_id}");
        return None;
    };
    let mut limit = skill_get_time3(c, src.map, skill_id, skill_lv);
    let mut range = skill_get_unit_range(skill_id, skill_lv);
    let interval = skill.unit_interval as i32;
    let mut target = skill_get_unit_target(skill_id);
    let dir = if src.x == x && src.y == y { 6 } else { crate::map::calc_dir(&src, x, y) };
    let layout = skill_get_unit_layout(skill_id, skill_lv, src.kind == crate::map::BL_MOB, dir);
    let sd = src.kind == BL_PC;
    let status = crate::status::status_get_status_data(c, &src);
    let has_sc = crate::battle::bl::sc(c, &src);
    let hidden = skill.has_unit_flag(UF_HIDDENTRAP) && (conf::traps_setting() == 2 || (conf::traps_setting() == 1 && map_flag_vs(c, src.map)));
    let (mut val1, mut val2, mut val3) = (0i32, 0i32, 0i32);
    let link_group_id = 0; // (set by HW_GRAVITATION: pre-renewal only)
    let mut req_item = 0u32;
    let mut subunt = 0i32;
    let checkskill = |id: u16| if sd { crate::skill::checkskill(c, src.id, id) as i32 } else { 0 };
    let sce = |t: i32| has_sc.as_ref().and_then(|s| s.get(t).cloned());

    match skill_id {
        MH_STEINWAND => {
            val2 = 4 + skill_lv as i32;
            val3 = 300 * skill_lv as i32 + 65 * (status.int_ as i32 + crate::status::status_get_lv(c, &src)) + status.max_sp as i32; //nb hp
        }
        MG_SAFETYWALL => {
            val2 = skill_lv as i32 + 1;
            // RENEWAL
            val3 = 300 * skill_lv as i32 + 65 * (status.int_ as i32 + crate::status::status_get_lv(c, &src)) + status.max_sp as i32;
        }
        MG_FIREWALL => {
            if sce(SC_VIOLENTGALE).is_some() {
                limit = limit * 3 / 2;
            }
            val2 = 4 + skill_lv as i32;
        }
        AL_WARP => {
            val1 = skill_lv as i32 + 6;
            if flag & 1 == 0 {
                limit = 2000;
            } else {
                // previous implementation (not used anymore)
                // Warp Portal morphing to active mode, extract relevant data from src. [Skotlex]
                if src.kind != BL_SKILL {
                    return None;
                }
                let g = data::group_of_unit(c, src.id)?;
                src = unit::get(c, g.src_id)?;
                val2 = g.val2; //Copy the (x,y) position you warp to
                val3 = g.val3; //as well as the mapindex to warp to.
            }
        }
        // (HP_BASILICA: pre-renewal only)
        PR_SANCTUARY | NPC_EVILLAND => val1 = skill_lv as i32 + 3,
        WZ_METEOR | SU_CN_METEOR | SU_CN_METEOR2 | NPC_RAINOFMETEOR | HN_METEOR_STORM_BUSTER => {
            limit = flag as i64;
            flag = 0; // Flag should not influence anything else for these skills
        }
        WZ_FIREPILLAR => {
            if map_cell::getcell(c, Some(src.map), x, y, CELL_CHKLANDPROTECTOR) != 0 {
                return None;
            }
            if flag & 1 != 0 {
                limit = 1000;
            }
            val1 = skill_lv as i32 + 2;
        }
        WZ_QUAGMIRE | AM_DEMONSTRATION => {
            //The target changes to "all" if used in a gvg map. [Skotlex]
            if conf::vs_traps_bctall() != 0 && (src.kind & conf::vs_traps_bctall()) != 0 && map_flag_vs(c, src.map) {
                target = BCT_ALL;
            }
        }
        HT_SKIDTRAP | MA_SKIDTRAP | HT_ANKLESNARE | HT_SHOCKWAVE | HT_SANDMAN | MA_SANDMAN | HT_CLAYMORETRAP | HT_LANDMINE
        | MA_LANDMINE | HT_FLASHER | HT_FREEZINGTRAP | MA_FREEZINGTRAP | HT_BLASTMINE | RA_ELECTRICSHOCKER | RA_CLUSTERBOMB
        | RA_MAGENTATRAP | RA_COBALTTRAP | RA_MAIZETRAP | RA_VERDURETRAP | RA_FIRINGTRAP | RA_ICEBOUNDTRAP | RL_B_TRAP
        | sk::SC_ESCAPE => {
            if skill_id == HT_SKIDTRAP || skill_id == MA_SKIDTRAP {
                //Save position of caster
                val1 = ((src.x as i32) << 16) | src.y as i32;
            }
            // skill_get_requirement(sd, …): the trap item among the requirement's items
            let items = iface::skill_get_requirement_items(c, sd.then_some(&src), skill_id, skill_lv);
            if let Some(items) = items {
                if let Some(&it) = items.iter().find(|&&it| it != 0 && (it == ITEMID_TRAP || it == ITEMID_TRAP_ALLOY)) {
                    req_item = it;
                }
            }
            if skill_id != RL_B_TRAP // Target type should not change on GvG maps.
                && conf::vs_traps_bctall() != 0 && (src.kind & conf::vs_traps_bctall()) != 0 && map_flag_vs(c, src.map)
            {
                target = BCT_ALL;
            }
        }
        SA_LANDPROTECTOR | SA_VOLCANO | SA_DELUGE | SA_VIOLENTGALE | sk::SC_CHAOSPANIC | SOA_TOTEM_OF_TUTELARY => {
            if let Some(old_sg) = group::skill_locate_element_field(c, src.id) {
                //HelloKitty confirmed that these are interchangeable,
                //so you can change element and not consume gemstones.
                if matches!(old_sg.skill_id, SA_VOLCANO | SA_DELUGE | SA_VIOLENTGALE) && old_sg.limit > 0 {
                    //Use the previous limit (minus the elapsed time) [Skotlex]
                    limit = old_sg.limit - (c.tick() - old_sg.tick);
                    if limit < 0 {
                        //This can happen...
                        limit = skill_get_time(skill_id, skill_lv) as i64;
                    }
                }
                group::skill_clear_group(c, src.id, 1);
            }
        }
        BA_WHISTLE => {
            val1 = skill_lv as i32 + status.agi as i32 / 10; // Flee increase
            val2 = (skill_lv as i32 + 1) / 2 + status.luk as i32 / 30; // Perfect dodge increase
            if sd {
                val1 += checkskill(BA_MUSICALLESSON) / 2;
                val2 += checkskill(BA_MUSICALLESSON) / 5;
            }
        }
        DC_HUMMING => {
            val1 = 1 + 2 * skill_lv as i32 + status.dex as i32 / 10; // Hit increase
            if sd {
                val1 += checkskill(DC_DANCINGLESSON);
            }
        }
        BA_POEMBRAGI => {
            val1 = 3 * skill_lv as i32 + status.dex as i32 / 10; // Casting time reduction
            //For some reason at level 10 the base delay reduction is 50%.
            val2 = (if skill_lv < 10 { 3 * skill_lv as i32 } else { 50 }) + status.int_ as i32 / 5; // After-cast delay reduction
            if sd {
                val1 += checkskill(BA_MUSICALLESSON);
                val2 += 2 * checkskill(BA_MUSICALLESSON);
            }
        }
        DC_DONTFORGETME => {
            // RENEWAL
            val1 = 3 * skill_lv as i32 + status.dex as i32 / 15; // ASPD decrease
            val2 = 2 * skill_lv as i32 + status.agi as i32 / 20; // Movement speed adjustment.
            if sd {
                val1 += checkskill(DC_DANCINGLESSON);
                val2 += checkskill(DC_DANCINGLESSON) / 2;
            }
            val1 *= 10; //Because 10 is actually 1% aspd
        }
        DC_SERVICEFORYOU => {
            val1 = 15 + skill_lv as i32 + (status.int_ as i32 / 10); // MaxSP percent increase
            val2 = 20 + 3 * skill_lv as i32 + (status.int_ as i32 / 10); // SP cost reduction
            if sd {
                val1 += checkskill(DC_DANCINGLESSON) / 2;
                val2 += checkskill(DC_DANCINGLESSON) / 2;
            }
        }
        BA_ASSASSINCROSS => {
            if sd {
                val1 = checkskill(BA_MUSICALLESSON) / 2;
            }
            val1 += 5 + skill_lv as i32 + (status.agi as i32 / 20);
            val1 *= 10; // ASPD works with 1000 as 100%
        }
        DC_FORTUNEKISS => {
            val1 = 10 + skill_lv as i32 + (status.luk as i32 / 10); // Critical increase
            val1 *= 10; //Because every 10 crit is an actual cri point.
            if sd {
                val1 += 5 * checkskill(DC_DANCINGLESSON);
            }
        }
        BD_DRUMBATTLEFIELD => {
            val1 = (skill_lv as i32 + 1) * 25; //Atk increase
            val2 = (skill_lv as i32 + 1) * 2; //Def increase
        }
        BD_RINGNIBELUNGEN => val1 = (skill_lv as i32 + 2) * 25, //Atk increase
        BD_RICHMANKIM => val1 = 25 + 11 * skill_lv as i32,      //Exp increase bonus.
        BD_SIEGFRIED => {
            val1 = 55 + skill_lv as i32 * 5; //Elemental Resistance
            val2 = skill_lv as i32 * 10; //Status ailment resistance
        }
        WE_CALLPARTNER => {
            if sd {
                val1 = crate::pc::character_of(c, src.id).map(|ch| ch.partner_id as i32).unwrap_or(0);
            }
        }
        WE_CALLPARENT => {
            if sd {
                if let Some(ch) = crate::pc::character_of(c, src.id) {
                    val1 = ch.father as i32;
                    val2 = ch.mother as i32;
                }
            }
        }
        WE_CALLBABY => {
            if sd {
                val1 = crate::pc::character_of(c, src.id).map(|ch| ch.child as i32).unwrap_or(0);
            }
        }
        NJ_KAENSIN => {
            group::skill_clear_group(c, src.id, 1); //Delete previous Kaensins/Suitons
            val2 = (skill_lv as i32 + 1) / 2 + 4;
        }
        NJ_SUITON => {
            group::skill_clear_group(c, src.id, 1);
        }
        GS_GROUNDDRIFT => {
            // Ground Drift Element is decided when it's placed.
            let ele = skill_get_ele(skill_id, skill_lv);
            let element = [ELE_WIND, ELE_DARK, ELE_POISON, ELE_WATER, ELE_FIRE];
            if ele == crate::battle::calc::ELE_RANDOM {
                val1 = element[(c.rnd() % 5) as usize]; // Use random from available unit visual?
            } else if ele == crate::battle::calc::ELE_ENDOWED {
                val1 = match &has_sc {
                    Some(s) => crate::status::calc_sc::status_calc_attack_element(s, 0) as i32,
                    None => 0,
                };
            } else if ele == crate::battle::calc::ELE_WEAPON {
                val1 = status.rhw.ele as i32;
                if let Some(e) = sce(SC_ENCHANTARMS) {
                    val1 = e.val1;
                }
            }
            match val1 {
                ELE_FIRE => subunt = 4,
                ELE_WATER => subunt = 3,
                ELE_POISON => subunt = 2,
                ELE_DARK => subunt = 1,
                ELE_WIND => {}
                _ => subunt = (c.rnd() % 5) as i32,
            }
            // (the C falls through into GC_POISONSMOKE? no: `break;` ends the GS_GROUNDDRIFT block)
        }
        GC_POISONSMOKE => {
            let Some(pw) = sce(SC_POISONINGWEAPON) else { return None };
            val2 = pw.val2; // Type of Poison
            val3 = pw.val1;
            limit = skill_get_time(skill_id, skill_lv) as i64;
        }
        NPC_COMET | WL_COMET => {
            // sc->comet_x / comet_y: read by the comet's damage calculation (skillcore agent)
            if has_sc.is_some() {
                super::iface::set_comet_pos(c, src.id, x, y);
            }
        }
        GD_LEADERSHIP | GD_GLORYWOUNDS | GD_SOULCOLD | GD_HAWKEYES => limit = 1000000, //it doesn't matter
        LG_BANDING => limit = -1,
        WM_POEMOFNETHERWORLD | WM_SEVERE_RAINSTORM | SO_WATER_INSIGNIA | SO_FIRE_INSIGNIA | SO_WIND_INSIGNIA | SO_EARTH_INSIGNIA => {
            // Can't be placed on top of Land Protector.
            if skill_id == WM_POEMOFNETHERWORLD && crate::npc::mapflag_gvg2(c, src.map) {
                target = BCT_ALL;
            }
            if map_cell::getcell(c, Some(src.map), x, y, CELL_CHKLANDPROTECTOR) != 0 {
                return None;
            }
        }
        SO_CLOUD_KILL | NPC_CLOUD_KILL => {
            group::skill_clear_group(c, src.id, 4);
        }
        SO_WARMER => {
            group::skill_clear_group(c, src.id, 8);
        }
        SO_FIREWALK | SO_ELECTRICWALK => limit = skill_get_time2(skill_id, skill_lv) as i64,
        GN_WALLOFTHORN => {
            // Turns to Firewall
            if flag & 1 != 0 {
                limit = 3000;
            }
            val3 = ((x as i32) << 16) | y as i32;
        }
        GN_DEMONIC_FIRE => {
            if flag != 0 {
                // Fire Expansion level 1
                limit = flag as i64 + 10000;
                flag = 0;
            }
        }
        GN_FIRE_EXPANSION_SMOKE_POWDER | GN_FIRE_EXPANSION_TEAR_GAS => {
            limit = ((if sd { checkskill(GN_DEMONIC_FIRE) } else { 1 }) as i64 + 1) * limit;
        }
        KO_ZENKAI => {
            // sd->spiritcharm / spiritcharm_type: skillcore's PcSkill (skill/state.rs)
            let charms = if sd { Some(crate::skill::state::get(c, src.id)) } else { None };
            if let Some(ch) = charms.filter(|s| s.spiritcharm_type != crate::skill::state::CHARM_TYPE_NONE && s.spiritcharm > 0) {
                val1 = ch.spiritcharm as i32;
                val2 = ch.spiritcharm_type;
                limit = 6000 * val1 as i64;
                subunt = ch.spiritcharm_type - 1;
                crate::skill::state::pc_delspiritcharm(c, src.id, ch.spiritcharm as i32, ch.spiritcharm_type);
            }
        }
        // (HW_GRAVITATION's link group: pre-renewal only)
        SO_VACUUM_EXTREME => {
            // Coordinates
            val1 = x as i32;
            val2 = y as i32;
            val3 = 0; // Suck target at n seconds.
        }
        MH_POISON_MIST | MH_BLAST_FORGE | MH_LAVA_SLIDE => {
            group::skill_clear_group(c, src.id, 1);
        }
        MH_VOLCANIC_ASH => {
            if !map_flag_vs(c, src.map) {
                target = BCT_ENEMY;
            }
        }
        AG_VIOLENT_QUAKE | AG_ALL_BLOOM => {
            if let Some(cl) = sce(SC_CLIMAX) {
                if skill_id == AG_ALL_BLOOM && cl.val1 == 1 {
                    limit /= 2; // Rose buds spawn at double the speed, so rose garden duration must be halved.
                } else if cl.val1 == 4 {
                    limit = 3000; // Show main AoE for fixed duration on status giving effect.
                }
            }
        }
        AG_VIOLENT_QUAKE_ATK | AG_ALL_BLOOM_ATK | AG_ALL_BLOOM_ATK2 => {
            limit = flag as i64;
            flag = 0;
            if skill_id == AG_VIOLENT_QUAKE_ATK && sce(SC_CLIMAX).map(|cl| cl.val1 == 2).unwrap_or(false) {
                range = 4; // Rising rocks splash is increased to 9x9.
            }
        }
        WH_DEEPBLINDTRAP | WH_SOLIDTRAP | WH_SWIFTTRAP | WH_FLAMETRAP => {
            limit += 3000 * (if sd { checkskill(WH_ADVANCED_TRAP) } else { 5 }) as i64;
        }
        NW_GRENADES_DROPPING => limit = skill_get_time2(skill_id, skill_lv) as i64,
        _ => {}
    }

    // Init skill unit group
    let unit_id = (if flag & 1 != 0 { skill.unit_id2 } else { skill.unit_id }) as i32 + subunt;
    let mut group = group::skill_initunitgroup(c, &src, layout.count, skill_id, skill_lv, unit_id, limit, interval)?;
    let group_id = group.group_id;
    group.val1 = val1;
    group.val2 = val2;
    group.val3 = val3;
    group.link_group_id = link_group_id;
    group.target_flag = target;
    group.bl_flag = skill_get_unit_bl_target(skill_id);
    // Store if this skill needs to consume ammo.
    group.ammo_consume = sd && iface::arrow_atk(c, src.id) && skill_id != GS_GROUNDDRIFT && flag & UNIT_NOCONSUME_AMMO == 0;
    group.song_dance = ((skill.has_unit_flag(UF_DANCE) || skill.has_unit_flag(UF_SONG)) as u8) | ((skill.has_unit_flag(UF_ENSEMBLE) as u8) << 1); //Signals if this is a song/dance/duet
    group.guildaura = (GD_LEADERSHIP..=GD_HAWKEYES).contains(&skill_id);
    group.item_id = req_item;
    // If tick is greater than current, do not invoke onplace function just yet. [Skotlex]
    let active_flag = group.tick - c.tick() <= SKILLUNITTIMER_INTERVAL;
    // Put message for Talkie Box & Graffiti
    if skill_id == HT_TALKIEBOX || skill_id == RG_GRAFFITI {
        let msg = if sd {
            iface::skill_message(c, src.id)
        } else {
            //Eh... we have to write something here... even though mobs shouldn't use this. [Skotlex]
            "Boo!".to_string()
        };
        // safestrncpy(group->valstr, …, MESSAGE_SIZE)
        let bytes = crate::net::wire::latin1(&msg);
        group.valstr = Some(bytes.iter().take(MESSAGE_SIZE - 1).map(|&b| b as char).collect());
    }
    data::put_group(c, &group);
    // Dance skill
    if group.song_dance != 0 {
        if sd {
            data::set_dance(c, src.id, skill_id, skill_lv);
        }
        let started = sc::sc_start4(
            c,
            Some(&src),
            &src,
            SC_DANCING,
            100,
            skill_id as i32,
            group.group_id,
            skill_lv as i32,
            if group.song_dance & 2 != 0 { BCT_SELF } else { 0 },
            limit + 1000,
        );
        if started && sd && group.song_dance & 2 != 0 && skill_id != CG_HERMODE {
            //Hermod is a encore with a warp!
            let mut lv = skill_lv;
            super::castend::skill_check_pc_partner(c, src.id, skill_id, &mut lv, 1, 1);
        }
    }
    // Set skill unit
    let limit = data::skill_id2group(c, group_id).map(|g| g.limit).unwrap_or(limit);
    for i in 0..layout.count as usize {
        let ux = x as i32 + layout.dx[i];
        let uy = y as i32 + layout.dy[i];
        let mut unit_val1 = skill_lv as i32;
        let mut unit_val2 = 0i32;
        let mut alive = 1i32;
        // are the coordinates out of range?
        if ux <= 0 || uy <= 0 || ux >= md.xs as i32 || uy >= md.ys as i32 {
            continue;
        }
        let (ux, uy) = (ux as i16, uy as i16);
        let song_dance = data::skill_id2group(c, group_id).map(|g| g.song_dance).unwrap_or(0);
        if song_dance == 0 && map_cell::getcell(c, Some(src.map), ux, uy, CELL_CHKREACH) == 0 {
            continue; // don't place skill units on walls (except for songs/dances/encores)
        }
        if conf::skill_wall_check()
            && skill.has_unit_flag(UF_PATHCHECK)
            && !crate::path::path_search_long(src.map, ux, uy, src.x, src.y, crate::map::CellChk::ChkWall)
        {
            continue; // no path between cell and caster
        }
        match skill_id {
            // HP for Skill unit that can be damaged, see also skill_unit_ondamaged
            HT_LANDMINE | MA_LANDMINE | HT_ANKLESNARE | HT_SHOCKWAVE | HT_SANDMAN | MA_SANDMAN | HT_FLASHER | HT_FREEZINGTRAP
            | MA_FREEZINGTRAP | HT_SKIDTRAP | MA_SKIDTRAP | HT_CLAYMORETRAP | HT_BLASTMINE | sk::SC_ESCAPE => unit_val1 = 3500,
            MG_FIREWALL | NJ_KAENSIN => unit_val2 = data::skill_id2group(c, group_id).map(|g| g.val2).unwrap_or(0),
            CR_GRANDCROSS | NPC_GRANDDARKNESS => {
                unit_val1 = skill_get_time(skill_id, skill_lv) / interval; //Default: 950/300 = 3 hits
            }
            WZ_ICEWALL => {
                unit_val1 = 200 + 200 * skill_lv as i32;
                unit_val2 = map_cell::getcell(c, Some(src.map), ux, uy, CELL_GETTYPE);
            }
            WZ_WATERBALL => {
                //Check if there are cells that can be turned into waterball units
                if !sd
                    || map_cell::getcell(c, Some(src.map), ux, uy, CELL_CHKWATER) != 0
                    || super::movement::map_find_skill_unit_oncell(c, &src, ux, uy, SA_DELUGE, None, 1).is_some()
                    || super::movement::map_find_skill_unit_oncell(c, &src, ux, uy, NJ_SUITON, None, 1).is_some()
                {
                    //Turn water, deluge or suiton into waterball cell
                } else {
                    continue;
                }
            }
            GS_DESPERADO => {
                unit_val1 = layout.dx[i].abs();
                unit_val2 = layout.dy[i].abs();
                if unit_val1 < 2 || unit_val2 < 2 {
                    //Nearby cross, linear decrease with no diagonals
                    if unit_val2 > unit_val1 {
                        unit_val1 = unit_val2;
                    }
                    if unit_val1 != 0 {
                        unit_val1 -= 1;
                    }
                    unit_val1 = 36 - 12 * unit_val1;
                } else {
                    //Diagonal edges
                    unit_val1 = 28 - 4 * unit_val1 - 4 * unit_val2;
                }
                if unit_val1 < 1 {
                    unit_val1 = 1;
                }
                unit_val2 = 0;
            }
            NPC_REVERBERATION => unit_val1 = 1 + skill_lv as i32,
            WM_POEMOFNETHERWORLD => unit_val1 = 1 + skill_lv as i32,
            GN_WALLOFTHORN => {
                if flag & 1 == 0 {
                    // (flag&1: turned into a Firewall)
                    unit_val1 = 2000 + 2000 * skill_lv as i32; // HP
                    unit_val2 = 20; // Max hits
                }
            }
            RL_B_TRAP => {
                unit_val1 = 3500;
                unit_val2 = 0;
            }
            _ => {
                if song_dance & 0x1 != 0 {
                    // Songs / Dances
                    // val1: Original skill ID (needed for dissonance overlap check)
                    // val2: Store whether this is a song/dance
                    unit_val1 = data::skill_id2group(c, group_id).map(|g| g.skill_id as i32).unwrap_or(skill_id as i32);
                    unit_val2 = if skill.has_unit_flag(UF_DANCE) {
                        1 << UF_DANCE
                    } else if skill.has_unit_flag(UF_SONG) {
                        1 << UF_SONG
                    } else {
                        0
                    };
                }
            }
        }
        if skill.has_unit_flag(UF_RANGEDSINGLEUNIT) && i == (layout.count / 2) as usize {
            unit_val2 |= 1 << UF_RANGEDSINGLEUNIT; // center.
        }
        if sd && map_cell::getcell(c, Some(src.map), ux, uy, CELL_CHKMAELSTROM) != 0 {
            //Does not recover SP from monster skills
            let ids = util::ids_in_cell(c, src.map, ux, uy, BL_SKILL);
            util::each(c, &ids, |bl| skill_maelstrom_suction(c, bl, skill_id, skill_lv));
        }
        // Check active cell to failing or remove current unit
        let ids = util::ids_in_cell(c, src.map, ux, uy, BL_SKILL);
        util::each(c, &ids, |bl| skill_cell_overlap(c, bl, skill_id, &mut alive, &src));
        if alive == 0 {
            continue;
        }
        let unit = match group::skill_initunit(c, group_id, i as i32, ux, uy, unit_val1, unit_val2, hidden, range, limit) {
            InitUnit::Failed => return None,
            InitUnit::NotPlaced => {
                // the slot rAthena returns is dead and zeroed: `unit->limit *= 2` keeps 0 and the group limit becomes
                // 0; the onplace below finds a dead unit and does nothing
                if skill_id == PF_FOGWALL && alive == 2 {
                    data::upd_group(c, group_id, |g| g.limit = 0);
                }
                continue;
            }
            InitUnit::Placed(u) => u,
        };
        if skill_id == PF_FOGWALL && alive == 2 {
            //Double duration of cells on top of Deluge/Suiton
            let mut u = unit.clone();
            u.limit *= 2;
            data::put_unit(c, &u);
            data::upd_group(c, group_id, |g| g.limit = u.limit);
        }
        // Execute on all targets standing on this cell
        if range == 0 && active_flag {
            let bl_flag = data::skill_id2group(c, group_id).map(|g| g.bl_flag).unwrap_or(0);
            super::effect::foreachincell_unit_effect(c, &unit, unit.bl.map, unit.bl.x, unit.bl.y, bl_flag, c.tick(), 1);
        }
    }
    let Some(g) = data::skill_id2group(c, group_id) else { return None };
    if g.alive_count == 0 {
        //No cells? Something that was blocked completely by Land Protector?
        group::skill_delunitgroup(c, group_id);
        return None;
    }
    //success, unit created.
    if skill_id == NJ_TATAMIGAESHI {
        //Store number of tiles.
        data::upd_group(c, group_id, |g| g.val1 = g.alive_count);
    }
    Some(group_id)
}

/// skill.cpp skill_unitsetmapcell: the cell flag on the unit's cell and `unit range` cells around it.
pub fn skill_unitsetmapcell(c: &Ctx, src: &SkillUnit, skill_id: u16, skill_lv: u16, cell: i32, flag: bool) {
    let range = skill_get_unit_range(skill_id, skill_lv) as i16;
    for y in (src.bl.y - range)..=(src.bl.y + range) {
        for x in (src.bl.x - range)..=(src.bl.x + range) {
            map_cell::setcell(c, Some(src.bl.map), x, y, cell, flag);
        }
    }
}

/// skill.cpp skill_cell_overlap: a unit already on the new unit's cell may be removed, or stop the new one
/// (`*alive` 0), or (Fog Wall on Deluge / Suiton) double its duration (`*alive` 2).
pub fn skill_cell_overlap(c: &Ctx, bl: &Unit, skill_id: u16, alive: &mut i32, _src: &Unit) -> i32 {
    let Some(unit) = data::get_unit(c, bl.id) else { return 0 };
    let Some(ug) = data::skill_id2group(c, unit.group_id) else { return 0 };
    if *alive == 0 {
        return 0;
    }
    if ug.guildaura {
        /* guild auras are not cancelled! */
        return 0;
    }
    match skill_id {
        SA_LANDPROTECTOR => {
            if ug.skill_id == SA_LANDPROTECTOR {
                //Check for offensive Land Protector to delete both. [Skotlex]
                *alive = 0;
                group::skill_delunit(c, unit.id());
                return 1;
            }
            let skill = skill_db().find(ug.skill_id);
            //It deletes everything except traps and barriers
            let (istrap, ignorelp, ranged) = skill
                .map(|s| (s.has_inf2(INF2_ISTRAP), s.has_inf2(INF2_IGNORELANDPROTECTOR), s.has_unit_flag(UF_RANGEDSINGLEUNIT)))
                .unwrap_or((false, false, false));
            if (!istrap && !ignorelp) || ug.skill_id == WZ_FIREPILLAR {
                if ranged {
                    if unit.val2 & (1 << UF_RANGEDSINGLEUNIT) != 0 {
                        group::skill_delunitgroup(c, ug.group_id);
                    }
                } else {
                    group::skill_delunit(c, unit.id());
                }
                return 1;
            }
        }
        GN_CRAZYWEED_ATK | HW_GANBANTEIN | LG_EARTHDRIVE => {
            if !(skill_id == GN_CRAZYWEED_ATK && skill_get_unit_flag(ug.skill_id, &[UF_CRAZYWEEDIMMUNE])) {
                // Officially songs/dances are removed
                if skill_get_unit_flag(ug.skill_id, &[UF_RANGEDSINGLEUNIT]) {
                    if unit.val2 & (1 << UF_RANGEDSINGLEUNIT) != 0 {
                        group::skill_delunitgroup(c, ug.group_id);
                    }
                } else {
                    group::skill_delunit(c, unit.id());
                }
                return 1;
            }
        }
        SA_VOLCANO | SA_DELUGE | SA_VIOLENTGALE => {
            // The official implementation makes them fail to appear when casted on top of ANYTHING
            if unit.range <= 0 && skill_get_unit_id(ug.skill_id) != UNT_DUMMYSKILL {
                *alive = 0;
                return 1;
            }
        }
        PF_FOGWALL => match ug.skill_id {
            SA_VOLCANO | SA_VIOLENTGALE => {
                //Can't be placed on top of these
                *alive = 0;
                return 1;
            }
            SA_DELUGE | NJ_SUITON => {
                //Cheap 'hack' to notify the calling function that duration should be doubled [Skotlex]
                *alive = 2;
            }
            _ => {}
        },
        WZ_WATERBALL => {
            if matches!(ug.skill_id, SA_DELUGE | NJ_SUITON) {
                //Consumes deluge/suiton
                group::skill_delunit(c, unit.id());
                return 1;
            }
        }
        WZ_ICEWALL => {
            // (HP_BASILICA, HW_GRAVITATION: pre-renewal only)
            //These can't be placed on top of themselves (duration can't be refreshed)
            if ug.skill_id == skill_id {
                *alive = 0;
                return 1;
            }
        }
        RL_FIRE_RAIN => {
            if skill_get_unit_flag(ug.skill_id, &[UF_REMOVEDBYFIRERAIN]) {
                if skill_get_unit_flag(ug.skill_id, &[UF_RANGEDSINGLEUNIT]) {
                    if unit.val2 & (1 << UF_RANGEDSINGLEUNIT) != 0 {
                        group::skill_delunitgroup(c, ug.group_id);
                    }
                } else {
                    group::skill_delunit(c, unit.id());
                }
                return 1;
            }
        }
        SS_KUNAIWAIKYOKU => {
            if ug.skill_id == skill_id {
                group::skill_delunit(c, unit.id());
                return 1;
            }
        }
        _ => {}
    }
    if ug.skill_id == SA_LANDPROTECTOR && !skill_get_inf2(skill_id, &[INF2_ISTRAP]) && !skill_get_inf2(skill_id, &[INF2_IGNORELANDPROTECTOR]) {
        //It deletes everything except traps and barriers
        *alive = 0;
        return 1;
    }
    0
}

/// skill.cpp skill_maelstrom_suction: a Maelstrom under a new unit gives its caster SP.
pub fn skill_maelstrom_suction(c: &Ctx, bl: &Unit, skill_id: u16, skill_lv: u16) -> i32 {
    let Some(unit) = data::get_unit(c, bl.id) else { return 0 };
    let Some(ug) = data::skill_id2group(c, unit.group_id) else { return 0 };
    if skill_get_inf2(skill_id, &[INF2_ISTRAP]) {
        return 0;
    }
    if ug.skill_id == sk::SC_MAELSTROM {
        if let Some(src) = unit::get(c, ug.src_id) {
            let mut sp = ug.skill_lv as i32 * skill_lv as i32;
            if src.kind == BL_PC {
                sp += crate::pc::character_of(c, src.id).map(|ch| ch.job_level as i32).unwrap_or(0) / 5;
            }
            crate::status::hp::status_heal(c, src.id, 0, (sp / 2) as i64, 0, 1);
        }
    }
    0
}

// ---------------------------------------------------------------------------------------------- dissonance

/// skill.cpp skill_dance_overlap_revert: a dissonance-overlapped unit turns back into its song or dance.
pub fn skill_dance_overlap_revert(c: &Ctx, unit_id: u32) {
    let Some(su) = data::get_unit(c, unit_id) else { return };
    // Need to delete previous unit on the client as it can't handle unit_id changes
    super::clif::clif_skill_delunit(c, &su);
    // Remove overlap signal
    data::upd_unit(c, unit_id, |u| u.val2 &= !(1 << UF_ENSEMBLE));
    // If the unit is removed because overlap dissonance killed the caster, we need to reset it here
    skill_dance_switch(c, unit_id, true);
    // Respawn the unit with the original unit_id
    if let Some(su) = data::get_unit(c, unit_id) {
        super::clif::skill_getareachar_skillunit_visibilty(c, &su, crate::clif::Target::Area);
    }
}

/// skill.cpp skill_dance_overlap_sub: `target` (a unit on the same cell as `src`) and `src` become (OVERLAP_SET) or
/// stop being (OVERLAP_REMOVE) dissonance when a song meets a dance; OVERLAP_COUNT only counts.
fn skill_dance_overlap_sub(c: &Ctx, target_id: u32, src_id: u32, flag: i32) -> i32 {
    let (Some(target), Some(src)) = (data::get_unit(c, target_id), data::get_unit(c, src_id)) else { return 0 };
    let (Some(tg), Some(sg)) = (data::skill_id2group(c, target.group_id), data::skill_id2group(c, src.group_id)) else { return 0 };
    if src_id == target_id {
        return 0;
    }
    if sg.song_dance & 0x1 == 0 || tg.song_dance & 0x1 == 0 {
        return 0;
    }
    if target.val2 & src.val2 & !(1 << UF_ENSEMBLE) == 0 {
        //They don't match (song + dance) is valid.
        return 0;
    }
    // Just want to count the unit, stop here
    if flag == OVERLAP_COUNT {
        return 1;
    }
    // These action needs to happen for both target (0) and the src (1)
    for id in [target_id, src_id] {
        let Some(unit) = data::get_unit(c, id) else { continue };
        if flag == OVERLAP_SET {
            if unit.val2 & (1 << UF_ENSEMBLE) == 0 {
                // Set dissonance
                // Need to delete previous unit on the client as it can't handle unit_id changes
                super::clif::clif_skill_delunit(c, &unit);
                // Add ensemble to signal this unit is overlapping.
                data::upd_unit(c, id, |u| u.val2 |= 1 << UF_ENSEMBLE);
                if let Some(unit) = data::get_unit(c, id) {
                    super::clif::skill_getareachar_skillunit_visibilty(c, &unit, crate::clif::Target::Area);
                    // Trigger onleft event on all affected units on the cell
                    let bl_flag = data::skill_id2group(c, unit.group_id).map(|g| g.bl_flag).unwrap_or(0);
                    super::effect::foreachincell_unit_effect(c, &unit, unit.bl.map, unit.bl.x, unit.bl.y, bl_flag, c.tick(), 4 | 8);
                }
            }
        } else if flag == OVERLAP_REMOVE {
            if unit.val2 & (1 << UF_ENSEMBLE) != 0 {
                // Remove dissonance
                skill_dance_overlap_revert(c, id);
                // Trigger onplace event on all affected units on the cell
                // Don't do this for src as that unit is to be removed
                if id == target_id {
                    if let Some(unit) = data::get_unit(c, id) {
                        let bl_flag = data::skill_id2group(c, unit.group_id).map(|g| g.bl_flag).unwrap_or(0);
                        super::effect::foreachincell_unit_effect(c, &unit, unit.bl.map, unit.bl.x, unit.bl.y, bl_flag, c.tick(), 1 | 8);
                    }
                }
            }
        }
    }
    1
}

/// skill.cpp skill_dance_overlap: the song / dance overlapping->dissonance check of a unit that was placed or is about
/// to be removed. Returns the number of other units on the cell that overlap with it.
pub fn skill_dance_overlap(c: &Ctx, unit_id: u32, flag: i32) -> i32 {
    let Some(unit) = data::get_unit(c, unit_id) else { return 0 };
    let Some(g) = data::skill_id2group(c, unit.group_id) else { return 0 };
    if g.song_dance & 0x1 == 0 {
        return 0;
    }
    let cell = |c: &Ctx| util::ids_in_cell(c, unit.bl.map, unit.bl.x, unit.bl.y, BL_SKILL);
    if flag == OVERLAP_REMOVE {
        // Nothing to remove, this unit is not overlapped
        if unit.val2 & (1 << UF_ENSEMBLE) == 0 {
            return 0;
        }
        // When the unit is removed, but there are more than one other skill unit that overlaps,
        // dissonance is only removed from the unit that is removed and not the other units
        let ids = cell(c);
        let count = util::each(c, &ids, |bl| skill_dance_overlap_sub(c, bl.id, unit_id, OVERLAP_COUNT));
        if count > 1 {
            // Remove dissonance
            skill_dance_overlap_revert(c, unit_id);
            return count;
        }
    }
    let ids = cell(c);
    util::each(c, &ids, |bl| skill_dance_overlap_sub(c, bl.id, unit_id, flag))
}

/// skill.cpp skill_dance_switch: while a song / dance unit overlaps another (val2 & 1<<UF_ENSEMBLE), its group is
/// handled as Dissonance / Ugly Dance (revert false), or turned back (revert true). Returns whether it overlaps.
pub fn skill_dance_switch(c: &Ctx, unit_id: u32, revert: bool) -> bool {
    let Some(unit) = data::get_unit(c, unit_id) else { return false };
    let Some(mut group) = data::skill_id2group(c, unit.group_id) else { return false };
    // Not a song or dance
    if group.song_dance & 0x1 == 0 {
        return false;
    }
    // val2&(1 << UF_ENSEMBLE) signalizes if the unit is currently overlapping with another song/dance
    let overlap = unit.val2 & (1 << UF_ENSEMBLE) != 0;
    // No need to convert if there is no overlap
    // But we still need to revert even if the cell is no longer overlapping
    if !revert && !overlap {
        return false;
    }
    // Transform or restore the skill group depending on flag
    let skill_id = if !revert && overlap {
        //Transform
        if unit.val2 & (1 << UF_SONG) != 0 {
            BA_DISSONANCE
        } else {
            DC_UGLYDANCE
        }
    } else {
        //Restore (val1 contains original skill ID)
        unit.val1 as u16
    };
    group.skill_id = skill_id;
    group.unit_id = skill_get_unit_id(skill_id);
    group.target_flag = skill_get_unit_target(skill_id);
    group.interval = skill_get_unit_interval(skill_id);
    data::put_group(c, &group);
    overlap
}
