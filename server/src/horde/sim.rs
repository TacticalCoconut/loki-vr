//! Horde's simulation: client/Assets/Loki/Runtime/Horde/Core/HordeSim.cs ported rule for rule (the C# file is the
//! specification: every function here names its C# lines; nothing is designed here). Fixed ticks (40 a second), seeded,
//! deterministic: for the same tables, seed and calls this engine's [`Sim::state_hash`] equals the C# engine's
//! `StateHash` after every Step (server/src/horde/golden.rs replays tools/hordegolden's runs). Places are integers,
//! 1 cell = 256 units; the floating point the C# engine uses for multipliers is written here in the precision C# uses
//! at each step (see math.rs for what is and is not bit-exact).
//!
//! How C#'s object references are held: a def is its index in the tables' list (`C.Weapon(id)` = the first with that
//! id, as in C#); an `OwnedWeapon` is (slot, index in that player's weapons): the list only ever grows; a `SimFx`'s
//! `Lv` (the level object it was made with, which a later level-up does not change) is (weapon index, level index).
//! Lasting effects carry a `uid` that is never reused, so the snapshot encoder can read an effect it was told of in
//! the state it has at encoding time, as the C# encoder does through its object reference ([`Sim::fx_removed`]).

use std::collections::{HashMap, VecDeque};

use super::content::*;
use super::math::{self, clamp, dist2, isqrt, round, ticks, units, Rng, TICK_RATE, UNIT};

/// HordeLimits.
pub const MAX_PLAYERS: usize = 8;
pub const MAX_ENEMIES: usize = 400;
pub const MAX_PICKUPS: usize = 400;
pub const MAX_FX: usize = 600;
pub const MAX_SUMMONS: usize = 32;
pub const MAX_ARENA_CELLS: i32 = 250;

/// HordeView.cs HordeEventKind.
pub mod ev {
    pub const HIT: u8 = 1;
    pub const PLAYER_HIT: u8 = 2;
    pub const HEAL: u8 = 3;
    pub const SWING: u8 = 4;
    pub const NOVA: u8 = 5;
    pub const STRIKE: u8 = 6;
    pub const CHAIN: u8 = 7;
    pub const ENEMY_DIED: u8 = 8;
    pub const BOSS_APPEARED: u8 = 9;
    pub const PICKUP_TAKEN: u8 = 10;
    pub const LEVEL_UP: u8 = 11;
    pub const CARD_TAKEN: u8 = 12;
    pub const JOB_CHANGED: u8 = 13;
    pub const FELL: u8 = 14;
    pub const ROSE: u8 = 15;
    pub const ENEMY_ABILITY: u8 = 16;
    pub const SUMMON: u8 = 17;
    pub const FINAL_PHASE: u8 = 18;
    pub const PLAYER_JOINED: u8 = 19;
    pub const PLAYER_LEFT: u8 = 20;
    pub const EFFECT: u8 = 21;
    pub const ENEMY_BURST: u8 = 22;
    pub const INCOMING: u8 = 23;
    pub const PLAYER_SLOWED: u8 = 25;
    pub const BOSS_STRIKE: u8 = 26;
    pub const SOUL_SPLIT: u8 = 27;
    pub const SOUL_TAKEN: u8 = 28;
    pub const LIFE_SAVED: u8 = 29;
    pub const PROC: u8 = 30;
    pub const OBJECTIVE_UP: u8 = 32;
    pub const OBJECTIVE_CLAIMED: u8 = 33;
    pub const OBJECTIVE_FADED: u8 = 34;
    pub const OBJECTIVE_GOT: u8 = 35;
    pub const EMPERIUM_WAVE: u8 = 36;
    pub const DASH: u8 = 40;
    pub const WIND_UP: u8 = 56;
}

pub const RISE_BUFF_ID: i32 = -1835;
pub const OBJ_SHRINE_BUFF_ID: i32 = -1701;

#[derive(Clone, Debug)]
pub struct SimObjective {
    pub id: i32,
    pub kind: u8,
    pub state: u8,
    pub x: i32,
    pub y: i32,
    pub radius: i32,
    pub start_tick: i32,
    pub deadline: i32,
    pub end_tick: i32,
    pub progress: f64,
    pub claimer: i32,
    pub crowd: i32,
    pub last_inside: i32,
    pub next_wave: i32,
    pub waves: i32,
    pub guard_slot: i32,
    pub guard_serial: i32,
    pub owner: i32,
    pub last_hit: HashMap<i32, i32>,
}

#[derive(Clone, Copy, Debug)]
pub struct SimObjectiveBuff {
    pub slot: i32,
    pub kind: u8,
    pub until: i32,
    pub next_heal: i32,
    pub fx_id: i32,
}

/// SimSoul (HordeSim.Souls.cs:107-115).
#[derive(Clone, Debug)]
pub struct Soul {
    pub id: i32,
    pub x: i32,
    pub y: i32,
    pub radius: i32,
    pub start_tick: i32,
    pub owners: i32,
    pub dropper: i32,
    pub progress: f64,
    pub claimer: i32,
    pub crowd: i32,
}
/// HordeEventFlags.
pub mod evf {
    pub const CRIT: u8 = 1;
    pub const KILL: u8 = 2;
    pub const DOT: u8 = 4;
    pub const DODGED: u8 = 8;
    pub const ELITE: u8 = 16;
    pub const BOSS: u8 = 32;
    pub const FINAL: u8 = 64;
    pub const LATE: u8 = 128;
}
/// HordeFxKind.
pub mod fxk {
    pub const SHOT: u8 = 1;
    pub const ENEMY_SHOT: u8 = 2;
    pub const ZONE: u8 = 3;
    pub const TRAP: u8 = 4;
    pub const WARNING: u8 = 5;
    pub const ENEMY_WARNING: u8 = 6;
    pub const ORBIT: u8 = 7;
}
/// HordeEnemyFlags.
pub mod ef {
    pub const SLOWED: u8 = 1;
    pub const FROZEN: u8 = 2;
    pub const STUNNED: u8 = 4;
    pub const HURTING: u8 = 8;
    pub const WINDING: u8 = 16;
    pub const CHARGING: u8 = 32;
    pub const KNOCKED: u8 = 64;
}
/// HordeCardKind.
pub mod card {
    pub const NEW_WEAPON: u8 = 1;
    pub const WEAPON_LEVEL: u8 = 2;
    pub const NEW_PASSIVE: u8 = 3;
    pub const PASSIVE_LEVEL: u8 = 4;
    pub const STAT: u8 = 5;
    pub const JOB: u8 = 6;
    pub const HEAL: u8 = 7;
    pub const WEAPON_BRANCH: u8 = 8;
    pub const WEAPON_TRANSCEND: u8 = 9;
    pub const PASSIVE_TRANSCEND: u8 = 10;
}
/// HordeOutcome.
pub mod outcome {
    pub const NONE: u8 = 0;
    pub const VICTORY: u8 = 1;
    pub const TIME_UP: u8 = 2;
    pub const DEFEAT: u8 = 3;
    pub const STOPPED: u8 = 4;
}

/// HordeEvent. Places are kept in units (C# keeps cells as floats, units / 256f: exact, and the wire turns them back).
#[derive(Clone, Copy, Debug, Default)]
pub struct Event {
    pub kind: u8,
    pub flags: u8,
    pub tick: i32,
    pub slot: i32,
    pub target: i32,
    pub def_id: i32,
    pub value: i32,
    pub x: i32,
    pub y: i32,
    pub x2: i32,
    pub y2: i32,
    pub a: f32,
    pub b: f32,
    pub c: f32,
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct Card {
    pub kind: u8,
    pub def_id: i32,
    pub level: i32,
    pub bonus_stat: u8,
    pub bonus_stat_points: u8,
}

/// HordeOffer. `cards` None = null (a card offer's cards are drawn when it opens).
#[derive(Clone, Debug)]
pub struct Offer {
    pub id: i32,
    pub is_job: bool,
    pub seconds: f32,
    pub cards: Option<Vec<Card>>,
}

/// PlayerStats: a player's modifiers, summed.
#[derive(Clone, Debug, Default)]
pub struct Stats {
    pub max_hp_flat: f64,
    pub max_hp_pct: f64,
    pub regen: f64,
    pub move_pct: f64,
    pub damage_pct: f64,
    pub melee_pct: f64,
    pub ranged_pct: f64,
    pub magic_pct: f64,
    pub cooldown_pct: f64,
    pub area_pct: f64,
    pub projectile_count: f64,
    pub projectile_speed_pct: f64,
    pub duration_pct: f64,
    pub pickup_radius_pct: f64,
    pub crit: f64,
    pub crit_damage: f64,
    pub exp_gain_pct: f64,
    pub defence: f64,
    pub damage_taken_pct: f64,
    pub dodge: f64,
    pub heal_power_pct: f64,
    pub revive_speed_pct: f64,
    pub vs_boss: f64,
    pub effect_chance: f64,
    pub knockback_pct: f64,
    pub hp_on_kill: f64,
    pub vs_race: [f64; 10],
    pub vs_element: [f64; 10],
    pub element_damage: [f64; 10],
    pub stats_added: [f64; 6],
}

#[derive(Clone, Debug)]
pub struct OwnedWeapon {
    pub def: usize,
    pub level: i32,
    pub signature: bool,
    pub next_tick: i32,
    pub active_next_tick: i32,
    pub burst_left: i32,
    pub burst_next: i32,
    pub orbit_fx_id: i32,
    pub orb_hits: HashMap<i32, i32>,
}

impl OwnedWeapon {
    fn new(def: usize, next_tick: i32, signature: bool) -> Self {
        OwnedWeapon { def, level: 1, signature, next_tick, active_next_tick: 0, burst_left: 0, burst_next: 0, orbit_fx_id: 0, orb_hits: HashMap::new() }
    }
}

#[derive(Clone, Debug)]
pub struct OwnedPassive {
    pub def: usize,
    pub level: i32,
}

/// SimBuff. `mods` is the level whose Buffs array it holds: (weapon index, level index).
#[derive(Clone, Debug)]
pub struct Buff {
    pub mods: (usize, usize),
    pub until: i32,
    pub source_slot: i32,
    pub weapon_id: i32,
}

#[derive(Clone, Copy, Debug)]
pub struct Input {
    pub seq: u16,
    pub dx: i8,
    pub dy: i8,
}

#[derive(Clone, Debug)]
pub struct Player {
    pub present: bool,
    pub used: bool,
    pub left: bool,
    pub away: bool,
    pub moving: bool,
    pub fallen: bool,
    pub helped: bool,
    pub idle_chooser: bool,
    pub slot: i32,
    pub aid: u32,
    pub name: String,
    pub x: i32,
    pub y: i32,
    pub face_deg: i32,
    pub vx: i32,
    pub vy: i32,
    pub accel_per_tick: i32,
    pub decel_per_tick: i32,
    pub hp: i32,
    pub max_hp: i32,
    pub speed_per_tick: i32,
    pub regen_carry: f64,
    pub kill_heal_carry: f64,
    pub revive: f64,
    pub level: i32,
    pub job_id: i32,
    pub tier: i32,
    pub sex: u8,
    pub jobs: Vec<usize>,
    pub weapons: Vec<OwnedWeapon>,
    pub passives: Vec<OwnedPassive>,
    pub buffs: Vec<Buff>,
    pub stat_points: [i32; 6],
    pub s: Stats,
    pub safe_until: i32,
    pub hurt_until: i32,
    pub offers: VecDeque<Offer>,
    pub open: Option<Offer>,
    pub open_deadline: i32,
    pub inputs: VecDeque<Input>,
    pub ack_seq: u16,
    pub join_tick: i32,
    pub left_tick: i32,
    pub kills: i32,
    pub elite_kills: i32,
    pub boss_kills: i32,
    pub falls: i32,
    pub revives: i32,
    pub damage_dealt: i64,
    pub healing: i64,
    pub damage_taken: i64,
    pub loadout_version: i32,
}

impl Player {
    fn new(slot: i32) -> Self {
        Player {
            present: false, used: false, left: false, away: false, moving: false, fallen: false, helped: false, idle_chooser: false, slot, aid: 0, name: String::new(),
            x: 0, y: 0, face_deg: 0, vx: 0, vy: 0, accel_per_tick: 1, decel_per_tick: 1, hp: 0, max_hp: 0, speed_per_tick: 0, regen_carry: 0.0, kill_heal_carry: 0.0, revive: 0.0,
            level: 1, job_id: 0, tier: 0, sex: 0, jobs: Vec::new(), weapons: Vec::new(), passives: Vec::new(), buffs: Vec::new(), stat_points: [0; 6], s: Stats::default(),
            safe_until: 0, hurt_until: 0, offers: VecDeque::new(), open: None, open_deadline: 0, inputs: VecDeque::new(), ack_seq: 0, join_tick: 0, left_tick: -1,
            kills: 0, elite_kills: 0, boss_kills: 0, falls: 0, revives: 0, damage_dealt: 0, healing: 0, damage_taken: 0, loadout_version: 0,
        }
    }
    /// SimPlayer.Reset (HordeSim.cs:1317-1324): everything but Slot, Used and Sex; LoadoutVersion counts on.
    fn reset(&mut self) {
        let (slot, used, sex, lv) = (self.slot, self.used, self.sex, self.loadout_version);
        *self = Player::new(slot);
        self.used = used;
        self.sex = sex;
        self.loadout_version = lv + 1;
    }
    /// SimPlayer.Stat(i).
    pub fn stat(&self, i: usize) -> i32 {
        self.stat_points[i] + round(self.s.stats_added[i])
    }
    fn owns_weapon(&self, def: usize) -> bool {
        self.weapons.iter().any(|o| o.def == def)
    }
    fn owns_passive(&self, def: usize) -> bool {
        self.passives.iter().any(|o| o.def == def)
    }
    pub fn owns_weapon_or_root(&self, c: &Content, def: usize) -> bool {
        let w = &c.weapons[def];
        let root = w.root_id();
        self.weapons.iter().any(|o| {
            let ow_def = &c.weapons[o.def];
            o.def == def || ow_def.root_id() == root || (w.group != 0 && ow_def.group == w.group)
        })
    }
    pub fn owns_passive_or_root(&self, c: &Content, def: usize) -> bool {
        let p = &c.passives[def];
        let root = p.root_id();
        self.passives.iter().any(|o| {
            let op_def = &c.passives[o.def];
            o.def == def || op_def.root_id() == root || (p.group != 0 && op_def.group == p.group)
        })
    }
    pub fn count_passive_trans(&self, c: &Content, kind: u8) -> i32 {
        self.passives.iter().filter(|op| c.passives[op.def].trans == kind).count() as i32
    }
}

#[derive(Clone, Debug, Default)]
pub struct TransPlayerState {
    pub aura_pulses: i32,
    pub pet_acts: i32,
    pub dash_end_tick: i32,
    pub dash_end_dmg: i32,
    pub dash_end_widx: Option<usize>,
    pub fortress_next: i32,
    pub archmage_next: i32,
    pub wind_trail_next: i32,
    pub bane_blast_next: i32,
    pub warlord_hits: i32,
    pub deadeye_hits: i32,
    pub harvester_kills: i32,
    pub in_chain_effect: bool,
}

#[derive(Clone, Debug)]
pub struct Enemy {
    pub alive: bool,
    pub slot: i32,
    pub serial: i32,
    pub def: usize,
    pub x: i32,
    pub y: i32,
    pub hp: i32,
    pub max_hp: i32,
    pub damage: i32,
    pub shot_damage: i32,
    pub speed: i32,
    pub radius: i32,
    pub spawn_tick: i32,
    pub next_attack: i32,
    pub shot_next: i32,
    pub slow_until: i32,
    pub slow_pct: i32,
    pub frozen_until: i32,
    pub stun_until: i32,
    pub dot_until: i32,
    pub dot_next: i32,
    pub dot_every: i32,
    pub dot_damage: i32,
    pub dot_slot: i32,
    pub dot_weapon: i32,
    pub charge_next: i32,
    pub wind_until: i32,
    pub dash_until: i32,
    pub dash_vx: i32,
    pub dash_vy: i32,
    pub dash_lock_x: i32,
    pub dash_lock_y: i32,
    pub kb_vx: i32,
    pub kb_vy: i32,
    pub kb_window_tick: i32,
    pub kb_count: i32,
    pub zone_hit_until: i32,
    pub ability_next: Vec<i32>,
    pub flags: u8,
}

impl Enemy {
    fn new(slot: i32) -> Self {
        Enemy {
            alive: false, slot, serial: 0, def: 0, x: 0, y: 0, hp: 0, max_hp: 0, damage: 0, shot_damage: 0, speed: 0, radius: 0, spawn_tick: 0, next_attack: 0, shot_next: 0,
            slow_until: 0, slow_pct: 0, frozen_until: 0, stun_until: 0, dot_until: 0, dot_next: 0, dot_every: 0, dot_damage: 0, dot_slot: -1, dot_weapon: 0,
            charge_next: 0, wind_until: 0, dash_until: 0, dash_vx: 0, dash_vy: 0, dash_lock_x: 0, dash_lock_y: 0, kb_vx: 0, kb_vy: 0, kb_window_tick: 0, kb_count: 0, zone_hit_until: 0, ability_next: Vec::new(), flags: 0,
        }
    }
    /// SimEnemy.Reset (HordeSim.cs:1341-1346): everything but Slot and Serial.
    fn reset(&mut self) {
        let (slot, serial) = (self.slot, self.serial);
        *self = Enemy::new(slot);
        self.serial = serial;
    }
}

#[derive(Clone, Debug)]
pub struct Pickup {
    pub id: i32,
    pub def: usize,
    pub x: i32,
    pub y: i32,
    pub value: i32,
    pub pulled_by: i32,
    pub pull_tick: i32,
}

#[derive(Clone, Debug)]
pub struct Summon {
    pub id: i32,
    pub owner_slot: i32,
    pub x: i32,
    pub y: i32,
    pub hp: i32,
    pub max_hp: i32,
    pub next_attack: i32,
    pub until: i32,
    /// The OwnedWeapon that called it: its index in the owner's weapons.
    pub widx: usize,
}

/// SimFx: a lasting effect (shots, zones, traps, warnings, orbits).
#[derive(Clone, Debug)]
pub struct Fx {
    /// Never reused (C#: the object's identity).
    pub uid: u64,
    pub id: i32,
    pub kind: u8,
    pub owner_slot: i32,
    pub owner_enemy: i32,
    pub def_id: i32,
    pub level: i32,
    pub enemy_def_id: i32,
    pub x: i32,
    pub y: i32,
    pub vx: i32,
    pub vy: i32,
    pub radius: i32,
    pub range: i32,
    pub angle: f64,
    pub turns: f64,
    pub count: i32,
    pub target: i32,
    pub follows: bool,
    pub start_tick: i32,
    pub arm_tick: i32,
    pub end_tick: i32,
    /// Weapon: the OwnedWeapon's index in the owner's weapons (a player's effect only).
    pub widx: usize,
    /// Lv: (weapon index, level index) of the level it was made with.
    pub lv: (usize, usize),
    pub base_damage: f64,
    pub damage: i32,
    pub pierce_left: i32,
    pub next_tick: i32,
    pub every: i32,
    pub heal: i32,
    pub heal_pct: f64,
    pub zone_flags: u8,
    pub burst_area: i32,
    pub hit_list: Vec<i32>,
    pub ended: bool,
}

struct WaveState {
    def: usize,
    from: i32,
    to: i32,
    every: i32,
    next: i32,
    carry: f64,
    done: bool,
    warned: bool,
}

/// Grid (HordeSim.cs:1382-1415): enemies binned by 2 × 2 cells, rebuilt twice a tick. The order a query returns
/// slots in (cells row by row, within a cell the highest slot first) is part of the game: it decides ties.
pub(super) struct Grid {
    x0: i32,
    y0: i32,
    w: i32,
    h: i32,
    head: Vec<i32>,
    next: Vec<i32>,
}

const GRID_SHIFT: i32 = 9; // 512 units = 2 cells

impl Grid {
    fn new(min_x: i32, min_y: i32, max_x: i32, max_y: i32) -> Self {
        let w = ((max_x - min_x) >> GRID_SHIFT) + 1;
        let h = ((max_y - min_y) >> GRID_SHIFT) + 1;
        Grid { x0: min_x, y0: min_y, w, h, head: vec![-1; (w * h) as usize], next: vec![-1; MAX_ENEMIES] }
    }
    fn cx(&self, x: i32) -> i32 {
        let c = (x - self.x0) >> GRID_SHIFT;
        if c < 0 {
            0
        } else if c >= self.w {
            self.w - 1
        } else {
            c
        }
    }
    fn cy(&self, y: i32) -> i32 {
        let c = (y - self.y0) >> GRID_SHIFT;
        if c < 0 {
            0
        } else if c >= self.h {
            self.h - 1
        } else {
            c
        }
    }
    fn build(&mut self, es: &[Enemy]) {
        for h in self.head.iter_mut() {
            *h = -1;
        }
        for (i, e) in es.iter().enumerate() {
            if !e.alive {
                continue;
            }
            let c = (self.cy(e.y) * self.w + self.cx(e.x)) as usize;
            self.next[i] = self.head[c];
            self.head[c] = i as i32;
        }
    }
    /// Every live enemy whose cell is within r of (x, y) (a superset: the caller tests the distance).
    pub(super) fn query(&self, x: i32, y: i32, r: i32, es: &[Enemy], into: &mut Vec<i32>) {
        let (ax, bx, ay, by) = (self.cx(x - r), self.cx(x + r), self.cy(y - r), self.cy(y + r));
        for cy in ay..=by {
            for cx in ax..=bx {
                let mut i = self.head[(cy * self.w + cx) as usize];
                while i >= 0 {
                    if es[i as usize].alive {
                        into.push(i);
                    }
                    i = self.next[i as usize];
                }
            }
        }
    }
}

/// HordePlayerResult.
#[derive(Clone, Debug)]
pub struct PlayerResult {
    pub slot: i32,
    pub aid: u32,
    pub name: String,
    pub level: i32,
    pub job_id: i32,
    pub kills: i32,
    pub elite_kills: i32,
    pub boss_kills: i32,
    pub falls: i32,
    pub revives: i32,
    pub damage: i64,
    pub healing: i64,
    pub seconds: f32,
    pub score: i32,
    pub base_share: i32,
    pub job_share: i32,
    /// HordeRewardState: 0 none, 1 pending, 2 granted.
    pub reward: u8,
    pub left: bool,
}

/// HordeResults.
#[derive(Clone, Debug)]
pub struct Results {
    pub run_id: u32,
    pub outcome: u8,
    pub seconds: f32,
    pub players: Vec<PlayerResult>,
}

pub struct Sim {
    pub c: &'static Content,
    pub arena: ArenaDef,
    pub min_x: i32,
    pub min_y: i32,
    pub max_x: i32,
    pub max_y: i32,
    pub tick: i32,
    pub run_ticks: i32,
    pub final_ticks: i32,
    pub run_seconds: f32,
    pub time_scale: f64,
    pub final_phase: bool,
    pub wave_cycle: i32,
    pub paused: bool,
    pub held_by: i32,
    pub pause_count_by: i32,
    pub pause_count_to_pause: bool,
    pub pause_count_end: i32,
    pub last_hold: u8,
    pub wall_tick: i32,
    pub current_choice_seconds: f32,
    pub choice_limits_met: i32,
    auto_picked_in_pause: bool,
    last_answer_late: bool,
    pub party_level: i32,
    pub party_exp: i32,
    party_exp_carry: f64,
    pub over: bool,
    pub outcome: u8,
    pub end_tick: i32,
    pub players: Vec<Player>,
    pub enemies: Vec<Enemy>,
    pub enemies_alive: i32,
    pub pickups: Vec<Pickup>,
    pub fx: Vec<Fx>,
    pub summons: Vec<Summon>,
    // ---- outputs since the last clear_outputs
    pub events: Vec<Event>,
    pub hits: Vec<Event>,
    /// FxBegun, as uids (the effect itself is in `fx`, or in `fx_removed` once it is gone).
    pub fx_begun: Vec<u64>,
    pub fx_ended: Vec<(i32, bool)>,
    /// The effects taken off the list since the last clear_outputs, in the state they had then.
    pub fx_removed: Vec<Fx>,
    pub offers_opened: Vec<(i32, Offer)>,
    pub offers_closed: Vec<(i32, i32)>,
    pub pickups_added: Vec<i32>,
    pub pickups_removed: Vec<i32>,
    pub pickups_pulled: Vec<i32>,
    pub(super) rng: Rng,
    pub(super) wall_units: i32,
    pub(super) kb_keep_pct: i32,
    pub(super) next_fx_id: i32,
    pub(super) next_fx_uid: u64,
    next_pickup_id: i32,
    pub(super) next_summon_id: i32,
    next_offer_id: i32,
    waves: Vec<WaveState>,
    final_boss_slot: i32,
    final_warned: bool,
    pub(super) grid: Grid,
    pub souls: Vec<Soul>,
    pub soul_total: [i32; MAX_PLAYERS],
    pub soul_need: [i32; MAX_PLAYERS],
    soul_deaths: HashMap<u32, i32>,
    soul_dice: Rng,
    soul_next_id: i32,
    pub ygg_burnt: std::collections::HashSet<u32>,
    pub ygg_until: HashMap<u32, i32>,
    ygg_clear: Vec<(usize, i32)>,
    pub(super) trans: [TransPlayerState; MAX_PLAYERS],
    pub(super) manual_cast_used: [bool; MAX_PLAYERS],
    pub objectives: Vec<SimObjective>,
    pub objective_buffs: Vec<SimObjectiveBuff>,
    obj_dice: Rng,
    obj_next_id: i32,
    obj_next_tick: i32,
    obj_last_kind: u8,
    emperium_seen: bool,
    pub(super) arrow_buff_until: [i32; MAX_PLAYERS],
    pub(super) crit_proc_stacks: [i32; MAX_PLAYERS],
    pub(super) bane_drop_next: [i32; MAX_PLAYERS],
}

/// HordeSim.Arr: a scaling array's entry for this many players (null or empty = 1).
pub(super) fn arr(a: &Option<Vec<f32>>, players: i32) -> f64 {
    match a {
        Some(a) if !a.is_empty() => a[(players.min(a.len() as i32) - 1) as usize] as f64,
        _ => 1.0,
    }
}

/// HordeSim.RankFlags.
fn rank_flags(r: u8) -> u8 {
    match r {
        rank::ELITE => evf::ELITE,
        rank::BOSS => evf::BOSS,
        rank::FINAL_BOSS => evf::BOSS | evf::FINAL,
        _ => 0,
    }
}

impl Sim {
    /// HordeSim's constructor (HordeSim.cs:97-126). `arena`: one of the content's; an arena wider than the wire can
    /// carry is refused (C# throws).
    pub fn new(c: &'static Content, arena: &ArenaDef, seed: u64, run_seconds: f32) -> Result<Sim, String> {
        if arena.x1 - arena.x0 + 1 > MAX_ARENA_CELLS || arena.y1 - arena.y0 + 1 > MAX_ARENA_CELLS || arena.x1 < arena.x0 || arena.y1 < arena.y0 {
            return Err(format!("arena {} {},{}-{},{}: at most {MAX_ARENA_CELLS} cells a side", arena.map, arena.x0, arena.y0, arena.x1, arena.y1));
        }
        let r = &c.rules;
        let (min_x, min_y, max_x, max_y) = (arena.x0 * UNIT, arena.y0 * UNIT, (arena.x1 + 1) * UNIT - 1, (arena.y1 + 1) * UNIT - 1);
        let run_seconds: f32 = if run_seconds > 0.0 { run_seconds } else { r.reference_run_seconds };
        // RunSeconds / R.ReferenceRunSeconds is a float division, widened
        let time_scale: f64 = if r.reference_run_seconds > 0.0 { (run_seconds / r.reference_run_seconds) as f64 } else { 1.0 };
        let run_ticks = 1.max(round((run_seconds * TICK_RATE as f32) as f64));
        let final_ticks = 1.max(round((r.final_boss_seconds * TICK_RATE as f32) as f64));
        let waves = c
            .waves
            .iter()
            .enumerate()
            .map(|(i, w)| WaveState {
                def: i,
                from: ticks(w.from as f64 * time_scale),
                to: if w.once { i32::MAX } else { ticks(w.to as f64 * time_scale) },
                every: 1.max(ticks(w.interval as f64)),
                next: ticks(w.from as f64 * time_scale),
                carry: 0.0,
                done: false,
                warned: false,
            })
            .collect();
        Ok(Sim {
            c,
            arena: arena.clone(),
            min_x,
            min_y,
            max_x,
            max_y,
            tick: 0,
            run_ticks,
            final_ticks,
            run_seconds,
            time_scale,
            final_phase: false,
            wave_cycle: 0,
            paused: false,
            held_by: -1,
            pause_count_by: -1,
            pause_count_to_pause: false,
            pause_count_end: 0,
            last_hold: 0,
            wall_tick: 0,
            current_choice_seconds: r.choice_seconds_min.max(r.choice_seconds),
            choice_limits_met: 0,
            auto_picked_in_pause: false,
            last_answer_late: false,
            party_level: 1,
            party_exp: 0,
            party_exp_carry: 0.0,
            over: false,
            outcome: outcome::NONE,
            end_tick: 0,
            players: (0..MAX_PLAYERS as i32).map(Player::new).collect(),
            enemies: (0..MAX_ENEMIES as i32).map(Enemy::new).collect(),
            enemies_alive: 0,
            pickups: Vec::new(),
            fx: Vec::new(),
            summons: Vec::new(),
            events: Vec::new(),
            hits: Vec::new(),
            fx_begun: Vec::new(),
            fx_ended: Vec::new(),
            fx_removed: Vec::new(),
            offers_opened: Vec::new(),
            offers_closed: Vec::new(),
            pickups_added: Vec::new(),
            pickups_removed: Vec::new(),
            pickups_pulled: Vec::new(),
            rng: Rng::new(seed),
            wall_units: 0.max(units(r.wall_soft_cells as f64)),
            kb_keep_pct: clamp(round(r.knockback_keep_pct as f64), 0, 95),
            next_fx_id: 1,
            next_fx_uid: 1,
            next_pickup_id: 1,
            next_summon_id: 1,
            next_offer_id: 1,
            waves,
            final_boss_slot: -1,
            final_warned: false,
            grid: Grid::new(min_x, min_y, max_x, max_y),
            souls: Vec::new(),
            soul_total: [0; MAX_PLAYERS],
            soul_need: [0; MAX_PLAYERS],
            soul_deaths: HashMap::new(),
            soul_dice: Rng::new(seed ^ 0x50015EA7C0FFEE18),
            soul_next_id: 1,
            ygg_burnt: std::collections::HashSet::new(),
            ygg_until: HashMap::new(),
            ygg_clear: Vec::new(),
            trans: std::array::from_fn(|_| TransPlayerState::default()),
            manual_cast_used: [false; MAX_PLAYERS],
            objectives: Vec::new(),
            objective_buffs: Vec::new(),
            obj_dice: Rng::new(seed ^ 0x0B1EC7175EED2417),
            obj_next_id: 1,
            obj_next_tick: -1,
            obj_last_kind: 0,
            emperium_seen: false,
            arrow_buff_until: [0; MAX_PLAYERS],
            crit_proc_stacks: [0; MAX_PLAYERS],
            bane_drop_next: [0; MAX_PLAYERS],
        })
    }

