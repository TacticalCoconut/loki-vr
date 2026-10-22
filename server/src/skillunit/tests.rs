//! Unit tests of the skill unit port: the constants and battle_config values against the reference server's, and
//! skillunit_db's red-black trees (db.cpp's algorithms) against their invariants and iteration order.

use super::consts::*;
use super::unitdb::{bucket, Db, MemStore, Store, HASH_SIZE};

#[test]
fn unit_ids_match_the_reference_constants() {
    // script_constants.hpp exports every e_skill_unit_id value but these (the enum's //TODO entries and UNT_MAX),
    // which the check cannot see; their values follow from the enum's order
    const NOT_EXPORTED: &[&str] = &[
        "UNT_BENEDICTIO", "UNT_HIDDEN_TRAP", "UNT_TRAP", "UNT_HIDDEN_WARP_NPC", "UNT_KAENSIN", "UNT_DEATHWAVE",
        "UNT_WATERATTACK", "UNT_WINDATTACK", "UNT_DARK_RUNNER", "UNT_DARK_TRANSFER", "UNT_MAX",
    ];
    let mut checked = 0;
    for &(name, value) in UNT_ALL {
        if NOT_EXPORTED.contains(&name) {
            assert_eq!(crate::data::constant(name), None, "{name} is exported now: check it");
            continue;
        }
        assert_eq!(crate::data::constant(name), Some(value as i64), "{name}");
        checked += 1;
    }
    assert_eq!(checked, UNT_ALL.len() - NOT_EXPORTED.len());
    // the unexported ones sit between exported neighbours of the consecutive enum
    assert_eq!(UNT_BENEDICTIO, UNT_WARP_ACTIVE + 1);
    assert_eq!(UNT_HIDDEN_TRAP, UNT_FIREPILLAR_ACTIVE + 1);
    assert_eq!(UNT_USED_TRAPS, UNT_HIDDEN_WARP_NPC + 1);
    assert_eq!(UNT_SUITON, UNT_KAENSIN + 1);
    assert_eq!(UNT_EARTHQUAKE, UNT_WINDATTACK + 1);
    assert_eq!(UNT_EPICLESIS, UNT_DARK_TRANSFER + 1);
}

#[test]
fn conf_matches_battle_flags() {
    for (key, value) in super::conf::checked() {
        let found = crate::script::battle_flags::BATTLE_FLAGS.iter().find(|(k, _)| k.eq_ignore_ascii_case(key)).map(|&(_, v)| v);
        assert_eq!(found, Some(value), "{key}");
    }
}

/// Checks the red-black invariants and parent links of every bucket; returns the linked node count.
fn check(db: &Db<MemStore>) -> usize {
    fn walk(db: &Db<MemStore>, n: u32, parent: u32) -> (usize, usize) {
        if n == 0 {
            return (1, 0);
        }
        let node = db.s.get(n).expect("linked node");
        assert_eq!(node.parent, parent, "parent link of {n}");
        if node.red {
            assert!(node.left == 0 || !db.s.get(node.left).unwrap().red, "red {n} with a red child");
            assert!(node.right == 0 || !db.s.get(node.right).unwrap().red, "red {n} with a red child");
        }
        if node.left != 0 {
            assert!((node.left as i32) < (n as i32));
        }
        if node.right != 0 {
            assert!((node.right as i32) > (n as i32));
        }
        let (bl, cl) = walk(db, node.left, n);
        let (br, cr) = walk(db, node.right, n);
        assert_eq!(bl, br, "black height at {n}");
        (bl + (!node.red) as usize, cl + cr + 1)
    }
    let mut total = 0;
    for b in 0..HASH_SIZE {
        let r = db.s.root(b);
        if r != 0 {
            assert!(!db.s.get(r).unwrap().red, "red root in bucket {b}");
            assert_eq!(bucket(r), b);
        }
        total += walk(db, r, 0).1;
    }
    total
}

/// db_obj_vforeach's order over a store.
fn order(db: &Db<MemStore>) -> Vec<u32> {
    let mut out = Vec::new();
    for b in 0..HASH_SIZE {
        let mut n = db.s.root(b);
        while n != 0 {
            if !db.deleted(n) {
                out.push(n);
            }
            n = db.next(n);
        }
    }
    out
}

#[test]
fn skillunit_db_keeps_rb_invariants_and_walks_bucket_preorder() {
    let mut db = Db { s: MemStore::default() };
    // consecutive object ids, as skill_initunit allocates them, wrapping around the hash size several times
    for id in 2..2000u32 {
        assert_eq!(db.put(id), 0);
    }
    assert_eq!(check(&db), 1998);
    let o = order(&db);
    assert_eq!(o.len(), 1998);
    // bucket order first
    assert!(o.windows(2).all(|w| bucket(w[0]) <= bucket(w[1])));
    // an existing key is replaced (retval 1), nothing is added
    assert_eq!(db.put(7), 1);
    assert_eq!(check(&db), 1998);
    // removals under a lock (skill_unit_timer's foreach) stay linked but are skipped, then go in free-list order
    db.lock();
    for id in (2..2000u32).step_by(3) {
        assert_eq!(db.remove(id), 1);
    }
    assert_eq!(check(&db), 1998);
    assert_eq!(order(&db).len(), 1998 - 666);
    // put of a key removed under the lock revives it (db_free_remove)
    assert_eq!(db.put(5), 0);
    assert_eq!(order(&db).len(), 1998 - 665);
    db.unlock();
    assert_eq!(check(&db), 1998 - 665);
    assert_eq!(db.s.free_count(), 0);
    // unlocked removals unlink at once
    for id in 2..2000u32 {
        db.remove(id);
        if id % 97 == 0 {
            check(&db);
        }
    }
    assert_eq!(check(&db), 0);
}

#[test]
fn skillunit_db_single_bucket_preorder() {
    // keys that share a bucket form one tree: 283 apart
    let mut db = Db { s: MemStore::default() };
    let keys: Vec<u32> = (0..10).map(|i| 5 + i * HASH_SIZE).collect();
    for &k in &keys {
        db.put(k);
    }
    check(&db);
    // inserted in ascending order, the red-black tree of 10 keys is rooted at the 4th key; pre-order visits the root
    // first, then its left subtree
    let o = order(&db);
    assert_eq!(o.len(), 10);
    assert_eq!(o[0], keys[3]);
    assert_eq!(o[1], keys[1]);
}
