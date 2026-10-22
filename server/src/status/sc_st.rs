//! The local state that status.cpp's status_change_start_post_delay, status_change_end and status_change_timer share
//! with their big per-status `switch (type)` blocks. rAthena writes those switches inline; here each switch lives in its
//! own file and works on one of these structs, which hold exactly the variables the C++ cases read and change:
//! - [`StartSt`]: status_change_start_post_delay's `src`, `bl`, `sd`, `type`, `val1`..`val4`, `tick`, `tick_time`,
//!   `flag`, `scdb`, `status`, `undead_flag` (the immunity switch `sc_immune.rs`, the value setup `sc_vals_*.rs`).
//! - [`EndSt`]: status_change_end's `bl`, `type`, `tid`, the copies `val1`..`val4`, `calc_flag`, `scdb`
//!   (`sc_end_fx.rs`).
//! - [`TimerSt`]: status_change_timer's `tid`, `tick`, `bl`, `type`, `sce`, `interval` (`sc_timer_fx.rs`).
//!
//! rAthena's `status` and `sce` are pointers to live data; the rows here are copies, so a case re-reads them after a
//! call that can change them (`StartSt::status`, `TimerSt::status` read the live values every time).

use super::data::{sc_entry, ScEntry, StatusData};
use super::defs::*;
use super::sc;
use crate::db::misc::status::StatusChangeDb;
use crate::timer::{self, Func};
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// What a switch case does next: `break` out of the switch (Continue) or `return` from the function.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Flow {
    Continue,
    Return(bool),
}

/// status_change_start_post_delay's variables.
pub struct StartSt<'a, 'b> {
    pub c: &'a Ctx<'b>,
    /// `src` (None: no source unit; rAthena would have a null pointer).
    pub src: Option<Unit>,
    /// `bl` (the row as it was when the function started: id, kind, map and position; re-read with `unit::get` for
    /// anything that changes).
    pub bl: Unit,
    pub t: i32,
    pub val1: i32,
    pub val2: i32,
    pub val3: i32,
    pub val4: i32,
    /// `int32 tick` (the duration; INFINITE_TICK -1).
    pub tick: i32,
    /// `int32 tick_time` (the first interval of a periodic status; 0 none).
    pub tick_time: i32,
    pub flag: u8,
    /// `scdb` (status_db.find(type)).
    pub scdb: StatusChangeDb,
    /// `undead_flag = battle_check_undead(status->race, status->def_ele)` at the start of the function.
    pub undead_flag: bool,
}

impl StartSt<'_, '_> {
    /// `sd != nullptr` (the target is a player).
    pub fn is_pc(&self) -> bool {
        self.bl.kind == BL_PC
    }
    /// `status_get_status_data(*bl)`, read now (rAthena's `status` pointer sees every change).
    pub fn status(&self) -> StatusData {
        super::status_get_status_data(self.c, &self.bl)
    }
    /// `src->id` (0 when there is no source).
    pub fn src_id(&self) -> u32 {
        self.src.as_ref().map(|s| s.id).unwrap_or(0)
    }
    /// `src->type` (BL_NUL when there is no source).
    pub fn src_kind(&self) -> u16 {
        self.src.as_ref().map(|s| s.kind).unwrap_or(0)
    }
    /// The target's live unit row (falls back to the row the function started with).
    pub fn bl_now(&self) -> Unit {
        unit::get(self.c, self.bl.id).unwrap_or_else(|| self.bl.clone())
    }
    /// `sc->getSCE(type)` on the target, read now.
    pub fn sce(&self, t: i32) -> Option<ScEntry> {
        sc::get_sce(self.c, self.bl.id, t)
    }
    pub fn has(&self, t: i32) -> bool {
        sc::has_sce(self.c, self.bl.id, t)
    }
}

/// What status_change_end does after its effect switch.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum EndFlow {
    Continue,
    Return(i32),
}

