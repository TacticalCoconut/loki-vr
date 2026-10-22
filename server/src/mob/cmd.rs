//! The monster @commands (atcommand.cpp): @monster / @spawn / @monstersmall / @monsterbig (ACMD_FUNC(monster)),
//! @killmonster / @killmonster2 (ACMD_FUNC(killmonster)), @mobsearch, @showmobs, @mobinfo, @whodrops, @whereis,
//! @summon, @clone / @slaveclone / @evilclone (ACMD_FUNC(clone)); and ItemDatabase::create_item_link, which @mobinfo
//! and @whodrops print.

use crate::atcommand::Out;
use crate::cstr::Scan;
use crate::data::{self, format_msg, msg_txt};
use crate::db::items::{self, ItemData};
use crate::db::mobs::{self, BossType};
use crate::unit;
use crate::world::Ctx;

use super::mob_data as _;
use spacetimedb::Table;
use super::{conf, get, rtdb};

/// conf/battle/gm.conf `atcommand_spawn_quantity_limit: 100`, `atcommand_mobinfo_type: 1` (misc.conf `etc_log: off`),
/// `atcommand_slave_clone_limit: 25`.
pub fn atc_spawn_quantity_limit() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::ATCOMMAND_SPAWN_QUANTITY_LIMIT)
}
pub fn atc_slave_clone_limit() -> usize {
    crate::battle_config::get(crate::battle_config::Bc::ATCOMMAND_SLAVE_CLONE_LIMIT) as usize
}
pub fn atcommand_mobinfo_type() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::ATCOMMAND_MOBINFO_TYPE)
}

/// The monster commands, by ACMD_FUNC name (None: not a monster command).
pub(crate) fn run(c: &Ctx, out: &Out, sd: u32, func: &str, command: &str, message: &str) -> Option<i32> {
    Some(match func {
        "monster" => monster(c, out, sd, command, message),
        "killmonster" => killmonster(c, out, sd, command, message),
        "mobsearch" => mobsearch(c, out, sd, message),
        "mobinfo" => mobinfo(c, out, sd, message),
        "whodrops" => whodrops(c, out, sd, message),
        "whereis" => whereis(c, out, message),
        "summon" => summon(c, out, sd, message),
        "clone" => clone(c, out, sd, command, message),
        "showmobs" => showmobs(c, out, sd, message),
        _ => return None,
    })
}

/// common/utilities.cpp util::strtoint32def(str, 0): strtol over the whole string (Some(value truncated to int32)),
/// None (the default) when characters are left over or the value is out of range.
pub fn strtoint32def(s: &str) -> Option<i32> {
    let b = crate::net::wire::latin1(s);
    let mut p = 0;
    while p < b.len() && crate::cstr::is_space(b[p]) {
        p += 1;
    }
    let st = p;
    if p < b.len() && (b[p] == b'+' || b[p] == b'-') {
        p += 1;
    }
    let ds = p;
    while p < b.len() && b[p].is_ascii_digit() {
        p += 1;
    }
    if p == ds {
        // no conversion: str_end is the string itself, which ends there only when it is empty
        return if b.is_empty() { Some(0) } else { None };
    }
    if p != b.len() {
        return None;
    }
    let v: i64 = std::str::from_utf8(&b[st..p]).ok()?.parse().ok()?; // ERANGE: the default
    Some(v as i32)
}

/// strtol(s, nullptr, 10): the leading number (0 when none), truncated to int32.
fn strtol(s: &str) -> i32 {
    let b = crate::net::wire::latin1(s);
    Scan::new(&b).int(usize::MAX).map(|v| v as i32).unwrap_or(0)
}

/// mob_has_spawn, for the database's name searches.
fn has_spawn(c: &Ctx) -> impl Fn(u16) -> bool {
    super::spawn::mob_has_spawn_fn(c)
}

// ---------------------------------------------------------------------------------------------- @monster

