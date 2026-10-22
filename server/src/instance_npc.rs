//! npc.cpp's instance side: npc_duplicate4instance (a warp copied directly, every other NPC through
//! npc_parse_duplicate with the "INSTANCING" file path), npc_parse_duplicate itself for that call (the run-time
//! NPC builder: npc_create_npc, npc_parsename, npc_parseview, map_addnpc, unit_dataset, map_addblock,
//! status_set_viewdata, npc_enable_target of the source's state, the label exports), npc_instanceinit and
//! npc_instancedestroy (OnInstanceInit / OnInstanceDestroy), and nd->instance_id (`npc_instance`).
//!
//! The world build (npc.rs) makes the NPCs of the NPC files; these are the only NPCs made at run time from other
//! NPCs. Their rows are the same `npc` / `unit` rows; their code is their source's (npc_code resolves src_id), so
//! they share the source's `.` variables as rAthena's duplicates share its script_code.

use spacetimedb::{table, Table};

use crate::npc::{npc, Npc, JT_FAKENPC, JT_WARPNPC, NPCTYPE_CASHSHOP, NPCTYPE_ITEMSHOP, NPCTYPE_MARKETSHOP, NPCTYPE_POINTSHOP, NPCTYPE_SCRIPT, NPCTYPE_SHOP, NPCTYPE_TOMB, NPCTYPE_WARP, NPCVIEW_ENABLE, NPC_NAME_LENGTH};
use crate::unit::{self, ScView, Unit, UnitData};
use crate::world::Ctx;

/// npc_data::instance_id: set on the script NPCs npc_parse_duplicate makes for an instance (the "INSTANCING" path).
#[table(accessor = npc_instance)]
#[derive(Clone, Debug)]
pub struct NpcInstance {
    #[primary_key]
    pub npc_id: u32,
    pub instance_id: i32,
}

/// nd->instance_id of an NPC (0 for any other object or an NPC outside an instance).
pub fn npc_instance_id(c: &Ctx, npc_id: u32) -> i32 {
    c.db().npc_instance().npc_id().find(npc_id).map(|r| r.instance_id).unwrap_or(0)
}

/// npc_unload frees the npc_data: its instance id goes with it.
pub fn on_npc_unload(c: &Ctx, npc_id: u32) {
    c.db().npc_instance().npc_id().delete(npc_id);
}

/// The world is rebuilt: every NPC is gone.
pub fn on_world_rebuild(c: &Ctx) {
    for id in c.db().npc_instance().iter().map(|r| r.npc_id).collect::<Vec<_>>() {
        c.db().npc_instance().npc_id().delete(id);
    }
}

/// The unique name npc_duplicate4instance / instance_npcname give an NPC's copy: "dup_<instance id>_<source id>"
/// (snprintf into NPC_NAME_LENGTH + 1 / NAME_LENGTH buffers: never cut, the number is short).
pub fn dup_name(instance_id: i32, npc_id: u32) -> String {
    format!("dup_{}_{}", instance_id, npc_id as i32)
}

