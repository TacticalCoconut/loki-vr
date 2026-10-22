//! clif.cpp clif_parse_* for the packets phase 1 handles: time sync, walking, facing, chat, whispers, emotions,
//! sitting / standing, names, leaving.

use crate::clif::{self, Target};
use crate::net::packets::{packet_pos, PACKET_CZ_MOVETO_MAP, PACKET_CZ_REQ_EMOTION};
use crate::net::wire::{from_latin1, read_pos3, until_nul, Reader};
use crate::pc;
use crate::unit::{self, USW_FIXPOS};
use crate::world::Ctx;

fn pos_reader<'a>(pkt: &'a [u8], cmd: u16, i: usize) -> Reader<'a> {
    let pos = packet_pos(cmd);
    Reader::at(pkt, pos.get(i).copied().unwrap_or(2) as usize)
}

fn cmd_of(pkt: &[u8]) -> u16 {
    u16::from_le_bytes([pkt[0], pkt[1]])
}

/// CZ_REQUEST_TIME2 (0x0360): ZC_NOTIFY_TIME.
pub fn tick_send(c: &Ctx, aid: u32, pkt: &[u8]) {
    if let Some(mut p) = pc::get(c, aid) {
        p.client_tick = pos_reader(pkt, cmd_of(pkt), 0).u32().unwrap_or(0);
        pc::put(c, &p);
    }
    if let Some(sd) = unit::get(c, aid) {
        clif::notify_time(c, &sd);
    }
}

/// CZ_REQUEST_MOVE2 (0x035f).
pub fn walk_to_xy(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(sd) = unit::get(c, aid) else { return };
    let Some(p) = pc::get(c, aid) else { return };
    if p.dead_sit == 1 {
        clif::clearunit_area(c, &sd, unit::CLR_DEAD);
        return;
    }
    const OPT1_STONEWAIT: i16 = 6;
    const OPT1_BURNING: i16 = 7;
    if sd.sc.opt1 != 0 && (sd.sc.opt1 == OPT1_STONEWAIT || sd.sc.opt1 == OPT1_BURNING) {
        // you CAN walk with these
    } else if crate::pc_misc::progressbar_abort_if_running(c, aid) {
        return; // First walk attempt cancels the progress bar
    } else if pc::cant_act(&p, sd.sc.opt1) {
        return;
    }
    // clif.cpp:11396: no walking while running
    if crate::status::sc::has_sce(c, aid, crate::status::consts::SC_RUN) || crate::status::sc::has_sce(c, aid, crate::status::consts::SC_WUGDASH) {
        return;
    }
    let off = packet_pos(cmd_of(pkt)).first().copied().unwrap_or(2) as usize;
    let Some((x, y, _)) = pkt.get(off..).and_then(read_pos3) else { return };
    // A move one cell west is only valid if the target cell is free (official_cell_stack_limit)
    if crate::conf::official_cell_stack_limit() > 0 && sd.x == x + 1 && sd.y == y && crate::map::count_oncell(c, sd.map, x, y, crate::map::BL_CHAR | crate::map::BL_NPC, 1) > 0 {
        return;
    }
    // Cloaking wall check is actually updated when you click to process next movement
    // not when you move each cell.  This is official behaviour. (thief agent: clif.cpp clif_parse_WalkToXY)
    {
        use crate::status::{consts::*, sc};
        if sc::has_sce(c, aid, SC_CLOAKING) {
            crate::status::sc_ext::skill_check_cloaking(c, &sd, true);
        }
        sc::status_change_end(c, aid, SC_ROLLINGCUTTER, 0); // If you move, you lose your counters. [malufett]
        sc::status_change_end(c, aid, SC_CRESCIVEBOLT, 0);
        sc::status_change_end(c, aid, SC_KI_SUL_RAMPAGE, 0);
    }
    pc::delinvincibletimer(c, aid);
    if crate::conf::idletime_option() & 0x0001 != 0 {
        if let Some(mut p) = pc::get(c, aid) {
            p.idletime = crate::pc::last_tick(c);
            pc::put(c, &p);
        }
    }
    unit::walktoxy(c, aid, x, y, 4);
}

