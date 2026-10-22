//! The stylist (stylist.yml): clif.cpp clif_stylist_response, clif_parse_stylist_buy_sub, clif_parse_stylist_buy,
//! clif_parse_stylist_close; the window is opened by clif_ui_open(OUT_UI_STYLIST) (@stylist, `openstylist`) and the
//! dressing room by clif_dressing_room (`opendressroom`).

use crate::clif::{self, Target};
use crate::db::items::stylist::{self as stylist_db, LOOK_BODY2, LOOK_CLOTHES_COLOR, LOOK_HAIR, LOOK_HAIR_COLOR, LOOK_HEAD_BOTTOM, LOOK_HEAD_MID, LOOK_HEAD_TOP};
use crate::db::jobs::{JOBL_FOURTH, JOBL_THIRD, MAPID_FIRSTMASK, MAPID_SUMMONER};
use crate::inventory as inv;
use crate::net::packets::*;
use crate::unit;
use crate::world::Ctx;

use super::*;

/// clif.cpp clif_stylist_response: ZC_STYLE_CHANGE_RES; a success closes the window (stylist_open = false).
pub fn clif_stylist_response(c: &Ctx, aid: u32, failed: bool) {
    let p = PACKET_ZC_STYLE_CHANGE_RES { PacketType: PACKET_ZC_STYLE_CHANGE_RES::ID as i16, flag: failed as i8 };
    if let Some(sd) = unit::get(c, aid) {
        clif::send(c, &p.to_vec(), &sd, Target::SelfOnly);
    }
    if !failed {
        update(c, aid, |s| s.stylist_open = false);
    }
}

/// clif.cpp clif_parse_stylist_buy_sub: pays one option (its item or item box, and the price) and applies it.
fn buy_sub(c: &Ctx, aid: u32, look: u16, index: i16) -> bool {
    let Some(entry) = stylist_db::stylist_entry(look, index) else { return false };
    let Some(ch) = inv::status(c, aid) else { return false };
    let costs = if inv::class_mapid(&ch) & MAPID_FIRSTMASK == MAPID_SUMMONER { entry.doram.as_ref() } else { entry.human.as_ref() };
    let Some(costs) = costs else { return false };
    if ch.zeny < costs.price {
        return false;
    }
    let mut inventory_index: i16 = -1;
    if costs.required_item != 0 {
        inventory_index = inv::pc_search_inventory(c, aid, costs.required_item);
        if inventory_index < 0 {
            // No other option
            if costs.required_item_box == 0 {
                return false;
            }
            // Check if the box that contains the item is in the inventory
            inventory_index = inv::pc_search_inventory(c, aid, costs.required_item_box);
            // The box containing the item also does not exist
            if inventory_index < 0 {
                return false;
            }
        }
    } else if costs.required_item_box != 0 {
        // (rAthena searches requiredItem here, which is 0: the first empty slot, whose deletion then fails)
        inventory_index = inv::pc_search_inventory(c, aid, costs.required_item);
        if inventory_index < 0 {
            return false;
        }
    }
    if inventory_index >= 0 && inv::pc_delitem(c, aid, inventory_index as i32, 1, 0, 0, crate::map_log::LOG_TYPE_OTHER) != 0 {
        return false;
    }
    if costs.price > 0 && inv::pc_payzeny(c, aid, costs.price as i64, crate::map_log::LOG_TYPE_OTHER, 0) != 0 {
        return false;
    }
    match look {
        LOOK_HAIR | LOOK_HAIR_COLOR | LOOK_CLOTHES_COLOR | LOOK_BODY2 => {
            super::look::pc_changelook(c, aid, look as i32, entry.value as i32);
        }
        LOOK_HEAD_BOTTOM | LOOK_HEAD_MID | LOOK_HEAD_TOP => {
            // the headgear comes by RoDEX mail (the social2 agent's mail.rs)
            use crate::int_mail::{MailMessage, MAIL_INBOX_NORMAL, MAIL_NEW};
            let mut msg = MailMessage::new();
            msg.dest_id = crate::pc::get(c, aid).map(|p| p.char_id).unwrap_or(0);
            msg.send_name = "Styling Shop".to_string();
            msg.title = "<MSG>2949</MSG>".to_string();
            msg.body = "<MSG>2950</MSG>".to_string();
            msg.item[0].nameid = entry.value as _;
            msg.item[0].identify = 1;
            msg.item[0].amount = 1;
            msg.status = MAIL_NEW;
            msg.kind = MAIL_INBOX_NORMAL;
            msg.timestamp = c.tick().div_euclid(1000); // time( nullptr )
            crate::mail::intif_mail_send(c, 0, msg);
        }
        _ => {}
    }
    true
}

/// clif.cpp clif_parse_stylist_buy (CZ_REQ_STYLE_CHANGE2 0x0afc, also 0x0a46): each requested option in turn; the
/// first one that fails answers failure (the ones before it are already paid and applied, as in rAthena). The
/// window's open state is not checked (rAthena does not either).
pub fn clif_parse_stylist_buy(c: &Ctx, aid: u32, pkt: &[u8]) {
    // PACKETVER >= 20180516: the request is read as PACKET_CZ_REQ_STYLE_CHANGE2 whichever id it came with (a shorter
    // 0x0a46 has no BodyStyle: 0 here, what follows in the socket buffer in rAthena)
    let rd = |o: usize| pkt.get(o..o + 2).map(|b| i16::from_le_bytes([b[0], b[1]])).unwrap_or(0);
    let (head_palette, head_style, body_palette, top, mid, bottom, body_style) = (rd(2), rd(4), rd(6), rd(8), rd(10), rd(12), rd(14));
    for (v, look) in [
        (head_palette, LOOK_HAIR_COLOR),
        (head_style, LOOK_HAIR),
        (body_palette, LOOK_CLOTHES_COLOR),
        (top, LOOK_HEAD_TOP),
        (mid, LOOK_HEAD_MID),
        (bottom, LOOK_HEAD_BOTTOM),
    ] {
        if v != 0 && !buy_sub(c, aid, look, v) {
            clif_stylist_response(c, aid, true);
            return;
        }
    }
    if body_style != 0 {
        let class = inv::status(c, aid).map(|ch| inv::class_mapid(&ch)).unwrap_or(0);
        if class & JOBL_THIRD != 0 && class & JOBL_FOURTH == 0 && !buy_sub(c, aid, LOOK_BODY2, body_style) {
            clif_stylist_response(c, aid, true);
            return;
        }
    }
    clif_stylist_response(c, aid, false);
}

/// clif.cpp clif_parse_stylist_close (CZ_STYLE_CLOSE 0x0a48).
pub fn clif_parse_stylist_close(c: &Ctx, aid: u32) {
    update(c, aid, |s| s.stylist_open = false);
}

/// clif.cpp clif_dressing_room: ZC_DRESSROOM_OPEN (view 0, ignored by the client).
pub fn clif_dressing_room(c: &Ctx, aid: u32) {
    let p = PACKET_ZC_DRESSROOM_OPEN { packetType: PACKET_ZC_DRESSROOM_OPEN::ID as i16, view: 0 };
    if let Some(sd) = unit::get(c, aid) {
        clif::send(c, &p.to_vec(), &sd, Target::SelfOnly);
    }
}
