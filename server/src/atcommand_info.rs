//! map/atcommand.cpp: information commands: @help, @commands, @charcommands, @stats, @exp, @rates, @version, @uptime, @servertime, @gat, @mapinfo, @whogm, @idsearch, @iteminfo, @skillid, @itemlist / @storagelist / @cartlist, @gmotd, @request, @partyspy (atcmd agent, wave D). Routed from atcommand_atcmd.rs `run` by the ACMD_FUNC name.
//!
//! What differs from the oracle by design (no process, no sockets): @version names this server (as pc.rs does at
//! login), @uptime counts from the world build (world_state.built_at_tick), @servertime reads the reducer's clock,
//! and @mapinfo 1 prints session #0 (the module has no socket numbers).

use spacetimedb::Table;

use crate::atcommand_atcmd::Out;
use crate::cstr::Scan;
use crate::data::{self, format_msg, msg_txt};
use crate::net::wire::{from_latin1, latin1};
use crate::npc::{map_save_point as _, npc as _, world_state as _};
use crate::pc::pc as _;
use crate::skilltree::pc_skilltree as _;
use crate::unit::Unit;
use crate::world::Ctx;

/// atcommand.cpp atcommand_symbol (conf/battle/gm.conf `atcommand_symbol: @`).
const ATCOMMAND_SYMBOL: char = '@';
/// atcommand.cpp ATCOMMAND_LENGTH: the command name's buffer (AtCommandInfo::command) and atcommand_db's maxlen.
const ATCOMMAND_LENGTH: usize = 50;
/// map.hpp CHATBOX_SIZE (70 + 1): atcommand_commands_sub's line buffer.
const CHATBOX_SIZE: usize = 70 + 1;
/// map.hpp CHAT_SIZE_MAX (255 + 1).
const CHAT_SIZE_MAX: usize = 255 + 1;
/// mmo.hpp MAP_NAME_LENGTH (11 + 1).
const MAP_NAME_LENGTH: usize = 11 + 1;
/// mmo.hpp NAME_LENGTH (23 + 1).
const NAME_LENGTH: usize = 23 + 1;
/// itemdb.hpp MAX_SEARCH.
const MAX_SEARCH: usize = crate::mob::rtdb::MAX_SEARCH;
/// conf/map_athena.conf `enable_spy: no` (map.cpp enable_spy; the Loki import map_conf.txt does not set it).
const ENABLE_SPY: bool = false;

/// Runs `func` when it is one of this file's commands; None otherwise.
pub fn run(c: &Ctx, fd: Option<&Unit>, sd: u32, func: &str, command: &str, message: &str) -> Option<i32> {
    let out = Out { c, fd };
    Some(match func {
        "help" => help(c, &out, sd, message),
        "commands" => commands(c, &out, sd),
        "charcommands" => charcommands(c, &out, sd),
        "stats" => stats(c, &out, sd),
        "exp" => exp(c, &out, sd),
        "rates" => rates(&out),
        "version" => version(c, sd),
        "uptime" => uptime(c, &out),
        "servertime" => servertime(c, &out),
        "gat" => gat(c, &out, sd),
        "mapinfo" => mapinfo(c, &out, sd, message),
        "whogm" => whogm(c, &out, sd, message),
        "idsearch" => idsearch(&out, message),
        "iteminfo" => iteminfo(c, &out, message),
        "skillid" => skillid(&out, message),
        "itemlist" => itemlist(c, &out, sd, command),
        "gmotd" => gmotd(c),
        "request" => request(c, sd, message),
        "partyspy" => partyspy(&out, message),
        _ => return None,
    })
}

// ---------------------------------------------------------------------------------------------- C formatting helpers

/// printf("%.<prec>f", v) as glibc prints it: the exact binary value, rounded half to even on an exact tie; "-" for
/// a negative value (also when it rounds to zero); "inf" / "nan" with their sign.
fn c_fixed(v: f64, prec: usize) -> String {
    if v.is_nan() {
        return if v.is_sign_negative() { "-nan".into() } else { "nan".into() };
    }
    if v.is_infinite() {
        return if v < 0.0 { "-inf".into() } else { "inf".into() };
    }
    // every finite double has a finite decimal expansion; 80 places hold it exactly for the magnitudes printed here
    let exact = format!("{:.80}", v.abs());
    let (int_part, frac) = exact.split_once('.').unwrap_or((&exact, ""));
    let (keep, rest) = frac.split_at(prec.min(frac.len()));
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
    let split = digits.len() - prec;
    let mut s = String::new();
    if v.is_sign_negative() {
        s.push('-');
    }
    s.extend(digits[..split].iter().map(|d| (b'0' + d) as char));
    if prec > 0 {
        s.push('.');
        s.extend(digits[split..].iter().map(|d| (b'0' + d) as char));
    }
    s
}

/// snprintf(buf, n, …) / safestrncpy(dst, src, n): at most n - 1 bytes of the text (latin-1 bytes).
fn cut(s: &str, n: usize) -> String {
    let b = latin1(s);
    from_latin1(&b[..b.len().min(n.saturating_sub(1))])
}

/// ASCII tolower of latin-1 bytes (TOLOWER in the C locale).
fn lower_bytes(s: &str) -> Vec<u8> {
    latin1(s).into_iter().map(|b| b.to_ascii_lowercase()).collect()
}

/// strstr(h, n) != nullptr (an empty needle matches).
fn contains_bytes(h: &[u8], n: &[u8]) -> bool {
    n.is_empty() || h.windows(n.len()).any(|w| w == n)
}

/// strlib.cpp stristr(haystack, needle) != nullptr: TOUPPER (ASCII) comparison.
fn stristr(h: &str, n: &str) -> bool {
    let h: Vec<u8> = latin1(h).into_iter().map(|b| b.to_ascii_uppercase()).collect();
    let n: Vec<u8> = latin1(n).into_iter().map(|b| b.to_ascii_uppercase()).collect();
    contains_bytes(&h, &n)
}

/// strnicmp(a, b, n) == 0 (strncasecmp: ASCII folding, stops at the end of either string).
fn strnicmp_eq(a: &str, b: &str, n: usize) -> bool {
    let (a, b) = (latin1(a), latin1(b));
    for i in 0..n {
        let x = a.get(i).copied().unwrap_or(0).to_ascii_lowercase();
        let y = b.get(i).copied().unwrap_or(0).to_ascii_lowercase();
        if x != y {
            return false;
        }
        if x == 0 {
            return true;
        }
    }
    true
}

/// mapindex.cpp mapindex_id2name for a map the module names by its id (none: "", rAthena's indexes[0].name).
fn map_name(m: Option<u16>) -> String {
    m.and_then(|m| data::maps().get(m)).map(|d| d.name.clone()).unwrap_or_default()
}

/// mapit_getallusers: every player in pc_db, DBIterator order (int DBMap: bucket, then key).
fn all_users(c: &Ctx) -> Vec<crate::pc::Pc> {
    let mut v: Vec<crate::pc::Pc> = c.db().pc().iter().collect();
    v.sort_by_key(|p| crate::party::idb_order(p.account_id));
    v
}

/// clif_displaymessage(sd->fd, text): to the player `sd` (for a #command, the target, not the invoker).
fn say_sd(c: &Ctx, sd: u32, text: &str) {
    if let Some(u) = crate::unit::get(c, sd) {
        crate::clif::displaymessage(c, &u, text);
    }
}

// ---------------------------------------------------------------------------------------------- @help, @commands

/// ACMD_FUNC(help): the help text and aliases of a command (conf/atcommands.yml). `atcommand_get_suggestions` does
/// nothing here: conf/battle/feature.conf `feature.atcommand_suggestions: off` (its first line returns).
fn help(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let cmds = data::atcommands();
    let Some(p) = crate::pc::get(c, sd) else { return -1 };
    let mut message = message;
    let command_name = if message.is_empty() {
        "help" // If no command_name specified, display help for @help.
    } else {
        if message.starts_with(ATCOMMAND_SYMBOL) || message.starts_with(crate::atcommand::CHARCOMMAND_SYMBOL) {
            message = &message[1..];
        }
        cmds.check_alias(message)
    };
    // pc_can_use_command(sd, command_name, COMMAND_ATCOMMAND)
    if !crate::rules::can_use_command(p.group_id, command_name, false) {
        out.say(&cut(&format_msg(msg_txt(153), &[&message]), CHAT_SIZE_MAX)); // "%s is Unknown Command"
        return -1;
    }
    // attempt to find the first default help command (atcommand_alias_db.find: the command as atcommands.yml
    // writes it, case-sensitive; every entry of the file has a Help, so an entry without one never occurs)
    let Some(text) = cmds.help.get(command_name) else {
        out.say(&cut(&format_msg(msg_txt(988), &[&ATCOMMAND_SYMBOL, &command_name]), CHAT_SIZE_MAX)); // There is no help for %c%s.
        return -1;
    };
    out.say(&cut(&format_msg(msg_txt(989), &[&ATCOMMAND_SYMBOL, &command_name]), CHAT_SIZE_MAX)); // Help for command %c%s:
    // Display aliases: info->aliases is a std::set<std::string> (byte order)
    let mut aliases: Vec<&String> = cmds.aliases.iter().filter(|(_, cmd)| cmd.as_str() == command_name).map(|(a, _)| a).collect();
    aliases.sort_by_key(|a| latin1(a));
    if !aliases.is_empty() {
        let mut buf = msg_txt(990).to_string(); // Available aliases:
        for a in aliases {
            buf.push(' ');
            buf.push_str(a);
        }
        out.say(&buf);
    }
    // Display help contents
    out.say(text);
    0
}

