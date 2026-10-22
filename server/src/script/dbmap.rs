//! common/db.cpp's DBMap for string keys, reduced to what decides iteration order: a 283-bucket hash table whose
//! buckets are red-black trees (db_obj_put + db_rebalance), walked bucket by bucket in pre-order (db_obj_vforeach /
//! the DBIterator). rAthena iterates `ev_db` (NPC events), `scriptlabel_db` (a script's labels) and friends this
//! way, so the order OnInit runs in, the order of an NPC's label list and the order of `::OnEvent` broadcasts all
//! follow it. Removal follows db_obj_remove / db_free_lock: a removed node is only marked deleted while the map is
//! locked (a foreach is running), and unlinked with db_rebalance_erase when the last lock goes.

/// db.cpp HASH_SIZE.
pub const HASH_SIZE: usize = 256 + 27;

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
enum Color {
    Red,
    Black,
}

#[derive(Clone, Debug)]
struct Node<V> {
    key: Vec<u8>,
    data: V,
    parent: Option<usize>,
    left: Option<usize>,
    right: Option<usize>,
    color: Color,
    deleted: bool,
    /// Unlinked from its tree (db_rebalance_erase ran).
    gone: bool,
}

/// A DB_STRING (`case_insensitive` false) or DB_ISTRING map with rAthena's `maxlen`.
#[derive(Clone, Debug)]
pub struct StrDb<V> {
    nodes: Vec<Node<V>>,
    ht: Vec<Option<usize>>,
    maxlen: usize,
    case_insensitive: bool,
    free_lock: u32,
    /// db->free_list: (node, bucket).
    free_list: Vec<(usize, usize)>,
}

impl<V: Clone> StrDb<V> {
    /// strdb_alloc / stridb_alloc: `maxlen` 0 means unlimited (db_alloc sets UINT16_MAX).
    pub fn new(maxlen: u16, case_insensitive: bool) -> Self {
        let maxlen = if maxlen == 0 { u16::MAX as usize } else { maxlen as usize };
        StrDb { nodes: Vec::new(), ht: vec![None; HASH_SIZE], maxlen, case_insensitive, free_lock: 0, free_list: Vec::new() }
    }

    /// db_string_hash / db_istring_hash (maxlen + 1 characters take part: the loop breaks after `i == maxlen`).
    fn hash(&self, key: &[u8]) -> usize {
        let mut hash: u32 = 0;
        for (i, &b) in key.iter().enumerate() {
            if b == 0 {
                break;
            }
            let c = if self.case_insensitive { b.to_ascii_lowercase() } else { b };
            hash = (hash.wrapping_mul(33).wrapping_add(c as u32)) ^ (hash >> 24);
            if i == self.maxlen {
                break;
            }
        }
        hash as usize % HASH_SIZE
    }

    /// db_string_cmp (strncmp) / db_istring_cmp (strncasecmp) over `maxlen` bytes.
    fn cmp(&self, a: &[u8], b: &[u8]) -> i32 {
        for i in 0..self.maxlen {
            let x = a.get(i).copied().unwrap_or(0);
            let y = b.get(i).copied().unwrap_or(0);
            let (x, y) = if self.case_insensitive { (x.to_ascii_lowercase(), y.to_ascii_lowercase()) } else { (x, y) };
            if x != y {
                return x as i32 - y as i32;
            }
            if x == 0 {
                return 0;
            }
        }
        0
    }

    /// db_obj_get.
    pub fn get(&self, key: &[u8]) -> Option<&V> {
        let mut node = self.ht[self.hash(key)];
        while let Some(n) = node {
            let c = self.cmp(key, &self.nodes[n].key);
            if c == 0 {
                return if self.nodes[n].deleted { None } else { Some(&self.nodes[n].data) };
            }
            node = if c < 0 { self.nodes[n].left } else { self.nodes[n].right };
        }
        None
    }

