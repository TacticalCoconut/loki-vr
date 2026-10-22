//! The macro detector (captcha checks against bots): pc.cpp pc_macro_captcha_register … pc_macro_reporter_process
//! (rAthena e985006 pc.cpp:15700-15975) and clif.cpp's captcha / macro-detector packets clif_parse_captcha_register …
//! clif_macro_reporter_status (clif.cpp:25128-25390), PACKETVER 20211103.
//!
//! A GM registers captcha images with /macro_register (CZ_REQ_UPLOAD_MACRO_DETECTOR, then the image in chunks); a GM
//! or @macrochecker / the `macro_detector` script command sends a player a random captcha, whose actions are blocked
//! until the answer is right; wrong answers and timeouts use up macro_detection_retry tries, then the player is banned
//! or jailed (macro_detection_punishment).
//!
//! Deliberately different: rAthena also loads captchas from db/captcha_db.yml (it lists none, and its BMP files and
//! `Bonus` scripts are read from disk); the module has no filesystem, so captchas come only from /macro_register and
//! never carry a bonus script. The captcha database keeps rAthena's unordered_map<int16> iteration order
//! (skill/umap.rs), which umap_random walks.

use spacetimedb::{table, Table};

use crate::clif::{self, Target};
use crate::net::packets::*;
use crate::net::wire::{latin1, until_nul, Reader};
use crate::skill::umap::Umap;
use crate::timer::{self, Func};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// pc.hpp CAPTCHA_ANSWER_SIZE_MIN.
pub const CAPTCHA_ANSWER_SIZE_MIN: usize = 1;
/// pc.hpp CAPTCHA_ANSWER_SIZE_MAX (the answer's char array, NUL included).
pub const CAPTCHA_ANSWER_SIZE_MAX: usize = 16;
/// pc.hpp CAPTCHA_BMP_SIZE: sizeof("BM") + sizeof(BITMAPV2INFOHEADER) + a 24-bit 220x90 BMP.
pub const CAPTCHA_BMP_SIZE: usize = 2 + 52 + (3 * 220 * 90);
/// pc.hpp MAX_CAPTCHA_CHUNK_SIZE.
pub const MAX_CAPTCHA_CHUNK_SIZE: usize = 1024;

/// conf/battle/client.conf `macro_detection_retry: 3`.
pub fn macro_detection_retry() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MACRO_DETECTION_RETRY)
}
/// conf/battle/client.conf `macro_detection_timeout: 60000` (ms per answer).
pub fn macro_detection_timeout() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MACRO_DETECTION_TIMEOUT)
}
/// conf/battle/client.conf `macro_detection_punishment: 0` (0 ban, 1 jail).
pub fn macro_detection_punishment() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MACRO_DETECTION_PUNISHMENT)
}
/// conf/battle/client.conf `macro_detection_punishment_time: 0` (minutes; 0 = a block / an endless jail).
pub fn macro_detection_punishment_time() -> i32 {
    crate::battle_config::get(crate::battle_config::Bc::MACRO_DETECTION_PUNISHMENT_TIME)
}

/// pc.hpp e_macro_detect_status.
pub const MCD_TIMEOUT: i32 = 0;
pub const MCD_INCORRECT: i32 = 1;
pub const MCD_GOOD: i32 = 2;
/// pc.hpp e_macro_report_status.
pub const MCR_MONITORING: i32 = 0;
pub const MCR_NO_DATA: i32 = 1;
pub const MCR_INPROGRESS: i32 = 2;

/// captcha_db (pc.hpp CaptchaDatabase, TypesafeYamlDatabase<int16, s_captcha_data>): one s_captcha_data per index.
#[table(accessor = captcha)]
pub struct Captcha {
    #[primary_key]
    pub index: u16,
    /// cd->image_data[0..image_size].
    pub image_data: Vec<u8>,
    /// cd->captcha_answer (without its NUL).
    pub answer: Vec<u8>,
}

/// captcha_db's hash table order (its data is a std::unordered_map; skill/umap.rs): one row, key 0.
#[table(accessor = captcha_order)]
pub struct CaptchaOrder {
    #[primary_key]
    pub key: u8,
    pub order: Vec<u16>,
    pub buckets: u64,
    pub next_resize: u64,
}