/// Rovert's prevent logout option (clif_parse_QuitGame / clif_parse_Restart): no hiding, cloaking, chase walk,
/// cloaking exceed, Suhide or New Moon, and prevent_logout's delay after combat has passed.
fn can_log_out(c: &Ctx, p: &pc::Pc) -> bool {
    use crate::status::{consts::*, sc};
    let hidden = [SC_CLOAKING, SC_HIDING, SC_CHASEWALK, SC_CLOAKINGEXCEED, SC_SUHIDE, SC_NEWMOON].iter().any(|&t| sc::has_sce(c, p.account_id, t));
    !hidden && (crate::conf::prevent_logout() == 0 || p.canlog_tick == 0 || c.tick() - p.canlog_tick > crate::conf::prevent_logout())
}

/// CZ_REQ_DISCONNECT (0x018a): 0x018b {0} unless prevent_logout holds the character.
pub fn quit_game(c: &Ctx, aid: u32) {
    let Some(sd) = unit::get(c, aid) else { return };
    let Some(p) = pc::get(c, aid) else { return };
    let ok = can_log_out(c, &p);
    if ok {
        clif::disconnect_ack(c, &sd, 0);
        // (flush_fifo(fd): the module sends at the end of the reducer)
        if crate::conf::drop_connection_on_quit() {
            crate::session::set_eof(c, aid);
        }
    } else {
        clif::disconnect_ack(c, &sd, 1);
    }
}

/// CZ_REQNAME2 (0x0368).
pub fn get_char_name_request(c: &Ctx, aid: u32, pkt: &[u8]) {
    let mut id = pos_reader(pkt, cmd_of(pkt), 0).i32().unwrap_or(0);
    if id < 0 && -id == aid as i32 {
        id = aid as i32; // disguises
    }
    let Some(sd) = unit::get(c, aid) else { return };
    let Some(bl) = unit::get(c, id as u32) else { return };
    if !bl.on_map && bl.id != sd.id {
        return;
    }
    if sd.map != bl.map || !crate::path::check_distance((sd.x - bl.x) as i32, (sd.y - bl.y) as i32, crate::conf::area_size() as i32) {
        return;
    }
    clif::name(c, &sd, &bl, Target::SelfOnly);
}

