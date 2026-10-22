//! Marriage and family: marriage, wedding (wedding_effect), divorce, ispartneron, getpartnerid, getchildid,
//! getmotherid, getfatherid, warppartner, adopt; pc_marriage / pc_divorce (with the char server's
//! char_divorce_char_sql + chrif_divorceack when the partner is offline), pc_try_adopt / pc_adoption and the adoption
//! packets (clif_Adopt_request / _reply, clif_parse_Adopt_request / _reply).

use crate::character::character as _;
use crate::character::Character;
use crate::db::jobs::ids::*;
use crate::script::buildins::*;
use crate::script::vm::*;
use crate::inventory::inventory as _;
use crate::unit;
use crate::world::Ctx;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "marriage" => marriage,
        "wedding_effect" => wedding_effect,
        "divorce" => divorce,
        "ispartneron" => ispartneron,
        "getpartnerid" => getpartnerid,
        "getchildid" => getchildid,
        "getmotherid" => getmotherid,
        "getfatherid" => getfatherid,
        "warppartner" => warppartner,
        "adopt" => adopt,
        _ => return None,
    })
}

/// item.hpp WEDDING_RING_M / WEDDING_RING_F.
pub const WEDDING_RING_M: u32 = 2634;
pub const WEDDING_RING_F: u32 = 2635;

/// pc.hpp enum adopt_responses.
pub const ADOPT_ALLOWED: i32 = 0;
pub const ADOPT_ALREADY_ADOPTED: i32 = 1;
pub const ADOPT_MARRIED_AND_PARTY: i32 = 2;
pub const ADOPT_EQUIP_RINGS: i32 = 3;
pub const ADOPT_NOT_NOVICE: i32 = 4;
pub const ADOPT_CHARACTER_NOT_FOUND: i32 = 5;
pub const ADOPT_MORE_CHILDREN: i32 = 6;
pub const ADOPT_LEVEL_70: i32 = 7;
pub const ADOPT_MARRIED: i32 = 8;
/// clif.hpp enum adopt_replies.
const ADOPT_REPLY_MORE_CHILDREN: i32 = 0;
const ADOPT_REPLY_LEVEL_70: i32 = 1;
const ADOPT_REPLY_MARRIED: i32 = 2;

/// sd->status of an online player (the character row).
fn status(c: &Ctx, aid: u32) -> Option<Character> {
    crate::pc::character_of(c, aid)
}

fn put_status(c: &Ctx, ch: Character) {
    c.db().character().id().update(ch);
}

/// sd->class_ (MAPID).
fn class_of(ch: &Character) -> u64 {
    pc_jobid2mapid(ch.class)
}

/// map_charid2sd: the account id of the online character.
fn charid2aid(c: &Ctx, char_id: u32) -> Option<u32> {
    if char_id == 0 {
        return None;
    }
    crate::pc::charid2sd(c, char_id).map(|u| u.id)
}

// ---------------------------------------------------------------------------------------------- marriage

/// pc.cpp pc_marriage.
pub fn pc_marriage(c: &Ctx, aid: u32, dst: u32) -> bool {
    let (Some(mut s), Some(mut d)) = (status(c, aid), status(c, dst)) else { return false };
    if s.partner_id > 0 || d.partner_id > 0 || class_of(&s) & JOBL_BABY != 0 || class_of(&d) & JOBL_BABY != 0 {
        return false;
    }
    s.partner_id = d.id;
    d.partner_id = s.id;
    put_status(c, s);
    put_status(c, d);
    crate::achievement::achievement_update_objective(c, Some(aid), crate::db::misc::achievement::AG_MARRY, &[1]);
    crate::achievement::achievement_update_objective(c, Some(dst), crate::db::misc::achievement::AG_MARRY, &[1]);
    true
}

/// marriage "<partner name>": the attached player and the named one marry (pc_marriage). Pushes 1 on success.
fn marriage(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    let ok = match rid2sd(rt, st, "buildin_marriage") {
        Some(aid) => {
            let name = lat(&getstr(rt, st, 2));
            match crate::pc::nick2sd(c, &name, false) {
                Some(p) => pc_marriage(c, aid, p.id),
                None => false,
            }
        }
        None => false,
    };
    st.pushint(ok as i64);
    SCRIPT_CMD_SUCCESS
}

