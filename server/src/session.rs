//! common/socket.cpp set_eof / session_isActive and map/clif.cpp clif_authfail_fd, clif_GM_kick, clif_GM_kickack,
//! clif_quitsave and clif_parse's eof branch: the one way the server closes a client's connection (atcmd agent, wave D).
//!
//! rAthena: set_eof marks the socket (session[fd]->flag.eof); from then on session_isActive(fd) is false, so
//! clif_send / socket_send write nothing more to it, and clif_parse — which tests eof before every packet it would
//! parse — never parses what the client sent after that point. The session is closed by the next clif_parse call on
//! it: at once when the handler that set eof was parsing this very session (the next turn of clif_parse's loop), else
//! in do_sockets' next pass over the sockets (send_shortlist_do_sends / the parse loop). clif_parse's eof branch lets
//! the character leave (clif_quitsave: map_quit, or clif_delayquit 10 s later while prevent_logout holds; map_quit
//! during log on / off; an @autotrade character stays without its client) and do_close drops the session with the
//! bytes the client had sent but the server had not read.
//!
//! Here: `Pc::disconnected` is session_isActive's false (clif::send and the packet dispatch skip such a session) and
//! also the "no fd" state of a character waiting out clif_delayquit; a `session_eof` row marks a session whose eof
//! clif_parse has not handled yet. `net::parse` handles the sender's own eof right after the packet that caused it
//! (and drops the rest of the buffer); every other pending eof is handled when the reducer's work is done
//! (world::run), i.e. after the handler that set it — the module's "next pass over the sockets". The server's
//! reducers handle a whole receive buffer at once, so rAthena's three-packets-per-pass limit does not apply.

use spacetimedb::{table, Identity, Table};

use crate::pc;
use crate::timer::{self, Func};
use crate::unit;
use crate::world::Ctx;

/// A session set_eof marked and clif_parse has not closed yet, in the order the marks were made.
#[table(accessor = session_eof)]
pub struct SessionEof {
    #[primary_key]
    #[auto_inc]
    pub seq: u64,
    #[unique]
    pub account_id: u32,
    /// The connection (fd) the mark belongs to: do_close drops its unread bytes even when the character has already
    /// left by another way.
    pub identity: Identity,
}

/// socket.cpp set_eof(sd->fd): if the session is still active it stops being so at once (no packet reaches the client
/// any more, none of its packets is parsed) and clif_parse closes it later (`do_eof`).
pub fn set_eof(c: &Ctx, aid: u32) {
    let Some(mut p) = pc::get(c, aid) else { return };
    if p.disconnected {
        return; // !session_isActive(fd): eof already, or no connection (fd 0)
    }
    p.disconnected = true;
    let identity = p.identity;
    pc::put(c, &p);
    c.db().session_eof().insert(SessionEof { seq: 0, account_id: aid, identity });
}

/// Whether the session has an eof mark clif_parse has not handled yet (the fd is still valid).
pub fn eof_pending(c: &Ctx, aid: u32) -> bool {
    c.db().session_eof().account_id().find(aid).is_some()
}

/// clif.cpp clif_authfail_fd(sd->fd, type): SC_NOTIFY_BAN (0x0081) with the reason, then set_eof. A character without
/// its client (fd 0: waiting out clif_delayquit) gets nothing (session_isValid); an eof session gets no packet
/// (socket_send tests session_isActive).
pub fn clif_authfail_fd(c: &Ctx, aid: u32, kind: u8) {
    let Some(p) = pc::get(c, aid) else { return };
    if p.disconnected && !eof_pending(c, aid) {
        return; // !session_isValid(fd)
    }
    if !p.disconnected {
        let pk = crate::net::packets::PACKET_SC_NOTIFY_BAN { packetType: 0x0081, errorCode: kind };
        c.send(p.identity, &pk.to_vec());
    }
    set_eof(c, aid);
}

/// clif.cpp clif_GM_kick(sd, tsd): `tsd` is thrown out — SC_NOTIFY_BAN 15 and the connection closes, or map_quit at
/// once for a character without an active session — and the GM `sd` who asked gets the kick ack. `sd` None is a kick
/// by the server or a script (rAthena then sets tsd->state.keepshop so that a vending shop stays open over the quit:
/// the vending agent's @autotrade / vending state).
pub fn clif_gm_kick(c: &Ctx, sd: Option<u32>, tsd: u32) {
    let Some(target) = pc::get(c, tsd) else { return }; // nullpo_retv(tsd)
    let account_id = target.account_id;
    if sd.is_none() {
        crate::autotrade::set_keepshop(c, tsd);
    }
    if pc::is_active_session(c, tsd) {
        clif_authfail_fd(c, tsd, 15);
    } else {
        pc::quit(c, tsd);
    }
    if let Some(sd) = sd {
        clif_gm_kickack(c, sd, account_id);
    }
}