/// atcommand_db's iteration order (db_iterator over the stridb, DB_OPT_DUP_KEY, maxlen ATCOMMAND_LENGTH, filled by
/// atcommand_basecommands in ACMD_DEF order — with Loki's @lokirnd after @mobinfo, where the oracle's patch puts it —
/// skipping a duplicate name as atcommand_exists does). The DBIterator (dbit_obj_next) walks the buckets in order,
/// each tree in order (left, node, right).
fn atcommand_db_order() -> &'static [String] {
    static ORDER: std::sync::OnceLock<Vec<String>> = std::sync::OnceLock::new();
    ORDER.get_or_init(|| {
        let cmds = &data::atcommands().commands;
        let mut db: crate::script::dbmap::StrDb<usize> = crate::script::dbmap::StrDb::new(ATCOMMAND_LENGTH as u16, true);
        for (i, cmd) in cmds.iter().enumerate() {
            // safestrncpy(atcommand->command, …, sizeof(atcommand->command))
            let mut key = latin1(&cmd.0);
            key.truncate(ATCOMMAND_LENGTH - 1);
            if db.get(&key).is_some() {
                continue; // "atcommand_basecommands: duplicate ACMD_DEF"
            }
            db.put(&key, i);
        }
        db.iter_inorder().into_iter().map(|(k, _)| from_latin1(k)).collect()
    })
}

/// atcommand_commands_sub's line buffer: CHATBOX_SIZE bytes of spaces with a NUL at the end; each name is copied at
/// `cur`, which then moves on by slen + (10 - slen % 10) (a full 10 more after a name of 10, 20 … characters).
struct Columns {
    line: Vec<u8>,
    cur: usize,
}

impl Columns {
    fn new() -> Self {
        let mut c = Columns { line: Vec::new(), cur: 0 };
        c.reset();
        c
    }
    /// cur = line_buff; memset(line_buff, ' ', CHATBOX_SIZE); line_buff[CHATBOX_SIZE-1] = 0;
    fn reset(&mut self) {
        self.cur = 0;
        self.line = vec![b' '; CHATBOX_SIZE];
        self.line[CHATBOX_SIZE - 1] = 0;
    }
    /// The buffer as clif_displaymessage reads it (up to the NUL: the padding spaces are sent too).
    fn text(&self) -> String {
        let end = self.line.iter().position(|&b| b == 0).unwrap_or(self.line.len());
        from_latin1(&self.line[..end])
    }
    /// One name; a line it would not fit into is flushed through `say` first.
    fn add(&mut self, name: &[u8], say: &mut dyn FnMut(String)) {
        let slen = name.len();
        // flush the text buffer if this command won't fit into it
        if slen + self.cur >= CHATBOX_SIZE {
            say(self.text());
            self.reset();
        }
        let end = (self.cur + slen).min(CHATBOX_SIZE - 1);
        let n = end.saturating_sub(self.cur);
        if n > 0 {
            self.line[self.cur..end].copy_from_slice(&name[..n]);
        }
        self.cur += slen + (10 - slen % 10);
    }
}

/// atcommand.cpp atcommand_commands_sub: the commands the player's group may use (AtCommandInfo::at_groups /
/// char_groups = s_player_group::can_use_command), in atcommand_db order, then the script-bound ones (bindatcmd)
/// the group level reaches, and the count.
fn commands_sub(c: &Ctx, out: &Out, sd: u32, charcommand: bool) {
    let Some(p) = crate::pc::get(c, sd) else { return };
    let mut say = |s: String| out.say(&s);
    let mut cols = Columns::new();
    let mut count = 0;
    out.say(msg_txt(273)); // "Commands available:"
    for name in atcommand_db_order() {
        if !crate::rules::can_use_command(p.group_id, name, charcommand) {
            continue;
        }
        cols.add(&latin1(name), &mut say);
        count += 1;
    }
    say(cols.text());
    let bindings = crate::script::buildins::script2::admin::bindings(c);
    if !bindings.is_empty() {
        let gm_lvl = crate::rules::group_level(p.group_id);
        let mut count_bind = 0;
        for b in &bindings {
            if gm_lvl >= if charcommand { b.level2 } else { b.level } {
                if count_bind == 0 {
                    cols.reset();
                    say("-----------------".to_string());
                    say(msg_txt(509).to_string()); // Script-bound commands:
                }
                cols.add(&latin1(&b.command), &mut say);
                count_bind += 1;
            }
        }
        if count_bind > 0 {
            say(cols.text()); // last one
        }
        count += count_bind;
    }
    out.say(&format_msg(msg_txt(274), &[&count])); // "%d commands found."
}

/// ACMD_FUNC(commands): the @commands available.
fn commands(c: &Ctx, out: &Out, sd: u32) -> i32 {
    commands_sub(c, out, sd, false);
    0
}

/// ACMD_FUNC(charcommands): the #commands available.
fn charcommands(c: &Ctx, out: &Out, sd: u32) -> i32 {
    commands_sub(c, out, sd, true);
    0
}

// ---------------------------------------------------------------------------------------------- @stats, @exp, @rates

/// ACMD_FUNC(stats): the character's sd->status (hp / sp / ap as the last pc_makesavestatus left them, as in
/// rAthena) and the job change levels.
fn stats(c: &Ctx, out: &Out, sd: u32) -> i32 {
    let Some(ch) = crate::pc::character_of(c, sd) else { return -1 };
    let st = c.db().pc_skilltree().account_id().find(sd);
    let (l2, l3, l4) = st.map(|s| (s.change_level_2nd as i32, s.change_level_3rd as i32, s.change_level_4th as i32)).unwrap_or((0, 0, 0));
    // sprintf(job_jobname, "Job - %s %s", job_name(class_), "(level %d)"), then used as the job level's format
    let job_jobname = format!("Job - {} {}", crate::db::jobs::job_name(ch.class as i32), "(level %d)");
    let table: [(&str, i32); 27] = [
        ("Base Level - %d", ch.base_level as i32),
        (job_jobname.as_str(), ch.job_level as i32),
        ("Hp - %d", ch.hp as i32),
        ("MaxHp - %d", ch.max_hp as i32),
        ("Sp - %d", ch.sp as i32),
        ("MaxSp - %d", ch.max_sp as i32),
        ("Ap - %d", ch.ap as i32),
        ("MaxAp - %d", ch.max_ap as i32),
        ("Str - %3d", ch.str as i16 as i32),
        ("Agi - %3d", ch.agi as i16 as i32),
        ("Vit - %3d", ch.vit as i16 as i32),
        ("Int - %3d", ch.int as i16 as i32),
        ("Dex - %3d", ch.dex as i16 as i32),
        ("Luk - %3d", ch.luk as i16 as i32),
        ("Pow - %3d", ch.pow as i16 as i32),
        ("Sta - %3d", ch.sta as i16 as i32),
        ("Wis - %3d", ch.wis as i16 as i32),
        ("Spl - %3d", ch.spl as i16 as i32),
        ("Con - %3d", ch.con as i16 as i32),
        ("Crt - %3d", ch.crt as i16 as i32),
        ("Zeny - %d", ch.zeny as i32),
        ("Free Status Points - %d", ch.status_point as i32),
        ("Free Trait Points - %d", ch.trait_point as i32),
        ("Free Skill Points - %d", ch.skill_point as i32),
        ("JobChangeLvl (2nd) - %d", l2),
        ("JobChangeLvl (3rd) - %d", l3),
        ("JobChangeLvl (4th) - %d", l4),
    ];
    out.say(&format_msg(msg_txt(53), &[&ch.name])); // '%s' stats:
    for (fmt, value) in table {
        let s = crate::script::cfmt::printf1(&latin1(fmt), Some(crate::script::cfmt::Arg::Int(value)));
        out.say(&from_latin1(&s));
    }
    0
}

/// ACMD_FUNC(exp): the levels and the percentage of the next one (pc_nextbaseexp / pc_nextjobexp).
fn exp(c: &Ctx, out: &Out, sd: u32) -> i32 {
    let Some(ch) = crate::pc::character_of(c, sd) else { return -1 };
    let mut nextb = crate::db::jobs::pc_nextbaseexp(ch.class, ch.base_level as u32) as f64;
    if nextb != 0.0 {
        nextb = ch.base_exp as f64 * 100.0 / nextb;
    }
    let mut nextj = crate::db::jobs::pc_nextjobexp(ch.class, ch.job_level as u32) as f64;
    if nextj != 0.0 {
        nextj = ch.job_exp as f64 * 100.0 / nextj;
    }
    // Base Level: %d (%.3f%%) | Job Level: %d (%.3f%%)
    out.say(&format_msg(msg_txt(1148), &[&ch.base_level, &c_fixed(nextb, 3), &ch.job_level, &c_fixed(nextj, 3)]));
    0
}