/// ACMD_FUNC(monster): @monster / @spawn / @monstersmall / @monsterbig <monster name/id> [<amount>] [<name>], and
/// the forms with a quoted display name.
fn monster(c: &Ctx, out: &Out, aid: u32, command: &str, message: &str) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return -1 };
    if message.is_empty() {
        out.say(msg_txt(80)); // Give the display name or monster name/id please.
        return -1;
    }
    let b = crate::net::wire::latin1(message);
    let (mut name, mut monster, mut number) = (String::new(), String::new(), 0i32);
    // sscanf(message, "\"%23[^\"]\" %23s %11d", name, monster, &number) > 1
    let mut parsed = {
        let mut s = Scan::new(&b);
        let mut n = 0;
        if s.lit(b'"') {
            if let Some(v) = s.set(23, b"\"") {
                name = v;
                n += 1;
                if s.lit(b'"') {
                    if let Some(v) = s.word(23) {
                        monster = v;
                        n += 1;
                        if let Some(v) = s.int(11) {
                            number = v as i32;
                            n += 1;
                        }
                    }
                }
            }
        }
        n > 1
    };
    if !parsed {
        // || sscanf(message, "%23s \"%23[^\"]\" %11d", monster, name, &number) > 1
        let mut s = Scan::new(&b);
        let mut n = 0;
        if let Some(v) = s.word(23) {
            monster = v;
            n += 1;
            s.ws();
            if s.lit(b'"') {
                if let Some(v) = s.set(23, b"\"") {
                    name = v;
                    n += 1;
                    if s.lit(b'"') {
                        if let Some(v) = s.int(11) {
                            number = v as i32;
                            n += 1;
                        }
                    }
                }
            }
        }
        parsed = n > 1;
    }
    // All data can be left as it is.
    if !parsed {
        // sscanf(message, "%23s %11d %23s", monster, &number, name) > 1
        let mut s = Scan::new(&b);
        let mut count = 0;
        if let Some(v) = s.word(23) {
            monster = v;
            count += 1;
            if let Some(v) = s.int(11) {
                number = v as i32;
                count += 1;
                if let Some(v) = s.word(23) {
                    name = v;
                    count += 1;
                }
            }
        }
        if count > 1 {
            // Here, it is possible name was not given and we are using monster for it.
            if count < 3 {
                name.clear(); // Blank mob's name.
            }
            parsed = true;
        }
    }
    if !parsed {
        // sscanf(message, "%23s %23s %11d", name, monster, &number) > 1
        let mut s = Scan::new(&b);
        let mut n = 0;
        if let Some(v) = s.word(23) {
            name = v;
            n += 1;
            if let Some(v) = s.word(23) {
                monster = v;
                n += 1;
                if let Some(v) = s.int(11) {
                    number = v as i32;
                    n += 1;
                }
            }
        }
        parsed = n > 1;
    }
    if !parsed {
        // sscanf(message, "%23s", monster) > 0
        match Scan::new(&b).word(23) {
            Some(v) => {
                monster = v;
                name.clear(); // As before, name may be already filled.
            }
            None => {
                out.say(msg_txt(80)); // Give a display name and monster name/id please.
                return -1;
            }
        }
    }
    // If AegisName matches exactly, summon that monster
    let mob_id = match mobs::mobdb_search_aegisname(&monster) {
        Some(m) => m.id as i32,
        None => {
            // Otherwise, search for monster with that ID or name. Check for ID first as this is faster; if search
            // string is not a number it will return 0
            let id = strtoint32def(&monster).unwrap_or(0);
            if id == 0 || mobs::mobdb_checkid(id) == 0 {
                crate::atcommand_atcmd::admin::mobdb_searchname(c, &monster) as i32
            } else {
                id
            }
        }
    };
    if mob_id == 0 {
        out.say(msg_txt(40)); // Invalid monster ID or name.
        return -1;
    }
    if mob_id == mobs::MOBID_EMPERIUM as i32 {
        out.say(msg_txt(83)); // Monster 'Emperium' cannot be spawned.
        return -1;
    }
    if number <= 0 {
        number = 1;
    }
    if name.is_empty() {
        name = "--ja--".into();
    }
    // If value of atcommand_spawn_quantity_limit directive is greater than or equal to 1 and quantity of monsters
    // is greater than value of the directive
    if atc_spawn_quantity_limit() != 0 && number > atc_spawn_quantity_limit() {
        number = atc_spawn_quantity_limit();
    }
    let parent_cmd = data::atcommands().check_alias(command.get(1..).unwrap_or(""));
    let size = if parent_cmd == "monstersmall" {
        crate::status::defs::SZ_MEDIUM // This is just gorgeous [mkbu95]
    } else if parent_cmd == "monsterbig" {
        crate::status::defs::SZ_BIG
    } else {
        crate::status::defs::SZ_SMALL
    } as u8;
    // (etc_log: off)
    let mut count = 0;
    let range = ((number as f32).sqrt() as i32 + 2) as i16; // calculation of an odd number (+ 4 area around)
    let (mut mx, mut my) = (0i16, 0i16);
    for _ in 0..number {
        // map_search_freecell(sd, 0, &mx, &my, range, range, 0): on failure mx / my become the search centre (sd)
        (mx, my) = super::spawn::map_search_freecell(c, Some((sd.map, sd.x, sd.y)), sd.map as i32, mx, my, range, range, 0, 50).unwrap_or((sd.x, sd.y));
        let k = super::spawn::mob_once_spawn(c, Some(&sd), sd.map as i32, mx, my, Some(&name), mob_id, 1, Some(""), size, super::AI_NONE);
        if k != 0 {
            // mapreg_setreg(reference_uid(add_str("$@mobid"), i),k); (commented out in rAthena)
            count += 1;
        }
    }
    if count != 0 {
        if number == count {
            out.say(msg_txt(39)); // All monster summoned!
        } else {
            out.say(&format_msg(msg_txt(240), &[&count])); // %d monster(s) summoned!
        }
    } else {
        out.say(msg_txt(40)); // Invalid monster ID or name.
        return -1;
    }
    0
}