/// npc.cpp npc_duplicate4instance: copies NPC `snd_id` onto map `m` of an instance. 1 when nothing was made.
pub fn npc_duplicate4instance(c: &Ctx, snd_id: u32, m: u16) -> i32 {
    let instance_id = crate::instance_map::map_instance_id(c, m);
    if instance_id <= 0 {
        return 1;
    }
    let Some(snd) = c.db().npc().id().find(snd_id) else { return 1 };
    let Some(su) = unit::get(c, snd_id) else { return 1 };
    let newname = dup_name(instance_id, snd.id);
    if crate::script::npc_run::npc_name2id(c, &newname).is_some() {
        // Name already in use
        log::error!("npc_duplicate4instance: the npcname ({newname}) is already in use while trying to duplicate npc {} in instance {instance_id}.", snd.exname);
        return 1;
    }
    if snd.subtype == NPCTYPE_WARP {
        // Adjust destination, if instanced
        let Some(idata) = crate::instance::find(c, instance_id) else { return 1 };
        let dm = snd.warp_map; // map_mapindex2mapid(snd->u.warp.mapindex)
        if dm < 0 {
            return 1;
        }
        let name2id = |mm: u16| -> i32 { crate::data::maps().get(mm).and_then(|d| crate::data::maps().id_of(&d.name)).map(|x| x as i32).unwrap_or(-1) };
        let mut imap = 0;
        for it in &idata.map {
            if it.m != 0 && name2id(it.src_m) == dm {
                imap = name2id(it.m);
                break; // Instance map matches destination, update to instance map
            }
        }
        if imap == 0 {
            imap = name2id(dm as u16);
        }
        if imap == -1 {
            log::error!("npc_duplicate4instance: warp ({}) leading to instanced map ({}), but instance map is not attached to current instance.", crate::instance_map::map_mapid2mapname(c, dm), snd.exname);
            return 1;
        }
        let id = crate::mob::spawn::npc_get_new_npc_id(c);
        let wnd = Npc {
            id,
            exname: newname.chars().take(NPC_NAME_LENGTH).collect(),
            name: String::new(),
            subtype: NPCTYPE_WARP,
            map: m as i32,
            x: su.x,
            y: su.y,
            class: JT_WARPNPC,
            src_id: if snd.src_id != 0 { snd.src_id } else { snd.id },
            xs: snd.xs,
            ys: snd.ys,
            warp_map: imap,
            warp_x: snd.warp_x,
            warp_y: snd.warp_y,
            trigger_on_hidden: snd.trigger_on_hidden,
            state: NPCVIEW_ENABLE,
            is_invisible: false,
            // (npc_create_npc leaves the path empty: the warp is never named by npc_parsename)
            path: String::new(),
            script_file: 0,
            script_start: 0,
            script_end: 0,
            script_line: 0,
            shop: Vec::new(),
            shop_discount: false,
            itemshop_nameid: 0,
            pointshop_str: String::new(),
            owner_char: 0,
            touch_order: 0,
        };
        // map_addnpc, npc_setcells (the module's CELL_NPC is the NPCs' trigger areas), map_addblock,
        // status_set_viewdata, unit_dataset (the direction stays 0)
        add_to_map(c, wnd, 0);
        // copy the original npc state
        if snd.state != NPCVIEW_ENABLE {
            crate::script::buildins::npcctl::npc_enable_target(c, id, 0, snd.state);
        }
        set_state(c, id, snd.state);
    } else {
        let Some(md) = crate::data::maps().get(m) else { return 1 };
        let w1 = format!("{},{},{},{}", md.name, su.x, su.y, su.ud.dir);
        // a tomb's exname is empty in rAthena (the module names it for its unique index): "duplicate()" is refused
        let exname = if snd.subtype == NPCTYPE_TOMB { String::new() } else { snd.exname.clone() };
        let w2 = format!("duplicate({exname})");
        let w3 = format!("{}::{}", snd.name, newname);
        // snd->u.scr.xs / ys (the union's script view: a shop's bytes there are ignored by npc_parse_duplicate)
        let w4 = if snd.subtype == NPCTYPE_SCRIPT && snd.xs >= 0 && snd.ys >= 0 { format!("{},{},{}", snd.class, snd.xs, snd.ys) } else { format!("{}", snd.class) };
        npc_parse_duplicate_instancing(c, &w1, &w2, &w3, &w4);
    }
    0
}

/// The NPC's declared state (nd->state = dnd->state).
fn set_state(c: &Ctx, id: u32, state: u8) {
    if let Some(nd) = c.db().npc().id().find(id) {
        if nd.state != state {
            c.db().npc().id().update(Npc { state, ..nd });
        }
    }
}

