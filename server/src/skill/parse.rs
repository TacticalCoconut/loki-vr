//! The skill requests of clif.cpp: clif_parse_UseSkillToId (+ clif_parse_skill_toid), clif_parse_UseSkillToPos /
//! clif_parse_UseSkillToPosMoreInfo (+ clif_parse_UseSkillToPosSub), clif_parse_AutoSpell, clif_parse_SkillSelectMenu,
//! clif_parse_StartUseSkillToId / clif_parse_StopUseSkillToId. The field positions come from packet_db
//! (net/packets.rs packet_pos) as rAthena reads them. (clif_parse_UseSkillMap / skill_castend_map: skillunit agent.)
//! Homunculus skills (SKILL_CHK_HOMUN) go to clif_parse_UseSkillTo*_homun (skill/homun.rs); mercenary skills
//! (SKILL_CHK_MERC) to clif_parse_UseSkillTo*_mercenary (mercenary/skill.rs).

use crate::db::skills::*;
use crate::net::packets::packet_pos;
use crate::unit;
use crate::world::Ctx;

use super::clif::fail::*;
use super::clif::{clif_skill_fail, skill_fail};

/// pc.hpp e_idletime_option IDLE_USESKILLTOID / IDLE_USESKILLTOPOS.
const IDLE_USESKILLTOID: i32 = 0x0002;
const IDLE_USESKILLTOPOS: i32 = 0x0004;
/// clif.hpp MSI_BUSY (PACKETVER 20211103).
const MSI_BUSY: u16 = 1923;
/// clif.hpp MESSAGE_SIZE.
const MESSAGE_SIZE: usize = 80;

fn rw(pkt: &[u8], pos: u16) -> Option<u16> {
    let p = pos as usize;
    pkt.get(p..p + 2).map(|b| u16::from_le_bytes([b[0], b[1]]))
}

fn rl(pkt: &[u8], pos: u16) -> Option<u32> {
    let p = pos as usize;
    pkt.get(p..p + 4).map(|b| u32::from_le_bytes([b[0], b[1], b[2], b[3]]))
}

fn cmd_of(pkt: &[u8]) -> u16 {
    rw(pkt, 0).unwrap_or(0)
}

/// pc.hpp pc_cant_act2 (vending, buying store, opt1 but burning, trading, storage, prevend, npc shop, the item
/// windows — `Pc::ui_open`).
fn pc_cant_act2(p: &crate::pc::Pc, opt1: i16) -> bool {
    use crate::status::defs::OPT1_BURNING;
    p.vending || p.buyingstore || (opt1 != 0 && opt1 != OPT1_BURNING) || p.trading || p.storage_flag != 0 || p.prevend || p.ui_open != 0 || p.npc_shopid != 0
}

/// sd->idletime = last_tick when idletime_option has the flag.
fn touch_idletime(c: &Ctx, aid: u32, option: i32) {
    if crate::conf::idletime_option() & option != 0 {
        if let Some(mut p) = crate::pc::get(c, aid) {
            p.idletime = crate::pc::last_tick(c);
            crate::pc::put(c, &p);
        }
    }
}

/// clif_parse_UseSkillToId / _ToPos's menu rule: a menu that is open blocks skills, except that the pet capture
/// (SA_TAMINGMONSTER) and the Feeling selection (SG_FEEL) are cancelled and Autospell's menu stays. False: stop.
fn menu_allows(c: &Ctx, aid: u32) -> bool {
    let (menuskill_id, _, _) = crate::itemui::menu(c, aid);
    if menuskill_id != 0 {
        if menuskill_id == SA_TAMINGMONSTER as i32 {
            crate::itemui::clif_menuskill_clear(c, aid); // Cancel pet capture.
        } else if menuskill_id == SG_FEEL as i32 {
            crate::itemui::clif_menuskill_clear(c, aid); // Cancel selection
        } else if menuskill_id != SA_AUTOSPELL as i32 {
            return false; // Can't use skills while a menu is open.
        }
    }
    true
}

