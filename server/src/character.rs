//! Characters: what rAthena's char server does for the character screen (char/char.cpp char_make_new_char,
//! char_check_char_name, char_delete; char/char_clif.cpp chclif_parse_char_delete2_*), inside the database.

use spacetimedb::{rand::Rng, reducer, table, view, ReducerContext, Table, TimeDuration, Timestamp, ViewContext};

use crate::account::{current_account, session, session__view, Session};
use crate::cart::cart_inventory;
use crate::inventory::inventory;
use crate::rules;

/// rAthena `char` table (sql-files/main.sql), the columns the game uses so far. Private: owners read it through
/// `my_characters`.
#[table(accessor = character)]
#[derive(Clone)]
pub struct Character {
    #[primary_key]
    pub id: u32,
    #[index(btree)]
    pub account_id: u32,
    /// `char_num`: 0 .. CHAR_SLOTS-1.
    pub slot: u8,
    pub name: String,
    /// Lower-case copy: names are unique regardless of case (MySQL's default collation under `name_ignoring_case: no`).
    #[unique]
    pub name_key: String,
    pub class: u16,
    pub base_level: u16,
    pub job_level: u16,
    pub base_exp: u64,
    pub job_exp: u64,
    pub zeny: u32,
    /// mmo.hpp:574-575 uint32 (a u16 column before the fixes branch: schema change).
    pub status_point: u32,
    pub skill_point: u32,
    pub str: u16,
    pub agi: u16,
    pub vit: u16,
    pub int: u16,
    pub dex: u16,
    pub luk: u16,
    pub hp: u32,
    pub max_hp: u32,
    pub sp: u32,
    pub max_sp: u32,
    pub hair: u16,
    pub hair_color: u16,
    pub clothes_color: u16,
    /// rAthena `body`: the class whose body sprite is drawn (the start class at creation).
    pub body: u16,
    /// rAthena e_sex: 0 female, 1 male.
    pub sex: u8,
    pub last_map: String,
    pub last_x: u16,
    pub last_y: u16,
    pub save_map: String,
    pub save_x: u16,
    pub save_y: u16,
    pub party_id: u32,
    pub guild_id: u32,
    /// Set by a deletion request; the deletion can be confirmed once this time has passed.
    pub delete_at: Option<Timestamp>,
    pub created: Timestamp,
    // The rest of rAthena's `char` columns (sql-files/main.sql), with its defaults.
    pub pow: u16,
    pub sta: u16,
    pub wis: u16,
    pub spl: u16,
    pub con: u16,
    pub crt: u16,
    pub max_ap: u32,
    pub ap: u32,
    pub trait_point: u32,
    /// `option`: the saved OPTION_* bits (cart, riding, falcon, wug…).
    pub option: i32,
    /// mmo.hpp:578 `unsigned char karma`, kept in an i8 column: its readers take it `as u8`.
    pub karma: i8,
    pub manner: i16,
    pub pet_id: u32,
    pub homun_id: u32,
    pub elemental_id: u32,
    pub weapon: u16,
    pub shield: u16,
    pub head_top: u16,
    pub head_mid: u16,
    pub head_bottom: u16,
    pub robe: u16,
    pub last_instanceid: u32,
    pub partner_id: u32,
    pub father: u32,
    pub mother: u32,
    pub child: u32,
    pub fame: u32,
    pub rename: u16,
    pub moves: u32,
    pub unban_time: u32,
    pub font: u8,
    pub uniqueitem_counter: u32,
    pub hotkey_rowshift: u8,
    pub hotkey_rowshift2: u8,
    pub clan_id: u32,
    pub last_login: Option<Timestamp>,
    pub title_id: u32,
    pub show_equip: u8,
    /// INVENTORY_BASE_SIZE 100 plus expansions.
    pub inventory_slots: i16,
    pub body_direction: u8,
    pub disable_call: u8,
    pub disable_partyinvite: u8,
    pub disable_showcostumes: u8,
}

impl Character {
    /// A new character row with rAthena's column defaults for everything the creation screen does not set.
    #[allow(clippy::too_many_arguments)]
    pub fn new(id: u32, account_id: u32, slot: u8, name: String, class: u16, hair: u16, hair_color: u16, sex: u8, map: &str, x: u16, y: u16, created: Timestamp) -> Self {
        Character {
            id, account_id, slot, name_key: name.to_lowercase(), name, class, base_level: 1, job_level: 1, base_exp: 0, job_exp: 0,
            zeny: 0, status_point: 0, skill_point: 0, str: 1, agi: 1, vit: 1, int: 1, dex: 1, luk: 1, hp: 0, max_hp: 0, sp: 0,
            max_sp: 0, hair, hair_color, clothes_color: 0, body: class, sex, last_map: map.into(), last_x: x, last_y: y,
            save_map: map.into(), save_x: x, save_y: y, party_id: 0, guild_id: 0, delete_at: None, created,
            pow: 0, sta: 0, wis: 0, spl: 0, con: 0, crt: 0, max_ap: 0, ap: 0, trait_point: 0, option: 0, karma: 0, manner: 0,
            pet_id: 0, homun_id: 0, elemental_id: 0, weapon: 0, shield: 0, head_top: 0, head_mid: 0, head_bottom: 0, robe: 0,
            last_instanceid: 0, partner_id: 0, father: 0, mother: 0, child: 0, fame: 0, rename: 0, moves: 0, unban_time: 0,
            font: 0, uniqueitem_counter: 0, hotkey_rowshift: 0, hotkey_rowshift2: 0, clan_id: 0, last_login: None, title_id: 0,
            show_equip: 0, inventory_slots: 100, body_direction: 0, disable_call: 0, disable_partyinvite: 0, disable_showcostumes: 0,
        }
    }
}

