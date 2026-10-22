//! rAthena's timer heap (common/timer.cpp:47 `BHEAP_VAR(int32, timer_heap)`, ordered by timer.cpp:44
//! DIFFTICK_MINTOPCMP): the binary heap do_timer pops, ported operation for operation from common/db.hpp (BHEAP_PUSH
//! :1487, BHEAP_POP :1527, BHEAP_POPINDEX :1556) so that timers due on the same tick run in rAthena's order. The heap
//! breaks ties by its shape, not by age: BHEAP_PUSH moves a new timer above a parent with the same tick (it stops only
//! on a parent that is strictly earlier), so timers pushed one after the other for one tick near the top pop newest
//! first (three monster vanishes of one Magnus Exorcismus, a homunculus' next attack before the delayed damage of its
//! previous hit).
//!
//! As in rAthena, delete_timer does not take a timer out of the heap (timer.cpp:302-319 only clears its function): the
//! entry stays, keeps its place in the heap's shape, and is dropped when do_timer pops it. Here a deleted timer is an
//! entry whose `timer` row is gone. rAthena's heap holds every timer of its map server, some of which the module does
//! not have (socket, char-server and log timers …), so the order is rAthena's for the timers both servers share only
//! as far as those other timers do not change the heap's shape around them.

use std::collections::{BTreeMap, BTreeSet};

use spacetimedb::{table, Table};

use super::timer as _;
use crate::world::{Ctx, Tick};

/// One heap slot: a timer's tick and id (timer_data[tid].tick, tid).
pub type Entry = (Tick, u64);

/// The heap, one row per slot: `pos` is the index in rAthena's heap vector.
#[table(accessor = timer_heap, index(accessor = by_id, btree(columns = [id])))]
pub struct TimerHeapSlot {
    #[primary_key]
    #[index(direct)]
    pub pos: u32,
    pub due: Tick,
    pub id: u64,
}

/// Present (key 0) once the heap holds every timer: a database from before the heap has timer rows and no slots, and
/// [`ensure_built`] fills the heap from them once.
#[table(accessor = timer_heap_built)]
pub struct TimerHeapBuilt {
    #[primary_key]
    #[index(direct)]
    pub key: u8,
}

/// Where the heap operations read and write: a Vec in tests, the `timer_heap` table in the module.
pub trait Store {
    fn len(&self) -> usize;
    fn get(&mut self, i: usize) -> Entry;
    fn set(&mut self, i: usize, e: Entry);
    /// VECTOR_PUSH.
    fn push_back(&mut self, e: Entry);
    /// VECTOR_POP: the last slot, which leaves the vector.
    fn pop_back(&mut self) -> Entry;
    /// std::swap of two slots.
    fn swap(&mut self, a: usize, b: usize) {
        let (x, y) = (self.get(a), self.get(b));
        self.set(a, y);
        self.set(b, x);
    }
}

/// timer.cpp:44 DIFFTICK_MINTOPCMP(tid1, tid2) = DIFF_TICK(timer_data[tid1].tick, timer_data[tid2].tick) (timer.hpp:14
/// tick_diff: a - b): negative when the first is on top.
fn topcmp(a: Entry, b: Entry) -> i64 {
    a.0 - b.0
}

/// db.hpp:1487 BHEAP_PUSH: append, then swap up while the parent is not strictly earlier.
pub fn push<S: Store>(h: &mut S, v: Entry) {
    let mut i = h.len();
    h.push_back(v);
    while i != 0 {
        let parent = (i - 1) / 2;
        if topcmp(h.get(parent), h.get(i)) < 0 {
            break;
        }
        h.swap(parent, i);
        i = parent;
    }
}

