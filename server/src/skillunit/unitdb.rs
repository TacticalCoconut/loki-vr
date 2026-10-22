//! skill.cpp `skillunit_db` (idb_alloc(DB_OPT_BASE): unit id -> skill_unit*): the order skill_unit_timer walks the
//! units in. common/db.cpp keeps a DB_INT map as 283 hash buckets (key % HASH_SIZE) of red-black trees and
//! db_obj_vforeach walks them bucket by bucket in pre-order; a node removed while the map is locked (during that
//! foreach) is only marked deleted and unlinked (db_rebalance_erase) when the last lock goes, in free-list order.
//! The trees live in tables so the order survives from one reducer to the next exactly as rAthena's map does; the
//! algorithms are db.cpp's (db_obj_put, db_rebalance, db_obj_remove, db_free_add / db_free_remove, db_free_unlock,
//! db_rebalance_erase, db_obj_vforeach), written over a small storage trait so the unit tests can run them in memory.
//!
//! Keys are object ids (never 0), so 0 stands for a null pointer.

use spacetimedb::{table, Table};

use crate::world::Ctx;

/// db.cpp HASH_SIZE.
pub const HASH_SIZE: u32 = 256 + 27;

/// A DBNode of skillunit_db (key = the unit id; its data is the unit itself).
#[table(accessor = skillunit_db_node)]
#[derive(Clone, Debug, PartialEq)]
pub struct UnitDbNode {
    #[primary_key]
    pub key: u32,
    pub parent: u32,
    pub left: u32,
    pub right: u32,
    pub red: bool,
    pub deleted: bool,
}

/// db->ht[bucket]: a bucket's root (no row: nullptr).
#[table(accessor = skillunit_db_root)]
#[derive(Clone, Debug)]
pub struct UnitDbRoot {
    #[primary_key]
    pub bucket: u32,
    pub node: u32,
}

/// db->free_list[pos] (pos 0..free_count): a node removed under lock, with its bucket (the `root` pointer).
#[table(accessor = skillunit_db_free)]
#[derive(Clone, Debug)]
pub struct UnitDbFree {
    #[primary_key]
    pub pos: u32,
    pub node: u32,
    pub bucket: u32,
}

/// db_int_hash (`(uint64)key.i`) % HASH_SIZE.
pub fn bucket(key: u32) -> u32 {
    ((key as i32 as i64 as u64) % HASH_SIZE as u64) as u32
}

/// db_int_cmp.
fn cmp(a: u32, b: u32) -> i32 {
    let (a, b) = (a as i32, b as i32);
    if a < b {
        -1
    } else if a > b {
        1
    } else {
        0
    }
}

/// Where the DBMap lives (tables at run time, a HashMap in the tests).
pub trait Store {
    fn get(&self, k: u32) -> Option<UnitDbNode>;
    fn insert(&mut self, n: UnitDbNode);
    fn update(&mut self, n: UnitDbNode);
    fn delete(&mut self, k: u32);
    fn root(&self, b: u32) -> u32;
    fn set_root(&mut self, b: u32, k: u32);
    fn free_count(&self) -> u32;
    fn free_get(&self, pos: u32) -> Option<(u32, u32)>;
    fn free_set(&mut self, pos: u32, node: u32, bucket: u32);
    fn free_del(&mut self, pos: u32);
    fn lock_count(&self) -> u32;
    fn set_lock_count(&mut self, n: u32);
}

/// The db.cpp algorithms over a store.
pub struct Db<S: Store> {
    pub s: S,
}

impl<S: Store> Db<S> {
    fn node(&self, k: u32) -> UnitDbNode {
        self.s.get(k).expect("skillunit_db: dangling node")
    }
    fn parent(&self, k: u32) -> u32 {
        self.node(k).parent
    }
    fn left(&self, k: u32) -> u32 {
        self.node(k).left
    }
    fn right(&self, k: u32) -> u32 {
        self.node(k).right
    }
    fn red(&self, k: u32) -> bool {
        k != 0 && self.node(k).red
    }
    fn set_parent(&mut self, k: u32, p: u32) {
        let mut n = self.node(k);
        n.parent = p;
        self.s.update(n);
    }
    fn set_left(&mut self, k: u32, l: u32) {
        let mut n = self.node(k);
        n.left = l;
        self.s.update(n);
    }
    fn set_right(&mut self, k: u32, r: u32) {
        let mut n = self.node(k);
        n.right = r;
        self.s.update(n);
    }
    fn set_red(&mut self, k: u32, red: bool) {
        let mut n = self.node(k);
        n.red = red;
        self.s.update(n);
    }