    pub fn players_present(&self) -> i32 {
        self.players.iter().filter(|p| p.present).count() as i32
    }

    /// ClearOutputs.
    pub fn clear_outputs(&mut self) {
        self.events.clear();
        self.hits.clear();
        self.fx_begun.clear();
        self.fx_ended.clear();
        self.fx_removed.clear();
        self.offers_opened.clear();
        self.offers_closed.clear();
        self.pickups_added.clear();
        self.pickups_removed.clear();
        self.pickups_pulled.clear();
    }

    // =============================================================================================== players

    /// AddPlayer (HordeSim.cs:147-176): a player enters the run (at its start, or late: as a Novice at the party's
    /// level, or rebuilding their previous loadout if rejoining). Returns its slot, or -1 when the run is full or over.
    pub fn add_player(&mut self, aid: u32, name: &str, sex: u8) -> i32 {
        if self.over {
            return -1;
        }
        if let Some(q) = self.players.iter().find(|q| q.present && q.aid == aid) {
            return q.slot;
        }
        let Some(slot) = self.players.iter().position(|q| !q.present && !q.used) else { return -1 };
        let c = self.c;
        let old_state = if c.is_v4() {
            self.players.iter().find(|q| !q.present && q.used && q.aid == aid).map(|old| {
                let ws: Vec<(usize, i32, bool)> = old.weapons.iter().map(|w| (w.def, w.level, w.signature)).collect();
                (old.sex, old.job_id, old.tier, old.jobs.clone(), old.stat_points, ws, old.passives.clone())
            })
        } else {
            None
        };
        let late = self.tick > 0;
        let tick = self.tick;
        let party_level = self.party_level;
        let (cx, cy) = ((self.min_x + self.max_x) / 2, (self.min_y + self.max_y) / 2);
        {
            self.trans[slot] = TransPlayerState::default();
            self.manual_cast_used[slot] = false;
            self.arrow_buff_until[slot] = 0;
            self.crit_proc_stacks[slot] = 0;
            self.bane_drop_next[slot] = 0;
            let p = &mut self.players[slot];
            p.reset();
            p.present = true;
            p.used = true;
            p.aid = aid;
            p.name = name.to_string();
            p.join_tick = tick;
            p.sex = sex;
            let job = c.start_job();
            p.job_id = job.map(|j| c.jobs[j].job_id).unwrap_or(0);
            p.tier = 0;
            if let Some(j) = job {
                p.jobs.push(j);
            }
            p.level = party_level;
            // places: around the arena's centre, one cell apart
            let ang = p.slot as f64 * std::f64::consts::PI / 4.0;
            p.x = clamp(cx + (math::cos(ang) * 1.5 * UNIT as f64) as i32, self.min_x, self.max_x);
            p.y = clamp(cy + (math::sin(ang) * 1.5 * UNIT as f64) as i32, self.min_y, self.max_y);
            if let Some((old_sex, old_job_id, old_tier, old_jobs, old_stats, old_weapons, old_passives)) = old_state {
                if p.sex == 0 {
                    p.sex = old_sex;
                }
                p.job_id = old_job_id;
                p.tier = old_tier;
                p.jobs = old_jobs;
                p.stat_points = old_stats;
                for (def, lvl, sig) in old_weapons {
                    let mut ow = OwnedWeapon::new(def, tick + 1, sig);
                    ow.level = lvl;
                    p.weapons.push(ow);
                }
                p.passives = old_passives;
            } else if let Some(start) = c.weapon(c.rules.start_weapon) {
                if !c.weapons[start].levels.is_empty() {
                    p.weapons.push(OwnedWeapon::new(start, tick + TICK_RATE, false));
                }
            }
        }
        self.recalc(slot, true);
        let safe = tick + ticks(c.rules.revive_safe_seconds as f64);
        let p = &mut self.players[slot];
        p.hp = p.max_hp;
        p.safe_until = safe;
        let (level, x, y) = (p.level, p.x, p.y);
        self.add_event(ev::PLAYER_JOINED, if late { evf::LATE } else { 0 }, slot as i32, -1, 0, level, x, y);
        // a late joiner chooses the jobs whose marks the party has passed, one after the other
        if late {
            self.queue_job_offer(slot);
        }
        slot as i32
    }

    /// RemovePlayer (HordeSim.cs:179-188): the player leaves the run (its result stands as it is now).
    pub fn remove_player(&mut self, slot: i32) {
        let Some(s) = self.player_idx(slot) else { return };
        self.close_offers(s, false);
        let tick = self.tick;
        let p = &mut self.players[s];
        p.present = false;
        p.left = true;
        p.left_tick = tick;
        let (x, y) = (p.x, p.y);
        self.release_hold_of(slot);
        for i in 0..self.fx.len() {
            if self.fx[i].owner_slot == slot && self.fx[i].kind == fxk::ORBIT && self.fx[i].end_tick > tick {
                self.end_fx(i, false);
            }
        }
        self.add_event(ev::PLAYER_LEFT, 0, slot, -1, 0, 0, x, y);
        self.check_end();
    }

    /// Player(slot): the slot's index when a player is present there.
    pub fn player_idx(&self, slot: i32) -> Option<usize> {
        if slot >= 0 && (slot as usize) < self.players.len() && self.players[slot as usize].present {
            Some(slot as usize)
        } else {
            None
        }
    }

    /// PushInput (HordeSim.cs:194-201): one tick of the player's stick.
    pub fn push_input(&mut self, slot: i32, seq: u16, dx: i32, dy: i32) {
        let Some(s) = self.player_idx(slot) else { return };
        let p = &mut self.players[s];
        if p.inputs.len() >= 40 {
            p.inputs.pop_front();
        }
        p.inputs.push_back(Input { seq, dx: clamp(dx, -127, 127) as i8, dy: clamp(dy, -127, 127) as i8 });
        p.away = false;
    }

    /// SetAway.
    pub fn set_away(&mut self, slot: i32, away: bool) {
        if let Some(s) = self.player_idx(slot) {
            let p = &mut self.players[s];
            p.away = away;
            if away {
                p.inputs.clear();
            }
        }
    }

    /// Choose (HordeSim.cs:206-216): the player's choice for an open offer (ignored when that offer is not the open one).
    pub fn choose(&mut self, slot: i32, offer_id: i32, index: i32) -> bool {
        let Some(s) = self.player_idx(slot) else { return false };
        let Some(open) = self.players[s].open.clone() else { return false };
        if open.id != offer_id {
            return false;
        }
        let cards = open.cards.clone().unwrap_or_default();
        let index = if index < 0 || index as usize >= cards.len() { 0 } else { index as usize };
        self.players[s].idle_chooser = false;
        // an answer within the window's last second counts as the limit met, if it is the one that ends the pause (Step decides)
        self.last_answer_late = self.wall_tick >= self.players[s].open_deadline - ticks(1.0);
        self.apply_card(s, open.id, cards[index]);
        true
    }

    pub fn pause_count_ticks_left(&self) -> i32 {
        if self.pause_count_by >= 0 { 0.max(self.pause_count_end - self.wall_tick) } else { 0 }
    }

    /// Hold (HordeSim.Rules.cs:206-254): manual pause / unpause with 3 s co-op countdown.
    pub fn hold(&mut self, slot: i32, on: bool) {
        self.last_hold = 0;
        if self.over || self.player_idx(slot).is_none() {
            return;
        }
        let solo = self.players_present() <= 1;
        if on {
            if self.held_by >= 0 {
                return;
            }
            if solo {
                self.held_by = slot;
                self.pause_count_by = -1;
                self.paused = true;
                self.last_hold = 1;
                return;
            }
            if self.pause_count_by == slot && self.pause_count_to_pause {
                self.pause_count_by = -1;
                self.last_hold = 4;
                return;
            }
            self.pause_count_by = slot;
            self.pause_count_to_pause = true;
            self.pause_count_end = self.wall_tick + 3 * TICK_RATE;
            self.last_hold = 2;
        } else {
            if self.pause_count_by >= 0 && self.pause_count_to_pause {
                self.pause_count_by = -1;
                self.last_hold = 4;
                return;
            }
            if self.held_by < 0 {
                return;
            }
            if solo {
                self.held_by = -1;
                self.pause_count_by = -1;
                self.paused = self.players.iter().any(|p| p.present && p.open.is_some());
                self.last_hold = 1;
                return;
            }
            if self.pause_count_by == slot && !self.pause_count_to_pause {
                self.pause_count_by = -1;
                self.last_hold = 4;
                return;
            }
            self.pause_count_by = slot;
            self.pause_count_to_pause = false;
            self.pause_count_end = self.wall_tick + 3 * TICK_RATE;
            self.last_hold = 3;
        }
    }

    fn step_pause_count(&mut self) {
        if self.pause_count_by < 0 {
            return;
        }
        if self.player_idx(self.pause_count_by).is_none() {
            self.pause_count_by = -1;
            return;
        }
        if self.wall_tick < self.pause_count_end {
            return;
        }
        if self.pause_count_to_pause {
            self.held_by = self.pause_count_by;
        } else {
            self.held_by = -1;
        }
        self.pause_count_by = -1;
    }

    pub fn release_hold_of(&mut self, slot: i32) {
        if self.pause_count_by == slot {
            self.pause_count_by = -1;
        }
        if self.held_by == slot {
            self.held_by = -1;
        }
        if self.held_by >= 0 && self.players_present() == 0 {
            self.held_by = -1;
        }
    }

    // =============================================================================================== the tick

    /// Step (HordeSim.cs:220-248).
    pub fn step(&mut self) {
        if self.over {
            return;
        }
        self.wall_tick += 1;
        self.step_pause_count();
        self.step_offers();
        let was_paused = self.paused;
        let choosing = self.players.iter().any(|p| p.present && p.open.is_some());
        if self.held_by >= 0 && self.player_idx(self.held_by).is_none() {
            self.held_by = -1;
        }
        self.paused = choosing || self.held_by >= 0;
        if was_paused && !self.paused {
            // the pause ended: was its limit met (the host picked, or the last answer came in the window's last second)?
            if self.auto_picked_in_pause || self.last_answer_late {
                self.choice_limits_met += 1;
                let r = &self.c.rules;
                self.current_choice_seconds = r.choice_seconds_min.max(self.current_choice_seconds - 0f32.max(r.choice_seconds_step));
            }
            self.auto_picked_in_pause = false;
            self.last_answer_late = false;
        }
        if !was_paused && self.paused {
            self.auto_picked_in_pause = false;
            self.last_answer_late = false;
        }
        if self.paused {
            return; // the world waits while someone chooses
        }
        self.tick += 1;
        self.step_players();
        self.step_waves();
        self.grid.build(&self.enemies);
        self.step_enemies();
        self.grid.build(&self.enemies);
        self.step_weapons();
        self.step_fx();
        self.step_enemy_effects();
        self.step_pickups();
        if self.c.is_v4() {
            self.step_souls();
            self.step_objectives();
        } else {
            self.step_revive();
        }
        self.check_end();
    }

    /// StepPlayers (HordeSim.cs:250-282).
    fn step_players(&mut self) {
        let c = self.c;
        for s in 0..self.players.len() {
            if !self.players[s].present {
                continue;
            }
            let (wall, min_x, min_y, max_x, max_y, tick) = (self.wall_units, self.min_x, self.min_y, self.max_x, self.max_y, self.tick);
            let p = &mut self.players[s];
            if p.fallen && c.is_v4() {
                while let Some(inp) = p.inputs.pop_front() {
                    p.ack_seq = inp.seq;
                }
                p.vx = 0;
                p.vy = 0;
                p.moving = false;
                continue;
            }
            let n = p.inputs.len();
            let steps = if n > 6 { 3 } else if n > 2 { 2 } else if n > 0 { 1 } else { 0 };
            for _ in 0..steps {
                let inp = p.inputs.pop_front().unwrap();
                p.ack_seq = inp.seq;
                if p.fallen {
                    p.vx = 0;
                    p.vy = 0;
                    continue;
                }
                let (dx, dy) = (inp.dx as i32, inp.dy as i32);
                math::step_ramp(&mut p.x, &mut p.y, &mut p.vx, &mut p.vy, dx, dy, p.speed_per_tick, p.accel_per_tick, p.decel_per_tick, wall, min_x, min_y, max_x, max_y);
                if dx != 0 || dy != 0 {
                    p.face_deg = round(math::atan2(dy as f64, dx as f64) * 180.0 / std::f64::consts::PI);
                }
            }
            p.moving = steps > 0 && !p.fallen && (p.vx != 0 || p.vy != 0);
            if p.fallen {
                continue;
            }
            // regeneration
            if p.s.regen > 0.0 && p.hp < p.max_hp {
                p.regen_carry += p.s.regen / TICK_RATE as f64;
                if p.regen_carry >= 1.0 {
                    let h = p.regen_carry as i32;
                    p.regen_carry -= h as f64;
                    p.hp = p.max_hp.min(p.hp + h);
                }
            }
            // buffs that ran out
            let before = p.buffs.len();
            let mut i = p.buffs.len();
            while i > 0 {
                i -= 1;
                if p.buffs[i].until <= tick {
                    p.buffs.remove(i);
                }
            }
            let mut need_recalc = p.buffs.len() != before;
            if self.arrow_buff_until[s] > 0 && self.arrow_buff_until[s] <= tick {
                self.arrow_buff_until[s] = 0;
                need_recalc = true;
            }
            if need_recalc {
                self.recalc(s, false);
            }
            if c.is_v4() {
                self.step_trans_player(s);
            }
        }
        let _ = c;
    }

    /// StepOffers (HordeSim.cs:288-296): every Step, paused or not.
    fn step_offers(&mut self) {
        for s in 0..self.players.len() {
            if !self.players[s].present {
                continue;
            }
            if self.players[s].open.is_some() && self.wall_tick >= self.players[s].open_deadline {
                let open = self.players[s].open.clone().unwrap();
                self.players[s].idle_chooser = true;
                self.auto_picked_in_pause = true;
                let first = open.cards.as_ref().map(|c| c[0]).unwrap();
                self.apply_card(s, open.id, first);
            }
            if self.players[s].open.is_none() && !self.players[s].offers.is_empty() {
                self.open_next(s);
            }
        }
    }

    // =============================================================================================== waves

    pub(super) fn players_for_scaling(&self) -> i32 {
        clamp(self.players_present(), 1, 8)
    }

    /// StepWaves (HordeSim.cs:302-342).
    fn step_waves(&mut self) {
        let c = self.c;
        let r = &c.rules;
        let (cx, cy) = ((self.min_x + self.max_x) / 2, (self.min_y + self.max_y) / 2);
        let cycle_len = 1.max(self.run_ticks);
        let next_milestone = if c.is_v4() { (self.wave_cycle + 1) * cycle_len } else { self.run_ticks };
        // the announcements: the final boss and Once waves say they come, WarnSeconds before
        if !self.final_warned
            && (!self.final_phase || c.is_v4())
            && r.final_boss_warn_seconds > 0.0
            && self.tick >= next_milestone - ticks(r.final_boss_warn_seconds as f64)
            && self.tick < next_milestone
            && c.enemy(r.final_boss_enemy_id).is_some()
        {
            self.final_warned = true;
            let left = 1.max(round((next_milestone - self.tick) as f64 / TICK_RATE as f64));
            self.add_event(ev::INCOMING, evf::BOSS | evf::FINAL, -1, -1, r.final_boss_enemy_id, left, cx, cy);
        }
        if c.is_v4() {
            if self.tick >= next_milestone {
                let base_tick = (self.wave_cycle + 1) * cycle_len;
                self.wave_cycle += 1;
                self.final_warned = false;
                self.final_phase = true;
                for wi in 0..self.waves.len() {
                    let wd = &c.waves[self.waves[wi].def];
                    let endgame_wave = !wd.once && r.reference_run_seconds > 0.0 && wd.to >= r.reference_run_seconds - 10.0;
                    if endgame_wave {
                        self.waves[wi].done = false;
                        self.waves[wi].to = i32::MAX;
                        if self.waves[wi].next < self.tick {
                            self.waves[wi].next = self.tick + 1;
                        }
                    } else {
                        self.waves[wi].from = base_tick + ticks(wd.from as f64 * self.time_scale);
                        self.waves[wi].to = if wd.once { i32::MAX } else { base_tick + ticks(wd.to as f64 * self.time_scale) };
                        self.waves[wi].next = self.waves[wi].from;
                        self.waves[wi].done = false;
                        self.waves[wi].warned = false;
                    }
                }
                self.add_event(ev::FINAL_PHASE, evf::FINAL, -1, -1, r.final_boss_enemy_id, r.final_boss_seconds as i32, cx, cy);
                if let Some(fb) = c.enemy(r.final_boss_enemy_id) {
                    self.final_boss_slot = self.spawn_one(fb, true);
                }
            }
            if self.final_phase {
                let boss_dead = self.final_boss_slot < 0 || !self.enemies[self.final_boss_slot as usize].alive;
                let phase_expired = self.tick >= self.wave_cycle * cycle_len + self.final_ticks;
                if boss_dead || phase_expired {
                    self.final_phase = false;
                }
            }
            // Item 56: past 15:00 in endless mode, spawn an additional boss every 90 s
            if self.wave_cycle > 0 && self.tick > self.run_ticks && (self.tick - self.run_ticks) % (90 * TICK_RATE) == 0 {
                if let Some(fb) = c.enemy(r.final_boss_enemy_id) {
                    self.spawn_one(fb, true);
                }
            }
        } else if !self.final_phase && self.tick >= self.run_ticks {
            self.final_phase = true;
            self.add_event(ev::FINAL_PHASE, evf::FINAL, -1, -1, r.final_boss_enemy_id, r.final_boss_seconds as i32, cx, cy);
            if let Some(fb) = c.enemy(r.final_boss_enemy_id) {
                self.final_boss_slot = self.spawn_one(fb, true);
            }
        }
        let n = self.players_for_scaling();
        let soul_mul = if c.is_v4() { self.soul_spawn_mul() } else { 1.0 };
        for wi in 0..self.waves.len() {
            if self.waves[wi].done {
                continue;
            }
            let wd = &c.waves[self.waves[wi].def];
            if wd.once && !self.waves[wi].warned && wd.warn_seconds > 0.0 && self.tick >= self.waves[wi].next - ticks(wd.warn_seconds as f64) {
                self.waves[wi].warned = true;
                if let Some(e) = c.enemy(wd.enemy_id) {
                    let left = 1.max(round((self.waves[wi].next - self.tick) as f64 / TICK_RATE as f64));
                    self.add_event(ev::INCOMING, rank_flags(c.enemies[e].rank), -1, -1, c.enemies[e].id, left, cx, cy);
                }
            }
            if self.tick < self.waves[wi].next {
                continue;
            }
            let Some(def) = c.enemy(wd.enemy_id) else {
                self.waves[wi].done = true;
                continue;
            };
            if wd.once {
                // exactly Count (client wave 9; wave 8 spawned one)
                self.waves[wi].done = true;
                self.spawn_group(def, 1.max(wd.count), wd.pattern, true);
                continue;
            }
            let endless = c.is_v4() && (self.final_phase || self.wave_cycle > 0) && wd.to >= r.reference_run_seconds - 10.0;
            if !endless && self.tick >= self.waves[wi].to {
                self.waves[wi].done = true;
                continue;
            }
            self.waves[wi].next = self.tick + self.waves[wi].every;
            let endless_mul = if endless { 1.0 + 3.0f64.min(0.max(self.tick - self.run_ticks) as f64 / (TICK_RATE as f64 * 120.0)) } else { 1.0 };
            let want = wd.count as f64 * arr(&c.scaling.spawn_mul, n) * soul_mul * endless_mul + self.waves[wi].carry;
            let mut count = want as i32;
            self.waves[wi].carry = want - count as f64;
            if wd.max_alive > 0 {
                let alive = self.enemies.iter().filter(|e| e.alive && e.def == def).count() as i32;
                count = count.min(0.max(wd.max_alive - alive));
            }
            self.spawn_group(def, count, wd.pattern, false);
        }
    }

