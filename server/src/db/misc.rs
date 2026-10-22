//! Status, companion and world databases, as rAthena holds them after loading (tools/datagen_mods/misc.py resolves
//! the load rules — base file then imports, partial overrides, defaults for new entries, each loader's own fixes):
//!
//! - [`status`]: status.cpp StatusDatabase (db/re/status.yml), SCDisabled (db/status_disabled.txt), SizeFixDatabase
//!   (db/size_fix.yml + db/re/size_fix.yml), AttributeDatabase (db/re/attr_fix.yml).
//! - [`pet`]: pet.cpp PetDatabase (db/re/pet_db.yml).
//! - [`homunculus`]: homunculus.cpp HomunculusDatabase (db/re/homunculus_db.yml) and HomExpDatabase (db/re/exp_homun.yml).
//! - [`mercenary`]: mercenary.cpp MercenaryDatabase (db/mercenary_db.yml + db/re/mercenary_db.yml).
//! - [`elemental`]: elemental.cpp ElementalDatabase (db/re/elemental_db.yml).
//! - [`castle`]: guild.cpp CastleDatabase (db/re/castle_db.yml).
//! - [`quest`]: quest.cpp QuestDatabase (db/re/quest_db.yml).
//! - [`achievement`]: achievement.cpp AchievementDatabase / AchievementLevelDatabase.
//! - [`battleground`]: battleground.cpp BattlegroundDatabase (db/battleground_db.yml).
//! - [`instance`]: instance.cpp InstanceDatabase (db/re/instance_db.yml).
//!
//! Lists keep rAthena's unordered_map iteration order wherever a lookup returns the first match in that order.
//! Scripts (status Script, pet scripts, achievement conditions and rewards) are source text for the script engine.

pub mod achievement;
pub mod battleground;
pub mod castle;
pub mod elemental;
pub mod homunculus;
pub mod instance;
pub mod mercenary;
pub mod pet;
pub mod quest;
pub mod status;

/// common/mapindex.cpp mapindex_getmapname: at most 15 characters are read, a ".gat" extension (any case) is dropped,
/// 11 characters are kept.
pub(crate) fn mapindex_getmapname(name: &str) -> String {
    let n: String = name.chars().take(15).collect();
    let n = if n.len() >= 4 && n.is_char_boundary(n.len() - 4) && n[n.len() - 4..].eq_ignore_ascii_case(".gat") { &n[..n.len() - 4] } else { &n[..] };
    n.chars().take(11).collect()
}