    /// db_rotate_left.
    fn rotate_left(&mut self, node: u32, b: u32) {
        let y = self.right(node);
        let yl = self.left(y);
        self.set_right(node, yl);
        if yl != 0 {
            self.set_parent(yl, node);
        }
        let np = self.parent(node);
        self.set_parent(y, np);
        if node == self.s.root(b) {
            self.s.set_root(b, y);
        } else if self.left(np) == node {
            self.set_left(np, y);
        } else {
            self.set_right(np, y);
        }
        self.set_left(y, node);
        self.set_parent(node, y);
    }

    /// db_rotate_right.
    fn rotate_right(&mut self, node: u32, b: u32) {
        let y = self.left(node);
        let yr = self.right(y);
        self.set_left(node, yr);
        if yr != 0 {
            self.set_parent(yr, node);
        }
        let np = self.parent(node);
        self.set_parent(y, np);
        if node == self.s.root(b) {
            self.s.set_root(b, y);
        } else if self.right(np) == node {
            self.set_right(np, y);
        } else {
            self.set_left(np, y);
        }
        self.set_right(y, node);
        self.set_parent(node, y);
    }

    /// db_rebalance.
    fn rebalance(&mut self, mut node: u32, b: u32) {
        self.set_red(node, true);
        while node != self.s.root(b) && self.red(self.parent(node)) {
            let p = self.parent(node);
            let g = self.parent(p);
            if p == self.left(g) {
                let y = self.right(g);
                if y != 0 && self.red(y) {
                    // case 1
                    self.set_red(p, false);
                    self.set_red(y, false);
                    self.set_red(g, true);
                    node = g;
                } else {
                    if node == self.right(p) {
                        // case 2
                        node = p;
                        self.rotate_left(node, b);
                    }
                    // case 3
                    let p = self.parent(node);
                    let g = self.parent(p);
                    self.set_red(p, false);
                    self.set_red(g, true);
                    self.rotate_right(g, b);
                }
            } else {
                let y = self.left(g);
                if y != 0 && self.red(y) {
                    self.set_red(p, false);
                    self.set_red(y, false);
                    self.set_red(g, true);
                    node = g;
                } else {
                    if node == self.left(p) {
                        node = p;
                        self.rotate_right(node, b);
                    }
                    let p = self.parent(node);
                    let g = self.parent(p);
                    self.set_red(p, false);
                    self.set_red(g, true);
                    self.rotate_left(g, b);
                }
            }
        }
        let r = self.s.root(b);
        self.set_red(r, false); // the root can and should always be black
    }

