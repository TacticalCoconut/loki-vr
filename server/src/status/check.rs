//! status.cpp status_check_skilluse / status_check_visibility at the path other systems call (server/ARCHITECTURE.md,
//! battle -> mob). The port lives with the monster code that needs it most (src/mob/unit.rs, mob agent).

pub use crate::mob::unit::{status_check_skilluse, status_check_visibility};