// ---------------------------------------------------------------------------------------------- @killmonster

/// ACMD_FUNC(killmonster): @killmonster [<map>] (status_zap: drops and EXP) / @killmonster2 [<map>] (status_kill:
/// neither). WoE monsters are left alone.
fn killmonster(c: &Ctx, out: &Out, aid: u32, command: &str, message: &str) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return -1 };
    let b = crate::net::wire::latin1(message);
    let map_name = if message.is_empty() { None } else { Scan::new(&b).word(15) };
    let map_id = map_name.and_then(|n| data::maps().id_of(&n)).unwrap_or(sd.map);
    let parent_cmd = data::atcommands().check_alias(command.get(1..).unwrap_or(""));
    // drop_flag = strcmpi(parent_cmd, "killmonster2"); map_foreachinmap(..., -drop_flag)
    let drop_flag = !crate::cstr::eq_ignore_case(parent_cmd, "killmonster2");
    for bl in super::mobs_foreachinmap(c, map_id) {
        atkillmonster_sub(c, bl.id, drop_flag);
    }
    out.say(msg_txt(165)); // All monsters killed!
    0
}

/// atcommand.cpp atkillmonster_sub.
fn atkillmonster_sub(c: &Ctx, id: u32, flag: bool) -> i32 {
    // map_foreachinmap: only units still in the block list
    if !unit::get(c, id).map(|u| u.on_map).unwrap_or(false) {
        return 0;
    }
    let Some(md) = get(c, id) else { return 0 };
    if md.guardian {
        return 0; // Do not touch WoE mobs!
    }
    if flag {
        crate::status::hp::status_zap(c, id, md.status.hp as i64, 0, 0);
    } else {
        crate::status::hp::status_kill(c, id);
    }
    1
}

// ---------------------------------------------------------------------------------------------- @mobsearch

/// ACMD_FUNC(mobsearch): the monsters of one kind on the player's map, in the id database's iteration order.
fn mobsearch(c: &Ctx, out: &Out, aid: u32, message: &str) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return -1 };
    let b = crate::net::wire::latin1(message);
    let mob_name = if message.is_empty() { None } else { Scan::new(&b).set(99, b"\n") };
    let Some(mob_name) = mob_name else {
        out.say(msg_txt(1218)); // Please enter a monster name (usage: @mobsearch <monster name>).
        return -1;
    };
    let mut mob_id = strtol(&mob_name);
    if mob_id == 0 {
        mob_id = crate::atcommand_atcmd::admin::mobdb_searchname(c, &mob_name) as i32;
    }
    let Some(mob) = super::db_find(c, mob_id).filter(|_| mobs::mobdb_checkid(mob_id) != 0) else {
        out.say(&format_msg(msg_txt(1219), &[&mob_name])); // Invalid mob ID %s!
        return -1;
    };
    let jname = mob.jname.clone(); // --ja--
    let map_name = data::maps().get(sd.map).map(|m| m.name.clone()).unwrap_or_default();
    out.say(&format_msg(msg_txt(1220), &[&jname, &map_name])); // Mob Search... %s %s
    // mapit_geteachmob: every monster in the id database (int DBMap order)
    let mut all: Vec<super::MobData> = c.db().mob_data().iter().collect();
    all.sort_by_key(|m| crate::party::idb_order(m.id));
    let mut number = 0;
    for md in all {
        let Some(u) = unit::get(c, md.id) else { continue };
        if u.map != sd.map {
            continue;
        }
        if md.mob_id != mob_id {
            continue;
        }
        number += 1;
        if md.spawn_timer == 0 {
            out.say(&format!("{number:>2}[{:>3}:{:>3}] {}", u.x, u.y, md.name));
        } else {
            out.say(&format!("{number:>2}[dead] {}", md.name));
        }
    }
    0
}

// ---------------------------------------------------------------------------------------------- @mobinfo

