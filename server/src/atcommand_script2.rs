//! atcommand.cpp: script2's thin-wrapper @commands — @save, @load, @memo, @users, @me, @kami / @kamib / @kamic /
//! @lkami, @broadcast, @localbroadcast, @npctalk / @npctalkc, @npcmove, @effect, @sound, @hide, @option, @speed,
//! @kick, @mute, @unmute, @night, @day, @follow. Routed from atcommand.rs `run` by their ACMD_FUNC name.

use crate::character::character as _;
use crate::clif::{self, Target};
use spacetimedb::Table;
use crate::cstr::Scan;
use crate::data::{self, msg_txt};
use crate::pc;
use crate::pc_misc;
use crate::unit::{self, Unit};
use crate::world::Ctx;

/// Replies go to the invoker (rAthena's fd).
struct Out<'a> {
    c: &'a Ctx<'a>,
    fd: Option<&'a Unit>,
}

impl Out<'_> {
    fn say(&self, text: &str) {
        if let Some(u) = self.fd {
            clif::displaymessage(self.c, u, text);
        }
    }
}

/// Runs `func` for script2's commands; None when `func` is not one of them.
pub fn run(c: &Ctx, fd: Option<&Unit>, sd: u32, func: &str, command: &str, message: &str) -> Option<i32> {
    let out = Out { c, fd };
    Some(match func {
        "save" => save(c, &out, sd),
        "load" => load(c, &out, sd),
        "memo" => memo(c, &out, sd, message),
        "users" => users(c, &out),
        "me" => me(c, &out, sd, message),
        "kami" => kami(c, &out, sd, message, 0, false),
        "kamib" => kami(c, &out, sd, message, 0x10, false),
        "lkami" => kami(c, &out, sd, message, 0, true),
        "kamic" => kamic(c, &out, message),
        "broadcast" => broadcast(c, &out, sd, message, false),
        "localbroadcast" => broadcast(c, &out, sd, message, true),
        "npctalk" => npctalk(c, &out, sd, command, message),
        "npcmove" => npcmove(c, &out, message),
        "effect" => effect(c, &out, sd, message),
        "sound" => sound(c, &out, sd, message),
        "hide" => hide(c, &out, sd),
        "option" => option(c, &out, sd, command, message),
        "speed" => speed(c, &out, sd, message),
        "kick" => kick(c, &out, sd, message),
        "mute" => mute(c, &out, sd, message),
        "unmute" => unmute(c, &out, sd, message),
        "night" => night(c, &out),
        "day" => day(c, &out),
        "follow" => follow(c, &out, sd, message),
        "recallall" => recallall(c, &out, sd),
        "mapflag" => mapflag(c, &out, sd, message),
        _ => return None,
    })
}

fn bytes(s: &str) -> Vec<u8> {
    crate::net::wire::latin1(s)
}

/// `%<width>lx`: leading whitespace, an optional 0x, hex digits; (value, bytes consumed).
fn scan_hex(b: &[u8], width: usize) -> Option<(u64, usize)> {
    let mut p = 0;
    while p < b.len() && crate::cstr::is_space(b[p]) {
        p += 1;
    }
    let st = p;
    let mut n = 0;
    if p + 1 < b.len() && b[p] == b'0' && (b[p + 1] == b'x' || b[p + 1] == b'X') && width >= 2 {
        p += 2;
        n += 2;
    }
    let ds = p;
    let mut v: u64 = 0;
    while p < b.len() && b[p].is_ascii_hexdigit() && n < width {
        v = v.wrapping_mul(16).wrapping_add((b[p] as char).to_digit(16).unwrap() as u64);
        p += 1;
        n += 1;
    }
    if p == ds {
        // "0x" without digits: the 0 alone was the number
        if ds > st {
            return Some((0, st + 1));
        }
        return None;
    }
    Some((v, p))
}

/// sscanf(message, "%255[^\n]", out) with rAthena's buffer: None for an empty message.
fn line(message: &str, width: usize) -> Option<String> {
    Scan::new(&bytes(message)).set(width, b"\n")
}

fn my_name(c: &Ctx, aid: u32) -> String {
    unit::get(c, aid).map(|u| u.name).unwrap_or_default()
}

fn group_level(c: &Ctx, aid: u32) -> i32 {
    pc::group_level(c, aid)
}