/// clif.cpp clif_process_message: validates "Name : message" (or the whisper layout), runs @commands.
/// Returns (name, message, full "Name : message") when it is ordinary chat.
pub(crate) fn process_message(c: &Ctx, aid: u32, pkt: &[u8], whisper: bool) -> Option<(String, String, String)> {
    let cmd = cmd_of(pkt);
    let pos = packet_pos(cmd);
    let len_off = pos.first().copied().unwrap_or(2) as usize;
    let input_off = pos.get(1).copied().unwrap_or(4) as usize;
    let packet_len = Reader::at(pkt, len_off).u16()? as usize;
    if packet_len < 4 {
        log::warn!("clif_process_message: malformed packet (no message data)");
        return None;
    }
    let input_len = packet_len - 4;
    let input = pkt.get(input_off..input_off + input_len.min(pkt.len().saturating_sub(input_off)))?;
    let sd = unit::get(c, aid)?;
    let (name, message_bytes) = if whisper {
        if input_len < 24 + 1 {
            log::warn!("clif_process_message: malformed whisper (packet length)");
            return None;
        }
        let name_len = input[..23].iter().position(|&b| b == 0).unwrap_or(23);
        if input[name_len] != 0 {
            log::warn!("clif_process_message: unterminated whisper name");
            return None;
        }
        (from_latin1(&input[..name_len]), &input[24..])
    } else {
        let own: Vec<u8> = crate::net::wire::latin1(&sd.name);
        let name_len = own.len().min(23);
        let sep = b" : ";
        if input_len < name_len + sep.len() + 1 {
            log::warn!("clif_process_message: malformed message (no username data)");
            return None;
        }
        if input[..name_len] != own[..name_len] || &input[name_len..name_len + 3] != sep {
            // Hacked message, or the "client desynch" issue: kick them out to correct it.
            log::warn!("clif_process_message: Player '{}' sent a message using an incorrect name! Forcing a relog...", sd.name);
            crate::session::set_eof(c, aid); // Just kick them out to correct it.
            return None;
        }
        // If it is not a whisper message, set the name to the fakename of the player (@fakename)
        let fakename = crate::atcmd_pc::get(c, aid).fakename;
        (if fakename.is_empty() { sd.name.clone() } else { fakename }, &input[name_len + 3..])
    };
    // PACKETVER >= 20151001: no zero termination anymore (messageLength + 1)
    let message_len = message_bytes.len() + 1;
    if message_len > crate::net::CHAT_SIZE_MAX - 1 {
        log::warn!("clif_process_message: Player '{}' sent a message too long", sd.name);
        return None;
    }
    let message = from_latin1(until_nul(message_bytes));
    let full = if whisper { format!("{name:<24}{message}") } else { format!("{name} : {message}") };
    if crate::atcommand::is_atcommand(c, aid, &message, 1) {
        return None;
    }
    let mut p = pc::get(c, aid)?;
    // clif.cpp:10559: no "chatting" while muted (sc.cant.chat is SC_NOCHAT with MANNER_NOCHAT, status_calc_state)
    if crate::status::sc::cant(c, aid, crate::status::consts::SCS_NOCHAT) || p.block_action & pc::PCBLOCK_CHAT != 0 {
        return None;
    }
    if crate::conf::min_chat_delay() > 0 {
        if p.cantalk_tick - c.tick() > 0 {
            return None;
        }
        p.cantalk_tick = c.tick() + crate::conf::min_chat_delay();
    }
    if crate::conf::idletime_option() & 0x0020 != 0 {
        p.idletime = crate::pc::last_tick(c);
    }
    pc::put(c, &p);
    Some((name, message, full))
}

/// CZ_REQUEST_CHAT (0x00f3): to the area (or the chat room) and back to the speaker.
pub fn global_message(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some((_name, message, output)) = process_message(c, aid, pkt, false) else { return };
    let Some(sd) = unit::get(c, aid) else { return };
    // a chat bar bound to a channel (@channel bindto) talks there
    if crate::channel::global_message(c, aid, &message) {
        return;
    }
    // send message to others: the chat room (CHAT_WOS) or the area (AREA_CHAT_WOC)
    if pc::get(c, aid).map(|p| p.chat_id != 0).unwrap_or(false) {
        crate::chat::global_message(c, &sd, &output);
    } else {
        clif::global_message(c, &sd, &output, Target::AreaChatWoc);
    }
    // send back message to the speaker: 0x008e
    let mut b = crate::net::wire::latin1(&output);
    b.push(0);
    let mut w = Vec::with_capacity(4 + b.len());
    w.extend_from_slice(&0x008eu16.to_le_bytes());
    w.extend_from_slice(&((4 + b.len()) as u16).to_le_bytes());
    w.extend_from_slice(&b);
    clif::send(c, &w, &sd, Target::SelfOnly);
    // trigger listening npcs (PCRE_SUPPORT: npc_chat_sub, script2's npcchat.rs)
    crate::script::buildins::script2::npcchat::npc_chat_area(c, aid, &output);
    // Chat logging type 'O' / Global Chat
    crate::map_log::log_chat_sd(c, crate::map_log::LOG_CHAT_GLOBAL, 0, aid, None, &message);
}