/// ACMD_FUNC(rates): the experience and drop rates (battle_config: conf/battle/exp.conf and drops.conf, with
/// conf/import/battle_conf.txt's base / job 500). pc_isvip is false: VIP_ENABLE is not defined (config/core.hpp),
/// so the vip_* increases add nothing.
fn rates(out: &Out) -> i32 {
    use crate::script::battle_flags::battle_get_value as bc;
    const PC_ISVIP: bool = false;
    let r = |key: &str, vip_key: &str| -> String {
        let v = bc(key);
        let add = if PC_ISVIP { bc(vip_key) * v / 100 } else { 0 };
        c_fixed((v + add) as f64 / 100.0, 2)
    };
    let d = |key: &str| r(key, "vip_drop_increase");
    let line = |n: u16, args: &[String]| {
        let a: Vec<&dyn std::fmt::Display> = args.iter().map(|s| s as &dyn std::fmt::Display).collect();
        out.say(&cut(&format_msg(msg_txt(n), &a), CHAT_SIZE_MAX));
    };
    // Experience rates: Base %.2fx / Job %.2fx
    line(1298, &[r("base_exp_rate", "vip_base_exp_increase"), r("job_exp_rate", "vip_job_exp_increase")]);
    // Normal Drop Rates: Common %.2fx / Healing %.2fx / Usable %.2fx / Equipment %.2fx / Card %.2fx
    line(1299, &[d("item_rate_common"), d("item_rate_heal"), d("item_rate_use"), d("item_rate_equip"), d("item_rate_card")]);
    // Boss Drop Rates: …
    line(1300, &[d("item_rate_common_boss"), d("item_rate_heal_boss"), d("item_rate_use_boss"), d("item_rate_equip_boss"), d("item_rate_card_boss")]);
    // MVP Drop Rates: …
    line(1024, &[d("item_rate_common_mvp"), d("item_rate_heal_mvp"), d("item_rate_use_mvp"), d("item_rate_equip_mvp"), d("item_rate_card_mvp")]);
    // Other Drop Rates: MvP %.2fx / Card-Based %.2fx / Treasure %.2fx
    line(1301, &[d("item_rate_mvp"), d("item_rate_adddrop"), d("item_rate_treasure")]);
    0
}

// ---------------------------------------------------------------------------------------------- server facts

/// The line pc_show_version prints here. rAthena prints msg 1295 with its SVN revision / Git hash (the oracle, built
/// outside a checkout, prints msg 1296 "Cannot determine SVN/Git version."); the module has neither and names itself
/// instead, with the text the login shows (pc.rs, pc_authok's pc_show_version). Deliberately different.
pub(crate) const VERSION_LINE: &str = "Loki Online (a SpacetimeDB module; rules ported from rAthena e985006)";

/// pc.cpp pc_show_version: to sd->fd (see [`VERSION_LINE`]).
pub(crate) fn pc_show_version(c: &Ctx, sd: u32) {
    say_sd(c, sd, VERSION_LINE);
}

/// ACMD_FUNC(version): pc_show_version(sd).
fn version(c: &Ctx, sd: u32) -> i32 {
    pc_show_version(c, sd);
    0
}

/// ACMD_FUNC(uptime): timer.cpp get_uptime (difftime(time(nullptr), start_time): whole seconds since the map server
/// started). The module has no process start: the closest equivalent is the world build (npc.rs world_state
/// .built_at_tick: a new database, or a publish with new data), which rAthena's start also is. Differs by design.
fn uptime(c: &Ctx, out: &Out) -> i32 {
    let start = c.db().world_state().key().find(0).map(|w| w.built_at_tick).unwrap_or(c.tick());
    let mut seconds = (c.tick().div_euclid(1000) - start.div_euclid(1000)).max(0);
    let (day, hour, minute) = (24 * 60 * 60, 60 * 60, 60);
    let days = seconds / day;
    seconds -= if seconds / day > 0 { (seconds / day) * day } else { 0 };
    let hours = seconds / hour;
    seconds -= if seconds / hour > 0 { (seconds / hour) * hour } else { 0 };
    let minutes = seconds / minute;
    seconds -= if seconds / minute > 0 { (seconds / minute) * minute } else { 0 };
    // Server Uptime: %ld days, %ld hours, %ld minutes, %ld seconds.
    out.say(&cut(&format_msg(msg_txt(245), &[&days, &hours, &minutes, &seconds]), CHAT_SIZE_MAX));
    0
}

/// atcommand.cpp txt_time: "%d day(s) %d hour(s) %d minute(s) and %d second(s)" pieces. rAthena quirk kept: the
/// result is the static temp1, so with 0 seconds it is the hours piece (or empty) and the minutes are lost.
fn txt_time(duration: i64) -> String {
    let mut duration = duration as i32; // Cap it
    let days = duration / (60 * 60 * 24);
    duration -= 60 * 60 * 24 * days;
    let hours = duration / (60 * 60);
    duration -= 60 * 60 * hours;
    let minutes = duration / 60;
    let seconds = duration - 60 * minutes;
    let mut temp = String::new();
    let mut temp1 = String::new();
    if days == 1 {
        temp = format_msg(msg_txt(219), &[&days]); // %d day
    } else if days > 1 {
        temp = format_msg(msg_txt(220), &[&days]); // %d days
    }
    if hours == 1 {
        temp1 = format_msg(msg_txt(221), &[&temp, &hours]); // %s %d hour
    } else if hours > 1 {
        temp1 = format_msg(msg_txt(222), &[&temp, &hours]); // %s %d hours
    }
    if minutes < 2 {
        temp = format_msg(msg_txt(223), &[&temp1, &minutes]); // %s %d minute
    } else {
        temp = format_msg(msg_txt(224), &[&temp1, &minutes]); // %s %d minutes
    }
    if seconds == 1 {
        temp1 = format_msg(msg_txt(225), &[&temp, &seconds]); // %s and %d second
    } else if seconds > 1 {
        temp1 = format_msg(msg_txt(226), &[&temp, &seconds]); // %s and %d seconds
    }
    temp1
}

/// get_timer(night_timer_tid / day_timer_tid): (tick, interval). pc.cpp do_init_pc arms the day / night timers only
/// when day_duration and night_duration are both > 0 (conf/battle/misc.conf: both 0), and the module has no such
/// timers (pc_misc.rs: only @day / @night and the script commands switch): never one.
fn daynight_timer(_night: bool) -> Option<(i64, i64)> {
    None
}

/// ACMD_FUNC(servertime): the server's local time (localtime in the oracle's time zone, script/npc_run.rs; the
/// reducer's clock, so the seconds can differ from the oracle's by design) and the day / night state.
fn servertime(c: &Ctx, out: &Out) -> i32 {
    use crate::script::battle_flags::battle_get_value as bc;
    let tm = crate::script::npc_run::localtime(c.tick().div_euclid(1000));
    // strftime(temp, sizeof(temp)-1, msg_txt(230), datetime): Server time (normal time): %A, %B %d %Y %X.
    let s = crate::script::cfmt::strftime(&latin1(msg_txt(230)), CHAT_SIZE_MAX - 1, &tm);
    out.say(&from_latin1(&s));
    let night_flag = crate::pc_misc::night_flag(c);
    let (day_duration, night_duration) = (bc("day_duration"), bc("night_duration"));
    let now = c.tick();
    let say_t = |n: u16, secs: i64| out.say(&format_msg(msg_txt(n), &[&txt_time(secs)]));
    if night_duration == 0 && day_duration == 0 {
        out.say(msg_txt(if !night_flag { 231 } else { 232 })); // permanent daylight / permanent night
    } else if night_duration == 0 {
        if night_flag {
            // we start with night
            match daynight_timer(false) {
                Some((tick, _)) => {
                    say_t(233, (tick - now) / 1000); // Game time: The game is in night for %s.
                    out.say(msg_txt(234)); // Game time: After, the game will be in permanent daylight.
                }
                None => out.say(msg_txt(232)),
            }
        } else {
            out.say(msg_txt(231));
        }
    } else if day_duration == 0 {
        if !night_flag {
            // we start with day
            match daynight_timer(true) {
                Some((tick, _)) => {
                    say_t(235, (tick - now) / 1000); // Game time: The game is in daylight for %s.
                    out.say(msg_txt(236)); // Game time: After, the game will be in permanent night.
                }
                None => out.say(msg_txt(231)),
            }
        } else {
            out.say(msg_txt(232));
        }
    } else if !night_flag {
        match (daynight_timer(true), daynight_timer(false)) {
            (Some((t1, i1)), Some((t2, _))) => {
                say_t(235, (t1 - now) / 1000);
                if t1 - t2 > 0 {
                    say_t(237, (i1 - (t1 - t2)) / 1000); // Game time: After, the game will be in night for %s.
                } else {
                    say_t(237, (t2 - t1) / 1000);
                }
                say_t(238, i1 / 1000); // Game time: A day cycle has a normal duration of %s.
            }
            _ => out.say(msg_txt(231)),
        }
    } else {
        match (daynight_timer(false), daynight_timer(true)) {
            (Some((t1, i1)), Some((t2, _))) => {
                say_t(233, (t1 - now) / 1000);
                if t1 - t2 > 0 {
                    say_t(239, (i1 - (t1 - t2)) / 1000); // Game time: After, the game will be in daylight for %s.
                } else {
                    say_t(239, (t2 - t1) / 1000);
                }
                say_t(238, i1 / 1000);
            }
            _ => out.say(msg_txt(232)),
        }
    }
    0
}