/// status_change_end's variables after the entry was removed.
pub struct EndSt<'a, 'b> {
    pub c: &'a Ctx<'b>,
    /// `bl` (the row when the effect switch starts).
    pub bl: Unit,
    pub t: i32,
    /// `tid` (0 = INVALID_TIMER: ended by code, not by the status's own timer).
    pub tid: u64,
    /// The copies of the removed entry's values.
    pub val1: i32,
    pub val2: i32,
    pub val3: i32,
    pub val4: i32,
    /// `calc_flag` (SCB_* bits; SC_INTRAVISION widens it).
    pub calc_flag: u64,
    pub scdb: StatusChangeDb,
}

impl EndSt<'_, '_> {
    pub fn is_pc(&self) -> bool {
        self.bl.kind == BL_PC
    }
    /// `status_get_status_data(*bl)`, read now.
    pub fn status(&self) -> StatusData {
        super::status_get_status_data(self.c, &self.bl)
    }
    pub fn bl_now(&self) -> Unit {
        unit::get(self.c, self.bl.id).unwrap_or_else(|| self.bl.clone())
    }
    /// `sc->getSCE(type)` on the unit, read now.
    pub fn sce(&self, t: i32) -> Option<ScEntry> {
        sc::get_sce(self.c, self.bl.id, t)
    }
    pub fn has(&self, t: i32) -> bool {
        sc::has_sce(self.c, self.bl.id, t)
    }
}

/// What status_change_timer does after its effect switch: `break` (Break: then the interval check, else the status
/// ends) or `return 0` (Return: the case re-armed the timer or the status stays as it is).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum TimerFlow {
    Break,
    Return,
}

/// status_change_timer's variables.
pub struct TimerSt<'a, 'b> {
    pub c: &'a Ctx<'b>,
    pub tid: u64,
    pub tick: Tick,
    /// `bl` (the row when the timer fired).
    pub bl: Unit,
    pub t: i32,
    /// `sce` (a copy: `sync` writes it back, `reload` reads it again).
    pub sce: ScEntry,
    /// `interval` (status_get_sc_interval(type); SC_TOXIN / SC_SERVANTWEAPON / SC_ABYSSFORCEWEAPON change it).
    pub interval: i32,
}

impl TimerSt<'_, '_> {
    pub fn is_pc(&self) -> bool {
        self.bl.kind == BL_PC
    }
    /// `status_get_status_data(*bl)`, read now.
    pub fn status(&self) -> StatusData {
        super::status_get_status_data(self.c, &self.bl)
    }
    pub fn bl_now(&self) -> Unit {
        unit::get(self.c, self.bl.id).unwrap_or_else(|| self.bl.clone())
    }
    /// `sc->getSCE(type)` of any type on the unit, read now.
    pub fn get(&self, t: i32) -> Option<ScEntry> {
        sc::get_sce(self.c, self.bl.id, t)
    }
    pub fn has(&self, t: i32) -> bool {
        sc::has_sce(self.c, self.bl.id, t)
    }
    /// The lambda `sc_timer_next(t)`: `sce->timer = add_timer(t, status_change_timer, bl->id, data)`, and the entry is
    /// written back. When a call inside the case ended the status (the unit died, …) there is no entry left: rAthena
    /// would write into the freed entry (its cases that can get there test `sc->getSCE(type)` first); no timer is
    /// added then.
    pub fn next(&mut self, due: Tick) {
        if self.c.db().sc_entry().id().find(self.sce.id).is_none() {
            return;
        }
        self.sce.timer = timer::add(self.c, due, Func::StatusChangeTimer, self.bl.id, self.t as i64);
        sc::put_sce(self.c, &self.sce);
    }
    /// Writes the copy of `sce` back (before calling something that reads this unit's status changes).
    pub fn sync(&self) {
        sc::put_sce(self.c, &self.sce);
    }
    /// Reads `sce` again (after a call that may have changed or ended it). False when the status is gone.
    pub fn reload(&mut self) -> bool {
        match sc::get_sce(self.c, self.bl.id, self.t) {
            Some(e) => {
                self.sce = e;
                true
            }
            None => false,
        }
    }
}