// ---------------------------------------------------------------------------------------------- save point, memo

/// @save: the save point here (msg 6).
fn save(c: &Ctx, out: &Out, sd: u32) -> i32 {
    let Some(u) = unit::get(c, sd) else { return -1 };
    if crate::instance_map::map_instance_id(c, u.map) != 0 {
        out.say(msg_txt(383)); // You cannot create a savepoint in an instance.
        return 1;
    }
    pc_misc::pc_setsavepoint(c, sd, u.map, u.x as i32, u.y as i32);
    // if (sd->status.pet_id > 0 && sd->pd) intif_save_petdata(...): the pet is saved here and again in chrif_save
    crate::pet::chrif_save_pet(c, sd);
    crate::chrif::chrif_save(c, sd, crate::chrif::CSAVE_NORMAL);
    out.say(msg_txt(6)); // Your save point has been changed.
    0
}

/// @load: warps to the save point (msg 7), unless the save map is nowarpto or this map nowarp.
fn load(c: &Ctx, out: &Out, sd: u32) -> i32 {
    use crate::npc::{mapflag, MF_NOWARP, MF_NOWARPTO};
    let Some(ch) = pc::character_of(c, sd) else { return -1 };
    let Some(u) = unit::get(c, sd) else { return -1 };
    let m = data::maps().id_of(&ch.save_map);
    let warp_anywhere = pc_misc::pc_has_permission(c, sd, crate::rules::PC_PERM_WARP_ANYWHERE);
    if m.map(|m| mapflag(c, m, MF_NOWARPTO)).unwrap_or(false) && !warp_anywhere {
        out.say(msg_txt(249)); // You are not authorized to warp to your save map.
        return -1;
    }
    if mapflag(c, u.map, MF_NOWARP) && !warp_anywhere {
        out.say(msg_txt(248)); // You are not authorized to warp from your current map.
        return -1;
    }
    match m {
        Some(m) => {
            pc::setpos(c, sd, m, ch.save_x as i16, ch.save_y as i16, unit::CLR_OUTSIGHT);
        }
        None => log::error!("atcommand_load: save map '{}' not found", ch.save_map),
    }
    out.say(msg_txt(7)); // Warping to save point..
    0
}

/// @memo {<position>}: lists the memo points, or memorizes this map at the position (pc_memo).
fn memo(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let b = bytes(message);
    let position = Scan::new(&b).int(11);
    let Some(position) = position.filter(|_| !message.is_empty()) else {
        out.say(msg_txt(668)); // Your actual memo positions are:
        let cid = pc::get(c, sd).map(|p| p.char_id).unwrap_or(0);
        for (i, p) in pc_misc::memo_points(c, cid).iter().enumerate() {
            if !p.map.is_empty() {
                out.say(&format!("{} - {} ({},{})", i, p.map, p.x, p.y));
            } else {
                out.say(&data::format_msg(msg_txt(171), &[&i])); // %d - void
            }
        }
        return 0;
    };
    let position = position as i32;
    if position < 0 || position >= pc_misc::MAX_MEMOPOINTS as i32 {
        out.say(&data::format_msg(msg_txt(1008), &[&0, &(pc_misc::MAX_MEMOPOINTS - 1)])); // Please enter a valid position
        return -1;
    }
    (!pc_misc::pc_memo(c, sd, position)) as i32
}

// ---------------------------------------------------------------------------------------------- information

/// @users: the players per map (mapindex order) with their share, then the total.
fn users(c: &Ctx, out: &Out) -> i32 {
    use crate::pc::pc as _;
    // (users[] is indexed by mapindex (db/map_index.txt order); the module has the map ids only, so the maps come in
    // map id order — the same with every player on one map)
    let mut per_map: std::collections::BTreeMap<u16, i32> = std::collections::BTreeMap::new();
    let mut users_all = 0i32;
    // mapit_getallusers: every session
    for p in c.db().pc().iter() {
        let Some(u) = unit::get(c, p.account_id) else { continue };
        *per_map.entry(u.map).or_insert(0) += 1;
        users_all += 1;
    }
    for (m, n) in per_map {
        let name = data::maps().get(m).map(|d| d.name.clone()).unwrap_or_default();
        let pct = 100.0f32 * n as f32 / users_all as f32;
        out.say(&format!("{name}: {n} ({pct:.2}%)"));
    }
    out.say(&format!("all: {users_all}"));
    0
}

