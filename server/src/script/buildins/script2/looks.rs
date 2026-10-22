//! Player information, looks and options: setlook, changelook, getlook, getgmlevel, getgroupid, checkoption,
//! checkoption1, checkoption2, setoption, setfalcon / checkfalcon, setriding / checkriding, checkwug, setmadogear /
//! checkmadogear, setdragon / checkdragon, ismounting / setmounting, checkidle, getcharip, convertpcinfo, setfont,
//! autoloot / has_autoloot, changesex / changecharsex (map side).
//!
//! pc_setoption is the skilltree agent's (skilltree.rs), pc_changelook the itemui agent's (itemui/look.rs).

use crate::db::jobs::ids::*;
use crate::pc_misc;
use crate::script::buildins::*;
use crate::script::vm::*;
use crate::status::defs::*;
use crate::unit;
use crate::world::Ctx;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "setlook" => setlook,
        "changelook" => changelook,
        "getlook" => getlook,
        "getgmlevel" => getgmlevel,
        "getgroupid" => getgroupid,
        "checkoption" => checkoption,
        "checkoption1" => checkoption1,
        "checkoption2" => checkoption2,
        "setoption" => setoption,
        "setfalcon" => setfalcon,
        "checkfalcon" => checkfalcon,
        "setriding" => setriding,
        "checkriding" => checkriding,
        "checkwug" => checkwug,
        "setmadogear" => setmadogear,
        "checkmadogear" => checkmadogear,
        "setdragon" => setdragon,
        "checkdragon" => checkdragon,
        "ismounting" => ismounting,
        "setmounting" => setmounting,
        "checkidle" => checkidle,
        "getcharip" => getcharip,
        "convertpcinfo" => convertpcinfo,
        "setfont" => setfont,
        "autoloot" => autoloot,
        "has_autoloot" => has_autoloot,
        "changesex" => changesex,
        "changecharsex" => changesex,
        _ => return None,
    })
}

fn option_of(c: &Ctx, aid: u32) -> i32 {
    unit::get(c, aid).map(|u| u.sc.option).unwrap_or(0)
}

/// sd->class_ (the MAPID of the character's job).
fn class_of(c: &Ctx, aid: u32) -> u64 {
    crate::pc::character_of(c, aid).map(|ch| pc_jobid2mapid(ch.class)).unwrap_or(0)
}

fn checkskill(c: &Ctx, aid: u32, skill_id: u16) -> i32 {
    crate::skill::checkskill(c, aid, skill_id) as i32
}

fn pc_setoption(c: &Ctx, aid: u32, option: i32, subtype: i32) {
    crate::skilltree::pc_setoption(c, aid, option, subtype);
}

/// setlook <type>,<value>{,<char id>}: pc_changelook (kept in the character).
fn setlook(rt: &Rt, st: &mut State) -> i32 {
    let kind = getnum(rt, st, 2);
    let val = getnum(rt, st, 3);
    let Some(aid) = charid2sd(rt, st, 4, "buildin_setlook") else { return SCRIPT_CMD_SUCCESS };
    crate::itemui::look::pc_changelook(rt.c.unwrap(), aid, kind, val);
    SCRIPT_CMD_SUCCESS
}