/// clif.cpp clif_GM_kickack (ZC_ACK_DISCONNECT_CHARACTER 0x00cd): written straight to the GM's fd with the low byte
/// of the kicked account id as the result ("FIXME: this is not account id" in rAthena).
pub fn clif_gm_kickack(c: &Ctx, sd: u32, id: u32) {
    let Some(p) = pc::get(c, sd) else { return };
    // WFIFOHEAD / WFIFOSET on sd->fd: no session_isActive test, but nothing reaches a closed or eof socket here
    if !p.disconnected {
        c.send(p.identity, &[0xcd, 0x00, id as u8]);
    }
}

/// clif.cpp clif_quitsave: the character leaves now (map_quit), or — while prevent_logout holds after combat — the
/// session lets go of it (sd->fd = 0) and clif_delayquit removes it 10 s later.
pub fn clif_quitsave(c: &Ctx, aid: u32) {
    let Some(mut p) = pc::get(c, aid) else { return };
    let prevent = crate::conf::prevent_logout();
    if prevent == 0 || p.canlog_tick == 0 || c.tick() - p.canlog_tick > prevent {
        pc::quit(c, aid);
    } else {
        // session_isValid(sd->fd): disassociate the session from the player and set a timer to make them quit later
        p.disconnected = true;
        pc::put(c, &p);
        timer::add(c, c.tick() + 10_000, Func::DelayQuit, aid, 0);
    }
}

/// clif.cpp clif_parse's eof branch for one session, then do_close: the character leaves (@autotrade: stays without
/// its client — vending agent; active: clif_quitsave; logging on / off: map_quit) and the session goes with the bytes
/// it had not read.
fn clif_parse_eof(c: &Ctx, aid: u32) {
    let mark = c.db().session_eof().account_id().find(aid);
    if let Some(row) = &mark {
        c.db().session_eof().seq().delete(row.seq);
    }
    let Some(p) = pc::get(c, aid) else {
        // the character already left (map_quit by another way): only the session goes
        if let Some(row) = mark {
            do_close(c, row.identity);
        }
        return;
    };
    let who = p.identity;
    if crate::autotrade::autotrade(c, aid) != 0 {
        //Disassociate character from the socket connection (session_data = nullptr, sd->fd = 0: autotrade.rs)
        let mut p = p;
        crate::autotrade::disassociate(c, &mut p);
    } else if p.active {
        log::info!("Character '{}' logged off.", unit::get(c, aid).map(|u| u.name).unwrap_or_default());
        clif_quitsave(c, aid);
    } else {
        // Unusual logout (during log on/off/map-changer procedure)
        log::info!("Player AID:{}/CID:{} logged off.", p.account_id, p.char_id);
        pc::quit(c, aid);
    }
    do_close(c, who);
}

/// socket.cpp do_close: what the client had sent and the server had not parsed goes with the session.
fn do_close(c: &Ctx, who: Identity) {
    crate::net::clear_rfifo(c, who);
}

/// do_sockets' pass over the eof sessions: clif_parse closes every session set_eof marked, in the order they were
/// marked. Called when a reducer's work is done (world::run).
pub fn do_eof(c: &Ctx) {
    // a closing session's map_quit can mark others (e.g. a kicked party leader's handover): take them too
    for _ in 0..1000 {
        let Some(row) = c.db().session_eof().iter().min_by_key(|r| r.seq) else { return };
        clif_parse_eof(c, row.account_id);
    }
}

/// clif_parse's test at the top of its loop for the session being parsed (the reducer's sender): true when a handler
/// has set eof on it, which closes it now; the caller parses nothing more from this buffer.
pub fn close_if_eof(c: &Ctx, who: Identity) -> bool {
    let Some(p) = pc::by_identity(c, who) else { return false };
    if !eof_pending(c, p.account_id) {
        return false;
    }
    clif_parse_eof(c, p.account_id);
    true
}

/// The client's connection is gone (recv_to_fifo read 0 bytes, or the module's equivalents: the SpacetimeDB
/// connection closed, the client left the map server): set_eof, and clif_parse closes the session in the same pass
/// (do_sockets' POSTSEND handles eof sessions right after receiving). A character already waiting out
/// clif_delayquit (no fd) is left to its timer.
pub fn connection_closed(c: &Ctx, who: Identity) {
    if let Some(p) = pc::by_identity(c, who) {
        set_eof(c, p.account_id);
        if eof_pending(c, p.account_id) {
            clif_parse_eof(c, p.account_id);
        }
    }
    do_close(c, who);
}