/// clif.cpp clif_parse_skill_toid: a player asks to use a targeted skill.
pub fn clif_parse_skill_toid(c: &Ctx, aid: u32, skill_id: u16, skill_lv: u16, target_id: u32) {
    let Some(sdu) = unit::get(c, aid) else { return };
    let Some(p) = crate::pc::get(c, aid) else { return };
    let tick = c.tick();
    let mut skill_lv = skill_lv;
    let mut target_id = target_id;
    if skill_lv < 1 {
        skill_lv = 1; // No clue, I have seen the client do this with guild skills :/ [Skotlex]
    }
    let inf = skill_get_inf(skill_id);
    if inf & INF_GROUND_SKILL as i32 != 0 || inf == 0 {
        return; // Using a ground/passive skill on a target? WRONG.
    }
    if p.block_action & crate::pc::PCBLOCK_SKILL != 0 {
        crate::clif_items::msg(c, &sdu, MSI_BUSY);
        return;
    }
    if skill_chk_homun(skill_id) {
        super::homun::clif_parse_use_skill_to_id_homun(c, aid, tick, skill_id, skill_lv, target_id);
        return;
    }
    if skill_chk_merc(skill_id) {
        // clif_parse_UseSkillToId_mercenary (mercenary/skill.rs)
        crate::mercenary::skill::clif_parse_use_skill_to_id_mercenary(c, aid, tick, skill_id, skill_lv, target_id);
        return;
    }
    // Whether skill fails or not is irrelevant, the char ain't idle. [Skotlex]
    touch_idletime(c, aid, IDLE_USESKILLTOID);
    // hom_idle_no_share (homunc.conf: 0): sd->idletime_hom is never touched here; mer_idle_no_share (mercenary.conf: 0): nor sd->idletime_mer
    let st = super::state::get(c, aid);
    if p.npc_id != 0 {
        // !sd->npc_item_flag: an NPC dialog blocks skills unless an item opened it (itemskill in a dialog)
        if crate::itemui::pc_hasprogress(c, aid, crate::itemui::WIP_DISABLE_SKILLITEM) || !super::iface::npc_item_flag(c, aid) || inf & INF_SELF_SKILL as i32 == 0 {
            crate::clif_items::msg(c, &sdu, MSI_BUSY); // RENEWAL
            return;
        }
    }
    let opt1 = sdu.sc.opt1;
    if (pc_cant_act2(&p, opt1) || p.chat_id != 0)
        && skill_id != RK_REFRESH
        && !((skill_id == SR_GENTLETOUCH_CURE || skill_id == SU_GROOMING) && (opt1 == crate::status::defs::OPT1_STONE || opt1 == crate::status::defs::OPT1_FREEZE || opt1 == crate::status::defs::OPT1_STUN))
        && !(p.storage_flag != 0 && inf & INF_SELF_SKILL as i32 != 0)
    {
        // SELF skills can be used with the storage open, issue: 8027
        return;
    }
    if crate::inventory::pc_issit(c, aid) {
        return;
    }
    if super::condition::skill_is_not_ok(c, &sdu, skill_id) {
        return;
    }
    if aid != target_id && inf & INF_SELF_SKILL as i32 != 0 {
        target_id = aid; // never trust the client
    }
    if (target_id as i32) < 0 && (-(target_id as i32)) as u32 == aid {
        target_id = aid; // Disguised GMs receive a negative integer that needs to be inverted to skill themselves [Skotlex]
    }
    let sdu = unit::get(c, aid).unwrap_or(sdu);
    if sdu.ud.skilltimer != 0 {
        if skill_id != SA_CASTCANCEL && skill_id != SO_SPELLFIST {
            return;
        }
    } else if tick - sdu.ud.canact_tick < 0 && st.skillitem != skill_id as i16 {
        clif_skill_fail(c, &sdu, skill_id, USESKILL_FAIL_SKILLINTERVAL, 0, 0);
        return;
    }
    if sdu.sc.option & crate::status::defs::OPTION_COSTUME != 0 {
        return;
    }
    if !menu_allows(c, aid) {
        return;
    }
    let st = super::state::get(c, aid);
    if st.skillitem == skill_id as i16 {
        if skill_lv as i16 != st.skillitemlv {
            skill_lv = st.skillitemlv as u16;
        }
        if inf & INF_SELF_SKILL as i32 == 0 {
            crate::pc::delinvincibletimer(c, aid); // Target skills thru items cancel invincibility. [Inkfish]
        }
        super::unit::unit_skilluse_id(c, aid, target_id, skill_id, skill_lv);
        return;
    }
    super::state::with(c, aid, |s| {
        s.skillitem = 0;
        s.skillitemlv = 0;
    });
    if skill_chk_guild(skill_id) {
        let gx = crate::guild::gx(c, aid);
        skill_lv = match crate::guild::sd_guild(c, aid) {
            Some(g) if gx.gmaster_flag || skill_id == GD_CHARGESHOUT_BEATING => crate::guild::guild_checkskill(&g.guild, skill_id as i32) as u16,
            _ => 0,
        };
    } else if skill_id != ALL_EQSWITCH {
        skill_lv = (crate::skill::checkskill(c, aid, skill_id) as u16).min(skill_lv); // never trust client
    }
    crate::pc::delinvincibletimer(c, aid);
    if skill_lv != 0 {
        super::unit::unit_skilluse_id(c, aid, target_id, skill_id, skill_lv);
    }
}