/// CZ_WHISPER (0x0096).
pub fn wis_message(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some((target, message, _output)) = process_message(c, aid, pkt, true) else { return };
    let Some(sd) = unit::get(c, aid) else { return };
    // Chat logging type 'W' / Whisper
    crate::map_log::log_chat_sd(c, crate::map_log::LOG_CHAT_WHISPER, 0, aid, Some(&target), &message);
    let tb = crate::net::wire::latin1(&target);
    if tb.len() > 4 && tb[..4].eq_ignore_ascii_case(b"NPC:") {
        // whispering an NPC runs its OnWhisperGlobal label with @whispervar0$..9$ (script engine, phase 5)
        if crate::script::whisper_npc(c, aid, &from_latin1(&tb[4..]), &message) {
            return;
        }
    } else if target.starts_with('#') {
        // chat channels (channel.rs)
        if crate::channel::whisper(c, aid, &target, &message) {
            return;
        }
    }
    // searching destination character
    let dst = pc::nick2sd(c, &target, false);
    let Some(dst) = dst.filter(|d| d.name == target) else {
        clif::wis_end(c, &sd, 1); // not online
        return;
    };
    if dst.id == sd.id {
        // rAthena passes it through (you can whisper yourself)
    }
    // if player ignores everyone / is autotrading / ignores the source character (social.rs)
    if let Some(r) = crate::social::whisper_refused(c, &sd, &dst) {
        if r == crate::social::WHISPER_AUTOTRADE {
            // safesnprintf(output, sizeof(output), "%s is in autotrade mode and cannot receive whispered messages.", …)
            let output = format!("{} is in autotrade mode and cannot receive whispered messages.", dst.name);
            clif::wis_message(c, &sd, crate::rules::WISP_SERVER_NAME, None, &output, 0);
        } else {
            clif::wis_end(c, &sd, r);
        }
        return;
    }
    // the ack first, then the message (clif.cpp:11973-11976)
    clif::wis_end(c, &sd, 0);
    clif::wis_message(c, &dst, &sd.name, Some(&sd), &message, 0);
}

/// CZ_CHANGE_DIRECTION2 (0x0361).
pub fn change_dir(c: &Ctx, aid: u32, pkt: &[u8]) {
    let cmd = cmd_of(pkt);
    let headdir = pos_reader(pkt, cmd, 0).u8().unwrap_or(0);
    let dir = pos_reader(pkt, cmd, 1).u8().unwrap_or(0);
    let Some(mut sd) = unit::get(c, aid) else { return };
    // pc_setdir
    sd.ud.dir = dir;
    sd.head_dir = headdir;
    unit::save(c, &sd);
    clif::changed_dir(c, &sd, Target::AreaWos);
}

/// CZ_REQ_EMOTION (0x00bf).
pub fn emotion(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_REQ_EMOTION::read(&mut Reader::new(pkt)) else { return };
    const ET_MAX: u8 = 88;
    const ET_CHAT_PROHIBIT: u8 = 34;
    const ET_DICE1: u8 = 58;
    const ET_DICE6: u8 = 63;
    if p.emotion_type >= ET_MAX {
        return;
    }
    let mut emoticon = p.emotion_type;
    let Some(sd) = unit::get(c, aid) else { return };
    let Some(mut pcd) = pc::get(c, aid) else { return };
    if !crate::conf::basic_skill_check() || crate::skill::checkskill(c, aid, 1) >= 2 || crate::skill::checkskill(c, aid, 5018) >= 1 {
        if emoticon == ET_CHAT_PROHIBIT {
            crate::skill::skill_fail(c, &sd, 1, 1);
            return;
        }
        let now = c.tick() / 1000;
        if pcd.emotionlasttime + 1 >= now {
            pcd.emotionlasttime = now;
            pc::put(c, &pcd);
            crate::skill::skill_fail(c, &sd, 1, 1);
            return;
        }
        pcd.emotionlasttime = now;
        if crate::conf::idletime_option() & 0x0080 != 0 {
            pcd.idletime = crate::pc::last_tick(c);
        }
        pc::put(c, &pcd);
        if pcd.block_action & pc::PCBLOCK_EMOTION != 0 {
            crate::skill::skill_fail(c, &sd, 1, 1);
            return;
        }
        if crate::conf::client_reshuffle_dice() && (ET_DICE1..=ET_DICE6).contains(&emoticon) {
            emoticon = (c.rnd() % 6) as u8 + ET_DICE1;
        }
        clif::emotion(c, &sd, emoticon);
    } else {
        crate::skill::skill_fail(c, &sd, 1, 1);
    }
}