/// map_addnpc (the trigger-area order: npc.rs), unit_dataset, map_addblock, status_set_viewdata, and clif_spawn when
/// players are on the map; the row goes into npcname_db. `dir` is what the caller sets after unit_dataset.
fn add_to_map(c: &Ctx, mut nd: Npc, dir: u8) {
    let m = nd.map as u16;
    let touch = (nd.subtype == NPCTYPE_WARP || nd.subtype == NPCTYPE_SCRIPT) && nd.xs >= 0 && nd.ys >= 0;
    nd.touch_order = 0;
    let (id, x, y, class, name) = (nd.id, nd.x, nd.y, nd.class, nd.name.clone());
    let subtype = nd.subtype;
    c.db().npc().insert(nd);
    if touch {
        map_addnpc_area(c, m, id, subtype);
    }
    let tick = c.tick();
    let mut u = Unit {
        id,
        kind: crate::map::BL_NPC,
        map: m,
        x,
        y,
        on_map: false,
        name,
        // status_set_viewdata(nd, nd->class_) unless JT_FAKENPC (the view db's JT_INVISIBLE otherwise)
        vd: crate::npc::npc_viewdata(if class == JT_FAKENPC { crate::npc::JT_INVISIBLE } else { class }),
        ud: UnitData { dir, sx: 8, sy: 8, attackabletime: tick, canact_tick: tick, canmove_tick: tick, endure_tick: tick, ..Default::default() },
        sc: ScView::default(),
        speed: crate::npc::DEFAULT_NPC_WALK_SPEED,
        mode: unit::MD_CANMOVE,
        level: 0,
        char_id: 0,
        head_dir: 0,
        font: 0,
        manner: 0,
        karma: false,
        guild_id: 0,
        emblem_id: 0,
        hp: 0,
        max_hp: 0,
        dead: false,
        size: 0,
        boss: 0,
        invisible: false,
        npc_owner_char: 0,
    };
    c.db().unit().insert(u.clone());
    if !unit::addblock(c, &mut u) {
        return;
    }
    if class != JT_FAKENPC && crate::mob::map_users(c, m) > 0 {
        crate::clif::spawn(c, &u, false);
    }
}

/// map.cpp map_addnpc's grouping of the NPCs with a trigger area (0 < npc_num_warp < npc_num_area): a warp takes the
/// first script's slot (that script moves to the end of the area section), a script is appended. npc.rs keeps the
/// section as each NPC's `touch_order` (index + 1).
pub(crate) fn map_addnpc_area(c: &Ctx, m: u16, id: u32, subtype: u8) {
    let mut area: Vec<u32> = crate::npc::touch_npcs(c, m).iter().map(|n| n.id).collect();
    let num_warp = crate::npc::touch_npcs(c, m).iter().filter(|n| n.subtype == NPCTYPE_WARP).count();
    if subtype == NPCTYPE_WARP {
        // npc[npc_num] = npc[npc_num_area]; npc[npc_num_area] = npc[npc_num_warp]; npc[npc_num_warp] = nd;
        if num_warp < area.len() {
            let moved = area[num_warp];
            area.push(moved);
            area[num_warp] = id;
        } else {
            area.push(id);
        }
    } else {
        area.push(id);
    }
    for (i, nid) in area.into_iter().enumerate() {
        if let Some(n) = c.db().npc().id().find(nid) {
            if n.touch_order != i as u32 + 1 {
                c.db().npc().id().update(Npc { touch_order: i as u32 + 1, ..n });
            }
        }
    }
}