/// wedding: clif_wedding_effect (ZC_CONGRATULATION 0x01ea) around the attached player, else the NPC.
fn wedding_effect(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    let bl = match rid2sd(rt, st, "buildin_wedding_effect") {
        Some(aid) => unit::get(c, aid),
        None => unit::get(c, st.oid),
    };
    if let Some(bl) = bl {
        crate::atcommand_atcmd::inv::clif_wedding_effect(c, &bl); // (one port: @marry's)
    }
    SCRIPT_CMD_SUCCESS
}

// ---------------------------------------------------------------------------------------------- divorce

/// clif.cpp clif_divorced: ZC_DIVORCE (0x0205) with the former partner's name.
fn clif_divorced(c: &Ctx, aid: u32, name: &str) {
    let mut w = Vec::with_capacity(26);
    w.extend_from_slice(&0x0205u16.to_le_bytes());
    let mut n = crate::net::wire::latin1(name);
    n.truncate(23);
    n.resize(24, 0);
    w.extend_from_slice(&n);
    if let Some(sd) = unit::get(c, aid) {
        crate::clif::send(c, &w, &sd, crate::clif::Target::SelfOnly);
    }
}

/// `for (i = 0; i < MAX_INVENTORY; i++) if (ring) pc_delitem(sd, i, 1, 0, 0, LOG_TYPE_OTHER)` for one player.
fn delete_rings_slot(c: &Ctx, aid: u32, i: usize) {
    let is_ring = crate::inventory::inv_get(c, aid, i).map(|r| r.item.nameid == WEDDING_RING_M || r.item.nameid == WEDDING_RING_F).unwrap_or(false);
    if is_ring {
        crate::inventory::pc_delitem(c, aid, i as i32, 1, 0, 0, crate::map_log::LOG_TYPE_OTHER);
    }
}

/// pc.cpp pc_divorce.
pub fn pc_divorce(c: &Ctx, aid: u32) -> bool {
    let Some(mut s) = status(c, aid) else { return false };
    if s.partner_id == 0 {
        return false; // Char is not married
    }
    let Some(paid) = charid2aid(c, s.partner_id) else {
        // Lets char server do the divorce (chrif_divorce, 0x2b11): answered after this request, as the char server's
        // reply arrives later
        crate::timer::add(c, c.tick(), crate::timer::Func::ChrifDivorce, s.id, s.partner_id as i64);
        return true;
    };
    let Some(mut p) = status(c, paid) else { return false };
    // Both players online, lets do the divorce manually
    s.partner_id = 0;
    p.partner_id = 0;
    let (sname, pname) = (s.name.clone(), p.name.clone());
    put_status(c, s);
    put_status(c, p);
    for i in 0..crate::inventory::MAX_INVENTORY {
        delete_rings_slot(c, aid, i);
        delete_rings_slot(c, paid, i);
    }
    clif_divorced(c, aid, &pname);
    clif_divorced(c, paid, &sname);
    true
}

/// char.cpp char_divorce_char_sql + chrif.cpp chrif_divorceack (Func::ChrifDivorce: target = the requester's char id,
/// data = the partner's). Both rows lose their partner; `DELETE … wedding rings … (char_id = a OR char_id = b) LIMIT 2`
/// takes the first two ring rows of the table (the saved rows, in save order) — for a character that is online its
/// live inventory is the map server's, and the ack takes the rings out of it (pc_delitem, with the packets).
pub fn chrif_divorce_timer(c: &Ctx, cid1: u32, cid2: u32) {
    let online = |cid: u32| charid2aid(c, cid);
    // the map server's sd->status.partner_id before the ack (the rows are the online players' status here)
    let before = |cid: u32| c.db().character().id().find(cid).map(|ch| ch.partner_id).unwrap_or(0);
    let (p1_before, p2_before) = (before(cid1), before(cid2));
    // UPDATE `char` SET `partner_id`='0' WHERE `char_id`=a OR `char_id`=b LIMIT 2
    for cid in [cid1, cid2] {
        if let Some(mut ch) = c.db().character().id().find(cid) {
            if ch.partner_id != 0 {
                ch.partner_id = 0;
                put_status(c, ch);
            }
        }
    }
    // DELETE FROM `inventory` WHERE (`nameid`=M OR `nameid`=F) AND (`char_id`=a OR `char_id`=b) LIMIT 2
    let mut rows: Vec<crate::inventory::InventoryItem> = [cid1, cid2]
        .iter()
        .flat_map(|&cid| crate::inventory::inv_list_char(c, cid))
        .filter(|r| r.save_id > 0 && (r.item.nameid == WEDDING_RING_M || r.item.nameid == WEDDING_RING_F))
        .collect();
    rows.sort_by_key(|r| r.save_id);
    for r in rows.into_iter().take(2) {
        if online(r.char_id).is_none() {
            c.db().inventory().id().delete(r.id);
        }
    }
    // chmapif_send_ackdivorce -> chrif_divorceack(char_id, partner_id)
    if let Some(aid) = online(cid1).filter(|_| p1_before == cid2) {
        for i in 0..crate::inventory::MAX_INVENTORY {
            delete_rings_slot(c, aid, i);
        }
    }
    if let Some(aid) = online(cid2).filter(|_| p2_before == cid1) {
        for i in 0..crate::inventory::MAX_INVENTORY {
            delete_rings_slot(c, aid, i);
        }
    }
}