/// changelook <type>,<value>{,<char id>}: clif_changelook only (the character keeps its look).
fn changelook(rt: &Rt, st: &mut State) -> i32 {
    use crate::pc_equip::*;
    let Some(aid) = charid2sd(rt, st, 4, "buildin_changelook") else { return SCRIPT_CMD_FAILURE };
    let kind = getnum(rt, st, 2);
    let val = getnum(rt, st, 3);
    use crate::conf::*;
    match kind {
        k if k == LOOK_HAIR as i32 => {
            if val < min_hair_style() {
                log::error!("buildin_changelook: Invalid hair style. Minimum: {}", min_hair_style());
                return SCRIPT_CMD_FAILURE;
            }
            if val > max_hair_style() {
                log::error!("buildin_changelook: Invalid hair style. Maximum: {}", max_hair_style());
                return SCRIPT_CMD_FAILURE;
            }
        }
        k if k == LOOK_HAIR_COLOR as i32 => {
            if val < min_hair_color() {
                log::error!("buildin_changelook: Invalid hair color. Minimum: {}", min_hair_color());
                return SCRIPT_CMD_FAILURE;
            }
            if val > max_hair_color() {
                log::error!("buildin_changelook: Invalid hair color. Maximum: {}", max_hair_color());
                return SCRIPT_CMD_FAILURE;
            }
        }
        k if k == LOOK_CLOTHES_COLOR as i32 => {
            if val < min_cloth_color() {
                log::error!("buildin_changelook: Invalid cloth color. Minimum: {}", min_cloth_color());
                return SCRIPT_CMD_FAILURE;
            }
            if val > max_cloth_color() {
                log::error!("buildin_changelook: Invalid cloth color. Maximum: {}", max_cloth_color());
                return SCRIPT_CMD_FAILURE;
            }
        }
        k if k == LOOK_BODY2 as i32 => {
            if val < 0 {
                log::error!("buildin_changelook: Invalid body style. Minimum: 0");
                return SCRIPT_CMD_FAILURE;
            }
            if val >= JOB_MAX {
                log::error!("buildin_changelook: Invalid body style. Maximum: {}", JOB_MAX - 1);
                return SCRIPT_CMD_FAILURE;
            }
        }
        // LOOK_BASE, WEAPON, HEAD_BOTTOM, HEAD_TOP, HEAD_MID, SHIELD, SHOES, BODY, RESET_COSTUMES, ROBE: no value cap
        0 | 2 | 3 | 4 | 5 | 8 | 9 | 10 | 11 | 12 => {}
        _ => {
            log::error!("buildin_changelook: Unknown look type {kind}");
            return SCRIPT_CMD_FAILURE;
        }
    }
    clif_changelook(rt.c.unwrap(), aid, kind as u8, val);
    SCRIPT_CMD_SUCCESS
}

/// getlook(<type>{,<char id>}): the saved look (sd->status).
fn getlook(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 3, "buildin_getlook") else {
        st.pushint(-1);
        return SCRIPT_CMD_FAILURE;
    };
    let kind = getnum(rt, st, 2);
    let ch = crate::pc::character_of(rt.c.unwrap(), aid);
    let val: i64 = match (kind, ch) {
        (1, Some(ch)) => ch.hair as i64,
        (2, Some(ch)) => ch.weapon as i64,
        (3, Some(ch)) => ch.head_bottom as i64,
        (4, Some(ch)) => ch.head_top as i64,
        (5, Some(ch)) => ch.head_mid as i64,
        (6, Some(ch)) => ch.hair_color as i64,
        (7, Some(ch)) => ch.clothes_color as i64,
        (8, Some(ch)) => ch.shield as i64,
        (12, Some(ch)) => ch.robe as i64,
        (13, Some(ch)) => ch.body as i64,
        _ => -1, // LOOK_SHOES and the others: -1
    };
    st.pushint(val);
    SCRIPT_CMD_SUCCESS
}

/// getgmlevel({<char id>}): pc_get_group_level.
fn getgmlevel(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 2, "buildin_getgmlevel") else { return SCRIPT_CMD_FAILURE };
    st.pushint(crate::pc::group_level(rt.c.unwrap(), aid) as i64);
    SCRIPT_CMD_SUCCESS
}

/// getgroupid({<char id>}): pc_get_group_id.
fn getgroupid(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 2, "buildin_getgroupid") else { return SCRIPT_CMD_FAILURE };
    let g = crate::pc::get(rt.c.unwrap(), aid).map(|p| p.group_id as i64).unwrap_or(0);
    st.pushint(g);
    SCRIPT_CMD_SUCCESS
}

/// checkoption(<option>{,<char id>}).
fn checkoption(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 3, "buildin_checkoption") else { return SCRIPT_CMD_FAILURE };
    let option = getnum(rt, st, 2);
    st.pushint((option_of(rt.c.unwrap(), aid) & option != 0) as i64);
    SCRIPT_CMD_SUCCESS
}

/// checkoption1(<opt1>{,<char id>}): sc.opt1 equals.
fn checkoption1(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 3, "buildin_checkoption1") else { return SCRIPT_CMD_FAILURE };
    let opt1 = getnum(rt, st, 2);
    let v = unit::get(rt.c.unwrap(), aid).map(|u| u.sc.opt1 as i32 == opt1).unwrap_or(false);
    st.pushint(v as i64);
    SCRIPT_CMD_SUCCESS
}

