//! The smaller skill.cpp helpers the pipeline uses: skill_get_range2 (every unit type), skill_calc_heal,
//! skill_isCopyable, skill_block_check, skill_disable_check, skill_get_elemental_type, skill_check_unit_movepos;
//! and what they need from elsewhere: party.cpp party_skill_check, pc.cpp pc_steal_item / pc_skillheal_bonus /
//! pc_skillheal2_bonus, status.cpp status_base_matk_min / status_base_matk_max (RENEWAL, every unit type).

use crate::db::skills::*;
use crate::map::{BL_ELEM, BL_HOM, BL_MER, BL_MOB, BL_PC, BL_PET};
use crate::status::consts::*;
use crate::status::data::StatusData;
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::Ctx;

use super::conf;
use crate::status::consts::SC_NONE;

/// skill.cpp skill_get_range2: the skill's cast range for this unit (weapon range for negative ranges when
/// use_weapon_skill_range covers the unit, the range-altering skills of players, 9 for non-players' self skills).
pub fn skill_get_range2(c: &Ctx, bl: &Unit, skill_id: u16, skill_lv: u16, is_server: bool) -> i32 {
    if bl.kind == BL_MOB && conf::mob_ai() & 0x400 != 0 {
        return 9; // Mobs have a range of 9 regardless of skill used.
    }
    if bl.kind == BL_PC {
        // the player's range: skilltree.rs port (the range-altering skills read the player's skill levels)
        let mut range = skill_get_range(skill_id, skill_lv);
        if range < 0 && conf::use_weapon_skill_range() & bl.kind != 0 {
            return crate::status::status_get_status_data(c, bl).rhw.range as i32;
        }
        range = crate::skilltree::skill_get_range2_pc(c, bl.id, skill_id, skill_lv, is_server);
        return range;
    }
    let mut range = skill_get_range(skill_id, skill_lv);
    if range < 0 {
        if conf::use_weapon_skill_range() & bl.kind != 0 {
            return crate::status::status_get_status_data(c, bl).rhw.range as i32;
        }
        range *= -1;
    }
    if is_server && range > 14 {
        range = 14; // Server-sided base range can't be above 14
    }
    let Some(skill) = skill_db().find(skill_id) else { return range };
    if skill.has_inf2(INF2_ALTERRANGEVULTURE) || skill.has_inf2(INF2_ALTERRANGESNAKEEYE) {
        range += conf::mob_eye_range_bonus();
    }
    // (INF2_ALTERRANGESHADOWJUMP / RADIUS / RESEARCHTRAP only change players' ranges)
    if range == 0 {
        return 9; // Enable non players to use self skills on others. [Skotlex]
    }
    range
}

/// status.cpp status_base_matk_min (RENEWAL).
pub fn status_base_matk_min(c: &Ctx, bl: &Unit, status: &StatusData, level: i32) -> u16 {
    match bl.kind {
        BL_PET | BL_MOB | BL_MER | BL_ELEM => (status.int_ as i32 + level + status.rhw.matk as i32 * 70 / 100) as u16,
        // BL_HOM: status_get_homint + level + (homint + homdex) / 5 (homunculus/status.rs)
        BL_HOM => crate::homunculus::status::status_base_matk_min_of(c, bl.id, status, level),
        _ => crate::status::calc::status_base_matk_pc(status, level),
    }
}

/// status.cpp status_base_matk_max (RENEWAL).
pub fn status_base_matk_max(c: &Ctx, bl: &Unit, status: &StatusData, level: i32) -> u16 {
    match bl.kind {
        BL_PET | BL_MOB | BL_MER | BL_ELEM => (status.int_ as i32 + level + status.rhw.matk as i32 * 130 / 100) as u16,
        // BL_HOM: status_get_homint + level + (homluk + homint + homdex) / 3 (homunculus/status.rs)
        BL_HOM => crate::homunculus::status::status_base_matk_max_of(c, bl.id, status, level),
        _ => crate::status::calc::status_base_matk_pc(status, level),
    }
}

/// pc.cpp pc_skillheal_bonus (bHealPower with skill_add_heal_rate, bSkillHeal).
pub fn pc_skillheal_bonus(c: &Ctx, aid: u32, skill_id: u16) -> i32 {
    let Some(sd) = crate::status::data::sd_get(c, aid) else { return 0 };
    let mut bonus = sd.bonus.add_heal_rate;
    let skill_id = skill_dummy2skill_id(skill_id);
    if bonus != 0 {
        let r = conf::skill_add_heal_rate();
        let off = match skill_id {
            AL_HEAL => r & 1 == 0,
            PR_SANCTUARY => r & 2 == 0,
            AM_POTIONPITCHER => r & 4 == 0,
            CR_SLIMPITCHER => r & 8 == 0,
            BA_APPLEIDUN => r & 16 == 0,
            AB_CHEAL => r & 32 == 0,
            AB_HIGHNESSHEAL => r & 64 == 0,
            CD_MEDIALE_VOTUM => r & 128 == 0,
            CD_DILECTIO_HEAL => r & 256 == 0,
            _ => false,
        };
        if off {
            bonus = 0;
        }
    }
    if let Some(it) = sd.lists.skillheal.iter().find(|it| it.id == skill_id) {
        bonus += it.val;
    }
    bonus
}

