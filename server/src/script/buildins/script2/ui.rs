//! Dialog and client UI commands: mesitemlink, mesitemicon, itemlink, meshyperlink, mesemotion, progressbar,
//! progressbar_npc.

use crate::script::buildins::*;
use crate::db::items::{self as itemdb, ItemData};
use crate::inventory::{Item, MAX_ITEM_RDM_OPT};
use crate::itemui::{set_workinprogress, WIP_DISABLE_ALL, WIP_DISABLE_NONE};
use crate::pc_misc;
use crate::script::battle_flags::battle_get_value;
use crate::script::vm::*;
use crate::unit;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "mesitemlink" => mesitemlink,
        "mesitemicon" => mesitemicon,
        "itemlink" => itemlink,
        "meshyperlink" => meshyperlink,
        "mesemotion" => mesemotion,
        "progressbar" => progressbar,
        "progressbar_npc" => progressbar_npc,
        _ => return None,
    })
}

/// The item of mesitemlink / mesitemicon's first argument: by name (ItemDatabase::searchname) or by id.
fn item_arg(rt: &Rt, st: &mut State, func: &str) -> Option<&'static ItemData> {
    if isstring(rt, st, 2) {
        let name = getstr(rt, st, 2);
        let data = itemdb::itemdb_searchname(&lat(&name));
        if data.is_none() {
            log::error!("buildin_{func}: Item \"{}\" does not exist.", lat(&name));
        }
        data
    } else {
        let nameid = getnum(rt, st, 2) as u32;
        let data = itemdb::itemdb_exists(nameid);
        if data.is_none() {
            log::error!("buildin_{func}: Item ID {nameid} does not exist.");
        }
        data
    }
}

/// itemdb.cpp ItemDatabase::create_item_link_for_mes (PACKETVER >= 20151104: <ITEM>…</ITEM>; feature.mesitemlink,
/// feature.mesitemlink_brackets, feature.mesitemlink_dbname from the battle configuration).
pub fn create_item_link_for_mes(data: &ItemData, use_brackets: bool, name: Option<&[u8]>) -> Vec<u8> {
    let brackets = use_brackets || battle_get_value("feature.mesitemlink_brackets") != 0;
    let dbname = battle_get_value("feature.mesitemlink_dbname") != 0;
    let shown: Vec<u8> = match name {
        // Name was forcefully overwritten
        Some(n) if !dbname => n.to_vec(),
        // Use database name
        _ => crate::net::wire::latin1(&data.ename),
    };
    if battle_get_value("feature.mesitemlink") != 0 {
        let mut s = b"<ITEM>".to_vec();
        if brackets {
            s.push(b'[');
        }
        s.extend_from_slice(&shown);
        if brackets {
            s.push(b']');
        }
        s.extend_from_slice(b"<INFO>");
        s.extend_from_slice(data.nameid.to_string().as_bytes());
        s.extend_from_slice(b"</INFO>");
        s.extend_from_slice(b"</ITEM>");
        return s;
    }
    // itemlinks disabled by configuration
    shown
}

/// mesitemlink(<item id>|"<item name>"{,<use brackets>{,"<display name>"}}).
fn mesitemlink(rt: &Rt, st: &mut State) -> i32 {
    let Some(data) = item_arg(rt, st, "mesitemlink") else {
        st.pushstr(Vec::new());
        return SCRIPT_CMD_FAILURE;
    };
    let mut use_brackets = true;
    if st.hasdata(3) {
        use_brackets = getnum(rt, st, 3) != 0;
    }
    let name = if st.hasdata(4) { Some(getstr(rt, st, 4)) } else { None };
    let s = create_item_link_for_mes(data, use_brackets, name.as_deref());
    st.pushstr(s);
    SCRIPT_CMD_SUCCESS
}

/// mesitemicon(<item id>|"<item name>"{,"<display name>"}): ItemDatabase::create_item_icon_for_mes. feature.mesitemicon
/// needs PACKETVER 2023-03-02, so battle_adjust_conf switched it off for this client and the text is the name.
fn mesitemicon(rt: &Rt, st: &mut State) -> i32 {
    let Some(data) = item_arg(rt, st, "mesitemicon") else {
        st.pushstr(Vec::new());
        return SCRIPT_CMD_FAILURE;
    };
    let name = if st.hasdata(3) { Some(getstr(rt, st, 3)) } else { None };
    let s = if battle_get_value("feature.mesitemicon") == 0 {
        match name {
            Some(n) if battle_get_value("feature.mesitemicon_dbname") == 0 => n,
            _ => crate::net::wire::latin1(&data.ename),
        }
    } else {
        format!("^i[{}]", data.nameid).into_bytes()
    };
    st.pushstr(s);
    SCRIPT_CMD_SUCCESS
}