/// map_session_data's captcha_upload and macro_detect (pc.hpp), for a player that has either; no row = both empty
/// (cd nullptr, upload_size 0, retry 0, timer INVALID_TIMER: pc.cpp:2129).
#[table(accessor = pc_macro)]
#[derive(Clone, Default)]
pub struct PcMacro {
    #[primary_key]
    pub aid: u32,
    /// sd.captcha_upload.cd != nullptr: a captcha being uploaded (its image_size, answer and image so far).
    pub upload_cd: bool,
    pub upload_image_size: u16,
    pub upload_answer: Vec<u8>,
    pub upload_image: Vec<u8>,
    /// sd.captcha_upload.upload_size.
    pub upload_size: u16,
    /// sd.macro_detect.cd != nullptr: the captcha this player must answer (rAthena holds a shared_ptr to it; the row
    /// keeps its image and answer).
    pub detect_cd: bool,
    pub detect_image: Vec<u8>,
    pub detect_answer: Vec<u8>,
    /// sd.macro_detect.reporter_aid.
    pub reporter_aid: i32,
    /// sd.macro_detect.retry.
    pub retry: i32,
    /// sd.macro_detect.timer (0 = INVALID_TIMER).
    pub timer: u64,
}

fn get(c: &Ctx, aid: u32) -> PcMacro {
    c.db().pc_macro().aid().find(aid).unwrap_or(PcMacro { aid, ..Default::default() })
}

fn put(c: &Ctx, m: PcMacro) {
    let empty = !m.upload_cd && m.upload_size == 0 && !m.detect_cd && m.reporter_aid == 0 && m.retry == 0 && m.timer == 0;
    let exists = c.db().pc_macro().aid().find(m.aid).is_some();
    match (exists, empty) {
        (true, true) => {
            c.db().pc_macro().aid().delete(m.aid);
        }
        (true, false) => {
            c.db().pc_macro().aid().update(m);
        }
        (false, false) => {
            c.db().pc_macro().insert(m);
        }
        (false, true) => {}
    }
}

/// `sd.macro_detect = {}; sd.macro_detect.timer = INVALID_TIMER;` (the upload state is left as it is).
fn clear_detect(m: &mut PcMacro) {
    m.detect_cd = false;
    m.detect_image.clear();
    m.detect_answer.clear();
    m.reporter_aid = 0;
    m.retry = 0;
    m.timer = 0;
}

/// A captcha answer as strcmp / strlen see it: up to the first NUL.
fn answer_bytes(s: &str) -> Vec<u8> {
    until_nul(&latin1(s)).to_vec()
}

fn order(c: &Ctx) -> Umap {
    c.db().captcha_order().key().find(0).map(|r| Umap { order: r.order, buckets: r.buckets, next_resize: r.next_resize }).unwrap_or_default()
}

fn put_order(c: &Ctx, u: Umap) {
    let row = CaptchaOrder { key: 0, order: u.order, buckets: u.buckets, next_resize: u.next_resize };
    if c.db().captcha_order().key().find(0).is_some() {
        c.db().captcha_order().key().update(row);
    } else {
        c.db().captcha_order().insert(row);
    }
}

/// captcha_db.empty().
pub fn captcha_db_empty(c: &Ctx) -> bool {
    c.db().captcha().count() == 0
}

/// captcha_db.random() (database.hpp:137, utilities.hpp umap_random): the entry at `rnd_value<size_t>(0, size - 1)`
/// steps from the unordered_map's begin().
fn captcha_db_random(c: &Ctx) -> Option<Captcha> {
    let o = order(c);
    if o.order.is_empty() {
        return None;
    }
    let i = c.rnd_value_i64(0, o.order.len() as i64 - 1) as usize;
    c.db().captcha().index().find(o.order[i])
}

// ---------------------------------------------------------------------------------------------- pc.cpp

/// pc.cpp pc_macro_captcha_register: a captcha (answer, image size) whose image the client uploads next.
pub fn pc_macro_captcha_register(c: &Ctx, aid: u32, image_size: u16, captcha_answer: &[u8]) {
    let mut m = get(c, aid);
    m.upload_cd = false;
    m.upload_image_size = 0;
    m.upload_answer.clear();
    m.upload_image.clear();
    m.upload_size = 0;
    if captcha_answer.len() < CAPTCHA_ANSWER_SIZE_MIN || image_size == 0 || image_size as usize > CAPTCHA_BMP_SIZE {
        put(c, m);
        clif_captcha_upload_request(c, aid); // Notify client of failure.
        return;
    }
    m.upload_cd = true;
    m.upload_image_size = image_size;
    // safestrncpy(cd->captcha_answer, captcha_answer, sizeof(cd->captcha_answer))
    m.upload_answer = captcha_answer[..captcha_answer.len().min(CAPTCHA_ANSWER_SIZE_MAX - 1)].to_vec();
    // memset(cd->image_data, 0, sizeof(cd->image_data)): the image_size bytes that are ever read
    m.upload_image = vec![0; image_size as usize];
    put(c, m);
    // Request the image data from the client.
    clif_captcha_upload_request(c, aid);
}