    /// db_rebalance_erase.
    fn rebalance_erase(&mut self, node: u32, b: u32) {
        let mut y = node;
        let mut x: u32;
        let mut x_parent: u32;
        // Select where to change the tree
        if self.left(y) == 0 {
            x = self.right(y);
        } else if self.right(y) == 0 {
            x = self.left(y);
        } else {
            // both exist, go to the leftmost node of the right sub-tree
            y = self.right(y);
            while self.left(y) != 0 {
                y = self.left(y);
            }
            x = self.right(y);
        }
        // Remove the node from the tree
        if y != node {
            // both child existed
            let nl = self.left(node);
            self.set_parent(nl, y);
            self.set_left(y, nl);
            if y != self.right(node) {
                // 'y' is not the direct child of 'node'
                x_parent = self.parent(y);
                if x != 0 {
                    let yp = self.parent(y);
                    self.set_parent(x, yp);
                }
                let yp = self.parent(y);
                self.set_left(yp, x);
                let nr = self.right(node);
                self.set_right(y, nr);
                self.set_parent(nr, y);
            } else {
                x_parent = y;
            }
            // link 'y' and the parent of 'node'
            if self.s.root(b) == node {
                self.s.set_root(b, y);
            } else {
                let np = self.parent(node);
                if self.left(np) == node {
                    self.set_left(np, y);
                } else {
                    self.set_right(np, y);
                }
            }
            let np = self.parent(node);
            self.set_parent(y, np);
            // switch colors
            let (yc, nc) = (self.red(y), self.red(node));
            self.set_red(y, nc);
            self.set_red(node, yc);
            y = node;
        } else {
            // one child did not exist
            x_parent = self.parent(y);
            if x != 0 {
                let yp = self.parent(y);
                self.set_parent(x, yp);
            }
            if self.s.root(b) == node {
                self.s.set_root(b, x);
            } else {
                let np = self.parent(node);
                if self.left(np) == node {
                    self.set_left(np, x);
                } else {
                    self.set_right(np, x);
                }
            }
        }
        // Restore the RED-BLACK properties
        if !self.red(y) {
            while x != self.s.root(b) && !self.red(x) {
                let xp = x_parent;
                if x == self.left(xp) {
                    let mut w = self.right(xp);
                    if self.red(w) {
                        self.set_red(w, false);
                        self.set_red(xp, true);
                        self.rotate_left(xp, b);
                        w = self.right(xp);
                    }
                    if !self.red(self.left(w)) && !self.red(self.right(w)) {
                        self.set_red(w, true);
                        x = xp;
                        x_parent = self.parent(xp);
                    } else {
                        if !self.red(self.right(w)) {
                            let wl = self.left(w);
                            if wl != 0 {
                                self.set_red(wl, false);
                            }
                            self.set_red(w, true);
                            self.rotate_right(w, b);
                            w = self.right(xp);
                        }
                        let xpc = self.red(xp);
                        self.set_red(w, xpc);
                        self.set_red(xp, false);
                        let wr = self.right(w);
                        if wr != 0 {
                            self.set_red(wr, false);
                        }
                        self.rotate_left(xp, b);
                        break;
                    }
                } else {
                    let mut w = self.left(xp);
                    if self.red(w) {
                        self.set_red(w, false);
                        self.set_red(xp, true);
                        self.rotate_right(xp, b);
                        w = self.left(xp);
                    }
                    if !self.red(self.right(w)) && !self.red(self.left(w)) {
                        self.set_red(w, true);
                        x = xp;
                        x_parent = self.parent(xp);
                    } else {
                        if !self.red(self.left(w)) {
                            let wr = self.right(w);
                            if wr != 0 {
                                self.set_red(wr, false);
                            }
                            self.set_red(w, true);
                            self.rotate_left(w, b);
                            w = self.left(xp);
                        }
                        let xpc = self.red(xp);
                        self.set_red(w, xpc);
                        self.set_red(xp, false);
                        let wl = self.left(w);
                        if wl != 0 {
                            self.set_red(wl, false);
                        }
                        self.rotate_right(xp, b);
                        break;
                    }
                }
            }
            if x != 0 {
                self.set_red(x, false);
            }
        }
    }

    /// db_free_lock.
    pub fn lock(&mut self) {
        let n = self.s.lock_count();
        self.s.set_lock_count(n + 1);
    }

    /// db_free_unlock: the last lock unlinks the nodes removed meanwhile, in free-list order.
    pub fn unlock(&mut self) {
        let n = self.s.lock_count();
        if n == 0 {
            log::warn!("db_free_unlock: free_lock was already 0 (skillunit_db)");
        } else {
            self.s.set_lock_count(n - 1);
        }
        if self.s.lock_count() > 0 {
            return; // Not last lock
        }
        let count = self.s.free_count();
        for pos in 0..count {
            let Some((node, b)) = self.s.free_get(pos) else { continue };
            self.rebalance_erase(node, b);
            self.s.delete(node);
            self.s.free_del(pos);
        }
    }

    /// db_free_add: the node is marked deleted and appended to the free list.
    fn free_add(&mut self, node: u32, b: u32) {
        let pos = self.s.free_count();
        let mut n = self.node(node);
        n.deleted = true;
        self.s.update(n);
        self.s.free_set(pos, node, b);
    }

    /// db_free_remove: the node leaves the free list (the last entry takes its place) and is live again.
    fn free_remove(&mut self, node: u32) {
        let count = self.s.free_count();
        let found = (0..count).find(|&p| self.s.free_get(p).map(|(n, _)| n == node).unwrap_or(false));
        let mut n = self.node(node);
        n.deleted = false;
        self.s.update(n);
        match found {
            Some(i) => {
                let last = count - 1;
                if i < last {
                    let (ln, lb) = self.s.free_get(last).expect("free list entry");
                    self.s.free_set(i, ln, lb);
                }
                self.s.free_del(last);
            }
            None => log::warn!("db_free_remove: node was not found (skillunit_db)"),
        }
    }