/// pc.cpp pc_skillheal2_bonus (bHealPower2, bSkillHeal2).
pub fn pc_skillheal2_bonus(c: &Ctx, aid: u32, skill_id: u16) -> i32 {
    let Some(sd) = crate::status::data::sd_get(c, aid) else { return 0 };
    let mut bonus = sd.bonus.add_heal2_rate;
    let skill_id = skill_dummy2skill_id(skill_id);
    if let Some(it) = sd.lists.skillheal2.iter().find(|it| it.id == skill_id) {
        bonus += it.val;
    }
    bonus
}

/// skill.cpp skill_calc_heal (RENEWAL): the heal of a healing skill (`heal` false: its damage part, halved).
pub fn skill_calc_heal(c: &Ctx, src: &Unit, target: Option<&Unit>, skill_id: u16, skill_lv: u16, heal: bool) -> i32 {
    let is_sd = src.kind == BL_PC;
    let tsd = target.filter(|t| t.kind == BL_PC);
    let st = crate::status::status_get_status_data(c, src);
    let lv = crate::status::status_get_lv(c, src);
    let chk = |id| if is_sd { crate::skill::checkskill(c, src.id, id) as i32 } else { 0 };
    let mut hp_bonus: i32 = 0;
    let mut global_bonus: f64 = 1.0;
    let mut hp: i32;
    match skill_id {
        PR_SANCTUARY => hp = if skill_lv > 6 { 777 } else { skill_lv as i32 * 100 },
        NPC_EVILLAND => hp = if skill_lv > 6 { 666 } else { skill_lv as i32 * 100 },
        AB_HIGHNESSHEAL => {
            hp = ((st.int_ as i32 + lv) / 5) * 30;
            let skill = chk(HP_MEDITATIO);
            if is_sd && skill > 0 {
                hp_bonus += skill * 2;
            }
        }
        SU_FRESHSHRIMP => hp = (lv + st.int_ as i32) / 5 * 6,
        SU_BUNCHOFSHRIMP => hp = (lv + st.int_ as i32) / 5 * 15,
        CD_MEDIALE_VOTUM | CD_DILECTIO_HEAL => {
            // Does the learned level of heal affect this skill?
            hp = (lv + st.int_ as i32) / 5 * 30;
            let skill = chk(HP_MEDITATIO);
            if is_sd && skill > 0 {
                hp_bonus += skill * 2;
            }
        }
        SOA_TALISMAN_OF_PROTECTION => {
            // (rAthena calls pc_checkskill(sd, …) with a possibly null sd: 0 for a non-player)
            hp = (500 + chk(SOA_TALISMAN_MASTERY) * 50) * skill_lv as i32 * lv / 100;
            hp += (lv + st.int_ as i32) / 5 * 30 * st.crt as i32 / 100;
        }
        _ => {
            if skill_lv >= conf::max_heal_lv() {
                return conf::max_heal();
            }
            // Renewal Heal Formula: ( [(Base Level + INT) / 5] x 30 ) x (Heal Level / 10) x (Modifiers) + MATK
            hp = (lv + st.int_ as i32) / 5 * 30 * skill_lv as i32 / 10;
            let skill = chk(HP_MEDITATIO);
            if is_sd && skill > 0 {
                hp_bonus += skill * 2;
            } else if src.kind == BL_HOM {
                // (skill.cpp:596, RENEWAL) hom_checkskill(hd, HLIF_BRAIN) > 0: hp_bonus += skill * 2
                let skill = crate::homunculus::hom_checkskill_id(c, src.id, HLIF_BRAIN) as i32;
                if skill > 0 {
                    hp_bonus += skill * 2;
                }
            }
            if let Some(t) = tsd {
                let partner = crate::pc::character_of(c, src.id).map(|ch| ch.partner_id).unwrap_or(0);
                let t_char = crate::pc::character_of(c, t.id).map(|ch| ch.id).unwrap_or(0);
                let class_ = crate::status::data::sd_get(c, src.id).map(|s| s.class_).unwrap_or(0);
                let sex = crate::pc::character_of(c, src.id).map(|ch| ch.sex).unwrap_or(1);
                if is_sd && partner == t_char && class_ & crate::db::jobs::MAPID_SECONDMASK == crate::db::jobs::MAPID_SUPER_NOVICE && sex == 0 {
                    hp *= 2;
                }
            }
        }
    }
    if (!heal || target.map(|t| t.kind == BL_MER).unwrap_or(false)) && skill_id != NPC_EVILLAND {
        hp /= 2;
    }
    if is_sd {
        if chk(SU_POWEROFSEA) > 0 {
            hp_bonus += 10;
            if let Some(sdd) = crate::battle::bl::sd(c, src) {
                if crate::battle::bl::checkskill_summoner(c, &sdd, crate::status::defs::SUMMONER_POWER_SEA) >= 20 {
                    hp_bonus += 20;
                }
            }
        }
        let skill = chk(NV_BREAKTHROUGH);
        if skill > 0 {
            hp_bonus += 2 * skill;
        }
        let skill = chk(NV_TRANSCENDENCE);
        if skill > 0 {
            hp_bonus += 3 * skill;
        }
        let skill = pc_skillheal_bonus(c, src.id, skill_id);
        if skill != 0 {
            hp_bonus += skill;
        }
    }
    if let Some(t) = tsd {
        let skill = pc_skillheal2_bonus(c, t.id, skill_id);
        if skill != 0 {
            hp_bonus += skill;
        }
    }
    let scl = crate::battle::bl::sc_nonempty(c, src);
    if let Some(s) = &scl {
        if s.has(SC_OFFERTORIUM) && matches!(skill_id, AB_HIGHNESSHEAL | AB_CHEAL | NPC_CHEAL | PR_SANCTUARY | AL_HEAL | CD_DILECTIO_HEAL | CD_MEDIALE_VOTUM) {
            hp_bonus += s.v2(SC_OFFERTORIUM);
        }
        if s.has(SC_MEDIALE) && skill_id == CD_MEDIALE_VOTUM {
            hp_bonus += s.v2(SC_MEDIALE);
        }
    }
    let tscl = target.and_then(|t| crate::battle::bl::sc_nonempty(c, t));
    if let Some(ts) = &tscl {
        if skill_id != NPC_EVILLAND && skill_id != BA_APPLEIDUN {
            if ts.has(SC_INCHEALRATE) {
                hp_bonus += ts.v1(SC_INCHEALRATE); // Only affects Heal, Sanctuary and PotionPitcher.(like bHealPower) [Inkfish]
            }
            if ts.has(SC_ANCILLA) {
                hp_bonus += ts.v1(SC_ANCILLA);
            }
            if ts.has(SC_ASSUMPTIO) {
                hp_bonus += ts.v1(SC_ASSUMPTIO) * 2;
            }
        }
    }
    if hp_bonus != 0 && skill_id != SOA_TALISMAN_OF_PROTECTION {
        hp += hp * hp_bonus / 100;
    }
    // MATK part of the RE heal formula [malufett]
    // Note: in this part matk bonuses from items or skills are not applied
    match skill_id {
        BA_APPLEIDUN | PR_SANCTUARY | NPC_EVILLAND => {}
        _ => {
            let mut min = status_base_matk_min(c, src, &st, lv) as i32;
            let mut max = status_base_matk_max(c, src, &st, lv) as i32;
            if st.rhw.matk > 0 {
                let w_matk = st.rhw.matk as i32;
                let variance = w_matk * st.rhw.wlv as i32 / 10;
                min += w_matk - variance;
                max += w_matk + variance;
            }
            if scl.as_ref().map(|s| s.has(SC_RECOGNIZEDSPELL)).unwrap_or(false) {
                min = max;
            }
            if is_sd {
                let over = crate::status::data::sd_get(c, src.id).map(|s| s.right_weapon.overrefine as i32).unwrap_or(0);
                if over > 0 {
                    min += 1;
                    max += over - 1;
                }
            }
            if max > min {
                hp += min + (c.rnd() % (max - min) as u32) as i32;
            } else {
                hp += min;
            }
        }
    }
    // Global multipliers are applied after the MATK is applied
    if let Some(ts) = &tscl {
        if skill_id != NPC_EVILLAND && skill_id != BA_APPLEIDUN && ts.has(SC_WATER_INSIGNIA) && ts.v1(SC_WATER_INSIGNIA) == 2 {
            global_bonus *= 1.1f32 as f64;
        }
    }
    if skill_id == AB_HIGHNESSHEAL {
        global_bonus *= (2.0f32 + 0.3f32 * (skill_lv as i32 - 1) as f32) as f64;
    } else if skill_id == CD_DILECTIO_HEAL {
        // Description says its 10% on Lv 1 but thats clearly a typo. [Rytech]
        global_bonus *= (1.0f32 + 0.15f32 + 0.05f32 * skill_lv as f32) as f64;
    }
    if heal {
        if let Some(ts) = &tscl {
            let mut penalty: u8 = 0;
            if ts.has(SC_CRITICALWOUND) {
                penalty = penalty.wrapping_add(ts.v2(SC_CRITICALWOUND) as u8);
            }
            if ts.has(SC_DEATHHURT) && ts.v3(SC_DEATHHURT) == 1 {
                penalty = penalty.wrapping_add(20);
            }
            if ts.has(SC_NORECOVER_STATE) {
                penalty = 100;
            }
            if penalty > 0 {
                let p = (penalty as i32).clamp(1, 100);
                global_bonus *= ((100 - p) as f32 / 100.0f32) as f64;
            }
        }
    }
    hp = (hp as f64 * global_bonus) as i32;
    // Final heal increased by HPlus. Is this the right place for this??? [Rytech]
    if is_sd && st.hplus > 0 && skill_id != SOA_TALISMAN_OF_PROTECTION {
        hp += hp * st.hplus as i32 / 100;
    }
    if heal {
        hp.max(1)
    } else {
        hp
    }
}