/// char.cpp char_delete's de-adoption of a deleted baby (char.cpp:1619-1634: `UPDATE char SET child=0` and
/// `DELETE FROM skill WHERE id=410` for both parents, then 0x2b25 to the map servers) with chrif.cpp chrif_deadopt for
/// the parents that are online. The character rows are the online players' sd->status here, so an online parent
/// changes only as chrif_deadopt changes it (when its child is this baby: its map server's next save would restore
/// the SQL otherwise), an offline one as the SQL does.
pub fn char_deadopt(c: &Ctx, father_id: u32, mother_id: u32, child_id: u32) {
    use crate::db::skills::WE_CALLBABY;
    use crate::skill::char_skill;
    for pid in [father_id, mother_id] {
        if pid == 0 {
            continue;
        }
        match charid2aid(c, pid) {
            Some(aid) => {
                let Some(mut ch) = status(c, aid) else { continue };
                if ch.child != child_id {
                    continue;
                }
                ch.child = 0;
                put_status(c, ch);
                // sd->status.skill[skill_get_index(WE_CALLBABY)] = { 0, 0, SKILL_FLAG_PERMANENT }
                if let Some(mut s) = crate::skilltree::Skills::load(c, aid) {
                    let idx = crate::db::skills::skill_db().get_index(WE_CALLBABY, true);
                    s.put(idx, crate::skilltree::Slot::default());
                    s.save(c);
                }
                crate::clif_skill::clif_deleteskill(c, aid, WE_CALLBABY, false);
            }
            None => {
                if let Some(mut ch) = c.db().character().id().find(pid) {
                    if ch.child != 0 {
                        ch.child = 0;
                        put_status(c, ch);
                    }
                }
                let rows: Vec<u64> = c.db().char_skill().by_char().filter(pid).filter(|r| r.skill_id == WE_CALLBABY).map(|r| r.id).collect();
                for id in rows {
                    c.db().char_skill().id().delete(id);
                }
            }
        }
    }
}

/// divorce({<char id>}): pc_divorce. Pushes 1 on success.
fn divorce(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    match charid2sd(rt, st, 2, "buildin_divorce") {
        Some(aid) if pc_divorce(c, aid) => {
            st.pushint(1);
            SCRIPT_CMD_SUCCESS
        }
        _ => {
            st.pushint(0);
            SCRIPT_CMD_FAILURE
        }
    }
}

/// ispartneron({<char id>}): the partner is online.
fn ispartneron(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    let on = charid2sd(rt, st, 2, "buildin_ispartneron").and_then(|aid| status(c, aid)).map(|s| s.partner_id > 0 && charid2aid(c, s.partner_id).is_some()).unwrap_or(false);
    st.pushint(on as i64);
    if on {
        SCRIPT_CMD_SUCCESS
    } else {
        SCRIPT_CMD_FAILURE
    }
}