    /// db_obj_put for `key` (returns 1 when the entry already existed).
    pub fn put(&mut self, key: u32) -> i32 {
        self.lock();
        let b = bucket(key);
        let mut node = self.s.root(b);
        let mut parent = 0;
        let mut c = 0;
        let mut retval = 0;
        let mut found = false;
        while node != 0 {
            c = cmp(key, node);
            if c == 0 {
                // equal entry, replace
                if self.node(node).deleted {
                    self.free_remove(node);
                } else {
                    retval = 1;
                }
                found = true;
                break;
            }
            parent = node;
            node = if c < 0 { self.left(node) } else { self.right(node) };
        }
        // allocate a new node if necessary
        if !found {
            if c == 0 {
                // hash entry is empty
                self.s.insert(UnitDbNode { key, parent: 0, left: 0, right: 0, red: false, deleted: false });
                self.s.set_root(b, key);
            } else {
                self.s.insert(UnitDbNode { key, parent, left: 0, right: 0, red: true, deleted: false });
                if c < 0 {
                    self.set_left(parent, key);
                } else {
                    self.set_right(parent, key);
                }
                if self.red(parent) {
                    // two consecutive RED nodes, must rebalance
                    self.rebalance(key, b);
                }
            }
        }
        self.unlock();
        retval
    }

    /// db_obj_remove (returns 1 when the key was there).
    pub fn remove(&mut self, key: u32) -> i32 {
        self.lock();
        let b = bucket(key);
        let mut node = self.s.root(b);
        let mut retval = 0;
        while node != 0 {
            let c = cmp(key, node);
            if c == 0 {
                if !self.node(node).deleted {
                    retval = 1;
                    self.free_add(node, b);
                }
                break;
            }
            node = if c < 0 { self.left(node) } else { self.right(node) };
        }
        self.unlock();
        retval
    }

    /// One step of db_obj_vforeach's walk from `node` (after its function ran): left child, else right child, else
    /// the right sibling of the nearest ancestor that has one; 0 at the end of the bucket.
    pub fn next(&self, node: u32) -> u32 {
        let n = self.node(node);
        if n.left != 0 {
            return n.left;
        }
        if n.right != 0 {
            return n.right;
        }
        let mut node = node;
        while node != 0 {
            let parent = self.parent(node);
            if parent != 0 && self.right(parent) != 0 && self.left(parent) == node {
                return self.right(parent);
            }
            node = parent;
        }
        0
    }

    /// Whether the node is marked deleted.
    pub fn deleted(&self, node: u32) -> bool {
        self.node(node).deleted
    }
}

/// The run-time store: the skillunit_db tables (and the lock count in skill_unit_state).
pub struct TableStore<'a, 'b> {
    pub c: &'a Ctx<'b>,
}

impl Store for TableStore<'_, '_> {
    fn get(&self, k: u32) -> Option<UnitDbNode> {
        self.c.db().skillunit_db_node().key().find(k)
    }
    fn insert(&mut self, n: UnitDbNode) {
        self.c.db().skillunit_db_node().insert(n);
    }
    fn update(&mut self, n: UnitDbNode) {
        self.c.db().skillunit_db_node().key().update(n);
    }
    fn delete(&mut self, k: u32) {
        self.c.db().skillunit_db_node().key().delete(k);
    }
    fn root(&self, b: u32) -> u32 {
        self.c.db().skillunit_db_root().bucket().find(b).map(|r| r.node).unwrap_or(0)
    }
    fn set_root(&mut self, b: u32, k: u32) {
        let t = self.c.db().skillunit_db_root();
        if k == 0 {
            t.bucket().delete(b);
        } else if t.bucket().find(b).is_some() {
            t.bucket().update(UnitDbRoot { bucket: b, node: k });
        } else {
            t.insert(UnitDbRoot { bucket: b, node: k });
        }
    }
    fn free_count(&self) -> u32 {
        self.c.db().skillunit_db_free().iter().count() as u32
    }
    fn free_get(&self, pos: u32) -> Option<(u32, u32)> {
        self.c.db().skillunit_db_free().pos().find(pos).map(|f| (f.node, f.bucket))
    }
    fn free_set(&mut self, pos: u32, node: u32, bucket: u32) {
        let t = self.c.db().skillunit_db_free();
        if t.pos().find(pos).is_some() {
            t.pos().update(UnitDbFree { pos, node, bucket });
        } else {
            t.insert(UnitDbFree { pos, node, bucket });
        }
    }
    fn free_del(&mut self, pos: u32) {
        self.c.db().skillunit_db_free().pos().delete(pos);
    }
    fn lock_count(&self) -> u32 {
        super::data::state(self.c).db_free_lock
    }
    fn set_lock_count(&mut self, n: u32) {
        let mut s = super::data::state(self.c);
        s.db_free_lock = n;
        super::data::put_state(self.c, &s);
    }
}

