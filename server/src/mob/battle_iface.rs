//! The battle agent's functions the monster code calls (battle.cpp battle_check_target / battle_check_range /
//! battle_getenemy / battle_get_master, unit.cpp unit_attack / unit_set_walkdelay). They are ported by the battle
//! agent (server/ARCHITECTURE.md, "mob <-> battle coordination"); its commits 6e686c6 and 84e1e29 are cherry-picked on
//! this branch, so every function here forwards to `crate::battle`.

use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// battle.hpp enum e_battle_check_target (the flags the monster code uses).
pub const BCT_NOONE: i32 = crate::battle::BCT_NOONE;
pub const BCT_SELF: i32 = crate::battle::BCT_SELF;
pub const BCT_ENEMY: i32 = crate::battle::BCT_ENEMY;
pub const BCT_PARTY: i32 = crate::battle::BCT_PARTY;
pub const BCT_GUILD: i32 = crate::battle::BCT_GUILD;

/// battle.cpp battle_check_target (battle agent, src/battle/target.rs).
pub fn battle_check_target(c: &Ctx, src: &Unit, target: &Unit, flag: i32) -> i32 {
    crate::battle::target::battle_check_target(c, src, target, flag)
}

/// battle.cpp battle_check_range (battle agent, src/battle/target.rs).
pub fn battle_check_range(c: &Ctx, src: &Unit, target: &Unit, range: i32) -> bool {
    crate::battle::target::battle_check_range(c, src, target, range)
}

/// battle.cpp battle_getenemy (battle agent, src/battle/target.rs, commit 84e1e29).
pub fn battle_getenemy(c: &Ctx, target: &Unit, kinds: u16, range: i32) -> Option<Unit> {
    crate::battle::target::battle_getenemy(c, target, kinds, range)
}

/// battle.cpp battle_get_master (battle agent, src/battle/target.rs). rAthena returns the unit itself when it has no
/// master; `None` never comes back from the port.
pub fn battle_get_master(c: &Ctx, src: &Unit) -> Option<Unit> {
    Some(crate::battle::target::battle_get_master(c, src))
}

/// unit.cpp unit_attack (battle agent, src/battle/unit_attack.rs): the USW_* stop flag.
pub fn unit_attack(c: &Ctx, src_id: u32, target_id: u32, continuous: i32) -> i32 {
    crate::battle::unit_attack::unit_attack(c, src_id, target_id, continuous)
}

/// unit.cpp unit_set_walkdelay (battle agent, src/battle/unit_attack.rs; skill_id defaults to 0 as in unit.hpp).
/// The unit row is loaded and saved by the port: callers must not hold a copy they save afterwards.
pub fn unit_set_walkdelay(c: &Ctx, id: u32, tick: Tick, delay: Tick, type_: i32) -> i32 {
    crate::battle::unit_attack::unit_set_walkdelay(c, id, tick, delay, type_, 0)
}