/// clif.cpp clif_parse_UseSkillToId: CZ_USE_SKILL (<skill lv>.W <skill id>.W <target id>.L at packet_db's positions).
pub fn parse_use_skill_to_id(c: &Ctx, aid: u32, pkt: &[u8]) {
    let pos = packet_pos(cmd_of(pkt));
    let (Some(&p0), Some(&p1), Some(&p2)) = (pos.first(), pos.get(1), pos.get(2)) else { return };
    let (Some(lv), Some(id), Some(target)) = (rw(pkt, p0), rw(pkt, p1), rl(pkt, p2)) else { return };
    clif_parse_skill_toid(c, aid, id, lv, target);
}

/// clif.cpp clif_parse_UseSkillToPosSub: a player asks to use a ground skill (`more_info`: the talk box text of
/// CZ_USE_SKILL_TOGROUND_WITHTALKBOX, None for the plain request).
fn use_skill_to_pos_sub(c: &Ctx, aid: u32, skill_lv: u16, skill_id: u16, x: i16, y: i16, more_info: Option<&[u8]>) {
    let tick = c.tick();
    let mut skill_lv = skill_lv;
    if skill_get_inf(skill_id) & INF_GROUND_SKILL as i32 == 0 {
        return; // Using a target skill on the ground? WRONG.
    }
    let Some(sdu) = unit::get(c, aid) else { return };
    let Some(p) = crate::pc::get(c, aid) else { return };
    if p.block_action & crate::pc::PCBLOCK_SKILL != 0 {
        crate::clif_items::msg(c, &sdu, MSI_BUSY);
        return;
    }
    if skill_chk_homun(skill_id) {
        super::homun::clif_parse_use_skill_to_pos_homun(c, aid, tick, skill_id, skill_lv, x, y);
        return;
    }
    if skill_chk_merc(skill_id) {
        // clif_parse_UseSkillToPos_mercenary (mercenary/skill.rs)
        crate::mercenary::skill::clif_parse_use_skill_to_pos_mercenary(c, aid, tick, skill_id, skill_lv, x, y);
        return;
    }
    if crate::itemui::pc_hasprogress(c, aid, crate::itemui::WIP_DISABLE_SKILLITEM) {
        crate::clif_items::msg(c, &sdu, MSI_BUSY); // RENEWAL
        return;
    }
    // Whether skill fails or not is irrelevant, the char ain't idle. [Skotlex]
    touch_idletime(c, aid, IDLE_USESKILLTOPOS);
    // hom_idle_no_share (homunc.conf: 0): sd.idletime_hom is never touched here; mer_idle_no_share (mercenary.conf: 0): nor sd.idletime_mer
    if super::condition::skill_is_not_ok(c, &sdu, skill_id) {
        return;
    }
    if let Some(text) = more_info {
        if crate::inventory::pc_issit(c, aid) {
            skill_fail(c, &sdu, skill_id);
            return;
        }
        // You can't use Graffiti/TalkieBox AND have a vending open, so this is safe.
        let end = text.iter().take(MESSAGE_SIZE - 1).position(|&b| b == 0).unwrap_or(text.len().min(MESSAGE_SIZE - 1));
        let msg: String = text[..end].iter().map(|&b| b as char).collect();
        super::iface::set_sd_message(c, aid, &msg); // safestrncpy(sd.message, …, MESSAGE_SIZE)
    }
    let sdu = unit::get(c, aid).unwrap_or(sdu);
    if sdu.ud.skilltimer != 0 {
        return;
    }
    let st = super::state::get(c, aid);
    if tick - sdu.ud.canact_tick < 0 && st.skillitem != skill_id as i16 {
        clif_skill_fail(c, &sdu, skill_id, USESKILL_FAIL_SKILLINTERVAL, 0, 0);
        return;
    }
    if sdu.sc.option & crate::status::defs::OPTION_COSTUME != 0 {
        return;
    }
    if !menu_allows(c, aid) {
        return;
    }
    crate::pc::delinvincibletimer(c, aid);
    let st = super::state::get(c, aid);
    if st.skillitem == skill_id as i16 {
        if skill_lv as i16 != st.skillitemlv {
            skill_lv = st.skillitemlv as u16;
        }
        super::unit::unit_skilluse_pos(c, aid, x, y, skill_id, skill_lv);
    } else {
        super::state::with(c, aid, |s| {
            s.skillitem = 0;
            s.skillitemlv = 0;
        });
        let lv = crate::skill::checkskill(c, aid, skill_id) as u16;
        if lv > 0 {
            if skill_lv > lv {
                skill_lv = lv;
            }
            super::unit::unit_skilluse_pos(c, aid, x, y, skill_id, skill_lv);
        }
    }
}

