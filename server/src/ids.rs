//! Sequential ids in rAthena's ranges. SpacetimeDB's auto-increment leaves gaps and starts at 1, so the module keeps
//! its own counters (one row per kind).

use spacetimedb::{table, ReducerContext, Table};

#[table(accessor = id_counter)]
pub struct IdCounter {
    #[primary_key]
    pub kind: String,
    pub next: u32,
}

/// Returns the next id of `kind`, starting at `start`.
pub fn next(ctx: &ReducerContext, kind: &str, start: u32) -> u32 {
    let key = kind.to_string();
    match ctx.db.id_counter().kind().find(&key) {
        Some(row) => {
            let id = row.next;
            ctx.db.id_counter().kind().update(IdCounter { kind: key, next: id + 1 });
            id
        }
        None => {
            ctx.db.id_counter().insert(IdCounter { kind: key, next: start + 1 });
            start
        }
    }
}