/// printf("%02.02f", v): the value rounded to two decimals the way glibc does (the exact binary value, ties to
/// even).
pub fn c_fixed2(v: f64) -> String {
    if !v.is_finite() {
        return format!("{v}");
    }
    // Every finite double has a finite decimal expansion; 80 digits cover the values printed here exactly enough
    // to tell below / at / above one half of the last kept digit.
    let exact = format!("{:.80}", v.abs());
    let (int_part, frac) = exact.split_once('.').unwrap_or((&exact, ""));
    let (keep, rest) = frac.split_at(2.min(frac.len()));
    let mut digits: Vec<u8> = int_part.bytes().chain(keep.bytes()).map(|d| d - b'0').collect();
    let rest = rest.as_bytes();
    let first = rest.first().copied().unwrap_or(b'0');
    let tail_nonzero = rest.iter().skip(1).any(|&d| d != b'0');
    let round_up = match first {
        b'6'..=b'9' => true,
        b'5' => tail_nonzero || digits.last().map(|d| d % 2 == 1).unwrap_or(false),
        _ => false,
    };
    if round_up {
        let mut i = digits.len();
        loop {
            if i == 0 {
                digits.insert(0, 1);
                break;
            }
            i -= 1;
            if digits[i] == 9 {
                digits[i] = 0;
            } else {
                digits[i] += 1;
                break;
            }
        }
    }
    let n = digits.len();
    let int_s: String = digits[..n - 2].iter().map(|d| (b'0' + d) as char).collect();
    let frac_s: String = digits[n - 2..].iter().map(|d| (b'0' + d) as char).collect();
    let int_s = if int_s.is_empty() { "0".to_string() } else { int_s };
    let sign = if v.is_sign_negative() && digits.iter().any(|&d| d != 0) { "-" } else { "" };
    format!("{sign}{int_s}.{frac_s}")
}

/// util::base62_encode.
fn base62(mut val: u32) -> String {
    const DICT: &[u8; 62] = b"0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    let mut out = Vec::new();
    while val != 0 {
        out.push(DICT[(val % 62) as usize]);
        val /= 62;
    }
    out.reverse();
    String::from_utf8(out).unwrap_or_default()
}

/// util::string_left_pad.
fn left_pad(s: &str, pad: char, num: usize) -> String {
    let mut out = pad.to_string().repeat(num.saturating_sub(s.len()));
    out.push_str(s);
    out
}

/// itemdb.cpp ItemDatabase::create_item_link(data) for a plain item (no refine, cards, options or grade), with
/// feature.itemlink on and PACKETVER 20211103 (>= 20200724): `<ITEML>` equip(5) isequip id [&look(2)] 'grade(2)
/// )card(2) x4 `</ITEML>`, and " [0]" after an equipment without slots.
pub fn create_item_link(id: &ItemData) -> String {
    let equip = items::itemdb_isequip2(id);
    let mut s = String::from("<ITEML>");
    s += &left_pad(&base62(id.equip), '0', 5);
    s += if equip { "1" } else { "0" };
    s += &base62(id.nameid);
    // (item.refine > 0: "%" + refine)
    if equip {
        s += "&";
        s += &left_pad(&base62(id.look), '0', 2);
    }
    s += "'";
    s += &left_pad(&base62(0), '0', 2); // enchantgrade
    for _ in 0..4 {
        // MAX_SLOTS cards
        s += ")";
        s += &left_pad(&base62(0), '0', 2);
    }
    // (no random options)
    s += "</ITEML>";
    if equip && id.slots == 0 {
        s += &format!(" [{}]", id.slots);
    }
    s
}

/// ACMD_FUNC(mobinfo)'s size / race / element names (mrace[RC_ALL][11]: the entries past "Player" are empty).
const MSIZE: [&str; 3] = ["Small", "Medium", "Large"];
const MRACE: [&str; 11] = ["Formless", "Undead", "Beast", "Plant", "Insect", "Fish", "Demon", "Demi-Human", "Angel", "Dragon", "Player"];
const MELEMENT: [&str; 10] = ["Neutral", "Water", "Earth", "Fire", "Wind", "Poison", "Holy", "Dark", "Ghost", "Undead"];