/// pc.cpp pc_macro_captcha_register_upload: one chunk of the image; the last one stores the captcha under the first
/// free index.
pub fn pc_macro_captcha_register_upload(c: &Ctx, aid: u32, upload_data: &[u8]) {
    let mut m = get(c, aid);
    let at = m.upload_size as usize;
    // memcpy(&cd->image_data[upload_size], upload_data, upload_size): the caller keeps it inside image_size
    m.upload_image[at..at + upload_data.len()].copy_from_slice(upload_data);
    m.upload_size += upload_data.len() as u16;
    // Notify that the image finished uploading.
    if m.upload_size == m.upload_image_size {
        put(c, m.clone());
        // Tell the client that the upload was finished
        clif_captcha_upload_end(c, aid);
        // Look for a free key
        let index = (0..u16::MAX).find(|&i| c.db().captcha().index().find(i).is_none());
        let image = std::mem::take(&mut m.upload_image);
        let answer = std::mem::take(&mut m.upload_answer);
        m.upload_cd = false;
        m.upload_image_size = 0;
        m.upload_size = 0;
        put(c, m);
        let Some(index) = index else {
            return; // no free key found...
        };
        // captcha_db.put(index, cd)
        c.db().captcha().insert(Captcha { index, image_data: image, answer });
        let mut o = order(c);
        o.insert(index);
        put_order(c, o);
        // TODO: write YAML and BMP file? (rAthena's own note: it keeps the captcha in memory only, as here)
        return;
    }
    put(c, m);
}

/// pc.cpp pc_macro_detector_timeout (TIMER_FUNC): one try used up; the last one punishes, otherwise the answer window
/// is shown again with a new timeout.
pub fn pc_macro_detector_timeout(c: &Ctx, aid: u32) {
    // map_id2sd(id); nullpo_ret(sd)
    if crate::pc::get(c, aid).is_none() {
        return;
    }
    let mut m = get(c, aid);
    // Remove the current timer
    m.timer = 0;
    // Deduct an answering attempt
    m.retry -= 1;
    if m.retry == 0 {
        put(c, m);
        // All attempts have been exhausted, punish the user
        pc_macro_punishment(c, aid, MCD_TIMEOUT);
    } else {
        put(c, m.clone());
        // Update the client
        clif_macro_detector_request_show(c, aid);
        // Start a new timer
        m.timer = timer::add(c, c.tick() + macro_detection_timeout() as Tick, Func::PcMacroDetectorTimeout, aid, 0);
        put(c, m);
    }
}

/// pc.cpp pc_macro_detector_process_answer.
pub fn pc_macro_detector_process_answer(c: &Ctx, aid: u32, captcha_answer: &[u8]) {
    let mut m = get(c, aid);
    // Has no captcha request
    if !m.detect_cd {
        return;
    }
    // Correct answer
    if captcha_answer == m.detect_answer.as_slice() {
        // Delete the timer
        if m.timer != 0 {
            timer::delete(c, m.timer);
        }
        // Clear the macro detect data
        clear_detect(&mut m);
        put(c, m.clone());
        // Unblock all actions for the player
        if let Some(mut p) = crate::pc::get(c, aid) {
            p.block_action &= !crate::pc::PCBLOCK_ALL;
            p.block_action &= !crate::pc::PCBLOCK_IMMUNE;
            crate::pc::put(c, &p);
        }
        // Assign temporary macro variable to check failures (after the clear above, as rAthena: retry is 0 here, so
        // this is always macro_detection_retry)
        crate::script::reg::pc_setreg(c, aid, "@captcha_retries", (macro_detection_retry() - m.retry) as i64);
        // Grant bonuses via script: cd->bonus_script comes only from db/captcha_db.yml, which the module does not
        // read (see the file comment), so there is none.
        // Notify the client
        clif_macro_detector_status(c, aid, MCD_GOOD);
    } else {
        // Deduct an answering attempt
        m.retry -= 1;
        // All attempts have been exhausted, punish the user
        if m.retry <= 0 {
            put(c, m);
            pc_macro_punishment(c, aid, MCD_INCORRECT);
            return;
        }
        let tid = m.timer;
        put(c, m);
        // Incorrect response, update the client
        clif_macro_detector_request_show(c, aid);
        // Reset the timer: addtick_timer(timer, gettick() + timeout) adds to the timer's own tick (timer.cpp:323-326)
        timer::addtick(c, tid, c.tick() + macro_detection_timeout() as Tick);
    }
}

