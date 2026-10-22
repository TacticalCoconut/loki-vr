//! Native unit tests of the compiler and the VM (no database: `.@` variables and code in the per-instance cache).
//! Run with `cargo test --lib script`.

use std::cell::Cell;

use super::compiler::{self, SCRIPT_RETURN_EMPTY_SCRIPT};
use super::vm::*;
use super::{buildins, code};

thread_local! {
    static NEXT: Cell<u32> = const { Cell::new(1_000_000) };
}

fn compile(src: &str, funcs: &[&str]) -> Result<Option<compiler::ScriptCode>, compiler::ParseError> {
    let names: std::collections::HashSet<Vec<u8>> = funcs.iter().map(|f| f.as_bytes().to_vec()).collect();
    let is_fn = compiler::userfunc_set(&names);
    code::with_table(|t| compiler::parse_script(t, src.as_bytes(), "test", 1, 0, &is_fn))
}

/// Defines a `function script` for callfunc.
fn define(name: &str, body: &str) {
    let names: std::collections::HashSet<Vec<u8>> = std::collections::HashSet::new();
    let is_fn = compiler::userfunc_set(&names);
    let code = code::with_table(|t| compiler::parse_script(t, body.as_bytes(), "func", 1, SCRIPT_RETURN_EMPTY_SCRIPT, &is_fn)).unwrap().unwrap();
    code::insert(CodeKey::Func(name.to_string()), code);
}

/// Compiles and runs `src` to its end; returns the state (its `.@` scope is scope 1).
fn run(src: &str) -> State {
    run_with(src, &[])
}

fn run_with(src: &str, funcs: &[&str]) -> State {
    let code = compile(src, funcs).expect("compiles").expect("not empty");
    let id = NEXT.with(|n| {
        let v = n.get();
        n.set(v + 1);
        v
    });
    let key = CodeKey::Npc(id);
    code::insert(key.clone(), code);
    let buf = code::get(None, &key).unwrap();
    let mut st = State::new(key, buf, 0, 0, 0);
    execute(&Rt::none(), &mut st);
    st
}

fn var(st: &State, name: &str, idx: u32) -> Option<Val> {
    st.scopes.get(&1).and_then(|s| s.vars.get(&(name.to_ascii_lowercase().into_bytes(), idx))).cloned()
}

fn num(st: &State, name: &str) -> i64 {
    num_at(st, name, 0)
}

fn num_at(st: &State, name: &str, idx: u32) -> i64 {
    match var(st, name, idx) {
        Some(Val::Num(n)) => n,
        None => 0,
        other => panic!("{name}[{idx}] is {other:?}"),
    }
}

fn s(st: &State, name: &str) -> String {
    s_at(st, name, 0)
}

fn s_at(st: &State, name: &str, idx: u32) -> String {
    match var(st, name, idx) {
        Some(Val::Str(v)) => String::from_utf8(v).unwrap(),
        None => String::new(),
        other => panic!("{name}[{idx}] is {other:?}"),
    }
}

#[test]
fn arithmetic_and_precedence() {
    let st = run(r#"{
        .@a = 1 + 2 * 3;
        .@b = (1 + 2) * 3;
        .@c = 7 / 2;
        .@d = -7 / 2;
        .@e = -7 % 3;
        .@f = 1 << 40;
        .@g = 0x7fffffff + 1;
        .@h = 10 - 2 - 3;
        .@i = 2 | 4 ^ 7 & 3;
        .@j = !0 + !5 + ~0;
        .@k = 5 > 3 && 2 > 1 || 0;
        .@l = 1 ? 2 : 3;
        .@m = 0 ? 2 : 0 ? 3 : 4;
        .@n = -(-3);
        end;
    }"#);
    assert_eq!(num(&st, ".@a"), 7);
    assert_eq!(num(&st, ".@b"), 9);
    assert_eq!(num(&st, ".@c"), 3);
    assert_eq!(num(&st, ".@d"), -3);
    assert_eq!(num(&st, ".@e"), -1);
    assert_eq!(num(&st, ".@f"), 1 << 40);
    assert_eq!(num(&st, ".@g"), 2147483648);
    assert_eq!(num(&st, ".@h"), 5);
    assert_eq!(num(&st, ".@i"), 2 | (4 ^ (7 & 3)));
    assert_eq!(num(&st, ".@j"), 1 + 0 - 1);
    assert_eq!(num(&st, ".@k"), 1);
    assert_eq!(num(&st, ".@l"), 2);
    assert_eq!(num(&st, ".@m"), 4);
    assert_eq!(num(&st, ".@n"), 3);
    assert_eq!(st.state, END);
}