// ---------------------------------------------------------------------------------------------- @gat, @mapinfo

/// ACMD_FUNC(gat): the gat types (map_getcell CELL_GETTYPE) of the 5x5 cells around the player, top row first.
fn gat(c: &Ctx, out: &Out, sd: u32) -> i32 {
    use crate::map_cell::{getcell, CELL_GETTYPE};
    let Some(u) = crate::unit::get(c, sd) else { return -1 };
    let name = map_name(Some(u.map));
    let (x, y) = (u.x as i32, u.y as i32);
    for dy in (-2..=2).rev() {
        let cell = |dx: i32| getcell(c, Some(u.map), (x + dx) as i16, (y + dy) as i16, CELL_GETTYPE);
        out.say(&format!("{} (x= {}, y= {}) {:02X} {:02X} {:02X} {:02X} {:02X}", name, x - 2, y + dy, cell(-2), cell(-1), cell(0), cell(1), cell(2)));
    }
    0
}

/// e_mapflag values (map.hpp) that npc.rs does not name.
mod mf {
    pub const NOBRANCH: i16 = 3;
    pub const PVP_NOPARTY: i16 = 7;
    pub const PVP_NOGUILD: i16 = 8;
    pub const GVG_NOPARTY: i16 = 10;
    pub const NOTRADE: i16 = 11;
    pub const NOICEWALL: i16 = 15;
    pub const NIGHTENABLED: i16 = 28;
    pub const NODROP: i16 = 38;
    pub const NOVENDING: i16 = 41;
    pub const LOADEVENT: i16 = 42;
    pub const GUILDLOCK: i16 = 45;
    pub const AUTOTRADE: i16 = 47;
    pub const ALLOWKS: i16 = 48;
    pub const MONSTER_NOTELEPORT: i16 = 49;
    pub const PVP_NOCALCRANK: i16 = 50;
    pub const RESET: i16 = 52;
    pub const NOMAPCHANNELAUTOJOIN: i16 = 53;
    pub const NOITEMCONSUMPTION: i16 = 55;
    pub const NOTOMB: i16 = 59;
    pub const PRIVATEAIRSHIP_SOURCE: i16 = 67;
    pub const PRIVATEAIRSHIP_DESTINATION: i16 = 68;
    pub const NOCASHSHOP: i16 = 70;
    pub const NORODEX: i16 = 71;
    pub const NORENEWALEXPPENALTY: i16 = 72;
    pub const NORENEWALDROPPENALTY: i16 = 73;
    pub const NOPETCAPTURE: i16 = 74;
    pub const NOBUYINGSTORE: i16 = 75;
    pub const NODYNAMICNPC: i16 = 76;
    pub const NOBANK: i16 = 77;
}

/// map.cpp map_addnpc's order of mapdata->npc[]: NPCs with a trigger area (warps and scripts with xs, ys >= 0) are
/// grouped at the front, warps first (0 < npc_num_warp < npc_num_area < npc_num); a new warp takes the first
/// script's slot, which takes the first other NPC's slot, which goes to the end; a new script takes the first other
/// NPC's slot, which goes to the end; any other NPC is appended. `entries`: (npc id, subtype, has an area), in the
/// order map_addnpc was called.
fn addnpc_order(entries: &[(u32, u8, bool)]) -> Vec<u32> {
    use crate::npc::{NPCTYPE_SCRIPT, NPCTYPE_WARP};
    let mut npc: Vec<u32> = Vec::with_capacity(entries.len());
    let (mut num_area, mut num_warp) = (0usize, 0usize);
    for &(id, subtype, area) in entries {
        let num = npc.len();
        npc.push(0);
        if !area {
            npc[num] = id;
        } else if subtype == NPCTYPE_WARP {
            npc[num] = npc[num_area];
            npc[num_area] = npc[num_warp];
            npc[num_warp] = id;
            num_warp += 1;
            num_area += 1;
        } else if subtype == NPCTYPE_SCRIPT {
            npc[num] = npc[num_area];
            npc[num_area] = id;
            num_area += 1;
        } else {
            npc[num] = id;
        }
    }
    npc
}

/// mapdata->npc[0 .. npc_num]. The module keeps no per-map NPC array: it is rebuilt by replaying map_addnpc over the
/// NPCs now on the map in id order, which is the order the NPC files, the barters and the run-time duplicates were
/// added in. Known difference: after map_delnpc (an NPC unloaded or moved away while others stay) rAthena's array
/// has the removal's reshuffle, which this replay does not reproduce.
fn map_npcs(c: &Ctx, m: u16) -> Vec<crate::npc::Npc> {
    use crate::npc::{NPCTYPE_SCRIPT, NPCTYPE_WARP};
    let mut nds: Vec<crate::npc::Npc> = c.db().npc().by_map().filter(m as i32).collect();
    nds.sort_by_key(|n| n.id);
    let entries: Vec<(u32, u8, bool)> = nds.iter().map(|n| (n.id, n.subtype, (n.subtype == NPCTYPE_WARP || n.subtype == NPCTYPE_SCRIPT) && n.xs >= 0 && n.ys >= 0)).collect();
    let order = addnpc_order(&entries);
    order.into_iter().filter_map(|id| nds.iter().find(|n| n.id == id).cloned()).collect()
}

/// sd->fd for @mapinfo 1: the module has no socket numbers (a connection is a SpacetimeDB identity), so every session
/// shows 0, which is also rAthena's value for a character without its client. Differs by design.
fn session_fd(_p: &crate::pc::Pc) -> i32 {
    0
}

