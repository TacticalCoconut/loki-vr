//! pc.cpp pc_disguise and the disguise commands (atcmd agent, wave D): @disguise, @undisguise, @disguiseall,
//! @undisguiseall, @disguiseguild, @undisguiseguild, and the `disguise` / `undisguise` script commands.
//!
//! sd->disguise is `pc_atcmd.disguise` (atcmd_pc.rs). A disguised player looks like the monster / NPC to everyone
//! else; its own client is sent the packets about itself a second time with the id negated (clif.cpp
//! disguised_bl_id), which draws the disguise on top of the character: the `disguised(bl)` branches of clif.cpp are in
//! the module's senders (clif.rs and the others), each calling [`disguised`].

use crate::atcommand_atcmd::Out;
use crate::cstr::Scan;
use crate::data::msg_txt;
use crate::pc;
use crate::unit::{self, Unit};
use crate::world::Ctx;

/// Runs `func` when it is one of this file's commands; None otherwise.
pub fn run(c: &Ctx, fd: Option<&Unit>, sd: u32, func: &str, _command: &str, message: &str) -> Option<i32> {
    let out = Out { c, fd };
    Some(match func {
        "disguise" => disguise(c, &out, sd, message),
        "undisguise" => undisguise(c, &out, sd),
        "disguiseall" => disguiseall(c, &out, message),
        "undisguiseall" => undisguiseall(c, &out),
        "disguiseguild" => disguiseguild(c, &out, message),
        "undisguiseguild" => undisguiseguild(c, &out, message),
        _ => return None,
    })
}

/// sd->disguise: the class the player is disguised as (0: none).
pub fn disguise_of(c: &Ctx, aid: u32) -> i32 {
    crate::atcmd_pc::get(c, aid).disguise
}

/// clif.cpp disguised(bl): a disguised player.
pub fn disguised(c: &Ctx, bl: &Unit) -> bool {
    bl.kind == crate::map::BL_PC && disguise_of(c, bl.id) != 0
}

/// clif.cpp disguised_bl_id: the negated id the disguised player's own client knows the disguise by.
pub fn disguised_bl_id(id: u32) -> u32 {
    (id as i32).wrapping_neg() as u32
}

/// pc.cpp pc_disguise: the player takes the look of `class_` (0: its own again). 0 nothing to do, 2 invisible (the
/// view data follows when it becomes visible), 1 done: the player vanishes from view, gets the new view data and
/// option, and appears again (its cart list comes back on undisguise, a chat room it owns is shown again).
pub fn pc_disguise(c: &Ctx, aid: u32, class_: i32) -> i32 {
    let Some(u) = unit::get(c, aid) else { return 0 };
    let current = disguise_of(c, aid);
    if class_ == 0 && current == 0 {
        return 0;
    }
    if class_ != 0 && current == class_ {
        return 0;
    }
    let set = |v: i32| {
        let mut r = crate::atcmd_pc::get(c, aid);
        r.disguise = v;
        crate::atcmd_pc::put(c, &r);
    };
    if u.sc.option & crate::unit::OPTION_INVISIBLE != 0 {
        // Character is invisible. Stealth class-change. [Skotlex]
        set(class_); // viewdata is set on uncloaking.
        return 2;
    }
    if u.on_map {
        let mut u = u.clone();
        unit::stop_walking(c, &mut u, unit::USW_NONE, 0);
        unit::save(c, &u);
        crate::clif::clearunit_area(c, &u, unit::CLR_OUTSIGHT);
    }
    let own_class = pc::character_of(c, aid).map(|ch| ch.class as i32).unwrap_or(0);
    let class_ = if class_ == 0 {
        set(0);
        own_class
    } else {
        set(class_);
        class_
    };
    crate::skilltree::status_set_viewdata_pc(c, aid, class_);
    if let Some(u) = unit::get(c, aid) {
        crate::clif::changeoption(c, &u, u.sc.option, None);
    }
    if let Some(u) = unit::get(c, aid).filter(|u| u.on_map) {
        crate::clif::spawn(c, &u, false);
        if class_ == own_class && crate::inventory::pc_iscarton(c, aid) {
            // It seems the cart info is lost on undisguise.
            crate::clif_items::cartlist(c, aid);
            crate::inventory::updatestatus(c, aid, crate::inventory::SP_CARTINFO);
        }
        let chat_id = pc::get(c, aid).map(|p| p.chat_id).unwrap_or(0);
        if chat_id != 0 {
            if let Some(cd) = crate::chat::get(c, chat_id) {
                crate::chat::clif_dispchat(c, &cd);
            }
        }
    }
    1
}