/// The logged-in account's characters, for the character screen.
#[view(accessor = my_characters, public)]
pub fn my_characters(ctx: &ViewContext) -> Vec<Character> {
    let Some(l) = ctx.db.session().identity().find(ctx.sender()) else { return Vec::new() };
    ctx.db.character().account_id().filter(l.account_id).collect()
}

/// strlib.cpp normalize_name(name, TRIM_CHARS): trim both ends, collapse inner runs of trim characters to one space.
fn normalize_name(name: &str) -> String {
    name.split(|c: char| rules::TRIM_CHARS.contains(&c)).filter(|p| !p.is_empty()).collect::<Vec<_>>().join(" ")
}

/// char.cpp char_check_char_name, with the renewal defaults of rules.rs.
fn check_char_name(ctx: &ReducerContext, name: &str) -> Result<(), String> {
    if name.is_empty() || name.chars().count() < rules::CHAR_NAME_MIN {
        return Err(format!("Character names need at least {} letters.", rules::CHAR_NAME_MIN));
    }
    if name.eq_ignore_ascii_case(rules::WISP_SERVER_NAME) || name.starts_with('#') {
        return Err("That name is reserved.".into());
    }
    if name.chars().any(|c| !rules::CHAR_NAME_LETTERS.contains(c)) {
        return Err("Use only letters, digits and spaces in character names.".into());
    }
    if ctx.db.character().name_key().find(&name.to_lowercase()).is_some() {
        return Err("That name is already taken.".into());
    }
    Ok(())
}

/// char.cpp char_check_char_name's result (:1334-1389): 0 the name may be used, -1 it exists or is reserved, -2 it is
/// not allowed (empty, too short, the channel symbol, a letter outside `char_name_letters`).
fn char_name_code(ctx: &ReducerContext, name: &str) -> i32 {
    char_name_rule(name, |key| ctx.db.character().name_key().find(&key.to_string()).is_some())
}

/// [`char_name_code`] with the name lookup passed in (rAthena's `SELECT 1 FROM char WHERE name = ...`, which MySQL's
/// default collation answers without regard to case).
fn char_name_rule(name: &str, exists: impl Fn(&str) -> bool) -> i32 {
    if name.is_empty() || name.chars().count() < rules::CHAR_NAME_MIN {
        return -2;
    }
    if name.eq_ignore_ascii_case(rules::WISP_SERVER_NAME) {
        return -1; // nick reserved for internal server messages
    }
    if name.starts_with('#') {
        return -2;
    }
    if name.chars().any(|c| !rules::CHAR_NAME_LETTERS.contains(c)) {
        return -2;
    }
    if exists(&name.to_lowercase()) {
        return -1;
    }
    0
}

/// char_clif.cpp chclif_rename_response's results (:1520-1534), as the client's own message table words them. The
/// reducers answer a refusal with "#<result> <text>": Loki reads the number (it has the texts in the player's
/// language), any other client the text.
fn rename_refused(result: u8) -> String {
    let text = match result {
        1 => "This character's name has already been changed. You cannot change a character's name more than once.",
        2 => "User information is not correct.",
        4 => "Another user is using this character name, so please select another one.",
        5 => "In order to change the character name, you must leave the guild.",
        6 => "In order to change the character name, you must leave the party.",
        8 => "Name contains invalid characters. Character name change failed.",
        _ => "You have failed to change this character's name.",
    };
    format!("#{result} {text}")
}

/// char.cpp char_rename_char_sql's decisions before it writes (:1272-1303): Ok(true) rename, Ok(false) the new name
/// is exactly the old one (answered 0 without using the rename up), Err(result).
fn rename_rule(old_name: &str, new_name: &str, rename: u16, party_id: u32, guild_id: u32, name_code: impl Fn(&str) -> i32) -> Result<bool, u8> {
    if new_name.is_empty() {
        return Err(2); // Not ready for rename
    }
    // If the new name is exactly the same as the old one
    if new_name == old_name {
        return Ok(false);
    }
    if rename == 0 {
        return Err(1);
    }
    if !rules::CHAR_RENAME_PARTY && party_id != 0 {
        return Err(6);
    }
    if !rules::CHAR_RENAME_GUILD && guild_id != 0 {
        return Err(5);
    }
    match name_code(new_name) {
        0 => Ok(true),
        -1 => Err(4), // character already exists
        _ => Err(8),
    }
}