/// db.hpp:1556 BHEAP_POPINDEX: the last slot replaces slot `idx`, then moves up (while its parent is not strictly
/// earlier) and down (to the earlier child, the left one on a tie, while a child is strictly earlier). Assumes the
/// index exists.
pub fn pop_index<S: Store>(h: &mut S, idx: usize) {
    let mut i = idx;
    // VECTOR_INDEX(__heap,__idx) = VECTOR_POP(__heap)
    let last = h.pop_back();
    if i >= h.len() {
        return; // removed last, nothing to do
    }
    h.set(idx, last);
    while i != 0 {
        let parent = (i - 1) / 2;
        if topcmp(h.get(parent), h.get(i)) < 0 {
            break;
        }
        h.swap(parent, i);
        i = parent;
    }
    while i < h.len() {
        let l = i * 2 + 1;
        let r = i * 2 + 2;
        let len = h.len();
        if (l >= len || topcmp(h.get(i), h.get(l)) <= 0) && (r >= len || topcmp(h.get(i), h.get(r)) <= 0) {
            break;
        } else if r >= len || topcmp(h.get(l), h.get(r)) <= 0 {
            h.swap(i, l);
            i = l;
        } else {
            h.swap(i, r);
            i = r;
        }
    }
}

/// db.hpp:1527 BHEAP_POP.
pub fn pop<S: Store>(h: &mut S) {
    pop_index(h, 0);
}

impl Store for Vec<Entry> {
    fn len(&self) -> usize {
        Vec::len(self)
    }
    fn get(&mut self, i: usize) -> Entry {
        self[i]
    }
    fn set(&mut self, i: usize, e: Entry) {
        self[i] = e;
    }
    fn push_back(&mut self, e: Entry) {
        self.push(e);
    }
    fn pop_back(&mut self) -> Entry {
        self.pop().expect("BHEAP_POPINDEX on an empty heap")
    }
}

thread_local! {
    /// Per-call cache of `timer_heap` slots and `timer_heap_built`, reset at the start of `world::run` and flushed
    /// once at the end of `world::run` (and before `by_id` index reads in `find`). During `on_wakeup` dozens of
    /// timers pop and re-push; keeping the touched heap slots in memory across the call avoids re-reading `count()`
    /// and rewriting `slot 0` and its children to WASM host tables on every individual pop and push.
    static CACHE: std::cell::RefCell<HeapCache> = const { std::cell::RefCell::new(HeapCache::new()) };
}

struct HeapCache {
    built: bool,
    open: bool,
    len: usize,
    orig_len: usize,
    slots: BTreeMap<usize, Entry>,
    orig_slots: BTreeMap<usize, Entry>,
    dirty: BTreeSet<usize>,
}

impl HeapCache {
    const fn new() -> Self {
        HeapCache {
            built: false,
            open: false,
            len: 0,
            orig_len: 0,
            slots: BTreeMap::new(),
            orig_slots: BTreeMap::new(),
            dirty: BTreeSet::new(),
        }
    }
}

/// Resets the per-call timer heap cache (`world::run` start).
pub fn reset_cache() {
    CACHE.with(|c| *c.borrow_mut() = HeapCache::new());
}

/// Flushes dirty heap slots from the per-call cache to `timer_heap` (`world::run` end, and before `by_id` queries).
pub fn flush(c: &Ctx) {
    CACHE.with(|cell| {
        let mut h = cell.borrow_mut();
        if !h.open || (h.dirty.is_empty() && h.len == h.orig_len) {
            return;
        }
        let t = c.db().timer_heap();
        let len = h.len;
        let orig_len = h.orig_len;
        let dirty: Vec<usize> = h.dirty.iter().copied().filter(|&p| p < len).collect();
        for pos in dirty {
            let (due, id) = h.slots[&pos];
            let row = TimerHeapSlot { pos: pos as u32, due, id };
            if pos < orig_len {
                if h.orig_slots.get(&pos) != Some(&(due, id)) {
                    t.pos().update(row);
                    h.orig_slots.insert(pos, (due, id));
                }
            } else {
                t.insert(row);
                h.orig_slots.insert(pos, (due, id));
            }
        }
        for pos in len..orig_len {
            t.pos().delete(pos as u32);
            h.orig_slots.remove(&pos);
            h.slots.remove(&pos);
        }
        h.orig_len = len;
        h.dirty.clear();
    });
}

struct TableStore<'a, 'c> {
    c: &'a Ctx<'c>,
    h: &'a mut HeapCache,
}

fn with_store<R>(c: &Ctx, f: impl FnOnce(TableStore<'_, '_>) -> R) -> R {
    CACHE.with(|cell| {
        let mut h = cell.borrow_mut();
        if !h.open {
            let len = c.db().timer_heap().count() as usize;
            h.len = len;
            h.orig_len = len;
            h.open = true;
        }
        f(TableStore { c, h: &mut h })
    })
}