// ---------------------------------------------------------------------------------------------- messages

/// sc->cant.chat || block_action & PCBLOCK_CHAT: no "chatting" while muted.
fn cant_chat(c: &Ctx, sd: u32) -> bool {
    crate::status::sc::cant(c, sd, crate::status::consts::SCS_NOCHAT) || pc::get(c, sd).map(|p| p.block_action & pc::PCBLOCK_CHAT != 0).unwrap_or(false)
}

/// @me <message>: "*<name> <message>*" over the head (clif_disp_overhead).
fn me(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    if cant_chat(c, sd) {
        return -1; // no "chatting" while muted.
    }
    if message.is_empty() {
        out.say(msg_txt(1302)); // Please enter a message (usage: @me <message>).
        return -1;
    }
    let tempmes = line(message, 255).unwrap_or_default();
    let text = data::format_msg(msg_txt(270), &[&my_name(c, sd), &tempmes]); // *%s %s*
    if let Some(u) = unit::get(c, sd) {
        crate::clif_misc::disp_overhead(c, &u, &bytes(&text), Target::Area);
    }
    0
}

/// @kami / @kamib (blue) <message>: intif_broadcast to everyone; @lkami: clif_broadcast to this map.
fn kami(c: &Ctx, out: &Out, sd: u32, message: &str, kind: i32, local: bool) -> i32 {
    if message.is_empty() {
        out.say(msg_txt(980)); // Please enter a message (usage: @kami <message>).
        return -1;
    }
    let text = bytes(&line(message, 255).unwrap_or_default());
    if local {
        if let Some(u) = unit::get(c, sd) {
            clif::broadcast(c, Some(&u), &text, text.len() + 1, kind, Target::AllSamemap);
        }
    } else {
        clif::broadcast(c, None, &text, text.len() + 1, kind, Target::AllClient);
    }
    0
}

/// @kamic <color> <message>: intif_broadcast2 in the color (font 0x190, size 12).
fn kamic(c: &Ctx, out: &Out, message: &str) -> i32 {
    let b = bytes(message);
    let hx = scan_hex(&b, 20);
    let color = hx.map(|(v, _)| v);
    let text = hx.and_then(|(_, used)| {
        let mut s = Scan::new(&b[used..]);
        s.ws();
        s.set(199, b"\n")
    });
    let (Some(color), Some(text)) = (color, text) else {
        out.say(msg_txt(981)); // Please enter color and message (usage: @kamic <color> <message>).
        return -1;
    };
    if color > 0xFFFFFF {
        out.say(msg_txt(982)); // Invalid color.
        return -1;
    }
    let t = bytes(&text);
    clif::broadcast2(c, None, &t, t.len() + 1, color as u32, 0x190, 12, 0, 0, Target::AllClient);
    0
}

/// @broadcast / @localbroadcast <message>: "<name>: <message>" to everyone / this map.
fn broadcast(c: &Ctx, out: &Out, sd: u32, message: &str, local: bool) -> i32 {
    if message.is_empty() {
        out.say(msg_txt(if local { 1150 } else { 1149 })); // Please enter a message (usage: @broadcast <message>).
        return -1;
    }
    let text = bytes(&format!("{}: {}", my_name(c, sd), message));
    if local {
        if let Some(u) = unit::get(c, sd) {
            clif::broadcast(c, Some(&u), &text, text.len() + 1, 0, Target::AllSamemap);
        }
    } else {
        clif::broadcast(c, None, &text, text.len() + 1, 0, Target::AllClient);
    }
    0
}