/// The pc_cant_act / pc_issit guard of clif_parse_UseSkillToPos and _MoreInfo.
fn pos_guard(c: &Ctx, aid: u32) -> bool {
    let Some(sdu) = unit::get(c, aid) else { return false };
    let Some(p) = crate::pc::get(c, aid) else { return false };
    if crate::pc::cant_act(&p, sdu.sc.opt1) {
        return false;
    }
    !crate::inventory::pc_issit(c, aid)
}

/// clif.cpp clif_parse_UseSkillToPos: CZ_USE_SKILL_TOGROUND (<skill lv>.W <skill id>.W <x>.W <y>.W).
pub fn parse_use_skill_to_pos(c: &Ctx, aid: u32, pkt: &[u8]) {
    if !pos_guard(c, aid) {
        return;
    }
    let pos = packet_pos(cmd_of(pkt));
    let (Some(&p0), Some(&p1), Some(&p2), Some(&p3)) = (pos.first(), pos.get(1), pos.get(2), pos.get(3)) else { return };
    let (Some(lv), Some(id), Some(x), Some(y)) = (rw(pkt, p0), rw(pkt, p1), rw(pkt, p2), rw(pkt, p3)) else { return };
    // (RFIFOB(fd, info->pos[4]): "TODO: find out what this is intended to do" — unused)
    use_skill_to_pos_sub(c, aid, lv, id, x as i16, y as i16, None);
}

/// clif.cpp clif_parse_UseSkillToPosMoreInfo: CZ_USE_SKILL_TOGROUND_WITHTALKBOX (… <contents>.80B).
pub fn parse_use_skill_to_pos_more_info(c: &Ctx, aid: u32, pkt: &[u8]) {
    if !pos_guard(c, aid) {
        return;
    }
    let pos = packet_pos(cmd_of(pkt));
    let (Some(&p0), Some(&p1), Some(&p2), Some(&p3), Some(&p4)) = (pos.first(), pos.get(1), pos.get(2), pos.get(3), pos.get(4)) else { return };
    let (Some(lv), Some(id), Some(x), Some(y)) = (rw(pkt, p0), rw(pkt, p1), rw(pkt, p2), rw(pkt, p3)) else { return };
    let text = pkt.get(p4 as usize..).unwrap_or(&[]);
    use_skill_to_pos_sub(c, aid, lv, id, x as i16, y as i16, Some(text));
}

