//! The iteration order of rAthena's `std::unordered_map<uint16, int32>` skill maps (pc.hpp `sd->scd`, the skill
//! cooldowns). rAthena iterates sd->scd in skill_blockpc_clear (one ZC_SKILL_POSTDELAY per entry), in
//! chrif_skillcooldown_save (the order the char server stores the rows, so the order they start again at the next
//! login) and in @resetcooltime (one message per entry). The oracle is built with GCC 13's libstdc++, whose
//! unordered_map is a hash table over one singly linked list of all nodes; the order depends on the bucket count and
//! on the history of inserts, erases and rehashes. This reproduces it:
//! - std::hash<uint16> is the identity; the bucket is `key % bucket_count`;
//! - a new key goes first in its bucket's group of nodes, or at the front of the list when its bucket is empty
//!   (`_Hashtable::_M_insert_bucket_begin`);
//! - before an insert, `_Prime_rehash_policy::_M_need_rehash` may grow the bucket count (the first insert: 13
//!   buckets, from the policy's initial hint of 11; then 29, 59 … when the element count passes it, max_load_factor
//!   1.0); `_M_rehash_aux` walks the old list in order and puts each node first in its new bucket's group, or at the
//!   front when that bucket is empty;
//! - erase unlinks the node; clear() drops every node and keeps the bucket count and the policy's state.
//! The unit test replays sequences whose orders were printed by a program built with the oracle's g++ 13.3.0
//! (Ubuntu 13.3.0-6ubuntu2~24.04.1, the compiler of its map-server) on Ubuntu 24.04.

/// libstdc++ `__prime_list` from index 6 (where `_M_next_bkt`'s lower_bound starts), far beyond what
/// MAX_SKILLCOOLDOWN (20) entries can reach.
const PRIMES: &[u64] = &[17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71, 73, 79, 83, 89, 97, 103, 109, 113, 127];

/// The hash table's order state: the keys in list order, the bucket count (1 for a new map) and the rehash
/// policy's `_M_next_resize` (0 for a new map).
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Umap {
    pub order: Vec<u16>,
    pub buckets: u64,
    pub next_resize: u64,
}

/// `_Prime_rehash_policy::_M_next_bkt(n)`: the bucket count and the new `_M_next_resize`.
fn next_bkt(n: u64) -> (u64, u64) {
    const FAST_BKT: [u64; 14] = [2, 2, 2, 3, 5, 5, 7, 7, 11, 11, 11, 11, 13, 13];
    if n < FAST_BKT.len() as u64 {
        if n == 0 {
            return (1, 0);
        }
        let b = FAST_BKT[n as usize];
        return (b, b);
    }
    let b = PRIMES.iter().copied().find(|&p| p >= n).unwrap_or(127);
    (b, b)
}

fn bucket(key: u16, buckets: u64) -> u64 {
    key as u64 % buckets.max(1)
}

/// `_M_insert_bucket_begin`: `key` goes before the first node of its bucket, or at the front of the list.
fn insert_bucket_begin(order: &mut Vec<u16>, key: u16, buckets: u64) {
    let b = bucket(key, buckets);
    match order.iter().position(|&k| bucket(k, buckets) == b) {
        Some(i) => order.insert(i, key),
        None => order.insert(0, key),
    }
}

impl Umap {
    /// `map[key]` (operator[]): a key already there keeps its place; a new one is inserted after the rehash check.
    pub fn insert(&mut self, key: u16) {
        if self.order.contains(&key) {
            return;
        }
        let n_elt = self.order.len() as u64;
        let n_bkt = self.buckets.max(1);
        // _M_need_rehash(n_bkt, n_elt, 1)
        if n_elt + 1 > self.next_resize {
            let min_bkts = (n_elt + 1).max(if self.next_resize != 0 { 0 } else { 11 }) as f64;
            if min_bkts >= n_bkt as f64 {
                let (nb, next_resize) = next_bkt((min_bkts.floor() as u64 + 1).max(n_bkt * 2));
                // _M_rehash_aux: the old list in order, each node first in its new bucket's group
                let old = std::mem::take(&mut self.order);
                for k in old {
                    insert_bucket_begin(&mut self.order, k, nb);
                }
                self.buckets = nb;
                self.next_resize = next_resize;
            } else {
                self.next_resize = n_bkt;
            }
        }
        let nb = self.buckets.max(1);
        insert_bucket_begin(&mut self.order, key, nb);
    }

    /// `map.erase(key)`.
    pub fn erase(&mut self, key: u16) {
        self.order.retain(|&k| k != key);
    }

    /// `map.clear()`: the bucket count and the policy stay.
    pub fn clear(&mut self) {
        self.order.clear();
    }
}

#[cfg(test)]
mod tests {
    use super::Umap;

    /// Replays `+K` (insert), `-K` (erase), `c` (clear) and checks each `p` against the expected (buckets, order).
    fn replay(ops: &str, expected: &[(u64, &[u16])]) {
        let mut m = Umap::default();
        let mut e = expected.iter();
        for op in ops.split_whitespace() {
            match op.as_bytes()[0] {
                b'+' => m.insert(op[1..].parse().unwrap_or(0)),
                b'-' => m.erase(op[1..].parse().unwrap_or(0)),
                b'c' => m.clear(),
                _ => {
                    let (b, order) = e.next().copied().unwrap_or((0, &[]));
                    assert_eq!((m.buckets, m.order.as_slice()), (b, order), "after {op} in {ops}");
                }
            }
        }
    }

    /// The orders printed by std::unordered_map<uint16_t, int32_t> built with g++ 13.3.0 (the oracle's compiler).
    #[test]
    fn matches_libstdcxx() {
        replay("+7 p +8 p +5076 p", &[(13, &[7]), (13, &[8, 7]), (13, &[5076, 8, 7])]);
        replay(
            "+1 +2 +3 +4 +5 +6 +7 +8 +9 +10 +11 +12 +13 p +14 p +15 +16 +17 +18 +19 +20 p",
            &[
                (13, &[13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1]),
                (29, &[14, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13]),
                (29, &[20, 19, 18, 17, 16, 15, 14, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13]),
            ],
        );
        replay("+7 +20 +33 +8 p -20 p c p +46 +7 p", &[(13, &[8, 33, 20, 7]), (13, &[8, 33, 7]), (13, &[]), (13, &[7, 46])]);
        replay(
            "+100 +200 +300 +113 +126 +5 +18 +31 +44 +57 +70 +83 +96 +109 p +122 p",
            &[
                (29, &[109, 100, 113, 200, 5, 18, 31, 44, 57, 70, 83, 96, 126, 300]),
                (29, &[122, 109, 100, 113, 200, 5, 18, 31, 44, 57, 70, 83, 96, 126, 300]),
            ],
        );
    }
}