/// char_clif.cpp chclif_parse_reqrename (CH_REQ_IS_VALID_CHARNAME 0x028d, :1421-1458): is the new name free and
/// allowed? Ok = HC_ACK_IS_VALID_CHARNAME 1, Err = 0. (rAthena also keeps the checked name in the session for the old
/// 0x028f rename; for this PACKETVER the rename request carries the name itself, :1559-1568.)
#[reducer]
pub fn rename_check(ctx: &ReducerContext, char_id: u32, new_name: String) -> Result<(), String> {
    let c = own_character(ctx, char_id)?;
    if !char_auth_ok(ctx, c.account_id) {
        return Ok(());
    }
    let name: String = new_name.chars().take(rules::NAME_MAX).collect();
    let name = normalize_name(&name);
    if char_name_code(ctx, &name) != 0 {
        return Err("That name is taken or not allowed.".into());
    }
    Ok(())
}

/// char_clif.cpp chclif_parse_ackrename (CH_REQ_CHANGE_CHARACTERNAME 0x08fc, :1547-1582) → char.cpp
/// char_rename_char_sql (:1267-1332): renames a character that still has a rename (`rename` > 0, which a GM or an
/// item grants) and uses it up. Ok = HC_ACK_CHANGE_CHARNAME 0 (the client then gets the list again: here the
/// `my_characters` row changes); Err = "#<result> <text>" ([`rename_refused`]).
#[reducer]
pub fn rename_character(ctx: &ReducerContext, char_id: u32, new_name: String) -> Result<(), String> {
    let c = own_character(ctx, char_id)?;
    if !char_auth_ok(ctx, c.account_id) {
        return Ok(());
    }
    // safestrncpy(name, p->new_name, NAME_LENGTH); normalize_name(name, TRIM_CHARS) (char_clif.cpp:1562-1564)
    let name: String = new_name.chars().take(rules::NAME_MAX).collect();
    let name = normalize_name(&name);
    match rename_rule(&c.name, &name, c.rename, c.party_id, c.guild_id, |n| char_name_code(ctx, n)) {
        Ok(true) => {}
        Ok(false) => return Ok(()),
        Err(result) => return Err(rename_refused(result)),
    }
    let (old_name, guild_id, rename, account_id) = (c.name.clone(), c.guild_id, c.rename - 1, c.account_id);
    // UPDATE `char` SET `name` = '%s', `rename` = '%d' (--char_dat.rename)
    ctx.db.character().id().update(Character { name: name.clone(), name_key: name.to_lowercase(), rename, ..c });
    // Update party and party members with the new player name (inter_party_charname_changed): never reached while
    // `char_rename_party: no` refuses a character in a party; the party rows hold no name here.
    // Change character's name into guild_db (unreached too while `char_rename_guild: no`).
    if guild_id != 0 {
        crate::world::run(ctx, |cx| crate::int_guild::inter_guild_charname_changed(cx, guild_id as i32, char_id, &name));
    }
    // rAthena's `friends` table holds char ids, and a character's list is loaded with the friends' current names
    // (char.cpp char_mmo_char_fromsql: JOIN on `char`); the rows here hold the names, so they follow the rename
    use crate::social::char_friends;
    let lists: Vec<crate::social::CharFriends> = ctx.db.char_friends().iter().filter(|r| r.friends.iter().any(|f| f.char_id == char_id)).collect();
    for mut r in lists {
        for f in r.friends.iter_mut().filter(|f| f.char_id == char_id) {
            f.name = name.clone();
        }
        ctx.db.char_friends().char_id().update(r);
    }
    // log change (charlog "change char name"; `log_char: yes`): the module keeps no charlog table, the server log has it
    log::info!("change char name: account {account_id} character {char_id} '{old_name}' -> '{name}'");
    Ok(())
}

/// What a slot move does (char_clif.cpp chclif_parse_moveCharSlot, :72-144), decided from the account's characters as
/// (slot, moves left): Err = HC_ACK_CHANGE_CHARACTER_SLOT reason 1; Ok(swap) = the character in `from` goes to `to`,
/// and `swap` says the character that was in `to` goes to `from` (`char_movetoused`: "Admin is friendly and uses
/// triangle exchange"). rAthena tests in this order; a move onto the character's own slot passes as a swap with
/// itself and costs a move (the client never sends it).
fn move_slot_rule(chars: &[(u8, u32)], from: u8, to: u8) -> Result<bool, &'static str> {
    // Bounds check
    if from >= rules::CHAR_SLOTS {
        return Err("That character slot isn't available.");
    }
    let moves = chars.iter().find(|c| c.0 == from).map(|c| c.1).unwrap_or(0);
    // Have we changed too often or is it disabled?
    if !rules::CHAR_MOVE_ENABLED || (!rules::CHAR_MOVES_UNLIMITED && moves == 0) {
        return Err("This character has no slot move left.");
    }
    // Check if there is a character on this slot
    if !chars.iter().any(|c| c.0 == from) {
        return Err("There is no character in that slot.");
    }
    // Bounds check, and the maximum allowed char slot for this account (sd.char_slots: every slot here)
    if to >= rules::CHAR_SLOTS {
        return Err("That character slot isn't available.");
    }
    let used = chars.iter().any(|c| c.0 == to);
    if used && !rules::CHAR_MOVETOUSED {
        return Err("That slot is used."); // Admin doesn't allow us to
    }
    Ok(used)
}