impl Store for TableStore<'_, '_> {
    fn len(&self) -> usize {
        self.h.len
    }
    fn get(&mut self, i: usize) -> Entry {
        if let Some(e) = self.h.slots.get(&i) {
            return *e;
        }
        if let Some(e) = self.c.db().timer_heap().pos().find(i as u32).map(|s| (s.due, s.id)) {
            self.h.orig_slots.entry(i).or_insert(e);
            self.h.slots.insert(i, e);
            return e;
        }
        log::warn!("timer heap: slot {i} of {} is missing; rebuilding from timer table", self.h.len);
        let stale: Vec<u32> = self.c.db().timer_heap().iter().map(|s| s.pos).collect();
        for pos in stale {
            self.c.db().timer_heap().pos().delete(pos);
        }
        let mut live: Vec<Entry> = self.c.db().timer().iter().map(|t| (t.due, t.id)).collect();
        live.sort_unstable();
        self.h.slots.clear();
        self.h.orig_slots.clear();
        self.h.dirty.clear();
        for (pos, entry) in live.iter().copied().enumerate() {
            self.c.db().timer_heap().insert(TimerHeapSlot { pos: pos as u32, due: entry.0, id: entry.1 });
            self.h.orig_slots.insert(pos, entry);
            self.h.slots.insert(pos, entry);
        }
        self.h.len = live.len();
        self.h.orig_len = live.len();
        live.as_slice().get(i).copied().unwrap_or((0, 0))
    }
    fn set(&mut self, i: usize, e: Entry) {
        self.h.slots.insert(i, e);
        self.h.dirty.insert(i);
    }
    fn push_back(&mut self, e: Entry) {
        let i = self.h.len;
        self.h.len += 1;
        self.set(i, e);
    }
    fn pop_back(&mut self) -> Entry {
        let e = self.get(self.h.len - 1);
        self.h.len -= 1;
        e
    }
}

/// A database published before the heap existed has timer rows but no heap: they go in once, in (tick, id) order (a
/// sorted vector is a valid heap). A new database gets the marker with its first timer.
pub fn ensure_built(c: &Ctx) {
    if CACHE.with(|h| h.borrow().built) {
        return;
    }
    if c.db().timer_heap_built().key().find(0).is_some() {
        CACHE.with(|h| h.borrow_mut().built = true);
        return;
    }
    reset_cache();
    let stale: Vec<u32> = c.db().timer_heap().iter().map(|s| s.pos).collect();
    for pos in stale {
        c.db().timer_heap().pos().delete(pos);
    }
    let mut live: Vec<Entry> = c.db().timer().iter().map(|t| (t.due, t.id)).collect();
    live.sort_unstable();
    if !live.is_empty() {
        log::info!("timer heap: filled with the {} existing timers", live.len());
    }
    for (pos, (due, id)) in live.into_iter().enumerate() {
        c.db().timer_heap().insert(TimerHeapSlot { pos: pos as u32, due, id });
    }
    c.db().timer_heap_built().insert(TimerHeapBuilt { key: 0 });
    CACHE.with(|h| h.borrow_mut().built = true);
}

/// BHEAP_PUSH onto the table.
pub fn push_timer(c: &Ctx, due: Tick, id: u64) {
    with_store(c, |mut h| push(&mut h, (due, id)));
}

/// BHEAP_PEEK: the top slot (the smallest tick), deleted timers included.
pub fn peek(c: &Ctx) -> Option<Entry> {
    CACHE.with(|cell| {
        let mut h = cell.borrow_mut();
        if h.open && h.len == 0 {
            return None;
        }
        if let Some(&e) = h.slots.get(&0) {
            return Some(e);
        }
        let e = c.db().timer_heap().pos().find(0).map(|s| (s.due, s.id));
        if let Some(e) = e {
            h.orig_slots.entry(0).or_insert(e);
            h.slots.insert(0, e);
        } else if !h.open {
            h.len = 0;
            h.orig_len = 0;
            h.open = true;
        }
        e
    })
}

/// BHEAP_POP from the table.
pub fn pop_top(c: &Ctx) {
    with_store(c, |mut h| {
        if h.len() > 0 {
            pop(&mut h);
        }
    });
}