/// @npctalk <NPC name>, <message> / @npctalkc <color> <NPC name>, <message>: the NPC says it (over its head, or in
/// the color to the area).
fn npctalk(c: &Ctx, out: &Out, sd: u32, command: &str, message: &str) -> i32 {
    // bool ifcolor = (*(command + 8) != 'c' && *(command + 8) != 'C') ? 0 : 1;
    let ifcolor = matches!(command.as_bytes().get(8), Some(b'c') | Some(b'C'));
    if cant_chat(c, sd) {
        return -1; // no "chatting" while muted.
    }
    let b = bytes(message);
    let mut color = 0u64;
    let name_mes = |rest: &[u8], w: usize| -> Option<(String, String)> {
        let mut s = Scan::new(rest);
        s.ws();
        let name = s.set(w, b",")?;
        if !s.lit(b',') {
            return None;
        }
        s.ws();
        let m = s.set(99, b"\n")?;
        Some((name, m))
    };
    let parsed = if !ifcolor {
        // "%49[^,], %99[^\n]"
        let mut s = Scan::new(&b);
        s.set(49, b",").and_then(|name| if s.lit(b',') { s.ws(); s.set(99, b"\n").map(|m| (name, m)) } else { None })
    } else {
        // "%16lx %23[^,], %99[^\n]"
        scan_hex(&b, 16).and_then(|(col, used)| {
            color = col;
            name_mes(&b[used..], 23)
        })
    };
    let Some((name, mes)) = parsed.filter(|_| !message.is_empty()) else {
        out.say(msg_txt(if ifcolor { 1223 } else { 1222 })); // Please enter the correct parameters
        return -1;
    };
    let Some(nd) = crate::script::npc_run::npc_name2id(c, &name) else {
        out.say(msg_txt(111)); // This NPC doesn't exist
        return -1;
    };
    // strtok(name, "#"): discard extra name identifier if present
    let shown = name.split('#').next().unwrap_or("").to_string();
    let temp = bytes(&format!("{shown} : {mes}"));
    if let Some(u) = unit::get(c, nd.id) {
        if ifcolor {
            clif::messagecolor_target(c, &u, color as u32, &temp, true, Target::AreaChatWoc, None);
        } else {
            crate::clif_misc::disp_overhead(c, &u, &temp, Target::Area);
        }
    }
    0
}

/// @npcmove <x> <y> <NPC name>: npc_movenpc (a move answers "NPC is not on this map." and fails, an NPC off the
/// map "NPC moved": rAthena tests npc_movenpc's true as a failure).
fn npcmove(c: &Ctx, out: &Out, message: &str) -> i32 {
    let b = bytes(message);
    let mut s = Scan::new(&b);
    let x = s.int(6);
    let y = x.and_then(|_| s.int(6));
    let name = y.and_then(|_| {
        s.ws();
        s.set(49, b"\n")
    });
    let (Some(x), Some(y), Some(name)) = (x, y, name) else {
        out.say(msg_txt(1153)); // Usage: @npcmove <X> <Y> <npc_name>
        return -1;
    };
    let Some(nd) = crate::script::npc_run::npc_name2id(c, &name) else {
        out.say(msg_txt(111)); // This NPC doesn't exist.
        return -1;
    };
    // rAthena quirk kept: npc_movenpc returns true when it moved the NPC, and this tests it as a failure
    if crate::script::buildins::npcctl::npc_movenpc(c, &nd, x as i16, y as i16) {
        //actually failed to move
        out.say(msg_txt(1154)); // NPC is not on this map.
        return -1; // Not on a map.
    }
    out.say(msg_txt(1155)); // NPC moved
    0
}

/// @effect <effect id>: clif_specialeffect to everyone (msg 229).
fn effect(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    const EF_NONE: i64 = -1;
    const EF_MAX: i64 = 1243;
    let kind = Scan::new(&bytes(message)).int(11);
    let Some(kind) = kind.filter(|_| !message.is_empty()) else {
        out.say(msg_txt(1152)); // Please enter an effect number (usage: @effect <effect number>).
        return -1;
    };
    if kind <= EF_NONE || kind >= EF_MAX {
        out.say(&data::format_msg(msg_txt(1152), &[&(EF_NONE + 1), &(EF_MAX - 1)]));
        return -1;
    }
    if let Some(u) = unit::get(c, sd) {
        clif::specialeffect(c, &u, kind as i32, Target::AllClient);
    }
    out.say(msg_txt(229)); // Your effect has changed.
    0
}

/// @sound <file>: clif_soundeffect (".wav" added when missing) to the area.
fn sound(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let Some(mut file) = line(message, 99).filter(|_| !message.is_empty()) else {
        out.say(msg_txt(1217)); // Please enter a sound filename (usage: @sound <filename>).
        return -1;
    };
    if !file.contains(".wav") {
        file.push_str(".wav");
    }
    if let Some(u) = unit::get(c, sd) {
        clif::soundeffect(c, &u, &bytes(&file), 0, Target::Area);
    }
    0
}