/// ACMD_FUNC(mapinfo): @mapinfo <0-3> {<map>}: map facts and flags; 1 lists the players, 2 the NPCs, 3 the chats.
fn mapinfo(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    use crate::npc::{
        mapflag, mapflag_value, MF_BATTLEGROUND, MF_CLOUDS, MF_CLOUDS2, MF_FIREWORKS, MF_FOG, MF_FORCEMINEFFECT, MF_GVG, MF_GVG_CASTLE, MF_GVG_DUNGEON,
        MF_GVG_TE, MF_GVG_TE_CASTLE, MF_HIDEMOBHPBAR, MF_LEAVES, MF_NOBASEEXP, MF_NOCOMMAND, MF_NOCOSTUME, MF_NOEXPPENALTY, MF_NOGO, MF_NOJOBEXP,
        MF_NOLOCKON, MF_NOMEMO, MF_NOMOBLOOT, MF_NOMVPLOOT, MF_NORETURN, MF_NOSAVE, MF_NOSKILL, MF_NOSUNMOONSTARMIRACLE, MF_NOTELEPORT, MF_NOUSECART,
        MF_NOWARP, MF_NOWARPTO, MF_NOZENYPENALTY, MF_PARTYLOCK, MF_PVP, MF_PVP_NIGHTMAREDROP, MF_RESTRICTED, MF_SAKURA, MF_SKILL_DAMAGE,
        MF_SKILL_DURATION, MF_SNOW, MF_TOWN,
    };
    let Some(p) = crate::pc::get(c, sd) else { return -1 };
    // sscanf(message, "%11d %11[^\n]", &list, mapname)
    let b = latin1(message);
    let mut s = Scan::new(&b);
    let mut list = 0i32;
    let mut mapname = String::new();
    if let Some(v) = s.int(11) {
        list = v as i32;
        s.ws();
        if let Some(n) = s.set(11, b"\n") {
            mapname = n;
        }
    }
    if !(0..=3).contains(&list) {
        out.say(msg_txt(1038)); // Please enter at least one valid list number (usage: @mapinfo <0-3> <map>).
        return -1;
    }
    let m_id = if mapname.is_empty() {
        mapname = cut(&map_name(p.mapindex), MAP_NAME_LENGTH);
        p.mapindex
    } else {
        data::maps().id_of(&mapname)
    };
    let Some(m) = m_id else {
        out.say(msg_txt(1)); // Map not found.
        return -1;
    };
    // m_index = mapindex_name2id(mapname): the module's players carry the map id
    let m_index = Some(m);
    out.say(msg_txt(1039)); // ------ Map Info ------
    // count chats (for initial message)
    let users = all_users(c);
    let (mut chat_num, mut vend_num) = (0, 0);
    for pl in &users {
        if pl.mapindex == m_index {
            if pl.vending {
                vend_num += 1;
            } else if crate::chat::get(c, pl.chat_id).map(|cd| cd.users.first() == Some(&pl.account_id)).unwrap_or(false) {
                chat_num += 1;
            }
        }
    }
    let npcs = map_npcs(c, m);
    // Map: %s | Players: %d | NPCs: %d | Chats: %d | Vendings: %d
    out.say(&format_msg(msg_txt(1040), &[&mapname, &crate::mob::map_users(c, m), &npcs.len(), &chat_num, &vend_num]));
    out.say(msg_txt(1041)); // ------ Map Flags ------
    let on = |f: i16| mapflag(c, m, f);
    if on(MF_TOWN) {
        out.say(msg_txt(1042)); // Town Map
    }
    if on(MF_RESTRICTED) {
        // map_getmapflag(MF_RESTRICTED) is mapdata->zone (npc.rs keeps the zone bits as the flag's value)
        out.say(&format!(" Restricted (zone {})", mapflag_value(c, m, MF_RESTRICTED)));
    }
    // battle_config.autotrade_mapflag: conf/battle/misc.conf `at_mapflag: no`
    if crate::script::battle_flags::battle_get_value("at_mapflag") == mapflag_value(c, m, mf::AUTOTRADE) {
        out.say(msg_txt(1043)); // Autotrade Enabled
    } else {
        out.say(msg_txt(1044)); // Autotrade Disabled
    }
    if on(MF_BATTLEGROUND) {
        out.say(&format_msg(msg_txt(1045), &[&mapflag_value(c, m, MF_BATTLEGROUND)])); // Battlegrounds ON (type %d)
    }
    // Skill damage adjustment info [Cydh]
    if on(MF_SKILL_DAMAGE) {
        out.say(msg_txt(1052)); // Skill Damage Adjustments:
        let a = crate::map_skill_damage::damage_adjust(c, m);
        // > [Map] %d%%, %d%%, %d%%, %d%% | Caster:%d
        out.say(&format_msg(msg_txt(1053), &[&a.rate[0], &a.rate[1], &a.rate[2], &a.rate[3], &a.caster]));
        let list = crate::map_skill_damage::skill_damage_list(c, m);
        if !list.is_empty() {
            out.say(msg_txt(1054)); // > [Map Skill] Name : Player, Monster, Boss Monster, Other | Caster
            for (skill_id, d) in list {
                out.say(&format!(
                    "     {} : {}%, {}%, {}%, {}% | {}",
                    crate::db::skills::skill_get_name(skill_id),
                    d.rate[0],
                    d.rate[1],
                    d.rate[2],
                    d.rate[3],
                    d.caster
                ));
            }
        }
    }
    if on(MF_SKILL_DURATION) {
        out.say(msg_txt(1055)); // Skill Duration Adjustments:
        for (skill_id, per) in crate::skillunit::data::map_skill_duration_list(c, m) {
            out.say(&format!(" > {} : {}%", crate::db::skills::skill_get_name(skill_id), per));
        }
    }
    let flags = |head: u16, list: &[(i16, &str)]| {
        let mut o = msg_txt(head).to_string();
        for &(f, t) in list {
            if on(f) {
                o.push_str(t);
            }
        }
        out.say(&o);
    };
    // PvP Flags:
    flags(1046, &[(MF_PVP, " Pvp ON |"), (mf::PVP_NOGUILD, " NoGuild |"), (mf::PVP_NOPARTY, " NoParty |"), (MF_PVP_NIGHTMAREDROP, " NightmareDrop |"), (mf::PVP_NOCALCRANK, " NoCalcRank |")]);
    // GvG Flags:
    flags(
        1047,
        &[(MF_GVG, " GvG ON |"), (MF_GVG_DUNGEON, " GvG Dungeon |"), (MF_GVG_CASTLE, " GvG Castle |"), (MF_GVG_TE, " GvG TE |"), (MF_GVG_TE_CASTLE, " GvG TE Castle |"), (mf::GVG_NOPARTY, " NoParty |")],
    );
    // Teleport Flags:
    flags(
        1048,
        &[
            (MF_NOTELEPORT, " NoTeleport |"),
            (mf::MONSTER_NOTELEPORT, " Monster NoTeleport |"),
            (MF_NOWARP, " NoWarp |"),
            (MF_NOWARPTO, " NoWarpTo |"),
            (MF_NORETURN, " NoReturn |"),
            (MF_NOGO, " NoGo |"),
            (MF_NOMEMO, "  NoMemo |"),
            (mf::PRIVATEAIRSHIP_SOURCE, " PrivateAirship_Source |"),
            (mf::PRIVATEAIRSHIP_DESTINATION, " PrivateAirship_Destination |"),
        ],
    );
    let onoff = |f: i16| msg_txt(if on(f) { 1066 } else { 1067 }); // On / Off
    out.say(&format_msg(msg_txt(1065), &[&onoff(MF_NOEXPPENALTY), &onoff(MF_NOZENYPENALTY)])); // No Exp Penalty: %s | No Zeny Penalty: %s
    if on(MF_NOSAVE) {
        // mapdata->save: the nosave flag's point (npc.rs map_save_point; "SavePoint" is save_map -1 there, map 0 in
        // rAthena)
        match c.db().map_save_point().map().find(m).filter(|sp| sp.save_map >= 0) {
            None => out.say(msg_txt(1068)), // No Save (Return to last Save Point)
            Some(sp) => {
                let name = map_name(Some(sp.save_map as u16));
                if sp.x == -1 || sp.y == -1 {
                    out.say(&format_msg(msg_txt(1069), &[&name])); // No Save, Save Point: %s,Random
                } else {
                    out.say(&format_msg(msg_txt(1070), &[&name, &sp.x, &sp.y])); // No Save, Save Point: %s,%d,%d
                }
            }
        }
    }
    // Weather Flags:
    flags(
        1049,
        &[
            (MF_SNOW, " Snow |"),
            (MF_FOG, " Fog |"),
            (MF_SAKURA, " Sakura |"),
            (MF_CLOUDS, " Clouds |"),
            (MF_CLOUDS2, "  Clouds2 |"),
            (MF_FIREWORKS, " Fireworks |"),
            (MF_LEAVES, "  Leaves |"),
            (mf::NIGHTENABLED, "  Displays Night |"),
        ],
    );
    // Other Flags:
    flags(
        1050,
        &[
            (mf::NOBRANCH, " NoBranch |"),
            (mf::NOTRADE, " NoTrade |"),
            (mf::NOVENDING, " NoVending |"),
            (mf::NOBUYINGSTORE, " NoBuyingstore |"),
            (mf::NODROP, " NoDrop |"),
            (MF_NOSKILL, " NoSkill |"),
            (mf::NOICEWALL, " NoIcewall |"),
            (mf::ALLOWKS, " AllowKS |"),
            (mf::RESET, " Reset |"),
            (MF_HIDEMOBHPBAR, " HideMobHPBar |"),
            (MF_NOCOMMAND, " NoCommand |"),
            (MF_NOBASEEXP, " NoBaseEXP |"),
            (MF_NOJOBEXP, " NoJobEXP |"),
            (MF_NOMOBLOOT, " NoMobLoot |"),
            (MF_NOMVPLOOT, " NoMVPLoot |"),
            (mf::NORENEWALEXPPENALTY, " NoRenewalExpPenalty |"),
            (mf::NORENEWALDROPPENALTY, " NoRenewalDropPenalty |"),
            (MF_PARTYLOCK, " PartyLock |"),
            (mf::GUILDLOCK, " GuildLock |"),
            (mf::LOADEVENT, " Loadevent |"),
            (mf::NODYNAMICNPC, " NoDynamicNPC |"),
            (mf::NOMAPCHANNELAUTOJOIN, " NoMapChannelAutoJoin |"),
            (MF_NOUSECART, " NoUsecart |"),
            (mf::NOITEMCONSUMPTION, " NoItemConsumption |"),
            (MF_NOSUNMOONSTARMIRACLE, " NoSunMoonStarMiracle |"),
            (MF_FORCEMINEFFECT, " ForceMinEffect |"),
            (MF_NOLOCKON, " NoLockOn |"),
            (mf::NOTOMB, " NoTomb |"),
            (MF_NOCOSTUME, " NoCostume |"),
            (mf::NOBANK, " NoBank |"),
            (mf::NOCASHSHOP, " NoCashShop |"),
            (mf::NORODEX, " NoRODex |"),
            (mf::NOPETCAPTURE, " NoPetCapture |"),
        ],
    );
    match list {
        1 => {
            out.say(msg_txt(480)); // ----- Players in Map -----
            for pl in &users {
                if pl.mapindex == m_index {
                    let name = crate::pc::character_of(c, pl.account_id).map(|ch| ch.name).unwrap_or_default();
                    let (x, y) = crate::unit::get(c, pl.account_id).map(|u| (u.x, u.y)).unwrap_or((0, 0));
                    // Player '%s' (session #%d) | Location: %d,%d
                    out.say(&format_msg(msg_txt(481), &[&name, &session_fd(pl), &x, &y]));
                }
            }
        }
        2 => {
            out.say(msg_txt(482)); // ----- NPCs in Map -----
            for (i, nd) in npcs.iter().enumerate() {
                // nd->ud.dir (an NPC outside its map's bounds has no unit row here: 0)
                let dir = crate::unit::get(c, nd.id).map(|u| u.ud.dir).unwrap_or(0);
                let direction = msg_txt(match dir {
                    0 => 491, // North
                    1 => 492, // North West
                    2 => 493, // West
                    3 => 494, // South West
                    4 => 495, // South
                    5 => 496, // South East
                    6 => 497, // East
                    7 => 498, // North East
                    _ => 499, // Unknown
                });
                if latin1(&nd.name) == latin1(&nd.exname) {
                    // NPC %d: %s | Direction: %s | Sprite: %d | Location: %d %d
                    out.say(&format_msg(msg_txt(490), &[&(i + 1), &nd.name, &direction, &nd.class, &nd.x, &nd.y]));
                } else {
                    // NPC %d: %s::%s | Direction: %s | Sprite: %d | Location: %d %d
                    out.say(&format_msg(msg_txt(489), &[&(i + 1), &nd.name, &nd.exname, &direction, &nd.class, &nd.x, &nd.y]));
                }
            }
        }
        3 => {
            out.say(msg_txt(483)); // ----- Chats in Map -----
            for pl in &users {
                let Some(cd) = crate::chat::get(c, pl.chat_id) else { continue };
                if pl.mapindex == m_index && cd.users.first() == Some(&pl.account_id) {
                    let name = crate::pc::character_of(c, pl.account_id).map(|ch| ch.name).unwrap_or_default();
                    // Chat: %s | Player: %s | Location: %d %d
                    out.say(&format_msg(msg_txt(484), &[&from_latin1(&cd.title), &name, &cd.x, &cd.y]));
                    //    Users: %d/%d | Password: %s | Public: %s
                    let public = msg_txt(if cd.is_pub { 486 } else { 487 }); // Yes / No
                    out.say(&format_msg(msg_txt(485), &[&cd.users.len(), &cd.limit, &from_latin1(&cd.pass), &public]));
                }
            }
        }
        _ => {} // 0: Do nothing. It's list 0, no additional display.
    }
    0
}