    /// RandomStanding: a random standing player's slot (the first present one when nobody stands).
    fn random_standing(&mut self) -> Option<usize> {
        let n = self.players.iter().filter(|p| p.present && !p.fallen).count() as i32;
        if n == 0 {
            return self.players.iter().position(|p| p.present);
        }
        let k = self.rng.next(n);
        self.players.iter().enumerate().filter(|(_, p)| p.present && !p.fallen).nth(k as usize).map(|(i, _)| i)
    }

    /// SpawnGroup (HordeSim.cs:355-396).
    fn spawn_group(&mut self, def: usize, count: i32, pat: u8, force: bool) {
        if count <= 0 {
            return;
        }
        let Some(around) = self.random_standing() else { return };
        let pi = std::f64::consts::PI;
        let base_ang = self.rng.next_double() * pi * 2.0;
        for i in 0..count {
            if !force && self.enemies_alive >= self.c.rules.max_enemies.min(MAX_ENEMIES as i32) {
                return;
            }
            let (ax, ay) = (self.players[around].x, self.players[around].y);
            let dist = 11.0 + self.rng.next_double() * 3.0; // just out of a table's usual sight (cells)
            let (x, y);
            match pat {
                pattern::CLUSTER => {
                    let a = base_ang + (self.rng.next_double() - 0.5) * 0.6;
                    x = ax + units(math::cos(a) * dist);
                    y = ay + units(math::sin(a) * dist);
                }
                pattern::CIRCLE => {
                    let a = base_ang + i as f64 * pi * 2.0 / count as f64;
                    x = ax + units(math::cos(a) * 9.0);
                    y = ay + units(math::sin(a) * 9.0);
                }
                pattern::EDGE => {
                    let side = self.rng.next(4);
                    let t = self.rng.next_double();
                    x = if side == 0 { self.min_x } else if side == 1 { self.max_x } else { self.min_x + ((self.max_x - self.min_x) as f64 * t) as i32 };
                    y = if side == 2 { self.min_y } else if side == 3 { self.max_y } else { self.min_y + ((self.max_y - self.min_y) as f64 * t) as i32 };
                }
                _ => {
                    let a = self.rng.next_double() * pi * 2.0;
                    x = ax + units(math::cos(a) * dist);
                    y = ay + units(math::sin(a) * dist);
                }
            }
            self.spawn(def, clamp(x, self.min_x, self.max_x), clamp(y, self.min_y, self.max_y), force);
        }
    }

    /// SpawnOne (HordeSim.cs:398-410): the final boss.
    fn spawn_one(&mut self, def: usize, force: bool) -> i32 {
        let mut x = (self.min_x + self.max_x) / 2;
        let mut y = self.max_y;
        if let Some(around) = self.random_standing() {
            let a = self.rng.next_double() * std::f64::consts::PI * 2.0;
            x = clamp(self.players[around].x + units(math::cos(a) * 10.0), self.min_x, self.max_x);
            y = clamp(self.players[around].y + units(math::sin(a) * 10.0), self.min_y, self.max_y);
        }
        self.spawn(def, x, y, force)
    }

    /// Spawn (HordeSim.cs:413-446): an enemy at (x, y) units. Returns its slot or -1 (no slot free). `def` is the
    /// enemy's index in the tables.
    pub fn spawn(&mut self, def: usize, x: i32, y: i32, _force: bool) -> i32 {
        let Some(slot) = self.enemies.iter().position(|e| !e.alive) else { return -1 };
        let c = self.c;
        let d = &c.enemies[def];
        let n = self.players_for_scaling();
        let minutes = self.tick as f64 / TICK_RATE as f64 / 60.0 / (if self.time_scale > 0.0 { self.time_scale } else { 1.0 });
        let (late_hp, late_dmg) = if c.is_v4() {
            let late = 0.0f64.max(minutes - 15.0);
            let after20 = 0.0f64.max(minutes - 20.0);
            let exp20_hp = if after20 > 0.0 { 1.18f64.powf(after20.min(60.0)) } else { 1.0 };
            let exp20_dmg = if after20 > 0.0 { 1.12f64.powf(after20.min(60.0)) } else { 1.0 };
            ((1.0 + late * 0.35 + late * late * 0.04) * exp20_hp, (1.0 + late * 0.20 + late * late * 0.02) * exp20_dmg)
        } else {
            (1.0, 1.0)
        };
        let hp_mul = if d.rank == rank::NORMAL {
            arr(&c.scaling.enemy_hp_mul, n) * (1.0 + c.scaling.enemy_hp_pct_per_minute as f64 / 100.0 * minutes) * late_hp
        } else if d.rank == rank::ELITE {
            arr(&c.scaling.elite_hp_mul, n) * late_hp
        } else {
            arr(&c.scaling.boss_hp_mul, n) * late_hp
        };
        let dmg_mul = arr(&c.scaling.enemy_damage_mul, n) * (if d.rank == rank::NORMAL { 1.0 + c.scaling.enemy_damage_pct_per_minute as f64 / 100.0 * minutes } else { 1.0 }) * late_dmg;
        let tick = self.tick;
        {
            let e = &mut self.enemies[slot];
            e.reset();
            e.alive = true;
            e.serial = (e.serial + 1) & 0xff;
            e.def = def;
            e.x = x;
            e.y = y;
            e.max_hp = 1.max(round(d.hp as f64 * hp_mul));
            e.hp = e.max_hp;
            e.damage = 0.max(round(d.damage as f64 * dmg_mul));
            e.shot_damage = 0.max(round(d.shot_damage as f64 * dmg_mul));
            e.speed = 1.max(units((d.speed / TICK_RATE as f32) as f64)); // float / int: a single-precision division
            e.radius = 16.max(units(d.radius as f64));
            e.spawn_tick = tick;
            e.next_attack = tick;
            e.charge_next = tick + ticks(d.charge_cooldown as f64);
            e.shot_next = tick + ticks(d.attack_interval as f64);
            e.ability_next = d.abilities.iter().map(|a| tick + ticks(a.first_after as f64)).collect();
        }
        if d.behavior == behavior::SWEEP {
            let t = self.random_standing();
            let a = match t {
                Some(t) => math::atan2((self.players[t].y - y) as f64, (self.players[t].x - x) as f64),
                None => 0.0,
            };
            let (dvx, dvy, rad) = {
                let e = &mut self.enemies[slot];
                e.dash_vx = (math::cos(a) * e.speed as f64) as i32;
                e.dash_vy = (math::sin(a) * e.speed as f64) as i32;
                (e.dash_vx, e.dash_vy, e.radius)
            };
            if c.is_v4() {
                let w = 0.5f32.max(rad as f32 * 2.0 / UNIT as f32);
                let x2 = clamp(x + dvx * 40, self.min_x, self.max_x);
                let y2 = clamp(y + dvy * 40, self.min_y, self.max_y);
                self.add_event_abc(ev::WIND_UP, 0, -1, slot as i32, d.id, 1, x, y, 0.35, w, 0.0, x2, y2);
            }
        }
        self.enemies_alive += 1;
        if d.rank != rank::NORMAL {
            self.add_event(ev::BOSS_APPEARED, rank_flags(d.rank), -1, slot as i32, d.id, 0, x, y);
        }
        slot as i32
    }

    // =============================================================================================== enemies

    /// NearestStanding: (slot, squared distance) of the nearest standing player.
    pub(super) fn nearest_standing(&self, x: i32, y: i32) -> (Option<usize>, i64) {
        let mut best = None;
        let mut d2 = i64::MAX;
        for (i, p) in self.players.iter().enumerate() {
            if !p.present || p.fallen {
                continue;
            }
            let d = dist2(p.x, p.y, x, y);
            if d < d2 {
                d2 = d;
                best = Some(i);
            }
        }
        (best, d2)
    }

    /// Toward (HordeSim.cs:601-609).
    fn toward(&self, ei: usize, t: Option<usize>, speed: i32) -> (i32, i32) {
        let Some(t) = t else { return (0, 0) };
        let e = &self.enemies[ei];
        let dx = (self.players[t].x - e.x) as i64;
        let dy = (self.players[t].y - e.y) as i64;
        let d = isqrt(dx * dx + dy * dy) as i64;
        if d < 4 {
            return (0, 0);
        }
        ((dx * speed as i64 / d) as i32, (dy * speed as i64 / d) as i32)
    }

    /// StepEnemies (HordeSim.cs:465-599).
    fn step_enemies(&mut self) {
        let c = self.c;
        let r = &c.rules;
        let mut neigh: Vec<i32> = Vec::with_capacity(32);
        let body = units(r.body_radius as f64);
        for ei in 0..self.enemies.len() {
            if !self.enemies[ei].alive {
                continue;
            }
            let tick = self.tick;
            let def = &c.enemies[self.enemies[ei].def];
            let (target, d2) = self.nearest_standing(self.enemies[ei].x, self.enemies[ei].y);
            let (min_x, min_y, max_x, max_y, kb_keep) = (self.min_x, self.min_y, self.max_x, self.max_y, self.kb_keep_pct);
            let held;
            let speed;
            {
                let e = &mut self.enemies[ei];
                held = e.frozen_until > tick || e.stun_until > tick;
                speed = if e.slow_until > tick { 1.max(e.speed * (100 - e.slow_pct) / 100) } else { e.speed };
                e.flags = 0;
                if e.slow_until > tick {
                    e.flags |= ef::SLOWED;
                }
                if e.frozen_until > tick {
                    e.flags |= ef::FROZEN;
                }
                if e.stun_until > tick {
                    e.flags |= ef::STUNNED;
                }
                if e.dot_until > tick {
                    e.flags |= ef::HURTING;
                }
                // a knockback is an impulse that dies down over ticks (it moves a held enemy too)
                if e.kb_vx != 0 || e.kb_vy != 0 {
                    e.flags |= ef::KNOCKED;
                    e.x = clamp(e.x + e.kb_vx, min_x, max_x);
                    e.y = clamp(e.y + e.kb_vy, min_y, max_y);
                    math::decay(&mut e.kb_vx, &mut e.kb_vy, kb_keep);
                }
            }
            if held {
                continue;
            }
            let (mut vx, mut vy) = (0, 0);
            match def.behavior {
                behavior::SWEEP => {
                    let e = &self.enemies[ei];
                    vx = e.dash_vx;
                    vy = e.dash_vy;
                    if e.x + vx <= min_x || e.x + vx >= max_x || e.y + vy <= min_y || e.y + vy >= max_y {
                        self.kill_quiet(ei);
                        continue;
                    }
                }
                behavior::STILL => {}
                behavior::CHARGE => {
                    let (dash_until, wind_until, charge_next) = (self.enemies[ei].dash_until, self.enemies[ei].wind_until, self.enemies[ei].charge_next);
                    if dash_until > tick {
                        let e = &mut self.enemies[ei];
                        vx = e.dash_vx;
                        vy = e.dash_vy;
                        e.flags |= ef::CHARGING;
                    } else if wind_until > tick {
                        self.enemies[ei].flags |= ef::WINDING;
                    } else if wind_until != 0 && wind_until == tick {
                        if c.is_v4() {
                            let (tx, ty) = {
                                let e = &mut self.enemies[ei];
                                if e.dash_lock_x != 0 || e.dash_lock_y != 0 {
                                    let (lx, ly) = (e.dash_lock_x, e.dash_lock_y);
                                    e.dash_lock_x = 0;
                                    e.dash_lock_y = 0;
                                    (lx, ly)
                                } else if let Some(t) = target {
                                    (self.players[t].x, self.players[t].y)
                                } else {
                                    (e.x + UNIT, e.y)
                                }
                            };
                            let e = &mut self.enemies[ei];
                            let a = math::atan2((ty - e.y) as f64, (tx - e.x) as f64);
                            let cs = units((def.charge_speed / TICK_RATE as f32) as f64);
                            e.dash_vx = (math::cos(a) * cs as f64) as i32;
                            e.dash_vy = (math::sin(a) * cs as f64) as i32;
                            e.dash_until = tick + ticks(1.0);
                        } else if let Some(t) = target {
                            let (tx, ty) = (self.players[t].x, self.players[t].y);
                            let e = &mut self.enemies[ei];
                            let a = math::atan2((ty - e.y) as f64, (tx - e.x) as f64);
                            let cs = units((def.charge_speed / TICK_RATE as f32) as f64);
                            e.dash_vx = (math::cos(a) * cs as f64) as i32;
                            e.dash_vy = (math::sin(a) * cs as f64) as i32;
                            e.dash_until = tick + ticks(1.0);
                        }
                        self.enemies[ei].wind_until = 0;
                    } else if tick >= charge_next && target.is_some() && d2 < units(8.0) as i64 * units(8.0) as i64 {
                        let wu = (if c.is_v4() { 14 } else { 1 }).max(ticks(def.charge_windup as f64));
                        let (ex, ey, erad) = {
                            let e = &mut self.enemies[ei];
                            e.wind_until = tick + wu;
                            e.charge_next = tick + ticks(def.charge_cooldown as f64);
                            (e.x, e.y, e.radius)
                        };
                        if c.is_v4() {
                            let t = target.unwrap();
                            let (tx, ty) = (self.players[t].x, self.players[t].y);
                            let a = math::atan2((ty - ey) as f64, (tx - ex) as f64);
                            let dist = units(6.0f32.max(def.charge_speed) as f64);
                            let lx = clamp(ex + round(math::cos(a) * dist as f64), min_x, max_x);
                            let ly = clamp(ey + round(math::sin(a) * dist as f64), min_y, max_y);
                            self.enemies[ei].dash_lock_x = lx;
                            self.enemies[ei].dash_lock_y = ly;
                            let secs = 0.35f32.max(wu as f32 / TICK_RATE as f32);
                            let w = 0.7f32.max(erad as f32 * 2.2 / UNIT as f32);
                            self.add_event_abc(ev::WIND_UP, rank_flags(def.rank), -1, ei as i32, def.id, 1, ex, ey, secs, w, 0.0, lx, ly);
                        }
                    } else {
                        (vx, vy) = self.toward(ei, target, speed);
                    }
                }
                behavior::RANGED => {
                    let sr = units(def.shot_range as f64) as i64;
                    if target.is_some() && def.shot_range > 0.0 && d2 <= sr * sr {
                        let t = target.unwrap();
                        if c.is_v4() && self.enemies[ei].shot_damage > 0 && self.enemies[ei].shot_next - tick == 10 {
                            let (ex, ey) = (self.enemies[ei].x, self.enemies[ei].y);
                            let (tx, ty) = (self.players[t].x, self.players[t].y);
                            let a = math::atan2((ty - ey) as f64, (tx - ex) as f64);
                            let r_u = units(if def.shot_range > 0.0 { def.shot_range as f64 } else { 8.0 });
                            let x2 = clamp(ex + round(math::cos(a) * r_u as f64), min_x, max_x);
                            let y2 = clamp(ey + round(math::sin(a) * r_u as f64), min_y, max_y);
                            self.add_event_abc(ev::WIND_UP, rank_flags(def.rank), -1, ei as i32, def.id, 2, ex, ey, 10.0 / TICK_RATE as f32, 0.35, 0.0, x2, y2);
                        }
                        if self.enemies[ei].shot_damage > 0 && tick >= self.enemies[ei].shot_next {
                            self.enemies[ei].shot_next = tick + 1.max(ticks(def.attack_interval as f64));
                            let (tx, ty) = (self.players[t].x, self.players[t].y);
                            self.enemy_shoot(ei, tx, ty);
                        }
                        // it keeps its distance: away from a player nearer than KeepRange
                        let kr = units(def.keep_range as f64) as i64;
                        if def.keep_range > 0.0 && d2 < kr * kr {
                            let (tvx, tvy) = self.toward(ei, target, speed);
                            vx = -tvx;
                            vy = -tvy;
                        }
                    } else {
                        (vx, vy) = self.toward(ei, target, speed);
                    }
                }
                _ => {
                    (vx, vy) = self.toward(ei, target, speed);
                }
            }
            if def.behavior == behavior::STILL && self.enemies[ei].shot_damage > 0 && target.is_some() {
                let sr = units(def.shot_range as f64) as i64;
                if def.shot_range <= 0.0 || d2 <= sr * sr {
                    let t = target.unwrap();
                    if c.is_v4() && self.enemies[ei].shot_next - tick == 10 {
                        let (ex, ey) = (self.enemies[ei].x, self.enemies[ei].y);
                        let (tx, ty) = (self.players[t].x, self.players[t].y);
                        let a = math::atan2((ty - ey) as f64, (tx - ex) as f64);
                        let r_u = units(if def.shot_range > 0.0 { def.shot_range as f64 } else { 8.0 });
                        let x2 = clamp(ex + round(math::cos(a) * r_u as f64), min_x, max_x);
                        let y2 = clamp(ey + round(math::sin(a) * r_u as f64), min_y, max_y);
                        self.add_event_abc(ev::WIND_UP, rank_flags(def.rank), -1, ei as i32, def.id, 2, ex, ey, 10.0 / TICK_RATE as f32, 0.35, 0.0, x2, y2);
                    }
                    if tick >= self.enemies[ei].shot_next {
                        self.enemies[ei].shot_next = tick + 1.max(ticks(def.attack_interval as f64));
                        let (tx, ty) = (self.players[t].x, self.players[t].y);
                        self.enemy_shoot(ei, tx, ty);
                    }
                }
            }
            // keep apart from neighbours and from the players' bodies (soft separation); bosses are not pushed by
            // anyone, sweeping packs fly straight through
            if def.behavior != behavior::SWEEP && def.rank < rank::BOSS {
                neigh.clear();
                let (ex, ey, erad, eslot) = (self.enemies[ei].x, self.enemies[ei].y, self.enemies[ei].radius, self.enemies[ei].slot);
                self.grid.query(ex, ey, erad * 2, &self.enemies, &mut neigh);
                let (mut push_x, mut push_y, mut checked) = (0i32, 0i32, 0);
                for &j in &neigh {
                    if j == eslot {
                        continue;
                    }
                    let o = &self.enemies[j as usize];
                    let min_d = (erad + o.radius) * 3 / 4;
                    let mut dx = (ex - o.x) as i64;
                    let mut dy = (ey - o.y) as i64;
                    let mut dd = dx * dx + dy * dy;
                    if dd >= min_d as i64 * min_d as i64 {
                        continue;
                    }
                    if dd == 0 {
                        dx = if (eslot & 1) == 0 { 1 } else { -1 };
                        dy = if (eslot & 2) == 0 { 1 } else { -1 };
                        dd = 2;
                    }
                    let dist = isqrt(dd);
                    let overlap = (min_d - dist) as i64;
                    push_x = push_x.wrapping_add((dx * overlap / 1.max(dist) as i64 / 2) as i32);
                    push_y = push_y.wrapping_add((dy * overlap / 1.max(dist) as i64 / 2) as i32);
                    checked += 1;
                    if checked >= 8 {
                        break;
                    }
                }
                for q in &self.players {
                    if !q.present || q.fallen {
                        continue;
                    }
                    let min_d = (erad + body) * 3 / 5; // still within touching reach: a crowd can hurt, not stack
                    let mut dx = (ex - q.x) as i64;
                    let mut dy = (ey - q.y) as i64;
                    let mut dd = dx * dx + dy * dy;
                    if dd >= min_d as i64 * min_d as i64 {
                        continue;
                    }
                    if dd == 0 {
                        dx = if (eslot & 1) == 0 { 1 } else { -1 };
                        dy = if (eslot & 2) == 0 { 1 } else { -1 };
                        dd = 2;
                    }
                    let dist = isqrt(dd);
                    let overlap = (min_d - dist) as i64;
                    push_x = push_x.wrapping_add((dx * overlap / 1.max(dist) as i64) as i32);
                    push_y = push_y.wrapping_add((dy * overlap / 1.max(dist) as i64) as i32);
                }
                let max_push = speed.max(8);
                vx += clamp(push_x, -max_push, max_push);
                vy += clamp(push_y, -max_push, max_push);
            }
            let mut nx = clamp(self.enemies[ei].x + vx, min_x, max_x);
            let mut ny = clamp(self.enemies[ei].y + vy, min_y, max_y);
            if def.rank < rank::BOSS {
                self.block_by_zones(ei, &mut nx, &mut ny);
            }
            self.enemies[ei].x = nx;
            self.enemies[ei].y = ny;
            // abilities
            for ai in 0..def.abilities.len() {
                if c.is_v4() && def.abilities[ai].kind == ability::CHARGE && self.enemies[ei].ability_next[ai] - tick == 16 {
                    if let (Some(t), _) = self.nearest_standing(self.enemies[ei].x, self.enemies[ei].y) {
                        let (ex, ey, erad) = (self.enemies[ei].x, self.enemies[ei].y, self.enemies[ei].radius);
                        let (tx, ty) = (self.players[t].x, self.players[t].y);
                        let a = math::atan2((ty - ey) as f64, (tx - ex) as f64);
                        let r_u = units(if def.abilities[ai].range > 0.0 { def.abilities[ai].range as f64 } else { 6.0 });
                        let lx = clamp(ex + round(math::cos(a) * r_u as f64), min_x, max_x);
                        let ly = clamp(ey + round(math::sin(a) * r_u as f64), min_y, max_y);
                        self.enemies[ei].dash_lock_x = lx;
                        self.enemies[ei].dash_lock_y = ly;
                        let w = 0.9f32.max(erad as f32 * 2.4 / UNIT as f32);
                        self.add_event_abc(ev::WIND_UP, rank_flags(def.rank), -1, ei as i32, def.abilities[ai].skill_id, 1, ex, ey, 16.0 / TICK_RATE as f32, w, 0.0, lx, ly);
                    }
                }
                if tick >= self.enemies[ei].ability_next[ai] {
                    self.enemies[ei].ability_next[ai] = tick + 1.max(ticks(def.abilities[ai].cooldown as f64));
                    self.use_ability(ei, &def.abilities[ai]);
                    if !self.enemies[ei].alive {
                        break;
                    }
                }
            }
            if !self.enemies[ei].alive {
                continue;
            }
            // touching players
            if self.enemies[ei].damage > 0 || def.behavior == behavior::EXPLODER {
                for pi in 0..self.players.len() {
                    if !self.players[pi].present || self.players[pi].fallen {
                        continue;
                    }
                    let (ex, ey, edmg) = (self.enemies[ei].x, self.enemies[ei].y, self.enemies[ei].damage);
                    let reach = self.enemies[ei].radius + units(r.body_radius as f64);
                    if dist2(self.players[pi].x, self.players[pi].y, ex, ey) > reach as i64 * reach as i64 {
                        continue;
                    }
                    if def.behavior == behavior::EXPLODER {
                        let area = units(def.burst_area as f64);
                        self.add_event_abc(ev::ENEMY_BURST, 0, -1, ei as i32, def.id, 0, ex, ey, area as f32 / UNIT as f32, 0.0, 0.0, 0, 0);
                        for qi in 0..self.players.len() {
                            let q = &self.players[qi];
                            if q.present && !q.fallen && dist2(q.x, q.y, ex, ey) <= area as i64 * area as i64 {
                                self.hurt_player_from(qi, edmg, def.id, ei as i32, ex, ey, 3);
                            }
                        }
                        self.kill_quiet(ei);
                        break;
                    }
                    if tick >= self.enemies[ei].next_attack {
                        self.enemies[ei].next_attack = tick + 1.max(ticks(def.attack_interval as f64));
                        self.hurt_player_from(pi, edmg, def.id, ei as i32, ex, ey, 0);
                    }
                    break;
                }
            }
        }
    }