// ---------------------------------------------------------------------------------------------- the player's state

/// @hide: GM invisibility on / off (OPTION_INVISIBLE, the PvP ranking registration, clif_changeoption).
fn hide(c: &Ctx, out: &Out, sd: u32) -> i32 {
    use crate::status::defs::OPTION_INVISIBLE;
    let Some(mut u) = unit::get(c, sd) else { return -1 };
    if u.sc.option & OPTION_INVISIBLE != 0 {
        u.sc.option &= !OPTION_INVISIBLE;
        unit::save(c, &u);
        // status_set_viewdata(sd, sd->disguise ? sd->disguise : sd->status.class_)
        let disguise = crate::atcmd_disguise::disguise_of(c, sd);
        let class = if disguise != 0 { disguise } else { pc::character_of(c, sd).map(|ch| ch.class as i32).unwrap_or(0) };
        crate::skilltree::status_set_viewdata_pc(c, sd, class);
        out.say(msg_txt(10)); // Invisible: Off
        // (users_pvp is counted when asked: battle/pvp.rs)
        if crate::conf::pk_mode() == 0 && crate::npc::mapflag(c, u.map, crate::npc::MF_PVP) && !crate::npc::mapflag(c, u.map, crate::battle::target::MF_PVP_NOCALCRANK) {
            // register the player for ranking calculations
            let tid = crate::timer::add(c, c.tick() + 200, crate::timer::Func::PcCalcPvprank, sd, 0);
            crate::battle::state::with(c, sd, |b| b.pvp_timer = tid);
        }
        // bugreport:2266: map_foreachinmovearea(clif_insight, sd, AREA_SIZE, sd->x, sd->y, BL_ALL, sd)
        let Some(u) = unit::get(c, sd) else { return 0 };
        for other in crate::map::units_in_move_area(c, &u, crate::conf::area_size(), u.x, u.y, crate::map::BL_ALL) {
            clif::insight(c, &other, &u);
        }
    } else {
        u.sc.option |= OPTION_INVISIBLE;
        u.vd.class = crate::npc::JT_INVISIBLE;
        unit::save(c, &u);
        out.say(msg_txt(11)); // Invisible: On
        if crate::npc::mapflag(c, u.map, crate::npc::MF_PVP) && !crate::npc::mapflag(c, u.map, crate::battle::target::MF_PVP_NOCALCRANK) {
            // unregister the player for ranking
            let t = crate::battle::state::get(c, sd).pvp_timer;
            if t != 0 {
                crate::timer::delete(c, t);
                crate::battle::state::with(c, sd, |b| b.pvp_timer = 0);
            }
        }
    }
    if let Some(u) = unit::get(c, sd) {
        let option = u.sc.option;
        clif::changeoption(c, &u, option, None);
    }
    0
}

/// @option <opt1> {<opt2> {<option>}}: sets opt1 / opt2 and pc_setoption (msg 9).
fn option(c: &Ctx, out: &Out, sd: u32, command: &str, message: &str) -> i32 {
    let b = bytes(message);
    let mut s = Scan::new(&b);
    let p1 = s.int(11);
    let p2 = p1.and_then(|_| s.int(11)).unwrap_or(0);
    let p3 = p1.and_then(|_| s.int(11)).unwrap_or(0);
    if message.is_empty() || p1.is_none() || p1.unwrap() < 0 || p2 < 0 || p3 < 0 {
        out.say(msg_txt(921)); // Please enter at least one option.
        if let Some(text) = data::atcommands().help_string(command) {
            out.say(text);
        }
        return -1;
    }
    if let Some(mut u) = unit::get(c, sd) {
        u.sc.opt1 = p1.unwrap() as i16;
        u.sc.opt2 = p2 as i16;
        unit::save(c, &u);
    }
    crate::skilltree::pc_setoption(c, sd, p3 as i32, 0);
    out.say(msg_txt(9)); // Options changed.
    0
}

