//! The skill unit packets of clif.cpp (clif_getareachar_skillunit, clif_graffiti, clif_clearchar_skillunit,
//! clif_skill_delunit, clif_skillunit_update, clif_changetraplook, clif_changelook of a unit, clif_talkiebox,
//! clif_gospel_info, clif_changemapcell with a send target, clif_skill_warppoint) and skill.cpp's visibility rules for
//! hidden traps (skill_getareachar_skillunit_visibilty / _sub / _single).

use crate::clif::{self, Target};
use crate::map::BL_PC;
use crate::net::packets::*;
use crate::unit::Unit;
use crate::world::Ctx;

use super::consts::*;
use super::data::{self, SkillUnit};

/// clif.cpp clif_getareachar_skillunit: ZC_SKILL_ENTRY5 (0x09ca; PACKETVER >= 20130731) of `unit` to `bl` / `target`
/// (a Graffiti: clif_graffiti). `visible` false shows it as UNT_DUMMYSKILL (a hidden trap).
pub fn clif_getareachar_skillunit(c: &Ctx, bl: &Unit, unit: &SkillUnit, target: Target, visible: bool) {
    let Some(group) = data::skill_id2group(c, unit.group_id) else { return };
    if group.guildaura {
        return;
    }
    let mut unit_id = if group.song_dance & 0x1 != 0 && unit.val2 & (1 << crate::db::skills::UF_ENSEMBLE) != 0 {
        if unit.val2 & (1 << crate::db::skills::UF_SONG) != 0 {
            UNT_DISSONANCE
        } else {
            UNT_UGLYDANCE
        }
    } else if crate::db::skills::skill_get_unit_flag(group.skill_id, &[crate::db::skills::UF_RANGEDSINGLEUNIT])
        && unit.val2 & (1 << crate::db::skills::UF_RANGEDSINGLEUNIT) == 0
    {
        UNT_DUMMYSKILL // Use invisible unit id for other case of rangedsingle unit
    } else {
        group.unit_id
    };
    if !visible {
        unit_id = UNT_DUMMYSKILL; // Hack to makes hidden trap really hidden!
    }
    if unit_id == UNT_GRAFFITI {
        // Graffiti [Valaris] (PACKETVER >= 3)
        clif_graffiti(c, unit, &group, target, bl);
        return;
    }
    let p = packet_skill_entry {
        PacketType: packet_skill_entry::ID as i16,
        PacketLength: packet_skill_entry::SIZE as i16,
        AID: unit.id(),
        creatorAID: group.src_id,
        xPos: unit.bl.x,
        yPos: unit.bl.y,
        job: unit_id,
        RadiusRange: unit.range as u8 as i8,
        isVisible: visible as u8,
        level: group.skill_lv as u8,
    };
    clif::send(c, &p.to_vec(), bl, target);
    if group.skill_id == crate::db::skills::WZ_ICEWALL {
        clif_changemapcell(c, unit.bl.map, unit.bl.x, unit.bl.y, 5, Target::SelfOnly, bl);
    }
}

/// clif.cpp clif_skill_unit_test (@displayskillunit): a fake unit entry 0x09ca (id 1000, creator 2000) of `unit_id` at
/// (x, y) to `bl`'s area. The C code writes the fields with WBUF* casts: range and level as unsigned char.
pub fn clif_skill_unit_test(c: &Ctx, bl: &Unit, x: i16, y: i16, unit_id: i32, range: i16, skill_lv: i16) {
    let p = packet_skill_entry {
        PacketType: packet_skill_entry::ID as i16,
        PacketLength: packet_skill_entry::SIZE as i16,
        AID: 1000,
        creatorAID: 2000,
        xPos: x,
        yPos: y,
        job: unit_id,
        RadiusRange: range as u8 as i8,
        isVisible: 1,
        level: skill_lv as u8,
    };
    clif::send(c, &p.to_vec(), bl, Target::Area);
}

/// clif.cpp clif_graffiti: ZC_SKILL_ENTRY2 (0x01c9) with the Graffiti's message.
fn clif_graffiti(c: &Ctx, unit: &SkillUnit, group: &data::SkillUnitGroup, target: Target, bl: &Unit) {
    let p = packet_graffiti_entry {
        PacketType: packet_graffiti_entry::ID as i16,
        AID: unit.id(),
        creatorAID: group.src_id,
        xPos: unit.bl.x,
        yPos: unit.bl.y,
        job: group.unit_id as u8,
        isVisible: 1,
        isContens: 1,
        msg: group.valstr.clone().unwrap_or_default(),
    };
    clif::send(c, &p.to_vec(), bl, target);
}

