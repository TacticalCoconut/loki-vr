//! The attendance system: pc.cpp pc_attendance_period / pc_attendance_enabled / pc_attendance_rewarded_today /
//! pc_attendance_counter / pc_attendance_claim_reward (the AttendanceDatabase replay is db/jobs.rs attendance_db),
//! the login check in pc_scdata_received ([`pc_scdata_received`]), and clif.cpp clif_ui_open (ZC_UI_OPEN 0x0ae2),
//! clif_parse_open_ui (CZ_UI_OPEN 0x0a68), clif_parse_attendance_request (CZ_REQ_ATTENDANCE_REWARD 0x0aef) and
//! clif_attendence_response (ZC_ACK_ATTENDANCE_REWARD 0x0af0).
//!
//! The day counter and the date of the last reward are the account registries #AttendanceCounter and
//! #AttendanceDate (pc_readreg2 / pc_setreg2, reputation.rs). The reward itself is a RoDEX mail (intif_Mail_send,
//! the social2 agent's mail.rs).

use crate::db::jobs::{pc_attendance_period as period_on, AttendancePeriod};
use crate::reputation::{pc_readreg2, pc_setreg2};
use crate::world::Ctx;

/// pc.hpp ATTENDANCE_DATE_VAR, ATTENDANCE_COUNT_VAR.
const ATTENDANCE_DATE_VAR: &str = "#AttendanceDate";
const ATTENDANCE_COUNT_VAR: &str = "#AttendanceCounter";

/// conf/battle/feature.conf `feature.attendance: on`.
fn feature_attendance() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::FEATURE_ATTENDANCE) != 0
}

/// map.hpp save_settings (conf/map_athena.conf `save_settings: 4095`) has CHARSAVE_ATTENDANCE (0x100).
const CHARSAVE_ATTENDANCE: bool = true;

/// clif.hpp enum out_ui_type / in_ui_type.
pub const OUT_UI_BANK: i8 = 0;
pub const OUT_UI_STYLIST: i8 = 1;
pub const OUT_UI_CAPTCHA: i8 = 2;
pub const OUT_UI_MACRO: i8 = 3;
pub const OUT_UI_TIP: i8 = 5;
pub const OUT_UI_QUEST: i8 = 6;
pub const OUT_UI_ATTENDANCE: i8 = 7;
pub const OUT_UI_ENCHANTGRADE: i8 = 8;
pub const OUT_UI_ENCHANT: i8 = 10;
pub const IN_UI_MACRO_REGISTER: i8 = 2;
pub const IN_UI_MACRO_DETECTOR: i8 = 3;
pub const IN_UI_ATTENDANCE: i8 = 5;

/// clif.hpp e_clif_messages MSI_CHECK_ATTENDANCE_NOT_EVENT.
const MSI_CHECK_ATTENDANCE_NOT_EVENT: u16 = 3474;

/// date_get(DT_YYYYMMDD): the server's local date.
fn today(c: &Ctx) -> u32 {
    let tm = crate::script::npc_run::localtime(c.tick().div_euclid(1000));
    ((tm.year + 1900) * 10000 + (tm.mon + 1) * 100 + tm.mday) as u32
}

/// pc.cpp pc_attendance_period: the period running today (the first one in the database's iteration order).
pub fn pc_attendance_period(c: &Ctx) -> Option<AttendancePeriod> {
    period_on(today(c))
}

/// pc.cpp pc_attendance_enabled.
pub fn pc_attendance_enabled(c: &Ctx) -> bool {
    // Check if the attendance feature is disabled
    if !feature_attendance() {
        return false;
    }
    // Check if there is a running attendance period
    pc_attendance_period(c).is_some()
}

/// pc.cpp pc_attendance_rewarded_today.
fn pc_attendance_rewarded_today(c: &Ctx, aid: u32) -> bool {
    pc_readreg2(c, aid, ATTENDANCE_DATE_VAR) >= today(c) as i64
}

/// pc.cpp pc_attendance_counter: 10 × the days rewarded in this period, + 1 when today's was taken (a counter left
/// from an earlier period is reset to 0).
pub fn pc_attendance_counter(c: &Ctx, aid: u32) -> i32 {
    // No running attendance period
    let Some(period) = pc_attendance_period(c) else { return 0 };
    // Get the counter for the current period
    let counter = pc_readreg2(c, aid, ATTENDANCE_COUNT_VAR) as i32;
    // Check if we have a remaining counter from a previous period
    if counter > 0 && pc_readreg2(c, aid, ATTENDANCE_DATE_VAR) < period.start as i64 {
        // Reset the counter to zero
        pc_setreg2(c, aid, ATTENDANCE_COUNT_VAR, 0);
        return 0;
    }
    10 * counter + if pc_attendance_rewarded_today(c, aid) { 1 } else { 0 }
}