/// skill.cpp skill_isCopyable: 0 cannot be copied, 1 by Plagiarism, 2 by Reproduce.
pub fn skill_is_copyable(c: &Ctx, aid: u32, skill_id: u16) -> i32 {
    use crate::status::defs::SKILL_FLAG_PLAGIARIZED;
    let skill_idx = skill_get_index(skill_id);
    if skill_idx == 0 {
        return 0;
    }
    // Only copy skill that player doesn't have or the skill is old clone
    if let Some(s) = crate::skilltree::Skills::load(c, aid) {
        let sl = s.at(skill_idx);
        if sl.id != 0 && sl.flag != SKILL_FLAG_PLAGIARIZED {
            return 0;
        }
    }
    let Some(skill) = skill_db().find(skill_id) else { return 0 };
    let copyable = &skill.copyable;
    // Reproduce can copy skill if SC__REPRODUCE is active and the skill is copyable by Reproduce
    if copyable.option & SKILL_COPY_REPRODUCE != 0 && crate::skill::checkskill(c, aid, SC_REPRODUCE) != 0 {
        if let Some(e) = sc::get_sce(c, aid, SC__REPRODUCE) {
            if e.val1 != 0 {
                return 2;
            }
        }
    }
    // Plagiarism only able to copy skill while SC_PRESERVE is not active and skill is copyable by Plagiarism
    if copyable.option & SKILL_COPY_PLAGIARISM != 0 && crate::skill::checkskill(c, aid, RG_PLAGIARISM) > 0 && !sc::has_sce(c, aid, SC_PRESERVE) {
        return 1;
    }
    0
}