/// checkoption2(<opt2>{,<char id>}): sc.opt2 has the bits.
fn checkoption2(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 3, "buildin_checkoption2") else { return SCRIPT_CMD_FAILURE };
    let opt2 = getnum(rt, st, 2);
    let v = unit::get(rt.c.unwrap(), aid).map(|u| u.sc.opt2 as i32 & opt2 != 0).unwrap_or(false);
    st.pushint(v as i64);
    SCRIPT_CMD_SUCCESS
}

/// setoption <option>{,<flag>{,<char id>}}: pc_setoption with the bits added (flag 1) or removed (flag 0); option 0
/// without a flag removes falcon and peco (NEW_CARTS: the cart is not an option).
fn setoption(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 4, "buildin_setoption") else { return SCRIPT_CMD_FAILURE };
    let c = rt.c.unwrap();
    let mut option = getnum(rt, st, 2);
    let mut flag = 1;
    if st.hasdata(3) {
        flag = getnum(rt, st, 3);
    } else if option == 0 {
        // Request to remove everything.
        flag = 0;
        option = OPTION_FALCON | OPTION_RIDING;
    }
    let cur = option_of(c, aid);
    if flag != 0 {
        // Add option
        if option & OPTION_WEDDING != 0 && crate::script::battle_flags::battle_get_value("wedding_modifydisplay") == 0 {
            option &= !OPTION_WEDDING; // Do not show the wedding sprites
        }
        pc_setoption(c, aid, cur | option, 0);
    } else {
        // Remove option
        pc_setoption(c, aid, cur & !option, 0);
    }
    SCRIPT_CMD_SUCCESS
}

/// pc.cpp pc_setfalcon.
fn pc_setfalcon(c: &Ctx, aid: u32, flag: i32) {
    let opt = option_of(c, aid);
    if flag != 0 {
        if checkskill(c, aid, crate::db::skills::ids::HT_FALCON) > 0 {
            // add falcon if he have the skill
            pc_setoption(c, aid, opt | OPTION_FALCON, 0);
        }
    } else if opt & OPTION_FALCON != 0 {
        pc_setoption(c, aid, opt & !OPTION_FALCON, 0); // remove falcon
    }
}

/// setfalcon {<flag>{,<char id>}}.
fn setfalcon(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 3, "buildin_setfalcon") else { return SCRIPT_CMD_FAILURE };
    let flag = if st.hasdata(2) { getnum(rt, st, 2) } else { 1 };
    pc_setfalcon(rt.c.unwrap(), aid, flag);
    SCRIPT_CMD_SUCCESS
}

fn check_opt(rt: &Rt, st: &mut State, loc: i32, func: &str, bits: i32) -> i32 {
    let Some(aid) = charid2sd(rt, st, loc, func) else { return SCRIPT_CMD_FAILURE };
    st.pushint((option_of(rt.c.unwrap(), aid) & bits != 0) as i64);
    SCRIPT_CMD_SUCCESS
}

/// checkfalcon({<char id>}).
fn checkfalcon(rt: &Rt, st: &mut State) -> i32 {
    check_opt(rt, st, 2, "buildin_checkfalcon", OPTION_FALCON)
}

/// pc.cpp pc_setriding.
fn pc_setriding(c: &Ctx, aid: u32, flag: i32) {
    if crate::status::sc::has_sce(c, aid, crate::status::consts::SC_ALL_RIDING) {
        return;
    }
    let opt = option_of(c, aid);
    if flag != 0 {
        if checkskill(c, aid, crate::db::skills::ids::KN_RIDING) > 0 {
            // add peco
            pc_setoption(c, aid, opt | OPTION_RIDING, 0);
        }
    } else if opt & OPTION_RIDING != 0 {
        pc_setoption(c, aid, opt & !OPTION_RIDING, 0);
    }
}

/// setriding {<flag>{,<char id>}}.
fn setriding(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 3, "buildin_setriding") else { return SCRIPT_CMD_FAILURE };
    let flag = if st.hasdata(2) { getnum(rt, st, 2) } else { 1 };
    pc_setriding(rt.c.unwrap(), aid, flag);
    SCRIPT_CMD_SUCCESS
}

/// checkriding({<char id>}).
fn checkriding(rt: &Rt, st: &mut State) -> i32 {
    check_opt(rt, st, 2, "buildin_checkriding", OPTION_RIDING)
}