/// clif.cpp clif_parse_AutoSpell: CZ_SELECTAUTOSPELL, the answer to Autospell's list.
pub fn parse_auto_spell(c: &Ctx, aid: u32, pkt: &[u8]) {
    if crate::itemui::menu(c, aid).0 != SA_AUTOSPELL as i32 {
        return;
    }
    crate::itemui::set_workinprogress(c, aid, crate::itemui::WIP_DISABLE_NONE);
    let Some(skill_id) = rl(pkt, 2) else { return };
    super::autospell::skill_autospell(c, aid, skill_id as u16);
    crate::itemui::clif_menuskill_clear(c, aid);
}

/// clif.cpp clif_parse_SkillSelectMenu: CZ_SKILL_SELECT_RESPONSE (SA_AUTOSPELL / SC_AUTOSHADOWSPELL lists).
pub fn parse_skill_select_menu(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(selected) = rw(pkt, 6) else { return };
    let menuskill_id = crate::itemui::menu(c, aid).0;
    if menuskill_id == SA_AUTOSPELL as i32 {
        crate::itemui::set_workinprogress(c, aid, crate::itemui::WIP_DISABLE_NONE);
        super::autospell::skill_autospell(c, aid, selected);
    } else if menuskill_id == SC_AUTOSHADOWSPELL as i32 {
        let Some(p) = crate::pc::get(c, aid) else { return };
        // pc_istrading: npc_id || vending || buyingstore || trading
        if p.npc_id != 0 || p.vending || p.buyingstore || p.trading {
            if let Some(u) = unit::get(c, aid) {
                skill_fail(c, &u, u.ud.skill_id);
            }
            crate::itemui::clif_menuskill_clear(c, aid);
            return;
        }
        super::autospell::skill_select_menu(c, aid, selected);
    } else {
        return;
    }
    crate::itemui::clif_menuskill_clear(c, aid);
}

/// clif.cpp clif_parse_StartUseSkillToId: CZ_USE_SKILL_START ("keep using"; only Rolling Cutter).
pub fn parse_start_use_skill_to_id(c: &Ctx, aid: u32, pkt: &[u8]) {
    let (Some(skill_id), Some(skill_lv), Some(target)) = (rw(pkt, 2), rw(pkt, 4), rl(pkt, 6)) else { return };
    // Only rolling cutter is supported for now
    if skill_id != GC_ROLLINGCUTTER {
        return;
    }
    // Already running - cant happen on officials, since only one skill is supported
    if super::state::get(c, aid).keep_using_skill_id != 0 {
        return;
    }
    super::state::with(c, aid, |s| {
        s.keep_using_tid = 0;
        s.keep_using_skill_id = skill_id;
        s.keep_using_level = skill_lv;
        s.keep_using_target = target;
    });
    clif_parse_skill_toid(c, aid, skill_id, skill_lv, target);
}

/// clif.cpp clif_parse_StopUseSkillToId: CZ_USE_SKILL_END.
pub fn parse_stop_use_skill_to_id(c: &Ctx, aid: u32, _pkt: &[u8]) {
    let st = super::state::get(c, aid);
    // Not running
    if st.keep_using_skill_id == 0 {
        return;
    }
    if st.keep_using_tid != 0 {
        crate::timer::delete(c, st.keep_using_tid);
    }
    super::state::with(c, aid, |s| {
        s.keep_using_tid = 0;
        s.keep_using_skill_id = 0;
        s.keep_using_level = 0;
        s.keep_using_target = 0;
    });
}