/// clif.cpp clif_clearchar_skillunit: ZC_SKILL_DISAPPEAR of `unit` to one player (and an Ice Wall's cell back).
pub fn clif_clearchar_skillunit(c: &Ctx, unit: &SkillUnit, sd: &Unit) {
    let p = PACKET_ZC_SKILL_DISAPPEAR { packetType: PACKET_ZC_SKILL_DISAPPEAR::ID as i16, GID: unit.id() };
    clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
    if data::skill_id2group(c, unit.group_id).map(|g| g.skill_id == crate::db::skills::WZ_ICEWALL).unwrap_or(false) {
        clif_changemapcell(c, unit.bl.map, unit.bl.x, unit.bl.y, unit.val2 as i16, Target::SelfOnly, sd);
    }
}

/// clif.cpp clif_skill_delunit: ZC_SKILL_DISAPPEAR to the unit's area.
pub fn clif_skill_delunit(c: &Ctx, unit: &SkillUnit) {
    let p = PACKET_ZC_SKILL_DISAPPEAR { packetType: PACKET_ZC_SKILL_DISAPPEAR::ID as i16, GID: unit.id() };
    clif::send(c, &p.to_vec(), &unit.bl, Target::Area);
}

/// clif.cpp clif_skillunit_update: ZC_SKILL_UPDATE (an object got ankle-snared) to the area.
pub fn clif_skillunit_update(c: &Ctx, bl: &Unit) {
    let p = PACKET_ZC_SKILL_UPDATE { packetType: PACKET_ZC_SKILL_UPDATE::ID as i16, GID: bl.id };
    clif::send(c, &p.to_vec(), bl, Target::Area);
}

/// clif.hpp LOOK_BASE.
const LOOK_BASE: u8 = 0;

/// clif.cpp clif_changetraplook: `clif_sprite_change(bl, bl->id, LOOK_BASE, val, 0, AREA)`.
pub fn clif_changetraplook(c: &Ctx, bl: &Unit, val: i32) {
    clif::sprite_change(c, bl, bl.id, LOOK_BASE, val, 0, Target::Area);
}

/// clif.cpp clif_changelook(unit, LOOK_BASE, val) for a skill unit: it has no view data and no status change, so only
/// the sprite change goes out (to the area), as for a trap.
pub fn clif_changelook_unit(c: &Ctx, bl: &Unit, val: i32) {
    clif::sprite_change(c, bl, bl.id, LOOK_BASE, val, 0, Target::Area);
}

/// clif.cpp clif_talkiebox: ZC_TALKBOX_CHATCONTENTS (TALKBOX_MESSAGE_SIZE 21) to the area.
pub fn clif_talkiebox(c: &Ctx, bl: &Unit, talkie: &str) {
    let p = PACKET_ZC_TALKBOX_CHATCONTENTS { PacketType: PACKET_ZC_TALKBOX_CHATCONTENTS::ID as i16, aid: bl.id, message: talkie.to_string() };
    clif::send(c, &p.to_vec(), bl, Target::Area);
}

/// clif.cpp clif_gospel_info: ZC_SKILLMSG to the player.
pub fn clif_gospel_info(c: &Ctx, sd: &Unit, kind: i32) {
    if sd.kind != BL_PC {
        return;
    }
    let p = PACKET_ZC_SKILLMSG { packetType: PACKET_ZC_SKILLMSG::ID as i16, msgId: kind };
    clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
}

/// common/mapindex.cpp mapindex_getmapname_ext: the part after a '#', at most MAP_NAME_LENGTH - 1 characters, with
/// ".gat" added when missing.
pub fn mapname_ext(name: &str) -> String {
    const MAP_NAME_LENGTH: usize = 12;
    let mut buf: Vec<u8> = crate::net::wire::latin1(name);
    // sscanf(string, "%*[^#]%*[#]%15s", buf): only when some non-'#' text is followed by '#'s and a word
    if let Some(first_hash) = buf.iter().position(|&b| b == b'#') {
        if first_hash > 0 {
            let rest = &buf[first_hash..];
            let after = rest.iter().position(|&b| b != b'#').map(|p| &rest[p..]).unwrap_or(&[]);
            let word: Vec<u8> = after.iter().take_while(|b| !b.is_ascii_whitespace()).take(15).copied().collect();
            if !word.is_empty() {
                buf = word;
            }
        }
    }
    let mut len = buf.iter().take(MAP_NAME_LENGTH).take_while(|&&b| b != 0).count();
    if len == MAP_NAME_LENGTH {
        len -= 1;
    }
    let mut dest: Vec<u8> = buf[..len].to_vec();
    if len < 4 || !dest[len - 4..].eq_ignore_ascii_case(b".gat") {
        dest.extend_from_slice(b".gat");
    }
    dest.iter().map(|&b| b as char).collect()
}