/// char_clif.cpp chclif_parse_moveCharSlot (CH_REQ_CHANGE_CHARACTER_SLOT 0x08d4): moves the character in slot `from`
/// to slot `to`, swapping with the character there, and uses up one of its `moves`. Ok = reason 0 (then the list
/// again: the `my_characters` rows change), Err = reason 1.
#[reducer]
pub fn move_character_slot(ctx: &ReducerContext, from: u8, to: u8) -> Result<(), String> {
    let acc = current_account(ctx)?;
    if !char_auth_ok(ctx, acc.id) {
        return Ok(());
    }
    let mine: Vec<Character> = ctx.db.character().account_id().filter(acc.id).collect();
    let slots: Vec<(u8, u32)> = mine.iter().map(|c| (c.slot, c.moves)).collect();
    let swap = move_slot_rule(&slots, from, to).map_err(|e| e.to_string())?;
    let Some(mover) = mine.iter().find(|c| c.slot == from).cloned() else { return Err("There is no character in that slot.".into()) };
    if swap && from != to {
        if let Some(other) = mine.iter().find(|c| c.slot == to).cloned() {
            ctx.db.character().id().update(Character { slot: from, ..other });
        }
    }
    // sd.char_moves[from]--; UPDATE `char` SET `moves` (not when char_moves_unlimited)
    let moves = if rules::CHAR_MOVES_UNLIMITED { mover.moves } else { mover.moves.saturating_sub(1) };
    log::info!("account {} moved character {} from slot {from} to slot {to} ({moves} move(s) left)", acc.id, mover.id);
    ctx.db.character().id().update(Character { slot: to, moves, ..mover });
    Ok(())
}

/// char.cpp char_make_new_char for PACKETVER >= 20151001: the client picks slot, hair, hair colour, start class
/// (Novice or Summoner) and sex; all six stats start at 1 with `start_status_points` to spend.
#[reducer]
pub fn create_character(ctx: &ReducerContext, slot: u8, name: String, hair: u16, hair_color: u16, start_job: u16, sex: u8) -> Result<(), String> {
    let class = start_job; // rAthena's `start_job` (a parameter named `class` breaks the C# bindings)
    let acc = current_account(ctx)?;
    if !char_auth_ok(ctx, acc.id) {
        return Ok(());
    }
    // safestrncpy(name, name_, NAME_LENGTH): a longer name is cut to 23 bytes (latin-1: one per character), then
    // normalised (char_clif.cpp:1268, char.cpp:1403-1404)
    let name: String = name.chars().take(rules::NAME_MAX).collect();
    let name = normalize_name(&name);
    check_char_name(ctx, &name)?;
    if slot >= rules::CHAR_SLOTS {
        return Err("That character slot isn't available.".into());
    }
    if sex > 1 {
        return Err("Pick male or female.".into());
    }
    if class != rules::JOB_NOVICE && class != rules::JOB_SUMMONER {
        return Err("New characters start as a Novice or a Summoner.".into());
    }
    let mine: Vec<Character> = ctx.db.character().account_id().filter(acc.id).collect();
    if mine.len() >= rules::CHAR_SLOTS as usize {
        return Err("This account has no free character slot.".into());
    }
    if mine.iter().any(|c| c.slot == slot) {
        return Err("That character slot is already used.".into());
    }
    let (points, items) = if class == rules::JOB_SUMMONER { (rules::START_POINTS_DORAM, rules::START_ITEMS_DORAM) } else { (rules::START_POINTS, rules::START_ITEMS) };
    let (map, x, y) = points[ctx.rng().gen_range(0..points.len())];
    // HP and SP as char_make_new_char writes them: 40 * (100 + vit) / 100 and 11 * (100 + int) / 100 with vit = int = 1.
    let (max_hp, max_sp) = (40 * (100 + 1) / 100, 11 * (100 + 1) / 100);
    let id = crate::ids::next(ctx, "char", rules::START_CHAR_ID);
    let mut row = Character::new(id, acc.id, slot, name.clone(), class, hair, hair_color, sex, map, x, y, ctx.timestamp);
    row.zeny = rules::START_ZENY;
    row.status_point = rules::START_STATUS_POINTS;
    (row.hp, row.max_hp, row.sp, row.max_sp) = (max_hp, max_hp, max_sp, max_sp);
    ctx.db.character().insert(row);
    // INSERT INTO inventory (char_id, nameid, amount, equip, identify): the SQL ids come in this order
    for &(item_id, amount, equip) in items {
        let mut item = crate::inventory::Item::new(item_id);
        item.amount = amount as i16;
        item.equip = equip;
        item.identify = 1;
        let save_id = crate::ids::next(ctx, "item_sql_id", 1) as u64;
        ctx.db.inventory().insert(crate::inventory::InventoryItem { id: 0, char_id: id, idx: 0, save_id, item });
    }
    log::info!("account {} created character {id} '{name}' in slot {slot} on {map}", acc.id);
    Ok(())
}