/// pc.cpp pc_macro_detector_disconnect (map.cpp:2328 map_quit): leaving during a check is punished as a timeout.
pub fn pc_macro_detector_disconnect(c: &Ctx, aid: u32) {
    let mut m = get(c, aid);
    // Delete the timeout timer
    if m.timer != 0 {
        timer::delete(c, m.timer);
        m.timer = 0;
    }
    let retry = m.retry;
    put(c, m);
    // If the player disconnects before clearing the challenge the player is punished.
    if retry != 0 {
        pc_macro_punishment(c, aid, MCD_TIMEOUT);
    }
    // the session's captcha_upload / macro_detect die with it (unit_free_pc)
    c.db().pc_macro().aid().delete(aid);
}

/// pc.cpp pc_macro_punishment: a ban (or block) through the login server, or a jail.
fn pc_macro_punishment(c: &Ctx, aid: u32, stype: i32) {
    let mut duration = 0;
    // Determine if there's a unique duration
    if macro_detection_punishment_time() > 0 {
        let time = format!("+{}nm", macro_detection_punishment_time());
        duration = crate::atcommand_atcmd::accounts::solve_time(c, time.as_bytes());
    }
    let mut m = get(c, aid);
    // Delete the timer
    if m.timer != 0 {
        timer::delete(c, m.timer);
    }
    let reporter_aid = m.reporter_aid;
    // Clear the macro detect data
    clear_detect(&mut m);
    put(c, m);
    // Unblock all actions for the player
    if let Some(mut p) = crate::pc::get(c, aid) {
        p.block_action &= !crate::pc::PCBLOCK_ALL;
        p.block_action &= !crate::pc::PCBLOCK_IMMUNE;
        crate::pc::put(c, &p);
    }
    if macro_detection_punishment() == 0 {
        // Ban
        clif_macro_detector_status(c, aid, stype);
        let name = crate::unit::get(c, aid).map(|u| latin1(&u.name)).unwrap_or_default();
        let op = if duration == 0 { crate::atcommand_atcmd::accounts::CHRIF_OP_LOGIN_BLOCK } else { crate::atcommand_atcmd::accounts::CHRIF_OP_LOGIN_BAN };
        crate::atcommand_atcmd::accounts::chrif_req_login_operation(c, reporter_aid, &name, op, duration, 0, 0);
    } else {
        // Jail: send success to close the window without closing the client
        clif_macro_detector_status(c, aid, MCD_GOOD);
        crate::atcommand_atcmd::world::pc_jail(c, aid, if duration == 0 { i32::MAX } else { duration / 60 });
    }
}

/// pc.cpp pc_macro_reporter_area_select: the players around (x, y) for the /macro_detector area selection.
pub fn pc_macro_reporter_area_select(c: &Ctx, aid: u32, x: i16, y: i16, radius: i8) {
    let Some(sd) = crate::unit::get(c, aid) else { return };
    let r = radius as i32;
    let (x0, y0, x1, y1) = ((x as i32 - r) as i16, (y as i32 - r) as i16, (x as i32 + r) as i16, (y as i32 + r) as i16);
    let aid_list: Vec<u32> = foreachinarea(c, sd.map, x0, y0, x1, y1, crate::map::BL_PC).iter().map(|u| u.id).collect();
    clif_macro_reporter_select(c, &sd, &aid_list);
}