/// common/utilities.cpp util::base62_encode.
pub fn base62_encode(mut val: u32) -> String {
    const DICT: &[u8; 62] = b"0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    let mut out = Vec::new();
    while val != 0 {
        out.push(DICT[(val % 62) as usize]);
        val /= 62;
    }
    out.reverse();
    String::from_utf8(out).unwrap_or_default()
}

/// util::string_left_pad (never truncates).
pub fn left_pad(s: &str, pad: char, num: usize) -> String {
    let n = num.saturating_sub(s.len());
    format!("{}{s}", pad.to_string().repeat(n))
}

/// itemdb.cpp ItemDatabase::create_item_link (feature.itemlink; PACKETVER 20211103: <ITEML>, the look (>= 20161116),
/// the enchant grade and the ")" / "+" / "," / "-" separators (>= 20200724)).
pub fn create_item_link(item: &Item, data: Option<&ItemData>) -> Vec<u8> {
    let Some(id) = data else {
        log::error!("Tried to create itemlink for unknown item {}.", item.nameid);
        return b"Unknown item".to_vec();
    };
    let isequip = itemdb::itemdb_isequip2(id);
    // struct item: refine is a char, the option fields int16 / int16 / char; base62_encode takes them as uint32
    let refine = item.refine as i8;
    if battle_get_value("feature.itemlink") != 0 {
        let p2 = |v: u32| left_pad(&base62_encode(v), '0', 2);
        let mut s = String::from("<ITEML>");
        s += &left_pad(&base62_encode(id.equip), '0', 5);
        s += if isequip { "1" } else { "0" };
        s += &base62_encode(item.nameid);
        if refine > 0 {
            s += &format!("%{}", p2(refine as i32 as u32));
        }
        if isequip {
            s += &format!("&{}", p2(id.look));
        }
        s += &format!("'{}", p2(item.enchantgrade as u32));
        for card in [item.card0, item.card1, item.card2, item.card3] {
            s += &format!("){}", p2(card));
        }
        for o in item.option.iter().take(MAX_ITEM_RDM_OPT) {
            if o.id == 0 {
                break; // ignore options including ones beyond this one since the client won't even display them
            }
            s += &format!("+{}", p2(o.id as i32 as u32));
            s += &format!(",{}", p2(o.param as i8 as i32 as u32));
            s += &format!("-{}", p2(o.value as i32 as u32));
        }
        s += "</ITEML>";
        if isequip && id.slots == 0 {
            s += &format!(" [{}]", id.slots);
        }
        return s.into_bytes();
    }
    let mut s = Vec::new();
    if refine > 0 {
        s.extend_from_slice(format!("+{refine} ").as_bytes());
    }
    s.extend_from_slice(&crate::net::wire::latin1(&id.ename));
    if isequip {
        s.extend_from_slice(format!("[{}]", id.slots).as_bytes());
    }
    s
}

/// itemlink(<item_id>,<refine>,<card0>,<card1>,<card2>,<card3>,<enchantgrade>{,<RandomIDArray>,<RandomValueArray>,
/// <RandomParamArray>}).
fn itemlink(rt: &Rt, st: &mut State) -> i32 {
    let mut item = Item::new(getnum(rt, st, 2) as u32);
    if itemdb::itemdb_exists(item.nameid).is_none() {
        log::error!("buildin_itemlink: Item ID {} does not exists.", item.nameid);
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    }
    // FETCH(n, t): if( script_hasdata(st,n) ) (t)=script_getnum(st,n);
    if st.hasdata(3) {
        item.refine = getnum(rt, st, 3) as u8;
    }
    if st.hasdata(4) {
        item.card0 = getnum(rt, st, 4) as u32;
    }
    if st.hasdata(5) {
        item.card1 = getnum(rt, st, 5) as u32;
    }
    if st.hasdata(6) {
        item.card2 = getnum(rt, st, 6) as u32;
    }
    if st.hasdata(7) {
        item.card3 = getnum(rt, st, 7) as u32;
    }
    if st.hasdata(8) {
        item.enchantgrade = getnum(rt, st, 8) as u8;
    }
    // PACKETVER >= 20150225
    if st.hasdata(9) {
        let mut sd = None;
        if !crate::script::buildins::items::getitem_randomoption(rt, st, &mut sd, &mut item, "itemlink", 9) {
            st.state = END;
            return SCRIPT_CMD_FAILURE;
        }
    }
    let s = create_item_link(&item, itemdb::itemdb_exists(item.nameid));
    st.pushstr(s);
    SCRIPT_CMD_SUCCESS
}

/// meshyperlink("<display text>","<url>"): "<URL>text<INFO>url</INFO></URL>".
fn meshyperlink(rt: &Rt, st: &mut State) -> i32 {
    let display = getstr(rt, st, 2);
    let url = getstr(rt, st, 3);
    let mut s = b"<URL>".to_vec();
    s.extend_from_slice(&display);
    s.extend_from_slice(b"<INFO>");
    s.extend_from_slice(&url);
    s.extend_from_slice(b"</INFO></URL>");
    st.pushstr(s);
    SCRIPT_CMD_SUCCESS
}

