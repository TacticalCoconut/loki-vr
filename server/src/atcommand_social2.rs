//! atcommand.cpp's commands of the social2 systems: @mail (the pre-RODEX mail window), @trade, @cash, @points,
//! @limitedsale.

use crate::atcommand::Out;
use crate::world::Ctx;

/// The social2 commands, by ACMD_FUNC name (None: not one of them).
pub(crate) fn run(c: &Ctx, out: &Out, sd: u32, func: &str, _command: &str, message: &str) -> Option<i32> {
    Some(match func {
        "mail" => mail(c, sd),
        "trade" => trade(c, out, sd, message),
        "cash" | "points" => cash(c, out, sd, func, message),
        "limitedsale" => limitedsale(c, sd),
        _ => return None,
    })
}

/// ACMD_FUNC(mail): mail_openmail.
fn mail(c: &Ctx, sd: u32) -> i32 {
    crate::mail::mail_openmail(c, sd);
    0
}

/// ACMD_FUNC(trade): trade_traderequest with a player found by (partial) name.
fn trade(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    // sscanf(message, "%23[^\n]", atcmd_player_name)
    let name: String = message.split('\n').next().unwrap_or("").chars().take(23).collect();
    if name.is_empty() {
        out.say(crate::data::msg_txt(1230)); // Please enter a player name (usage: @trade <char name>).
        return -1;
    }
    let Some(pl) = crate::pc::nick2sd(c, &name, true) else {
        out.say(crate::data::msg_txt(3)); // Character not found.
        return -1;
    };
    crate::trade::trade_traderequest(c, sd, Some(pl.id));
    0
}

/// atoi: leading white space, an optional sign, the digits that follow (0 when there are none).
fn atoi(s: &str) -> i32 {
    let t = s.trim_start();
    let (neg, rest) = match t.as_bytes().first() {
        Some(b'-') => (true, &t[1..]),
        Some(b'+') => (false, &t[1..]),
        _ => (false, t),
    };
    let mut v: i64 = 0;
    for ch in rest.bytes().take_while(|b| b.is_ascii_digit()) {
        v = (v * 10 + (ch - b'0') as i64).min(i64::MAX / 10);
    }
    let v = if neg { -v } else { v };
    v.clamp(i32::MIN as i64, i32::MAX as i64) as i32
}

fn say_green(c: &Ctx, sd: u32, text: &str) {
    if let Some(u) = crate::unit::get(c, sd) {
        // color_table[COLOR_LIGHT_GREEN] (0xB5FFB5 as BGR)
        crate::clif::messagecolor_target(c, &u, 0xb5ffb5, &crate::net::wire::latin1(text), false, crate::clif::Target::SelfOnly, None);
    }
}

/// ACMD_FUNC(cash) / ACMD_FUNC(points): gives or takes cash (Kafra) points.
fn cash(c: &Ctx, out: &Out, sd: u32, func: &str, message: &str) -> i32 {
    use crate::cashshop::{pc_getcash, pc_paycash, points, cashshop_show_points};
    use crate::data::{format_msg, msg_txt};
    let Some(p) = points(c, sd) else { return -1 };
    // Since there is no cashpoint update packet we need to force updating like this
    if p.cashshop_open {
        out.say(msg_txt(1376)); // Please close the cashshop before using this command.
        return -1;
    }
    let mut value = atoi(message);
    if message.is_empty() || value == 0 {
        out.say(msg_txt(1322)); // Please enter an amount.
        return -1;
    }
    let kafra = func == "points";
    if value > 0 {
        let ret = if kafra { pc_getcash(c, sd, 0, value, crate::map_log::LOG_TYPE_COMMAND) } else { pc_getcash(c, sd, value, 0, crate::map_log::LOG_TYPE_COMMAND) };
        if ret >= 0 {
            let p = points(c, sd).unwrap_or(p);
            if kafra {
                say_green(c, sd, &format_msg(msg_txt(506), &[&ret, &p.kafra_points])); // Gained %d kafra points. Total %d points.
            } else if !cashshop_show_points() {
                // If this option is set, the message is already sent by pc function
                say_green(c, sd, &format_msg(msg_txt(505), &[&ret, &p.cash_points])); // Gained %d cash points. Total %d points.
            }
        } else {
            out.say(msg_txt(149)); // Impossible to increase the number/value.
        }
    } else {
        let have = if kafra { p.kafra_points } else { p.cash_points };
        if -(value as i64) > have as i64 {
            // By command, if cash < value, force it to remove all
            value = -have;
        }
        let ret = if kafra { pc_paycash(c, sd, 0, -value, crate::map_log::LOG_TYPE_COMMAND) } else { pc_paycash(c, sd, -value, 0, crate::map_log::LOG_TYPE_COMMAND) };
        if ret >= 0 {
            let p = points(c, sd).unwrap_or(p);
            if kafra {
                say_green(c, sd, &format_msg(msg_txt(411), &[&ret, &p.kafra_points])); // Removed %d kafra points. Total %d points.
            } else if !cashshop_show_points() {
                say_green(c, sd, &format_msg(msg_txt(410), &[&ret, &p.cash_points])); // Removed %d cash points. Total %d points.
            }
        } else {
            out.say(msg_txt(41)); // Unable to decrease the number/value.
        }
    }
    0
}

/// ACMD_FUNC(limitedsale): the sale administration window.
fn limitedsale(c: &Ctx, sd: u32) -> i32 {
    crate::cashshop::clif_sale_open(c, sd);
    0
}