/// map.cpp map_foreachinarea's selection: map_foreachinareaV with wall_check = battle_config.skill_wall_check (the
/// cells reachable in a straight line from the area's centre, map.cpp:863-874) over map_foreachinallarea's.
fn foreachinarea(c: &Ctx, m: u16, x0: i16, y0: i16, x1: i16, y1: i16, kinds: u16) -> Vec<Unit> {
    let units = crate::map::units_in_area(c, m, x0, y0, x1, y1, kinds);
    if !crate::skill::conf::skill_wall_check() {
        return units;
    }
    let Some(md) = crate::data::maps().get(m) else { return units };
    let (mut x0, mut y0, mut x1, mut y1) = (x0, y0, x1, y1);
    if x1 < x0 {
        std::mem::swap(&mut x0, &mut x1);
    }
    if y1 < y0 {
        std::mem::swap(&mut y0, &mut y1);
    }
    let (x0, y0, x1, y1) = (x0.max(0), y0.max(0), x1.min(md.xs - 1), y1.min(md.ys - 1));
    let cx = x0 + (x1 - x0) / 2;
    let cy = y0 + (y1 - y0) / 2;
    units.into_iter().filter(|u| crate::path::path_search_long(m, cx, cy, u.x, u.y, crate::map::CellChk::ChkWall)).collect()
}

/// pc.cpp pc_macro_reporter_process: a random captcha for `aid`, its actions blocked until it is answered.
/// `reporter_account_id` -1 is the server (pc.hpp's default argument, buildin macro_detector).
pub fn pc_macro_reporter_process(c: &Ctx, aid: u32, reporter_account_id: i32) {
    if captcha_db_empty(c) {
        return;
    }
    // Pick a random image from the database.
    let Some(cd) = captcha_db_random(c) else { return };
    // Set macro detection data.
    let mut m = get(c, aid);
    m.detect_cd = true;
    m.detect_image = cd.image_data;
    m.detect_answer = cd.answer;
    m.reporter_aid = reporter_account_id;
    m.retry = macro_detection_retry();
    put(c, m.clone());
    // Block all actions for the target player.
    if let Some(mut p) = crate::pc::get(c, aid) {
        p.block_action |= crate::pc::PCBLOCK_ALL | crate::pc::PCBLOCK_IMMUNE;
        crate::pc::put(c, &p);
    }
    // Open macro detect client side.
    clif_macro_detector_request(c, aid);
    // Start the timeout timer.
    m.timer = timer::add(c, c.tick() + macro_detection_timeout() as Tick, Func::PcMacroDetectorTimeout, aid, 0);
    put(c, m);
}

// ---------------------------------------------------------------------------------------------- clif.cpp

fn self_send(c: &Ctx, aid: u32, bytes: &[u8]) {
    if let Some(sd) = crate::unit::get(c, aid) {
        clif::send(c, bytes, &sd, Target::SelfOnly);
    }
}

fn no_permission(c: &Ctx, aid: u32) {
    if let Some(sd) = crate::unit::get(c, aid) {
        clif::displaymessage(c, &sd, crate::data::msg_txt(246)); // Your GM level doesn't authorize you to perform this action.
    }
}

/// The packets of this file (net/mod.rs dispatch).
pub fn parse(c: &Ctx, aid: u32, func: &str, pkt: &[u8]) -> bool {
    match func {
        "clif_parse_captcha_register" => clif_parse_captcha_register(c, aid, pkt),
        "clif_parse_captcha_upload" => clif_parse_captcha_upload(c, aid, pkt),
        "clif_parse_captcha_preview_request" => clif_parse_captcha_preview_request(c, aid, pkt),
        "clif_parse_macro_detector_download_ack" => clif_parse_macro_detector_download_ack(c, aid),
        "clif_parse_macro_detector_answer" => clif_parse_macro_detector_answer(c, aid, pkt),
        "clif_parse_macro_reporter_select" => clif_parse_macro_reporter_select(c, aid, pkt),
        "clif_parse_macro_reporter_ack" => clif_parse_macro_reporter_ack(c, aid, pkt),
        _ => return false,
    }
    true
}

/// clif.cpp clif_parse_captcha_register (CZ_REQ_UPLOAD_MACRO_DETECTOR 0x0a52).
fn clif_parse_captcha_register(c: &Ctx, aid: u32, pkt: &[u8]) {
    if !crate::pc::has_permission(c, aid, crate::rules::PC_PERM_MACRO_REGISTER) {
        no_permission(c, aid);
        return;
    }
    let Some(p) = PACKET_CZ_REQ_UPLOAD_MACRO_DETECTOR::read(&mut Reader::new(pkt)) else { return };
    pc_macro_captcha_register(c, aid, p.imageSize, &answer_bytes(&p.answer));
}

/// clif.cpp clif_captcha_upload_request (ZC_ACK_UPLOAD_MACRO_DETECTOR 0x0a53): flag 0 = send the image, 1 = refused.
fn clif_captcha_upload_request(c: &Ctx, aid: u32) {
    let flag = if get(c, aid).upload_cd { 0 } else { 1 };
    let p = PACKET_ZC_ACK_UPLOAD_MACRO_DETECTOR { PacketType: PACKET_ZC_ACK_UPLOAD_MACRO_DETECTOR::ID as i16, captchaKey: String::new(), captchaFlag: flag };
    self_send(c, aid, &p.to_vec());
}