    pub(super) fn wall_closest_point(f: &Fx, px: i32, py: i32) -> (i32, i32) {
        if (f.zone_flags & zone_flags::BLOCKS_ENEMIES) == 0 || f.range <= 0 {
            return (f.x, f.y);
        }
        let rad = f.angle * std::f64::consts::PI / 180.0;
        let (ux, uy) = (math::cos(rad), math::sin(rad));
        let mut proj = (px - f.x) as f64 * ux + (py - f.y) as f64 * uy;
        let lim = f.range as f64;
        if proj < -lim {
            proj = -lim;
        } else if proj > lim {
            proj = lim;
        }
        (f.x + round(ux * proj), f.y + round(uy * proj))
    }

    pub(super) fn can_knockback(&mut self, ei: usize) -> bool {
        if !self.c.is_v4() {
            return true;
        }
        if ei >= self.enemies.len() || !self.enemies[ei].alive {
            return false;
        }
        let win = ticks(1.8);
        let gap = ticks(0.55);
        let tick = self.tick;
        let e = &mut self.enemies[ei];
        if e.kb_count == 0 || tick - e.kb_window_tick >= win {
            e.kb_window_tick = tick;
            e.kb_count = 1;
            return true;
        }
        if e.kb_count >= 2 || tick - e.kb_window_tick < gap {
            return false;
        }
        e.kb_count += 1;
        true
    }

    /// BlockByZones (HordeSim.cs:802-822).
    fn block_by_zones(&mut self, ei: usize, nx: &mut i32, ny: &mut i32) {
        let erad = self.enemies[ei].radius;
        for fi in 0..self.fx.len() {
            let f = &self.fx[fi];
            if f.kind != fxk::ZONE || (f.zone_flags & zone_flags::BLOCKS_ENEMIES) == 0 || f.end_tick <= self.tick {
                continue;
            }
            let (cx, cy) = if self.c.is_v4() { Self::wall_closest_point(f, *nx, *ny) } else { (f.x, f.y) };
            let r = f.radius + erad;
            let dx = (*nx - cx) as i64;
            let dy = (*ny - cy) as i64;
            let dd = dx * dx + dy * dy;
            if dd >= r as i64 * r as i64 {
                continue;
            }
            let d = 1.max(isqrt(dd)) as i64;
            let v4_dmg = self.c.is_v4() && f.base_damage > 0.0;
            *nx = clamp(cx + (dx * r as i64 / d) as i32, self.min_x, self.max_x);
            *ny = clamp(cy + (dy * r as i64 / d) as i32, self.min_y, self.max_y);
            if v4_dmg && self.enemies[ei].kb_vx == 0 && self.enemies[ei].kb_vy == 0 && self.tick >= self.enemies[ei].zone_hit_until {
                let owner = f.owner_slot;
                let widx = f.widx;
                let lvi = f.lv;
                let base = f.base_damage;
                if owner >= 0 && (owner as usize) < self.players.len() && self.players[owner as usize].present {
                    self.enemies[ei].x = *nx;
                    self.enemies[ei].y = *ny;
                    self.enemies[ei].zone_hit_until = self.tick + ticks(0.55);
                    if self.hit_enemy(owner as usize, widx, lvi, ei, base, cx, cy) {
                        return;
                    }
                }
            }
        }
    }

    /// EnemyShoot (HordeSim.cs:624-634).
    fn enemy_shoot(&mut self, ei: usize, tx: i32, ty: i32) {
        let def = &self.c.enemies[self.enemies[ei].def];
        let (ex, ey, shot) = (self.enemies[ei].x, self.enemies[ei].y, self.enemies[ei].shot_damage);
        let a = math::atan2((ty - ey) as f64, (tx - ex) as f64);
        let sp = 1.max(units((def.shot_speed / TICK_RATE as f32) as f64));
        let range = units(if def.shot_range > 0.0 { def.shot_range as f64 * 1.5 } else { 12.0 });
        let mut f = self.new_fx(fxk::ENEMY_SHOT);
        f.owner_enemy = ei as i32;
        f.def_id = def.id;
        f.x = ex;
        f.y = ey;
        f.vx = (math::cos(a) * sp as f64) as i32;
        f.vy = (math::sin(a) * sp as f64) as i32;
        f.radius = units(0.3);
        f.end_tick = self.tick + 1.max(range / sp);
        f.damage = shot;
        self.begin_fx(f);
    }

    /// UseAbility (HordeSim.cs:636-698).
    fn use_ability(&mut self, ei: usize, ab: &'static EnemyAbilityDef) {
        let c = self.c;
        let pi = std::f64::consts::PI;
        let (ex, ey) = (self.enemies[ei].x, self.enemies[ei].y);
        let edef = &c.enemies[self.enemies[ei].def];
        match ab.kind {
            ability::SUMMON_ADDS => {
                let Some(add) = c.enemy(ab.enemy_id) else { return };
                for i in 0..ab.count {
                    let a = i as f64 * pi * 2.0 / 1.max(ab.count) as f64;
                    let x = clamp(ex + units(math::cos(a) * 2.0), self.min_x, self.max_x);
                    let y = clamp(ey + units(math::sin(a) * 2.0), self.min_y, self.max_y);
                    self.spawn(add, x, y, true);
                }
                self.add_event_abc(ev::ENEMY_ABILITY, 0, -1, ei as i32, ab.skill_id, ab.kind as i32, ex, ey, 2.0, 0.0, 0.0, 0, 0);
            }
            ability::NOVA | ability::AREA_AT_PLAYERS => {
                let targets = if ab.kind == ability::NOVA { 1 } else { 1.max(ab.count) };
                let standing: Vec<usize> = self.players.iter().enumerate().filter(|(_, p)| p.present && !p.fallen).map(|(i, _)| i).collect();
                for _ in 0..targets {
                    let (mut x, mut y) = (ex, ey);
                    if ab.kind == ability::AREA_AT_PLAYERS {
                        if standing.is_empty() {
                            break;
                        }
                        let p = standing[self.rng.next(standing.len() as i32) as usize];
                        x = self.players[p].x;
                        y = self.players[p].y;
                    }
                    let mut f = self.new_fx(fxk::ENEMY_WARNING);
                    f.owner_enemy = ei as i32;
                    f.def_id = ab.skill_id;
                    f.x = x;
                    f.y = y;
                    f.radius = units(ab.area as f64);
                    f.end_tick = self.tick + 1.max(ticks(ab.delay as f64));
                    f.damage = round(ab.damage as f64 * arr(&c.scaling.enemy_damage_mul, self.players_for_scaling()));
                    f.enemy_def_id = edef.id;
                    self.begin_fx(f);
                }
            }
            ability::CHARGE => {
                let (tx, ty) = if c.is_v4() && (self.enemies[ei].dash_lock_x != 0 || self.enemies[ei].dash_lock_y != 0) {
                    let (lx, ly) = (self.enemies[ei].dash_lock_x, self.enemies[ei].dash_lock_y);
                    self.enemies[ei].dash_lock_x = 0;
                    self.enemies[ei].dash_lock_y = 0;
                    (lx, ly)
                } else {
                    let (t, _) = self.nearest_standing(ex, ey);
                    let Some(t) = t else { return };
                    (self.players[t].x, self.players[t].y)
                };
                let a = math::atan2((ty - ey) as f64, (tx - ex) as f64);
                let cs = 1.max(units((ab.speed / TICK_RATE as f32) as f64));
                let tick = self.tick;
                let e = &mut self.enemies[ei];
                e.dash_vx = (math::cos(a) * cs as f64) as i32;
                e.dash_vy = (math::sin(a) * cs as f64) as i32;
                e.dash_until = tick + 1.max(units(if ab.range > 0.0 { ab.range as f64 } else { 6.0 }) / cs);
                self.add_event_abc(ev::ENEMY_ABILITY, 0, -1, ei as i32, ab.skill_id, ab.kind as i32, ex, ey, 0.0, 0.0, 0.0, 0, 0);
            }
            ability::BARRAGE => {
                let n = 1.max(ab.count);
                let sp = 1.max(units(((if ab.speed > 0.0 { ab.speed } else { 5.0 }) / TICK_RATE as f32) as f64));
                for i in 0..n {
                    let mut a = i as f64 * pi * 2.0 / n as f64;
                    if ab.range > 0.0 {
                        if let (Some(t), _) = self.nearest_standing(ex, ey) {
                            a = math::atan2((self.players[t].y - ey) as f64, (self.players[t].x - ex) as f64) + (i as f64 - (n - 1) as f64 / 2.0) * 0.15;
                        }
                    }
                    let mut f = self.new_fx(fxk::ENEMY_SHOT);
                    f.owner_enemy = ei as i32;
                    f.def_id = edef.id;
                    f.x = ex;
                    f.y = ey;
                    f.vx = (math::cos(a) * sp as f64) as i32;
                    f.vy = (math::sin(a) * sp as f64) as i32;
                    f.radius = units(0.35);
                    f.end_tick = self.tick + 1.max(units(14.0) / sp);
                    f.damage = round(ab.damage as f64 * arr(&c.scaling.enemy_damage_mul, self.players_for_scaling()));
                    self.begin_fx(f);
                }
                self.add_event_abc(ev::ENEMY_ABILITY, 0, -1, ei as i32, ab.skill_id, ab.kind as i32, ex, ey, 0.0, 0.0, 0.0, 0, 0);
            }
            _ => {}
        }
    }

    /// StepEnemyEffects (HordeSim.cs:700-708): damage over time.
    fn step_enemy_effects(&mut self) {
        for ei in 0..self.enemies.len() {
            let e = &mut self.enemies[ei];
            if !e.alive || e.dot_until <= self.tick || self.tick < e.dot_next {
                continue;
            }
            e.dot_next = self.tick + 1.max(e.dot_every);
            let (dmg, slot, weapon) = (e.dot_damage, e.dot_slot, e.dot_weapon);
            self.damage_enemy(ei, dmg, slot, weapon, false, true);
        }
    }

    /// KillQuiet: an enemy leaves without dying for anyone (a sweep crossing out, an exploder that burst).
    fn kill_quiet(&mut self, ei: usize) {
        if !self.enemies[ei].alive {
            return;
        }
        self.enemies[ei].alive = false;
        self.enemies_alive -= 1;
        let (id, x, y) = (self.c.enemies[self.enemies[ei].def].id, self.enemies[ei].x, self.enemies[ei].y);
        self.add_event(ev::ENEMY_DIED, 0, -1, ei as i32, id, 0, x, y);
    }

    // =============================================================================================== damage

    /// HurtPlayer (HordeSim.cs:720-741).
    #[allow(dead_code)]
    pub(super) fn hurt_player(&mut self, pi: usize, dmg: i32, enemy_def_id: i32) {
        self.hurt_player_from(pi, dmg, enemy_def_id, -1, 0, 0, 0);
    }

    #[allow(clippy::too_many_arguments)]
    pub(super) fn hurt_player_from(&mut self, pi: usize, dmg: i32, enemy_def_id: i32, from_enemy: i32, from_x: i32, from_y: i32, via: i32) {
        let tick = self.tick;
        let v4 = self.c.is_v4();
        let hurt_grace = 1.max(ticks(self.c.rules.hurt_grace_seconds as f64));
        {
            let p = &self.players[pi];
            if p.fallen || !p.present || dmg <= 0 {
                return;
            }
            if p.safe_until > tick || p.open.is_some() && !p.idle_chooser || p.hurt_until > tick {
                return;
            }
        }
        self.players[pi].hurt_until = tick + hurt_grace;
        let dodge = self.players[pi].s.dodge;
        if dodge > 0.0 && self.rng.chance(75f64.min(dodge)) {
            let (x, y) = (self.players[pi].x, self.players[pi].y);
            if v4 {
                self.add_event_abc(ev::PLAYER_HIT, evf::DODGED, pi as i32, from_enemy, enemy_def_id, 0, x, y, via as f32, 0.0, 0.0, if from_x != 0 { from_x } else { x }, if from_y != 0 { from_y } else { y });
            } else {
                self.add_event(ev::PLAYER_HIT, evf::DODGED, pi as i32, -1, enemy_def_id, 0, x, y);
            }
            return;
        }
        let p = &mut self.players[pi];
        let d = dmg as f64 * 0.1f64.max(1.0 + p.s.damage_taken_pct / 100.0) - p.s.defence;
        let taken = 1.max(round(d));
        p.hp -= taken;
        p.damage_taken += taken as i64;
        let (x, y, hp) = (p.x, p.y, p.hp);
        if v4 {
            self.add_event_abc(ev::PLAYER_HIT, 0, pi as i32, from_enemy, enemy_def_id, taken, x, y, via as f32, 0.0, 0.0, if from_x != 0 { from_x } else { x }, if from_y != 0 { from_y } else { y });
        } else {
            self.add_event(ev::PLAYER_HIT, 0, pi as i32, -1, enemy_def_id, taken, x, y);
        }
        if hp <= 0 {
            if v4 && self.ygg_save(pi) {
                return;
            }
            let p = &mut self.players[pi];
            p.hp = 0;
            p.fallen = true;
            p.revive = 0.0;
            p.falls += 1;
            p.inputs.clear();
            p.vx = 0;
            p.vy = 0;
            p.moving = false;
            self.close_offers(pi, true);
            for i in 0..self.fx.len() {
                if self.fx[i].owner_slot == pi as i32 && self.fx[i].kind == fxk::ORBIT && self.fx[i].end_tick > tick {
                    self.end_fx(i, false);
                }
            }
            if v4 {
                self.add_event(ev::FELL, 0, pi as i32, from_enemy, enemy_def_id, via, x, y);
                self.souls_fell(pi);
            } else {
                self.add_event(ev::FELL, 0, pi as i32, -1, enemy_def_id, 0, x, y);
            }
        }
    }

    /// DamageMul (HordeSim.cs:743-757). `enemy`: the enemy's def index.
    pub(super) fn damage_mul(&self, pi: usize, w: &WeaponDef, enemy: Option<usize>) -> f64 {
        let s = &self.players[pi].s;
        let pct = s.damage_pct + (if w.class == damage_class::MELEE { s.melee_pct } else if w.class == damage_class::RANGED { s.ranged_pct } else { s.magic_pct });
        let mut m = 0.1f64.max(1.0 + pct / 100.0);
        m *= 1.0 + s.element_damage[(w.element % 10) as usize] / 100.0;
        if let Some(ed) = enemy {
            let ed = &self.c.enemies[ed];
            m *= 1.0 + s.vs_race[(ed.race % 10) as usize] / 100.0;
            m *= 1.0 + s.vs_element[(ed.element % 10) as usize] / 100.0;
            if ed.rank != rank::NORMAL {
                m *= 1.0 + s.vs_boss / 100.0;
            }
            if let Some(t) = &self.c.element_pct {
                m *= t[(w.element % 10) as usize][(ed.element % 10) as usize] as f64 / 100.0;
            }
            if self.c.is_v4() {
                m *= self.trans_damage_mul(pi);
            }
        }
        m
    }

    /// HitEnemy (HordeSim.cs:761-772): a player's weapon hits an enemy. `lv`: the level the hit is made with. True
    /// when the enemy died.
    pub(super) fn hit_enemy(&mut self, pi: usize, widx: usize, lv: (usize, usize), ei: usize, base_dmg: f64, from_x: i32, from_y: i32) -> bool {
        if !self.enemies[ei].alive {
            return false;
        }
        let c = self.c;
        let w = &c.weapons[self.players[pi].weapons[widx].def];
        let mut d = base_dmg * self.damage_mul(pi, w, Some(self.enemies[ei].def));
        let crit = self.rng.chance(self.players[pi].s.crit);
        if crit {
            d *= (c.rules.crit_damage_pct as f64 + self.players[pi].s.crit_damage) / 100.0;
        }
        let dmg = 1.max(round(d));
        let died = self.damage_enemy(ei, dmg, pi as i32, w.id, crit, false);
        if !died {
            self.apply_effects(pi, widx, lv, ei, from_x, from_y);
        }
        if c.is_v4() {
            self.trans_after_hit(pi, widx, ei, dmg, crit, died);
        }
        !self.enemies[ei].alive
    }

    /// ApplyEffects (HordeSim.cs:774-809).
    pub(super) fn apply_effects(&mut self, pi: usize, widx: usize, lv: (usize, usize), ei: usize, from_x: i32, from_y: i32) {
        let c = self.c;
        let Some(effects) = &c.weapons[lv.0].levels[lv.1].effects else { return };
        let wdef = &c.weapons[self.players[pi].weapons[widx].def];
        for fx in effects {
            let Some(fx) = fx else { continue };
            let chance = fx.chance as f64 * (1.0 + self.players[pi].s.effect_chance / 100.0);
            if !self.rng.chance(chance) {
                continue;
            }
            let edef = &c.enemies[self.enemies[ei].def];
            let resist = 0f64.max(1.0 - edef.effect_resist as f64 / 100.0);
            let tick = self.tick;
            // (a `break` inside C#'s switch leaves the switch only: the Effect event below is added all the same)
            match fx.kind {
                effect_kind::SLOW => {
                    if resist > 0.0 {
                        let e = &mut self.enemies[ei];
                        e.slow_until = e.slow_until.max(tick + ticks(fx.duration as f64 * resist));
                        e.slow_pct = clamp(fx.power as i32, 0, 90);
                    }
                }
                effect_kind::FREEZE => {
                    if resist > 0.0 {
                        let e = &mut self.enemies[ei];
                        e.frozen_until = e.frozen_until.max(tick + ticks(fx.duration as f64 * resist));
                    }
                }
                effect_kind::STUN => {
                    if resist > 0.0 {
                        let e = &mut self.enemies[ei];
                        e.stun_until = e.stun_until.max(tick + ticks(fx.duration as f64 * resist));
                    }
                }
                effect_kind::DOT => {
                    let mul = self.damage_mul(pi, wdef, Some(self.enemies[ei].def));
                    let e = &mut self.enemies[ei];
                    e.dot_until = tick + ticks(fx.duration as f64);
                    e.dot_every = 1.max(ticks(fx.tick as f64));
                    e.dot_next = tick + e.dot_every;
                    e.dot_damage = 1.max(round(fx.power as f64 * mul));
                    e.dot_slot = pi as i32;
                    e.dot_weapon = wdef.id;
                }
                effect_kind::KNOCKBACK => {
                    // an impulse away from the hit's source that dies down over the next ticks; a second hit adds to one still going
                    let kb = fx.power as f64 * (1.0 + self.players[pi].s.knockback_pct / 100.0) * 0f64.max(1.0 - edef.knockback_resist as f64 / 100.0);
                    if kb > 0.0 {
                        if !self.can_knockback(ei) {
                            continue;
                        }
                        let keep = self.kb_keep_pct;
                        let (p_x, p_y, p_vx, p_vy) = (self.players[pi].x, self.players[pi].y, self.players[pi].vx, self.players[pi].vy);
                        let e = &mut self.enemies[ei];
                        let mut dx = (e.x - from_x) as i64;
                        let mut dy = (e.y - from_y) as i64;
                        if c.is_v4() {
                            // Item 54: never push an enemy toward the attacking player.
                            let px = (e.x - p_x) as i64;
                            let py = (e.y - p_y) as i64;
                            if dx * px + dy * py <= 0 {
                                dx = px;
                                dy = py;
                            }
                            if dx == 0 && dy == 0 {
                                dx = if p_vx != 0 { p_vx as i64 } else { 1 };
                                dy = p_vy as i64;
                            }
                            let d = 1.max(isqrt(dx * dx + dy * dy)) as i64;
                            let v0 = math::impulse_for(units(kb), keep) as i64;
                            let mut nvx = clamp(e.kb_vx + (dx * v0 / d) as i32, -8 * UNIT, 8 * UNIT);
                            let mut nvy = clamp(e.kb_vy + (dy * v0 / d) as i32, -8 * UNIT, 8 * UNIT);
                            if (px != 0 || py != 0) && (nvx as i64 * px + nvy as i64 * py) < 0 {
                                nvx = clamp((dx * v0 / d) as i32, -8 * UNIT, 8 * UNIT);
                                nvy = clamp((dy * v0 / d) as i32, -8 * UNIT, 8 * UNIT);
                            }
                            e.kb_vx = nvx;
                            e.kb_vy = nvy;
                        } else {
                            let d = 1.max(isqrt(dx * dx + dy * dy)) as i64;
                            let v0 = math::impulse_for(units(kb), keep) as i64;
                            e.kb_vx = clamp(e.kb_vx + (dx * v0 / d) as i32, -8 * UNIT, 8 * UNIT);
                            e.kb_vy = clamp(e.kb_vy + (dy * v0 / d) as i32, -8 * UNIT, 8 * UNIT);
                        }
                    }
                }
                _ => {}
            }
            let (x, y) = (self.enemies[ei].x, self.enemies[ei].y);
            self.add_event(ev::EFFECT, 0, pi as i32, ei as i32, wdef.id, fx.kind as i32, x, y);
        }
    }

    /// DamageEnemy (HordeSim.cs:812-823): true when it died (EXP, drops, counters, the event).
    pub(super) fn damage_enemy(&mut self, ei: usize, dmg: i32, slot: i32, weapon_id: i32, crit: bool, dot: bool) -> bool {
        if !self.enemies[ei].alive || dmg <= 0 {
            return false;
        }
        self.enemies[ei].hp -= dmg;
        if slot >= 0 {
            self.players[slot as usize].damage_dealt += dmg as i64;
        }
        let died = self.enemies[ei].hp <= 0;
        let flags = (if crit { evf::CRIT } else { 0 }) | (if died { evf::KILL } else { 0 }) | (if dot { evf::DOT } else { 0 });
        let (x, y) = (self.enemies[ei].x, self.enemies[ei].y);
        self.hits.push(Event { kind: ev::HIT, flags, tick: self.tick, slot, target: ei as i32, def_id: weapon_id, value: dmg, x, y, ..Default::default() });
        if died {
            self.enemy_died(ei, if slot >= 0 { Some(slot as usize) } else { None });
        }
        died
    }