#[test]
fn compound_assignment_and_increments() {
    let st = run(r#"{
        .@a = 5;
        .@a += 3;
        .@a -= 1;
        .@a *= 4;
        .@a /= 2;
        .@a %= 5;
        .@b = 1;
        .@b <<= 4;
        .@b |= 3;
        .@c = .@b++;
        .@d = ++.@b;
        .@e = .@b--;
        .@f = --.@b;
        .@s$ = "ab";
        .@s$ += "cd";
        end;
    }"#);
    assert_eq!(num(&st, ".@a"), 4); // ((5+3-1)*4/2) % 5 = 14 % 5
    assert_eq!(num(&st, ".@c"), 19);
    assert_eq!(num(&st, ".@d"), 21);
    assert_eq!(num(&st, ".@e"), 21);
    assert_eq!(num(&st, ".@f"), 19);
    assert_eq!(num(&st, ".@b"), 19);
    assert_eq!(s(&st, ".@s$"), "abcd");
}

#[test]
fn overflow_and_division_by_zero_end_the_script() {
    let st = run(r#"{
        .@a = 1;
        .@x = 9223372036854775807;
        .@y = .@x + 1;
        .@a = 2;
        end;
    }"#);
    assert_eq!(num(&st, ".@a"), 1);
    assert_eq!(num(&st, ".@x"), i64::MAX);
    assert_eq!(st.state, END);
    let st = run(r#"{ .@a = 1; .@b = 5 / 0; .@a = 2; end; }"#);
    assert_eq!(num(&st, ".@a"), 1);
}

#[test]
fn strings_and_coercion() {
    let st = run(r#"{
        .@a$ = "abc" + 12;
        .@b$ = 7 + "x";
        .@c = "abc" == "abc";
        .@d = "abc" < "abd";
        .@e = "b" > "abc";
        .@f = "abc" != "ABC";
        .@n$ = 42;
        .@m = .@n$ + 1;
        end;
    }"#);
    assert_eq!(s(&st, ".@a$"), "abc12");
    assert_eq!(s(&st, ".@b$"), "7x");
    assert_eq!(num(&st, ".@c"), 1);
    assert_eq!(num(&st, ".@d"), 1);
    assert_eq!(num(&st, ".@e"), 1);
    assert_eq!(num(&st, ".@f"), 1);
    assert_eq!(s(&st, ".@n$"), "42");
    assert_eq!(num(&st, ".@m"), 421); // "42" + 1 = "421", converted for the numeric variable
}

#[test]
fn string_buildins() {
    let st = run(r#"{
        .@len = getstrlen("hello");
        .@up$ = strtoupper("Hello");
        .@lo$ = strtolower("HeLLo");
        .@sub$ = substr("abcdef", 1, 3);
        .@ch$ = charat("xyz", 2);
        .@pos = strpos("hello world", "o", 5);
        .@rep$ = replacestr("a-b-c", "-", "+");
        .@cnt = countstr("aXbXc", "x", 0);
        .@cnt2 = countstr("aXbXc", "x");
        .@fmt$ = sprintf("%s=%05d|%x|%-3s|", "v", 42, 255, "a");
        .@n = explode(.@parts$, "a,b,,c", ",");
        .@joined$ = implode(.@parts$, "/");
        .@atoi = atoi("  -12abc");
        .@hex = axtoi("ff");
        .@tol = strtol("0x1A", 0);
        .@cmp = compare("Hello World", "WORLD");
        .@scm = strcmp("a", "b");
        .@ins$ = insertchar("ac", "b", 1);
        .@del$ = delchar("abc", 1);
        .@set$ = setchar("abc", "X", 2);
        end;
    }"#);
    assert_eq!(num(&st, ".@len"), 5);
    assert_eq!(s(&st, ".@up$"), "HELLO");
    assert_eq!(s(&st, ".@lo$"), "hello");
    assert_eq!(s(&st, ".@sub$"), "bcd");
    assert_eq!(s(&st, ".@ch$"), "z");
    assert_eq!(num(&st, ".@pos"), 7);
    assert_eq!(s(&st, ".@rep$"), "a+b+c");
    assert_eq!(num(&st, ".@cnt"), 2); // usecase 0: case-insensitive
    assert_eq!(num(&st, ".@cnt2"), 0); // case-sensitive by default
    assert_eq!(s(&st, ".@fmt$"), "v=00042|ff|a  |");
    assert_eq!(s_at(&st, ".@parts$", 0), "a");
    assert_eq!(s_at(&st, ".@parts$", 1), "b");
    assert_eq!(s_at(&st, ".@parts$", 2), "");
    assert_eq!(s_at(&st, ".@parts$", 3), "c");
    assert_eq!(s(&st, ".@joined$"), "a/b//c");
    assert_eq!(num(&st, ".@atoi"), -12);
    assert_eq!(num(&st, ".@hex"), 255);
    assert_eq!(num(&st, ".@tol"), 26);
    assert_eq!(num(&st, ".@cmp"), 1);
    assert_eq!(num(&st, ".@scm"), -1);
    assert_eq!(s(&st, ".@ins$"), "abc");
    assert_eq!(s(&st, ".@del$"), "ac");
    assert_eq!(s(&st, ".@set$"), "abX");
}

#[test]
fn arrays() {
    let st = run(r#"{
        setarray .@a[0], 10, 20, 30, 40;
        .@size = getarraysize(.@a);
        .@third = .@a[2];
        .@a[7] = 5;
        .@size2 = getarraysize(.@a);
        deletearray .@a[1], 2;
        .@after0 = .@a[0];
        .@after1 = .@a[1];
        .@size3 = getarraysize(.@a);
        copyarray .@b[1], .@a[0], 3;
        cleararray .@c[0], 7, 3;
        .@in = inarray(.@a, 40);
        .@notin = inarray(.@a, 99);
        setarray .@s$[0], "x", "y", "z";
        .@ssize = getarraysize(.@s$);
        .@count = countinarray(.@a[0], .@b[0]);
        .@i = 2;
        .@dyn = .@a[.@i - 2];
        end;
    }"#);
    assert_eq!(num(&st, ".@size"), 4);
    assert_eq!(num(&st, ".@third"), 30);
    assert_eq!(num(&st, ".@size2"), 8);
    assert_eq!(num(&st, ".@after0"), 10);
    assert_eq!(num(&st, ".@after1"), 40);
    assert_eq!(num(&st, ".@size3"), 6);
    assert_eq!(num_at(&st, ".@b", 0), 0);
    assert_eq!(num_at(&st, ".@b", 1), 10);
    assert_eq!(num_at(&st, ".@b", 2), 40);
    assert_eq!(num_at(&st, ".@b", 3), 0);
    assert_eq!(num_at(&st, ".@c", 0), 7);
    assert_eq!(num_at(&st, ".@c", 2), 7);
    assert_eq!(num_at(&st, ".@c", 3), 0);
    assert_eq!(num(&st, ".@in"), 1);
    assert_eq!(num(&st, ".@notin"), -1);
    assert_eq!(num(&st, ".@ssize"), 3);
    assert_eq!(num(&st, ".@dyn"), 10);
}

#[test]
fn control_flow() {
    let st = run(r#"{
        for (.@i = 0; .@i < 5; .@i++)
            .@sum += .@i;
        .@j = 0;
        while (.@j < 3) {
            .@j++;
            if (.@j == 2) continue;
            .@w += 10;
        }
        do {
            .@k++;
        } while (.@k < 4);
        if (.@sum > 100) .@branch = 1;
        else if (.@sum == 10) .@branch = 2;
        else .@branch = 3;
        goto L_Skip;
        .@skipped = 1;
    L_Skip:
        .@after = 1;
        end;
    }"#);
    assert_eq!(num(&st, ".@sum"), 10);
    assert_eq!(num(&st, ".@w"), 20);
    assert_eq!(num(&st, ".@k"), 4);
    assert_eq!(num(&st, ".@branch"), 2);
    assert_eq!(num(&st, ".@skipped"), 0);
    assert_eq!(num(&st, ".@after"), 1);
}

#[test]
fn callsub_and_local_functions() {
    let st = run(r#"{
        function Twice;
        .@r = callsub(S_Add, 3, 4);
        .@t = Twice(21);
        .@args = callsub(S_Count, 1, "a", 3);
        .@scope = 1;
        callsub S_Scope;
        end;
    S_Add:
        return getarg(0) + getarg(1);
    S_Count:
        return getargcount();
    S_Scope:
        .@scope = 2; // a new scope: the caller's .@scope stays
        return;
    function Twice {
        return getarg(0) * 2;
    }
    }"#);
    assert_eq!(num(&st, ".@r"), 7);
    assert_eq!(num(&st, ".@t"), 42);
    assert_eq!(num(&st, ".@args"), 3);
    assert_eq!(num(&st, ".@scope"), 1);
}

#[test]
fn callfunc_with_references_and_defaults() {
    define("T_Fill", r#"{
        setarray getelementofarray(getarg(0), 0), 7, 8, 9;
        set getarg(1), getarg(2, 55);
        return getarg(3, "dflt");
    }"#);
    define("T_Rec", r#"{
        if (getarg(0) <= 1) return 1;
        return getarg(0) * callfunc("T_Rec", getarg(0) - 1);
    }"#);
    let st = run(r#"{
        .@r$ = callfunc("T_Fill", .@arr, .@out);
        .@fact = callfunc("T_Rec", 10);
        .@exists = is_function("T_Rec");
        .@missing = is_function("T_Nope");
        end;
    }"#);
    assert_eq!(num_at(&st, ".@arr", 0), 7);
    assert_eq!(num_at(&st, ".@arr", 2), 9);
    assert_eq!(num(&st, ".@out"), 55);
    assert_eq!(s(&st, ".@r$"), "dflt");
    assert_eq!(num(&st, ".@fact"), 3628800);
    assert_eq!(num(&st, ".@exists"), 1);
    assert_eq!(num(&st, ".@missing"), 0);
}

#[test]
fn getd_setd_and_math() {
    let st = run(r#"{
        setd ".@v" + 3, 12;
        .@g = getd(".@v3");
        setd ".@name$", "zz";
        .@p = pow(2, 10);
        .@q = sqrt(99);
        .@mn = min(5, 3, 9);
        .@mx = max(5, 3, 9);
        .@cap = cap_value(15, 0, 10);
        .@dist = distance(0, 0, 3, 4);
        end;
    }"#);
    assert_eq!(num(&st, ".@g"), 12);
    assert_eq!(s(&st, ".@name$"), "zz");
    assert_eq!(num(&st, ".@p"), 1024);
    assert_eq!(num(&st, ".@q"), 9);
    assert_eq!(num(&st, ".@mn"), 3);
    assert_eq!(num(&st, ".@mx"), 9);
    assert_eq!(num(&st, ".@cap"), 10);
    assert_eq!(num(&st, ".@dist"), 4);
}

#[test]
fn compile_errors_are_reported() {
    assert!(compile("{ .@a = ; }", &[]).is_err());
    assert!(compile("{ mes \"x\" }", &[]).is_err()); // missing ';'
    assert!(compile("{ function F_Declared; end; }", &[]).is_err()); // declared but not defined
    assert!(compile("{ goto L_Nowhere; }", &[]).is_ok()); // an unknown label defaults to a variable
    assert!(compile("{ if (1) { end; }", &[]).is_err()); // unclosed block
    assert!(compile("{ end; }", &[]).unwrap().is_some());
    assert!(compile("{}", &[]).unwrap().is_none());
}

#[test]
fn every_buildin_is_ported() {
    // wave F ported the last thirteen (plagiarizeskill … openstorage2): every buildin_func[] entry has its Rust
    // function, vm.rs's "not ported yet" end of the script is only a safety net now
    let ported = buildins::ported();
    assert_eq!(ported.len(), super::buildin_table::BUILDIN.len());
    for name in [
        "plagiarizeskill", "plagiarizeskillreset", "pvpon", "pvpoff", "unitattack", "unitstopattack", "checkidlemer", "openauction", "npcskill",
        "addspiritball", "delspiritball", "countspiritball", "openstorage2",
    ] {
        assert!(ported.contains(&name), "{name}");
    }
    // openauction without a player: script_charid2sd → script_rid2sd ends the script
    let st = run(r#"{ .@a = 1; openauction; .@a = 2; end; }"#);
    assert_eq!(num(&st, ".@a"), 1);
    assert_eq!(st.state, END);
}

/// The wave F commands without a world: a missing unit or a player named / numbered in the arguments is a failure the
/// script survives (unitattack and checkidlemer answer 0; the spirit sphere commands look the player up with
/// map_charid2sd / map_nick2sd, which report nothing; openauction's script_charid2sd reports it), while the commands
/// of the attached player end the script when there is none (script_rid2sd) and openstorage2 ends it whenever its
/// player is missing (`st->state = END`).
#[test]
fn wave_f_commands_without_a_world() {
    let st = run(r#"{
        .@u = 5; .@u = unitattack(0, "Nobody", 1);
        .@i = 5; .@i = checkidlemer("Nobody");
        unitstopattack 0;
        pvpon "no_such_map"; pvpoff "no_such_map";
        addspiritball 3, 1000, "Nobody";
        delspiritball 1, 150000;
        openauction 150000;
        .@end = 1;
        end;
    }"#);
    assert_eq!(num(&st, ".@u"), 0);
    assert_eq!(num(&st, ".@i"), 0);
    assert_eq!(num(&st, ".@end"), 1);
    for cmd in [
        "plagiarizeskill 5, 10;",
        "plagiarizeskillreset 1;",
        "npcskill 5, 1, 1, 1;",
        "addspiritball 1, 1000;",
        "delspiritball 1;",
        "countspiritball;",
        "openauction;",
        "openstorage2 0;",
        "openstorage2 0, 1, 2000000;",
    ] {
        let st = run(&format!("{{ .@a = 1; {cmd} .@a = 2; end; }}"));
        assert_eq!(num(&st, ".@a"), 1, "{cmd}");
        assert_eq!(st.state, END, "{cmd}");
    }
}

/// The argument rules of the wave F commands that need no world.
#[test]
fn wave_f_argument_rules() {
    use super::battle_flags::battle_get_value;
    use buildins::script2::{mapmisc, unitmove};
    use buildins::{items, skilluse};
    // plagiarizeskill: cap_value(skill_lv, 1, skill_get_max) on the uint16
    assert_eq!(crate::skilltree::plagiarism_level(0, 10), 1);
    assert_eq!(crate::skilltree::plagiarism_level(7, 10), 7);
    assert_eq!(crate::skilltree::plagiarism_level(11, 10), 10);
    assert_eq!(crate::skilltree::plagiarism_level(-1i32 as u16, 10), 10);
    // pvpon / pvpoff: map_setmapflag only when MF_PVP changes
    assert!(mapmisc::pvp_switch_needed(false, true));
    assert!(mapmisc::pvp_switch_needed(true, false));
    assert!(!mapmisc::pvp_switch_needed(true, true));
    assert!(!mapmisc::pvp_switch_needed(false, false));
    // unitattack: a player's action type
    assert_eq!(unitmove::unitattack_action(0), 0); // DMG_NORMAL
    assert_eq!(unitmove::unitattack_action(-3), 0);
    assert_eq!(unitmove::unitattack_action(1), 7); // DMG_REPEAT
    assert_eq!(unitmove::unitattack_action(42), 7);
    // openauction: the feature is off (and forced off for this client)
    assert_eq!(buildins::social2::feature_auction(), battle_get_value("feature.auction"));
    assert_eq!(buildins::social2::feature_auction(), 0);
    // npcskill: uint32 stat point and level
    assert_eq!(skilluse::max_third_parameter(), battle_get_value("max_third_parameter"));
    assert!(skilluse::npcskill_check(130, 275).is_ok());
    assert!(skilluse::npcskill_check(0, 0).is_ok());
    assert_eq!(skilluse::npcskill_check(131, 1).unwrap_err(), "npcskill: stat point exceeded maximum of 130.");
    assert_eq!(skilluse::npcskill_check(-1i32 as u32, 1).unwrap_err(), "npcskill: stat point exceeded maximum of 130.");
    assert_eq!(skilluse::npcskill_check(1, 276).unwrap_err(), "npcskill: level exceeded maximum of 275.");
    assert_eq!(skilluse::npcskill_check(1, -5i32 as u32).unwrap_err(), "npcskill: level exceeded maximum of 275.");
    // delspiritball: uint8 count, 0 → 1
    assert_eq!(skilluse::delspiritball_count(0), 1);
    assert_eq!(skilluse::delspiritball_count(3), 3);
    assert_eq!(skilluse::delspiritball_count(256), 1);
    assert_eq!(skilluse::delspiritball_count(-1), 255);
    // openstorage2: storage_db holds storage 0 only (the id is a uint8), modes NONE .. ALL
    assert!(crate::storage::storage_exists(0));
    assert!(crate::storage::storage_exists(256i32 as u8));
    assert!(!crate::storage::storage_exists(1));
    assert!(!crate::storage::storage_exists(255));
    assert_eq!(crate::storage::storage_get_name(0), "Storage");
    assert_eq!(crate::storage::storage_get_name(9), "Storage");
    assert!(!items::openstorage2_mode_ok(-1));
    assert!((0..=3).all(items::openstorage2_mode_ok));
    assert!(!items::openstorage2_mode_ok(4));
}

#[test]
fn labels_are_exported() {
    let names = std::collections::HashSet::new();
    let is_fn = compiler::userfunc_set(&names);
    let src = "{ end; OnInit: end; OnTimer1000: end; L_x: end; }";
    let code = code::with_table(|t| compiler::parse_script(t, src.as_bytes(), "test", 1, compiler::SCRIPT_USE_LABEL_DB, &is_fn)).unwrap().unwrap();
    let mut names: Vec<&str> = code.labels.iter().map(|(n, _)| n.as_str()).collect();
    names.sort();
    assert_eq!(names, vec!["L_x", "OnInit", "OnTimer1000"]);
}

/// The SpacetimeDB host functions the database code links against. Native tests never reach the database (the VM
/// runs with `Rt::none()`), but the linker still needs the symbols.
mod host_stubs {
    macro_rules! stub {
        ($($name:ident),* $(,)?) => {
            $(
                #[no_mangle]
                pub extern "C" fn $name() -> u16 {
                    panic!(concat!(stringify!($name), ": no database in native tests"))
                }
            )*
        };
    }
    stub!(
        table_id_from_name,
        index_id_from_name,
        datastore_table_row_count,
        datastore_table_scan_bsatn,
        datastore_index_scan_range_bsatn,
        datastore_index_scan_point_bsatn,
        datastore_delete_by_index_scan_range_bsatn,
        datastore_delete_by_index_scan_point_bsatn,
        datastore_delete_all_by_eq_bsatn,
        row_iter_bsatn_advance,
        row_iter_bsatn_close,
        datastore_insert_bsatn,
        datastore_update_bsatn,
        datastore_clear,
        volatile_nonatomic_schedule_immediate,
        identity,
    );
}

fn test_provider(_c: Option<&crate::world::Ctx>, key: &str) -> Option<code::ExtSource> {
    let text = match key {
        // item_db style: no outer braces (SCRIPT_IGNORE_EXTERNAL_BRACKETS)
        "501" => "if (1) .@heal = 45 + 20; .@name$ = \"Red Potion\";",
        _ => return None,
    };
    Some(code::ExtSource { text: text.as_bytes().to_vec(), file: "item_db.yml".into(), line: 7, options: compiler::SCRIPT_IGNORE_EXTERNAL_BRACKETS, userfuncs: Vec::new() })
}

#[test]
fn ext_scripts_compile_through_providers() {
    code::register_provider("testitem", test_provider);
    let key = CodeKey::Ext(ExtCode { kind: "testitem".into(), key: "501".into() });
    let buf = code::get(None, &key).expect("compiles");
    let mut st = State::new(key, buf, 0, 0, 0);
    execute(&Rt::none(), &mut st);
    assert_eq!(num(&st, ".@heal"), 65);
    assert_eq!(s(&st, ".@name$"), "Red Potion");
    assert!(code::get(None, &CodeKey::Ext(ExtCode { kind: "testitem".into(), key: "999".into() })).is_none());
}

#[test]
#[ignore]
fn print_ported() {
    { use std::io::Write; let _ = writeln!(std::io::stdout(), "PORTED {}", buildins::ported().join(" ")); }
}

/// script_errorwarning_sub's layout (script.cpp:2403-2449): the lines before, the starred line of the position with
/// the character quoted, the lines after; a position on a newline belongs to the next line.
#[test]
fn error_report_layout() {
    let src = b"{\n\tmes \"a\";\n\tfoo;\n}\n";
    let pos = src.iter().position(|&c| c == b'f').unwrap();
    let out = compiler::script_errorwarning_sub(src, "test", 10, "parse_line: expect command", pos);
    assert_eq!(out, "script error on test line 12\n    parse_line: expect command\n    10 : {\n    11 : \tmes \"a\";\n*   12 : \t'f'oo;\n    13 : }\n");
    // the newline that ends line 11: reported on line 12, nothing quoted
    let nl = src.iter().position(|&c| c == b';').unwrap() + 1;
    let out = compiler::script_errorwarning_sub(src, "test", 10, "m", nl);
    assert!(out.starts_with("script error on test line 12\n"), "{out}");
    assert!(out.contains("*   12 : \tfoo;\n"), "{out}");
    // five-digit line numbers keep the sign blank ("% 5d")
    let out = compiler::script_errorwarning_sub(b"a\n", "f", 12345, "m", 0);
    assert!(out.contains("* 12345 : 'a'\n"), "{out}");
}

/// SCRIPT_COMMAND_DEPRECATION: getvariableofinstance is deprecated; the deprecated getmonsterinfo (strmobinfo) is
/// redefined by the current one, which add_buildin_func leaves undeprecated.
#[test]
fn deprecated_buildins() {
    let idx = |n: &str, f: &str| super::buildin_table::BUILDIN.iter().position(|b| b.0 == n && b.2 == f).unwrap();
    assert_eq!(compiler::buildin_deprecated(idx("getvariableofinstance", "getinstancevar")), Some("2021-12-13"));
    assert_eq!(compiler::buildin_deprecated(idx("getinstancevar", "getinstancevar")), None);
    assert_eq!(compiler::buildin_deprecated(idx("getmonsterinfo", "strmobinfo")), Some("2025-03-11"));
    assert_eq!(compiler::buildin_deprecated(idx("getmonsterinfo", "getmonsterinfo")), None);
    code::with_table(|t| {
        let n = t.search_str(b"getvariableofinstance");
        assert!(t.data[n as usize].deprecated);
        let n = t.search_str(b"getmonsterinfo");
        assert!(!t.data[n as usize].deprecated);
    });
}