    /// db_obj_put: returns true when the key already existed (its data is replaced in place).
    pub fn put(&mut self, key: &[u8], data: V) -> bool {
        let hash = self.hash(key);
        let mut node = self.ht[hash];
        let mut parent = None;
        let mut c = 0;
        while let Some(n) = node {
            c = self.cmp(key, &self.nodes[n].key);
            if c == 0 {
                // (a node still marked deleted is revived: db_free_remove)
                let existed = !self.nodes[n].deleted;
                if self.nodes[n].deleted {
                    self.nodes[n].deleted = false;
                    if let Some(i) = self.free_list.iter().position(|&(x, _)| x == n) {
                        let last = self.free_list.len() - 1;
                        self.free_list.swap(i, last);
                        self.free_list.pop();
                    }
                }
                self.nodes[n].key = key.to_vec();
                self.nodes[n].data = data;
                return existed;
            }
            parent = Some(n);
            node = if c < 0 { self.nodes[n].left } else { self.nodes[n].right };
        }
        let id = self.nodes.len();
        if c == 0 {
            // hash entry is empty
            self.nodes.push(Node { key: key.to_vec(), data, parent: None, left: None, right: None, color: Color::Black, deleted: false, gone: false });
            self.ht[hash] = Some(id);
        } else {
            let p = parent.expect("non-empty bucket has a parent");
            self.nodes.push(Node { key: key.to_vec(), data, parent: Some(p), left: None, right: None, color: Color::Red, deleted: false, gone: false });
            if c < 0 {
                self.nodes[p].left = Some(id);
            } else {
                self.nodes[p].right = Some(id);
            }
            if self.nodes[p].color == Color::Red {
                self.rebalance(id, hash);
            }
        }
        false
    }

    fn rotate_left(&mut self, node: usize, root: usize) {
        let y = self.nodes[node].right.expect("rotate_left needs a right child");
        self.nodes[node].right = self.nodes[y].left;
        if let Some(yl) = self.nodes[y].left {
            self.nodes[yl].parent = Some(node);
        }
        self.nodes[y].parent = self.nodes[node].parent;
        if Some(node) == self.ht[root] {
            self.ht[root] = Some(y);
        } else {
            let p = self.nodes[node].parent.unwrap();
            if self.nodes[p].left == Some(node) {
                self.nodes[p].left = Some(y);
            } else {
                self.nodes[p].right = Some(y);
            }
        }
        self.nodes[y].left = Some(node);
        self.nodes[node].parent = Some(y);
    }

    fn rotate_right(&mut self, node: usize, root: usize) {
        let y = self.nodes[node].left.expect("rotate_right needs a left child");
        self.nodes[node].left = self.nodes[y].right;
        if let Some(yr) = self.nodes[y].right {
            self.nodes[yr].parent = Some(node);
        }
        self.nodes[y].parent = self.nodes[node].parent;
        if Some(node) == self.ht[root] {
            self.ht[root] = Some(y);
        } else {
            let p = self.nodes[node].parent.unwrap();
            if self.nodes[p].right == Some(node) {
                self.nodes[p].right = Some(y);
            } else {
                self.nodes[p].left = Some(y);
            }
        }
        self.nodes[y].right = Some(node);
        self.nodes[node].parent = Some(y);
    }

    /// db.cpp db_rebalance.
    fn rebalance(&mut self, mut node: usize, root: usize) {
        self.nodes[node].color = Color::Red;
        loop {
            if Some(node) == self.ht[root] {
                break;
            }
            let p = self.nodes[node].parent.unwrap();
            if self.nodes[p].color != Color::Red {
                break;
            }
            let g = self.nodes[p].parent.unwrap();
            if self.nodes[g].left == Some(p) {
                let y = self.nodes[g].right;
                if let Some(y) = y.filter(|&y| self.nodes[y].color == Color::Red) {
                    self.nodes[p].color = Color::Black;
                    self.nodes[y].color = Color::Black;
                    self.nodes[g].color = Color::Red;
                    node = g;
                } else {
                    if self.nodes[p].right == Some(node) {
                        node = p;
                        self.rotate_left(node, root);
                    }
                    let p = self.nodes[node].parent.unwrap();
                    let g = self.nodes[p].parent.unwrap();
                    self.nodes[p].color = Color::Black;
                    self.nodes[g].color = Color::Red;
                    self.rotate_right(g, root);
                }
            } else {
                let y = self.nodes[g].left;
                if let Some(y) = y.filter(|&y| self.nodes[y].color == Color::Red) {
                    self.nodes[p].color = Color::Black;
                    self.nodes[y].color = Color::Black;
                    self.nodes[g].color = Color::Red;
                    node = g;
                } else {
                    if self.nodes[p].left == Some(node) {
                        node = p;
                        self.rotate_right(node, root);
                    }
                    let p = self.nodes[node].parent.unwrap();
                    let g = self.nodes[p].parent.unwrap();
                    self.nodes[p].color = Color::Black;
                    self.nodes[g].color = Color::Red;
                    self.rotate_left(g, root);
                }
            }
        }
        let r = self.ht[root].unwrap();
        self.nodes[r].color = Color::Black;
    }