/// char.cpp char_auth_ok's check, which rAthena makes when a client reaches the character screen (every character
/// action comes after it): while a character of the account is in the world, that character is kicked
/// (mapif_disconnectplayer reason 2: it leaves at once, or after prevent_logout's delay) and this client is refused
/// with "Server still recognizes your last login" (auth result 8). Each character-screen reducer makes the check;
/// a refusal is reported through `my_login_notice` and the reducer succeeds, so that the kick is not rolled back.
fn char_auth_ok(ctx: &ReducerContext, account_id: u32) -> bool {
    // the module's login binding outlives rAthena's per-connection login_mmo_auth: a binding made before an @ban /
    // @block must not get past it (login.cpp:365-375; atcmd agent: atcommand_accounts.rs)
    if let Some(refusal) = crate::atcommand_atcmd::accounts::login_mmo_auth_check(&ctx.db, account_id, ctx.timestamp) {
        crate::account::login_refused(ctx, &refusal);
        return false;
    }
    let in_world = crate::world::run(ctx, |c| {
        if crate::pc::get(c, account_id).is_none() {
            return false;
        }
        crate::pc::disconnectplayer(c, account_id, 2);
        true
    });
    if in_world {
        // char.cpp char_auth_ok:1981-1986 `chclif_send_auth_result(fd,8);` (SC_NOTIFY_BAN 8)
        crate::account::login_refused(ctx, &crate::account::notify_ban(8, "Server still recognizes your last login."));
    }
    !in_world
}

/// chclif_reject's comment for error 0 (char_clif.cpp:1096, :1111).
const REJECTED: &str = "Rejected from server.";
const NOT_YOURS: &str = "That character isn't on your account.";

fn own_character(ctx: &ReducerContext, char_id: u32) -> Result<Character, String> {
    let acc = current_account(ctx)?;
    match ctx.db.character().id().find(char_id) {
        Some(c) if c.account_id == acc.id => Ok(c),
        _ => Err(NOT_YOURS.into()),
    }
}

/// [`own_character`] for the deletion reducers: a character that is not the account's is the packet's "not found"
/// result (`packet` / `result`: account.rs `refuse_delete`).
fn own_character_for_delete(ctx: &ReducerContext, char_id: u32, packet: &str, result: u8) -> Result<Character, String> {
    let acc = current_account(ctx)?;
    match ctx.db.character().id().find(char_id) {
        Some(c) if c.account_id == acc.id => Ok(c),
        _ => Err(crate::account::refuse_delete(packet, result, NOT_YOURS)),
    }
}

/// chclif_parse_char_delete2_req (char_clif.cpp:586-664): schedule a deletion `char_del_delay` from now. Refused, in
/// rAthena's order: the character is not the account's (3), already queued (0), in a guild (4) or in a party (5)
/// (`char_del_restriction: 3`).
#[reducer]
pub fn request_delete(ctx: &ReducerContext, char_id: u32) -> Result<(), String> {
    let c = own_character_for_delete(ctx, char_id, "delres", 3)?;
    if !char_auth_ok(ctx, c.account_id) {
        return Ok(());
    }
    if c.delete_at.is_some() {
        return Err(crate::account::refuse_delete("delres", 0, "This character's deletion is already scheduled."));
    }
    if c.guild_id != 0 {
        return Err(crate::account::refuse_delete("delres", 4, "Leave your guild before deleting this character."));
    }
    if c.party_id != 0 {
        return Err(crate::account::refuse_delete("delres", 5, "Leave your party before deleting this character."));
    }
    let at = ctx.timestamp + TimeDuration::from_micros(rules::CHAR_DEL_DELAY_SECS * 1_000_000);
    ctx.db.character().id().update(Character { delete_at: Some(at), ..c });
    Ok(())
}

/// chclif_delchar_check with CHAR_DEL_BIRTHDATE (char_clif.cpp:672-694) for an account whose stored birth date is
/// empty, which every account made here has (login_mmo_auth_new stores "", login.cpp:260; the module keeps no birth
/// date): the code the client sent matches only when it is empty too (`!strcmp("", sd->birthdate) && !strcmp("",
/// delcode)`; the other branch compares against the stored date, which is empty).
pub fn birthdate_matches_none(delcode: &str) -> bool {
    delcode.is_empty()
}