// ---------------------------------------------------------------------------------------------- @whogm

/// ACMD_FUNC(whogm): the online GMs (group level > 0) whose name contains the text (case-insensitive); those above
/// the invoker's level show only their name, and not at all while invisible (@hide).
fn whogm(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    // sscanf(message, "%255[^\n]", match_text), lowered
    let match_text = Scan::new(&latin1(message)).set(255, b"\n").map(|t| lower_bytes(&t)).unwrap_or_default();
    let mut count = 0;
    let level = crate::pc::group_level(c, sd);
    for pl in all_users(c) {
        let pl_level = crate::rules::group_level(pl.group_id);
        if pl_level == 0 {
            continue;
        }
        let Some(ch) = crate::pc::character_of(c, pl.account_id) else { continue };
        if !match_text.is_empty() && !contains_bytes(&lower_bytes(&ch.name), &match_text) {
            continue; // search with no case sensitive
        }
        let u = crate::unit::get(c, pl.account_id);
        if pl_level > level {
            // pc_isinvisible: sd->sc.option & OPTION_INVISIBLE
            if u.as_ref().map(|u| u.sc.option & crate::unit::OPTION_INVISIBLE != 0).unwrap_or(false) {
                continue;
            }
            out.say(&format_msg(msg_txt(913), &[&ch.name])); // Name: %s (GM)
            count += 1;
            continue;
        }
        let (x, y) = u.as_ref().map(|u| (u.x, u.y)).unwrap_or((0, 0));
        // Name: %s (GM:%d) | Location: %s %d %d
        out.say(&format_msg(msg_txt(914), &[&ch.name, &pl_level, &map_name(pl.mapindex), &x, &y]));
        // BLvl: %d | Job: %s (Lvl: %d)
        out.say(&format_msg(msg_txt(915), &[&ch.base_level, &crate::db::jobs::job_name(ch.class as i32), &ch.job_level]));
        let party = crate::party::party_search(c, ch.party_id).map(|p| p.party.name).unwrap_or_else(|| msg_txt(917).to_string());
        let guild = crate::guild::sd_guild(c, pl.account_id).map(|g| g.guild.name).unwrap_or_else(|| msg_txt(917).to_string());
        out.say(&format_msg(msg_txt(916), &[&party, &guild])); // Party: '%s' | Guild: '%s'
        count += 1;
    }
    match count {
        0 => out.say(msg_txt(150)),                          // No GM found.
        1 => out.say(msg_txt(151)),                          // 1 GM found.
        _ => out.say(&format_msg(msg_txt(152), &[&count])), // %d GMs found.
    }
    0
}

// ---------------------------------------------------------------------------------------------- item and skill searches

/// ACMD_FUNC(idsearch): the items whose names contain the text (itemdb_searchname_array: at most MAX_SEARCH, by id).
fn idsearch(out: &Out, message: &str) -> i32 {
    // sscanf(message, "%99s", item_name) < 0: only an all-blank message (EOF)
    let item_name = Scan::new(&latin1(message)).word(99);
    let Some(item_name) = item_name.filter(|_| !message.is_empty()) else {
        out.say(msg_txt(1031)); // Please enter part of an item name (usage: @idsearch <part_of_item_name>).
        return -1;
    };
    out.say(&cut(&format_msg(msg_txt(77), &[&item_name]), CHAT_SIZE_MAX)); // The reference result of '%s' (name: id):
    let items = crate::db::items::itemdb_searchname_array(&item_name, MAX_SEARCH);
    if items.len() == MAX_SEARCH {
        out.say(&format_msg(msg_txt(269), &[&MAX_SEARCH])); // Displaying first %d matches
    }
    for id in &items {
        out.say(&format_msg(msg_txt(78), &[&crate::mob::cmd::create_item_link(id), &id.nameid])); // %s: %u
    }
    out.say(&format_msg(msg_txt(79), &[&items.len()])); // It is %d affair above.
    0
}

/// item_data->script != nullptr: the item's `Script` (setitemscript's change included) parses to code. parse_script
/// (SCRIPT_IGNORE_EXTERNAL_BRACKETS) gives nullptr for a script that is only blanks and comments (skip_space); a
/// script that does not compile is not detected here (the item database's scripts all compile).
fn has_use_script(nameid: u32) -> bool {
    let Some(text) = crate::db::items::item_scripts(nameid).and_then(|s| s.script.as_ref()) else { return false };
    let b = latin1(text);
    crate::npc::skip_space(&b, 0) < b.len()
}

/// ACMD_FUNC(iteminfo): @ii / @iteminfo <item name/ID>: type, script, prices, weight, drop chance.
fn iteminfo(c: &Ctx, out: &Out, message: &str) -> i32 {
    use crate::db::items::{itemdb_exists, itemdb_searchname_array, itemdb_typename, itemdb_typename_ammo, IT_AMMO};
    if message.is_empty() {
        out.say(msg_txt(1276)); // Please enter an item name/ID (usage: @ii/@iteminfo <item name/ID>).
        return -1;
    }
    // t_itemid itemid = strtoul(message, nullptr, 10)
    let itemid = super::c_strtoul(message) as u32;
    let items = if itemid == 0 {
        // Entered a string
        itemdb_searchname_array(message, MAX_SEARCH)
    } else {
        itemdb_exists(itemid).into_iter().collect()
    };
    if items.is_empty() {
        out.say(msg_txt(19)); // Invalid item ID or name.
        return -1;
    }
    if items.len() == MAX_SEARCH {
        out.say(&format_msg(msg_txt(269), &[&MAX_SEARCH])); // Displaying first %d matches
    }
    for it in items {
        let kind = if it.r#type != IT_AMMO { itemdb_typename(it.r#type) } else { itemdb_typename_ammo(it.subtype) };
        let effect = msg_txt(if has_use_script(it.nameid) { 1279 } else { 1278 }); // With script / None
        // Item: '%s'/'%s' (%u) Type: %s | Extra Effect: %s
        out.say(&format_msg(msg_txt(1277), &[&it.name, &crate::mob::cmd::create_item_link(it), &it.nameid, &kind, &effect]));
        // NPC Buy:%dz, Sell:%dz | Weight: %.1f
        out.say(&format_msg(msg_txt(1280), &[&(it.value_buy as i32), &(it.value_sell as i32), &c_fixed(it.weight as f64 / 10.0, 1)]));
        let maxchance = crate::npc::item_maxchance(c, it.nameid);
        if maxchance == -1 {
            out.say(msg_txt(1281)); //  - Available in the shops only.
        } else if crate::mob::cmd::atcommand_mobinfo_type() == 0 {
            if maxchance != 0 {
                //  - Maximal monsters drop chance: %02.02f%%
                out.say(&format_msg(msg_txt(1282), &[&c_fixed((maxchance as f32 / 100.0) as f64, 2)]));
            } else {
                out.say(msg_txt(1283)); //  - Monsters don't drop this item.
            }
        }
    }
    0
}