/// party.cpp party_skill_check: party-wide skill bonuses (TK_COUNTER → Triple Attack rate of Monks, MO_COMBOFINISH →
/// Counter rate of Star Gladiators; AM_TWILIGHT2 / 3 → whether a Super Novice / Taekwon is in the party).
pub fn party_skill_check(c: &Ctx, aid: u32, party_id: u32, skill_id: u16, skill_lv: u16) -> i32 {
    use crate::party::{PSTATE_MONK, PSTATE_SG, PSTATE_SNOVICE, PSTATE_TK};
    if party_id == 0 {
        return 0;
    }
    let Some(mut p) = crate::party::party_search(c, party_id) else { return 0 };
    crate::party::party_check_state(&mut p);
    {
        use crate::party::party_data as _;
        c.db().party_data().party_id().update(p.clone());
    }
    match skill_id {
        TK_COUNTER => {
            if p.state & PSTATE_MONK == 0 {
                return 0; // Increase Triple Attack rate of Monks.
            }
        }
        MO_COMBOFINISH => {
            if p.state & PSTATE_SG == 0 {
                return 0; // Increase Counter rate of Star Gladiators
            }
        }
        AM_TWILIGHT2 => return (p.state & PSTATE_SNOVICE != 0) as i32, // Twilight Pharmacy, requires Super Novice
        AM_TWILIGHT3 => return (p.state & PSTATE_TK != 0) as i32,      // Twilight Pharmacy, Requires Taekwon
        _ => return 0,                                                   // Unknown case?
    }
    let Some(sd_map) = unit::get(c, aid).map(|u| u.map) else { return 0 };
    use crate::db::jobs::{MAPID_MONK, MAPID_SECONDMASK, MAPID_STAR_GLADIATOR};
    for d in p.data.iter() {
        let p_aid = d.sd;
        if p_aid == 0 {
            continue;
        }
        let Some(pu) = unit::get(c, p_aid) else { continue };
        if pu.map != sd_map {
            continue;
        }
        let class_ = crate::status::data::sd_get(c, p_aid).map(|s| s.class_).unwrap_or(0);
        match skill_id {
            TK_COUNTER => {
                // Increase Triple Attack rate of Monks.
                if class_ & MAPID_SECONDMASK == MAPID_MONK && crate::skill::checkskill(c, p_aid, MO_TRIPLEATTACK) != 0 {
                    sc::sc_start4(c, Some(&pu), &pu, SC_SKILLRATE_UP, 100, MO_TRIPLEATTACK as i32, 50 + 50 * skill_lv as i32, 0, 0, skill_get_time(SG_FRIEND, 1) as i64);
                }
            }
            MO_COMBOFINISH => {
                // Increase Counter rate of Star Gladiators
                let sg = crate::skill::checkskill(c, p_aid, SG_FRIEND) as i32;
                if class_ & MAPID_SECONDMASK == MAPID_STAR_GLADIATOR && sc::has_sce(c, p_aid, SC_READYCOUNTER) && sg != 0 {
                    sc::sc_start4(c, Some(&pu), &pu, SC_SKILLRATE_UP, 100, TK_COUNTER as i32, 50 + 50 * sg, 0, 0, skill_get_time(SG_FRIEND, 1) as i64);
                }
            }
            _ => {}
        }
    }
    0
}