/// checkwug({<char id>}): a warg or riding one.
fn checkwug(rt: &Rt, st: &mut State) -> i32 {
    check_opt(rt, st, 2, "buildin_checkwug", OPTION_WUG | OPTION_WUGRIDER)
}

/// pc.cpp e_mado_type: MADO_ROBOT 0 … MADO_UNUSED, MADO_MAX.
const MADO_ROBOT: i32 = 0;

/// pc.cpp pc_setmadogear.
pub(crate) fn pc_setmadogear(c: &Ctx, aid: u32, flag: bool, kind: i32) {
    if class_of(c, aid) & MAPID_THIRDMASK != MAPID_MECHANIC {
        return;
    }
    let opt = option_of(c, aid);
    if flag {
        if checkskill(c, aid, crate::db::skills::ids::NC_MADOLICENCE) > 0 {
            pc_setoption(c, aid, opt | OPTION_MADOGEAR, kind);
        }
    } else if opt & OPTION_MADOGEAR != 0 {
        pc_setoption(c, aid, opt & !OPTION_MADOGEAR, 0);
    }
}

/// setmadogear {<flag>{,<type>{,<char id>}}}.
fn setmadogear(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 4, "buildin_setmadogear") else { return SCRIPT_CMD_FAILURE };
    let flag = if st.hasdata(2) { getnum(rt, st, 2) != 0 } else { true };
    let mut kind = MADO_ROBOT;
    if st.hasdata(3) {
        kind = getnum(rt, st, 3) as u16 as i32;
        // pc.hpp e_mado_type: MADO_ROBOT, MADO_SUITE, MADO_UNUSED (2), MADO_MAX
        if kind == 2 || kind >= 3 {
            log::error!("buildin_setmadogear: Invalid mado gear type {kind}, defaulting to robot...");
            kind = MADO_ROBOT;
        }
    }
    pc_setmadogear(rt.c.unwrap(), aid, flag, kind);
    SCRIPT_CMD_SUCCESS
}

/// checkmadogear({<char id>}).
fn checkmadogear(rt: &Rt, st: &mut State) -> i32 {
    check_opt(rt, st, 2, "buildin_checkmadogear", OPTION_MADOGEAR)
}

/// setdragon({<color>{,<char id>}}): Rune Knights with Dragon Training mount (colour 1-5) or dismount.
fn setdragon(rt: &Rt, st: &mut State) -> i32 {
    let color = if st.hasdata(2) { getnum(rt, st, 2) } else { 0 };
    let Some(aid) = charid2sd(rt, st, 3, "buildin_setdragon") else { return SCRIPT_CMD_FAILURE };
    let c = rt.c.unwrap();
    let opt = option_of(c, aid);
    if checkskill(c, aid, crate::db::skills::ids::RK_DRAGONTRAINING) == 0 || class_of(c, aid) & MAPID_THIRDMASK != MAPID_RUNE_KNIGHT {
        st.pushint(0); // Doesn't have the skill or it's not a Rune Knight
    } else if opt & OPTION_DRAGON != 0 {
        // Is mounted; release
        pc_setoption(c, aid, opt & !OPTION_DRAGON, 0);
        st.pushint(1);
    } else {
        // Not mounted; Mount now.
        let mut option = OPTION_DRAGON1;
        if color != 0 {
            option = match color {
                1 => OPTION_DRAGON1,
                2 => OPTION_DRAGON2,
                3 => OPTION_DRAGON3,
                4 => OPTION_DRAGON4,
                5 => OPTION_DRAGON5,
                _ => 0,
            };
            if option == 0 {
                log::warn!("script_setdragon: Unknown Color {color} used; changing to green (1)");
                option = OPTION_DRAGON1;
            }
        }
        pc_setoption(c, aid, opt | option, 0);
        st.pushint(1);
    }
    SCRIPT_CMD_SUCCESS
}

/// checkdragon({<char id>}).
fn checkdragon(rt: &Rt, st: &mut State) -> i32 {
    check_opt(rt, st, 2, "buildin_checkdragon", OPTION_DRAGON)
}

/// ismounting({<char id>}): SC_ALL_RIDING.
fn ismounting(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 2, "buildin_ismounting") else { return SCRIPT_CMD_FAILURE };
    let v = crate::status::sc::has_sce(rt.c.unwrap(), aid, crate::status::consts::SC_ALL_RIDING);
    st.pushint(v as i64);
    SCRIPT_CMD_SUCCESS
}