    /// EnemyDied (HordeSim.cs:825-856).
    fn enemy_died(&mut self, ei: usize, killer: Option<usize>) {
        let c = self.c;
        self.enemies[ei].alive = false;
        self.enemies_alive -= 1;
        let def = &c.enemies[self.enemies[ei].def];
        let (ex, ey) = (self.enemies[ei].x, self.enemies[ei].y);
        self.add_event(ev::ENEMY_DIED, rank_flags(def.rank), killer.map(|k| k as i32).unwrap_or(-1), ei as i32, def.id, 0, ex, ey);
        if let Some(k) = killer {
            let p = &mut self.players[k];
            p.kills += 1;
            if def.rank == rank::ELITE {
                p.elite_kills += 1;
            } else if def.rank >= rank::BOSS {
                p.boss_kills += 1;
            }
            if p.s.hp_on_kill > 0.0 && !p.fallen {
                p.kill_heal_carry += p.s.hp_on_kill;
                if p.kill_heal_carry >= 1.0 {
                    let h = p.kill_heal_carry as i32;
                    p.kill_heal_carry -= h as f64;
                    p.hp = p.max_hp.min(p.hp + h);
                }
            }
            if c.is_v4() {
                self.trans_on_kill(ei, k);
            }
        }
        if def.exp > 0 {
            self.drop_exp(ex, ey, def.exp);
        }
        for d in &def.drops {
            let Some(d) = d else { continue };
            if !self.rng.chance(d.chance as f64) {
                continue;
            }
            let Some(pd) = c.pickup(d.pickup_id) else { continue };
            let value = c.pickups[pd].value;
            if d.scatter_cells > 0.0 {
                // off the path: ScatterCells away in a random direction, inside the arena
                let a = self.rng.next_double() * std::f64::consts::PI * 2.0;
                let x = ex + units(math::cos(a) * d.scatter_cells as f64);
                let y = ey + units(math::sin(a) * d.scatter_cells as f64);
                self.add_pickup(pd, x, y, value);
            } else {
                let x = ex + units(self.rng.next_double() - 0.5);
                let y = ey + units(self.rng.next_double() - 0.5);
                self.add_pickup(pd, x, y, value);
            }
        }
        if ei as i32 == self.final_boss_slot && def.rank == rank::FINAL_BOSS {
            self.final_boss_slot = -1;
            if !c.is_v4() {
                self.end(outcome::VICTORY);
            } else {
                self.final_phase = false;
                for wi in 0..self.waves.len() {
                    let wd = &c.waves[self.waves[wi].def];
                    if !wd.once && c.rules.reference_run_seconds > 0.0 && wd.to >= c.rules.reference_run_seconds - 10.0 {
                        self.waves[wi].done = false;
                        self.waves[wi].to = i32::MAX;
                        if self.waves[wi].next < self.tick {
                            self.waves[wi].next = self.tick + 1;
                        }
                    }
                }
            }
        }
    }

    // =============================================================================================== pickups

    /// DropExp (HordeSim.cs:860-874).
    fn drop_exp(&mut self, x: i32, y: i32, value: i32) {
        let c = self.c;
        let mut best: Option<usize> = None;
        for (i, pd) in c.pickups.iter().enumerate() {
            if pd.kind == pickup_kind::EXP && pd.value <= value && best.map_or(true, |b| pd.value > c.pickups[b].value) {
                best = Some(i);
            }
        }
        if best.is_none() {
            for (i, pd) in c.pickups.iter().enumerate() {
                if pd.kind == pickup_kind::EXP && best.map_or(true, |b| pd.value < c.pickups[b].value) {
                    best = Some(i);
                }
            }
        }
        let Some(best) = best else {
            self.give_exp_all(None, value);
            return;
        };
        if self.pickups.len() >= MAX_PICKUPS {
            // full: the value goes into the oldest EXP pickup (nothing is lost)
            if let Some(q) = self.pickups.iter_mut().find(|q| c.pickups[q.def].kind == pickup_kind::EXP) {
                q.value += value;
            }
            return;
        }
        self.add_pickup(best, x, y, value);
    }

    /// AddPickup (HordeSim.cs:876-882).
    fn add_pickup(&mut self, def: usize, x: i32, y: i32, value: i32) {
        if self.pickups.len() >= MAX_PICKUPS {
            return;
        }
        let k = Pickup { id: self.next_pickup_id, def, x: clamp(x, self.min_x, self.max_x), y: clamp(y, self.min_y, self.max_y), value, pulled_by: -1, pull_tick: 0 };
        self.next_pickup_id = if self.next_pickup_id >= 65535 { 1 } else { self.next_pickup_id + 1 };
        self.pickups_added.push(k.id);
        self.pickups.push(k);
    }

    /// PullSpeed (HordeSim.cs:886): a flying pickup's speed in units per tick after this many ticks of flight.
    pub fn pull_speed(&self, ticks_flying: i32) -> i32 {
        let r = &self.c.rules;
        // R.PickupPullAccel * Math.Max(0, ticksFlying) is float × int: single precision; the rest is double
        let accel = (r.pickup_pull_accel * 0.max(ticks_flying) as f32) as f64 / TICK_RATE as f64;
        1.max(units((r.pickup_pull_speed as f64 + accel) / TICK_RATE as f64))
    }

    /// StepPickups (HordeSim.cs:888-920).
    fn step_pickups(&mut self) {
        let c = self.c;
        let r = &c.rules;
        let mut i = self.pickups.len();
        while i > 0 {
            i -= 1;
            let tick = self.tick;
            let mut taker: Option<usize> = None;
            let pb = self.pickups[i].pulled_by;
            if pb >= 0 && self.players[pb as usize].present && !self.players[pb as usize].fallen {
                taker = Some(pb as usize);
            } else {
                self.pickups[i].pulled_by = -1;
                let mut best = i64::MAX;
                for (pi, p) in self.players.iter().enumerate() {
                    if !p.present || p.fallen {
                        continue;
                    }
                    // (R.PickupRadius + R.PickupRadiusPerLevel * Math.Max(0, p.Level - 1)) is all float: single precision
                    let base: f32 = r.pickup_radius + r.pickup_radius_per_level * 0.max(p.level - 1) as f32;
                    let rad = units(base as f64 * (1.0 + p.s.pickup_radius_pct / 100.0));
                    let d = dist2(p.x, p.y, self.pickups[i].x, self.pickups[i].y);
                    if d <= rad as i64 * rad as i64 && d < best {
                        best = d;
                        taker = Some(pi);
                    }
                }
                if let Some(t) = taker {
                    let k = &mut self.pickups[i];
                    k.pulled_by = t as i32;
                    k.pull_tick = tick;
                    self.pickups_pulled.push(k.id);
                }
            }
            let Some(t) = taker else { continue };
            let (tx, ty) = (self.players[t].x, self.players[t].y);
            let dd = dist2(tx, ty, self.pickups[i].x, self.pickups[i].y);
            let grab = units(0.6);
            if dd > grab as i64 * grab as i64 {
                let d = isqrt(dd);
                let sp = self.pull_speed(tick - self.pickups[i].pull_tick);
                let k = &mut self.pickups[i];
                k.x += ((tx - k.x) as i64 * sp.min(d) as i64 / d as i64) as i32;
                k.y += ((ty - k.y) as i64 * sp.min(d) as i64 / d as i64) as i32;
                continue;
            }
            let k = self.pickups.remove(i);
            self.pickups_removed.push(k.id);
            self.take_pickup(t, &k);
        }
    }

    /// TakePickup (HordeSim.cs:922-949).
    fn take_pickup(&mut self, pi: usize, k: &Pickup) {
        let c = self.c;
        let def = &c.pickups[k.def];
        self.add_event(ev::PICKUP_TAKEN, 0, pi as i32, k.id, def.id, k.value, k.x, k.y);
        match def.kind {
            pickup_kind::EXP => {
                if c.is_v4() {
                    self.trans_on_pickup(pi);
                }
                self.give_exp_all(Some(pi), k.value);
            }
            pickup_kind::HEAL => {
                let p = &mut self.players[pi];
                let h = 1.max(p.max_hp * k.value / 100);
                let before = p.hp;
                p.hp = p.max_hp.min(p.hp + h);
                let (got, x, y) = (p.hp - before, p.x, p.y);
                self.add_event(ev::HEAL, 0, pi as i32, -1, 0, got, x, y);
            }
            pickup_kind::MAGNET => {
                let tick = self.tick;
                for q in self.pickups.iter_mut() {
                    if c.pickups[q.def].kind == pickup_kind::EXP && q.pulled_by < 0 {
                        q.pulled_by = pi as i32;
                        q.pull_tick = tick;
                        self.pickups_pulled.push(q.id);
                    }
                }
            }
            pickup_kind::CHEST => {
                for _ in 0..1.max(k.value) {
                    self.chest_upgrade(pi);
                }
            }
            pickup_kind::BOMB => {
                let rad = units(if def.area > 0.0 { def.area as f64 } else { 8.0 });
                for ei in 0..self.enemies.len() {
                    if self.enemies[ei].alive && dist2(self.enemies[ei].x, self.enemies[ei].y, k.x, k.y) <= rad as i64 * rad as i64 {
                        self.damage_enemy(ei, k.value, pi as i32, 0, false, false);
                    }
                }
                self.add_event_abc(ev::STRIKE, 0, pi as i32, -1, 0, k.value, k.x, k.y, rad as f32 / UNIT as f32, 0.0, 0.0, 0, 0);
            }
            pickup_kind::PASSIVE => {
                self.arrow_buff_until[pi] = self.arrow_buff_until[pi].max(self.tick + ticks(10.0));
                self.recalc(pi, false);
            }
            _ => {}
        }
    }

    /// GiveExpAll (HordeSim.cs:953-960): run EXP into the party's pool (the taker's EXP bonus applies).
    fn give_exp_all(&mut self, taker: Option<usize>, value: i32) {
        let scale = if self.time_scale > 0.0 { 1.0 / self.time_scale } else { 1.0 };
        let bonus = match taker {
            Some(t) => 1.0 + self.players[t].s.exp_gain_pct / 100.0,
            None => 1.0,
        };
        self.party_exp_carry += value as f64 * scale * bonus;
        let gain = self.party_exp_carry as i32;
        self.party_exp_carry -= gain as f64;
        if gain > 0 {
            self.gain_exp(gain);
        }
    }

    /// ExpToNext (HordeSim.cs:963-971): the table's requirement from level n to n + 1, for one player.
    pub fn exp_to_next(&self, level: i32) -> i32 {
        let r = &self.c.rules;
        let t = match &r.exp_table {
            Some(t) if !t.is_empty() => t,
            _ => return 10 * level,
        };
        if ((level - 1) as i64) < t.len() as i64 {
            return 1.max(t[0.max(level - 1) as usize]);
        }
        let mut v = t[t.len() - 1] as f64;
        for _ in t.len() as i32..level {
            v *= 1.0 + r.exp_growth_pct as f64 / 100.0;
        }
        ((i32::MAX / 2) as f64).min(v.round_ties_even()) as i32
    }

    /// PartyExpToNext (HordeSim.cs:975-979).
    pub fn party_exp_to_next(&self) -> i32 {
        let mul = arr(&self.c.rules.party_exp_scale, self.players_for_scaling());
        1f64.max(((i32::MAX / 2) as f64).min((self.exp_to_next(self.party_level) as f64 * 0.01f64.max(mul)).round_ties_even())) as i32
    }

    /// GainExp (HordeSim.cs:981-1000).
    fn gain_exp(&mut self, gain: i32) {
        let r = &self.c.rules;
        if self.party_level >= r.max_level {
            return;
        }
        self.party_exp += gain;
        while self.party_level < r.max_level && self.party_exp >= self.party_exp_to_next() {
            self.party_exp -= self.party_exp_to_next();
            self.party_level += 1;
            for s in 0..self.players.len() {
                if !self.players[s].present {
                    continue;
                }
                self.players[s].level = self.party_level;
                self.recalc(s, false);
                let p = &mut self.players[s];
                if !p.fallen {
                    p.hp = p.max_hp.min(p.hp + r.hp_per_level);
                }
                let (level, x, y) = (p.level, p.x, p.y);
                self.add_event(ev::LEVEL_UP, 0, s as i32, -1, 0, level, x, y);
                self.queue_job_offer(s);
                self.queue_card_offer(s);
            }
            if self.c.is_v4() {
                self.souls_job_mark();
            }
        }
    }

    // =============================================================================================== offers

    /// QueueJobOffer (HordeSim.cs:1004-1017): the next tier whose mark is reached and not yet offered.
    fn queue_job_offer(&mut self, s: usize) {
        let c = self.c;
        let r = &c.rules;
        let Some(job) = c.job(self.players[s].job_id) else { return };
        let job = &c.jobs[job];
        let Some(next) = &job.next else { return };
        if next.is_empty() {
            return;
        }
        let Some(marks) = &r.job_marks else { return };
        if job.tier < 0 || job.tier as usize >= marks.len() || self.players[s].level < marks[job.tier as usize] {
            return;
        }
        if self.players[s].offers.iter().any(|o| o.is_job) {
            return;
        }
        if self.players[s].open.as_ref().is_some_and(|o| o.is_job) {
            return;
        }
        let sex = self.players[s].sex;
        let mut cards = Vec::new();
        for &id in next {
            if let Some(j) = c.job(id) {
                let j = &c.jobs[j];
                if j.sex == 0 || sex == 0 || j.sex == sex {
                    cards.push(Card { kind: card::JOB, def_id: id, level: j.tier, ..Default::default() });
                }
            }
        }
        if cards.is_empty() {
            return;
        }
        if cards.len() == 1 {
            self.change_job(s, c.job(cards[0].def_id));
            return;
        }
        let id = self.next_offer();
        self.players[s].offers.push_back(Offer { id, is_job: true, seconds: r.choice_seconds, cards: Some(cards) });
    }

    fn next_offer(&mut self) -> i32 {
        let id = self.next_offer_id;
        self.next_offer_id = if self.next_offer_id >= 65535 { 1 } else { self.next_offer_id + 1 };
        id
    }

    /// QueueCardOffer: the cards are drawn when the offer opens (they reflect what the player has then).
    fn queue_card_offer(&mut self, s: usize) {
        let id = self.next_offer();
        let seconds = self.c.rules.choice_seconds;
        self.players[s].offers.push_back(Offer { id, is_job: false, seconds, cards: None });
    }

    /// OpenNext (HordeSim.cs:1027-1041).
    fn open_next(&mut self, s: usize) {
        while let Some(mut o) = self.players[s].offers.pop_front() {
            if !o.is_job {
                o.cards = Some(self.draw_cards(s));
            }
            if o.cards.as_ref().map_or(true, |c| c.is_empty()) {
                continue;
            }
            if self.c.is_v4() && self.players_present() <= 1 {
                o.seconds = 0.0;
                self.players[s].open_deadline = i32::MAX / 2;
            } else {
                o.seconds = self.current_choice_seconds;
                self.players[s].open_deadline = self.wall_tick + 1.max(ticks(self.current_choice_seconds as f64));
            }
            self.offers_opened.push((s as i32, o.clone()));
            self.players[s].open = Some(o);
            return;
        }
    }

    /// CloseOffers (HordeSim.cs:1043-1053).
    fn close_offers(&mut self, s: usize, keep_queued: bool) {
        if let Some(mut o) = self.players[s].open.take() {
            self.offers_closed.push((s as i32, o.id));
            // a fallen player's open choice is put back: it is offered again when it rises
            if keep_queued {
                if !o.is_job {
                    o.cards = None;
                }
                o.id = self.next_offer();
                self.players[s].offers.push_front(o);
            }
        }
        if !keep_queued {
            self.players[s].offers.clear();
        }
    }

    pub fn is_active_weapon_v3(id: i32) -> bool {
        matches!(id, 42 | 43 | 44 | 58 | 62 | 76 | 77 | 78 | 93)
    }

    pub fn is_active_weapon(id: i32) -> bool {
        let root = if id >= 6000 {
            id - 6000
        } else if id >= 5000 {
            id - 5000
        } else if id >= 3000 {
            id - 3000
        } else if id >= 2000 {
            id - 2000
        } else if id >= 1000 {
            id - 1000
        } else {
            id
        };
        matches!(root, 22 | 28 | 45 | 111 | 120 | 121 | 123 | 170 | 183)
    }

    pub fn active_cooldown_seconds(id: i32) -> f64 {
        let root = if id >= 6000 {
            id - 6000
        } else if id >= 5000 {
            id - 5000
        } else if id >= 3000 {
            id - 3000
        } else if id >= 2000 {
            id - 2000
        } else if id >= 1000 {
            id - 1000
        } else {
            id
        };
        match root {
            28 | 45 => 35.0,
            22 => 30.0,
            170 => 40.0,
            111 | 120 => 45.0,
            121 | 123 => 50.0,
            183 => 60.0,
            _ => 35.0,
        }
    }

    pub(super) fn active_cooldown_ticks(&self, pi: usize, weapon_id: i32) -> i32 {
        let base_sec = Self::active_cooldown_seconds(weapon_id);
        ticks(12.0).max(ticks(base_sec * 0.4f64.max(1.0 + self.players[pi].s.cooldown_pct / 100.0)))
    }

    /// DrawCards (HordeSim.cs:1057-1101 + HordeSim.Transcend.cs:45-95).
    pub(super) fn draw_cards(&mut self, s: usize) -> Vec<Card> {
        #[derive(Clone, Copy)]
        struct Cand {
            kind: u8,
            def_id: i32,
            level: i32,
            weight: i32,
        }
        let c = self.c;
        let r = &c.rules;
        let p = &self.players[s];
        let (mut new_w, mut lvl_w, mut new_p, mut lvl_p, mut stat) = (Vec::new(), Vec::new(), Vec::new(), Vec::new(), Vec::new());
        let (mut seen_w, mut seen_p): (Vec<i32>, Vec<i32>) = (Vec::new(), Vec::new());
        for (wi, ow) in p.weapons.iter().enumerate() {
            if c.is_v4() && self.ygg_burnt_weapon(s, wi) {
                continue;
            }
            let d = &c.weapons[ow.def];
            if (ow.level as usize) < d.levels.len() {
                lvl_w.push(Cand { kind: card::WEAPON_LEVEL, def_id: d.id, level: ow.level + 1, weight: 1.max(d.weight) });
            } else if c.is_v4() && d.trans_of == 0 && d.evolve_of == 0 {
                let rw_idx = c.weapon(d.root_id()).unwrap_or(ow.def);
                let rw = &c.weapons[rw_idx];
                if let Some(tb) = &rw.trans_branches {
                    if tb.len() >= 2 && c.weapon(tb[0]).is_some() && c.weapon(tb[1]).is_some() {
                        lvl_w.push(Cand {
                            kind: card::WEAPON_TRANSCEND,
                            def_id: d.id,
                            level: ow.level + 1,
                            weight: 1.max(if rw.weight > 0 { rw.weight } else { 100 }),
                        });
                    }
                }
            }
        }
        for op in &p.passives {
            let d = &c.passives[op.def];
            if (op.level as usize) < d.levels.len() {
                lvl_p.push(Cand { kind: card::PASSIVE_LEVEL, def_id: d.id, level: op.level + 1, weight: 1.max(d.weight) });
            } else if c.is_v4() && d.trans_of == 0 {
                if let Some(tp_idx) = c.passive(1000 + d.id) {
                    lvl_p.push(Cand {
                        kind: card::PASSIVE_TRANSCEND,
                        def_id: c.passives[tp_idx].id,
                        level: op.level + 1,
                        weight: 1.max(if d.weight > 0 { d.weight } else { 80 }),
                    });
                }
            }
        }
        let slots_w = p.weapons.iter().filter(|ow| !ow.signature).count() as i32;
        let active_w = p
            .weapons
            .iter()
            .filter(|ow| {
                let rid = c.weapons[ow.def].root_id();
                if c.is_v4() { Self::is_active_weapon(rid) } else { Self::is_active_weapon_v3(rid) }
            })
            .count() as i32;
        for &j in &p.jobs {
            let j = &c.jobs[j];
            for &id in j.weapons.iter().flatten() {
                let Some(wi) = c.weapon(id) else { continue };
                let w = &c.weapons[wi];
                if w.weight <= 0 || w.levels.is_empty() || (c.is_v4() && (w.kind > 10 || w.trans_of > 0 || w.branch_of > 0 || w.evolve_of > 0 || w.hidden)) {
                    continue;
                }
                if seen_w.contains(&id) {
                    continue;
                }
                seen_w.push(id);
                let owned = if c.is_v4() { p.owns_weapon_or_root(c, wi) } else { p.owns_weapon(wi) };
                let is_act = if c.is_v4() { Self::is_active_weapon(id) } else { Self::is_active_weapon_v3(id) };
                if owned || slots_w >= r.max_weapons || (is_act && active_w >= 2) {
                    continue;
                }
                let cand_w = if c.is_v4() && is_act { 1.max(w.weight / 3) } else { w.weight };
                new_w.push(Cand { kind: card::NEW_WEAPON, def_id: id, level: 1, weight: cand_w });
            }
            for &id in j.passives.iter().flatten() {
                let Some(pi) = c.passive(id) else { continue };
                let ps = &c.passives[pi];
                if ps.weight <= 0 || ps.levels.is_empty() || (c.is_v4() && (ps.trans_of > 0 || !ps.levels.iter().any(|l| l.as_ref().is_some_and(|m| m.iter().any(|x| x.is_some()))))) {
                    continue;
                }
                if seen_p.contains(&id) {
                    continue;
                }
                seen_p.push(id);
                let owned = if c.is_v4() { p.owns_passive_or_root(c, pi) } else { p.owns_passive(pi) };
                if owned || p.passives.len() as i32 >= r.max_passives {
                    continue;
                }
                new_p.push(Cand { kind: card::NEW_PASSIVE, def_id: id, level: 1, weight: ps.weight });
            }
        }
        if !c.is_v4() {
            for sd in &c.stats {
                stat.push(Cand { kind: card::STAT, def_id: sd.stat as i32, level: 1.max(r.stat_card_points), weight: 100 });
            }
        }
        let mut pools = [new_w, lvl_w, new_p, lvl_p, stat];
        let weights = [r.weight_new_weapon, r.weight_weapon_level, r.weight_new_passive, r.weight_passive_level, r.weight_stat];
        let mut picked: Vec<Card> = Vec::new();
        let want = 1.max(r.cards_per_offer) as usize;
        while picked.len() < want {
            let mut total = 0;
            for i in 0..pools.len() {
                if !pools[i].is_empty() {
                    total += 0.max(weights[i]);
                }
            }
            if total <= 0 {
                break;
            }
            let mut rr = self.rng.next(total);
            let mut k = 0;
            while k < pools.len() {
                if pools[k].is_empty() || weights[k] <= 0 {
                    k += 1;
                    continue;
                }
                rr -= weights[k];
                if rr < 0 {
                    break;
                }
                k += 1;
            }
            if k >= pools.len() {
                break;
            }
            let pool = &mut pools[k];
            let tw: i32 = pool.iter().map(|c| c.weight).sum();
            let mut r2 = self.rng.next(1.max(tw));
            let mut idx = 0;
            while idx + 1 < pool.len() {
                r2 -= pool[idx].weight;
                if r2 < 0 {
                    break;
                }
                idx += 1;
            }
            let ch = pool.remove(idx);
            if c.is_v4() && ch.kind == card::WEAPON_TRANSCEND {
                if let Some(wi) = c.weapon(ch.def_id) {
                    let rw_idx = c.weapon(c.weapons[wi].root_id()).unwrap_or(wi);
                    if let Some(tb) = &c.weapons[rw_idx].trans_branches {
                        if tb.len() >= 2 {
                            if picked.len() + 2 <= 6 {
                                picked.push(Card { kind: card::WEAPON_TRANSCEND, def_id: tb[0], level: ch.level, ..Default::default() });
                                picked.push(Card { kind: card::WEAPON_TRANSCEND, def_id: tb[1], level: ch.level, ..Default::default() });
                            } else {
                                let b = tb[self.rng.next(2) as usize];
                                picked.push(Card { kind: card::WEAPON_TRANSCEND, def_id: b, level: ch.level, ..Default::default() });
                            }
                            continue;
                        }
                    }
                }
            }
            picked.push(Card { kind: ch.kind, def_id: ch.def_id, level: ch.level, ..Default::default() });
        }
        if picked.is_empty() {
            picked.push(Card { kind: card::HEAL, def_id: 0, level: 30, ..Default::default() });
        }
        if c.is_v4() {
            // Item 52: every weapon / passive / heal card also grants bonus stat points.
            let pts = clamp(if r.stat_card_points > 0 { r.stat_card_points } else { 2 }, 1, 255) as u8;
            let start = self.rng.next(6);
            let step = 1 + self.rng.next(5); // 1..5 are all coprime to 6
            for (i, cd) in picked.iter_mut().enumerate() {
                if cd.kind != card::JOB && cd.kind != card::STAT {
                    cd.bonus_stat = ((start + i as i32 * step) % 6) as u8;
                    cd.bonus_stat_points = pts;
                }
            }
        }
        picked
    }