/// pc.cpp pc_attendance_claim_reward: today's reward (the counter's day of the period) is mailed.
pub fn pc_attendance_claim_reward(c: &Ctx, aid: u32) {
    // If the user's group does not have the permission
    if !crate::pc::has_permission(c, aid, crate::rules::PC_PERM_ATTENDANCE) {
        return;
    }
    // Check if the attendance feature is disabled
    if !pc_attendance_enabled(c) {
        return;
    }
    // Check if the user already got his reward today
    if pc_attendance_rewarded_today(c, aid) {
        return;
    }
    let attendance_counter = pc_readreg2(c, aid, ATTENDANCE_COUNT_VAR) as i32 + 1;
    let Some(period) = pc_attendance_period(c) else { return };
    // size_t < int32: the counter is converted to size_t
    if (period.rewards.len() as u64) < attendance_counter as i64 as u64 {
        return;
    }
    pc_setreg2(c, aid, ATTENDANCE_DATE_VAR, today(c) as i64);
    pc_setreg2(c, aid, ATTENDANCE_COUNT_VAR, attendance_counter as i64);
    if CHARSAVE_ATTENDANCE {
        crate::chrif::chrif_save(c, aid, crate::chrif::CSAVE_NORMAL);
    }
    let reward = period.rewards.get(&((attendance_counter - 1) as u32)).cloned().unwrap_or(crate::db::jobs::AttendanceReward { item_id: 0, amount: 0 });
    // the reward mail (RoDEX: social2 agent, 678e275)
    use crate::int_mail::{MailMessage, MAIL_BODY_LENGTH, MAIL_INBOX_NORMAL, MAIL_NEW, MAIL_TITLE_LENGTH, NAME_LENGTH};
    let cut = |s: String, n: usize| -> String { s.chars().take(n.saturating_sub(1)).collect() };
    let mut msg = MailMessage::new();
    msg.dest_id = crate::pc::get(c, aid).map(|p| p.char_id).unwrap_or(0);
    msg.send_name = cut(crate::data::msg_txt(788).to_string(), NAME_LENGTH);
    msg.title = cut(crate::data::format_msg(crate::data::msg_txt(789), &[&attendance_counter]), MAIL_TITLE_LENGTH);
    msg.body = cut(crate::data::format_msg(crate::data::msg_txt(790), &[&attendance_counter]), MAIL_BODY_LENGTH);
    msg.item[0].nameid = reward.item_id;
    msg.item[0].amount = reward.amount as i16;
    msg.item[0].identify = 1;
    msg.status = MAIL_NEW;
    msg.kind = MAIL_INBOX_NORMAL;
    msg.timestamp = c.tick().div_euclid(1000);
    crate::mail::intif_mail_send(c, 0, msg);
    clif_attendence_response(c, aid, attendance_counter);
}

/// pc.cpp pc_scdata_received's attendance part: the window opens at login while today's reward is waiting.
pub fn pc_scdata_received(c: &Ctx, aid: u32) {
    if crate::pc::has_permission(c, aid, crate::rules::PC_PERM_ATTENDANCE)
        && pc_attendance_enabled(c)
        && !pc_attendance_rewarded_today(c, aid)
        && pc_attendance_counter(c, aid) < 200
    {
        let counter = pc_attendance_counter(c, aid);
        clif_ui_open(c, aid, OUT_UI_ATTENDANCE, counter);
    }
}

// ------------------------------------------------------------------------------------------------ clif.cpp

/// clif.cpp clif_ui_open: the one port is itemui's (it also sets sd.state.stylist_open / enchantgrade_open).
pub use crate::itemui::clif_ui_open;

/// clif.cpp clif_parse_open_ui (CZ_UI_OPEN 0x0a68 type.B).
pub fn parse_open_ui(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(t) = pkt.get(2).map(|&b| b as i8) else { return };
    match t {
        IN_UI_ATTENDANCE => {
            let Some(u) = crate::unit::get(c, aid) else { return };
            if !crate::pc::has_permission(c, aid, crate::rules::PC_PERM_ATTENDANCE) {
                // You are not allowed to use the attendance system.
                let m = crate::net::wire::latin1z(crate::data::msg_txt(791));
                crate::clif::messagecolor_target(c, &u, crate::social::COLOR_RED, &m, false, crate::clif::Target::SelfOnly, None);
            } else if pc_attendance_enabled(c) {
                let counter = pc_attendance_counter(c, aid);
                clif_ui_open(c, aid, OUT_UI_ATTENDANCE, counter);
            } else {
                crate::clif_items::msg_color(c, &u, MSI_CHECK_ATTENDANCE_NOT_EVENT, crate::social::COLOR_RED);
            }
        }
        // PACKETVER >= 20160316
        IN_UI_MACRO_REGISTER => clif_ui_open(c, aid, OUT_UI_CAPTCHA, 0),
        IN_UI_MACRO_DETECTOR => clif_ui_open(c, aid, OUT_UI_MACRO, 0),
        _ => {}
    }
}

/// clif.cpp clif_parse_attendance_request (CZ_REQ_ATTENDANCE_REWARD 0x0aef).
pub fn parse_attendance_request(c: &Ctx, aid: u32) {
    pc_attendance_claim_reward(c, aid);
}

/// clif.cpp clif_attendence_response: 0x0af0 unknown.L (0) data.L (written to the player's fd).
pub fn clif_attendence_response(c: &Ctx, aid: u32, data: i32) {
    let mut w = Vec::with_capacity(10);
    w.extend_from_slice(&0x0af0u16.to_le_bytes());
    w.extend_from_slice(&0u32.to_le_bytes());
    w.extend_from_slice(&data.to_le_bytes());
    if let Some(id) = crate::pc::identity_of(c, aid) {
        c.send(id, &w);
    }
}
