//! The iteration order of rAthena's `status_change::data`, a `std::unordered_map<sc_type, status_change_entry>`
//! (status.hpp). rAthena iterates that map in two places: chrif_save_scdata (the order the char server stores the
//! statuses at logout, so the order they start again at the next login) and status_calc_pc_sub (the order the status
//! scripts run). The oracle is built with GCC's libstdc++, whose unordered_map is a hash table with one singly linked
//! list of all nodes; its order depends on the bucket count and on the history of inserts, erases and rehashes. This
//! file reproduces it:
//! - the hash of an enum key is its integer value; the bucket is `key % bucket_count`;
//! - a new key goes first in its bucket's group of nodes, or at the front of the list when its bucket is empty
//!   (`_M_insert_bucket_begin`);
//! - before an insert, `_Prime_rehash_policy::_M_need_rehash` may grow the bucket count (first insert: 13 buckets;
//!   then 29, 59, 127 … when the element count passes it, max_load_factor 1.0); `_M_rehash_aux` then walks the old
//!   list in order and inserts each node first in its new bucket's group, or at the front when that bucket is empty;
//! - erase unlinks the node (buckets never shrink).
//! The state lives in the unit's `sc_unit` row (status_change is created with the unit and lives as long as it).

use super::data::ScUnit;

/// libstdc++ `__prime_list` from index 6 (the part `_M_next_bkt`'s lower_bound searches), up to values no unit's
/// status list can reach.
const PRIMES: &[u64] = &[
    17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71, 73, 79, 83, 89, 97, 103, 109, 113, 127, 137, 139, 149, 157, 167, 179, 193, 199, 211,
    227, 241, 257, 277, 293, 313, 337, 359, 383, 409, 439, 467, 503, 541, 577, 619, 661, 709, 761, 823, 887, 953, 1031, 1109, 1193, 1289, 1381,
    1493, 1613, 1741, 1879, 2029, 2179, 2357,
];

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
    let b = PRIMES.iter().copied().find(|&p| p >= n).unwrap_or(*PRIMES.last().unwrap_or(&2357));
    (b, b)
}

fn bucket(key: i32, buckets: u64) -> u64 {
    (key as i64 as u64) % buckets.max(1)
}

/// `_M_insert_bucket_begin`: `key` goes before the first node of its bucket, or at the front of the list.
fn insert_bucket_begin(order: &mut Vec<i32>, key: i32, buckets: u64) {
    let b = bucket(key, buckets);
    match order.iter().position(|&k| bucket(k, buckets) == b) {
        Some(i) => order.insert(i, key),
        None => order.insert(0, key),
    }
}

/// `data[type]` of a new key (status_change::createSCE): the rehash check, then the insert.
pub fn insert(us: &mut ScUnit, key: i32) {
    if us.umap_order.contains(&key) {
        return;
    }
    let n_elt = us.umap_order.len() as u64;
    let n_bkt = us.umap_buckets.max(1);
    // _M_need_rehash(n_bkt, n_elt, 1)
    if n_elt + 1 > us.umap_next_resize {
        let min_bkts = (n_elt + 1).max(if us.umap_next_resize != 0 { 0 } else { 11 }) as f64;
        if min_bkts >= n_bkt as f64 {
            let (nb, next_resize) = next_bkt(((min_bkts.floor() as u64) + 1).max(n_bkt * 2));
            // _M_rehash_aux: the old list in order, each node first in its new bucket's group
            let old = std::mem::take(&mut us.umap_order);
            for k in old {
                insert_bucket_begin(&mut us.umap_order, k, nb);
            }
            us.umap_buckets = nb;
            us.umap_next_resize = next_resize;
        } else {
            us.umap_next_resize = n_bkt;
        }
    }
    let nb = us.umap_buckets.max(1);
    insert_bucket_begin(&mut us.umap_order, key, nb);
}

/// `data.erase(type)` (status_change::deleteSCE).
pub fn erase(us: &mut ScUnit, key: i32) {
    us.umap_order.retain(|&k| k != key);
}

#[cfg(test)]
mod tests {
    use super::*;

    fn fresh() -> ScUnit {
        ScUnit::default()
    }

    /// The first insert sizes the table to 13 buckets; keys go first in their bucket or to the front.
    #[test]
    fn libstdcxx_order() {
        let mut us = fresh();
        insert(&mut us, 30); // bucket 4
        assert_eq!(us.umap_buckets, 13);
        insert(&mut us, 203); // bucket 8: empty, front
        insert(&mut us, 257); // bucket 10: empty, front
        insert(&mut us, 4); // bucket 4: before 30
        insert(&mut us, 0); // bucket 0: front
        assert_eq!(us.umap_order, vec![0, 257, 203, 4, 30]);
        erase(&mut us, 203);
        assert_eq!(us.umap_order, vec![0, 257, 4, 30]);
    }

    /// The 14th key grows the table to 29 buckets and rebuilds the list.
    #[test]
    fn rehash_at_14() {
        let mut us = fresh();
        for k in 0..13 {
            insert(&mut us, k);
        }
        assert_eq!(us.umap_buckets, 13);
        insert(&mut us, 13);
        assert_eq!(us.umap_buckets, 29);
        assert_eq!(us.umap_order.len(), 14);
    }
}