/// clif.cpp clif_changemapcell with a send target and block_list (ZC_UPDATE_MAPINFO): AREA / SELF / ALL_SAMEMAP
/// around `tbl`.
#[allow(clippy::too_many_arguments)]
pub fn clif_changemapcell(c: &Ctx, m: u16, x: i16, y: i16, kind: i16, target: Target, tbl: &Unit) {
    let name = crate::data::maps().get(m).map(|d| d.name.clone()).unwrap_or_default();
    let p = PACKET_ZC_UPDATE_MAPINFO { packetType: PACKET_ZC_UPDATE_MAPINFO::ID as i16, x, y, r#type: kind, mapname: mapname_ext(&name) };
    clif::send(c, &p.to_vec(), tbl, target);
}

/// clif.cpp clif_skill_warppoint: ZC_WARPLIST (0x0abe, PACKETVER_RE_NUM >= 20170419) of the non-empty map names; the
/// menu skill is remembered (AL_WARP: the portal's cell, and work in progress blocks everything; else the level).
pub fn clif_skill_warppoint(c: &Ctx, sd: &Unit, skill_id: u16, skill_lv: u16, maps: &[String]) {
    if maps.is_empty() {
        return;
    }
    let p = PACKET_ZC_WARPLIST {
        packetType: PACKET_ZC_WARPLIST::ID as i16,
        packetLength: 0,
        skillId: skill_id,
        maps: maps.iter().filter(|m| !m.is_empty()).map(|m| PACKET_ZC_WARPLIST_sub { map: mapname_ext(m) }).collect(),
    };
    clif::send(c, &p.to_vec(), sd, Target::SelfOnly);
    if skill_id == crate::db::skills::AL_WARP {
        //Store warp position here.
        let val = ((sd.ud.skillx as i32) << 16) | sd.ud.skilly as i32;
        crate::itemui::set_menu(c, sd.id, skill_id as i32, val, crate::itemui::menu(c, sd.id).2);
        crate::itemui::set_workinprogress(c, sd.id, crate::itemui::WIP_DISABLE_ALL);
    } else {
        crate::itemui::set_menu(c, sd.id, skill_id as i32, skill_lv as i32, crate::itemui::menu(c, sd.id).2);
    }
}

// ---------------------------------------------------------------------------------------------- visibility

/// skill.cpp skill_getareachar_skillunit_visibilty_sub: `bl` (a player around the unit) sees a hidden unit only when
/// it is the unit's master or in the master's party.
fn visibility_sub(c: &Ctx, bl: &Unit, su: &SkillUnit, src: &Unit, party1: i32) -> i32 {
    let mut visible = true;
    if src.id != bl.id {
        let party2 = crate::battle::target::status_get_party_id(c, bl);
        if party1 == 0 || party2 == 0 || party1 != party2 {
            visible = false;
        }
    }
    clif_getareachar_skillunit(c, bl, su, Target::SelfOnly, visible);
    1
}

/// skill.cpp skill_getareachar_skillunit_visibilty: the unit shown to its area when it is placed or moved.
pub fn skill_getareachar_skillunit_visibilty(c: &Ctx, su: &SkillUnit, target: Target) {
    if !su.hidden {
        // It's not hidden, just do this!
        clif_getareachar_skillunit(c, &su.bl, su, target, true);
    } else {
        let src = crate::battle::target::battle_get_master(c, &su.bl);
        let party1 = crate::battle::target::status_get_party_id(c, &src);
        let r = crate::conf::area_size();
        let ids = super::util::ids_in_area(c, su.bl.map, su.bl.x - r, su.bl.y - r, su.bl.x + r, su.bl.y + r, BL_PC, false);
        super::util::each(c, &ids, |bl| visibility_sub(c, bl, su, &src, party1));
    }
}

/// skill.cpp skill_getareachar_skillunit_visibilty_single: the unit shown to one player coming into sight of it.
pub fn skill_getareachar_skillunit_visibilty_single(c: &Ctx, su: &SkillUnit, bl: &Unit) {
    let mut visible = true;
    let src = crate::battle::target::battle_get_master(c, &su.bl);
    if su.hidden && src.id != bl.id {
        let party1 = crate::battle::target::status_get_party_id(c, &src);
        let party2 = crate::battle::target::status_get_party_id(c, bl);
        if party1 == 0 || party2 == 0 || party1 != party2 {
            visible = false;
        }
    }
    clif_getareachar_skillunit(c, bl, su, Target::SelfOnly, visible);
}

/// clif.cpp clif_getareachar / clif_insight's BL_SKILL arm: `sd` sees the unit `bl`.
pub fn getareachar_skill(c: &Ctx, sd: &Unit, bl: &Unit) {
    if let Some(su) = data::get_unit(c, bl.id) {
        skill_getareachar_skillunit_visibilty_single(c, &su, sd);
    }
}

/// clif.cpp clif_outsight's BL_SKILL arms: `sd` loses sight of the unit `bl`.
pub fn outsight_skill(c: &Ctx, bl: &Unit, sd: &Unit) {
    if let Some(su) = data::get_unit(c, bl.id) {
        clif_clearchar_skillunit(c, &su, sd);
    }
}