/// pc.cpp pc_steal_item (RENEWAL): TF_STEAL / Gank on a monster; true when an item was stolen.
pub fn pc_steal_item(c: &Ctx, sd: &Unit, bl: &Unit, skill_lv: i32) -> bool {
    use crate::mob::RC2_TREASURE;
    use crate::status::defs::{MD_STATUSIMMUNE, OPT1_BURNING};
    if bl.kind != BL_MOB {
        return false;
    }
    let Some(mut md) = crate::mob::get(c, bl.id) else { return false };
    if md.steal_flag == u8::MAX || (bl.sc.opt1 != 0 && bl.sc.opt1 != OPT1_BURNING) {
        return false; // already stolen from / status change check
    }
    let sd_status = crate::status::status_get_status_data(c, sd);
    let md_status = crate::status::status_get_status_data(c, bl);
    let race2 = crate::battle::bl::race2_of(c, bl);
    let steal_max_tries = crate::battle_config::get(crate::battle_config::Bc::SKILL_STEAL_MAX_TRIES); // skill.conf: 0
    if md.master_id != 0
        || crate::battle::mobif::has_mode(md_status.mode, MD_STATUSIMMUNE as i32)
        || race2.contains(&(RC2_TREASURE as u8))
        || crate::npc::mapflag(c, bl.map, crate::npc::MF_NOMOBLOOT)
        || (steal_max_tries != 0 && {
            let t = md.steal_flag;
            md.steal_flag = md.steal_flag.wrapping_add(1);
            t as i32 >= steal_max_tries
        })
    {
        // Can't steal from
        md.steal_flag = u8::MAX;
        crate::mob::put(c, &md);
        return false;
    }
    // base skill success chance (percentual)
    let mut rate: f64 = ((sd_status.dex as i32 - md_status.dex as i32) / 2 + skill_lv * 6 + 4) as f64;
    rate += crate::status::data::sd_get(c, sd.id).map(|s| s.bonus.add_steal_rate as f64).unwrap_or(0.0);
    if rate < 1.0 || ((c.rnd() % 100) as f64) >= rate {
        return false;
    }
    // Try dropping one item: md->db->dropitem (pc.cpp pc_steal_item), the monster's list as it is now (a script's
    // addmonsterdrop / delmonsterdrop, the drop rates of the last mob_reload)
    let Some(db) = crate::mob::db(c, &md) else { return false };
    let mut drop = None;
    for entry in &db.dropitem {
        if entry.steal_protected {
            continue;
        }
        if crate::db::items::itemdb_exists(entry.nameid).is_none() {
            continue;
        }
        if ((c.rnd() % 10000) as u32) < entry.rate {
            drop = Some(entry.clone());
            break;
        }
    }
    let Some(drop) = drop else { return false };
    let itemid = drop.nameid;
    let mut tmp_item = crate::inventory::Item { nameid: itemid, amount: 1, identify: crate::db::items::itemdb_isidentified(itemid) as u8, ..Default::default() };
    // (battle_config.skill_steal_random_options: 0)
    let flag = crate::inventory::pc_additem(c, sd.id, &mut tmp_item, 1, crate::map_log::LOG_TYPE_PICKDROP_PLAYER);
    // TODO (rAthena): Should we disable stealing when the item you stole couldn't be added to your inventory?
    md.steal_flag = u8::MAX; // you can't steal from this mob any more
    crate::mob::put(c, &md);
    if flag != 0 {
        // Failed to steal due to overweight
        crate::clif_items::additem(c, sd.id, 0, 0, flag);
        return false;
    }
    // (battle_config.show_steal_in_same_party: no)
    //Logs items, Stolen from mobs [Lupus]
    crate::map_log::log_pick_mob(c, md.mob_id, bl.map, crate::map_log::LOG_TYPE_STEAL, -1, &tmp_item);
    // A Rare Steal Global Announce by Lupus (battle_config.rare_drop_announce)
    if (drop.rate as i32) <= crate::mob::conf::rare_drop_announce() {
        let name = crate::pc::character_of(c, sd.id).map(|ch| ch.name).unwrap_or_default();
        let who = if name.is_empty() { "GM".to_string() } else { name };
        let iname = crate::db::items::itemdb_search(itemid).ename.clone();
        // MSG 542: "'%s' stole %s's %s (chance: %0.02f%%)" — sprintf's %0.02f of the float rate, intif_broadcast BC_DEFAULT
        let msg = crate::data::msg_txt(542).replacen("%s", &who, 1).replacen("%s", &db.jname, 1).replacen("%s", &iname, 1).replacen("%0.02f", &format!("{:.2}", drop.rate as f32 / 100.0), 1).replace("%%", "%");
        let b = crate::net::wire::latin1(&msg);
        crate::clif::broadcast(c, None, &b, b.len() + 1, 0, crate::clif::Target::AllClient);
    }
    true
}

