//! The parts of map_session_data that only battle / PvP / death use, one row per online character (`pc_battle`,
//! created on first use with rAthena's zeroed defaults, removed by unit_free).

use spacetimedb::{table, Table};

use crate::world::Ctx;

#[table(accessor = pc_battle)]
#[derive(Clone, Debug, Default, PartialEq)]
pub struct PcBattle {
    #[primary_key]
    pub account_id: u32,
    /// sd->state.arrow_atk: the last normal attack was a ranged (ammo) attack.
    pub arrow_atk: bool,
    /// sd->state.killer (@killer): may attack anything.
    pub killer: bool,
    /// sd->state.killable (@killable): anything may attack it.
    pub killable: bool,
    /// sd->pvp_point, pvp_rank, pvp_lastusers, pvp_won, pvp_lost, pvp_timer (pc_calc_pvprank_timer; 0 = none).
    pub pvp_point: i32,
    pub pvp_rank: u32,
    pub pvp_lastusers: u32,
    pub pvp_won: u32,
    pub pvp_lost: u32,
    pub pvp_timer: u64,
    /// sd->respawn_tid (pc_respawn_timer; 0 = none).
    pub respawn_tid: u64,
    /// sd->state.snovice_dead_flag (the Super Novice's angel saved it once this level).
    pub snovice_dead_flag: bool,
}

pub fn get(c: &Ctx, aid: u32) -> PcBattle {
    c.db().pc_battle().account_id().find(aid).unwrap_or(PcBattle { account_id: aid, ..Default::default() })
}

pub fn put(c: &Ctx, b: &PcBattle) {
    match c.db().pc_battle().account_id().find(b.account_id) {
        Some(old) if old == *b => {}
        Some(_) => {
            c.db().pc_battle().account_id().update(b.clone());
        }
        None => {
            c.db().pc_battle().insert(b.clone());
        }
    }
}

/// Runs `f` on the character's row and stores it.
pub fn with<R>(c: &Ctx, aid: u32, f: impl FnOnce(&mut PcBattle) -> R) -> R {
    let mut b = get(c, aid);
    let r = f(&mut b);
    put(c, &b);
    r
}

/// unit_free: the row goes with the character.
pub fn free(c: &Ctx, aid: u32) {
    c.db().pc_battle().account_id().delete(aid);
}