/// clif.cpp clif_parse_captcha_upload (CZ_UPLOAD_MACRO_DETECTOR_CAPTCHA 0x0a54): one image chunk. rAthena reads
/// sd->captcha_upload.cd->image_size without checking cd, so a chunk without a registration first crashes its map
/// server; here it is ignored.
fn clif_parse_captcha_upload(c: &Ctx, aid: u32, pkt: &[u8]) {
    if !crate::pc::has_permission(c, aid, crate::rules::PC_PERM_MACRO_REGISTER) {
        no_permission(c, aid);
        return;
    }
    let Some(p) = PACKET_CZ_UPLOAD_MACRO_DETECTOR_CAPTCHA::read(&mut Reader::new(pkt)) else { return };
    // int16 upload_size = p->PacketLength - sizeof(*p)
    let upload_size = p.PacketLength as i32 - PACKET_CZ_UPLOAD_MACRO_DETECTOR_CAPTCHA::SIZE as i32;
    if upload_size < 1 || upload_size > MAX_CAPTCHA_CHUNK_SIZE as i32 {
        return;
    }
    let m = get(c, aid);
    if !m.upload_cd {
        return; // (rAthena: nullptr dereference)
    }
    if m.upload_size as i32 + upload_size > m.upload_image_size as i32 {
        return;
    }
    let data = &p.imageData[..(upload_size as usize).min(p.imageData.len())];
    if data.len() != upload_size as usize {
        return; // a PacketLength beyond the bytes received (the dispatcher already framed the packet by it)
    }
    pc_macro_captcha_register_upload(c, aid, data);
}

/// clif.cpp clif_captcha_upload_end (ZC_COMPLETE_UPLOAD_MACRO_DETECTOR_CAPTCHA 0x0a55).
fn clif_captcha_upload_end(c: &Ctx, aid: u32) {
    let p = PACKET_ZC_COMPLETE_UPLOAD_MACRO_DETECTOR_CAPTCHA { PacketType: PACKET_ZC_COMPLETE_UPLOAD_MACRO_DETECTOR_CAPTCHA::ID as i16 };
    self_send(c, aid, &p.to_vec());
}

/// clif.cpp clif_parse_captcha_preview_request (CZ_REQ_PREVIEW_MACRO_DETECTOR 0x0a69).
fn clif_parse_captcha_preview_request(c: &Ctx, aid: u32, pkt: &[u8]) {
    if !(crate::pc::has_permission(c, aid, crate::rules::PC_PERM_MACRO_REGISTER) && crate::pc::has_permission(c, aid, crate::rules::PC_PERM_MACRO_DETECT)) {
        no_permission(c, aid);
        return;
    }
    let Some(p) = PACKET_CZ_REQ_PREVIEW_MACRO_DETECTOR::read(&mut Reader::new(pkt)) else { return };
    // captcha_db.find(p->captchaID) (clif.cpp:25202): the packet's int converts to the database's int16 key type, so
    // only its low 16 bits count (65536 previews captcha 0, -65535 captcha 1: parity testrig/parity/ggm macro_reg);
    // the keys here are the same 16 bits read as uint16 (pc_macro_captcha_register_upload's index)
    let cd = c.db().captcha().index().find(p.captchaID as u16);
    clif_captcha_preview_response(c, aid, cd.as_ref());
}

/// The image in MAX_CAPTCHA_CHUNK_SIZE pieces (clif_captcha_preview_response / clif_macro_detector_request's loop).
fn image_chunks(image: &[u8]) -> impl Iterator<Item = &[u8]> {
    image.chunks(MAX_CAPTCHA_CHUNK_SIZE)
}

