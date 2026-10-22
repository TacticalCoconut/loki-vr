//! Script commands of the social2 systems: RoDEX mail (`mail`, `openmail`), the auction window (`openauction`, the
//! feature is off), the bank (`openbank`), the buying store (`buyingstore`, `searchstores`). script.cpp BUILDIN_FUNC
//! of the same names.

use super::BuildinFn;
use crate::int_mail::{self, MailMessage, MAIL_BODY_LENGTH, MAIL_INBOX_NORMAL, MAIL_MAX_ITEM, MAIL_NEW, MAIL_TITLE_LENGTH, NAME_LENGTH};
use crate::script::reg;
use crate::script::vm::*;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "mail" => mail,
        "openmail" => openmail,
        "openauction" => openauction,
        "openbank" => openbank,
        "buyingstore" => buyingstore,
        "searchstores" => searchstores,
        _ => return None,
    })
}

fn lat(s: &[u8]) -> String {
    crate::net::wire::from_latin1(s)
}

/// script.cpp mail_sub: argument `i` must be a numeric array variable; returns its name, the start index, the end
/// (script_array_highest_key), its id and reference.
fn mail_sub(rt: &Rt, st: &mut State, sd: &mut Option<u32>, i: i32) -> Option<(Vec<u8>, u32, u32, i32, RegRef)> {
    // Check if it is a variable
    let Data::Name(id, idx, rf) = st.data(i).clone() else {
        log::error!("buildin_mail: argument {i} is not a variable.");
        return None;
    };
    let name = name_of(id);
    if is_string_variable(&name) {
        log::error!("buildin_mail: variable \"{}\" is a string variable.", lat(&name));
        return None;
    }
    // Check if the variable requires a player
    if not_server_variable(name.first().copied().unwrap_or(0)) && sd.is_none() {
        // If no player is attached
        match rid2sd(rt, st, "buildin_mail") {
            Some(a) => *sd = Some(a),
            None => {
                log::error!("buildin_mail: variable \"{}\" was not a server variable, but no player was attached.", lat(&name));
                return None;
            }
        }
    }
    // Try to find the array's source pointer
    if reg::store_for(rt, st, *sd, &name, &rf).is_none() {
        log::error!("buildin_mail: variable \"{}\" is not an array.", lat(&name));
        return None;
    }
    let end = reg::highest_key(rt, st, *sd, &name, &rf);
    Some((name, idx, end, id, rf))
}