/// A monster id (mobdb_checkid) or an NPC class (npcdb_checkid).
fn valid_class(id: i32) -> bool {
    crate::db::mobs::mobdb_checkid(id) != 0 || crate::unit_npc::npcdb_checkid(id)
}

/// mob.cpp mobdb_searchname (full name comparison; slave monsters without a spawn excluded).
fn mobdb_searchname(c: &Ctx, name: &str) -> i32 {
    crate::atcommand_atcmd::admin::mobdb_searchname(c, name) as i32
}

/// The id of @disguise / @disguiseguild: a number (checked) or a monster name, else an NPC name's class.
fn class_of(c: &Ctx, s: &str) -> i32 {
    let id = crate::cstr::atoi(s);
    if id > 0 {
        // Acquired an ID
        if valid_class(id) {
            id
        } else {
            0 // Invalid id for either mobs or npcs.
        }
    } else {
        // Acquired a Name
        match mobdb_searchname(c, s) {
            0 => crate::script::npc_run::npc_name2id(c, s).map(|nd| nd.class).unwrap_or(0),
            id => id,
        }
    }
}

/// pc_isriding: OPTION_RIDING.
fn is_riding(c: &Ctx, aid: u32) -> bool {
    unit::get(c, aid).map(|u| u.sc.option & crate::status::defs::OPTION_RIDING != 0).unwrap_or(false)
}

/// ACMD_FUNC(disguise): @disguise <monster / NPC name or id>.
fn disguise(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    use crate::status::consts::{SC_ACTIVE_MONSTER_TRANSFORM, SC_MONSTER_TRANSFORM};
    if message.is_empty() {
        out.say(msg_txt(1143)); // Please enter a Monster/NPC name/ID (usage: @disguise <name/ID>).
        return -1;
    }
    let id = class_of(c, message);
    if id == 0 {
        out.say(msg_txt(123)); // Invalid Monster/NPC name/ID specified.
        return -1;
    }
    if is_riding(c, sd) {
        out.say(msg_txt(1144)); // Character cannot be disguised while mounted.
        return -1;
    }
    if crate::status::sc::has_sce(c, sd, SC_MONSTER_TRANSFORM) || crate::status::sc::has_sce(c, sd, SC_ACTIVE_MONSTER_TRANSFORM) {
        out.say(msg_txt(730)); // Character cannot be disguised while in monster transform.
        return -1;
    }
    pc_disguise(c, sd, id);
    out.say(msg_txt(122)); // Disguise applied.
    0
}

/// ACMD_FUNC(disguiseall): every player, as the monster (name checked first) or NPC class.
fn disguiseall(c: &Ctx, out: &Out, message: &str) -> i32 {
    if message.is_empty() {
        out.say(msg_txt(1145)); // Please enter a Monster/NPC name/ID (usage: @disguiseall <name/ID>).
        return -1;
    }
    // check name first (to avoid possible name beginning by a number)
    let mut mob_id = mobdb_searchname(c, message);
    if mob_id == 0 {
        mob_id = crate::cstr::atoi(message);
    }
    if !valid_class(mob_id) {
        out.say(msg_txt(123)); // Monster/NPC name/id not found.
        return -1;
    }
    for pid in all_users(c) {
        pc_disguise(c, pid, mob_id);
    }
    out.say(msg_txt(122)); // Disguise applied.
    0
}

/// mapit_getallusers order.
fn all_users(c: &Ctx) -> Vec<u32> {
    use crate::pc::pc as _;
    use spacetimedb::Table as _;
    let mut ids: Vec<u32> = c.db().pc().iter().map(|p| p.account_id).collect();
    ids.sort_by_key(|&id| crate::party::idb_order(id));
    ids
}

/// The online members of a guild (g->guild.member[i].sd), in member slot order.
fn guild_member_sds(g: &crate::guild::MapGuild) -> Vec<u32> {
    g.guild.member.iter().take(g.guild.max()).map(|m| m.sd).filter(|&sd| sd != 0).collect()
}

