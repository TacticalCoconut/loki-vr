//! Load-test profiling (feature `loadprof`, never in a published build; testrig/load, server/ARCHITECTURE.md): every
//! 10th timer wakeup is sampled: each timer function it runs is timed by the host (LogStopwatch: a "Timing span" log
//! line per call) and a few counters (area queries, rows they iterate, unit reads, bytes queued) are logged at its end.
//! Without the feature every function here is empty.

#[cfg(feature = "loadprof")]
mod imp {
    use std::cell::{Cell, RefCell};
    use std::collections::BTreeMap;

    thread_local! {
        static N: Cell<u64> = const { Cell::new(0) };
        static ON: Cell<bool> = const { Cell::new(false) };
        static COUNTS: RefCell<BTreeMap<&'static str, u64>> = RefCell::new(BTreeMap::new());
        static SITES: RefCell<BTreeMap<(&'static str, u32), u64>> = RefCell::new(BTreeMap::new());
    }

    pub fn site(l: &'static std::panic::Location<'static>) {
        if ON.with(|o| o.get()) {
            SITES.with(|c| *c.borrow_mut().entry((l.file(), l.line())).or_default() += 1);
        }
    }

    pub fn wakeup_start() -> Option<spacetimedb::log_stopwatch::LogStopwatch> {
        let n = N.with(|n| {
            n.set(n.get() + 1);
            n.get()
        });
        let on = n % 10 == 0;
        ON.with(|o| o.set(on));
        COUNTS.with(|c| c.borrow_mut().clear());
        SITES.with(|c| c.borrow_mut().clear());
        on.then(|| spacetimedb::log_stopwatch::LogStopwatch::new("wakeup"))
    }

    pub fn wakeup_end() {
        if ON.with(|o| o.get()) {
            let s = COUNTS.with(|c| c.borrow().iter().map(|(k, v)| format!("{k}={v}")).collect::<Vec<_>>().join(" "));
            log::info!("prof counts: {s}");
            let t = SITES.with(|c| c.borrow().iter().map(|((f, l), v)| format!("{f}:{l}={v}")).collect::<Vec<_>>().join(" "));
            log::info!("prof sites: {t}");
        }
        ON.with(|o| o.set(false));
    }

    pub fn span(name: impl FnOnce() -> String) -> Option<spacetimedb::log_stopwatch::LogStopwatch> {
        ON.with(|o| o.get()).then(|| spacetimedb::log_stopwatch::LogStopwatch::new(&name()))
    }

    pub fn count(key: &'static str, n: u64) {
        if ON.with(|o| o.get()) {
            COUNTS.with(|c| *c.borrow_mut().entry(key).or_default() += n);
        }
    }
}

#[cfg(feature = "loadprof")]
pub use imp::*;

#[cfg(not(feature = "loadprof"))]
mod off {
    pub struct Nothing;
    #[inline(always)]
    pub fn wakeup_start() -> Option<Nothing> {
        None
    }
    #[inline(always)]
    pub fn wakeup_end() {}
    #[inline(always)]
    pub fn span(_name: impl FnOnce() -> String) -> Option<Nothing> {
        None
    }
    #[inline(always)]
    pub fn count(_key: &'static str, _n: u64) {}
    #[inline(always)]
    pub fn site<T>(_l: T) {}
}

#[cfg(not(feature = "loadprof"))]
pub use off::*;