/// mail <destination id>,"<sender name>","<title>","<body>"{,<zeny>{,<item id array>,<item amount array>{,refine
/// {,bound{,<card0..3 arrays>{,<random option id/value/parameter arrays>…}}}}}}: a mail from the server
/// (intif_Mail_send with account id 0).
fn mail(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let mut msg = MailMessage::new();
    let mut sd: Option<u32> = None;
    msg.dest_id = getnum(rt, st, 2) as u32;
    let sender = getstr(rt, st, 3);
    if sender.len() > NAME_LENGTH {
        log::error!("buildin_mail: sender name can not be longer than {NAME_LENGTH} characters.");
        return SCRIPT_CMD_FAILURE;
    }
    msg.send_name = int_mail::strncpy(&lat(&sender), NAME_LENGTH);
    let title = getstr(rt, st, 4);
    if title.len() > MAIL_TITLE_LENGTH {
        log::error!("buildin_mail: title can not be longer than {MAIL_TITLE_LENGTH} characters.");
        return SCRIPT_CMD_FAILURE;
    }
    msg.title = int_mail::strncpy(&lat(&title), MAIL_TITLE_LENGTH);
    let body = getstr(rt, st, 5);
    if body.len() > MAIL_BODY_LENGTH {
        log::error!("buildin_mail: body can not be longer than {MAIL_BODY_LENGTH} characters.");
        return SCRIPT_CMD_FAILURE;
    }
    msg.body = int_mail::strncpy(&lat(&body), MAIL_BODY_LENGTH);
    if st.hasdata(6) {
        let mut zeny = getnum(rt, st, 6);
        if zeny < 0 {
            log::error!("buildin_mail: a negative amount of zeny can not be sent.");
            return SCRIPT_CMD_FAILURE;
        } else if zeny as i64 > crate::inventory::MAX_ZENY {
            log::error!("buildin_mail: amount of zeny {zeny} is exceeding maximum of {}. Capping...", crate::inventory::MAX_ZENY);
            zeny = crate::inventory::MAX_ZENY as i32;
        }
        msg.zeny = zeny as u32;
    }
    // Items (a pseudo scope that `break`s out, as rAthena's while loop)
    #[allow(clippy::never_loop)]
    while st.hasdata(7) {
        let Some((name, mut start, end, id, rf)) = mail_sub(rt, st, &mut sd, 7) else { return SCRIPT_CMD_FAILURE };
        let mut num_items = end.wrapping_sub(start);
        if num_items == 0 {
            log::warn!("buildin_mail: array \"{}\" contained no items.", lat(&name));
            break;
        }
        if num_items as usize > MAIL_MAX_ITEM {
            log::warn!("buildin_mail: array \"{}\" contained {num_items} items, capping to maximum of {MAIL_MAX_ITEM}...", lat(&name));
            num_items = MAIL_MAX_ITEM as u32;
        }
        let num_items = num_items as usize;
        let mut i = 0;
        while i < num_items && start < end {
            msg.item[i].nameid = get_val2_num(rt, st, id, start, &rf) as u32;
            msg.item[i].identify = 1;
            if crate::db::items::itemdb_exists(msg.item[i].nameid).is_none() {
                log::error!("buildin_mail: invalid item id {}.", msg.item[i].nameid);
                return SCRIPT_CMD_FAILURE;
            }
            i += 1;
            start += 1;
        }
        // Amounts
        if !st.hasdata(8) {
            log::error!("buildin_mail: missing item count variable at position 8.");
            return SCRIPT_CMD_FAILURE;
        }
        let Some((_, mut start, end, id, rf)) = mail_sub(rt, st, &mut sd, 8) else { return SCRIPT_CMD_FAILURE };
        let mut i = 0;
        while i < num_items && start < end {
            let itm = crate::db::items::itemdb_search(msg.item[i].nameid);
            msg.item[i].amount = get_val2_num(rt, st, id, start, &rf) as i16;
            if msg.item[i].amount <= 0 {
                log::error!("buildin_mail: amount {} for item {} is invalid.", msg.item[i].amount, msg.item[i].nameid);
                return SCRIPT_CMD_FAILURE;
            } else if crate::db::items::itemdb_isstackable2(itm) {
                let max: i32 = if itm.stack.amount > 0 { itm.stack.amount as i32 } else { crate::inventory::MAX_AMOUNT };
                if msg.item[i].amount as i32 > max {
                    log::warn!("buildin_mail: amount {} for item {} is exceeding the maximum of {max}. Capping...", msg.item[i].amount, msg.item[i].nameid);
                    msg.item[i].amount = max as i16;
                }
            } else if msg.item[i].amount > 1 {
                log::warn!("buildin_mail: amount {} is invalid for non-stackable item {}.", msg.item[i].amount, msg.item[i].nameid);
                msg.item[i].amount = 1;
            }
            i += 1;
            start += 1;
        }
        // Refine
        if !st.hasdata(9) {
            break;
        }
        let Some((_, mut start, end, id, rf)) = mail_sub(rt, st, &mut sd, 9) else { return SCRIPT_CMD_FAILURE };
        let mut i = 0;
        while i < num_items && start < end {
            let itm = crate::db::items::itemdb_search(msg.item[i].nameid);
            // (char) cast, then the checks
            let mut refine = get_val2_num(rt, st, id, start, &rf) as i8;
            use crate::db::items::{IT_ARMOR, IT_SHADOWGEAR, IT_WEAPON};
            if !itm.flag.no_refine && matches!(itm.r#type, IT_WEAPON | IT_ARMOR | IT_SHADOWGEAR) {
                if refine as i32 > crate::inventory::MAX_REFINE {
                    refine = crate::inventory::MAX_REFINE as i8;
                }
            } else {
                refine = 0;
            }
            if refine < 0 {
                refine = 0;
            }
            msg.item[i].refine = refine as u8;
            i += 1;
            start += 1;
        }
        // Bound
        if !st.hasdata(10) {
            break;
        }
        let Some((_, mut start, end, id, rf)) = mail_sub(rt, st, &mut sd, 10) else { return SCRIPT_CMD_FAILURE };
        let mut i = 0;
        while i < num_items && start < end {
            let bound = get_val2_num(rt, st, id, start, &rf) as i8;
            if bound < crate::inventory::BOUND_NONE as i8 || bound >= crate::inventory::BOUND_MAX as i8 {
                log::error!("buildin_mail: bound {bound} for item {} is invalid.", msg.item[i].nameid);
                return SCRIPT_CMD_FAILURE;
            }
            msg.item[i].bound = bound as u8;
            i += 1;
            start += 1;
        }
        // Cards
        if !st.hasdata(11) {
            break;
        }
        let mut j = 11;
        for ci in 0..crate::inventory::MAX_SLOTS {
            if !st.hasdata(j) {
                break;
            }
            let Some((_, mut start, end, id, rf)) = mail_sub(rt, st, &mut sd, j) else { return SCRIPT_CMD_FAILURE };
            let mut k = 0;
            while k < num_items && start < end {
                let card = get_val2_num(rt, st, id, start, &rf) as u32;
                msg.item[k].set_card(ci, card);
                if card != 0 && !crate::db::items::itemdb_isspecial(msg.item[k].card0) && crate::db::items::itemdb_exists(card).is_none() {
                    log::error!("buildin_mail: invalid card id {card}.");
                    return SCRIPT_CMD_FAILURE;
                }
                k += 1;
                start += 1;
            }
            j += 1;
        }
        // Random Options
        let mut j = 11 + crate::inventory::MAX_SLOTS as i32;
        if !st.hasdata(j) {
            break;
        }
        let mut oi = 0;
        while oi < crate::inventory::MAX_ITEM_RDM_OPT && st.hasdata(j) && st.hasdata(j + 1) && st.hasdata(j + 2) {
            for part in 0..3 {
                let Some((_, mut start, end, id, rf)) = mail_sub(rt, st, &mut sd, j) else { return SCRIPT_CMD_FAILURE };
                let mut k = 0;
                while k < num_items && start < end {
                    let v = get_val2_num(rt, st, id, start, &rf);
                    let o = &mut msg.item[k].option[oi];
                    match part {
                        0 => o.id = v as i16,
                        1 => o.value = v as i16,
                        _ => o.param = v as i8 as u8,
                    }
                    k += 1;
                    start += 1;
                }
                if part < 2 {
                    j += 1;
                }
            }
            oi += 1;
            j += 1;
        }
        // Break the pseudo scope
        break;
    }
    msg.status = MAIL_NEW;
    msg.kind = MAIL_INBOX_NORMAL;
    msg.timestamp = c.tick() / 1000;
    crate::mail::intif_mail_send(c, 0, msg);
    SCRIPT_CMD_SUCCESS
}

