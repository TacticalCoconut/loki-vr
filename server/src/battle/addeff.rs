//! skill.cpp skill_additional_effect / skill_counter_additional_effect: ported in full by the skill system
//! (skill/effect.rs, wave D skillcore); wave C's normal-attack port that lived here moved there and was completed.
//! The battle code keeps calling them through this path.

pub use crate::skill::effect::{skill_additional_effect, skill_counter_additional_effect};