/// ACMD_FUNC(mobinfo): @mobinfo / @monsterinfo / @mi <monster name/id>.
fn mobinfo(c: &Ctx, out: &Out, aid: u32, message: &str) -> i32 {
    use crate::db::jobs::{PENALTY_DROP, PENALTY_EXP};
    let Some(sd) = unit::get(c, aid) else { return -1 };
    if message.is_empty() {
        out.say(msg_txt(1239)); // Please enter a monster name/ID (usage: @mobinfo <monster_name_or_monster_ID>).
        return -1;
    }
    // If monster identifier/name argument is a name (uint16 i = util::strtoint32def(message))
    let i = strtoint32def(message).unwrap_or(0) as u16;
    let mut mob_ids: Vec<u16> = if i != 0 && mobs::mobdb_checkid(i as i32) != 0 {
        vec![mobs::mobdb_checkid(i as i32) as u16]
    } else {
        mobs::mobdb_searchname_array(message, rtdb::MAX_SEARCH, &has_spawn(c))
    };
    if mob_ids.is_empty() {
        out.say(msg_txt(40)); // Invalid monster ID or name.
        return -1;
    }
    if mob_ids.len() >= rtdb::MAX_SEARCH {
        out.say(&format_msg(msg_txt(269), &[&rtdb::MAX_SEARCH])); // Displaying first %d matches
        mob_ids.truncate(rtdb::MAX_SEARCH);
    }
    for mob_id in mob_ids {
        let Some(mob) = super::db_find(c, mob_id as i32) else { continue };
        let mut base_exp = mob.base_exp;
        let mut job_exp = mob.job_exp;
        // (pc_isvip: the VIP system is off)
        // RENEWAL_EXP
        if atcommand_mobinfo_type() != 0 {
            let penalty = super::dead::pc_level_penalty_mod_db(c, Some(&sd), PENALTY_EXP, mob) as u64;
            base_exp = base_exp.wrapping_mul(penalty) / 100;
            job_exp = job_exp.wrapping_mul(penalty) / 100;
        }
        // stats: MVP Monster: / Monster: '%s'/'%s'/'%s' (%d)
        let head = if mob.get_bosstype() == BossType::Mvp { 1240 } else { 1241 };
        out.say(&format_msg(msg_txt(head), &[&mob.name, &mob.jname, &mob.sprite, &mob.id]));
        let st = &mob.status;
        let hit = mob.lv as i32 + st.dex as i32 + 175; // MOB_HIT (config/const.hpp, RENEWAL)
        let flee = mob.lv as i32 + st.agi as i32 + 100; // MOB_FLEE
        //  Lv:%d  HP:%d  Base EXP:%llu  Job EXP:%llu  HIT:%d  FLEE:%d
        out.say(&format_msg(msg_txt(1242), &[&mob.lv, &(st.max_hp as i32), &base_exp, &job_exp, &hit, &flee]));
        //  DEF:%d  MDEF:%d  STR:%d  AGI:%d  VIT:%d  INT:%d  DEX:%d  LUK:%d
        out.say(&format_msg(msg_txt(1243), &[&st.def, &st.mdef, &st.str, &st.agi, &st.vit, &st.int_, &st.dex, &st.luk]));
        //  ATK:%d~%d  Range:%d~%d~%d  Size:%s  Race: %s  Element: %s (Lv:%d)
        let size = MSIZE.get(st.size as usize).copied().unwrap_or("");
        let race = MRACE.get(st.race as usize).copied().unwrap_or("");
        let ele = MELEMENT.get(st.def_ele as usize).copied().unwrap_or("");
        out.say(&format_msg(
            msg_txt(1244),
            &[&(st.batk + st.rhw.atk as i32), &(st.batk + st.rhw.atk2 as i32), &st.rhw.range, &mob.range2, &mob.range3, &size, &race, &ele, &st.ele_lv],
        ));
        // RENEWAL:  RES:%d  MRES:%d
        out.say(&format_msg(msg_txt(827), &[&st.res, &st.mres]));
        // drops
        out.say(msg_txt(1245)); //  Drops:
        let mut line = String::from(" ");
        if mob.dropitem.is_empty() {
            out.say(msg_txt(1246)); // This monster has no drops.
        } else {
            let mut j = 0u32;
            let mut drop_modifier = 100;
            // RENEWAL_DROP
            if atcommand_mobinfo_type() != 0 {
                drop_modifier = super::dead::pc_level_penalty_mod_db(c, Some(&sd), PENALTY_DROP, mob);
            }
            for entry in &mob.dropitem {
                if entry.nameid == 0 || entry.rate < 1 {
                    continue;
                }
                let Some(id) = items::itemdb_exists(entry.nameid) else { continue };
                let droprate = super::dead::mob_getdroprate(c, Some(&sd), mob, entry.rate as i32, drop_modifier, None, 1);
                // " - %s  %02.02f%%" with (float)droprate / 100
                line += &format!(" - {}  {}%", create_item_link(id), c_fixed2((droprate as f32 / 100.0f32) as f64));
                j += 1;
                if j % 3 == 0 {
                    out.say(&line);
                    line = String::from(" ");
                }
            }
            if j % 3 != 0 {
                out.say(&line);
            }
        }
        // mvp
        if mob.get_bosstype() == BossType::Mvp {
            out.say(&format_msg(msg_txt(1247), &[&mob.mexp])); //  MVP Bonus EXP:%llu
            out.say(msg_txt(1248)); //  MVP drops:
            let mut line = String::from(" ");
            if mob.mvpitem.is_empty() {
                out.say(msg_txt(1249)); // This monster has no MVP drops.
            } else {
                let mut mvpremain: f32 = 100.0; // Remaining drop chance for official mvp drop mode
                let mut j = 0u32;
                for entry in &mob.mvpitem {
                    if entry.nameid == 0 {
                        continue;
                    }
                    let Some(id) = items::itemdb_exists(entry.nameid) else { continue };
                    // Because if there are 3 MVP drops at 50%, the first has a chance of 50%, the second 25% and
                    // the third 12.5%
                    let mvppercent: f32 = entry.rate as f32 * mvpremain / 10000.0f32;
                    if conf::item_drop_mvp_mode() == 0 {
                        mvpremain -= mvppercent;
                    }
                    if mvppercent > 0.0 {
                        line += &format!(" - {}  {}%", create_item_link(id), c_fixed2(mvppercent as f64));
                        j += 1;
                        if j % 3 == 0 {
                            out.say(&line);
                            line = String::from(" ");
                        }
                    }
                }
                if j % 3 != 0 {
                    out.say(&line);
                }
            }
        }
    }
    0
}