/// openmail {<char id>}: the pre-RODEX mail window; "not supported for PACKETVER 2015-05-13 or newer".
fn openmail(rt: &Rt, st: &mut State) -> i32 {
    if super::charid2sd(rt, st, 2, "buildin_openmail").is_none() {
        return SCRIPT_CMD_FAILURE;
    }
    log::error!("buildin_openmail: This command is not supported for PACKETVER 2015-05-13 or newer.");
    SCRIPT_CMD_FAILURE
}

/// battle_config.feature_auction as the conf files leave it: conf/battle/feature.conf `feature.auction: off`, and
/// battle_adjust_conf forces it to 0 anyway for PACKETVER >= 20141112 (battle.cpp:9027-9031: "not available for
/// clients from 2014-11-12 on"). Only battle_set_value (setbattleflag / @setbattleflag) can switch it on, at run
/// time: auction.rs `feature_auction` is the value in force.
pub fn feature_auction() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::FEATURE_AUCTION)
}

/// openauction {<char id>}: the auction window (script.cpp:20592-20607). With the auction feature off the player gets
/// msg 517 "Auction System is disabled." in red (clif_messagecolor, SELF) and the command succeeds.
fn openauction(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = super::charid2sd(rt, st, 2, "buildin_openauction") else { return SCRIPT_CMD_FAILURE };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let Some(sd) = crate::unit::get(c, aid) else { return SCRIPT_CMD_FAILURE };
    if crate::auction::feature_auction(c) == 0 {
        let msg = crate::net::wire::latin1(crate::data::msg_txt(517)); // Auction System is disabled.
        crate::clif::messagecolor_target(c, &sd, crate::social::COLOR_RED, &msg, false, crate::clif::Target::SelfOnly, None);
        return SCRIPT_CMD_SUCCESS;
    }
    crate::auction::clif_auction_openwindow(c, aid);
    SCRIPT_CMD_SUCCESS
}