/// clif.cpp clif_captcha_preview_response (ZC_ACK_PREVIEW_MACRO_DETECTOR 0x0a6a, then the image in
/// ZC_PREVIEW_MACRO_DETECTOR_CAPTCHA 0x0a6b chunks).
fn clif_captcha_preview_response(c: &Ctx, aid: u32, cd: Option<&Captcha>) {
    let (flag, size) = match cd {
        None => (1, 0),
        Some(cd) => (0, cd.image_data.len() as u16),
    };
    let p = PACKET_ZC_ACK_PREVIEW_MACRO_DETECTOR { PacketType: PACKET_ZC_ACK_PREVIEW_MACRO_DETECTOR::ID as i16, captchaFlag: flag, imageSize: size, captchaKey: String::new() };
    self_send(c, aid, &p.to_vec());
    if let Some(cd) = cd {
        for chunk in image_chunks(&cd.image_data) {
            let p2 = PACKET_ZC_PREVIEW_MACRO_DETECTOR_CAPTCHA {
                PacketType: PACKET_ZC_PREVIEW_MACRO_DETECTOR_CAPTCHA::ID as i16,
                PacketLength: 0,
                captchaKey: String::new(),
                imageData: chunk.to_vec(),
            };
            self_send(c, aid, &p2.to_vec());
        }
    }
}

/// clif.cpp clif_macro_detector_request (ZC_APPLY_MACRO_DETECTOR 0x0a58, then the image in
/// ZC_APPLY_MACRO_DETECTOR_CAPTCHA 0x0a59 chunks).
fn clif_macro_detector_request(c: &Ctx, aid: u32) {
    let m = get(c, aid);
    if !m.detect_cd {
        return;
    }
    // Send preview initialization request to the client.
    let p = PACKET_ZC_APPLY_MACRO_DETECTOR { PacketType: PACKET_ZC_APPLY_MACRO_DETECTOR::ID as i16, imageSize: m.detect_image.len() as u16, captchaKey: String::new() };
    self_send(c, aid, &p.to_vec());
    for chunk in image_chunks(&m.detect_image) {
        let p2 = PACKET_ZC_APPLY_MACRO_DETECTOR_CAPTCHA {
            PacketType: PACKET_ZC_APPLY_MACRO_DETECTOR_CAPTCHA::ID as i16,
            PacketLength: 0,
            captchaKey: String::new(),
            imageData: chunk.to_vec(),
        };
        self_send(c, aid, &p2.to_vec());
    }
}

/// clif.cpp clif_macro_detector_request_show (ZC_REQ_ANSWER_MACRO_DETECTOR 0x0a5b): the tries left and the timeout.
fn clif_macro_detector_request_show(c: &Ctx, aid: u32) {
    let m = get(c, aid);
    let p = PACKET_ZC_REQ_ANSWER_MACRO_DETECTOR {
        PacketType: PACKET_ZC_REQ_ANSWER_MACRO_DETECTOR::ID as i16,
        retryCount: m.retry as u8,
        timeout: macro_detection_timeout(),
    };
    self_send(c, aid, &p.to_vec());
}

/// clif.cpp clif_parse_macro_detector_download_ack (CZ_COMPLETE_APPLY_MACRO_DETECTOR_CAPTCHA 0x0a5a).
fn clif_parse_macro_detector_download_ack(c: &Ctx, aid: u32) {
    if get(c, aid).retry != 0 {
        clif_macro_detector_request_show(c, aid);
    }
}

/// clif.cpp clif_parse_macro_detector_answer (CZ_ACK_ANSWER_MACRO_DETECTOR 0x0a5c).
fn clif_parse_macro_detector_answer(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(p) = PACKET_CZ_ACK_ANSWER_MACRO_DETECTOR::read(&mut Reader::new(pkt)) else { return };
    pc_macro_detector_process_answer(c, aid, &answer_bytes(&p.answer));
}

/// clif.cpp clif_macro_detector_status (ZC_CLOSE_MACRO_DETECTOR 0x0a5d).
fn clif_macro_detector_status(c: &Ctx, aid: u32, stype: i32) {
    let p = PACKET_ZC_CLOSE_MACRO_DETECTOR { PacketType: PACKET_ZC_CLOSE_MACRO_DETECTOR::ID as i16, status: stype };
    self_send(c, aid, &p.to_vec());
}

/// clif.cpp clif_parse_macro_reporter_select (CZ_REQ_PLAYER_AID_IN_RANGE 0x0a6c).
fn clif_parse_macro_reporter_select(c: &Ctx, aid: u32, pkt: &[u8]) {
    if !crate::pc::has_permission(c, aid, crate::rules::PC_PERM_MACRO_DETECT) {
        no_permission(c, aid);
        return;
    }
    let Some(p) = PACKET_CZ_REQ_PLAYER_AID_IN_RANGE::read(&mut Reader::new(pkt)) else { return };
    pc_macro_reporter_area_select(c, aid, p.xPos, p.yPos, p.RadiusRange);
}