/// skill.cpp skill_block_check: a status that blocks this skill (1) or not (0).
pub fn skill_block_check(c: &Ctx, bl: &Unit, type_: i32, skill_id: u16) -> i32 {
    if skill_id == 0 || !crate::battle::mobif::has_sc(bl.kind) {
        return 0; // Can do it
    }
    let _ = c;
    match type_ {
        SC_ANKLE => {
            if skill_id == AL_TELEPORT {
                return 1;
            }
        }
        SC_STASIS => {
            if skill_get_inf2(skill_id, &[INF2_ISSONG, INF2_ISENSEMBLE, INF2_ISCHORUS]) || skill_get_type(skill_id) == crate::status::defs::BF_MAGIC {
                return 1; // Can't do it.
            }
        }
        SC_KAGEHUMI => {
            if skill_get_inf2(skill_id, &[INF2_IGNOREKAGEHUMI]) {
                return 1;
            }
        }
        SC_BITE => {
            if skill_get_inf2(skill_id, &[INF2_IGNOREWUGBITE]) {
                return 1;
            }
        }
        _ => {}
    }
    0
}

/// skill.cpp skill_disable_check: a toggleable skill whose status is running (turning it off costs nothing).
pub fn skill_disable_check(c: &Ctx, bl_id: u32, skill_id: u16) -> bool {
    let Some(skill) = skill_db().find(skill_id) else { return false };
    let t = skill.sc as i32;
    if t <= SC_NONE || t >= SC_MAX {
        return false;
    }
    if skill.has_inf2(INF2_TOGGLEABLE) {
        if sc::has_sce(c, bl_id, t) {
            return true;
        }
        // These 2 skills contain a master and are not correctly pulled using skill_get_sc
        if skill.nameid == NC_NEUTRALBARRIER && sc::has_sce(c, bl_id, SC_NEUTRALBARRIER_MASTER) {
            return true;
        }
        if skill.nameid == NC_STEALTHFIELD && sc::has_sce(c, bl_id, SC_STEALTHFIELD_MASTER) {
            return true;
        }
    }
    false
}

/// skill.cpp skill_get_elemental_type: the elemental class a Sorcerer summon skill creates at `skill_lv`
/// (ELEMENTALID_<spirit>_S + skill_lv - 1; any other skill: skill_lv - 1, as rAthena's `type = 0` start).
pub fn skill_get_elemental_type(skill_id: u16, skill_lv: u16) -> i32 {
    use crate::db::misc::elemental::{ELEMENTALID_AGNI_S, ELEMENTALID_AQUA_S, ELEMENTALID_TERA_S, ELEMENTALID_VENTUS_S};
    let mut type_: i32 = match skill_id {
        SO_SUMMON_AGNI => ELEMENTALID_AGNI_S,
        SO_SUMMON_AQUA => ELEMENTALID_AQUA_S,
        SO_SUMMON_VENTUS => ELEMENTALID_VENTUS_S,
        SO_SUMMON_TERA => ELEMENTALID_TERA_S,
        _ => 0,
    };
    type_ += skill_lv as i32 - 1;
    type_
}