/// openbank {<char id>}: the bank window (clif_ui_open OUT_UI_BANK).
fn openbank(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = super::charid2sd(rt, st, 2, "buildin_openbank") else { return SCRIPT_CMD_FAILURE };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    if !crate::cashshop::feature_banking() {
        log::error!("buildin_openbank: banking is disabled.");
        return SCRIPT_CMD_FAILURE;
    }
    crate::bank::clif_ui_open(c, aid, crate::bank::OUT_UI_BANK, 0);
    SCRIPT_CMD_SUCCESS
}

/// buyingstore <slots>: the buying store preparation window (Buy Market Permit and friends).
fn buyingstore(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = rid2sd(rt, st, "buildin_buyingstore") else { return SCRIPT_CMD_SUCCESS };
    let Some(c) = rt.c else { return SCRIPT_CMD_SUCCESS };
    let Some(sd) = crate::unit::get(c, aid) else { return SCRIPT_CMD_SUCCESS };
    if crate::chat::npc_isnear(c, &sd) {
        // conf/battle/player.conf min_npc_vendchat_distance: 3
        let out = crate::data::format_msg(crate::data::msg_txt(662), &[&3]);
        crate::clif::displaymessage(c, &sd, &out);
        return SCRIPT_CMD_SUCCESS;
    }
    let slots = getnum(rt, st, 2);
    crate::buyingstore::buyingstore_setup(c, aid, slots as u8);
    SCRIPT_CMD_SUCCESS
}

/// searchstores <uses>,<effect>{,"<map name>"}: the store search window.
fn searchstores(rt: &Rt, st: &mut State) -> i32 {
    if !crate::searchstore::feature_search_stores() {
        log::error!("buildin_searchstores: Search stores feature is disabled.");
        return SCRIPT_CMD_FAILURE;
    }
    let Some(aid) = rid2sd(rt, st, "buildin_searchstores") else { return SCRIPT_CMD_FAILURE };
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let uses = getnum(rt, st, 2);
    if !(1..=u8::MAX as i32).contains(&uses) {
        log::error!("buildin_searchstores: The amount of uses must be a number between 1 and {}.", u8::MAX);
        return SCRIPT_CMD_FAILURE;
    }
    let effect = getnum(rt, st, 3);
    if effect < crate::searchstore::SEARCHSTORE_EFFECT_NORMAL as i32 || effect >= crate::searchstore::SEARCHSTORE_EFFECT_MAX as i32 {
        log::error!("buildin_searchstores: Invalid effect id {effect}, specified.");
        return SCRIPT_CMD_FAILURE;
    }
    let Some(sd) = crate::unit::get(c, aid) else { return SCRIPT_CMD_FAILURE };
    let m: i16 = if st.hasdata(4) {
        let mapname = lat(&getstr(rt, st, 4));
        if mapname.eq_ignore_ascii_case("all") {
            0
        } else if mapname.eq_ignore_ascii_case("this") {
            sd.map as i16
        } else {
            match crate::data::maps().id_of(&mapname) {
                Some(m) => m as i16,
                None => {
                    log::error!("buildin_searchstores: Invalid map name {mapname}.");
                    return SCRIPT_CMD_FAILURE;
                }
            }
        }
    } else {
        sd.map as i16
    };
    crate::searchstore::searchstore_open(c, aid, uses as u8 as u16, effect as u16, m);
    SCRIPT_CMD_SUCCESS
}
