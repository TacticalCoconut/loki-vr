//! Hotkeys (HOTKEY_SAVING): the character's shortcut bar as the char server keeps it (char.cpp `hotkey` table:
//! mmo_char_tosql's REPLACE INTO, char_mmo_char_fromsql's load, char_delete), clif.cpp clif_hotkeys_send
//! (ZC_SHORTCUT_KEY_LIST 0x0b20), clif_parse_Hotkey (CZ_SHORTCUT_KEY_CHANGE2 0x0b21) and clif_parse_HotkeyRowShift
//! (CZ_SHORTCUTKEYBAR_ROTATE2 0x0b22; the row shifts are the character's `hotkey_rowshift` / `hotkey_rowshift2`).
//!
//! rAthena holds mmo_charstatus::hotkeys[MAX_HOTKEYS_DB] in memory and the char server writes the entries that changed
//! at every save; here the table is written when a hotkey changes (the rows are what the next login loads).

use spacetimedb::{table, Table};

use crate::clif::{self, Target};
use crate::net::packets::*;
use crate::world::Ctx;

/// mmo.hpp MAX_HOTKEYS (PACKETVER >= 20090617: 9 skills x 4 bars & 2 quickslots).
pub const MAX_HOTKEYS: usize = 38;
/// mmo.hpp MAX_HOTKEYS_DB (PACKETVER_RE_NUM >= 20190508: two tabs).
pub const MAX_HOTKEYS_DB: usize = MAX_HOTKEYS * 2;
/// packets_struct.hpp MAX_HOTKEYS_PACKET.
pub const MAX_HOTKEYS_PACKET: usize = 38;

/// rAthena `hotkey` table (sql-files/main.sql): (char_id, hotkey, type, itemskill_id, skill_lvl).
#[table(accessor = hotkey, index(accessor = by_char, btree(columns = [char_id])))]
#[derive(Clone, Debug)]
pub struct Hotkey {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub char_id: u32,
    /// The slot, 0 .. MAX_HOTKEYS_DB - 1.
    pub hotkey: u16,
    /// 0: item, 1: skill.
    pub type_: u8,
    pub itemskill_id: u32,
    pub skill_lvl: u16,
}

/// mmo.hpp struct hotkey of a slot (zero when the table has no row).
fn slot(rows: &[Hotkey], i: usize) -> (u8, u32, u16) {
    rows.iter().find(|r| r.hotkey as usize == i).map(|r| (r.type_, r.itemskill_id, r.skill_lvl)).unwrap_or((0, 0, 0))
}

/// clif.cpp clif_hotkeys_send: ZC_SHORTCUT_KEY_LIST for one tab (0 or 1).
pub fn clif_hotkeys_send(c: &Ctx, aid: u32, tab: i32) {
    let Some(u) = crate::unit::get(c, aid) else { return };
    let Some(ch) = crate::pc::character_of(c, aid) else { return };
    let rows: Vec<Hotkey> = c.db().hotkey().by_char().filter(ch.id).collect();
    let rotate = if tab == 0 { ch.hotkey_rowshift } else { ch.hotkey_rowshift2 };
    let offset = tab as usize * MAX_HOTKEYS;
    let hotkey = (0..MAX_HOTKEYS_PACKET)
        .map(|i| {
            let (t, id, lv) = slot(&rows, i + offset);
            hotkey_data { isSkill: t as i8, id, count: lv as i16 }
        })
        .collect();
    let p = PACKET_ZC_SHORTCUT_KEY_LIST { packetType: PACKET_ZC_SHORTCUT_KEY_LIST::ID as i16, rotate: rotate as i8, tab: tab as i16, hotkey };
    clif::send(c, &p.to_vec(), &u, Target::SelfOnly);
}

/// clif.cpp clif_parse_HotkeyRowShift: CZ_SHORTCUTKEYBAR_ROTATE2 (0x0b22 <tab>.W <rowshift>.B).
pub fn parse_hotkey_row_shift(c: &Ctx, aid: u32, pkt: &[u8]) {
    let mut r = crate::net::wire::Reader::at(pkt, 0);
    let Some(p) = PACKET_CZ_SHORTCUTKEYBAR_ROTATE2::read(&mut r) else { return };
    let Some(mut ch) = crate::pc::character_of(c, aid) else { return };
    if p.tab == 0 {
        ch.hotkey_rowshift = p.rowshift;
    } else {
        ch.hotkey_rowshift2 = p.rowshift;
    }
    crate::status::put_char(c, &ch);
}

/// clif.cpp clif_parse_Hotkey: CZ_SHORTCUT_KEY_CHANGE2 (0x0b21 <tab>.W <index>.W <is skill>.B <id>.L <count>.W).
pub fn parse_hotkey(c: &Ctx, aid: u32, pkt: &[u8]) {
    let mut r = crate::net::wire::Reader::at(pkt, 0);
    let Some(p) = PACKET_CZ_SHORTCUT_KEY_CHANGE2::read(&mut r) else { return };
    let idx = (p.index as u32 + p.tab as u32 * MAX_HOTKEYS as u32) as u16; // (uint16 arithmetic, as rAthena's)
    if idx as usize >= MAX_HOTKEYS_DB {
        return;
    }
    let Some(ch) = crate::pc::character_of(c, aid) else { return };
    let (type_, itemskill_id, skill_lvl) = (p.hotkey.isSkill as u8, p.hotkey.id, p.hotkey.count as u16);
    // char.cpp mmo_char_tosql: REPLACE INTO `hotkey` of every entry that changed
    match c.db().hotkey().by_char().filter(ch.id).find(|h| h.hotkey == idx) {
        Some(h) => {
            if (h.type_, h.itemskill_id, h.skill_lvl) != (type_, itemskill_id, skill_lvl) {
                c.db().hotkey().id().update(Hotkey { type_, itemskill_id, skill_lvl, ..h });
            }
        }
        None => {
            if (type_, itemskill_id, skill_lvl) != (0, 0, 0) {
                c.db().hotkey().insert(Hotkey { id: 0, char_id: ch.id, hotkey: idx, type_, itemskill_id, skill_lvl });
            }
        }
    }
}

/// char.cpp char_delete: `DELETE FROM hotkey WHERE char_id` (for the character deletion, character.rs).
pub fn char_delete(db: &spacetimedb::Local, char_id: u32) {
    let ids: Vec<u64> = db.hotkey().by_char().filter(char_id).map(|h| h.id).collect();
    for id in ids {
        db.hotkey().id().delete(id);
    }
}