    /// ApplyCard (HordeSim.cs:1103-1144 + HordeSim.Transcend.cs:97-130). `offer_id`: the offer it closes.
    pub(super) fn apply_card(&mut self, s: usize, offer_id: i32, cd: Card) {
        let c = self.c;
        self.offers_closed.push((s as i32, offer_id));
        self.players[s].open = None;
        let tick = self.tick;
        match cd.kind {
            card::JOB => self.change_job(s, c.job(cd.def_id)),
            card::NEW_WEAPON => {
                if let Some(w) = c.weapon(cd.def_id) {
                    let owned = if c.is_v4() { self.players[s].owns_weapon_or_root(c, w) } else { self.players[s].owns_weapon(w) };
                    if !owned {
                        self.players[s].weapons.push(OwnedWeapon::new(w, tick + 1, false));
                    }
                }
            }
            card::WEAPON_LEVEL => {
                for wi in 0..self.players[s].weapons.len() {
                    let ow = &mut self.players[s].weapons[wi];
                    let d = &c.weapons[ow.def];
                    if d.id == cd.def_id && (ow.level as usize) < d.levels.len() {
                        ow.level += 1;
                        self.restart_orbit(s, wi);
                        break;
                    }
                }
            }
            card::WEAPON_BRANCH => {
                if let Some(b_idx) = c.weapon(cd.def_id) {
                    let branch_of = c.weapons[b_idx].branch_of;
                    if branch_of > 0 {
                        for wi in 0..self.players[s].weapons.len() {
                            let ow = &mut self.players[s].weapons[wi];
                            if c.weapons[ow.def].id == branch_of && (ow.level as usize) < c.weapons[b_idx].levels.len() {
                                ow.def = b_idx;
                                ow.level = (ow.level + 1).max(3);
                                self.restart_orbit(s, wi);
                                break;
                            }
                        }
                    }
                }
            }
            card::WEAPON_TRANSCEND => {
                if let Some(t_idx) = c.weapon(cd.def_id) {
                    let trans_of = c.weapons[t_idx].trans_of;
                    if trans_of > 0 {
                        for wi in 0..self.players[s].weapons.len() {
                            let cur_def = &c.weapons[self.players[s].weapons[wi].def];
                            if cur_def.root_id() == trans_of && cur_def.trans_of == 0 {
                                let new_lv = (self.players[s].weapons[wi].level + 1).max(if cd.level > 0 { cd.level } else { 6 });
                                self.players[s].weapons[wi].def = t_idx;
                                self.players[s].weapons[wi].level = new_lv;
                                self.restart_orbit(s, wi);
                                self.summons.retain(|sm| !(sm.owner_slot == s as i32 && sm.widx == wi));
                                self.players[s].weapons[wi].next_tick = tick + 1;
                                break;
                            }
                        }
                    }
                }
            }
            card::NEW_PASSIVE => {
                if let Some(ps) = c.passive(cd.def_id) {
                    let owned = if c.is_v4() { self.players[s].owns_passive_or_root(c, ps) } else { self.players[s].owns_passive(ps) };
                    if !owned {
                        self.players[s].passives.push(OwnedPassive { def: ps, level: 1 });
                    }
                }
            }
            card::PASSIVE_LEVEL => {
                for op in self.players[s].passives.iter_mut() {
                    let d = &c.passives[op.def];
                    if d.id == cd.def_id && (op.level as usize) < d.levels.len() {
                        op.level += 1;
                        break;
                    }
                }
            }
            card::PASSIVE_TRANSCEND => {
                if let Some(tp_idx) = c.passive(cd.def_id) {
                    let trans_of = c.passives[tp_idx].trans_of;
                    if trans_of > 0 {
                        for op in self.players[s].passives.iter_mut() {
                            let cur_def = &c.passives[op.def];
                            if cur_def.id == trans_of && cur_def.trans_of == 0 {
                                op.def = tp_idx;
                                op.level = (op.level + 1).max(if cd.level > 0 { cd.level } else { 6 });
                                break;
                            }
                        }
                    }
                }
            }
            card::STAT => {
                if cd.def_id >= 0 && cd.def_id < 6 {
                    self.players[s].stat_points[cd.def_id as usize] += cd.level;
                }
            }
            card::HEAL => {
                let p = &mut self.players[s];
                p.hp = p.max_hp.min(p.hp + p.max_hp * cd.level / 100);
            }
            _ => {}
        }
        // Item 52: apply bonus stat points attached to the chosen card
        if cd.kind != card::JOB && cd.kind != card::STAT && cd.bonus_stat_points > 0 && cd.bonus_stat < 6 {
            self.players[s].stat_points[cd.bonus_stat as usize] += cd.bonus_stat_points as i32;
        }
        let p = &self.players[s];
        let mut lvl = 0;
        for ow in &p.weapons {
            if c.weapons[ow.def].id == cd.def_id && matches!(cd.kind, card::NEW_WEAPON | card::WEAPON_LEVEL | card::WEAPON_BRANCH | card::WEAPON_TRANSCEND) {
                lvl = ow.level;
            }
        }
        for op in &p.passives {
            if c.passives[op.def].id == cd.def_id && matches!(cd.kind, card::NEW_PASSIVE | card::PASSIVE_LEVEL | card::PASSIVE_TRANSCEND) {
                lvl = op.level;
            }
        }
        if cd.kind == card::STAT {
            lvl = if cd.def_id >= 0 && cd.def_id < 6 { p.stat_points[cd.def_id as usize] } else { 0 };
        }
        if cd.kind != card::JOB {
            let (x, y) = (p.x, p.y);
            self.add_event_abc(ev::CARD_TAKEN, 0, s as i32, -1, cd.def_id, lvl, x, y, cd.kind as f32, 0.0, 0.0, 0, 0);
        }
        self.recalc(s, false);
        self.players[s].loadout_version += 1;
        self.queue_job_offer(s);
    }

    /// ChestUpgrade (HordeSim.cs:1146-1155 + HordeSim.Transcend.cs:132-148).
    fn chest_upgrade(&mut self, s: usize) {
        let c = self.c;
        let p = &self.players[s];
        let mut opts: Vec<Card> = Vec::new();
        for (wi, ow) in p.weapons.iter().enumerate() {
            if c.is_v4() && self.ygg_burnt_weapon(s, wi) {
                continue;
            }
            let d = &c.weapons[ow.def];
            if (ow.level as usize) < d.levels.len() {
                opts.push(Card { kind: card::WEAPON_LEVEL, def_id: d.id, level: ow.level + 1, ..Default::default() });
            } else if c.is_v4() && d.trans_of == 0 && d.evolve_of == 0 {
                let rw_idx = c.weapon(d.root_id()).unwrap_or(ow.def);
                if let Some(tb) = &c.weapons[rw_idx].trans_branches {
                    if tb.len() >= 2 {
                        let b = tb[self.rng.next(2) as usize];
                        opts.push(Card { kind: card::WEAPON_TRANSCEND, def_id: b, level: ow.level + 1, ..Default::default() });
                    }
                }
            }
        }
        for op in &self.players[s].passives {
            let d = &c.passives[op.def];
            if (op.level as usize) < d.levels.len() {
                opts.push(Card { kind: card::PASSIVE_LEVEL, def_id: d.id, level: op.level + 1, ..Default::default() });
            } else if c.is_v4() && d.trans_of == 0 {
                if let Some(tp_idx) = c.passive(1000 + d.id) {
                    opts.push(Card { kind: card::PASSIVE_TRANSCEND, def_id: c.passives[tp_idx].id, level: op.level + 1, ..Default::default() });
                }
            }
        }
        if opts.is_empty() {
            let p = &mut self.players[s];
            p.hp = p.max_hp;
            return;
        }
        let cd = opts[self.rng.next(opts.len() as i32) as usize];
        self.apply_card(s, 0, cd);
        self.offers_closed.pop(); // (no offer was open)
    }

    /// ChangeJob (HordeSim.cs:1157-1174).
    fn change_job(&mut self, s: usize, job: Option<usize>) {
        let c = self.c;
        let Some(ji) = job else { return };
        let job = &c.jobs[ji];
        let tick = self.tick;
        {
            let p = &mut self.players[s];
            p.job_id = job.job_id;
            p.tier = job.tier;
            if !p.jobs.contains(&ji) {
                p.jobs.push(ji);
            }
            if let Some(sb) = &job.stat_bonus {
                for i in 0..6.min(sb.len()) {
                    p.stat_points[i] += sb[i];
                }
            }
        }
        let sig = c.weapon(job.signature_weapon);
        if let Some(sg) = sig {
            if !c.weapons[sg].levels.is_empty() {
                let sg_root = c.weapons[sg].root_id();
                let found = if c.is_v4() {
                    self.players[s].weapons.iter().position(|w| w.def == sg || c.weapons[w.def].root_id() == sg_root)
                } else {
                    self.players[s].weapons.iter().position(|w| w.def == sg)
                };
                match found {
                    None => self.players[s].weapons.push(OwnedWeapon::new(sg, tick + 1, true)),
                    Some(wi) => {
                        let d_len = c.weapons[self.players[s].weapons[wi].def].levels.len();
                        if (self.players[s].weapons[wi].level as usize) < d_len {
                            self.players[s].weapons[wi].level += 1;
                            self.restart_orbit(s, wi);
                        }
                    }
                }
            }
        }
        if c.is_v4() {
            self.souls_job_changed(s);
        }
        self.recalc(s, false);
        self.players[s].loadout_version += 1;
        let (x, y) = (self.players[s].x, self.players[s].y);
        self.add_event(ev::JOB_CHANGED, 0, s as i32, -1, sig.map(|g| c.weapons[g].id).unwrap_or(0), job.job_id, x, y);
        self.queue_job_offer(s);
    }

    // =============================================================================================== transcendence (h18trans, point 30)

    pub(super) fn trans_damage_mul(&self, pi: usize) -> f64 {
        let slayer = self.players[pi].count_passive_trans(self.c, passive_trans_kind::SLAYER);
        if slayer <= 0 {
            return 1.0;
        }
        let s = &self.players[pi].s;
        let mut best_bane = s.vs_boss;
        for i in 0..10 {
            if s.vs_race[i] > best_bane {
                best_bane = s.vs_race[i];
            }
            if s.vs_element[i] > best_bane {
                best_bane = s.vs_element[i];
            }
        }
        if best_bane > 0.0 {
            1.0 + (best_bane * 0.5 * slayer as f64) / 100.0
        } else {
            1.0 + 0.12 * slayer as f64
        }
    }

    pub(super) fn proc_strike(&mut self, pi: usize, ei: usize, base_dmg: f64, class: u8) -> bool {
        if !self.enemies[ei].alive {
            return false;
        }
        let s = &self.players[pi].s;
        let pct = s.damage_pct + (if class == damage_class::MELEE { s.melee_pct } else if class == damage_class::RANGED { s.ranged_pct } else { s.magic_pct });
        let mut d = base_dmg * 0.1f64.max(1.0 + pct / 100.0) * self.trans_damage_mul(pi);
        let crit = self.rng.chance(s.crit);
        if crit {
            d *= (self.c.rules.crit_damage_pct as f64 + s.crit_damage) / 100.0;
        }
        self.damage_enemy(ei, 1.max(round(d)), pi as i32, 0, crit, false)
    }

    pub(super) fn step_trans_player(&mut self, pi: usize) {
        let tick = self.tick;
        let c = self.c;
        if self.trans[pi].dash_end_tick == tick {
            if let Some(widx) = self.trans[pi].dash_end_widx.take() {
                if widx < self.players[pi].weapons.len() {
                    let lvi = self.lv_of(pi, widx);
                    let rad = self.area_units(pi, 2.6);
                    let dmg = self.trans[pi].dash_end_dmg as f64;
                    let (px, py) = (self.players[pi].x, self.players[pi].y);
                    self.strike_area(pi, widx, lvi, px, py, rad, dmg);
                }
            }
        }

        // VitalFortress ("Immortal Bastion"): every 4s pulses a Max-HP-scaling shockwave and heals
        let fort = self.players[pi].count_passive_trans(c, passive_trans_kind::VITAL_FORTRESS);
        if fort > 0 && tick >= self.trans[pi].fortress_next {
            self.trans[pi].fortress_next = tick + ticks(4.0);
            let (px, py, max_hp, hp) = (self.players[pi].x, self.players[pi].y, self.players[pi].max_hp, self.players[pi].hp);
            let h = 2.max(max_hp * (3 * fort) / 100);
            let new_hp = max_hp.min(hp + h);
            self.players[pi].hp = new_hp;
            if new_hp > hp {
                self.add_event(ev::HEAL, 0, pi as i32, pi as i32, 0, new_hp - hp, px, py);
            }
            let rad = self.area_units(pi, 2.8);
            let dmg = 35.0f64.max(max_hp as f64 * 0.15 * fort as f64);
            let q = self.query(px, py, rad + units(1.5));
            for &i in &q {
                let ei = i as usize;
                let e = &self.enemies[ei];
                let rr = (rad + e.radius) as i64;
                if e.alive && dist2(px, py, e.x, e.y) <= rr * rr {
                    self.proc_strike(pi, ei, dmg, damage_class::MELEE);
                }
            }
            self.add_event_abc(ev::NOVA, 0, pi as i32, -1, 0, fort, px, py, rad as f32 / UNIT as f32, 0.0, 0.0, 0, 0);
        }

        // Windrunner ("Tempest Stride"): moving leaves sparking wind trails
        let wind = self.players[pi].count_passive_trans(c, passive_trans_kind::WINDRUNNER);
        if wind > 0 && self.players[pi].moving && tick >= self.trans[pi].wind_trail_next {
            self.trans[pi].wind_trail_next = tick + ticks(0.6);
            let (px, py) = (self.players[pi].x, self.players[pi].y);
            let rad = self.area_units(pi, 1.6);
            let dmg = 45.0 * wind as f64;
            let q = self.query(px, py, rad + units(1.5));
            for &i in &q {
                let ei = i as usize;
                let e = &self.enemies[ei];
                let rr = (rad + e.radius) as i64;
                if !e.alive || dist2(px, py, e.x, e.y) > rr * rr {
                    continue;
                }
                if !self.proc_strike(pi, ei, dmg, damage_class::MAGIC) && self.enemies[ei].alive && c.enemies[self.enemies[ei].def].effect_resist < 100.0 {
                    let e = &mut self.enemies[ei];
                    e.slow_until = e.slow_until.max(tick + ticks(1.5));
                    e.slow_pct = e.slow_pct.max(35);
                }
            }
            self.add_event_abc(ev::NOVA, 0, pi as i32, -1, 0, wind, px, py, rad as f32 / UNIT as f32, 0.0, 0.0, 0, 0);
        }

        // Archmage ("Arcane Convergence"): every 3.5s erupts an arcane nova and reduces all weapon cooldowns
        let arch = self.players[pi].count_passive_trans(c, passive_trans_kind::ARCHMAGE);
        if arch > 0 && tick >= self.trans[pi].archmage_next {
            self.trans[pi].archmage_next = tick + ticks(3.5);
            let cd_save = ticks(0.35 * arch as f64);
            for ow in self.players[pi].weapons.iter_mut() {
                if ow.next_tick > tick {
                    ow.next_tick = tick.max(ow.next_tick - cd_save);
                }
            }
            let (px, py) = (self.players[pi].x, self.players[pi].y);
            let rad = self.area_units(pi, 3.2);
            let dmg = 90.0 * arch as f64;
            let q = self.query(px, py, rad + units(1.5));
            for &i in &q {
                let ei = i as usize;
                let e = &self.enemies[ei];
                let rr = (rad + e.radius) as i64;
                if e.alive && dist2(px, py, e.x, e.y) <= rr * rr {
                    self.proc_strike(pi, ei, dmg, damage_class::MAGIC);
                }
            }
            self.add_event_abc(ev::NOVA, 0, pi as i32, -1, 0, arch, px, py, rad as f32 / UNIT as f32, 0.0, 0.0, 0, 0);
        }
    }

    pub(super) fn trans_after_hit(&mut self, pi: usize, widx: usize, ei: usize, dmg: i32, crit: bool, mut died: bool) {
        if self.trans[pi].in_chain_effect {
            return;
        }
        self.trans[pi].in_chain_effect = true;
        let c = self.c;
        let tick = self.tick;
        let (w_id, w_class, ow_level) = {
            let ow = &self.players[pi].weapons[widx];
            let w = &c.weapons[ow.def];
            (w.id, w.class, ow.level)
        };
        let (ex, ey) = (self.enemies[ei].x, self.enemies[ei].y);

        // Warlord ("Overwhelming Force"): every 4th hit unleashes a cleave shockwave
        let warlord = self.players[pi].count_passive_trans(c, passive_trans_kind::WARLORD);
        if warlord > 0 {
            self.trans[pi].warlord_hits += 1;
            if self.trans[pi].warlord_hits >= 4 {
                self.trans[pi].warlord_hits = 0;
                let rad = self.area_units(pi, 2.4);
                let cleave = 15.max(round(dmg as f64 * 0.6 * warlord as f64));
                let q = self.query(ex, ey, rad + units(1.5));
                for &i in &q {
                    let oi = i as usize;
                    if oi == ei {
                        continue;
                    }
                    let o = &self.enemies[oi];
                    let rr = (rad + o.radius) as i64;
                    if o.alive && dist2(ex, ey, o.x, o.y) <= rr * rr {
                        self.damage_enemy(oi, cleave, pi as i32, w_id, false, false);
                    }
                }
                self.add_event_abc(ev::STRIKE, 0, pi as i32, -1, w_id, ow_level, ex, ey, rad as f32 / UNIT as f32, 0.0, 0.0, 0, 0);
            }
        }

        // Deadeye ("Ricochet Mastery"): every 3rd ranged hit fires a ricochet bolt to a nearby enemy
        let deadeye = self.players[pi].count_passive_trans(c, passive_trans_kind::DEADEYE);
        if deadeye > 0 && w_class == damage_class::RANGED {
            self.trans[pi].deadeye_hits += 1;
            if self.trans[pi].deadeye_hits >= 3 {
                self.trans[pi].deadeye_hits = 0;
                let ex_list = [ei as i32];
                let next = self.find_target(ex, ey, targeting::NEAREST, units(6.0), 0, Some(&ex_list));
                if next >= 0 {
                    let ni = next as usize;
                    let (nx, ny) = (self.enemies[ni].x, self.enemies[ni].y);
                    self.add_event_abc(ev::CHAIN, 0, pi as i32, next, w_id, ow_level, ex, ey, 0.0, 0.0, 0.0, nx, ny);
                    self.damage_enemy(ni, 10.max(round(dmg as f64 * 0.75 * deadeye as f64)), pi as i32, w_id, false, false);
                }
            }
        }

        // FortuneCrit ("Fated Execution"): crits chain lightning and execute low-HP non-bosses
        let fortune = self.players[pi].count_passive_trans(c, passive_trans_kind::FORTUNE_CRIT);
        if fortune > 0 {
            if !died && self.enemies[ei].alive && c.enemies[self.enemies[ei].def].rank == rank::NORMAL {
                let (ehp, emax) = (self.enemies[ei].hp as i64, self.enemies[ei].max_hp as i64);
                if ehp * 100 <= emax * (12 * fortune as i64) {
                    let rem = self.enemies[ei].hp;
                    self.damage_enemy(ei, rem, pi as i32, w_id, true, false);
                    died = !self.enemies[ei].alive;
                }
            }
            if crit {
                let ex_list = [ei as i32];
                let next = self.find_target(ex, ey, targeting::NEAREST, units(4.5), 0, Some(&ex_list));
                if next >= 0 {
                    let ni = next as usize;
                    let (nx, ny) = (self.enemies[ni].x, self.enemies[ni].y);
                    self.add_event_abc(ev::CHAIN, 0, pi as i32, next, w_id, ow_level, ex, ey, 0.0, 0.0, 0.0, nx, ny);
                    self.damage_enemy(ni, 10.max(round(dmg as f64 * 0.6 * fortune as f64)), pi as i32, w_id, false, false);
                }
            }
        }

        // CritCount ("Lucky Clover"): every 5th crit with Lunatic Card (205), Double Attack (60), or Soldier Skeleton (306) drops a clover pickup (240)
        if crit {
            let has_lucky = self.players[pi].passives.iter().any(|op| {
                let pid = c.passives[op.def].id;
                let root = if pid >= 1000 { pid - 1000 } else { pid };
                matches!(root, 60 | 205 | 306)
            });
            if has_lucky {
                self.crit_proc_stacks[pi] += 1;
                if self.crit_proc_stacks[pi] >= 5 {
                    self.crit_proc_stacks[pi] = 0;
                    if let Some(pd) = c.pickup(240) {
                        let val = c.pickups[pd].value;
                        self.add_pickup(pd, ex, ey, val);
                    }
                }
            }
        }

        // Slayer ("Bane Mastery"): favored targets explode on hit (at most once per 0.5s)
        let slayer = self.players[pi].count_passive_trans(c, passive_trans_kind::SLAYER);
        if slayer > 0 && tick >= self.trans[pi].bane_blast_next {
            let edef = &c.enemies[self.enemies[ei].def];
            let s = &self.players[pi].s;
            let favored = (edef.rank != rank::NORMAL && s.vs_boss > 0.0)
                || s.vs_race[(edef.race % 10) as usize] > 0.0
                || s.vs_element[(edef.element % 10) as usize] > 0.0;
            if favored {
                self.trans[pi].bane_blast_next = tick + ticks(0.5);
                let rad = self.area_units(pi, 2.0);
                let bd = 12.max(round(dmg as f64 * 0.55 * slayer as f64));
                let q = self.query(ex, ey, rad + units(1.5));
                for &i in &q {
                    let oi = i as usize;
                    if oi == ei {
                        continue;
                    }
                    let o = &self.enemies[oi];
                    let rr = (rad + o.radius) as i64;
                    if o.alive && dist2(ex, ey, o.x, o.y) <= rr * rr {
                        self.damage_enemy(oi, bd, pi as i32, w_id, false, false);
                    }
                }
                self.add_event_abc(ev::STRIKE, 0, pi as i32, -1, w_id, ow_level, ex, ey, rad as f32 / UNIT as f32, 0.0, 0.0, 0, 0);
            }
        }
        let _ = died;
        self.trans[pi].in_chain_effect = false;
    }

    pub(super) fn trans_on_kill(&mut self, ei: usize, killer: usize) {
        if !self.players[killer].present || self.trans[killer].in_chain_effect {
            return;
        }
        self.trans[killer].in_chain_effect = true;
        let c = self.c;
        let (ex, ey) = (self.enemies[ei].x, self.enemies[ei].y);
        let harv = self.players[killer].count_passive_trans(c, passive_trans_kind::HARVESTER);
        if harv > 0 {
            self.trans[killer].harvester_kills += 1;
            if self.trans[killer].harvester_kills >= 5 {
                self.trans[killer].harvester_kills = 0;
                self.trigger_harvester_bolts(killer, harv, ex, ey);
            }
        }
        // Bane heal mote drop (pickup 241) on favored kill
        if self.tick >= self.bane_drop_next[killer] {
            let edef = &c.enemies[self.enemies[ei].def];
            let s = &self.players[killer].s;
            let has_bane = self.players[killer].passives.iter().any(|op| {
                let pid = c.passives[op.def].id;
                let root = if pid >= 1000 { pid - 1000 } else { pid };
                matches!(root, 40 | 120 | 140 | 300 | 301)
            });
            let favored = (edef.rank != rank::NORMAL && (s.vs_boss > 0.0 || has_bane))
                || s.vs_race[(edef.race % 10) as usize] > 0.0
                || s.vs_element[(edef.element % 10) as usize] > 0.0;
            if has_bane && favored && self.rng.chance(35.0) {
                self.bane_drop_next[killer] = self.tick + ticks(3.0);
                if let Some(pd) = c.pickup(241) {
                    let val = c.pickups[pd].value;
                    self.add_pickup(pd, ex, ey, val);
                }
            }
        }
        // Making Arrow (passive 32 / 1032): drops arrow bundle (pickup 242)
        let mut arrow_lv = 0;
        for op in &self.players[killer].passives {
            let pid = c.passives[op.def].id;
            if pid == 32 || pid == 1032 {
                arrow_lv = arrow_lv.max(op.level);
            }
        }
        if arrow_lv > 0 && self.rng.chance((4 + 2 * 0.max(arrow_lv - 1)) as f64) {
            if let Some(pd) = c.pickup(242) {
                let val = c.pickups[pd].value;
                self.add_pickup(pd, ex, ey, val);
            }
        }
        self.trans[killer].in_chain_effect = false;
    }

    pub(super) fn trans_on_pickup(&mut self, pi: usize) {
        let harv = self.players[pi].count_passive_trans(self.c, passive_trans_kind::HARVESTER);
        if harv > 0 {
            let (px, py) = (self.players[pi].x, self.players[pi].y);
            self.trigger_harvester_bolts(pi, harv, px, py);
        }
    }