/// CZ_REQUEST_ACT2 (0x0437): attacks (phase 3), sit (2), stand (3).
pub fn action_request(c: &Ctx, aid: u32, pkt: &[u8]) {
    let cmd = cmd_of(pkt);
    let target_id = pos_reader(pkt, cmd, 0).i32().unwrap_or(0);
    let action_type = pos_reader(pkt, cmd, 1).u8().unwrap_or(0);
    action_request_sub(c, aid, action_type, target_id);
}

/// clif.cpp clif_parse_ActionRequest_sub.
pub fn action_request_sub(c: &Ctx, aid: u32, action_type: u8, target_id: i32) {
    const DMG_NORMAL: u8 = 0;
    const DMG_SIT_DOWN: u8 = 2;
    const DMG_STAND_UP: u8 = 3;
    const DMG_REPEAT: u8 = 7;
    const OPT1_STONEWAIT: i16 = 6;
    const OPT1_BURNING: i16 = 7;
    let Some(mut sd) = unit::get(c, aid) else { return };
    let Some(p) = pc::get(c, aid) else { return };
    if p.dead_sit == 1 {
        clif::clearunit_area(c, &sd, unit::CLR_DEAD);
        return;
    }
    // Statuses that don't let the player sit / stand / talk with NPCs (targeted)
    if action_type != DMG_NORMAL && action_type != DMG_REPEAT {
        if crate::status::sc::cant(c, aid, crate::status::consts::SCS_NOINTERACT) {
            return;
        }
        unit::stop_walking(c, &mut sd, USW_FIXPOS, 0);
    }
    unit::stop_attack(c, &mut sd);
    unit::save(c, &sd);
    let target_id = if target_id < 0 && -target_id == aid as i32 { aid as i32 } else { target_id };
    match action_type {
        DMG_NORMAL | DMG_REPEAT => crate::battle::pc_attack_request(c, aid, target_id as u32, action_type != 0),
        DMG_SIT_DOWN => {
            if crate::conf::basic_skill_check() && crate::skill::checkskill(c, aid, 1) < 3 && crate::skill::checkskill(c, aid, 5018) < 1 {
                crate::skill::skill_fail(c, &sd, 1, 2);
                return;
            }
            if sd.vd.dead_sit == 2 {
                clif::sitting(c, &sd); // bugged client? just refresh them
                return;
            }
            if sd.ud.skilltimer != 0 || (sd.sc.opt1 != 0 && sd.sc.opt1 != OPT1_STONEWAIT && sd.sc.opt1 != OPT1_BURNING) {
                return;
            }
            //No sitting during these states either.
            {
                use crate::status::{consts::*, defs::BCT_SELF, sc};
                if sc::has_sce(c, aid, SC_DANCING) || sc::get_sce(c, aid, SC_GRAVITATION).map(|e| e.val3 == BCT_SELF).unwrap_or(false) {
                    return;
                }
            }
            if p.block_action & pc::PCBLOCK_SITSTAND != 0 {
                clif::displaymessage(c, &sd, crate::data::msg_txt(794));
                return;
            }
            // pc_setsit
            let Some(mut sd) = unit::get(c, aid) else { return };
            unit::stop_walking(c, &mut sd, USW_FIXPOS | unit::USW_MOVE_FULL_CELL, 0);
            unit::stop_attack(c, &mut sd);
            sd.vd.dead_sit = 2;
            unit::save(c, &sd);
            let Some(mut p) = pc::get(c, aid) else { return };
            p.dead_sit = 2;
            if crate::conf::idletime_option() & 0x0040 != 0 {
                p.idletime = crate::pc::last_tick(c);
            }
            pc::put(c, &p);
            crate::status::sc_ext::skill_sit(c, aid, true);
            if let Some(sd) = unit::get(c, aid) {
                clif::sitting(c, &sd);
            }
        }
        DMG_STAND_UP => {
            if sd.vd.dead_sit != 2 {
                clif::standing(c, &sd); // bugged client? just refresh them
                return;
            }
            if sd.sc.opt1 != 0 && sd.sc.opt1 != OPT1_STONEWAIT && sd.sc.opt1 != OPT1_BURNING {
                return;
            }
            if p.block_action & pc::PCBLOCK_SITSTAND != 0 {
                clif::displaymessage(c, &sd, crate::data::msg_txt(794));
                return;
            }
            if pc::setstand(c, aid, false) {
                if crate::conf::idletime_option() & 0x0040 != 0 {
                    if let Some(mut p) = pc::get(c, aid) {
                        p.idletime = crate::pc::last_tick(c);
                        pc::put(c, &p);
                    }
                }
                // skill_sit(sd, false) then clif_standing: rAthena sends both again after pc_setstand's own pair
                crate::status::sc_ext::skill_sit(c, aid, false);
                if let Some(sd) = unit::get(c, aid) {
                    clif::standing(c, &sd);
                }
            }
        }
        _ => {}
    }
}