// ---------------------------------------------------------------------------------------------- @whodrops

/// strtoul(s, nullptr, 10) as t_itemid (uint32).
fn strtoul_u32(s: &str) -> u32 {
    let b = crate::net::wire::latin1(s);
    match Scan::new(&b).int(usize::MAX) {
        Some(v) => v as u64 as u32,
        None => 0,
    }
}

/// ACMD_FUNC(whodrops): the monsters with the highest chance to drop an item (item_data::mob[]).
fn whodrops(c: &Ctx, out: &Out, aid: u32, message: &str) -> i32 {
    use crate::db::jobs::PENALTY_DROP;
    let Some(sd) = unit::get(c, aid) else { return -1 };
    if message.is_empty() {
        out.say(msg_txt(1284)); // Please enter item name/ID (usage: @whodrops <item name/ID>).
        return -1;
    }
    let itemid = strtoul_u32(message);
    let item_array: Vec<&ItemData> = if itemid == 0 {
        // Entered a string
        items::itemdb_searchname_array(message, rtdb::MAX_SEARCH)
    } else {
        items::itemdb_exists(itemid).into_iter().collect()
    };
    if item_array.is_empty() {
        out.say(msg_txt(19)); // Invalid item ID or name.
        return -1;
    }
    if item_array.len() == rtdb::MAX_SEARCH {
        out.say(&format_msg(msg_txt(269), &[&rtdb::MAX_SEARCH])); // Displaying first %d matches
    }
    for id in item_array {
        out.say(&format_msg(msg_txt(1285), &[&create_item_link(id), &id.nameid])); // Item: '%s' (ID:%u)
        let mob = rtdb::item_mob(c, id.nameid);
        if mob[0].0 == 0 {
            out.say(msg_txt(1286)); //  - Item is not dropped by mobs.
        } else {
            //  - Common mobs with highest drop chance (only max %d are listed):
            out.say(&format_msg(msg_txt(1287), &[&rtdb::MAX_SEARCH]));
            for &(chance, mob_id) in mob.iter().take_while(|s| s.0 > 0) {
                let mut dropchance = chance;
                let Some(m) = super::db_find(c, mob_id) else { continue };
                // RENEWAL_DROP
                if atcommand_mobinfo_type() != 0 {
                    dropchance = dropchance * super::dead::pc_level_penalty_mod_db(c, Some(&sd), PENALTY_DROP, m) / 100;
                    if dropchance <= 0 && !conf::drop_rate0item() {
                        dropchance = 1;
                    }
                }
                // (pc_isvip: the VIP system is off)
                out.say(&format!("- {} ({}): {}%", m.jname, mob_id, c_fixed2(dropchance as f64 / 100.0)));
            }
        }
    }
    0
}

// ---------------------------------------------------------------------------------------------- @whereis

/// ACMD_FUNC(whereis): the maps a monster spawns on (mob_get_spawns: most monsters first).
fn whereis(c: &Ctx, out: &Out, message: &str) -> i32 {
    if message.is_empty() {
        out.say(msg_txt(1288)); // Please enter a monster name/ID (usage: @whereis <monster_name_or_monster_ID>).
        return -1;
    }
    let i_message = crate::cstr::atoi(message);
    let mut mob_ids: Vec<u16> = if mobs::mobdb_checkid(i_message) != 0 {
        // ID given
        vec![i_message as u16]
    } else {
        // Name given, get all monster associated whith this name
        mobs::mobdb_searchname_array(message, rtdb::MAX_SEARCH, &has_spawn(c))
    };
    if mob_ids.is_empty() {
        out.say(msg_txt(40)); // Invalid monster ID or name.
        return -1;
    }
    if mob_ids.len() >= rtdb::MAX_SEARCH {
        out.say(&format_msg(msg_txt(269), &[&rtdb::MAX_SEARCH])); // Displaying first %d matches
        mob_ids.truncate(rtdb::MAX_SEARCH);
    }
    for mob_id in mob_ids {
        let Some(mob) = super::db_find(c, mob_id as i32) else { continue };
        out.say(&format_msg(msg_txt(1289), &[&mob.jname])); // %s spawns in:
        let spawns = super::spawn::mob_get_spawns(c, mob_id);
        if spawns.is_empty() {
            out.say(msg_txt(1290)); // This monster does not spawn normally.
        } else {
            for (m, qty) in spawns {
                let Some(md) = data::maps().get(m) else { continue };
                out.say(&format!("{} ({})", md.name, qty));
            }
        }
    }
    0
}