    fn trigger_harvester_bolts(&mut self, pi: usize, harv: i32, from_x: i32, from_y: i32) {
        if !self.players[pi].fallen && self.players[pi].hp < self.players[pi].max_hp {
            let h = 1.max(self.players[pi].max_hp * (2 * harv) / 100);
            self.players[pi].hp = self.players[pi].max_hp.min(self.players[pi].hp + h);
        }
        let mut ex = Vec::new();
        for _ in 0..2 {
            let t = self.find_target(from_x, from_y, targeting::NEAREST, units(8.0), 0, Some(&ex));
            if t < 0 {
                break;
            }
            ex.push(t);
            let ti = t as usize;
            let (tx, ty) = (self.enemies[ti].x, self.enemies[ti].y);
            self.add_event_abc(ev::CHAIN, 0, pi as i32, t, 0, 1, from_x, from_y, 0.0, 0.0, 0.0, tx, ty);
            self.proc_strike(pi, ti, 65.0 * harv as f64, damage_class::MAGIC);
        }
    }

    // =============================================================================================== falling and rising (Soul Retrieval + Yggdrasil Berry)

    pub fn ygg_burnt(&self, pi: usize) -> bool {
        self.ygg_burnt.contains(&self.players[pi].aid)
    }

    pub fn ygg_cool_until(&self, pi: usize) -> i32 {
        self.ygg_until.get(&self.players[pi].aid).copied().unwrap_or(0)
    }

    pub fn is_berry(&self, ow: &OwnedWeapon) -> bool {
        self.c.weapons[ow.def].item_id == 607
    }

    pub(super) fn ygg_off(&self, pi: usize, wi: usize) -> bool {
        self.is_berry(&self.players[pi].weapons[wi]) && (self.ygg_burnt(pi) || self.tick < self.ygg_cool_until(pi))
    }

    pub(super) fn ygg_burnt_weapon(&self, pi: usize, wi: usize) -> bool {
        self.is_berry(&self.players[pi].weapons[wi]) && self.ygg_burnt(pi)
    }

    fn ygg_save(&mut self, pi: usize) -> bool {
        let Some(wi) = self.players[pi].weapons.iter().position(|ow| self.is_berry(ow)) else { return false };
        if self.ygg_off(pi, wi) {
            return false;
        }
        let wid = self.c.weapons[self.players[pi].weapons[wi].def].id;
        let alone = self.players_present() <= 1;
        let tick = self.tick;
        let aid = self.players[pi].aid;
        if alone {
            self.ygg_burnt.insert(aid);
        } else {
            self.ygg_until.insert(aid, tick + ticks(150.0));
        }
        let p = &mut self.players[pi];
        p.hp = p.max_hp;
        p.safe_until = p.safe_until.max(tick + ticks(4.0));
        p.loadout_version += 1;
        let (x, y) = (p.x, p.y);
        self.ygg_clear.push((pi, wid));
        self.add_event(ev::LIFE_SAVED, 0, pi as i32, -1, wid, if alone { 0 } else { 150 }, x, y);
        true
    }

    fn ygg_clear(&mut self) {
        if self.ygg_clear.is_empty() {
            return;
        }
        let clears = std::mem::take(&mut self.ygg_clear);
        let rad = units(4.0);
        let r2 = rad as i64 * rad as i64;
        let push_target = units(4.0 + 2.5);
        let keep = self.kb_keep_pct;
        for (pi, wid) in clears {
            if !self.players[pi].present {
                continue;
            }
            let (px, py) = (self.players[pi].x, self.players[pi].y);
            for ei in 0..self.enemies.len() {
                if !self.enemies[ei].alive {
                    continue;
                }
                let (ex, ey, ehp, erank) = (self.enemies[ei].x, self.enemies[ei].y, self.enemies[ei].hp, self.c.enemies[self.enemies[ei].def].rank);
                let d2 = dist2(ex, ey, px, py);
                if d2 > r2 {
                    continue;
                }
                if erank == rank::NORMAL {
                    self.damage_enemy(ei, 1.max(ehp), pi as i32, wid, false, false);
                    continue;
                }
                let mut dx = (ex - px) as i64;
                let mut dy = (ey - py) as i64;
                let mut d = isqrt(d2);
                if d < 1 {
                    dx = 1;
                    dy = 0;
                    d = 1;
                }
                let need = 16.max(push_target - d);
                let v0 = math::impulse_for(need, keep) as i64;
                let e = &mut self.enemies[ei];
                e.kb_vx = clamp((dx * v0 / d as i64) as i32, -8 * UNIT, 8 * UNIT);
                e.kb_vy = clamp((dy * v0 / d as i64) as i32, -8 * UNIT, 8 * UNIT);
            }
        }
    }

    fn soul_deaths(&self, aid: u32) -> i32 {
        self.soul_deaths.get(&aid).copied().unwrap_or(0)
    }

    fn soul_spawn_mul(&self) -> f64 {
        if !self.souls.is_empty() { 1.20 } else { 1.0 }
    }

    fn souls_fell(&mut self, pi: usize) {
        if !self.players[pi].present {
            return;
        }
        let other = self.players.iter().enumerate().any(|(qi, q)| qi != pi && q.present && !q.fallen);
        let aid = self.players[pi].aid;
        let deaths = self.soul_deaths(aid);
        self.soul_deaths.insert(aid, deaths + 1);
        if !other {
            return;
        }
        let players_present = self.players_present();
        let n = clamp(4 + 0.max(players_present - 2) + 0.max(deaths), 1, 16);
        self.soul_total[pi] = n;
        self.soul_need[pi] = n;
        self.players[pi].revive = 0.0;
        let mut share = [0i32; MAX_PLAYERS];
        for qi in 0..self.players.len() {
            if qi != pi && self.players[qi].present && self.players[qi].fallen && self.soul_need[qi] > 0 {
                share[qi] = self.soul_need[qi].min(n);
            }
        }
        let margin = units(3.0);
        let (mut lx, mut ly, mut hx, mut hy) = (self.min_x + margin, self.min_y + margin, self.max_x - margin, self.max_y - margin);
        if lx > hx {
            lx = (self.min_x + self.max_x) / 2;
            hx = lx;
        }
        if ly > hy {
            ly = (self.min_y + self.max_y) / 2;
            hy = ly;
        }
        let base_ang = self.soul_dice.next_double() * std::f64::consts::PI * 2.0;
        let apart_u = units(3.0) as i64;
        let apart2 = apart_u * apart_u;
        let (px, py) = (self.players[pi].x, self.players[pi].y);
        for i in 0..n {
            let (mut x, mut y) = (px, py);
            for try_n in 0..8 {
                let a = base_ang + (i as f64 + (self.soul_dice.next_double() - 0.5) * 0.6) * std::f64::consts::PI * 2.0 / n as f64 + try_n as f64 * 0.37;
                let d = 12.0 + self.soul_dice.next_double() * 8.0;
                x = clamp(px + units(math::cos(a) * d), lx, hx);
                y = clamp(py + units(math::sin(a) * d), ly, hy);
                if self.souls.iter().all(|s| dist2(s.x, s.y, x, y) >= apart2) {
                    break;
                }
            }
            let mut owners = 1 << pi;
            for (s, &sh) in share.iter().enumerate() {
                if i < sh {
                    owners |= 1 << s;
                }
            }
            self.souls.push(Soul {
                id: self.soul_next_id,
                x,
                y,
                radius: 16.max(units(1.5)),
                start_tick: self.tick,
                owners,
                dropper: pi as i32,
                progress: 0.0,
                claimer: -1,
                crowd: 0,
            });
            self.soul_next_id = if self.soul_next_id >= 255 { 1 } else { self.soul_next_id + 1 };
        }
        self.add_event(ev::SOUL_SPLIT, 0, pi as i32, -1, 0, n, px, py);
    }

    fn step_souls(&mut self) {
        self.ygg_clear();
        let mut fallen = 0i32;
        for s in 0..self.players.len() {
            if self.players[s].present && self.players[s].fallen && self.soul_total[s] > 0 {
                fallen |= 1 << s;
                self.players[s].revive = (self.soul_total[s] - self.soul_need[s]) as f64 / self.soul_total[s] as f64;
            } else if self.soul_total[s] != 0 || self.soul_need[s] != 0 {
                self.soul_total[s] = 0;
                self.soul_need[s] = 0;
                if !self.players[s].fallen {
                    self.players[s].revive = 0.0;
                }
            }
            self.players[s].helped = false;
        }
        let mut i = self.souls.len();
        while i > 0 {
            i -= 1;
            self.souls[i].owners &= fallen;
            if self.souls[i].owners == 0 {
                self.souls.remove(i);
            }
        }
        let full = 1.0 / 1.max(ticks(1.5)) as f64;
        let mut i = self.souls.len();
        while i > 0 {
            i -= 1;
            if i >= self.souls.len() {
                continue;
            }
            if self.souls[i].owners == 0 {
                self.souls.remove(i);
                continue;
            }
            let (sx, sy, srad, prev_claimer) = (self.souls[i].x, self.souls[i].y, self.souls[i].radius, self.souls[i].claimer);
            let sr2 = srad as i64 * srad as i64;
            let mut claimer: Option<usize> = if prev_claimer >= 0 && (prev_claimer as usize) < self.players.len() {
                let p = &self.players[prev_claimer as usize];
                if p.present && !p.fallen && dist2(p.x, p.y, sx, sy) <= sr2 { Some(prev_claimer as usize) } else { None }
            } else {
                None
            };
            if claimer.is_none() {
                for (pi, p) in self.players.iter().enumerate() {
                    if p.present && !p.fallen && dist2(p.x, p.y, sx, sy) <= sr2 {
                        claimer = Some(pi);
                        break;
                    }
                }
            }
            let mut crowd = 0i32;
            for e in &self.enemies {
                if !e.alive || dist2(e.x, e.y, sx, sy) > sr2 {
                    continue;
                }
                let erank = self.c.enemies[e.def].rank;
                crowd += if erank == rank::NORMAL { 1 } else if erank == rank::ELITE { 3 } else { 5 };
            }
            self.souls[i].crowd = crowd;
            self.souls[i].claimer = claimer.map(|c| c as i32).unwrap_or(-1);
            let Some(cl) = claimer else {
                self.souls[i].progress = 0.0f64.max(self.souls[i].progress - full * 0.5);
                continue;
            };
            self.souls[i].progress += full * 20.0f64.max(100.0 - 12.0 * crowd as f64) / 100.0;
            if self.souls[i].progress < 1.0 - 1e-9 {
                continue;
            }
            let s = self.souls.remove(i);
            self.add_event(ev::SOUL_TAKEN, 0, cl as i32, s.id, s.owners, s.dropper, s.x, s.y);
            for pi in 0..self.players.len() {
                if (s.owners & (1 << pi)) == 0 || !self.players[pi].present || !self.players[pi].fallen || self.soul_need[pi] <= 0 {
                    continue;
                }
                self.soul_need[pi] -= 1;
                self.players[pi].revive = (self.soul_total[pi] - self.soul_need[pi]) as f64 / 1.max(self.soul_total[pi]) as f64;
                if self.soul_need[pi] == 0 {
                    self.soul_rise(pi, cl);
                }
            }
            let mut still = 0i32;
            for pi in 0..self.players.len() {
                if self.players[pi].present && self.players[pi].fallen && self.soul_total[pi] > 0 {
                    still |= 1 << pi;
                }
            }
            let mut k = self.souls.len();
            while k > 0 {
                k -= 1;
                self.souls[k].owners &= still;
                if self.souls[k].owners == 0 {
                    self.souls.remove(k);
                }
            }
            i = i.min(self.souls.len());
        }
    }

    fn step_objectives(&mut self) {
        if self.over {
            return;
        }
        let tick = self.tick;
        // 1. Step objective buffs (Well heals 4% MaxHP/sec; expired buffs removed)
        let mut bi = self.objective_buffs.len();
        while bi > 0 {
            bi -= 1;
            let b = self.objective_buffs[bi];
            let Some(pi) = self.player_idx(b.slot) else {
                self.objective_buffs.remove(bi);
                continue;
            };
            let ring_gone = b.kind == 4 && !self.fx.iter().any(|f| f.id == b.fx_id && f.kind == fxk::ORBIT && !f.ended);
            if tick >= b.until || ring_gone {
                self.objective_buffs.remove(bi);
                continue;
            }
            if b.kind == 2 && tick >= b.next_heal {
                self.objective_buffs[bi].next_heal = tick + TICK_RATE;
                let p = &mut self.players[pi];
                if !p.fallen && p.hp < p.max_hp {
                    let h = 1.max(round(p.max_hp as f64 * 4.0 / 100.0));
                    let before = p.hp;
                    p.hp = p.max_hp.min(p.hp + h);
                    let (got, x, y) = (p.hp - before, p.x, p.y);
                    self.add_event(ev::HEAL, 0, pi as i32, -1, 0, got, x, y);
                }
            }
        }

        // 2. Step existing objectives
        let mut oi = self.objectives.len();
        while oi > 0 {
            oi -= 1;
            if oi >= self.objectives.len() {
                continue;
            }
            if self.objectives[oi].state == 2 {
                // Active Pylons
                let (end_tick, owner, ox, oy) = (
                    self.objectives[oi].end_tick,
                    self.objectives[oi].owner,
                    self.objectives[oi].x,
                    self.objectives[oi].y,
                );
                let Some(owner_pi) = self.player_idx(owner) else {
                    self.objectives.remove(oi);
                    continue;
                };
                if tick >= end_tick {
                    self.objectives.remove(oi);
                    continue;
                }
                let half = units(9.0);
                let x0 = self.min_x.max(ox - half);
                let x1 = self.max_x.min(ox + half);
                let y0 = self.min_y.max(oy - half);
                let y1 = self.max_y.min(oy + half);
                let band = 16.max(units(0.75));
                let every = 1.max(ticks(0.5));
                let dmg = round(14.0 * (1.0 + 0.08 * 0.max(self.party_level - 1) as f64)) as f64;
                for s in 0..self.enemies.len() {
                    if !self.enemies[s].alive {
                        continue;
                    }
                    let (ex, ey, eserial) = (self.enemies[s].x, self.enemies[s].y, self.enemies[s].serial);
                    if ex < x0 - band || ex > x1 + band || ey < y0 - band || ey > y1 + band {
                        continue;
                    }
                    let on_wall = (ex - x0).abs() <= band
                        || (ex - x1).abs() <= band
                        || (ey - y0).abs() <= band
                        || (ey - y1).abs() <= band;
                    if !on_wall {
                        continue;
                    }
                    let key = (s as i32) * 256 + eserial;
                    if let Some(&last) = self.objectives[oi].last_hit.get(&key) {
                        if tick - last < every {
                            continue;
                        }
                    }
                    self.objectives[oi].last_hit.insert(key, tick);
                    self.proc_strike(owner_pi, s, dmg, damage_class::MAGIC);
                }
                if self.objectives[oi].last_hit.len() > 2000 {
                    self.objectives[oi].last_hit.clear();
                }
                continue;
            }

            // Up (0) or Defending (1)
            let (ox, oy, orad, okind, ostate, gslot, gserial, prev_claimer) = (
                self.objectives[oi].x,
                self.objectives[oi].y,
                self.objectives[oi].radius,
                self.objectives[oi].kind,
                self.objectives[oi].state,
                self.objectives[oi].guard_slot,
                self.objectives[oi].guard_serial,
                self.objectives[oi].claimer,
            );
            // Guard leash for Chest (6)
            if okind == 6 && gslot >= 0 && (gslot as usize) < self.enemies.len() {
                let gi = gslot as usize;
                if self.enemies[gi].alive && self.enemies[gi].serial == gserial {
                    let leash = units(3.5);
                    let d2 = dist2(self.enemies[gi].x, self.enemies[gi].y, ox, oy);
                    if d2 > leash as i64 * leash as i64 {
                        let d = 1.max(isqrt(d2));
                        self.enemies[gi].x = clamp(ox + ((self.enemies[gi].x - ox) as i64 * leash as i64 / d as i64) as i32, self.min_x, self.max_x);
                        self.enemies[gi].y = clamp(oy + ((self.enemies[gi].y - oy) as i64 * leash as i64 / d as i64) as i32, self.min_y, self.max_y);
                    }
                }
            }
            let r2 = orad as i64 * orad as i64;
            let mut claimer: Option<usize> = if prev_claimer >= 0 && (prev_claimer as usize) < self.players.len() {
                let p = &self.players[prev_claimer as usize];
                if p.present && !p.fallen && dist2(p.x, p.y, ox, oy) <= r2 { Some(prev_claimer as usize) } else { None }
            } else {
                None
            };
            if claimer.is_none() {
                for (pi, p) in self.players.iter().enumerate() {
                    if p.present && !p.fallen && dist2(p.x, p.y, ox, oy) <= r2 {
                        claimer = Some(pi);
                        break;
                    }
                }
            }
            let mut crowd = 0i32;
            for e in &self.enemies {
                if !e.alive || dist2(e.x, e.y, ox, oy) > r2 {
                    continue;
                }
                let erank = self.c.enemies[e.def].rank;
                crowd += if erank == rank::NORMAL { 1 } else if erank == rank::ELITE { 3 } else { 5 };
            }
            self.objectives[oi].crowd = crowd;
            self.objectives[oi].claimer = claimer.map(|c| c as i32).unwrap_or(-1);

            if okind == 7 {
                // Emperium
                if ostate == 0 {
                    let Some(_cl) = claimer else {
                        if tick >= self.objectives[oi].deadline {
                            let o = self.objectives.remove(oi);
                            self.add_event(ev::OBJECTIVE_FADED, 0, -1, o.id, o.kind as i32, 0, o.x, o.y);
                        }
                        continue;
                    };
                    self.objectives[oi].state = 1;
                    self.objectives[oi].next_wave = tick;
                    self.objectives[oi].last_inside = tick;
                }
                if claimer.is_some() {
                    self.objectives[oi].last_inside = tick;
                    self.objectives[oi].progress += 1.0 / 1.max(ticks(30.0)) as f64;
                }
                if self.objectives[oi].progress >= 1.0 - 1e-9 {
                    let cl = claimer.or_else(|| self.players.iter().position(|p| p.present && !p.fallen));
                    if let Some(cl_pi) = cl {
                        self.claim_objective(oi, cl_pi);
                    } else {
                        let o = self.objectives.remove(oi);
                        self.add_event(ev::OBJECTIVE_FADED, 0, -1, o.id, o.kind as i32, 0, o.x, o.y);
                    }
                    continue;
                }
                if tick - self.objectives[oi].last_inside >= 1.max(ticks(6.0)) {
                    let o = self.objectives.remove(oi);
                    self.add_event(ev::OBJECTIVE_FADED, 0, -1, o.id, o.kind as i32, 1, o.x, o.y);
                    continue;
                }
                if tick >= self.objectives[oi].next_wave {
                    self.objectives[oi].next_wave = tick + 1.max(ticks(6.0));
                    self.objectives[oi].waves += 1;
                    let oid = self.objectives[oi].id;
                    let mut def_idx = None;
                    let mut best_from = i32::MIN;
                    for ws in &self.waves {
                        let wd = &self.c.waves[ws.def];
                        let Some(ei) = self.c.enemy(wd.enemy_id) else { continue };
                        if wd.once || self.c.enemies[ei].rank != rank::NORMAL || tick < ws.from || tick >= ws.to || ws.from < best_from {
                            continue;
                        }
                        best_from = ws.from;
                        def_idx = Some(ei);
                    }
                    if def_idx.is_none() {
                        def_idx = self.c.enemies.iter().position(|d| d.rank == rank::NORMAL && d.behavior != behavior::SWEEP);
                    }
                    if let Some(d_idx) = def_idx {
                        let cnt = 1.max(round(4.0 * arr(&self.c.scaling.spawn_mul, self.players_for_scaling())));
                        let a0 = self.obj_dice.next_double() * std::f64::consts::PI * 2.0;
                        let mut made = 0;
                        for k in 0..cnt {
                            if self.enemies_alive >= self.c.rules.max_enemies.min(MAX_ENEMIES as i32) {
                                break;
                            }
                            let a = a0 + k as f64 * std::f64::consts::PI * 2.0 / cnt as f64;
                            let sx = clamp(ox + units(math::cos(a) * 8.0), self.min_x, self.max_x);
                            let sy = clamp(oy + units(math::sin(a) * 8.0), self.min_y, self.max_y);
                            if self.spawn(d_idx, sx, sy, true) >= 0 {
                                made += 1;
                            }
                        }
                        self.add_event(ev::EMPERIUM_WAVE, 0, -1, oid, self.c.enemies[d_idx].id, made, ox, oy);
                    }
                }
                continue;
            }

            let full = 1.0 / 1.max(ticks(3.0)) as f64;
            if let Some(cl_pi) = claimer {
                let pct = 20.0f64.max(100.0 - 12.0 * crowd as f64);
                self.objectives[oi].progress += full * pct / 100.0;
                if self.objectives[oi].progress >= 1.0 - 1e-9 {
                    self.claim_objective(oi, cl_pi);
                    continue;
                }
            } else {
                self.objectives[oi].progress = 0.0f64.max(self.objectives[oi].progress - full * 0.5);
            }
            if tick >= self.objectives[oi].deadline && !(claimer.is_some() && self.objectives[oi].progress > 0.0) {
                let o = self.objectives.remove(oi);
                if o.guard_slot >= 0 && (o.guard_slot as usize) < self.enemies.len() {
                    let gi = o.guard_slot as usize;
                    if self.enemies[gi].alive && self.enemies[gi].serial == o.guard_serial {
                        self.enemies[gi].alive = false;
                        self.enemies_alive = 0.max(self.enemies_alive - 1);
                    }
                }
                self.add_event(ev::OBJECTIVE_FADED, 0, -1, o.id, o.kind as i32, 0, o.x, o.y);
            }
        }

        // 3. Spawn new objectives when due (first at 90s, then every 75-100s)
        if self.obj_next_tick < 0 {
            self.obj_next_tick = 1.max(ticks(90.0));
        }
        let present_n = self.players_present();
        if tick < self.obj_next_tick || present_n == 0 {
            return;
        }
        let want = if present_n >= 3 { 2 } else { 1 };
        let up = self.objectives.iter().filter(|o| o.state != 2).count();
        if up >= want {
            return;
        }
        for _ in up..want {
            self.spawn_objective();
        }
        let every = 75.0 + self.obj_dice.next_double() * 25.0;
        self.obj_next_tick = tick + 1.max(ticks(every));
    }

    fn spawn_objective(&mut self) {
        let weights = [0, 16, 16, 16, 16, 16, 16, 8];
        let other_kind = self.objectives.iter().find(|o| o.state != 2).map(|o| (o.kind, o.x, o.y));
        let mut total = 0;
        for k in 1..=7u8 {
            if k == self.obj_last_kind || other_kind.is_some_and(|(ok, _, _)| ok == k) || (k == 7 && self.emperium_seen) {
                continue;
            }
            total += weights[k as usize];
        }
        if total <= 0 {
            return;
        }
        let mut roll = self.obj_dice.next(total);
        let mut kind = 1u8;
        for k in 1..=7u8 {
            if k == self.obj_last_kind || other_kind.is_some_and(|(ok, _, _)| ok == k) || (k == 7 && self.emperium_seen) {
                continue;
            }
            let kw = weights[k as usize];
            if roll < kw {
                kind = k;
                break;
            }
            roll -= kw;
        }
        let (mut sx, mut sy, mut n) = (0i64, 0i64, 0i64);
        for p in &self.players {
            if p.present && !p.fallen {
                sx += p.x as i64;
                sy += p.y as i64;
                n += 1;
            }
        }
        if n == 0 {
            return;
        }
        let (cx, cy) = ((sx / n) as i32, (sy / n) as i32);
        let margin = units(4.0);
        let (mut lx, mut ly, mut hx, mut hy) = (self.min_x + margin, self.min_y + margin, self.max_x - margin, self.max_y - margin);
        if lx > hx {
            lx = (self.min_x + self.max_x) / 2;
            hx = lx;
        }
        if ly > hy {
            ly = (self.min_y + self.max_y) / 2;
            hy = ly;
        }
        let base_ang = match other_kind {
            Some((_, ox, oy)) => math::atan2((oy - cy) as f64, (ox - cx) as f64) + std::f64::consts::PI,
            None => self.obj_dice.next_double() * std::f64::consts::PI * 2.0,
        };
        let (mut x, mut y) = (cx, cy);
        for try_n in 0..12 {
            let a = if other_kind.is_some() { base_ang + (self.obj_dice.next_double() - 0.5) * 1.2 } else { base_ang + try_n as f64 * 0.9 };
            let d = 9.0 + self.obj_dice.next_double() * 5.0;
            x = clamp(cx + units(math::cos(a) * d), lx, hx);
            y = clamp(cy + units(math::sin(a) * d), ly, hy);
            let near2 = units(5.4) as i64 * units(5.4) as i64;
            let mut ok = self.players.iter().all(|p| !p.present || p.fallen || dist2(p.x, p.y, x, y) >= near2);
            if ok {
                if let Some((_, ox, oy)) = other_kind {
                    let apart2 = units(12.0) as i64 * units(12.0) as i64;
                    if dist2(ox, oy, x, y) < apart2 {
                        ok = false;
                    }
                }
            }
            if ok {
                break;
            }
        }
        let id = self.obj_next_id;
        self.obj_next_id = if self.obj_next_id >= 255 { 1 } else { self.obj_next_id + 1 };
        if kind == 7 {
            self.emperium_seen = true;
        }
        self.obj_last_kind = kind;
        let mut guard_slot = -1;
        let mut guard_serial = 0;
        if kind == 6 {
            // Spawn weakest elite as guard at 50% HP
            if let Some((e_idx, _)) = self.c.enemies.iter().enumerate().filter(|(_, d)| d.rank == rank::ELITE).min_by_key(|(_, d)| d.hp) {
                let gs = self.spawn(e_idx, clamp(x + units(1.2), self.min_x, self.max_x), y, true);
                if gs >= 0 {
                    let gi = gs as usize;
                    self.enemies[gi].max_hp = 1.max(self.enemies[gi].max_hp / 2);
                    self.enemies[gi].hp = self.enemies[gi].max_hp;
                    guard_slot = gs;
                    guard_serial = self.enemies[gi].serial;
                }
            }
        }
        let rad = 16.max(units(if kind == 7 { 4.0 } else { 3.0 }));
        let o = SimObjective {
            id,
            kind,
            state: 0,
            x,
            y,
            radius: rad,
            start_tick: self.tick,
            deadline: self.tick + 1.max(ticks(25.0)),
            end_tick: 0,
            progress: 0.0,
            claimer: -1,
            crowd: 0,
            last_inside: 0,
            next_wave: 0,
            waves: 0,
            guard_slot,
            guard_serial,
            owner: -1,
            last_hit: HashMap::new(),
        };
        self.objectives.push(o);
        self.add_event(ev::OBJECTIVE_UP, 0, -1, id, kind as i32, 25, x, y);
    }