/// npc.cpp npc_parse_duplicate for npc_duplicate4instance's call (w1..w4 as it formats them, filepath "INSTANCING").
fn npc_parse_duplicate_instancing(c: &Ctx, w1: &str, w2: &str, w3: &str, w4: &str) {
    const FILEPATH: &str = "INSTANCING";
    // get the npc being duplicated
    let w2b = crate::net::wire::latin1(w2);
    let length = w2b.len();
    if w2b.last() != Some(&b')') || length <= 11 || length - 11 >= 128 {
        // does not match 'duplicate(%127s)', name is empty or too long
        log::error!("npc_parse_script: bad duplicate name in file '{FILEPATH}', line '1' : {w2}");
        return;
    }
    let srcname = crate::net::wire::from_latin1(&w2b[10..length - 1]);
    let Some(dnd) = crate::script::npc_run::npc_name2id(c, &srcname) else {
        log::error!("npc_parse_script: original npc not found for duplicate in file '{FILEPATH}', line '1' : {srcname}");
        return;
    };
    let src_id = if dnd.src_id != 0 { dnd.src_id } else { dnd.id };
    let kind = dnd.subtype;
    // get placement: "%15[^,],%6hd,%6hd,%4hd" (w1 is never "-" here)
    let mut s = crate::cstr::Scan::new(w1.as_bytes());
    let parsed = (|| {
        let mapname = s.set(15, b",")?;
        s.lit(b',').then_some(())?;
        let x = s.int(6)?;
        s.lit(b',').then_some(())?;
        let y = s.int(6)?;
        s.lit(b',').then_some(())?;
        let d = s.int(4)?;
        Some((mapname, x as i16, y as i16, d as i16))
    })();
    let Some((mapname, x, y, dir)) = parsed else {
        log::error!("npc_parse_duplicate: Invalid placement format for duplicate in file '{FILEPATH}', line '1'. Skipping line...");
        return;
    };
    let m = crate::data::maps().id_of(&mapname).map(|v| v as i32).unwrap_or(-1);
    if m == -1 {
        // (map_getmapdata(-1) is nullptr and npc_create_npc would place the NPC nowhere: npc_duplicate4instance always
        // names an existing instance map)
        return;
    }
    let md = crate::data::maps().get(m as u16);
    if let Some(md) = md {
        if x < 0 || x >= md.xs || y < 0 || y >= md.ys {
            log::error!("npc_parse_duplicate: coordinates {x}/{y} are out of bounds in map {}({}x{}), in file '{FILEPATH}', line '1'", md.name, md.xs, md.ys);
        }
    }
    let (mut xs, mut ys) = (-1i16, -1i16);
    if kind == NPCTYPE_SCRIPT {
        // "%*[^,],%6hd,%6hd": <sprite id>,<triggerX>,<triggerY>
        let mut s4 = crate::cstr::Scan::new(w4.as_bytes());
        let _ = s4.set(usize::MAX, b",");
        if s4.lit(b',') {
            if let (Some(a), true, Some(b)) = (s4.int(6), s4.lit(b','), s4.int(6)) {
                xs = a as i16;
                ys = b as i16;
            }
        }
    }
    let id = crate::mob::spawn::npc_get_new_npc_id(c);
    // npc_parsename: <Display name>::<Unique name>
    let (name, exname) = match w3.find("::") {
        Some(i) => {
            let disp = &w3[..i];
            let disp: String = if disp.chars().count() > NPC_NAME_LENGTH {
                log::warn!("npc_parsename: Display name of '{w3}' is too long (len={}) in file '{FILEPATH}', line'1'. Truncating to {NPC_NAME_LENGTH} characters.", disp.chars().count());
                disp.chars().take(NPC_NAME_LENGTH).collect()
            } else {
                disp.to_string()
            };
            (disp, w3[i + 2..].chars().take(NPC_NAME_LENGTH).collect::<String>())
        }
        None => (w3.chars().take(NPC_NAME_LENGTH).collect::<String>(), w3.chars().take(NPC_NAME_LENGTH).collect::<String>()),
    };
    let mut exname = exname;
    if exname.is_empty() || exname.contains("::") {
        let newname = format!("0_{m}_{x}_{y}");
        log::warn!("npc_parsename: Invalid unique name in file '{FILEPATH}', line'1'. Renaming '{exname}' to '{newname}'.");
        exname = newname;
    }
    if crate::script::npc_run::npc_name2id(c, &exname).is_some() {
        // duplicate unique name, generate new one
        let mut i = 0;
        let newname = loop {
            i += 1;
            let n = format!("{i}_{m}_{x}_{y}");
            if crate::script::npc_run::npc_name2id(c, &n).is_none() {
                break n;
            }
        };
        log::warn!("npc_parsename: Duplicate unique name in file '{FILEPATH}', line'1'. Renaming '{exname}' to '{newname}'.");
        exname = newname;
    }
    // nd->class_ = npc_parseview(w4): the number before the first ','
    let class = npc_parseview_num(w4);
    let mut nd = Npc {
        id,
        exname,
        name,
        subtype: kind,
        map: m,
        x,
        y,
        class,
        src_id,
        xs: -1,
        ys: -1,
        warp_map: -1,
        warp_x: 0,
        warp_y: 0,
        trigger_on_hidden: false,
        state: NPCVIEW_ENABLE,
        is_invisible: false,
        path: FILEPATH.to_string(),
        script_file: 0,
        script_start: 0,
        script_end: 0,
        script_line: 0,
        shop: Vec::new(),
        shop_discount: false,
        itemshop_nameid: 0,
        pointshop_str: String::new(),
        owner_char: 0,
        touch_order: 0,
    };
    match kind {
        NPCTYPE_SCRIPT => {
            nd.xs = xs;
            nd.ys = ys;
            // nd->u.scr.script = dnd->u.scr.script (the code is resolved through src_id); label_list likewise
            nd.script_file = dnd.script_file;
            nd.script_start = dnd.script_start;
            nd.script_end = dnd.script_end;
            nd.script_line = dnd.script_line;
        }
        NPCTYPE_SHOP | NPCTYPE_CASHSHOP | NPCTYPE_ITEMSHOP | NPCTYPE_POINTSHOP | NPCTYPE_MARKETSHOP => {
            // safestrncpy(dst, src, strlen(src)): n counts the terminator, so the last character is lost
            let mut ps = dnd.pointshop_str.clone();
            ps.pop();
            nd.pointshop_str = ps;
            nd.itemshop_nameid = dnd.itemshop_nameid;
            // nd->u.shop.shop_item = dnd->u.shop.shop_item: the same list (npc.rs shop families)
            nd.shop = dnd.shop.clone();
            nd.shop_discount = dnd.shop_discount;
            let root = if dnd.src_id != 0 { dnd.src_id } else { dnd.id };
            let mut f = c.db().npc_shop_family().root().find(root).unwrap_or(crate::npc::NpcShopFamily { root, members: vec![root] });
            f.members.push(id);
            if c.db().npc_shop_family().root().find(root).is_some() {
                c.db().npc_shop_family().root().update(f);
            } else {
                c.db().npc_shop_family().insert(f);
            }
        }
        _ => {}
    }
    // Add the npc to its location
    add_to_map(c, nd.clone(), dir as u8);
    // copy the original npc state
    if dnd.state != NPCVIEW_ENABLE {
        crate::script::buildins::npcctl::npc_enable_target(c, id, 0, dnd.state);
    }
    set_state(c, id, dnd.state);
    if kind != NPCTYPE_SCRIPT {
        return;
    }
    // Loop through labels to export them as necessary
    let Some(nd) = c.db().npc().id().find(id) else { return };
    let instance_id = crate::instance_map::map_instance_id(c, m as u16);
    crate::script::npc_run::export_labels_rt(c, &nd, instance_id > 0, "npc_parse_duplicate");
    // Instance NPCs will use this for commands
    c.db().npc_instance().insert(NpcInstance { npc_id: id, instance_id });
}