fn family_value(rt: &Rt, st: &mut State, func: &str, f: fn(&Character) -> u32) -> i32 {
    let c = rt.c.unwrap();
    match charid2sd(rt, st, 2, func).and_then(|aid| status(c, aid)) {
        Some(s) => {
            st.pushint(f(&s) as i64);
            SCRIPT_CMD_SUCCESS
        }
        None => {
            st.pushint(0);
            SCRIPT_CMD_FAILURE
        }
    }
}

fn getpartnerid(rt: &Rt, st: &mut State) -> i32 {
    family_value(rt, st, "buildin_getpartnerid", |s| s.partner_id)
}

fn getchildid(rt: &Rt, st: &mut State) -> i32 {
    family_value(rt, st, "buildin_getchildid", |s| s.child)
}

fn getmotherid(rt: &Rt, st: &mut State) -> i32 {
    family_value(rt, st, "buildin_getmotherid", |s| s.mother)
}

fn getfatherid(rt: &Rt, st: &mut State) -> i32 {
    family_value(rt, st, "buildin_getfatherid", |s| s.father)
}

/// warppartner "<map>",<x>,<y>: pc_setpos of the online partner (CLR_OUTSIGHT). Pushes 1 when the map exists.
fn warppartner(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    let paid = rid2sd(rt, st, "buildin_warppartner").and_then(|aid| status(c, aid)).filter(|s| s.partner_id > 0).and_then(|s| charid2aid(c, s.partner_id));
    let Some(paid) = paid else {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    let name = getstr(rt, st, 2);
    let x = getnum(rt, st, 3);
    let y = getnum(rt, st, 4);
    match crate::data::maps().id_of(&lat(&name)) {
        // mapindex_name2id
        Some(m) => {
            super::mapctl::setpos32(c, paid, m, x, y, unit::CLR_OUTSIGHT);
            st.pushint(1);
        }
        None => st.pushint(0),
    }
    SCRIPT_CMD_SUCCESS
}

// ---------------------------------------------------------------------------------------------- adoption

/// clif.cpp clif_Adopt_reply: ZC_BABYMSG (0x0216).
fn clif_adopt_reply(c: &Ctx, aid: u32, kind: i32) {
    let mut w = Vec::with_capacity(6);
    w.extend_from_slice(&0x0216u16.to_le_bytes());
    w.extend_from_slice(&kind.to_le_bytes());
    if let Some(sd) = unit::get(c, aid) {
        crate::clif::send(c, &w, &sd, crate::clif::Target::SelfOnly);
    }
}

/// clif.cpp clif_Adopt_request: ZC_REQ_BABY (0x01f6) to the baby: the parent's account id, the other parent's
/// account id, the parent's name.
pub(crate) fn clif_adopt_request(c: &Ctx, baby: u32, src: u32, p_id: u32) {
    let name = status(c, src).map(|s| s.name).unwrap_or_default();
    let mut w = Vec::with_capacity(34);
    w.extend_from_slice(&0x01f6u16.to_le_bytes());
    w.extend_from_slice(&src.to_le_bytes());
    w.extend_from_slice(&p_id.to_le_bytes());
    let mut n = crate::net::wire::latin1(&name);
    n.truncate(23);
    n.resize(24, 0);
    w.extend_from_slice(&n);
    if let Some(sd) = unit::get(c, baby) {
        crate::clif::send(c, &w, &sd, crate::clif::Target::SelfOnly);
    }
}

/// pc.cpp pc_try_adopt (p1, p2, baby: account ids of online players).
pub fn pc_try_adopt(c: &Ctx, p1: Option<u32>, p2: Option<u32>, b: Option<u32>) -> i32 {
    let (Some(p1), Some(p2), Some(b)) = (p1, p2, b) else { return ADOPT_CHARACTER_NOT_FOUND };
    let (Some(s1), Some(s2), Some(sb)) = (status(c, p1), status(c, p2), status(c, b)) else { return ADOPT_CHARACTER_NOT_FOUND };
    if sb.father != 0 || sb.mother != 0 || crate::pc_misc::pcm(c, b).adopt_invite != 0 {
        return ADOPT_ALREADY_ADOPTED; // already adopted baby / in adopt request
    }
    if s1.partner_id == 0 || s1.party_id == 0 || s1.party_id != sb.party_id {
        return ADOPT_MARRIED_AND_PARTY; // You need to be married and in party with baby to adopt
    }
    if s1.partner_id != s2.id || s2.partner_id != s1.id {
        return ADOPT_MARRIED_AND_PARTY; // Not married, wrong married
    }
    if s2.party_id != s1.party_id {
        return ADOPT_MARRIED_AND_PARTY; // Both parents need to be in the same party
    }
    // Parents need to have their ring equipped
    let ring = |aid: u32| crate::pc_equip::pc_isequipped(c, aid, WEDDING_RING_M) || crate::pc_equip::pc_isequipped(c, aid, WEDDING_RING_F);
    if !ring(p1) || !ring(p2) {
        return ADOPT_EQUIP_RINGS;
    }
    // Already adopted a baby
    if s1.child != 0 || s2.child != 0 {
        clif_adopt_reply(c, p1, ADOPT_REPLY_MORE_CHILDREN);
        return ADOPT_MORE_CHILDREN;
    }
    // Parents need at least lvl 70 to adopt
    if s1.base_level < 70 || s2.base_level < 70 {
        clif_adopt_reply(c, p1, ADOPT_REPLY_LEVEL_70);
        return ADOPT_LEVEL_70;
    }
    if sb.partner_id != 0 {
        clif_adopt_reply(c, p1, ADOPT_REPLY_MARRIED);
        return ADOPT_MARRIED;
    }
    let class = sb.class as i32;
    if !((JOB_NOVICE..=JOB_THIEF).contains(&class) || class == JOB_SUPER_NOVICE || class == JOB_SUPER_NOVICE_E) {
        return ADOPT_NOT_NOVICE;
    }
    ADOPT_ALLOWED
}

/// pc.cpp pc_adoption: the baby becomes the baby class of its job (job level and job experience kept), the three
/// get their family ids and the family skills.
pub fn pc_adoption(c: &Ctx, p1: u32, p2: u32, b: u32) -> bool {
    if pc_try_adopt(c, Some(p1), Some(p2), Some(b)) != ADOPT_ALLOWED {
        return false;
    }
    let Some(sb) = status(c, b) else { return false };
    // Preserve current job levels and progress
    let (joblevel, jobexp) = (sb.job_level, sb.job_exp);
    let job = pc_mapid2jobid(pc_jobid2mapid(sb.class) | JOBL_BABY, sb.sex as i32);
    if job == -1 || !crate::skilltree::pc_jobchange(c, b, job, 0) {
        return false; // Job Change Fail
    }
    // Success, proceed to configure parents and baby skills
    let (Some(mut s1), Some(mut s2), Some(mut sb)) = (status(c, p1), status(c, p2), status(c, b)) else { return false };
    s1.child = sb.id;
    s2.child = sb.id;
    sb.father = s1.id;
    sb.mother = s2.id;
    // Restore progress
    sb.job_level = joblevel;
    sb.job_exp = jobexp;
    put_status(c, s1);
    put_status(c, s2);
    put_status(c, sb);
    crate::status::clif::updatestatus(c, b, crate::status::consts::SP_JOBLEVEL);
    crate::status::clif::updatestatus(c, b, crate::status::consts::SP_JOBEXP);
    // Baby Skills
    use crate::db::skills::ids::*;
    use crate::status::defs::ADDSKILL_PERMANENT;
    crate::skilltree::pc_skill(c, b, WE_BABY, 1, ADDSKILL_PERMANENT);
    crate::skilltree::pc_skill(c, b, WE_CALLPARENT, 1, ADDSKILL_PERMANENT);
    crate::skilltree::pc_skill(c, b, WE_CHEERUP, 1, ADDSKILL_PERMANENT);
    // Parents Skills
    crate::skilltree::pc_skill(c, p1, WE_CALLBABY, 1, ADDSKILL_PERMANENT);
    crate::skilltree::pc_skill(c, p2, WE_CALLBABY, 1, ADDSKILL_PERMANENT);
    crate::chrif::chrif_save(c, p1, crate::chrif::CSAVE_NORMAL);
    crate::chrif::chrif_save(c, p2, crate::chrif::CSAVE_NORMAL);
    crate::chrif::chrif_save(c, b, crate::chrif::CSAVE_NORMAL);
    use crate::db::misc::achievement::AG_BABY;
    crate::achievement::achievement_update_objective(c, Some(b), AG_BABY, &[1]);
    crate::achievement::achievement_update_objective(c, Some(p1), AG_BABY, &[2]);
    crate::achievement::achievement_update_objective(c, Some(p2), AG_BABY, &[2]);
    true
}

/// adopt "<parent>"|<parent char id>,"<baby>"|<baby char id>: pc_try_adopt with the parent's partner; when allowed
/// the baby gets the request (clif_Adopt_request). Pushes the adopt_responses value.
fn adopt(rt: &Rt, st: &mut State) -> i32 {
    let c = rt.c.unwrap();
    let find = |rt: &Rt, st: &mut State, i: i32, who: &str| -> Option<u32> {
        if isstring(rt, st, i) {
            let name = lat(&getstr(rt, st, i));
            let r = crate::pc::nick2sd(c, &name, false).map(|u| u.id);
            if r.is_none() {
                log::error!("buildin_adopt: Non-existant {who} character {name} requested.");
            }
            r
        } else {
            let char_id = getnum(rt, st, i) as u32;
            let r = charid2aid(c, char_id);
            if r.is_none() {
                log::error!("buildin_adopt: Non-existant {who} character {} requested.", char_id as i32);
            }
            r
        }
    };
    let Some(sd) = find(rt, st, 2, "parent") else { return SCRIPT_CMD_FAILURE };
    let Some(b_sd) = find(rt, st, 3, "baby") else { return SCRIPT_CMD_FAILURE };
    let partner = status(c, sd).and_then(|s| charid2aid(c, s.partner_id));
    let response = pc_try_adopt(c, Some(sd), partner, Some(b_sd));
    if response == ADOPT_ALLOWED {
        let p_sd = partner.unwrap();
        let mut m = crate::pc_misc::pcm(c, b_sd);
        m.adopt_invite = sd;
        crate::pc_misc::put_pcm(c, &m);
        clif_adopt_request(c, b_sd, sd, p_sd);
        st.pushint(ADOPT_ALLOWED as i64);
        return SCRIPT_CMD_SUCCESS;
    }
    st.pushint(response as i64);
    SCRIPT_CMD_FAILURE
}

/// clif.cpp clif_parse_Adopt_request (CZ_REQ_JOIN_BABY 0x01f9 <account id>.L): a parent asks a player.
pub fn clif_parse_adopt_request(c: &Ctx, aid: u32, pkt: &[u8]) {
    let pos = crate::net::packets::packet_pos(u16::from_le_bytes([pkt[0], pkt[1]]));
    let off = pos.first().copied().unwrap_or(2) as usize;
    let tid = pkt.get(off..off + 4).map(|b| u32::from_le_bytes([b[0], b[1], b[2], b[3]])).unwrap_or(0);
    let tsd = crate::pc::get(c, tid).map(|_| tid);
    let p_sd = status(c, aid).and_then(|s| charid2aid(c, s.partner_id));
    if pc_try_adopt(c, Some(aid), p_sd, tsd) == ADOPT_ALLOWED {
        let (Some(t), Some(p)) = (tsd, p_sd) else { return };
        let mut m = crate::pc_misc::pcm(c, t);
        m.adopt_invite = aid;
        crate::pc_misc::put_pcm(c, &m);
        clif_adopt_request(c, t, aid, p);
    }
}

/// clif.cpp clif_parse_Adopt_reply (CZ_JOIN_BABY 0x01f7 father_AID.L mother_AID.L accepted.L).
pub fn clif_parse_adopt_reply(c: &Ctx, aid: u32, pkt: &[u8]) {
    let rd = |o: usize| pkt.get(o..o + 4).map(|b| u32::from_le_bytes([b[0], b[1], b[2], b[3]])).unwrap_or(0);
    let (father, mother, accepted) = (rd(2), rd(6), rd(10));
    let mut m = crate::pc_misc::pcm(c, aid);
    let invite = m.adopt_invite;
    m.adopt_invite = 0;
    crate::pc_misc::put_pcm(c, &m);
    // Check if the adoption was accepted; the father and the mother have to be online; the father is the inviter
    if accepted == 0 || crate::pc::get(c, father).is_none() || crate::pc::get(c, mother).is_none() || invite != father {
        return;
    }
    pc_adoption(c, father, mother, aid);
}