/// clif.cpp clif_macro_reporter_select (ZC_ACK_PLAYER_AID_IN_RANGE 0x0a6d).
fn clif_macro_reporter_select(c: &Ctx, sd: &Unit, aid_list: &[u32]) {
    let p = PACKET_ZC_ACK_PLAYER_AID_IN_RANGE { PacketType: PACKET_ZC_ACK_PLAYER_AID_IN_RANGE::ID as i16, PacketLength: 0, AID: aid_list.to_vec() };
    clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
}

/// clif.cpp clif_parse_macro_reporter_ack (CZ_REQ_APPLY_MACRO_DETECTOR 0x0a56): a GM sends a player a captcha.
fn clif_parse_macro_reporter_ack(c: &Ctx, aid: u32, pkt: &[u8]) {
    if !crate::pc::has_permission(c, aid, crate::rules::PC_PERM_MACRO_DETECT) {
        no_permission(c, aid);
        return;
    }
    let Some(p) = PACKET_CZ_REQ_APPLY_MACRO_DETECTOR::read(&mut Reader::new(pkt)) else { return };
    // map_id2sd(p->AID)
    if crate::pc::get(c, p.AID).is_none() {
        if let Some(sd) = crate::unit::get(c, aid) {
            clif::displaymessage(c, &sd, crate::data::msg_txt(3)); // Character not found.
        }
        return;
    }
    if get(c, p.AID).retry != 0 {
        clif_macro_reporter_status(c, aid, MCR_INPROGRESS);
        return;
    }
    if captcha_db_empty(c) {
        clif_macro_reporter_status(c, aid, MCR_NO_DATA);
        return;
    }
    pc_macro_reporter_process(c, p.AID, aid as i32);
    clif_macro_reporter_status(c, aid, MCR_MONITORING);
}

/// clif.cpp clif_macro_reporter_status (ZC_ACK_APPLY_MACRO_DETECTOR 0x0a57).
fn clif_macro_reporter_status(c: &Ctx, aid: u32, stype: i32) {
    let p = PACKET_ZC_ACK_APPLY_MACRO_DETECTOR { PacketType: PACKET_ZC_ACK_APPLY_MACRO_DETECTOR::ID as i16, status: stype };
    self_send(c, aid, &p.to_vec());
}

#[cfg(test)]
mod tests {
    use super::*;

    /// pc.hpp CAPTCHA_BMP_SIZE and the chunking of clif_macro_detector_request: a full 220x90 captcha goes out in
    /// 58 chunks of 1024 bytes and one of 62.
    #[test]
    fn captcha_image_chunks() {
        assert_eq!(CAPTCHA_BMP_SIZE, 59454);
        let img = vec![7u8; CAPTCHA_BMP_SIZE];
        let sizes: Vec<usize> = image_chunks(&img).map(|c| c.len()).collect();
        assert_eq!(sizes.len(), 59);
        assert!(sizes[..58].iter().all(|&s| s == 1024));
        assert_eq!(sizes[58], 62);
        assert_eq!(image_chunks(&[]).count(), 0);
    }

    /// strcmp / strlen see a packet's char[16] answer up to its first NUL.
    #[test]
    fn answers_stop_at_nul() {
        assert_eq!(answer_bytes("abc"), b"abc".to_vec());
        assert_eq!(answer_bytes("ab\u{0}cd"), b"ab".to_vec());
        assert_eq!(answer_bytes(""), Vec::<u8>::new());
    }

    /// The captcha packets' sizes at PACKETVER 20211103 (packets_struct.hpp, clif_packetdb.hpp:1804-1814).
    #[test]
    fn captcha_packet_sizes() {
        assert_eq!(PACKET_CZ_REQ_UPLOAD_MACRO_DETECTOR::SIZE, 20);
        assert_eq!(PACKET_CZ_UPLOAD_MACRO_DETECTOR_CAPTCHA::SIZE, 8);
        assert_eq!(PACKET_ZC_ACK_PLAYER_AID_IN_RANGE::SIZE, 4);
        let p = PACKET_ZC_ACK_PLAYER_AID_IN_RANGE { PacketType: PACKET_ZC_ACK_PLAYER_AID_IN_RANGE::ID as i16, PacketLength: 0, AID: vec![150000, 150001] };
        let b = p.to_vec();
        assert_eq!(b.len(), 12);
        assert_eq!(u16::from_le_bytes([b[2], b[3]]), 12);
    }
}