/// skill.cpp skill_check_unit_movepos: unit_movepos unless the map (flag 1 BG, 2 GvG in WoE, 4 GvG maps) or
/// SC_SV_ROOTTWIST forbids it.
pub fn skill_check_unit_movepos(c: &Ctx, check_flag: u8, bl: &Unit, dst_x: i16, dst_y: i16, easy: i32, checkpath: bool) -> bool {
    if check_flag & 1 != 0 && crate::npc::mapflag(c, bl.map, crate::npc::MF_BATTLEGROUND) {
        return false;
    }
    if check_flag & 2 != 0 && crate::npc::mapflag_gvg(c, bl.map) {
        return false;
    }
    if check_flag & 4 != 0 && crate::npc::mapflag_gvg2(c, bl.map) {
        return false;
    }
    if sc::has_sce(c, bl.id, SC_SV_ROOTTWIST) {
        return false;
    }
    unit::movepos(c, bl.id, dst_x, dst_y, easy, checkpath)
}

/// skill.cpp skill_spellbook: Process Warlock Spellbooks (WL_READING_SB_READING). `sd` is the player reading the
/// book `nameid`: the sealed spell of that book goes into the highest free SC_SPELLBOOK slot (counting down from
/// SC_MAXSPELLBOOK, "how official saves spellbook"), its preserve points are added to SC_FREEZE_SP's val2, and the
/// spell's own SP cost is paid.
pub fn skill_spellbook(c: &Ctx, sd: &Unit, nameid: u32) {
    use super::clif::{fail::*, skill_fail_cause};
    if skill_misc().spellbook.is_empty() {
        return;
    }
    // status_change *sc = status_get_sc(&sd) (a player always has one)
    for i in SC_SPELLBOOK1..=SC_MAXSPELLBOOK {
        if !sc::has_sce(c, sd.id, i) {
            break;
        }
        if i == SC_MAXSPELLBOOK {
            skill_fail_cause(c, sd, WL_READING_SB, USESKILL_FAIL_SPELLBOOK_READING);
            return;
        }
    }
    let item_exists = crate::db::items::itemdb_exists(nameid).is_some();
    let Some(spell) = reading_spellbook_find_book(nameid, item_exists) else { return };
    let skill_id = spell.skill_id;
    let skill_lv = super::checkskill(c, sd.id, skill_id) as u16;
    if skill_lv == 0 {
        // Caster hasn't learned the skill
        let lv = super::checkskill(c, sd.id, WL_READING_SB) as u16;
        sc::sc_start(c, Some(sd), sd, SC_SLEEP, 100, 1, skill_get_time(WL_READING_SB, lv) as i64);
        let s = unit::get(c, sd.id).unwrap_or_else(|| sd.clone());
        skill_fail_cause(c, &s, WL_READING_SB, USESKILL_FAIL_SPELLBOOK_DIFFICULT_SLEEP);
        return;
    }
    let points = spell.points;
    if let Some(freeze) = sc::get_sce(c, sd.id, SC_FREEZE_SP) {
        // (val2 + points) > 8 * WL_FREEZE_SP + int / 10 + base_level / 10 (base_level is uint32: an unsigned
        // comparison in rAthena; both sides are non-negative here)
        let int_ = crate::status::status_get_status_data(c, sd).int_ as i64;
        let base_level = crate::pc::character_of(c, sd.id).map(|ch| ch.base_level as i64).unwrap_or(0);
        let cap = 8 * super::checkskill(c, sd.id, WL_FREEZE_SP) as i64 + int_ / 10 + base_level / 10;
        if freeze.val2 as i64 + points as i64 > cap {
            skill_fail_cause(c, sd, WL_READING_SB, USESKILL_FAIL_SPELLBOOK_PRESERVATION_POINT);
            return;
        }
        let mut i = SC_MAXSPELLBOOK;
        while i >= SC_SPELLBOOK1 {
            // This is how official saves spellbook. [malufett]
            if !sc::has_sce(c, sd.id, i) {
                if let Some(mut e) = sc::get_sce(c, sd.id, SC_FREEZE_SP) {
                    e.val2 += points as i32; // increase points
                    sc::put_sce(c, &e);
                }
                let s = unit::get(c, sd.id).unwrap_or_else(|| sd.clone());
                sc::sc_start4(c, Some(&s), &s, i, 100, skill_id as i32, skill_lv as i32, points as i32, 0, sc::INFINITE_TICK);
                break;
            }
            i -= 1;
        }
    } else {
        sc::sc_start2(c, Some(sd), sd, SC_FREEZE_SP, 100, 0, points as i32, sc::INFINITE_TICK);
        let s = unit::get(c, sd.id).unwrap_or_else(|| sd.clone());
        sc::sc_start4(c, Some(&s), &s, SC_MAXSPELLBOOK, 100, skill_id as i32, skill_lv as i32, points as i32, 0, sc::INFINITE_TICK);
    }
    // Reading Spell Book SP cost same as the sealed spell.
    crate::status::hp::status_zap(c, sd.id, 0, skill_get_sp(skill_id, skill_lv) as i64, 0);
}