/// ACMD_FUNC(skillid): the skills whose AEGIS or English name starts with the text (every one), then up to
/// MAX_SKILLID_PARTIAL_RESULTS that contain it, in skill_db's iteration order.
fn skillid(out: &Out, message: &str) -> i32 {
    const MAX_SKILLID_PARTIAL_RESULTS: usize = 5;
    const MAX_SKILLID_PARTIAL_RESULTS_LEN: usize = 74;
    if message.is_empty() {
        out.say(msg_txt(1163)); // Please enter a skill name to look up (usage: @skillid <skill name>).
        return -1;
    }
    let skillen = latin1(message).len();
    let mut partials: Vec<String> = Vec::new();
    for skill in crate::atcommand_atcmd::admin::skill_db_iter(out.c) {
        let (name, desc) = (skill.name.as_str(), skill.desc.as_str());
        let line = || format_msg(msg_txt(1164), &[&skill.nameid, &desc, &name]); // skill %d: %s (%s)
        if strnicmp_eq(name, message, skillen) || strnicmp_eq(desc, message, skillen) {
            out.say(&cut(&line(), CHAT_SIZE_MAX));
        } else if partials.len() < MAX_SKILLID_PARTIAL_RESULTS && (stristr(name, message) || stristr(desc, message)) {
            // snprintf(partials[found++], MAX_SKILLID_PARTIAL_RESULTS_LEN, …)
            partials.push(cut(&line(), MAX_SKILLID_PARTIAL_RESULTS_LEN));
        }
    }
    if !partials.is_empty() {
        out.say(&format_msg(msg_txt(1398), &[&partials.len()])); // -- Displaying first %d partial matches
    }
    for p in partials {
        out.say(&p);
    }
    0
}

// ---------------------------------------------------------------------------------------------- @itemlist

/// common/utils.cpp MakeDWord(word0, word1) of two card values taken as uint16.
fn make_dword(w0: u32, w1: u32) -> u32 {
    (w0 as u16 as u32) | ((w1 as u16 as u32) << 16)
}

/// ACMD_FUNC(itemlist): #itemlist / #storagelist / #cartlist: the target's inventory, storage or cart, with the
/// equipped positions and the cards / creator of each item.
fn itemlist(c: &Ctx, out: &Out, sd: u32, command: &str) -> i32 {
    use crate::db::items::{itemdb_exists, CARD0_CREATE, CARD0_FORGE, CARD0_PET};
    use crate::inventory::{
        Item, EQP_ACC_L, EQP_ACC_R, EQP_AMMO, EQP_ARMOR, EQP_ARMS, EQP_COSTUME_GARMENT, EQP_COSTUME_HEAD_LOW, EQP_COSTUME_HEAD_MID, EQP_COSTUME_HEAD_TOP,
        EQP_GARMENT, EQP_HAND_L, EQP_HAND_R, EQP_HEAD_LOW, EQP_HEAD_MID, EQP_HEAD_TOP, EQP_HELM, EQP_SHADOW_ACC_L, EQP_SHADOW_ACC_R, EQP_SHADOW_ARMOR,
        EQP_SHADOW_ARMS, EQP_SHADOW_SHIELD, EQP_SHADOW_SHOES, EQP_SHADOW_WEAPON, EQP_SHOES, MAX_CART, MAX_INVENTORY,
    };
    let Some(ch) = crate::pc::character_of(c, sd) else { return -1 };
    let parent_cmd = data::atcommands().check_alias(command.get(1..).unwrap_or(""));
    let (location, items): (&str, Vec<Item>) = match parent_cmd {
        "storagelist" => {
            // sd->storage.u.items_storage[0 .. sd->storage.max_amount]
            let size = crate::inventory::st(c, sd).map(|s| s.storage_max).unwrap_or(0);
            ("storage", crate::storage::storage_list(c, sd).into_iter().filter(|r| r.idx < size).map(|r| r.item).collect())
        }
        "cartlist" => ("cart", crate::cart::cart_list(c, sd).into_iter().filter(|r| (r.idx as usize) < MAX_CART).map(|r| r.item).collect()),
        "itemlist" => ("inventory", crate::inventory::inv_list(c, sd).into_iter().filter(|r| (r.idx as usize) < MAX_INVENTORY).map(|r| r.item).collect()),
        _ => return 1,
    };
    let mut count = 0; // total slots occupied
    let mut counter = 0i32; // total items found
    for it in &items {
        if it.nameid == 0 {
            continue;
        }
        let Some(itd) = itemdb_exists(it.nameid) else { continue };
        counter += it.amount as i32;
        count += 1;
        if count == 1 {
            out.say(&format_msg(msg_txt(1332), &[&location, &ch.name])); // ------ %s items list of '%s' ------
        }
        let link = from_latin1(&crate::script::buildins::script2::ui::create_item_link(it, Some(itd)));
        let mut buf = format!("{} {} ({}, id: {})", it.amount, link, itd.name, it.nameid);
        if it.equip != 0 {
            let e = it.equip;
            let mut equipstr = latin1(msg_txt(1333)); // | Equipped:
            let mut add = |n: u16| equipstr.extend_from_slice(&latin1(msg_txt(n)));
            if e & EQP_GARMENT != 0 {
                add(1334); // Garment,
            }
            if e & EQP_ACC_L != 0 {
                add(1335); // Left Accessory,
            }
            if e & EQP_ARMOR != 0 {
                add(1336); // Body/Armor,
            }
            if e & EQP_ARMS == EQP_HAND_R {
                add(1337); // Right Hand,
            }
            if e & EQP_ARMS == EQP_HAND_L {
                add(1338); // Left Hand,
            }
            if e & EQP_ARMS == EQP_ARMS {
                add(1339); // Both Hands,
            }
            if e & EQP_SHOES != 0 {
                add(1340); // Feet,
            }
            if e & EQP_ACC_R != 0 {
                add(1341); // Right Accessory,
            }
            if e & EQP_HELM == EQP_HEAD_LOW {
                add(1342); // Lower Head,
            }
            if e & EQP_HELM == EQP_HEAD_TOP {
                add(1343); // Top Head,
            }
            if e & EQP_HELM == EQP_HEAD_LOW | EQP_HEAD_TOP {
                add(1344); // Top/Lower Head,
            }
            if e & EQP_HELM == EQP_HEAD_MID {
                add(1345); // Mid Head,
            }
            if e & EQP_HELM == EQP_HEAD_LOW | EQP_HEAD_MID {
                add(1346); // Mid/Lower Head,
            }
            if e & EQP_HELM == EQP_HELM {
                add(1347); // Top/Mid/Lower Head,
            }
            // pc.hpp EQP_COSTUME_HELM
            const EQP_COSTUME_HELM: u32 = EQP_COSTUME_HEAD_TOP | EQP_COSTUME_HEAD_MID | EQP_COSTUME_HEAD_LOW;
            if e & EQP_COSTUME_HELM == EQP_COSTUME_HEAD_LOW {
                add(518); // Lower Costume Head,
            }
            if e & EQP_COSTUME_HELM == EQP_COSTUME_HEAD_TOP {
                add(519); // Top Costume Head,
            }
            if e & EQP_COSTUME_HELM == EQP_COSTUME_HEAD_LOW | EQP_COSTUME_HEAD_TOP {
                add(520); // Top/Lower Costume Head,
            }
            if e & EQP_COSTUME_HELM == EQP_COSTUME_HEAD_MID {
                add(521); // Mid Costume Head,
            }
            if e & EQP_COSTUME_HELM == EQP_COSTUME_HEAD_LOW | EQP_COSTUME_HEAD_MID {
                add(522); // Mid/Lower Costume Head,
            }
            if e & EQP_COSTUME_HELM == EQP_COSTUME_HELM {
                add(523); // Top/Mid/Lower Costume Head,
            }
            if e & EQP_COSTUME_GARMENT != 0 {
                add(524); // Costume Robe,
            }
            // (EQP_COSTUME_FLOOR, msg 525: commented out in rAthena)
            if e & EQP_AMMO != 0 {
                add(526); // Ammo,
            }
            if e & EQP_SHADOW_ARMOR != 0 {
                add(527); // Shadow Body,
            }
            if e & EQP_SHADOW_ARMS == EQP_SHADOW_WEAPON {
                add(528); // Shadow Right Hand,
            }
            if e & EQP_SHADOW_ARMS == EQP_SHADOW_SHIELD {
                add(529); // Shadow Left Hand,
            }
            if e & EQP_SHADOW_ARMS == EQP_SHADOW_ARMS {
                add(530); // Shadow Both Hands,
            }
            if e & EQP_SHADOW_SHOES != 0 {
                add(531); // Shadow Shoes,
            }
            if e & EQP_SHADOW_ACC_R != 0 {
                add(532); // Shadow Right Accessory,
            }
            if e & EQP_SHADOW_ACC_L != 0 {
                add(533); // Shadow Left Accessory,
            }
            // remove final ', ' (equipstr[strlen(equipstr) - 2] = '\0'; rAthena quirk kept: msgs 1334-1347 end in ","
            // without a space, so the last letter of their word goes with the comma)
            let n = equipstr.len().saturating_sub(2);
            equipstr.truncate(n);
            buf.push_str(&from_latin1(&equipstr));
        }
        out.say(&buf);
        let card = |i: usize| it.card(i);
        let mut buf = String::new();
        if card(0) == CARD0_PET {
            // pet egg:  -> (pet egg, pet id: %u, named) / (…, unnamed)
            let n = if card(3) & 1 != 0 { 1348 } else { 1349 };
            buf.push_str(&format_msg(msg_txt(n), &[&make_dword(card(1), card(2))]));
        } else if card(0) == CARD0_FORGE {
            // forged item:  -> (crafted item, creator id: %u, star crumbs %d, element %d)
            buf.push_str(&format_msg(msg_txt(1350), &[&make_dword(card(2), card(3)), &((card(1) >> 8) as i32), &((card(1) & 0x0f) as i32)]));
        } else if card(0) == CARD0_CREATE {
            // created item:  -> (produced item, creator id: %u)
            buf.push_str(&format_msg(msg_txt(1351), &[&make_dword(card(2), card(3))]));
        } else {
            // normal item
            let mut counter2 = 0;
            for j in 0..itd.slots as usize {
                if card(j) == 0 {
                    continue;
                }
                let Some(cd) = itemdb_exists(card(j)) else { continue };
                counter2 += 1;
                if counter2 == 1 {
                    buf.push_str(msg_txt(1352)); //  -> (card(s):
                }
                if counter2 != 1 {
                    buf.push_str(", ");
                }
                buf.push_str(&format!("#{} {} (id: {})", counter2, crate::mob::cmd::create_item_link(cd), cd.nameid));
            }
            if counter2 > 0 {
                buf.push(')');
            }
        }
        if !buf.is_empty() {
            out.say(&buf);
        }
    }
    if count == 0 {
        out.say(&format_msg(msg_txt(1353), &[&location])); // No item found in this player's %s.
    } else {
        out.say(&format_msg(msg_txt(1354), &[&counter, &count, &location])); // %d item(s) found in %d %s slots.
    }
    0
}