/// @speed <speed>: the walk speed kept over status calculations (sd->state.permanent_speed) until reset.
fn speed(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    const MIN_WALK_SPEED: i32 = 20;
    const MAX_WALK_SPEED: i32 = 1000;
    const DEFAULT_WALK_SPEED: i32 = 150;
    let speed = Scan::new(&bytes(message)).int(6);
    let Some(speed) = speed.filter(|_| !message.is_empty()) else {
        out.say(&data::format_msg(msg_txt(918), &[&MIN_WALK_SPEED, &MAX_WALK_SPEED])); // Please enter a speed value
        return -1;
    };
    let speed = speed as i16 as i32;
    let new = if speed < 0 { DEFAULT_WALK_SPEED } else { speed.clamp(MIN_WALK_SPEED, MAX_WALK_SPEED) };
    crate::status::data::with_sd(c, sd, |s| {
        s.permanent_speed = false; // Remove lock when set back to default speed.
        s.base_status.speed = new as u16;
        if new != DEFAULT_WALK_SPEED {
            s.permanent_speed = true; // Set lock when set to non-default speed.
        }
    });
    out.say(msg_txt(if new != DEFAULT_WALK_SPEED { 8 } else { 389 })); // Speed changed. / Speed returned to normal.
    crate::status::calc_bl(c, sd, 1u64 << crate::status::consts::SCB_SPEED, crate::status::defs::SCO_NONE);
    0
}

/// @kick <name|char id>: clif_GM_kick (msg 3 / 81).
fn kick(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let Some(name) = line(message, 23).filter(|_| !message.is_empty()) else {
        out.say(msg_txt(1026)); // Please enter a player name (usage: @kick <char name/ID>).
        return -1;
    };
    let pl = pc::nick2sd(c, &name, false).or_else(|| pc::charid2sd(c, crate::cstr::atoi(&name) as u32));
    let Some(pl) = pl else {
        out.say(msg_txt(3)); // Character not found.
        return -1;
    };
    if group_level(c, sd) < group_level(c, pl.id) {
        out.say(msg_txt(81)); // Your GM level don't authorise you to do this action on this player.
        return -1;
    }
    // clif_GM_kick(sd, pl_sd): SC_NOTIFY_BAN 15 + set_eof (or map_quit without a session), then clif_GM_kickack
    // (0x00cd with WFIFOB(fd,2) = id: "FIXME: this is not account id") — crate::session
    crate::session::clif_gm_kick(c, Some(sd), pl.id);
    0
}

/// clif.cpp clif_manner_message: ZC_ACK_GIVE_MANNER_POINT (0x014a) result.L.
fn clif_manner_message(c: &Ctx, aid: u32, kind: u32) {
    if let Some(u) = unit::get(c, aid) {
        let mut w = Vec::with_capacity(6);
        w.extend_from_slice(&0x014au16.to_le_bytes());
        w.extend_from_slice(&kind.to_le_bytes());
        clif::send(c, &w, &u, Target::SelfOnly);
    }
}

/// @mute <minutes> <name>: manner points down (SC_NOCHAT) or back to 0.
fn mute(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let b = bytes(message);
    let mut s = Scan::new(&b);
    let manner = s.int(11);
    let name = manner.and_then(|_| {
        s.ws();
        s.set(23, b"\n")
    });
    let Some(manner) = manner.filter(|_| !message.is_empty()) else {
        out.say(msg_txt(1237)); // Usage: @mute <time> <char name>
        return -1;
    };
    let manner = manner as i32;
    let Some(pl) = name.and_then(|n| pc::nick2sd(c, &n, false)) else {
        out.say(msg_txt(3)); // Character not found.
        return -1;
    };
    if group_level(c, sd) < group_level(c, pl.id) {
        out.say(msg_txt(81));
        return -1;
    }
    clif_manner_message(c, sd, 0);
    clif_manner_message(c, pl.id, 5);
    let Some(mut ch) = pc::character_of(c, pl.id) else { return -1 };
    if (ch.manner as i32) < manner {
        ch.manner = (ch.manner as i32 - manner) as i16;
        c.db().character().id().update(ch);
        if let Some(u) = unit::get(c, pl.id) {
            crate::status::sc::sc_start(c, None, &u, crate::status::consts::SC_NOCHAT, 100, 0, 0);
        }
    } else {
        ch.manner = 0;
        c.db().character().id().update(ch);
        crate::status::sc::status_change_end(c, pl.id, crate::status::consts::SC_NOCHAT, 0);
    }
    // clif_GM_silence(sd, pl_sd, manner > 0): ZC_NOTIFY_MANNER_POINT_GIVEN (0x014b) type.B GM name.24B
    let mut w = Vec::with_capacity(27);
    w.extend_from_slice(&0x014bu16.to_le_bytes());
    w.push((manner > 0) as u8);
    let mut n = bytes(&my_name(c, sd));
    n.truncate(23);
    n.resize(24, 0);
    w.extend_from_slice(&n);
    if let Some(u) = unit::get(c, pl.id) {
        clif::send(c, &w, &u, Target::SelfOnly);
    }
    0
}