/// clif.hpp MSI_FAIELD_RIDING_OVERLAPPED (PACKETVER 20211103).
const MSI_FAIELD_RIDING_OVERLAPPED: u16 = 1931;

/// setmounting({<char id>}): SC_ALL_RIDING on or off (not with another mount or while hidden).
fn setmounting(rt: &Rt, st: &mut State) -> i32 {
    use crate::status::consts::*;
    let Some(aid) = charid2sd(rt, st, 2, "buildin_setmounting") else { return SCRIPT_CMD_FAILURE };
    let c = rt.c.unwrap();
    let Some(u) = unit::get(c, aid) else { return SCRIPT_CMD_FAILURE };
    let has = |t: i32| crate::status::sc::has_sce(c, aid, t);
    if u.sc.option & (OPTION_WUGRIDER | OPTION_RIDING | OPTION_DRAGON | OPTION_MADOGEAR) != 0 {
        crate::clif_items::msg(c, &u, MSI_FAIELD_RIDING_OVERLAPPED);
        st.pushint(0); // can't mount with one of these
    } else if has(SC_CLOAKING) || has(SC_CHASEWALK) || has(SC_CLOAKINGEXCEED) || has(SC_CAMOUFLAGE) || has(SC_STEALTHFIELD) || has(SC__FEINTBOMB) {
        // SC_HIDING, SC__INVISIBILITY, SC__SHADOWFORM, SC_SUHIDE already disable item usage
        st.pushint(0); // Silent failure
    } else {
        if has(SC_ALL_RIDING) {
            crate::status::sc::status_change_end(c, aid, SC_ALL_RIDING, 0); // release mount
        } else {
            crate::status::sc::sc_start(c, None, &u, SC_ALL_RIDING, 10000, 1, crate::status::sc::INFINITE_TICK); // mount
        }
        st.pushint(1); // in both cases, return 1.
    }
    SCRIPT_CMD_SUCCESS
}

/// checkidle({"<character name>"}): seconds since the player's last action (last_tick - sd->idletime).
fn checkidle(rt: &Rt, st: &mut State) -> i32 {
    match nick2sd(rt, st, 2, "buildin_checkidle") {
        Some(aid) => {
            let c = rt.c.unwrap();
            let idle = crate::pc::get(c, aid).map(|p| p.idletime).unwrap_or(0);
            st.pushint(crate::pc::last_tick(c) - idle);
        }
        None => st.pushint(0),
    }
    SCRIPT_CMD_SUCCESS
}

/// getcharip({"<name>"|<account id>|<char id>}): the client's IP address. The module has no sockets and so no client
/// address: the answer is always "", as rAthena's for a session without client_addr.
fn getcharip(rt: &Rt, st: &mut State) -> i32 {
    if st.hasdata(2) {
        // (the player lookup of rAthena: nick, account id, char id — the address is unknown either way)
        let _ = isstring(rt, st, 2);
    } else {
        let _ = rt.c.map(|c| crate::pc::get(c, st.rid).is_some());
    }
    st.pushstr(Vec::new());
    SCRIPT_CMD_SUCCESS
}

/// convertpcinfo(<name>|<account id>|<char id>,<type>): CPC_NAME 0, CPC_CHAR 1, CPC_ACCOUNT 2.
fn convertpcinfo(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let sd = if isstring(rt, st, 2) {
        let n = getstr(rt, st, 2);
        crate::pc::nick2sd(c, &lat(&n), false).map(|u| u.id)
    } else {
        let id = getnum(rt, st, 2) as u32;
        if crate::pc::get(c, id).is_some() { Some(id) } else { crate::pc::charid2sd(c, id).map(|u| u.id) }
    };
    let kind = getnum(rt, st, 3);
    if !(0..=2).contains(&kind) {
        log::error!("buildin_convertpcinfo: Unknown type {kind}.");
        st.pushnil();
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    }
    let Some(aid) = sd else {
        if kind == 0 {
            st.pushstr(Vec::new());
        } else {
            st.pushint(0);
        }
        return SCRIPT_CMD_SUCCESS;
    };
    let ch = crate::pc::character_of(c, aid);
    match kind {
        0 => st.pushstr(ch.map(|ch| crate::net::wire::latin1(&ch.name)).unwrap_or_default()),
        1 => st.pushint(ch.map(|ch| ch.id as i64).unwrap_or(0)),
        _ => st.pushint(aid as i64),
    }
    SCRIPT_CMD_SUCCESS
}