// ---------------------------------------------------------------------------------------------- @gmotd, @request, @partyspy

/// ACMD_FUNC(gmotd): every fgets piece of conf/motd.txt (CHAT_SIZE_MAX - 1 bytes at most, EOL stripped: conf.rs
/// motd_file, cut by tools/datagen.py) but `//` comments and empty ones, broadcast to every player
/// (intif_broadcast(buf, len + 1, BC_DEFAULT): clif_broadcast to ALL_CLIENT; no other map server to relay to).
fn gmotd(c: &Ctx) -> i32 {
    for line in crate::conf::motd_file() {
        if line.starts_with("//") || line.is_empty() {
            continue;
        }
        let b = latin1(line);
        crate::clif::broadcast(c, None, &b, b.len() + 1, 0, crate::clif::Target::AllClient);
    }
    0
}

/// ACMD_FUNC(request): the petition goes as a whisper from the player to everyone with PC_PERM_RECEIVE_REQUESTS
/// (intif_wis_message_to_gm → the char server's mapif_parse_WisToGM → every map server's mapif_parse_WisToGM:
/// map_foreachpc, pc_has_permission, clif_wis_message), and to the player in light green; the messages go to sd->fd
/// (for #request, the target). The whispers arrive after the command's own messages (the char server round trip),
/// so they are sent last here.
fn request(c: &Ctx, sd: u32, message: &str) -> i32 {
    if message.is_empty() {
        say_sd(c, sd, msg_txt(277)); // Usage: @request <petition/message to online GMs>.
        return -1;
    }
    let Some(ch) = crate::pc::character_of(c, sd) else { return -1 };
    // sprintf(atcmd_output, msg_txt(278), message): atcmd_output holds CHAT_SIZE_MAX bytes (rAthena overflows it for
    // a message near 255 characters; cut here)
    let text = cut(&format_msg(msg_txt(278), &[&message]), CHAT_SIZE_MAX); // (@request): %s
    if let Some(u) = crate::unit::get(c, sd) {
        // clif_messagecolor(sd, color_table[COLOR_LIGHT_GREEN], atcmd_output, false, SELF)
        crate::clif::messagecolor_target(c, &u, crate::atcmd_pc::COLOR_LIGHT_GREEN, &latin1(&text), false, crate::clif::Target::SelfOnly, None);
    }
    say_sd(c, sd, msg_txt(279)); // @request sent.
    // mapif_parse_WisToGM: safestrncpy(Wisp_name, name, NAME_LENGTH); clif_wis_message's map_nick2sd(nick, false)
    // decides senderGID and the admin crown
    let wisp_name = cut(&ch.name, NAME_LENGTH);
    let sender = crate::pc::nick2sd(c, &wisp_name, false);
    for pl in all_users(c) {
        if !crate::pc_misc::pc_has_permission(c, pl.account_id, crate::rules::PC_PERM_RECEIVE_REQUESTS) {
            continue;
        }
        if let Some(u) = crate::unit::get(c, pl.account_id) {
            crate::clif::wis_message(c, &u, &wisp_name, sender.as_ref(), &text, 0);
        }
    }
    0
}

/// ACMD_FUNC(partyspy): conf/map_athena.conf `enable_spy: no`, so the command only answers that spying is off.
/// With enable_spy on, rAthena reads "%23[^\n]" (msg 1127 when missing), finds the party by name
/// (party_searchname) or by id (party_search(atoi)) (msg 96 when neither), and toggles sd->partyspy between 0 and
/// the party id (msgs 105 / 106), which clif_send's PARTY branch reads to copy party packets to the spy: that field
/// (map_session_data::partyspy) and that branch of clif_send are not in the module while the switch is off.
fn partyspy(out: &Out, message: &str) -> i32 {
    let _ = message;
    if !ENABLE_SPY {
        out.say(msg_txt(1125)); // The mapserver has spy command support disabled.
        return -1;
    }
    -1
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn fixed_point_rounds_like_glibc() {
        assert_eq!(c_fixed(5.0, 2), "5.00");
        assert_eq!(c_fixed(0.0, 3), "0.000");
        assert_eq!(c_fixed(100.0 / 3.0, 3), "33.333");
        assert_eq!(c_fixed(0.125, 2), "0.12"); // exact tie: half to even
        assert_eq!(c_fixed(0.375, 2), "0.38");
        assert_eq!(c_fixed(9.9996, 3), "10.000");
        assert_eq!(c_fixed(1.5, 1), "1.5");
        assert_eq!(c_fixed(0.05, 1), "0.1"); // 0.05 is a little above one half of 0.1
    }

    #[test]
    fn map_addnpc_groups_trigger_areas() {
        use crate::npc::{NPCTYPE_SCRIPT, NPCTYPE_SHOP, NPCTYPE_WARP};
        // shop 1, script with area 2, warp 3, plain script 4, warp 5
        let order = addnpc_order(&[(1, NPCTYPE_SHOP, false), (2, NPCTYPE_SCRIPT, true), (3, NPCTYPE_WARP, true), (4, NPCTYPE_SCRIPT, false), (5, NPCTYPE_WARP, true)]);
        // 1 → [1]; 2 → [2,1]; 3 → [3,2,1]; 4 → [3,2,1,4]; 5 → npc[4]=npc[2]=1, npc[2]=npc[1]=2, npc[1]=5 → [3,5,2,4,1]
        assert_eq!(order, vec![3, 5, 2, 4, 1]);
    }

    #[test]
    fn command_columns_pad_to_tens() {
        let mut lines = Vec::new();
        let mut say = |s: String| lines.push(s);
        let mut cols = Columns::new();
        for n in ["warp", "jumpto", "who", "abcdefghij", "go", "x"] {
            cols.add(n.as_bytes(), &mut say);
        }
        say(cols.text());
        // "abcdefghij" (10) moves the next name 20 on; "x" at 60 still fits (1 + 60 < 71)
        assert_eq!(lines.len(), 1);
        assert_eq!(lines[0].len(), 70);
        assert!(lines[0].starts_with("warp      jumpto    who       abcdefghij          go        x"));
        // a name that does not fit flushes the line first
        let mut lines = Vec::new();
        let mut say = |s: String| lines.push(s);
        let mut cols = Columns::new();
        for _ in 0..7 {
            cols.add(b"abcdefgh", &mut say);
        }
        cols.add(b"z", &mut say);
        say(cols.text());
        assert_eq!(lines.len(), 2);
        assert!(lines[1].starts_with("z "));
    }

    #[test]
    fn txt_time_keeps_the_static_buffer_quirk() {
        // 1 hour 5 minutes 0 seconds: the result is the hours piece (temp1), the minutes are lost
        let s = txt_time(3600 + 300);
        assert!(s.contains("1 hour"));
        assert!(!s.contains("minute"));
        let s = txt_time(3600 + 300 + 2);
        assert!(s.contains("5 minutes") && s.contains("2 seconds"));
    }

    #[test]
    fn atcommand_db_holds_every_command_once() {
        let order = atcommand_db_order();
        assert_eq!(order.len(), data::atcommands().commands.len());
        let mut seen = std::collections::HashSet::new();
        assert!(order.iter().all(|n| seen.insert(n.to_ascii_lowercase())));
        assert!(order.iter().any(|n| n == "lokirnd"));
    }

    #[test]
    fn case_insensitive_helpers() {
        assert!(strnicmp_eq("SM_BASH", "sm_b", 4));
        assert!(!strnicmp_eq("SM", "sm_b", 4));
        assert!(stristr("Bash", "AS"));
        assert!(!stristr("Bash", "x"));
    }
}
