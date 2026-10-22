//! rAthena's static databases (db/*.yml, db/*.txt and conf files they need), embedded as JSON datasets written by
//! tools/datagen_mods/*.py and parsed once per module instance. Each submodule mirrors the rAthena loader it names
//! (defaults, import overrides, Clear/Remove rules are resolved in the datagen step).

pub mod items;
pub mod jobs;
pub mod skills;
pub mod mobs;
pub mod misc;