/// skill.cpp skill_elementalanalysis: SO_EL_ANALYSIS's answer (clif_parse_ItemListWindowSelected types 1 / 2): each
/// (client index, amount) pair of `item_list` turns elemental stones into their other form (level 1: pure to rough,
/// 5-9 per stone; level 2: rough to pure, one per 10, with a 25% chance to lose the stones). Returns 1 on a failure.
pub fn skill_elementalanalysis(c: &Ctx, sd: &Unit, n: i32, skill_lv: u16, item_list: &[u16]) -> i32 {
    use crate::inventory::{self as inv, Item, MAX_INVENTORY};
    // itemdb.hpp ITEMID_BLOODY_RED .. ITEMID_GREAT_NATURE
    const ITEMID_BLOODY_RED: u32 = 990;
    const ITEMID_CRYSTAL_BLUE: u32 = 991;
    const ITEMID_WIND_OF_VERDURE: u32 = 992;
    const ITEMID_YELLOW_LIVE: u32 = 993;
    const ITEMID_FLAME_HEART: u32 = 994;
    const ITEMID_MISTIC_FROZEN: u32 = 995;
    const ITEMID_ROUGH_WIND: u32 = 996;
    const ITEMID_GREAT_NATURE: u32 = 997;
    if n <= 0 {
        return 1;
    }
    for i in 0..n as usize {
        // (a list shorter than n reads past the packet in rAthena; the module stops there)
        let (Some(&ci), Some(&amt)) = (item_list.get(i * 2), item_list.get(i * 2 + 1)) else { return 1 };
        let idx = ci as i32 - 2;
        if idx < 0 || idx as usize >= MAX_INVENTORY {
            return 1;
        }
        let mut del_amount = amt as i32;
        if skill_lv == 2 {
            del_amount -= del_amount % 10;
        }
        let add_amount = if skill_lv == 1 { del_amount * (5 + (c.rnd() % 5) as i32) } else { del_amount / 10 };
        let slot = inv::inv_get(c, sd.id, idx as usize);
        let nameid = slot.as_ref().map(|r| r.item.nameid).unwrap_or(0);
        if nameid == 0 || del_amount > slot.as_ref().map(|r| r.item.amount as i32).unwrap_or(0) {
            super::clif::skill_fail(c, sd, SO_EL_ANALYSIS);
            return 1;
        }
        let product = match nameid {
            // Level 1
            ITEMID_FLAME_HEART => ITEMID_BLOODY_RED,
            ITEMID_MISTIC_FROZEN => ITEMID_CRYSTAL_BLUE,
            ITEMID_ROUGH_WIND => ITEMID_WIND_OF_VERDURE,
            ITEMID_GREAT_NATURE => ITEMID_YELLOW_LIVE,
            // Level 2
            ITEMID_BLOODY_RED => ITEMID_FLAME_HEART,
            ITEMID_CRYSTAL_BLUE => ITEMID_MISTIC_FROZEN,
            ITEMID_WIND_OF_VERDURE => ITEMID_ROUGH_WIND,
            ITEMID_YELLOW_LIVE => ITEMID_GREAT_NATURE,
            _ => {
                super::clif::skill_fail(c, sd, SO_EL_ANALYSIS);
                return 1;
            }
        };
        // pc_delitem(&sd, idx, del_amount, 0, 1, LOG_TYPE_CONSUME)
        if inv::pc_delitem(c, sd.id, idx, del_amount, 0, 1, crate::map_log::LOG_TYPE_CONSUME) != 0 {
            super::clif::skill_fail(c, sd, SO_EL_ANALYSIS);
            return 1;
        }
        if skill_lv == 2 && c.rnd() % 100 < 25 {
            // At level 2 have a fail chance. You loose your items if it fails.
            super::clif::skill_fail(c, sd, SO_EL_ANALYSIS);
            return 1;
        }
        let mut tmp_item = Item::new(product);
        tmp_item.amount = add_amount as i16;
        tmp_item.identify = 1;
        if tmp_item.amount != 0 {
            // pc_additem(&sd, &tmp_item, tmp_item.amount, …): the int16 amount (skill.cpp:13828), not add_amount
            let amount = tmp_item.amount as i32;
            let flag = inv::pc_additem(c, sd.id, &mut tmp_item, amount, crate::map_log::LOG_TYPE_CONSUME);
            if flag != 0 {
                crate::clif_items::additem(c, sd.id, 0, 0, flag);
                // battle_config.skill_drop_items_full: no (map_addflooritem)
            }
        }
    }
    0
}