fn db<'a, 'b>(c: &'a Ctx<'b>) -> Db<TableStore<'a, 'b>> {
    Db { s: TableStore { c } }
}

/// idb_put(skillunit_db, key, unit).
pub fn put(c: &Ctx, key: u32) {
    db(c).put(key);
}

/// idb_remove(skillunit_db, key): true when the key was there.
pub fn remove(c: &Ctx, key: u32) -> bool {
    db(c).remove(key) == 1
}

/// skillunit_db->foreach(skillunit_db, func, …) (db_obj_vforeach): `f` runs for every live node, bucket by bucket,
/// each tree in pre-order; the walk continues from the node it is on after `f` returns, over the tree as `f` left it
/// (a node removed meanwhile stays linked, marked deleted, until the lock goes).
pub fn foreach(c: &Ctx, mut f: impl FnMut(u32)) {
    db(c).lock();
    for i in 0..HASH_SIZE {
        let mut node = db(c).s.root(i);
        while node != 0 {
            if !db(c).deleted(node) {
                f(node);
            }
            node = db(c).next(node);
        }
    }
    db(c).unlock();
}

/// The number of live entries (db_size).
pub fn len(c: &Ctx) -> usize {
    c.db().skillunit_db_node().iter().filter(|n| !n.deleted).count()
}

/// Empties the map (a world rebuild: rAthena starts with a fresh skillunit_db).
pub fn clear(c: &Ctx) {
    for k in c.db().skillunit_db_node().iter().map(|n| n.key).collect::<Vec<_>>() {
        c.db().skillunit_db_node().key().delete(k);
    }
    for b in c.db().skillunit_db_root().iter().map(|r| r.bucket).collect::<Vec<_>>() {
        c.db().skillunit_db_root().bucket().delete(b);
    }
    for p in c.db().skillunit_db_free().iter().map(|r| r.pos).collect::<Vec<_>>() {
        c.db().skillunit_db_free().pos().delete(p);
    }
}

/// An in-memory store (unit tests).
#[cfg(test)]
#[derive(Default)]
pub struct MemStore {
    pub nodes: std::collections::HashMap<u32, UnitDbNode>,
    pub roots: std::collections::HashMap<u32, u32>,
    pub free: std::collections::BTreeMap<u32, (u32, u32)>,
    pub lock: u32,
}

#[cfg(test)]
impl Store for MemStore {
    fn get(&self, k: u32) -> Option<UnitDbNode> {
        self.nodes.get(&k).cloned()
    }
    fn insert(&mut self, n: UnitDbNode) {
        self.nodes.insert(n.key, n);
    }
    fn update(&mut self, n: UnitDbNode) {
        self.nodes.insert(n.key, n);
    }
    fn delete(&mut self, k: u32) {
        self.nodes.remove(&k);
    }
    fn root(&self, b: u32) -> u32 {
        self.roots.get(&b).copied().unwrap_or(0)
    }
    fn set_root(&mut self, b: u32, k: u32) {
        if k == 0 {
            self.roots.remove(&b);
        } else {
            self.roots.insert(b, k);
        }
    }
    fn free_count(&self) -> u32 {
        self.free.len() as u32
    }
    fn free_get(&self, pos: u32) -> Option<(u32, u32)> {
        self.free.get(&pos).copied()
    }
    fn free_set(&mut self, pos: u32, node: u32, bucket: u32) {
        self.free.insert(pos, (node, bucket));
    }
    fn free_del(&mut self, pos: u32) {
        self.free.remove(&pos);
    }
    fn lock_count(&self) -> u32 {
        self.lock
    }
    fn set_lock_count(&mut self, n: u32) {
        self.lock = n;
    }
}