/// clif.cpp clif_font: ZC_NOTIFY_FONT (0x02ef) id.L font.W to the area.
pub fn clif_font(c: &Ctx, u: &crate::unit::Unit) {
    let mut w = Vec::with_capacity(8);
    w.extend_from_slice(&0x02efu16.to_le_bytes());
    w.extend_from_slice(&u.id.to_le_bytes());
    w.extend_from_slice(&(u.font as u16).to_le_bytes());
    crate::clif::send(c, &w, u, crate::clif::Target::Area);
}

/// setfont <font>: the character's font (the same font again switches it off), shown to the area.
fn setfont(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = rid2sd(rt, st, "buildin_setfont") else { return SCRIPT_CMD_SUCCESS };
    let c = rt.c.unwrap();
    let font = getnum(rt, st, 2);
    let Some(mut ch) = crate::pc::character_of(c, aid) else { return SCRIPT_CMD_SUCCESS };
    // sd->status.font is an unsigned char
    ch.font = if ch.font as i32 != font as u8 as i32 { font as u8 } else { 0 };
    let f = ch.font;
    crate::status::put_char(c, &ch);
    if let Some(mut u) = unit::get(c, aid) {
        u.font = f as i16;
        unit::save(c, &u);
        clif_font(c, &u);
    }
    SCRIPT_CMD_SUCCESS
}

/// autoloot({<rate>{,<char id>}}): sd->state.autoloot (no rate: toggles 0 / 10000).
fn autoloot(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 3, "buildin_autoloot") else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    let c = rt.c.unwrap();
    let mut m = pc_misc::pcm(c, aid);
    let rate = if st.hasdata(2) {
        let r = getnum(rt, st, 2);
        if !(0..=10000).contains(&r) {
            log::warn!("buildin_autoloot: Invalid rate value {r}, should be between 0 ~ 10000.");
            st.pushint(0);
            return SCRIPT_CMD_FAILURE;
        }
        r
    } else if m.autoloot > 0 {
        0
    } else {
        10000
    };
    m.autoloot = rate;
    pc_misc::put_pcm(c, &m);
    st.pushint(1);
    SCRIPT_CMD_SUCCESS
}

/// has_autoloot({<char id>}).
fn has_autoloot(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = charid2sd(rt, st, 2, "buildin_has_autoloot") else {
        st.pushint(0);
        return SCRIPT_CMD_FAILURE;
    };
    st.pushint(pc_misc::pcm(rt.c.unwrap(), aid).autoloot as i64);
    SCRIPT_CMD_SUCCESS
}

/// changesex({<char id>}) / changecharsex({<char id>}): pc_resetskill(sd, 4), every equipment off (pc_unequipitem
/// flag 3), then chrif_changesex: message 408 and the client is kicked (clif_authfail_fd 15).
///
/// The change itself is the char / login server's (chrif_changesex's 0x2b0e: char.cpp changes the account's or the
/// character's sex, and the Bard / Dancer class swap, while the player is away): atcommand_accounts.rs.
fn changesex(rt: &Rt, st: &mut State) -> i32 {
    let func = if st.funcname == "changecharsex" { "buildin_changecharsex" } else { "buildin_changesex" };
    let Some(aid) = charid2sd(rt, st, 2, func) else { return SCRIPT_CMD_FAILURE };
    let c = rt.c.unwrap();
    crate::skilltree::pc_resetskill(c, aid, 4);
    // to avoid any problem with equipment and invalid sex, equipment is unequiped.
    // (sd->equip_index is read again for every slot: unequipping one item clears every slot it filled)
    for slot in 0..crate::inventory::EQI_MAX {
        let i = crate::inventory::st(c, aid).and_then(|s| s.equip_index.get(slot).copied()).unwrap_or(-1);
        if i >= 0 {
            crate::pc_equip::pc_unequipitem(c, aid, i as i32, 3);
        }
    }
    // chrif_changesex(sd, true) / (sd, false): the request to the char / login servers, msg 408, the kick (atcmd
    // agent: atcommand_accounts.rs ports both servers' side)
    crate::atcommand_atcmd::accounts::chrif_changesex(c, aid, st.funcname != "changecharsex");
    SCRIPT_CMD_SUCCESS
}