// ---------------------------------------------------------------------------------------------- @summon / @clone

/// ACMD_FUNC(summon): @summon <monster name/id> {<duration in minutes, 1-60>}: a monster that fights for the player
/// (AI_ATTACK, master = the player), deleted after the duration.
fn summon(c: &Ctx, out: &Out, aid: u32, message: &str) -> i32 {
    let tick = c.tick();
    let Some(sd) = unit::get(c, aid) else { return -1 };
    // sscanf(message, "%23s %11d", name, &duration) < 1
    let b = crate::net::wire::latin1(message);
    let (name, mut duration) = {
        let mut s = Scan::new(&b);
        let name = if message.is_empty() { None } else { s.word(23) };
        let d = name.as_ref().and_then(|_| s.int(11)).map(|v| v as i32).unwrap_or(0);
        (name, d)
    };
    let Some(name) = name else {
        out.say(msg_txt(1225)); // Please enter a monster name (usage: @summon <monster name> {duration}).
        return -1;
    };
    duration = duration.clamp(1, 60);
    let mut mob_id = crate::cstr::atoi(&name);
    if mob_id == 0 {
        mob_id = crate::atcommand_atcmd::admin::mobdb_searchname(c, &name) as i32;
    }
    if mob_id == 0 || mobs::mobdb_checkid(mob_id) == 0 {
        out.say(msg_txt(40)); // Invalid monster ID or name.
        return -1;
    }
    let Some(mut md) = super::spawn::mob_once_spawn_sub(c, Some(&sd), sd.map, -1, -1, Some("--ja--"), mob_id, Some(""), crate::status::defs::SZ_SMALL as u8, super::AI_NONE) else {
        return -1;
    };
    md.master_id = sd.id;
    md.special_ai = super::AI_ATTACK;
    md.deletetimer = crate::timer::add(c, tick + duration as i64 * 60000, crate::timer::Func::MobTimerDelete, md.id, 0);
    super::put(c, &md);
    // clif_specialeffect(md, EF_ENTRY2, AREA) before the monster is on the map: sent around its cell
    if let Some(u) = unit::get(c, md.id) {
        crate::clif::specialeffect(c, &u, data::constant("EF_ENTRY2").unwrap_or(0) as i32, crate::clif::Target::Area);
    }
    super::spawn::mob_spawn(c, md.id);
    if let Some(u) = unit::get(c, md.id) {
        crate::status::sc::sc_start4(c, None, &u, crate::status::consts::SC_MODECHANGE, 100, 1, 0, mobs::MD_AGGRESSIVE, 0, 60000);
        super::clif::skill_poseffect(c, &sd, crate::db::skills::ids::AM_CALLHOMUN, 1, u.x, u.y, tick);
    }
    out.say(msg_txt(39)); // All monster summoned!
    0
}

/// ACMD_FUNC(clone): @clone / @slaveclone / @evilclone <char name/id>: a monster copy of a player near the caller
/// (friendly, a slave of the caller, or hostile).
fn clone(c: &Ctx, out: &Out, aid: u32, command: &str, message: &str) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return -1 };
    let b = crate::net::wire::latin1(message);
    let Some(name) = (if message.is_empty() { None } else { Scan::new(&b).set(23, b"\n") }) else {
        out.say(msg_txt(1323)); // You must enter a player name or ID.
        return 0;
    };
    let Some(pl_sd) = crate::pc::nick2sd(c, &name, true).or_else(|| crate::pc::charid2sd(c, crate::cstr::atoi(&name) as u32)) else {
        out.say(msg_txt(3)); // Character not found.
        return 0;
    };
    if crate::pc::group_level(c, pl_sd.id) > crate::pc::group_level(c, aid) {
        out.say(msg_txt(126)); // Cannot clone a player of higher GM level than yourself.
        return 0;
    }
    let parent_cmd = data::atcommands().check_alias(command.get(1..).unwrap_or(""));
    let mut flag = 0; // evilclone
    let mut master = 0;
    if crate::cstr::eq_ignore_case(parent_cmd, "clone") {
        flag = 1;
    } else if crate::cstr::eq_ignore_case(parent_cmd, "slaveclone") {
        flag = 2;
        if crate::pc::isdead(c, aid) {
            out.say(msg_txt((129 + flag * 2) as u16));
            return 0;
        }
        master = sd.id;
        if atc_slave_clone_limit() != 0 && super::spawn::mob_countslave(c, &sd) >= atc_slave_clone_limit() {
            out.say(msg_txt(127)); // You've reached your slave clones limit.
            return 0;
        }
    }
    // do { x = sd->x + rnd_value(-5, 5); y = ...; } while (map_getcell(sd->m,x,y,CELL_CHKNOPASS) && i++ < 10);
    let (mut x, mut y);
    let mut i = 0;
    loop {
        x = sd.x as i32 + c.rnd_value(-5, 5);
        y = sd.y as i32 + c.rnd_value(-5, 5);
        if !crate::map::getcell(c, sd.map, x as i16, y as i16, crate::map::CellChk::ChkNoPass) {
            break;
        }
        let more = i < 10;
        i += 1;
        if !more {
            break;
        }
    }
    if i >= 10 {
        x = sd.x as i32;
        y = sd.y as i32;
    }
    let id = super::spawn::mob_clone_spawn(c, pl_sd.id, sd.map as i32, x as i16, y as i16, "", master, mobs::MD_NONE, if flag != 0 { 1 } else { 0 }, 0);
    if id > 0 {
        out.say(msg_txt((128 + flag * 2) as u16)); // Evil Clone spawned. Clone spawned. Slave clone spawned.
        return 0;
    }
    out.say(msg_txt((129 + flag * 2) as u16)); // Unable to spawn evil clone. / clone. / slave clone.
    0
}