    fn claim_objective(&mut self, oi: usize, claimer_pi: usize) {
        let tick = self.tick;
        let (oid, okind, ox, oy) = (
            self.objectives[oi].id,
            self.objectives[oi].kind,
            self.objectives[oi].x,
            self.objectives[oi].y,
        );
        let (cx, cy) = (self.players[claimer_pi].x, self.players[claimer_pi].y);
        let share2 = units(5.0) as i64 * units(5.0) as i64;
        let mut got = vec![claimer_pi];
        for (pi, p) in self.players.iter().enumerate() {
            if pi != claimer_pi && p.present && !p.fallen && dist2(p.x, p.y, cx, cy) <= share2 {
                got.push(pi);
            }
        }
        self.objectives[oi].progress = 1.0;
        self.objectives[oi].claimer = claimer_pi as i32;
        self.add_event(ev::OBJECTIVE_CLAIMED, 0, claimer_pi as i32, oid, okind as i32, got.len() as i32, ox, oy);
        let mut stays = false;
        let level_scale = 1.0 + 0.08 * 0.max(self.party_level - 1) as f64;
        match okind {
            1 => {
                // Blessing Shrine: +30% attack speed (-23.1% cooldown) for 20s
                let until = tick + 1.max(ticks(20.0));
                for &pi in &got {
                    let p = &mut self.players[pi];
                    if let Some(b) = p.buffs.iter_mut().find(|b| b.weapon_id == OBJ_SHRINE_BUFF_ID) {
                        b.until = b.until.max(until);
                    } else {
                        p.buffs.push(Buff { mods: (0, 0), until, source_slot: -1, weapon_id: OBJ_SHRINE_BUFF_ID });
                    }
                    self.recalc(pi, false);
                    if let Some(ob) = self.objective_buffs.iter_mut().find(|x| x.slot == pi as i32 && x.kind == 1) {
                        ob.until = ob.until.max(until);
                    } else {
                        self.objective_buffs.push(SimObjectiveBuff { slot: pi as i32, kind: 1, until, next_heal: tick, fx_id: 0 });
                    }
                }
            }
            2 => {
                // Sanctuary Well: heals 4% MaxHP/sec for 15s
                let until = tick + 1.max(ticks(15.0));
                for &pi in &got {
                    if let Some(ob) = self.objective_buffs.iter_mut().find(|x| x.slot == pi as i32 && x.kind == 2) {
                        ob.until = ob.until.max(until);
                    } else {
                        self.objective_buffs.push(SimObjectiveBuff { slot: pi as i32, kind: 2, until, next_heal: tick, fx_id: 0 });
                    }
                }
            }
            3 => {
                // Fire Pylons: active for 20s
                self.objectives[oi].state = 2;
                self.objectives[oi].end_tick = tick + 1.max(ticks(20.0));
                self.objectives[oi].owner = claimer_pi as i32;
                self.objectives[oi].last_hit.clear();
                stays = true;
            }
            4 => {
                // Flame Ring: 3 fireballs orbit for 25s
                let until = tick + 1.max(ticks(25.0));
                let dmg = round(12.0 * level_scale) as f64;
                for &pi in &got {
                    if let Some(had) = self.objective_buffs.iter().find(|x| x.slot == pi as i32 && x.kind == 4).copied() {
                        for fi in 0..self.fx.len() {
                            if self.fx[fi].id == had.fx_id && self.fx[fi].kind == fxk::ORBIT && !self.fx[fi].ended {
                                self.end_fx(fi, false);
                            }
                        }
                    }
                    let (px, py) = (self.players[pi].x, self.players[pi].y);
                    let mut f = self.new_fx(fxk::ORBIT);
                    f.owner_slot = pi as i32;
                    f.def_id = 990;
                    f.level = 1;
                    f.x = px;
                    f.y = py;
                    f.count = 3;
                    f.range = 16.max(units(2.2));
                    f.radius = 16.max(units(0.55));
                    f.turns = 0.55;
                    f.angle = 0.0;
                    f.every = 1.max(ticks(0.5));
                    f.end_tick = until;
                    f.base_damage = dmg;
                    f.follows = true;
                    let fx_id = f.id;
                    self.begin_fx(f);
                    if let Some(ob) = self.objective_buffs.iter_mut().find(|x| x.slot == pi as i32 && x.kind == 4) {
                        ob.until = until;
                        ob.fx_id = fx_id;
                    } else {
                        self.objective_buffs.push(SimObjectiveBuff { slot: pi as i32, kind: 4, until, next_heal: 0, fx_id });
                    }
                }
            }
            5 => {
                // Meteor Beacon: 3 meteor strikes on densest crowds within 14 cells
                let area = 16.max(units(2.6));
                let reach = units(14.0);
                let dmg = round(60.0 * level_scale) as f64;
                let mut used: Vec<usize> = Vec::new();
                for k in 0..3 {
                    let mut best: Option<usize> = None;
                    let mut best_near = -1i32;
                    let mut best_d = i64::MAX;
                    for s in 0..self.enemies.len() {
                        if !self.enemies[s].alive || dist2(self.enemies[s].x, self.enemies[s].y, ox, oy) > reach as i64 * reach as i64 {
                            continue;
                        }
                        if used.iter().any(|&u| dist2(self.enemies[u].x, self.enemies[u].y, self.enemies[s].x, self.enemies[s].y) <= area as i64 * area as i64) {
                            continue;
                        }
                        let near = self.enemies.iter().filter(|q| q.alive && dist2(q.x, q.y, self.enemies[s].x, self.enemies[s].y) <= area as i64 * area as i64).count() as i32;
                        let d = dist2(self.enemies[s].x, self.enemies[s].y, ox, oy);
                        if near > best_near || (near == best_near && d < best_d) {
                            best = Some(s);
                            best_near = near;
                            best_d = d;
                        }
                    }
                    let (tx, ty) = match best {
                        Some(b) => {
                            used.push(b);
                            (self.enemies[b].x, self.enemies[b].y)
                        }
                        None => {
                            let a = self.obj_dice.next_double() * std::f64::consts::PI * 2.0;
                            (clamp(ox + units(math::cos(a) * 3.0), self.min_x, self.max_x), clamp(oy + units(math::sin(a) * 3.0), self.min_y, self.max_y))
                        }
                    };
                    let mut f = self.new_fx(fxk::WARNING);
                    f.owner_slot = claimer_pi as i32;
                    f.def_id = 991;
                    f.level = 1;
                    f.x = tx;
                    f.y = ty;
                    f.radius = area;
                    f.end_tick = tick + 1.max(ticks(1.0)) + k * ticks(0.25);
                    f.base_damage = dmg;
                    self.begin_fx(f);
                }
            }
            6 => {
                // Guarded Chest: 3 upgrades each
                for &pi in &got {
                    for _ in 0..3 {
                        self.chest_upgrade(pi);
                    }
                }
                self.objectives[oi].guard_slot = -1;
            }
            7 => {
                // Emperium: 3 upgrades each + magnet
                for &pi in &got {
                    for _ in 0..3 {
                        self.chest_upgrade(pi);
                    }
                }
                for q in self.pickups.iter_mut() {
                    if self.c.pickups[q.def].kind == pickup_kind::EXP && q.pulled_by < 0 {
                        q.pulled_by = claimer_pi as i32;
                        q.pull_tick = tick;
                        self.pickups_pulled.push(q.id);
                    }
                }
            }
            _ => {}
        }
        let secs = match okind {
            1 | 3 => 20,
            2 => 15,
            4 => 25,
            _ => 0,
        };
        for &pi in &got {
            let (px, py) = (self.players[pi].x, self.players[pi].y);
            self.add_event(ev::OBJECTIVE_GOT, 0, pi as i32, oid, okind as i32, secs, px, py);
        }
        if !stays {
            self.objectives.remove(oi);
        }
    }

    fn soul_rise(&mut self, pi: usize, claimer: usize) {
        let (px, py) = (self.players[pi].x, self.players[pi].y);
        let (cx, cy) = (self.players[claimer].x, self.players[claimer].y);
        let a = if px == cx && py == cy { 0.0 } else { math::atan2((py - cy) as f64, (px - cx) as f64) };
        let x = clamp(cx + units(math::cos(a) * 1.0), self.min_x, self.max_x);
        let y = clamp(cy + units(math::sin(a) * 1.0), self.min_y, self.max_y);
        self.rise(pi, x, y);
        self.players[claimer].revives += 1;
        let (rx, ry) = (self.players[pi].x, self.players[pi].y);
        self.add_event(ev::ROSE, 0, pi as i32, claimer as i32, 0, 1, rx, ry);
    }

    fn rise(&mut self, pi: usize, x: i32, y: i32) {
        let until = self.tick + 1.max(ticks(5.0));
        let tick = self.tick;
        {
            let p = &mut self.players[pi];
            p.fallen = false;
            p.revive = 0.0;
            p.helped = false;
            p.x = x;
            p.y = y;
            p.vx = 0;
            p.vy = 0;
            p.moving = false;
            p.inputs.clear();
            p.buffs.retain(|b| b.weapon_id != RISE_BUFF_ID);
            p.buffs.push(Buff { mods: (0, 0), until, source_slot: -1, weapon_id: RISE_BUFF_ID });
        }
        self.recalc(pi, false);
        let p = &mut self.players[pi];
        p.hp = 1.max(round(p.max_hp as f64 * 50.0 / 100.0));
        p.safe_until = p.safe_until.max(until);
        self.soul_total[pi] = 0;
        self.soul_need[pi] = 0;
        for ow in p.weapons.iter_mut() {
            ow.next_tick = ow.next_tick.max(tick + 1);
        }
    }

    fn souls_job_mark(&mut self) {
        let Some(marks) = &self.c.rules.job_marks else { return };
        if !marks.contains(&self.party_level) {
            return;
        }
        for pi in 0..self.players.len() {
            if !self.players[pi].present || !self.players[pi].fallen {
                continue;
            }
            let (px, py) = (self.players[pi].x, self.players[pi].y);
            self.rise(pi, px, py);
            self.add_event(ev::ROSE, 0, pi as i32, -1, 0, 2, px, py);
        }
        let mut still = 0i32;
        for pi in 0..self.players.len() {
            if self.players[pi].present && self.players[pi].fallen && self.soul_total[pi] > 0 {
                still |= 1 << pi;
            }
        }
        let mut k = self.souls.len();
        while k > 0 {
            k -= 1;
            self.souls[k].owners &= still;
            if self.souls[k].owners == 0 {
                self.souls.remove(k);
            }
        }
    }

    fn souls_job_changed(&mut self, pi: usize) {
        let aid = self.players[pi].aid;
        let d = self.soul_deaths(aid);
        if d > 0 {
            self.soul_deaths.insert(aid, 0.max(d - 2));
        }
    }

    /// StepRevive (HordeSim.cs:1178-1201).
    fn step_revive(&mut self) {
        let r = &self.c.rules;
        let any_standing = self.players.iter().any(|p| p.present && !p.fallen);
        for s in 0..self.players.len() {
            if !self.players[s].present || !self.players[s].fallen {
                continue;
            }
            self.players[s].helped = false;
            if !any_standing {
                continue;
            }
            let rad = units(r.revive_radius as f64);
            let (px, py) = (self.players[s].x, self.players[s].y);
            let helper = self.players.iter().enumerate().position(|(qi, q)| qi != s && q.present && !q.fallen && dist2(q.x, q.y, px, py) <= rad as i64 * rad as i64);
            self.players[s].helped = helper.is_some();
            // (helper != null ? Math.Max(1, R.ReviveNearMul) : 1) is a float
            let near: f32 = if helper.is_some() { 1f32.max(r.revive_near_mul) } else { 1.0 };
            let rate = 1.0 / 1.max(ticks(r.revive_seconds as f64)) as f64 * near as f64 * 0.1f64.max(1.0 + self.players[s].s.revive_speed_pct / 100.0);
            let tick = self.tick;
            let p = &mut self.players[s];
            p.revive += rate;
            if p.revive < 1.0 {
                continue;
            }
            p.fallen = false;
            p.revive = 0.0;
            // p.MaxHp * R.ReviveHpPct is int × float: single precision
            p.hp = 1.max(round((p.max_hp as f32 * r.revive_hp_pct) as f64 / 100.0));
            p.safe_until = tick + ticks(r.revive_safe_seconds as f64);
            for ow in p.weapons.iter_mut() {
                ow.next_tick = ow.next_tick.max(tick + 1);
            }
            if let Some(h) = helper {
                self.players[h].revives += 1;
            }
            self.add_event(ev::ROSE, 0, s as i32, helper.map(|h| h as i32).unwrap_or(-1), 0, 0, px, py);
        }
    }

    /// CheckEnd (HordeSim.cs:1203-1211).
    fn check_end(&mut self) {
        if self.over {
            return;
        }
        let present = self.players.iter().filter(|p| p.present).count();
        let standing = self.players.iter().filter(|p| p.present && !p.fallen).count();
        if present > 0 && standing == 0 {
            self.end(outcome::DEFEAT);
            return;
        }
        if !self.c.is_v4() {
            if self.final_phase && self.tick >= self.run_ticks + self.final_ticks {
                self.end(outcome::TIME_UP);
                return;
            }
            if self.final_phase && self.c.enemy(self.c.rules.final_boss_enemy_id).is_none() && self.tick >= self.run_ticks {
                self.end(outcome::TIME_UP);
            }
        }
    }

    /// End (HordeSim.cs:1214-1219): ends the run (the host may stop it: Stopped).
    pub fn end(&mut self, o: u8) {
        if self.over {
            return;
        }
        // Item 56: if the party survived into endless mode past 15:00 and then falls, count as Victory.
        let o = if self.c.is_v4() && o == outcome::DEFEAT && (self.final_phase || self.wave_cycle > 0) { outcome::VICTORY } else { o };
        self.over = true;
        self.outcome = o;
        self.end_tick = self.tick;
        self.held_by = -1;
        self.pause_count_by = -1;
        for s in 0..self.players.len() {
            if self.players[s].present {
                self.close_offers(s, false);
            }
        }
    }

    // =============================================================================================== helpers

    #[allow(clippy::too_many_arguments)]
    pub(super) fn add_event(&mut self, kind: u8, flags: u8, slot: i32, target: i32, def_id: i32, value: i32, x: i32, y: i32) {
        self.events.push(Event { kind, flags, tick: self.tick, slot, target, def_id, value, x, y, ..Default::default() });
    }

    #[allow(clippy::too_many_arguments)]
    pub(super) fn add_event_abc(&mut self, kind: u8, flags: u8, slot: i32, target: i32, def_id: i32, value: i32, x: i32, y: i32, a: f32, b: f32, c: f32, x2: i32, y2: i32) {
        self.events.push(Event { kind, flags, tick: self.tick, slot, target, def_id, value, x, y, x2, y2, a, b, c });
    }

    /// StateHash (HordeSim.cs:1232-1242): a hash of the whole state.
    pub fn state_hash(&self) -> u64 {
        let mut h: u64 = 1469598103934665603;
        let mut m = |v: i64| {
            h ^= v as u64;
            h = h.wrapping_mul(1099511628211);
        };
        m(self.tick as i64);
        m(self.enemies_alive as i64);
        m(self.rng.state0() as i64);
        m(self.rng.state1() as i64);
        m(self.party_level as i64);
        m(self.party_exp as i64);
        for p in &self.players {
            if !p.present {
                continue;
            }
            for v in [p.x, p.y, p.vx, p.vy, p.hp, p.level, p.job_id, p.kills, p.fallen as i32] {
                m(v as i64);
            }
            for w in &p.weapons {
                m(self.c.weapons[w.def].id as i64);
                m(w.level as i64);
            }
        }
        for e in &self.enemies {
            if !e.alive {
                continue;
            }
            for v in [e.slot, e.x, e.y, e.kb_vx, e.kb_vy, e.hp, self.c.enemies[e.def].id] {
                m(v as i64);
            }
        }
        for k in &self.pickups {
            for v in [k.id, k.x, k.y, k.value, k.pulled_by, k.pull_tick] {
                m(v as i64);
            }
        }
        for f in &self.fx {
            for v in [f.id, f.x, f.y, f.end_tick] {
                m(v as i64);
            }
        }
        h
    }

    /// Results (HordeSim.Weapons.cs:943-977). `size`: HordeRewardSize (0 off, 1 low, 2 normal, 3 high).
    pub fn results(&self, run_id: u32, size: u8) -> Results {
        let c = self.c;
        let seconds: f32 = (if self.over { self.end_tick } else { self.tick }) as f32 / TICK_RATE as f32;
        let total: f32 = 1f32.max(seconds);
        let mut res = Results { run_id, outcome: self.outcome, seconds, players: Vec::new() };
        for p in &self.players {
            if !p.used {
                continue;
            }
            let end = if p.left { p.left_tick } else if self.over { self.end_tick } else { self.tick };
            let secs: f32 = 0.max(end - p.join_tick) as f32 / TICK_RATE as f32;
            let sc = &c.score;
            let own: i64 = p.kills as i64 * sc.per_kill as i64
                + p.elite_kills as i64 * sc.per_elite_kill as i64
                + p.boss_kills as i64 * sc.per_boss_kill as i64
                + (p.level - 1) as i64 * sc.per_run_level as i64
                + p.tier as i64 * sc.per_job_tier as i64
                + p.damage_dealt / 1000 * sc.per_damage1000 as i64
                + p.healing / 100 * sc.per_heal100 as i64
                + p.revives as i64 * sc.per_revive as i64;
            // (long)(secs * sc.PerSecond): float × int in single; sc.Victory * Math.Min(1f, secs / total): single, then Math.Round(double)
            let party: i64 = (secs * sc.per_second as f32) as i64
                + if self.outcome == outcome::VICTORY && !p.left { ((sc.victory as f32 * 1f32.min(secs / total)) as f64).round_ties_even() as i64 } else { 0 };
            let score = (i32::MAX as i64).min(0.max(own + party)) as i32;
            let (bs, js) = if c.is_v4() {
                reward_shares_v4(&c.reward, p.kills, p.elite_kills, p.boss_kills, secs, size)
            } else {
                reward_shares(&c.reward, score, secs, size)
            };
            res.players.push(PlayerResult {
                slot: p.slot, aid: p.aid, name: p.name.clone(), level: p.level, job_id: p.job_id, kills: p.kills, elite_kills: p.elite_kills, boss_kills: p.boss_kills,
                falls: p.falls, revives: p.revives, damage: p.damage_dealt, healing: p.healing, seconds: secs, score, base_share: bs, job_share: js,
                reward: if bs > 0 || js > 0 { 1 } else { 0 }, left: p.left,
            });
        }
        if c.is_v4() {
            // Point 25: a player who left and came back has two slot records; merge them into one result row by Aid
            let mut i = 0;
            while i < res.players.len() {
                let mut j = i + 1;
                while j < res.players.len() {
                    if res.players[j].aid != 0 && res.players[j].aid == res.players[i].aid {
                        let b = res.players.remove(j);
                        let a = &mut res.players[i];
                        a.slot = b.slot;
                        a.level = a.level.max(b.level);
                        if b.job_id != 0 {
                            a.job_id = b.job_id;
                        }
                        a.kills += b.kills;
                        a.elite_kills += b.elite_kills;
                        a.boss_kills += b.boss_kills;
                        a.falls += b.falls;
                        a.revives += b.revives;
                        a.damage += b.damage;
                        a.healing += b.healing;
                        a.seconds += b.seconds;
                        a.score += b.score;
                        a.left = b.left;
                        let (bs, js) = reward_shares_v4(&c.reward, a.kills, a.elite_kills, a.boss_kills, a.seconds, size);
                        a.base_share = bs;
                        a.job_share = js;
                        a.reward = if bs > 0 || js > 0 { 1 } else { 0 };
                    } else {
                        j += 1;
                    }
                }
                i += 1;
            }
        }
        res
    }
}

/// HordeReward.MaxShare: the most any run can give, whatever the tables say: 100 % of a level.
pub const MAX_SHARE: i32 = 10000;

/// HordeReward.Shares (v3): from a score to the real character's reward.
pub fn reward_shares(r: &RewardDef, score: i32, seconds_in_run: f32, size: u8) -> (i32, i32) {
    if size == 0 || seconds_in_run < r.min_seconds || score <= 0 {
        return (0, 0);
    }
    let f = if r.score_for_full > 0 { 1f64.min(score as f64 / r.score_for_full as f64) } else { 1.0 };
    let i = size as usize;
    let cap = |a: &Option<Vec<f32>>| -> f64 {
        match a {
            Some(a) if i < a.len() => a[i] as f64,
            _ => 0.0,
        }
    };
    let bs = 0.max(MAX_SHARE.min(round(cap(&r.base_cap_pct) * 100.0 * f)));
    let js = 0.max(MAX_SHARE.min(round(cap(&r.job_cap_pct) * 100.0 * f)));
    (bs, js)
}

/// HordeReward.Shares (HordeSim.Weapons.cs:987-1007, Item 56): from time and kills to the real character's reward.
pub fn reward_shares_v4(r: &RewardDef, kills: i32, elite_kills: i32, boss_kills: i32, seconds_in_run: f32, size: u8) -> (i32, i32) {
    if size == 0 || seconds_in_run < r.min_seconds {
        return (0, 0);
    }
    let full_sec = if r.seconds_for_full > 0.0 { r.seconds_for_full } else { 900.0 };
    let time_f = 1.0f64.min((seconds_in_run / full_sec) as f64);
    let weighted_kills = 0.max(kills) + 0.max(elite_kills) * 5 + 0.max(boss_kills) * 25;
    let expected_kills = 20.0f64.max(seconds_in_run as f64 * 0.6);
    let kill_f = 1.25f64.min(weighted_kills as f64 / expected_kills);
    let f = 1.0f64.min(time_f * (0.35 + 0.65 * kill_f));
    if weighted_kills <= 0 && seconds_in_run < 60.0 {
        return (0, 0);
    }
    let i = size as usize;
    let cap = |a: &Option<Vec<f32>>| -> f64 {
        match a {
            Some(a) if i < a.len() => a[i] as f64,
            _ => 0.0,
        }
    };
    let bs = 0.max(MAX_SHARE.min(round(cap(&r.base_cap_pct) * 100.0 * f)));
    let js = 0.max(MAX_SHARE.min(round(cap(&r.job_cap_pct) * 100.0 * f)));
    (bs, js)
}