/// CZ_RESTART (0x00b2): 0 respawn (phase 3), 1 character select.
pub fn restart(c: &Ctx, aid: u32, pkt: &[u8]) {
    let kind = pos_reader(pkt, cmd_of(pkt), 0).u8().unwrap_or(0);
    let Some(sd) = unit::get(c, aid) else { return };
    let Some(p) = pc::get(c, aid) else { return };
    match kind {
        0 => pc::respawn(c, aid, unit::CLR_OUTSIGHT),
        1 => {
            if can_log_out(c, &p) {
                // chrif_charselectreq → char server → clif_charselectok: the character leaves the world and the login
                // binding stays, so the client shows the character list again.
                clif::charselectok(c, &sd, 1);
                let who = p.identity;
                pc::quit(c, aid);
                if let Some(s) = c.db().session().identity().find(who) {
                    c.db().session().identity().update(crate::account::Session { selected_char: None, ..s });
                }
            } else {
                clif::disconnect_ack(c, &sd, 1);
            }
        }
        _ => {}
    }
}

/// CZ_REQ_USER_COUNT (0x00c1): ZC_USER_COUNT.
pub fn how_many_connections(c: &Ctx, aid: u32) {
    let Some(sd) = unit::get(c, aid) else { return };
    let n = c.db().pc().iter().filter(|p| !p.disconnected).count() as u32;
    let mut w = Vec::with_capacity(6);
    w.extend_from_slice(&0x00c2u16.to_le_bytes());
    w.extend_from_slice(&n.to_le_bytes());
    clif::send(c, &w, &sd, Target::SelfOnly);
}

/// CZ_MOVETO_MAP (0x0140, /mm): "@mapmove <map> <x> <y>".
pub fn map_move(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_MOVETO_MAP::read(&mut Reader::new(pkt)) else { return };
    let map: String = p.map.chars().take(15).collect();
    crate::atcommand::is_atcommand(c, aid, &format!("@mapmove {} {} {}", map, p.x, p.y), 1);
}

/// A packet whose system is not ported yet: logged once per call.
pub fn not_yet(_c: &Ctx, aid: u32, cmd: u16, func: &str) {
    log::info!("{func} (0x{cmd:04x}) from {aid}: not handled yet");
}

use crate::account::session as _;
use crate::pc::pc as _;
use spacetimedb::Table as _;