/// npc.cpp npc_parseview for the number npc_duplicate4instance writes: strtol(viewid, &pid, 0) over the text before the
/// first blank, '/' or ','.
fn npc_parseview_num(w4: &str) -> i32 {
    let end = w4.find(|ch: char| ch.is_ascii_whitespace() || ch == '/' || ch == ',').unwrap_or(w4.len());
    let v = &w4[..end];
    match v.parse::<i64>() {
        Ok(n) => n as i32,
        Err(_) => crate::npc::JT_INVISIBLE,
    }
}

/// npc.cpp npc_instanceinit: the NPC's "exname::OnInstanceInit" event, without a player.
pub fn npc_instanceinit(c: &Ctx, nd_id: u32) -> i32 {
    run_instance_event(c, nd_id, "OnInstanceInit");
    0
}

/// npc.cpp npc_instancedestroy: the NPC's "exname::OnInstanceDestroy" event, without a player.
pub fn npc_instancedestroy(c: &Ctx, nd_id: u32) -> i32 {
    run_instance_event(c, nd_id, "OnInstanceDestroy");
    0
}

/// `strdb_get(ev_db, "exname::<event>")` then run_script(nd->u.scr.script, ev->pos, 0, nd->id).
fn run_instance_event(c: &Ctx, nd_id: u32, event: &str) {
    let Some(nd) = c.db().npc().id().find(nd_id) else { return };
    let evname: String = format!("{}::{}", nd.exname, event).chars().take(crate::script::npc_run::EVENT_NAME_LENGTH - 1).collect();
    let Some(ev) = c.db().npc_event().name().find(&evname) else { return };
    crate::script::npc_run::run_npc(c, nd_id, ev.pos, 0);
}

use crate::npc::npc_shop_family as _;
use crate::script::npc_run::npc_event as _;
use crate::unit::unit as _;