/// ACMD_FUNC(disguiseguild): @disguiseguild <monster name / id>, <guild name / id>: the guild's online members that
/// are not riding.
fn disguiseguild(c: &Ctx, out: &Out, message: &str) -> i32 {
    let b = crate::net::wire::latin1(message);
    // sscanf(message, "%23[^,], %23[^\r\n]", monster, guild)
    let mut s = Scan::new(&b);
    let monster = s.set(23, b",");
    let guild = monster.as_ref().and_then(|_| {
        if !s.lit(b',') {
            return None;
        }
        s.ws();
        s.set(23, b"\r\n")
    });
    let (Some(monster), Some(guild)) = (monster, guild) else {
        out.say(msg_txt(1146)); // Please enter a mob name/ID and guild name/ID (usage: @disguiseguild <mob name/ID>, <guild name/ID>).
        return -1;
    };
    let id = class_of(c, &monster);
    if id == 0 {
        out.say(msg_txt(123)); // Monster/NPC name/id hasn't been found.
        return -1;
    }
    let Some(g) = crate::guild::guild_searchnameid(c, &guild) else {
        out.say(msg_txt(94)); // Incorrect name/ID, or no one from the guild is online.
        return -1;
    };
    for pid in guild_member_sds(&g) {
        if !is_riding(c, pid) {
            pc_disguise(c, pid, id);
        }
    }
    out.say(msg_txt(122)); // Disguise applied.
    0
}

/// ACMD_FUNC(undisguise).
fn undisguise(c: &Ctx, out: &Out, sd: u32) -> i32 {
    if disguise_of(c, sd) != 0 {
        pc_disguise(c, sd, 0);
        out.say(msg_txt(124)); // Undisguise applied.
        0
    } else {
        out.say(msg_txt(125)); // You're not disguised.
        -1
    }
}

/// ACMD_FUNC(undisguiseall): every disguised player.
fn undisguiseall(c: &Ctx, out: &Out) -> i32 {
    for pid in all_users(c) {
        if disguise_of(c, pid) != 0 {
            pc_disguise(c, pid, 0);
        }
    }
    out.say(msg_txt(124)); // Undisguise applied.
    0
}

/// ACMD_FUNC(undisguiseguild): @undisguiseguild <guild name / id>: the guild's disguised online members.
fn undisguiseguild(c: &Ctx, out: &Out, message: &str) -> i32 {
    let b = crate::net::wire::latin1(message);
    let Some(guild_name) = Scan::new(&b).set(23, b"\n").filter(|_| !message.is_empty()) else {
        out.say(msg_txt(1147)); // Please enter guild name/ID (usage: @undisguiseguild <guild name/ID>).
        return -1;
    };
    let Some(g) = crate::guild::guild_searchnameid(c, &guild_name) else {
        out.say(msg_txt(94)); // Incorrect name/ID, or no one from the guild is online.
        return -1;
    };
    for pid in guild_member_sds(&g) {
        if disguise_of(c, pid) != 0 {
            pc_disguise(c, pid, 0);
        }
    }
    out.say(msg_txt(124)); // Undisguise applied.
    0
}

// ---------------------------------------------------------------------------------------------- script commands

/// The disguise script commands (script.cpp), chained into script/buildins/mod.rs lookup_cfunc.
pub fn lookup(cfunc: &str) -> Option<crate::script::buildins::BuildinFn> {
    Some(match cfunc {
        "disguise" => buildin_disguise,
        "undisguise" => buildin_undisguise,
        _ => return None,
    })
}

/// BUILDIN_FUNC(disguise): disguise <Monster ID>{,<char_id>}: pc_disguise, pushes the id (0 when it is not a monster /
/// NPC class).
fn buildin_disguise(rt: &crate::script::vm::Rt, st: &mut crate::script::vm::State) -> i32 {
    use crate::script::vm::*;
    let Some(aid) = crate::script::buildins::charid2sd(rt, st, 3, "buildin_disguise") else { return SCRIPT_CMD_FAILURE };
    let id = crate::script::vm::getnum(rt, st, 2);
    if valid_class(id) {
        pc_disguise(rt.c.unwrap(), aid, id);
        st.pushint(id as i64);
    } else {
        st.pushint(0);
    }
    SCRIPT_CMD_SUCCESS
}

/// BUILDIN_FUNC(undisguise): undisguise {<char_id>}: pushes 0 when the player was disguised (and is no longer), 1 when
/// it was not (rAthena's values, the reverse of its comment).
fn buildin_undisguise(rt: &crate::script::vm::Rt, st: &mut crate::script::vm::State) -> i32 {
    use crate::script::vm::*;
    let Some(aid) = crate::script::buildins::charid2sd(rt, st, 2, "buildin_undisguise") else { return SCRIPT_CMD_FAILURE };
    let c = rt.c.unwrap();
    if disguise_of(c, aid) != 0 {
        pc_disguise(c, aid, 0);
        st.pushint(0);
    } else {
        st.pushint(1);
    }
    SCRIPT_CMD_SUCCESS
}