    /// db_free_lock (a foreach / iterator is running).
    pub fn lock(&mut self) {
        self.free_lock += 1;
    }

    /// db_free_unlock: the last lock unlinks the nodes removed meanwhile, in removal order.
    pub fn unlock(&mut self) {
        self.free_lock = self.free_lock.saturating_sub(1);
        if self.free_lock > 0 {
            return;
        }
        for (n, b) in std::mem::take(&mut self.free_list) {
            self.rebalance_erase(n, b);
            self.nodes[n].gone = true;
        }
    }

    /// db_obj_remove: true when the key was there.
    pub fn remove(&mut self, key: &[u8]) -> bool {
        self.lock();
        let hash = self.hash(key);
        let mut node = self.ht[hash];
        let mut found = false;
        while let Some(n) = node {
            let c = self.cmp(key, &self.nodes[n].key);
            if c == 0 {
                if !self.nodes[n].deleted {
                    self.nodes[n].deleted = true;
                    self.free_list.push((n, hash));
                    found = true;
                }
                break;
            }
            node = if c < 0 { self.nodes[n].left } else { self.nodes[n].right };
        }
        self.unlock();
        found
    }

    fn color_of(&self, n: Option<usize>) -> Color {
        n.map(|n| self.nodes[n].color).unwrap_or(Color::Black)
    }