// ---------------------------------------------------------------------------------------------- @showmobs

/// ACMD_FUNC(showmobs): the monsters of one kind on the player's map as minimap marks (clif_viewpoint), without
/// slaves, summons and monsters waiting to respawn.
fn showmobs(c: &Ctx, out: &Out, aid: u32, message: &str) -> i32 {
    let Some(sd) = unit::get(c, aid) else { return -1 };
    // if(sscanf(message, "%99[^\n]", mob_name) < 0) return -1;  (EOF: an empty message)
    if message.is_empty() {
        return -1;
    }
    let b = crate::net::wire::latin1(message);
    let mut mob_name = Scan::new(&b).set(99, b"\n").unwrap_or_default();
    let mut mob_id = strtol(&mob_name);
    if mob_id == 0 {
        mob_id = crate::atcommand_atcmd::admin::mobdb_searchname(c, &mob_name) as i32;
    }
    let Some(mob) = super::db_find(c, mob_id).filter(|_| mobs::mobdb_checkid(mob_id) != 0) else {
        out.say(&format_msg(msg_txt(1250), &[&mob_name])); // Invalid mob id %s!
        return 0;
    };
    if mob.status.has_mode(mobs::MD_STATUSIMMUNE) && !crate::pc::has_permission(c, aid, crate::rules::PC_PERM_SHOW_BOSS) {
        out.say(msg_txt(1251)); // Can't show boss mobs!
        return 0;
    }
    if mob_id == strtol(&mob_name) && !mob.jname.is_empty() {
        mob_name = mob.jname.clone(); // --ja--
    }
    let map_name = data::maps().get(sd.map).map(|m| m.name.clone()).unwrap_or_default();
    out.say(&format_msg(msg_txt(1252), &[&mob_name, &map_name])); // Mob Search... %s %s
    // mapit_geteachmob: every monster in the id database (int DBMap order)
    let mut all: Vec<super::MobData> = c.db().mob_data().iter().collect();
    all.sort_by_key(|m| crate::party::idb_order(m.id));
    let mut number = 0;
    for md in all {
        let Some(u) = unit::get(c, md.id) else { continue };
        if u.map != sd.map {
            continue;
        }
        if mob_id != -1 && md.mob_id != mob_id {
            continue;
        }
        if md.special_ai != 0 || md.master_id != 0 {
            continue; // hide slaves and player summoned mobs
        }
        if md.spawn_timer != 0 {
            continue; // hide mobs waiting for respawn
        }
        number += 1;
        crate::clif::viewpoint(c, &sd, 1, 0, u.x as u16, u.y as u16, number, 0xFFFFFF);
    }
    0
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn fixed2_rounds_like_printf() {
        assert_eq!(c_fixed2(3.125), "3.12"); // exact tie: to even
        assert_eq!(c_fixed2(0.375), "0.38");
        assert_eq!(c_fixed2((1.0f32 / 100.0f32) as f64), "0.01");
        assert_eq!(c_fixed2(50.0), "50.00");
        assert_eq!(c_fixed2(12.5), "12.50");
        assert_eq!(c_fixed2(9.999), "10.00");
        assert_eq!(c_fixed2(0.0), "0.00");
    }

    #[test]
    fn base62_pad_and_strtol() {
        assert_eq!(base62(0), "");
        assert_eq!(base62(501), "85");
        assert_eq!(left_pad(&base62(0), '0', 5), "00000");
        assert_eq!(strtoint32def("1002"), Some(1002));
        assert_eq!(strtoint32def("poring"), None);
        assert_eq!(strtoint32def("12a"), None);
        assert_eq!(strtol("12a"), 12);
    }
}