/// @unmute <name>: ends SC_NOCHAT (manner 0).
fn unmute(c: &Ctx, out: &Out, _sd: u32, message: &str) -> i32 {
    let Some(name) = line(message, 23).filter(|_| !message.is_empty()) else {
        out.say(msg_txt(1234)); // Please enter a player name (usage: @unmute <char name>).
        return -1;
    };
    let Some(pl) = pc::nick2sd(c, &name, false) else {
        out.say(msg_txt(3)); // Character not found.
        return -1;
    };
    if !crate::status::sc::has_sce(c, pl.id, crate::status::consts::SC_NOCHAT) {
        out.say(msg_txt(1235)); // Player is not muted.
        return -1;
    }
    if let Some(mut ch) = pc::character_of(c, pl.id) {
        ch.manner = 0;
        c.db().character().id().update(ch);
    }
    crate::status::sc::status_change_end(c, pl.id, crate::status::consts::SC_NOCHAT, 0);
    out.say(msg_txt(1236)); // Player unmuted.
    0
}

/// @night / @day: map_night_timer / map_day_timer with data 1 (msg 89 / 90 when already so).
fn night(c: &Ctx, out: &Out) -> i32 {
    if pc_misc::night_flag(c) {
        out.say(msg_txt(89)); // Night mode is already enabled.
        return -1;
    }
    pc_misc::map_night_timer(c, 1);
    0
}

fn day(c: &Ctx, out: &Out) -> i32 {
    if !pc_misc::night_flag(c) {
        out.say(msg_txt(90)); // Day mode is already enabled.
        return -1;
    }
    pc_misc::map_day_timer(c, 1);
    0
}

/// @follow {<name>}: pc_follow the player (again: stop), no name: stop following.
fn follow(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    use crate::script::buildins::script2::unitmove::{pc_follow, pc_stop_following};
    let name = line(message, 23).filter(|_| !message.is_empty());
    let Some(name) = name else {
        if pc_misc::pcm(c, sd).followtarget == -1 {
            return -1;
        }
        pc_stop_following(c, sd);
        out.say(msg_txt(1159)); // Follow mode OFF.
        return 0;
    };
    let Some(pl) = pc::nick2sd(c, &name, true) else {
        out.say(msg_txt(3)); // Character not found.
        return -1;
    };
    if pc_misc::pcm(c, sd).followtarget == pl.id as i32 {
        pc_stop_following(c, sd);
        out.say(msg_txt(1159)); // Follow mode OFF.
    } else {
        pc_follow(c, sd, pl.id as i32);
        out.say(msg_txt(1160)); // Follow mode ON.
    }
    0
}

/// @recallall: every other player (of a group level not above ours, not already here) comes to our cell
/// (CLR_RESPAWN); players on nowarp maps stay and are counted.
fn recallall(c: &Ctx, out: &Out, sd: u32) -> i32 {
    use crate::npc::{mapflag, MF_NOWARP, MF_NOWARPTO};
    use crate::pc::pc as _;
    let Some(me) = unit::get(c, sd) else { return -1 };
    let warp_anywhere = pc_misc::pc_has_permission(c, sd, crate::rules::PC_PERM_WARP_ANYWHERE);
    if mapflag(c, me.map, MF_NOWARPTO) && !warp_anywhere {
        out.say(msg_txt(1032)); // You are not authorized to warp someone to your current map.
        return -1;
    }
    let mut count = 0;
    // mapit_getallusers
    let mut ids: Vec<u32> = c.db().pc().iter().map(|p| p.account_id).collect();
    ids.sort_by_key(|&id| crate::party::idb_order(id));
    for pid in ids {
        if pid == sd || group_level(c, sd) < group_level(c, pid) {
            continue;
        }
        let Some(pl) = unit::get(c, pid) else { continue };
        if pl.map == me.map && pl.x == me.x && pl.y == me.y {
            continue; // Don't waste time warping the character to the same place.
        }
        if mapflag(c, pl.map, MF_NOWARP) && !warp_anywhere {
            count += 1;
        } else {
            if pc::setpos(c, pid, me.map, me.x, me.y, unit::CLR_RESPAWN) == pc::SetPos::Autotrade {
                count += 1;
            }
        }
    }
    out.say(msg_txt(92)); // All characters recalled!
    if count > 0 {
        out.say(&data::format_msg(msg_txt(1033), &[&count]));
    }
    0
}

