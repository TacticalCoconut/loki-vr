//! The script sources the status system runs (script::code providers):
//!
//! | kind           | key                                  | rAthena                                                 |
//! |----------------|--------------------------------------|---------------------------------------------------------|
//! | `item`         | `<nameid>`, `<nameid>:equip`/`:unequip`, `combo:<id>` | item_data::script / equip_script / unequip_script, s_item_combo::script — the items system's provider (inventory.rs item_script_source), shared with pc_useitem / pc_equipitem so each script is compiled once |
//! | `randomopt`    | `<option id>`                        | s_random_opt_data::script (item_randomopt_db.yml)       |
//! | `sc`           | `<sc_type>`                          | s_status_change_db::script (status.yml `Script`)        |
//! | `bonus_script` | the script text                      | pc_bonus_script_add's parse_script(…, "bonus_script")   |
//! | `autobonus`    | the script text                      | script_add_autobonus's parse_script(…, "autobonus", 0, 0) |
//!
//! The database scripts are parsed with SCRIPT_IGNORE_EXTERNAL_BRACKETS like itemdb / status.yml loading does; the
//! file names only show in error reports.

use std::cell::Cell;

use crate::script::code::{register_provider, ExtSource};
use crate::script::compiler::SCRIPT_IGNORE_EXTERNAL_BRACKETS;
use crate::world::Ctx;

thread_local! {
    static REGISTERED: Cell<bool> = const { Cell::new(false) };
}

/// Registers the providers once per module instance.
pub fn ensure_providers() {
    if REGISTERED.with(|r| r.get()) {
        return;
    }
    register_provider("randomopt", randomopt_provider);
    register_provider("sc", sc_provider);
    register_provider("bonus_script", bonus_script_provider);
    register_provider("autobonus", autobonus_provider);
    REGISTERED.with(|r| r.set(true));
}

/// A database script (the dataset's text, as the item scripts of the items system are given).
fn src(text: &str, file: &str, options: u32) -> ExtSource {
    ExtSource { text: text.as_bytes().to_vec(), file: file.to_string(), line: 0, options, userfuncs: Vec::new() }
}

/// A script text that came from a script (the key holds its Latin-1 bytes as chars).
fn src_latin1(text: &str, file: &str, options: u32) -> ExtSource {
    ExtSource { text: crate::net::wire::latin1(text), file: file.to_string(), line: 0, options, userfuncs: Vec::new() }
}

fn randomopt_provider(_c: Option<&Ctx>, key: &str) -> Option<ExtSource> {
    let id: u16 = key.parse().ok()?;
    let o = crate::db::items::randomopt::random_option(id)?;
    Some(src(o.script.as_deref()?, "db/re/item_randomopt_db.yml", SCRIPT_IGNORE_EXTERNAL_BRACKETS))
}

fn sc_provider(_c: Option<&Ctx>, key: &str) -> Option<ExtSource> {
    let t: i32 = key.parse().ok()?;
    let s = crate::db::misc::status::status_db().find(t)?;
    Some(src(s.script.as_deref()?, "db/re/status.yml", SCRIPT_IGNORE_EXTERNAL_BRACKETS))
}

fn bonus_script_provider(_c: Option<&Ctx>, key: &str) -> Option<ExtSource> {
    Some(src_latin1(key, "bonus_script", SCRIPT_IGNORE_EXTERNAL_BRACKETS))
}

fn autobonus_provider(_c: Option<&Ctx>, key: &str) -> Option<ExtSource> {
    Some(src_latin1(key, "autobonus", 0))
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::script::vm::{CodeKey, ExtCode};

    /// A script text with nothing but blanks and comments.
    fn only_comments(t: &str) -> bool {
        let mut rest = t.trim();
        loop {
            if let Some(r) = rest.strip_prefix("/*") {
                match r.find("*/") {
                    Some(i) => rest = r[i + 2..].trim(),
                    None => return false,
                }
            } else if let Some(r) = rest.strip_prefix("//") {
                rest = r.split_once('\n').map(|x| x.1).unwrap_or("").trim();
            } else {
                return rest.is_empty();
            }
        }
    }

    fn compiles(kind: &str, key: &str) -> bool {
        crate::script::code::get(None, &CodeKey::Ext(ExtCode { kind: kind.into(), key: key.into() })).is_some()
    }

    /// Every item / combo / random option / status.yml script compiles through the providers (as itemdb / status.yml loading
    /// parses them with SCRIPT_IGNORE_EXTERNAL_BRACKETS).
    #[test]
    fn database_scripts_compile() {
        ensure_providers();
        let mut bad = Vec::new();
        let mut n = 0;
        for d in crate::db::items::item_db() {
            let Some(s) = crate::db::items::item_scripts(d.nameid) else { continue };
            for (which, text) in [("", &s.script), (":equip", &s.equip_script), (":unequip", &s.unequip_script)] {
                if let Some(t) = text {
                    n += 1;
                    let key = format!("{}{which}", d.nameid);
                    // parse_script returns nullptr for a script that is empty once comments are skipped
                    if !compiles("item", &key) && !only_comments(t) {
                        bad.push(key);
                    }
                }
            }
        }
        for combo in crate::db::items::combo::item_combos() {
            if let Some(t) = &combo.script {
                n += 1;
                if !compiles("item", &format!("combo:{}", combo.id)) && !only_comments(t) {
                    bad.push(format!("combo {}", combo.id));
                }
            }
        }
        for o in &crate::db::items::randomopt::random_option_file().options {
            if let Some(t) = &o.script {
                n += 1;
                if !compiles("randomopt", &o.id.to_string()) && !only_comments(t) {
                    bad.push(format!("randomopt {}", o.id));
                }
            }
        }
        for s in crate::db::misc::status::status_db().iter() {
            if s.script.is_some() {
                n += 1;
                if !compiles("sc", &s.type_.to_string()) {
                    bad.push(format!("sc {}", s.type_));
                }
            }
        }
        { use std::io::Write; let _ = writeln!(std::io::stdout(), "{n} scripts, {} failed: {:?}", bad.len(), &bad[..bad.len().min(20)]); }
        assert!(bad.is_empty());
    }

    #[test]
    fn bonus_and_autobonus_texts() {
        ensure_providers();
        assert!(compiles("bonus_script", "bonus bStr,10;"));
        assert!(compiles("autobonus", "{ bonus bAspdRate,5; }"));
        assert!(!compiles("bonus_script", "bonus bStr,"));
    }
}