/// clif.cpp clif_parse_ItemListWindowSelected (CZ_ITEMLISTWIN_RES 0x07e4: <len>.W <type>.L <flag>.L
/// { <index>.W <amount>.W }*): the answer to clif_skill_itemlistwindow's list (SO_EL_ANALYSIS, GN_CHANGEMATERIAL).
pub fn parse_item_list_window_selected(c: &Ctx, aid: u32, pkt: &[u8]) {
    let Some(sd) = unit::get(c, aid) else { return };
    // TODO: shuffle packet
    let pos = packet_pos(cmd_of(pkt));
    let (Some(&p0), Some(&p1), Some(&p2), Some(&p3)) = (pos.first(), pos.get(1), pos.get(2), pos.get(3)) else { return };
    let n = (rw(pkt, p0).unwrap_or(0) as i32 - 12) / 4;
    let type_ = rl(pkt, p1).unwrap_or(0) as i32;
    let flag = rl(pkt, p2).unwrap_or(0) as i32; // Button clicked: 0 = Cancel, 1 = OK
    let item_list: Vec<u16> = pkt.get(p3 as usize..).unwrap_or(&[]).chunks_exact(2).map(|b| u16::from_le_bytes([b[0], b[1]])).collect();
    let Some(p) = crate::pc::get(c, aid) else { return };
    if p.trading || p.npc_shopid != 0 {
        return;
    }
    if flag == 0 || n == 0 {
        crate::itemui::clif_menuskill_clear(c, aid);
        return; // Canceled by player.
    }
    let menuskill_id = crate::itemui::menu(c, aid).0;
    if menuskill_id != SO_EL_ANALYSIS as i32 && menuskill_id != GN_CHANGEMATERIAL as i32 {
        crate::itemui::clif_menuskill_clear(c, aid);
        return; // Prevent hacking.
    }
    match type_ {
        0 => {
            // Change Material
            crate::itemui::produce::skill_changematerial(c, aid, n, &item_list);
        }
        1 | 2 => {
            // Level 1: Pure to Rough / Level 2: Rough to Pure
            super::misc::skill_elementalanalysis(c, &sd, n, type_ as u16, &item_list);
        }
        _ => {}
    }
    crate::itemui::clif_menuskill_clear(c, aid);
}

/// clif.cpp clif_parse_NoviceExplosionSpirits: CZ_CHOPOKGI (0x01ed), the Super Novice's guardian angel prayer (the
/// client sends it after 7 lines of any text, the prayer of msgstringtable.txt lines 790~793 and another line). At
/// exactly 10.0%, 20.0%, ..., 90.0% of the base exp the Super Novice gets Fury at level 17 (+50 critical, noted by Poki
/// [Skotlex]) for Fury 5's duration, and the prayer always shows a successful MO_EXPLOSIONSPIRITS 5 cast (it disregards
/// noskill restrictions). (Ported by the acolyte agent: MO_EXPLOSIONSPIRITS is a Monk skill.)
pub fn parse_novice_explosion_spirits(c: &Ctx, aid: u32, _pkt: &[u8]) {
    use crate::db::jobs::ids::{MAPID_SECONDMASK, MAPID_SUPER_NOVICE};
    let Some(st) = crate::status::data::sd_get(c, aid) else { return };
    if st.class_ & MAPID_SECONDMASK != MAPID_SUPER_NOVICE {
        return;
    }
    let Some(ch) = crate::pc::character_of(c, aid) else { return };
    let next = crate::db::jobs::pc_nextbaseexp(ch.class, ch.base_level as u32);
    if next == 0 {
        return;
    }
    let percent = ((ch.base_exp as f64 / next as f64) * 1000.0) as i32;
    if percent != 0 && percent % 100 == 0 {
        // 10.0%, 20.0%, ..., 90.0%
        let Some(sd) = unit::get(c, aid) else { return };
        crate::status::sc::sc_start(c, Some(&sd), &sd, crate::status::consts::SC_EXPLOSIONSPIRITS, 100, 17, skill_get_time(MO_EXPLOSIONSPIRITS, 5) as i64);
        let sd = unit::get(c, aid).unwrap_or(sd);
        super::clif::clif_skill_nodamage(c, Some(&sd), &sd, MO_EXPLOSIONSPIRITS, 5, true);
    }
}