    /// db.cpp db_rebalance_erase.
    fn rebalance_erase(&mut self, node: usize, root: usize) {
        let mut y = node;
        let mut x: Option<usize>;
        let mut x_parent: Option<usize>;
        if self.nodes[y].left.is_none() {
            x = self.nodes[y].right;
        } else if self.nodes[y].right.is_none() {
            x = self.nodes[y].left;
        } else {
            y = self.nodes[y].right.unwrap();
            while let Some(l) = self.nodes[y].left {
                y = l;
            }
            x = self.nodes[y].right;
        }
        if y != node {
            // both children existed
            let nl = self.nodes[node].left.unwrap();
            self.nodes[nl].parent = Some(y);
            self.nodes[y].left = Some(nl);
            if Some(y) != self.nodes[node].right {
                x_parent = self.nodes[y].parent;
                if let Some(xx) = x {
                    self.nodes[xx].parent = self.nodes[y].parent;
                }
                let yp = self.nodes[y].parent.unwrap();
                self.nodes[yp].left = x;
                let nr = self.nodes[node].right.unwrap();
                self.nodes[y].right = Some(nr);
                self.nodes[nr].parent = Some(y);
            } else {
                x_parent = Some(y);
            }
            if self.ht[root] == Some(node) {
                self.ht[root] = Some(y);
            } else {
                let np = self.nodes[node].parent.unwrap();
                if self.nodes[np].left == Some(node) {
                    self.nodes[np].left = Some(y);
                } else {
                    self.nodes[np].right = Some(y);
                }
            }
            self.nodes[y].parent = self.nodes[node].parent;
            let tmp = self.nodes[y].color;
            self.nodes[y].color = self.nodes[node].color;
            self.nodes[node].color = tmp;
            y = node;
        } else {
            x_parent = self.nodes[y].parent;
            if let Some(xx) = x {
                self.nodes[xx].parent = self.nodes[y].parent;
            }
            if self.ht[root] == Some(node) {
                self.ht[root] = x;
            } else {
                let np = self.nodes[node].parent.unwrap();
                if self.nodes[np].left == Some(node) {
                    self.nodes[np].left = x;
                } else {
                    self.nodes[np].right = x;
                }
            }
        }
        if self.nodes[y].color != Color::Red {
            while x != self.ht[root] && self.color_of(x) == Color::Black {
                let xp = x_parent.unwrap();
                if x == self.nodes[xp].left {
                    let mut w = self.nodes[xp].right.unwrap();
                    if self.nodes[w].color == Color::Red {
                        self.nodes[w].color = Color::Black;
                        self.nodes[xp].color = Color::Red;
                        self.rotate_left(xp, root);
                        w = self.nodes[xp].right.unwrap();
                    }
                    if self.color_of(self.nodes[w].left) == Color::Black && self.color_of(self.nodes[w].right) == Color::Black {
                        self.nodes[w].color = Color::Red;
                        x = Some(xp);
                        x_parent = self.nodes[xp].parent;
                    } else {
                        if self.color_of(self.nodes[w].right) == Color::Black {
                            if let Some(wl) = self.nodes[w].left {
                                self.nodes[wl].color = Color::Black;
                            }
                            self.nodes[w].color = Color::Red;
                            self.rotate_right(w, root);
                            w = self.nodes[xp].right.unwrap();
                        }
                        self.nodes[w].color = self.nodes[xp].color;
                        self.nodes[xp].color = Color::Black;
                        if let Some(wr) = self.nodes[w].right {
                            self.nodes[wr].color = Color::Black;
                        }
                        self.rotate_left(xp, root);
                        break;
                    }
                } else {
                    let mut w = self.nodes[xp].left.unwrap();
                    if self.nodes[w].color == Color::Red {
                        self.nodes[w].color = Color::Black;
                        self.nodes[xp].color = Color::Red;
                        self.rotate_right(xp, root);
                        w = self.nodes[xp].left.unwrap();
                    }
                    if self.color_of(self.nodes[w].right) == Color::Black && self.color_of(self.nodes[w].left) == Color::Black {
                        self.nodes[w].color = Color::Red;
                        x = Some(xp);
                        x_parent = self.nodes[xp].parent;
                    } else {
                        if self.color_of(self.nodes[w].left) == Color::Black {
                            if let Some(wr) = self.nodes[w].right {
                                self.nodes[wr].color = Color::Black;
                            }
                            self.nodes[w].color = Color::Red;
                            self.rotate_left(w, root);
                            w = self.nodes[xp].left.unwrap();
                        }
                        self.nodes[w].color = self.nodes[xp].color;
                        self.nodes[xp].color = Color::Black;
                        if let Some(wl) = self.nodes[w].left {
                            self.nodes[wl].color = Color::Black;
                        }
                        self.rotate_right(xp, root);
                        break;
                    }
                }
            }
            if let Some(xx) = x {
                self.nodes[xx].color = Color::Black;
            }
        }
    }

    /// DBIterator order (dbit_obj_next): bucket 0..HASH_SIZE, each tree in order (left, node, right) — i.e. by key.
    pub fn iter_inorder(&self) -> Vec<(&[u8], &V)> {
        let mut out = Vec::with_capacity(self.nodes.len());
        for b in 0..HASH_SIZE {
            let mut stack: Vec<usize> = Vec::new();
            let mut cur = self.ht[b];
            while cur.is_some() || !stack.is_empty() {
                while let Some(n) = cur {
                    stack.push(n);
                    cur = self.nodes[n].left;
                }
                let n = stack.pop().unwrap();
                if !self.nodes[n].deleted {
                    out.push((&self.nodes[n].key[..], &self.nodes[n].data));
                }
                cur = self.nodes[n].right;
            }
        }
        out
    }

    /// db_obj_vforeach order: bucket 0..HASH_SIZE, each tree in pre-order (node, left, right); deleted nodes that
    /// are still linked (a lock is held) are skipped.
    pub fn iter_order(&self) -> Vec<(&[u8], &V)> {
        let mut out = Vec::with_capacity(self.nodes.len());
        for b in 0..HASH_SIZE {
            let mut node = self.ht[b];
            while let Some(n) = node {
                if !self.nodes[n].deleted {
                    out.push((&self.nodes[n].key[..], &self.nodes[n].data));
                }
                if let Some(l) = self.nodes[n].left {
                    node = Some(l);
                    continue;
                }
                if let Some(r) = self.nodes[n].right {
                    node = Some(r);
                    continue;
                }
                // climb until a right sibling not yet visited
                let mut cur = Some(n);
                node = None;
                while let Some(c) = cur {
                    let parent = self.nodes[c].parent;
                    if let Some(p) = parent {
                        if self.nodes[p].right.is_some() && self.nodes[p].left == Some(c) {
                            node = self.nodes[p].right;
                            break;
                        }
                    }
                    cur = parent;
                }
            }
        }
        out
    }