/// settick_timer's `ARR_FIND(0, BHEAP_LENGTH(timer_heap), i, BHEAP_DATA(timer_heap)[i] == tid)`: the slot of a timer
/// that is in the heap (not one that do_timer has popped and is running).
pub fn find(c: &Ctx, id: u64) -> Option<(usize, Tick)> {
    flush(c);
    c.db().timer_heap().by_id().filter(id).next().map(|s| (s.pos as usize, s.due))
}

/// settick_timer (timer.cpp:349-351): BHEAP_POPINDEX, the new tick, BHEAP_PUSH.
pub fn move_slot(c: &Ctx, pos: usize, id: u64, tick: Tick) {
    with_store(c, |mut h| {
        pop_index(&mut h, pos);
        push(&mut h, (tick, id));
    });
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Equal ticks pushed one after the other on an empty heap pop newest first (BHEAP_PUSH swaps past a parent with
    /// the same tick): [A] → [B, A] → [C, A, B]; popping C moves B to the top, and B then A follow.
    #[test]
    fn equal_ticks_pushed_in_a_row_pop_newest_first() {
        let mut h: Vec<Entry> = Vec::new();
        for id in 1..=3 {
            push(&mut h, (100, id));
        }
        let mut order = Vec::new();
        while !h.is_empty() {
            order.push(h[0].1);
            pop(&mut h);
        }
        assert_eq!(order, vec![3, 2, 1]);
    }

    /// An earlier tick always pops first, whatever the pushes' order.
    #[test]
    fn earlier_ticks_pop_first() {
        let mut h: Vec<Entry> = Vec::new();
        for (i, t) in [50, 10, 40, 10, 30, 20].into_iter().enumerate() {
            push(&mut h, (t, i as u64));
        }
        let mut ticks = Vec::new();
        while !h.is_empty() {
            ticks.push(h[0].0);
            pop(&mut h);
        }
        assert_eq!(ticks, vec![10, 10, 20, 30, 40, 50]);
    }

    /// The same 4000 random operations (push with ticks 0-7, pop, and settick's POPINDEX + PUSH of a random slot) run
    /// through rAthena's own macros give this pop order: a C++ program that includes rAthena e985006's
    /// common/db.hpp and uses BHEAP_PUSH / BHEAP_POP / BHEAP_POPINDEX with timer.cpp's DIFFTICK_MINTOPCMP, the same
    /// LCG (x = x * 6364136223846793005 + 1442695040888963407, value x >> 33, seed 12345) and the same draws, printed
    /// the popped ids as "%d," (2447 pops, 11125 bytes, FNV-1a 64 below; the first ones are checked as a readable
    /// sample).
    #[test]
    fn pop_order_matches_rathena_bheap() {
        let mut x: u64 = 12345;
        let mut next = || {
            x = x.wrapping_mul(6364136223846793005).wrapping_add(1442695040888963407);
            x >> 33
        };
        let mut h: Vec<Entry> = Vec::new();
        let mut nid: u64 = 0;
        let mut out = String::new();
        for _ in 0..4000 {
            let op = next() % 10;
            if op <= 5 || h.is_empty() {
                let tid = nid;
                nid += 1;
                let tick = (next() % 8) as i64;
                push(&mut h, (tick, tid));
            } else if op <= 8 {
                out.push_str(&format!("{},", h[0].1));
                pop(&mut h);
            } else {
                let i = (next() % h.len() as u64) as usize;
                let tid = h[i].1;
                pop_index(&mut h, i);
                let tick = (next() % 8) as i64;
                push(&mut h, (tick, tid));
            }
        }
        while !h.is_empty() {
            out.push_str(&format!("{},", h[0].1));
            pop(&mut h);
        }
        assert!(out.starts_with("1,3,0,2,6,7,8,5,9,10,16,17,11,22,26,27,31,33,36,30,14,21,37,38,51,42,44,39,50,43,20,"));
        assert_eq!(out.matches(',').count(), 2447);
        assert_eq!(out.len(), 11125);
        let fnv = out.bytes().fold(0xcbf29ce484222325u64, |h, b| (h ^ b as u64).wrapping_mul(0x100000001b3));
        assert_eq!(fnv, 0x9e7d71ab4d6cc6d6);
    }
}