/// chclif_parse_char_delete2_accept (char_clif.cpp:696-748) → char.cpp char_delete (:1545-1612): delete once the
/// scheduled time has passed. `birthdate` is the packet's (CH_DELETE_CHAR3 "YYMMDD"; "" for the six NUL bytes a
/// client sends for an account without one). Refused, in rAthena's order: the birth date does not match (5), the
/// character is not the account's (3), in a guild or a party (2: char_del_restriction; char_del_level is 0), not
/// queued or not yet due (4).
#[reducer]
pub fn confirm_delete(ctx: &ReducerContext, char_id: u32, birthdate: String) -> Result<(), String> {
    current_account(ctx)?;
    // "Only check for birthdate" (before the character is looked at)
    if !birthdate_matches_none(&birthdate) {
        return Err(crate::account::refuse_delete("delete", 5, "The birth date does not match."));
    }
    let c = own_character_for_delete(ctx, char_id, "delete", 3)?;
    if !char_auth_ok(ctx, c.account_id) {
        return Ok(());
    }
    // char.cpp char_delete: check for config char del condition (char_del_level: 0, no level limit), then the
    // char_del_restriction (3: guild and party) and the delay, again at the moment of deletion
    if c.guild_id != 0 {
        return Err(crate::account::refuse_delete("delete", 2, "Leave your guild before deleting this character."));
    }
    if c.party_id != 0 {
        return Err(crate::account::refuse_delete("delete", 2, "Leave your party before deleting this character."));
    }
    match c.delete_at {
        Some(at) if ctx.timestamp >= at => {}
        Some(_) => return Err(crate::account::refuse_delete("delete", 4, "This character can't be deleted yet.")),
        None => return Err(crate::account::refuse_delete("delete", 4, "Schedule the deletion first.")),
    }
    char_delete(ctx, &c);
    log::info!("character {char_id} '{}' deleted", c.name);
    Ok(())
}