/// mesemotion(<emotion id>): needs PACKETVER 2023-03-02; this build (20211103) reports the error and ends the script.
fn mesemotion(_rt: &Rt, st: &mut State) -> i32 {
    log::error!("buildin_mesemotion: This command requires PACKETVER 2023-03-02 or newer.");
    st.state = END;
    SCRIPT_CMD_FAILURE
}

/// strtol(color, nullptr, 0): the colour string of the progress bars (decimal, 0x hex or 0 octal).
fn color_of(s: &[u8]) -> i64 {
    crate::script::compiler::strtoll(s, 0, 0).0
}

/// progressbar "<color>",<seconds>: a cast-like bar over the player; the script waits (STOP) for the client's
/// CZ_PROGRESS (pc_misc::parse_progressbar).
fn progressbar(rt: &Rt, st: &mut State) -> i32 {
    let Some(aid) = rid2sd(rt, st, "buildin_progressbar") else { return SCRIPT_CMD_SUCCESS };
    let c = rt.c.unwrap();
    st.state = STOP;
    let color = getstr(rt, st, 2);
    let second = getnum(rt, st, 3) as u32;
    let mut m = pc_misc::pcm(c, aid);
    m.progressbar_npc_id = st.oid;
    m.progressbar_timeout = c.tick() + second.wrapping_mul(1000) as i64;
    pc_misc::put_pcm(c, &m);
    set_workinprogress(c, aid, WIP_DISABLE_ALL);
    if let Some(sd) = unit::get(c, aid) {
        // clif_progressbar takes the colour as unsigned long and writes 32 bits of it
        crate::clif_misc::progressbar(c, &sd, color_of(&color) as u32, second);
    }
    SCRIPT_CMD_SUCCESS
}

/// progressbar_npc "<color>",<seconds>{,"<NPC name>"}: a bar over the NPC. The first call sleeps (RERUNLINE with a
/// sleep tick); the call after the sleep ends the bar and lets the script continue.
fn progressbar_npc(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let sd = if crate::pc::get(c, st.rid).is_some() { Some(st.rid) } else { None };
    let nd_id = if st.hasdata(4) {
        let name = getstr(rt, st, 4);
        match crate::script::npc_run::npc_name2id(c, &lat(&name)) {
            Some(nd) => nd.id,
            None => {
                log::error!("buildin_progressbar_npc: NPC \"{}\" was not found.", lat(&name));
                return SCRIPT_CMD_FAILURE;
            }
        }
    } else {
        // map_id2nd(st->oid) (rAthena dereferences it unchecked)
        use crate::npc::npc;
        match c.db().npc().id().find(st.oid) {
            Some(nd) => nd.id,
            None => {
                log::error!("buildin_progressbar_npc: the script has no NPC.");
                return SCRIPT_CMD_FAILURE;
            }
        }
    };
    let mut nm = pc_misc::npcm(c, nd_id);
    let block = crate::pc::PCBLOCK_MOVE | crate::pc::PCBLOCK_ATTACK | crate::pc::PCBLOCK_SKILL;
    if nm.progressbar_timeout == 0 {
        // First call (by function call)
        let color = getstr(rt, st, 2);
        let second = getnum(rt, st, 3);
        if second < 0 {
            log::error!("buildin_progressbar_npc: negative amount('{second}') of seconds is not supported");
            return SCRIPT_CMD_FAILURE;
        }
        if let Some(aid) = sd {
            // Player attached - keep them from doing other things
            set_workinprogress(c, aid, WIP_DISABLE_ALL);
            if let Some(mut p) = crate::pc::get(c, aid) {
                p.block_action |= block;
                crate::pc::put(c, &p);
            }
        }
        // sleep for the target amount of time
        st.state = RERUNLINE;
        st.sleep_tick = second.wrapping_mul(1000);
        nm.progressbar_timeout = c.tick() + second.wrapping_mul(1000) as i64;
        nm.progressbar_color = color_of(&color) as u32;
        pc_misc::put_npcm(c, &nm);
        if let Some(nu) = unit::get(c, nd_id) {
            crate::clif_misc::progressbar_npc(c, &nu, None); // clif_progressbar_npc_area
        }
    } else {
        // Second call (by timer after sleeping time is over): continue the script
        if let Some(aid) = sd {
            // Player attached - remove restrictions
            set_workinprogress(c, aid, WIP_DISABLE_NONE);
            if let Some(mut p) = crate::pc::get(c, aid) {
                p.block_action &= !block;
                crate::pc::put(c, &p);
            }
        }
        st.state = RUN;
        st.sleep_tick = 0;
        nm.progressbar_timeout = 0;
        nm.progressbar_color = 0;
        pc_misc::put_npcm(c, &nm);
    }
    SCRIPT_CMD_SUCCESS
}