/// map.cpp map_getmapflag_name: the flag's script constant ("mf_…") without its prefix, lower case.
fn mapflag_name(flag: i32) -> Option<String> {
    let mut v: Vec<&String> = data::consts().iter().filter(|(k, c)| k.starts_with("mf_") && c.value == flag as i64 && !c.parameter).map(|(k, _)| k).collect();
    v.sort();
    v.first().map(|k| k[3..].to_string())
}

/// @mapflag {<flag> {<0|1>}} / @mapflag available: lists the map's flags, sets one (not the valued ones) or lists
/// the flag names.
fn mapflag(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    use crate::script::buildins::script2::mapmisc::{map_getmapflag_sub, map_setmapflag_sub, MfArgs, MF_MAX};
    let Some(me) = unit::get(c, sd) else { return -1 };
    let b = bytes(message);
    let mut s = Scan::new(&b);
    let flag_name = s.word(49);
    let flag = flag_name.as_ref().and_then(|_| s.int(6)).map(|v| v as i16).unwrap_or(0);
    let Some(flag_name) = flag_name.filter(|_| !message.is_empty()) else {
        out.say(msg_txt(1311)); // Enabled Mapflags in this map:
        out.say("----------------------------------");
        for i in 0..MF_MAX {
            if let Some(name) = mapflag_name(i) {
                if map_getmapflag_sub(c, me.map, i, &MfArgs::Val(0)) != 0 {
                    out.say(&name);
                }
            }
        }
        out.say(" ");
        out.say(msg_txt(1312)); // Usage: "@mapflag monster_noteleport 1" (0=Off | 1=On)
        out.say(msg_txt(1313)); // Type "@mapflag available" to list the available mapflags.
        return 1;
    };
    // Check if the list of mapflags was requested
    if !flag_name.eq_ignore_ascii_case("available") {
        let lower = flag_name.to_ascii_lowercase();
        match data::constant(&format!("mf_{lower}")) {
            Some(mf) => {
                use crate::npc::*;
                let disabled = [MF_NOSAVE, MF_PVP_NIGHTMAREDROP, MF_RESTRICTED, MF_NOCOMMAND, MF_BEXP, MF_JEXP, MF_BATTLEGROUND, MF_SKILL_DAMAGE, MF_SKILL_DURATION];
                let mf = mf as i16;
                if flag > 0 && disabled.contains(&mf) {
                    out.say(&format!("[ @mapflag ] {lower} flag cannot be enabled as it requires unique values."));
                } else {
                    map_setmapflag_sub(c, me.map, mf, flag != 0, &MfArgs::None);
                    out.say(&format!("[ @mapflag ] {lower} flag has been set to {} value = {flag}", if flag != 0 { "On" } else { "Off" }));
                }
                return 0;
            }
            None => {
                out.say(msg_txt(1314)); // Invalid flag name or flag.
                out.say(msg_txt(1313)); // Type "@mapflag available" to list the available mapflags.
                return 1;
            }
        }
    }
    out.say(msg_txt(1315)); // Available Flags:
    out.say("----------------------------------");
    let mut buf = String::new();
    let mut j = 0;
    for i in 0..MF_MAX {
        if let Some(name) = mapflag_name(i) {
            buf.push_str(&name);
            if i + 1 < MF_MAX {
                buf.push_str(", ");
            }
            j += 1;
        }
        if i > 0 && (j == 6 || i + 1 == MF_MAX) {
            out.say(&buf);
            buf.clear();
            j = 0;
        }
    }
    out.say(msg_txt(1312)); // Usage: "@mapflag monster_noteleport 1" (0=Off | 1=On)
    0
}