/// char.cpp char_delete's clean-up, for the tables the module has (in rAthena's order). The party (never reached:
/// char_del_restriction keeps party members), elemental, mercenary and charlog come with their systems.
fn char_delete(ctx: &ReducerContext, c: &Character) {
    use crate::int_mail::mail;
    use crate::int_pet::pet;
    use crate::inventory::item_cd;
    use crate::pc_misc::char_memo;
    use crate::script::reg::{char_reg_num, char_reg_str};
    use crate::skill::char_skill;
    use crate::social::char_friends;
    use crate::status::data::bonus_script;
    let char_id = c.id;
    // Divorce [Wizputer]: char_divorce_char_sql(char_id, partner_id) and its chmapif_send_ackdivorce
    if c.partner_id != 0 {
        let partner_id = c.partner_id;
        crate::world::run(ctx, |cx| crate::script::buildins::script2::family::chrif_divorce_timer(cx, char_id, partner_id));
    }
    // De-addopt [Zephyrus]: the character is a baby
    if c.father != 0 || c.mother != 0 {
        let (father, mother) = (c.father, c.mother);
        crate::world::run(ctx, |cx| crate::script::buildins::script2::family::char_deadopt(cx, father, mother, char_id));
    }
    // delete char's pet: the hatched one, then every pet that is stored in an egg (inventory + cart; card0 256,
    // pet_id = card1 | card2 << 16)
    let hatched: Vec<u32> = ctx.db.pet().iter().filter(|p| p.char_id == char_id && p.incubate == 0).map(|p| p.pet_id).collect();
    let mut eggs: Vec<u32> = ctx.db.inventory().char_id().filter(char_id).map(|r| r.item).chain(ctx.db.cart_inventory().char_id().filter(char_id).map(|r| r.item)).filter(|it| it.card0 == crate::db::items::CARD0_PET).map(|it| crate::pet::egg_pet_id(&it)).collect();
    eggs.extend(hatched);
    for id in eggs {
        ctx.db.pet().pet_id().delete(id);
    }
    /* remove homunculus */ // (mapif_homunculus_delete, int_homun.rs; char.cpp:1651-1653)
    if c.homun_id != 0 {
        crate::int_homun::delete_rows(&ctx.db, c.homun_id);
        log::info!("Homunculus '{}' has been deleted.", c.homun_id);
    }
    // remove elemental (mapif_elemental_delete); remove mercenary data (mercenary_owner_delete) (char.cpp:1655-1660)
    if c.elemental_id != 0 {
        crate::int_elemental::mapif_elemental_delete(&ctx.db, c.elemental_id as i32);
    }
    crate::int_mercenary::mercenary_owner_delete(&ctx.db, char_id);
    // delete char's friends list, and the char from other's friend list
    ctx.db.char_friends().char_id().delete(char_id);
    let others: Vec<crate::social::CharFriends> = ctx.db.char_friends().iter().filter(|r| r.friends.iter().any(|f| f.char_id == char_id)).collect();
    for mut r in others {
        r.friends.retain(|f| f.char_id != char_id);
        ctx.db.char_friends().char_id().update(r);
    }
    // delete hotkeys (HOTKEY_SAVING)
    crate::hotkey::char_delete(&ctx.db, char_id);
    // delete inventory, cart inventory
    let items: Vec<u64> = ctx.db.inventory().char_id().filter(char_id).map(|i| i.id).collect();
    for id in items {
        ctx.db.inventory().id().delete(id);
    }
    let cart: Vec<u64> = ctx.db.cart_inventory().char_id().filter(char_id).map(|i| i.id).collect();
    for id in cart {
        ctx.db.cart_inventory().id().delete(id);
    }
    // delete memo areas
    ctx.db.char_memo().char_id().delete(char_id);
    // delete character registry
    let regs: Vec<u64> = ctx.db.char_reg_str().by_key().filter(&char_id).map(|r| r.id).collect();
    for id in regs {
        ctx.db.char_reg_str().id().delete(id);
    }
    let regs: Vec<u64> = ctx.db.char_reg_num().by_key().filter(&char_id).map(|r| r.id).collect();
    for id in regs {
        ctx.db.char_reg_num().id().delete(id);
    }
    // delete skills
    let skills: Vec<u64> = ctx.db.char_skill().by_char().filter(&char_id).map(|r| r.id).collect();
    for id in skills {
        ctx.db.char_skill().id().delete(id);
    }
    // delete mails and attachments, where the receiver was the deleted character (the attachments are part of the
    // row here); mark mails as sent from server, where the sender was the deleted character
    let received: Vec<i32> = ctx.db.mail().by_dest().filter(&char_id).map(|m| m.id).collect();
    for id in received {
        ctx.db.mail().id().delete(id);
    }
    let sent: Vec<crate::int_mail::MailRow> = ctx.db.mail().iter().filter(|m| m.send_id == char_id).collect();
    for m in sent {
        ctx.db.mail().id().update(crate::int_mail::MailRow { send_id: 0, ..m });
    }
    // status changes (ENABLE_SC_SAVING), bonus_scripts
    crate::status::sc_data_delete_char(&ctx.db, char_id);
    let bs: Vec<u64> = ctx.db.bonus_script().by_char().filter(&char_id).map(|r| r.id).collect();
    for id in bs {
        ctx.db.bonus_script().id().delete(id);
    }
    // Quest Data
    crate::quest::char_delete(&ctx.db, char_id);
    // Achievement Data
    crate::achievement::char_delete(&ctx.db, char_id);
    // Make the character leave the guild OR break the guild (int_guild.rs; unreachable while char_del_restriction
    // refuses characters in a guild). char.cpp does it after deleting the row; it reads the row here, so it comes
    // first (no packet depends on the order)
    crate::world::run(ctx, |c| crate::int_guild::char_delete_guild(c, c.db().character().id().find(char_id).map(|x| x.account_id).unwrap_or(0), char_id, c.db().character().id().find(char_id).map(|x| x.guild_id).unwrap_or(0)));
    // the map server's itemcd_db entry of the character (memory there, a row here)
    ctx.db.item_cd().char_id().delete(char_id);
    // delete character
    ctx.db.character().id().delete(char_id);
}

/// chclif_parse_char_delete2_cancel (char_clif.cpp:750-778): 2 when the character is not the account's.
#[reducer]
pub fn cancel_delete(ctx: &ReducerContext, char_id: u32) -> Result<(), String> {
    let c = own_character_for_delete(ctx, char_id, "delcancel", 2)?;
    if !char_auth_ok(ctx, c.account_id) {
        return Ok(());
    }
    ctx.db.character().id().update(Character { delete_at: None, ..c });
    Ok(())
}