    /// The hash bucket of a key (db_string_hash % HASH_SIZE): iteration visits the buckets in order.
    pub fn bucket(&self, key: &[u8]) -> usize {
        self.hash(key)
    }

    pub fn len(&self) -> usize {
        self.nodes.iter().filter(|n| !n.deleted).count()
    }

    pub fn is_empty(&self) -> bool {
        self.len() == 0
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn iterates_every_key_once_and_replaces() {
        let mut db: StrDb<i32> = StrDb::new(50, false);
        for i in 0..2000 {
            assert!(!db.put(format!("key{i}").as_bytes(), i));
        }
        assert!(db.put(b"key7", -7));
        let all = db.iter_order();
        assert_eq!(all.len(), 2000);
        let mut seen: Vec<i32> = all.iter().map(|(_, v)| **v).collect();
        seen.sort();
        assert_eq!(seen[0], -7);
        assert_eq!(seen[1..], (0..2000).filter(|&v| v != 7).collect::<Vec<_>>()[..]);
        assert_eq!(db.get(b"key7"), Some(&-7));
        assert_eq!(db.get(b"nope"), None);
    }

    /// Checks the red-black invariants and parent links of every bucket; returns the node count.
    fn check(db: &StrDb<i32>) -> usize {
        fn walk(db: &StrDb<i32>, n: Option<usize>, parent: Option<usize>) -> (usize, usize) {
            let Some(n) = n else { return (1, 0) };
            let node = &db.nodes[n];
            assert_eq!(node.parent, parent);
            assert!(!node.gone);
            if node.color == Color::Red {
                assert_eq!(db.color_of(node.left), Color::Black);
                assert_eq!(db.color_of(node.right), Color::Black);
            }
            if let Some(l) = node.left {
                assert!(db.cmp(&db.nodes[l].key, &node.key) < 0);
            }
            if let Some(r) = node.right {
                assert!(db.cmp(&db.nodes[r].key, &node.key) > 0);
            }
            let (bl, cl) = walk(db, node.left, Some(n));
            let (br, cr) = walk(db, node.right, Some(n));
            assert_eq!(bl, br);
            (bl + (node.color == Color::Black) as usize, cl + cr + 1)
        }
        let mut total = 0;
        for b in 0..HASH_SIZE {
            if let Some(r) = db.ht[b] {
                assert_eq!(db.nodes[r].color, Color::Black);
            }
            total += walk(db, db.ht[b], None).1;
        }
        total
    }

    #[test]
    fn removal_keeps_the_tree_valid_and_defers_under_lock() {
        let mut db: StrDb<i32> = StrDb::new(50, false);
        for i in 0..3000 {
            db.put(format!("npc{i}::OnInit").as_bytes(), i);
        }
        assert_eq!(check(&db), 3000);
        db.lock();
        for i in (0..3000).step_by(3) {
            assert!(db.remove(format!("npc{i}::OnInit").as_bytes()));
        }
        // still linked while locked, but skipped
        assert_eq!(check(&db), 3000);
        assert_eq!(db.iter_order().len(), 2000);
        assert_eq!(db.get(b"npc3::OnInit"), None);
        db.unlock();
        assert_eq!(check(&db), 2000);
        assert_eq!(db.iter_order().len(), 2000);
        assert_eq!(db.iter_inorder().len(), 2000);
        for i in 0..3000 {
            if i % 3 != 0 {
                assert!(db.remove(format!("npc{i}::OnInit").as_bytes()));
                if i % 97 == 1 {
                    check(&db);
                }
            }
        }
        assert_eq!(check(&db), 0);
    }
}