/// The character screen's Play button. Phase 1 puts the character on its map; for now it is only remembered.
#[reducer]
pub fn select_character(ctx: &ReducerContext, char_id: u32) -> Result<(), String> {
    // char_clif.cpp chclif_parse_charselect:1090-1098: "Check if the character exists and is not scheduled for
    // deletion" (`WHERE account_id = … AND char_num = … AND delete_date = 0`); "Not found?? May be forged packet."
    // -> chclif_reject(fd, 0), HC_REFUSE_ENTER 0 "rejected from server"
    let acc = current_account(ctx)?;
    let Some(c) = ctx.db.character().id().find(char_id).filter(|c| c.account_id == acc.id) else {
        return Err(crate::account::refuse_enter(0, REJECTED));
    };
    if !char_auth_ok(ctx, c.account_id) {
        return Ok(());
    }
    if c.delete_at.is_some() {
        return Err(crate::account::refuse_enter(0, "Cancel this character's deletion to play it."));
    }
    // char_clif.cpp chclif_parse_charselect:1109-1113: a character banned with @charban until a time still to come is
    // rejected (chclif_reject 0) (atcmd agent: atcommand_accounts.rs)
    if crate::atcommand_atcmd::accounts::char_ban_active(&c, ctx.timestamp) {
        return Err(crate::account::refuse_enter(0, REJECTED));
    }
    let l = ctx.db.session().identity().find(ctx.sender()).ok_or("You are not logged in.")?;
    ctx.db.session().identity().update(Session { selected_char: Some(char_id), ..l });
    // char_set_char_online(-2, …) "prior to loading its data": `last_login` = NOW() (char_clif.cpp:1116, char.cpp:123),
    // then its inter_guild_CharOnline (char.cpp:156)
    ctx.db.character().id().update(Character { last_login: Some(ctx.timestamp), ..c });
    crate::world::run(ctx, |cx| {
        crate::int_guild::inter_guild_char_online(cx, char_id, -1);
    });
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    /// char.cpp char_check_char_name with conf/char_athena.conf's stock values.
    #[test]
    fn char_name_rule_is_char_check_char_name() {
        let taken = |k: &str| k == "taken name";
        assert_eq!(char_name_rule("Good Name1", taken), 0);
        assert_eq!(char_name_rule("", taken), -2);
        assert_eq!(char_name_rule("abc", taken), -2); // char_name_min_length: 4
        assert_eq!(char_name_rule("Server", taken), -1); // wisp_server_name
        assert_eq!(char_name_rule("server", taken), -1); // strcmpi
        assert_eq!(char_name_rule("#chan", taken), -2);
        assert_eq!(char_name_rule("Bad_Name", taken), -2); // char_name_letters
        assert_eq!(char_name_rule("Taken Name", taken), -1); // MySQL's collation: without regard to case
    }

    /// char.cpp char_rename_char_sql: the order of its refusals.
    #[test]
    fn rename_rule_is_char_rename_char_sql() {
        let code = |n: &str| if n == "Taken" { -1 } else if n == "bad!" { -2 } else { 0 };
        assert_eq!(rename_rule("Old", "", 1, 0, 0, code), Err(2));
        // the same name: 0 without spending the rename, even with none left and in a guild
        assert_eq!(rename_rule("Old", "Old", 0, 5, 7, code), Ok(false));
        assert_eq!(rename_rule("Old", "old", 1, 0, 0, code), Ok(true)); // strcmp: another case is another name
        assert_eq!(rename_rule("Old", "New Name", 0, 0, 0, code), Err(1));
        assert_eq!(rename_rule("Old", "New Name", 1, 5, 7, code), Err(6)); // party before guild
        assert_eq!(rename_rule("Old", "New Name", 1, 0, 7, code), Err(5));
        assert_eq!(rename_rule("Old", "Taken", 1, 0, 0, code), Err(4));
        assert_eq!(rename_rule("Old", "bad!", 1, 0, 0, code), Err(8));
        assert_eq!(rename_rule("Old", "New Name", 1, 0, 0, code), Ok(true));
        assert!(rename_refused(4).starts_with("#4 Another user"));
    }

    /// char_clif.cpp chclif_parse_moveCharSlot: the order of its refusals, and the swap.
    #[test]
    fn move_slot_rule_is_chclif_parse_move_char_slot() {
        let chars = [(0u8, 1u32), (2, 0), (3, 4)];
        assert!(move_slot_rule(&chars, 15, 1).is_err()); // from out of bounds
        assert!(move_slot_rule(&chars, 2, 1).is_err()); // no move left
        assert!(move_slot_rule(&chars, 1, 4).is_err()); // nobody in the slot (its moves are 0: the first refusal)
        assert!(move_slot_rule(&chars, 0, 15).is_err()); // to out of bounds
        assert_eq!(move_slot_rule(&chars, 0, 1), Ok(false)); // a free slot
        assert_eq!(move_slot_rule(&chars, 0, 14), Ok(false)); // the last slot
        assert_eq!(move_slot_rule(&chars, 0, 2), Ok(true)); // a used slot: swap (char_movetoused: yes)
        assert_eq!(move_slot_rule(&chars, 3, 3), Ok(true)); // onto itself: rAthena's swap with itself
    }

    /// chclif_delchar_check for an account without a birth date: only the empty code matches.
    #[test]
    fn birthdate_of_an_account_without_one() {
        assert!(birthdate_matches_none(""));
        assert!(!birthdate_matches_none("991231"));
        assert!(!birthdate_matches_none("000000")); // "00-00-00" is not "" (client wave 4 found it on rAthena)
    }

    #[test]
    fn normalize_name_trims_rathena_trim_chars() {
        // char.hpp TRIM_CHARS "\255\xA0\032\t\x0A\x0D ": 0xAD (octal 255), 0xA0, 0x1A, tab, LF, CR, space
        assert_eq!(normalize_name("\u{ad} Foo\u{a0}\u{1a}Bar\t\n"), "Foo Bar");
        assert_eq!(normalize_name("  Fix   Tester "), "Fix Tester");
        // 0xFF is not a trim character
        assert_eq!(normalize_name("\u{ff}Foo"), "\u{ff}Foo");
    }
}