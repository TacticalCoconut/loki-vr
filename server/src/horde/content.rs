//! Horde's content types (client/Assets/Loki/Runtime/Horde/Core/HordeContent.cs) and their loader. The tables are NOT
//! written here: the C# tables (Horde/Core/Content/HordeTables.cs) stay the single source; tools/hordegolden writes
//! `HordeTables.Build()` out as server/data/horde/content.bin (ContentWriter.cs: field by field, floats as the IEEE bits
//! .NET computed), and this file reads exactly that. Every number in them is Loki's own design, none an official value.

/// HordeContent.cs enums, as their numbers (the wire and the tables carry the numbers).
pub mod weapon_kind {
    pub const BOLT: u8 = 1;
    pub const MELEE_ARC: u8 = 2;
    pub const NOVA: u8 = 3;
    pub const AREA_AT_TARGET: u8 = 4;
    pub const GROUND_ZONE: u8 = 5;
    pub const ORBITER: u8 = 6;
    pub const CHAIN: u8 = 7;
    pub const AURA: u8 = 8;
    pub const SUMMON: u8 = 9;
    pub const TRAP: u8 = 10;
}
pub mod targeting {
    pub const NEAREST: u8 = 0;
    pub const RANDOM: u8 = 1;
    pub const STRONGEST: u8 = 2;
    pub const FARTHEST: u8 = 3;
    pub const FACING: u8 = 4;
    pub const SELF: u8 = 5;
    pub const LOWEST_HP_ALLY: u8 = 6;
    pub const RANDOM_ALLY: u8 = 7;
    pub const DENSEST: u8 = 8;
}
pub mod damage_class {
    pub const MELEE: u8 = 0;
    pub const RANGED: u8 = 1;
    pub const MAGIC: u8 = 2;
}
pub mod effect_kind {
    pub const SLOW: u8 = 1;
    pub const FREEZE: u8 = 2;
    pub const STUN: u8 = 3;
    pub const DOT: u8 = 4;
    pub const KNOCKBACK: u8 = 5;
}
pub mod zone_flags {
    pub const BLOCKS_ENEMIES: u8 = 1;
    pub const BLOCKS_SHOTS: u8 = 2;
    pub const FOLLOWS_OWNER: u8 = 4;
}
pub mod rank {
    pub const NORMAL: u8 = 0;
    pub const ELITE: u8 = 1;
    pub const BOSS: u8 = 2;
    pub const FINAL_BOSS: u8 = 3;
}
pub mod behavior {
    pub const CHARGE: u8 = 1;
    pub const RANGED: u8 = 2;
    pub const SWEEP: u8 = 3;
    pub const EXPLODER: u8 = 4;
    pub const STILL: u8 = 5;
}
pub mod ability {
    pub const SUMMON_ADDS: u8 = 1;
    pub const NOVA: u8 = 2;
    pub const AREA_AT_PLAYERS: u8 = 3;
    pub const CHARGE: u8 = 4;
    pub const BARRAGE: u8 = 5;
}
pub mod pattern {
    pub const CLUSTER: u8 = 1;
    pub const CIRCLE: u8 = 2;
    pub const EDGE: u8 = 3;
}
pub mod pickup_kind {
    pub const EXP: u8 = 1;
    pub const HEAL: u8 = 2;
    pub const MAGNET: u8 = 3;
    pub const CHEST: u8 = 4;
    pub const BOMB: u8 = 5;
    pub const PASSIVE: u8 = 6;
}
/// HordeContent.cs Mod.
pub mod modk {
    pub const MAX_HP: u8 = 1;
    pub const MAX_HP_PCT: u8 = 2;
    pub const REGEN: u8 = 3;
    pub const MOVE_SPEED_PCT: u8 = 4;
    pub const DAMAGE_PCT: u8 = 5;
    pub const MELEE_DAMAGE_PCT: u8 = 6;
    pub const RANGED_DAMAGE_PCT: u8 = 7;
    pub const MAGIC_DAMAGE_PCT: u8 = 8;
    pub const COOLDOWN_PCT: u8 = 9;
    pub const AREA_PCT: u8 = 10;
    pub const PROJECTILE_COUNT: u8 = 11;
    pub const PROJECTILE_SPEED_PCT: u8 = 12;
    pub const DURATION_PCT: u8 = 13;
    pub const PICKUP_RADIUS_PCT: u8 = 14;
    pub const CRIT_CHANCE: u8 = 15;
    pub const CRIT_DAMAGE_PCT: u8 = 16;
    pub const EXP_GAIN_PCT: u8 = 17;
    pub const DEFENCE: u8 = 18;
    pub const DAMAGE_TAKEN_PCT: u8 = 19;
    pub const DODGE: u8 = 20;
    pub const HEAL_POWER_PCT: u8 = 21;
    pub const REVIVE_SPEED_PCT: u8 = 22;
    pub const STR: u8 = 23;
    pub const AGI: u8 = 24;
    pub const VIT: u8 = 25;
    pub const INT: u8 = 26;
    pub const DEX: u8 = 27;
    pub const LUK: u8 = 28;
    pub const DAMAGE_VS_RACE_PCT: u8 = 29;
    pub const DAMAGE_VS_ELEMENT_PCT: u8 = 30;
    pub const ELEMENT_DAMAGE_PCT: u8 = 31;
    pub const DAMAGE_VS_BOSS_PCT: u8 = 32;
    pub const EFFECT_CHANCE_PCT: u8 = 33;
    pub const KNOCKBACK_PCT: u8 = 34;
    pub const HP_ON_KILL: u8 = 35;
}

pub mod trans_kind {
    pub const NONE: u8 = 0;
    pub const BOLT_NOVA: u8 = 1;
    pub const BOLT_RETURN: u8 = 2;
    pub const ARC_VORTEX: u8 = 3;
    pub const ARC_WAVE: u8 = 4;
    pub const NOVA_ECHO: u8 = 5;
    pub const NOVA_IMPLODE: u8 = 6;
    pub const AREA_CASCADE: u8 = 7;
    pub const AREA_SCORCH: u8 = 8;
    pub const ZONE_DRIFT: u8 = 9;
    pub const ZONE_COLLAPSE: u8 = 10;
    pub const ORBIT_SPIRAL: u8 = 11;
    pub const ORBIT_SHOOT: u8 = 12;
    pub const CHAIN_FORK: u8 = 13;
    pub const CHAIN_OVERLOAD: u8 = 14;
    pub const AURA_PULSE: u8 = 15;
    pub const AURA_NOVA: u8 = 16;
    pub const SUMMON_SWARM: u8 = 17;
    pub const SUMMON_EXPLODE: u8 = 18;
    pub const TRAP_SEEKER: u8 = 19;
    pub const TRAP_CLUSTER: u8 = 20;
    pub const DASH_MIRAGE: u8 = 21;
    pub const DASH_NOVA: u8 = 22;
    pub const MARK_SPREAD: u8 = 23;
    pub const MARK_DETONATE: u8 = 24;
    pub const PARRY_TEMPEST: u8 = 25;
    pub const PARRY_PRISM: u8 = 26;
    pub const PET_TWIN: u8 = 27;
    pub const PET_APEX: u8 = 28;
    pub const RANDOM_TWIN: u8 = 29;
    pub const RANDOM_OVERLOAD: u8 = 30;
}

pub mod passive_trans_kind {
    pub const NONE: u8 = 0;
    pub const WARLORD: u8 = 1;
    pub const DEADEYE: u8 = 2;
    pub const ARCHMAGE: u8 = 3;
    pub const VITAL_FORTRESS: u8 = 4;
    pub const WINDRUNNER: u8 = 5;
    pub const FORTUNE_CRIT: u8 = 6;
    pub const SLAYER: u8 = 7;
    pub const HARVESTER: u8 = 8;
}

#[derive(Clone, Debug)]
pub struct ModDef {
    pub kind: u8,
    pub value: f32,
    pub arg: i32,
}

#[derive(Clone, Debug)]
pub struct EffectDef {
    pub kind: u8,
    pub chance: f32,
    pub duration: f32,
    pub power: f32,
    pub tick: f32,
}

#[derive(Clone, Debug)]
pub struct WeaponLevel {
    pub damage: i32,
    pub cooldown: f32,
    pub count: i32,
    pub pierce: i32,
    pub speed: f32,
    pub range: f32,
    pub area: f32,
    pub arc: f32,
    pub spread: f32,
    pub burst: f32,
    pub delay: f32,
    pub duration: f32,
    pub tick: f32,
    pub heal: i32,
    pub heal_pct: f32,
    pub falloff: f32,
    /// null in the tables = None (the engine tells null from empty); an element may be null too.
    pub effects: Option<Vec<Option<EffectDef>>>,
    pub buffs: Option<Vec<Option<ModDef>>>,
}

#[derive(Clone, Debug)]
pub struct WeaponDef {
    pub id: i32,
    pub key: String,
    pub skill_id: i32,
    pub item_id: i32,
    pub kind: u8,
    pub target: u8,
    pub class: u8,
    pub element: u8,
    pub zone: u8,
    pub homing: bool,
    pub summon_mob_id: i32,
    pub summon_hp: i32,
    pub weight: i32,
    pub levels: Vec<WeaponLevel>,
    pub branch_of: i32,
    pub branch: i32,
    pub branches: Option<Vec<i32>>,
    pub evolve_of: i32,
    pub needs_weapon: i32,
    pub needs_passive: i32,
    pub group: i32,
    pub hidden: bool,
    pub trans_of: i32,
    pub trans_branch: i32,
    pub trans_branches: Option<Vec<i32>>,
    pub trans: u8,
    pub random_of: Option<Vec<i32>>,
}

impl WeaponDef {
    pub fn root_id(&self) -> i32 {
        if self.trans_of > 0 {
            self.trans_of
        } else if self.branch_of > 0 {
            self.branch_of
        } else if self.evolve_of > 0 {
            self.evolve_of
        } else {
            self.id
        }
    }
}

#[derive(Clone, Debug)]
pub struct PassiveDef {
    pub id: i32,
    pub key: String,
    pub skill_id: i32,
    pub item_id: i32,
    pub weight: i32,
    pub levels: Vec<Option<Vec<Option<ModDef>>>>,
    pub group: i32,
    pub gives_weapon: i32,
    pub trans_of: i32,
    pub trans: u8,
}

impl PassiveDef {
    pub fn root_id(&self) -> i32 {
        if self.trans_of > 0 {
            self.trans_of
        } else {
            self.id
        }
    }
}

#[derive(Clone, Debug)]
pub struct StatDef {
    pub stat: u8,
    pub per_point: Option<Vec<Option<ModDef>>>,
}

#[derive(Clone, Debug)]
pub struct JobDef {
    pub job_id: i32,
    pub key: String,
    pub tier: i32,
    pub next: Option<Vec<i32>>,
    pub signature_weapon: i32,
    pub weapons: Option<Vec<i32>>,
    pub passives: Option<Vec<i32>>,
    pub mods: Option<Vec<Option<ModDef>>>,
    pub stat_bonus: Option<Vec<i32>>,
    pub sex: u8,
}

#[derive(Clone, Debug)]
pub struct EnemyAbilityDef {
    pub kind: u8,
    pub cooldown: f32,
    pub first_after: f32,
    pub damage: i32,
    pub area: f32,
    pub delay: f32,
    pub speed: f32,
    pub range: f32,
    pub count: i32,
    pub enemy_id: i32,
    pub skill_id: i32,
}

#[derive(Clone, Debug)]
pub struct DropDef {
    pub pickup_id: i32,
    pub chance: f32,
    pub scatter_cells: f32,
}

#[derive(Clone, Debug)]
pub struct EnemyDef {
    pub id: i32,
    pub mob_id: i32,
    pub key: String,
    pub rank: u8,
    pub behavior: u8,
    pub hp: i32,
    pub damage: i32,
    pub attack_interval: f32,
    pub speed: f32,
    pub radius: f32,
    pub exp: i32,
    pub element: u8,
    pub race: u8,
    pub size: u8,
    pub knockback_resist: f32,
    pub effect_resist: f32,
    pub shot_damage: i32,
    pub shot_range: f32,
    pub shot_speed: f32,
    pub keep_range: f32,
    pub charge_cooldown: f32,
    pub charge_windup: f32,
    pub charge_speed: f32,
    pub burst_area: f32,
    pub draw_scale: f32,
    pub abilities: Vec<EnemyAbilityDef>,
    pub drops: Vec<Option<DropDef>>,
}

#[derive(Clone, Debug)]
pub struct WaveDef {
    pub from: f32,
    pub to: f32,
    pub enemy_id: i32,
    pub interval: f32,
    pub count: i32,
    pub max_alive: i32,
    pub pattern: u8,
    pub once: bool,
    pub warn_seconds: f32,
}

#[derive(Clone, Debug)]
pub struct PickupDef {
    pub id: i32,
    pub key: String,
    pub kind: u8,
    pub item_id: i32,
    pub value: i32,
    pub area: f32,
}

#[derive(Clone, Debug, Default)]
pub struct ArenaDef {
    pub id: i32,
    pub map: String,
    pub x0: i32,
    pub y0: i32,
    pub x1: i32,
    pub y1: i32,
}

impl ArenaDef {
    pub fn scaled_for_party(&self, party_count: usize) -> ArenaDef {
        if party_count <= 1 {
            return self.clone();
        }
        let mul = 1.0 + 0.20 * (party_count - 1) as f64;
        let w = 250.min(((self.x1 - self.x0) as f64 * mul).round() as i32);
        let h = 250.min(((self.y1 - self.y0) as f64 * mul).round() as i32);
        let cx = (self.x0 + self.x1) / 2;
        let cy = (self.y0 + self.y1) / 2;
        let x0 = 16.max(cx - w / 2);
        let y0 = 16.max(cy - h / 2);
        ArenaDef { id: self.id, map: self.map.clone(), x0, y0, x1: x0 + w, y1: y0 + h }
    }
}

#[derive(Clone, Debug, Default)]
pub struct RulesDef {
    pub reference_run_seconds: f32,
    pub final_boss_enemy_id: i32,
    pub final_boss_seconds: f32,
    pub final_boss_warn_seconds: f32,
    pub max_enemies: i32,
    pub start_hp: i32,
    pub hp_per_level: i32,
    pub move_speed: f32,
    pub move_accel_seconds: f32,
    pub move_decel_seconds: f32,
    pub wall_soft_cells: f32,
    pub knockback_keep_pct: f32,
    pub pickup_pull_speed: f32,
    pub pickup_pull_accel: f32,
    pub body_radius: f32,
    pub pickup_radius: f32,
    pub pickup_radius_per_level: f32,
    pub hurt_grace_seconds: f32,
    pub crit_chance: f32,
    pub crit_damage_pct: f32,
    pub start_weapon: i32,
    pub max_weapons: i32,
    pub max_passives: i32,
    pub exp_table: Option<Vec<i32>>,
    pub exp_growth_pct: f32,
    pub max_level: i32,
    pub exp_share_pct: f32,
    pub party_exp_scale: Option<Vec<f32>>,
    pub cards_per_offer: i32,
    pub choice_seconds: f32,
    pub choice_seconds_step: f32,
    pub choice_seconds_min: f32,
    pub job_marks: Option<Vec<i32>>,
    pub weight_new_weapon: i32,
    pub weight_weapon_level: i32,
    pub weight_new_passive: i32,
    pub weight_passive_level: i32,
    pub weight_stat: i32,
    pub stat_card_points: i32,
    pub revive_seconds: f32,
    pub revive_radius: f32,
    pub revive_near_mul: f32,
    pub revive_hp_pct: f32,
    pub revive_safe_seconds: f32,
}

#[derive(Clone, Debug, Default)]
pub struct ScalingDef {
    pub spawn_mul: Option<Vec<f32>>,
    pub enemy_hp_mul: Option<Vec<f32>>,
    pub elite_hp_mul: Option<Vec<f32>>,
    pub boss_hp_mul: Option<Vec<f32>>,
    pub enemy_damage_mul: Option<Vec<f32>>,
    pub enemy_hp_pct_per_minute: f32,
    pub enemy_damage_pct_per_minute: f32,
}

#[derive(Clone, Debug, Default)]
pub struct ScoreDef {
    pub per_kill: i32,
    pub per_elite_kill: i32,
    pub per_boss_kill: i32,
    pub per_run_level: i32,
    pub per_job_tier: i32,
    pub per_damage1000: i32,
    pub per_heal100: i32,
    pub per_revive: i32,
    pub per_second: i32,
    pub victory: i32,
}

#[derive(Clone, Debug, Default)]
pub struct RewardDef {
    pub score_for_full: i32,
    pub seconds_for_full: f32,
    pub min_seconds: f32,
    pub base_cap_pct: Option<Vec<f32>>,
    pub job_cap_pct: Option<Vec<f32>>,
}

/// HordeContent: everything a run is made of. Built once; the engine never changes it.
#[derive(Clone, Debug, Default)]
pub struct Content {
    /// HordeWire.ContentStamp as the C# side computed it for these tables ("h4-…"); [`Content::stamp`] computes it again.
    pub stamp_cs: String,
    pub rules: RulesDef,
    pub scaling: ScalingDef,
    pub score: ScoreDef,
    pub reward: RewardDef,
    pub weapons: Vec<WeaponDef>,
    pub passives: Vec<PassiveDef>,
    pub jobs: Vec<JobDef>,
    pub enemies: Vec<EnemyDef>,
    pub waves: Vec<WaveDef>,
    pub pickups: Vec<PickupDef>,
    pub arenas: Vec<ArenaDef>,
    pub stats: Vec<StatDef>,
    pub element_pct: Option<[[i32; 10]; 10]>,
}

impl Content {
    pub fn is_v4(&self) -> bool {
        self.stamp_cs.starts_with("h4-")
    }

    // HordeContent.Weapon(id) … : the first def with that id (the tables' lists are short: a scan, as in C#).
    pub fn weapon(&self, id: i32) -> Option<usize> {
        self.weapons.iter().position(|w| w.id == id)
    }
    pub fn passive(&self, id: i32) -> Option<usize> {
        self.passives.iter().position(|w| w.id == id)
    }
    pub fn job(&self, job_id: i32) -> Option<usize> {
        self.jobs.iter().position(|w| w.job_id == job_id)
    }
    pub fn enemy(&self, id: i32) -> Option<usize> {
        self.enemies.iter().position(|w| w.id == id)
    }
    pub fn pickup(&self, id: i32) -> Option<usize> {
        self.pickups.iter().position(|w| w.id == id)
    }
    pub fn arena(&self, id: i32) -> Option<usize> {
        self.arenas.iter().position(|w| w.id == id)
    }
    /// The Novice: the one job of tier 0.
    pub fn start_job(&self) -> Option<usize> {
        self.jobs.iter().position(|w| w.tier == 0)
    }

    /// HordeWire.ContentStamp (HordeWire.cs:324-380): the hash of everything the engine reads, numbers in 1/1000
    /// steps (floor(v × 1000 + 0.25)), so the same sources give the same stamp on Unity's Mono, on .NET and here.
    pub fn stamp(&self) -> String {
        if self.is_v4() {
            return self.stamp_cs.clone();
        }
        struct H(u64);
        impl H {
            fn m(&mut self, v: f64) {
                let q: i64 = if v.is_nan() {
                    i64::MIN
                } else if v.is_infinite() {
                    if v > 0.0 { i64::MAX } else { i64::MIN + 1 }
                } else {
                    ((v * 1000.0).min(9e15).max(-9e15) + 0.25).floor() as i64
                };
                self.0 ^= q as u64;
                self.0 = self.0.wrapping_mul(1099511628211);
            }
            fn s(&mut self, s: &str) {
                for ch in s.encode_utf16() {
                    self.m(ch as f64);
                }
                self.m(-1.0);
            }
            /// A field added after wave 8: hashed only when set (in the stamp's own steps).
            fn n(&mut self, v: f64, tag: i32) {
                if !v.is_nan() && ((v * 1000.0).min(9e15).max(-9e15) + 0.25).floor() != 0.0 {
                    self.m(tag as f64);
                    self.m(v);
                }
            }
            fn mods(&mut self, a: &Option<Vec<Option<ModDef>>>) {
                if let Some(a) = a {
                    for m in a.iter().flatten() {
                        self.m(m.kind as f64);
                        self.m(m.value as f64);
                        self.m(m.arg as f64);
                    }
                }
            }
        }
        let mut h = H(1469598103934665603);
        let r = &self.rules;
        h.n(r.final_boss_warn_seconds as f64, -11);
        h.n(r.pickup_radius_per_level as f64, -12);
        for v in [
            r.reference_run_seconds as f64, r.final_boss_enemy_id as f64, r.final_boss_seconds as f64, r.max_enemies as f64, r.start_hp as f64, r.hp_per_level as f64,
            r.move_speed as f64, r.body_radius as f64, r.pickup_radius as f64, r.hurt_grace_seconds as f64, r.crit_chance as f64, r.crit_damage_pct as f64, r.start_weapon as f64,
            r.max_weapons as f64, r.max_passives as f64, r.exp_growth_pct as f64, r.max_level as f64, r.exp_share_pct as f64, r.cards_per_offer as f64, r.choice_seconds as f64,
            r.weight_new_weapon as f64, r.weight_weapon_level as f64, r.weight_new_passive as f64, r.weight_passive_level as f64, r.weight_stat as f64, r.stat_card_points as f64,
            r.revive_seconds as f64, r.revive_radius as f64, r.revive_near_mul as f64, r.revive_hp_pct as f64, r.revive_safe_seconds as f64,
            r.move_accel_seconds as f64, r.move_decel_seconds as f64, r.wall_soft_cells as f64, r.knockback_keep_pct as f64, r.pickup_pull_speed as f64, r.pickup_pull_accel as f64,
            r.choice_seconds_step as f64, r.choice_seconds_min as f64,
        ] {
            h.m(v);
        }
        for v in r.exp_table.iter().flatten() {
            h.m(*v as f64);
        }
        for v in r.job_marks.iter().flatten() {
            h.m(*v as f64);
        }
        h.m(-5.0);
        for v in r.party_exp_scale.iter().flatten() {
            h.m(*v as f64);
        }
        for w in &self.weapons {
            h.m(w.id as f64);
            h.s(&w.key);
            for v in [w.skill_id as f64, w.kind as f64, w.target as f64, w.class as f64, w.element as f64, w.zone as f64, w.homing as i32 as f64, w.summon_mob_id as f64, w.summon_hp as f64, w.weight as f64] {
                h.m(v);
            }
            for l in &w.levels {
                for v in [
                    l.damage as f64, l.cooldown as f64, l.count as f64, l.pierce as f64, l.speed as f64, l.range as f64, l.area as f64, l.arc as f64, l.spread as f64, l.burst as f64,
                    l.delay as f64, l.duration as f64, l.tick as f64, l.heal as f64, l.heal_pct as f64, l.falloff as f64,
                ] {
                    h.m(v);
                }
                if let Some(fx) = &l.effects {
                    for e in fx.iter().flatten() {
                        h.m(e.kind as f64);
                        h.m(e.chance as f64);
                        h.m(e.duration as f64);
                        h.m(e.power as f64);
                        h.m(e.tick as f64);
                    }
                }
                h.mods(&l.buffs);
            }
        }
        for p in &self.passives {
            h.m(p.id as f64);
            h.s(&p.key);
            h.m(p.skill_id as f64);
            h.m(p.item_id as f64);
            h.m(p.weight as f64);
            for l in &p.levels {
                h.m(-2.0);
                h.mods(l);
            }
        }
        for j in &self.jobs {
            h.m(j.job_id as f64);
            h.m(j.tier as f64);
            h.m(j.signature_weapon as f64);
            h.m(j.sex as f64);
            for v in j.next.iter().flatten() {
                h.m(*v as f64);
            }
            h.m(-3.0);
            for v in j.weapons.iter().flatten() {
                h.m(*v as f64);
            }
            h.m(-3.0);
            for v in j.passives.iter().flatten() {
                h.m(*v as f64);
            }
            h.m(-3.0);
            for v in j.stat_bonus.iter().flatten() {
                h.m(*v as f64);
            }
            h.mods(&j.mods);
        }
        for e in &self.enemies {
            for v in [
                e.id as f64, e.mob_id as f64, e.rank as f64, e.behavior as f64, e.hp as f64, e.damage as f64, e.attack_interval as f64, e.speed as f64, e.radius as f64, e.exp as f64,
                e.element as f64, e.race as f64, e.size as f64, e.knockback_resist as f64, e.effect_resist as f64, e.shot_damage as f64, e.shot_range as f64, e.shot_speed as f64,
                e.charge_cooldown as f64, e.charge_windup as f64, e.charge_speed as f64, e.burst_area as f64, e.draw_scale as f64,
            ] {
                h.m(v);
            }
            for a in &e.abilities {
                for v in [a.kind as f64, a.cooldown as f64, a.first_after as f64, a.damage as f64, a.area as f64, a.delay as f64, a.speed as f64, a.range as f64, a.count as f64, a.enemy_id as f64, a.skill_id as f64] {
                    h.m(v);
                }
            }
            for d in e.drops.iter().flatten() {
                h.m(d.pickup_id as f64);
                h.m(d.chance as f64);
                h.n(d.scatter_cells as f64, -13);
            }
            h.n(e.keep_range as f64, -14);
        }
        for w in &self.waves {
            for v in [w.from as f64, w.to as f64, w.enemy_id as f64, w.interval as f64, w.count as f64, w.max_alive as f64, w.pattern as f64, w.once as i32 as f64] {
                h.m(v);
            }
            h.n(w.warn_seconds as f64, -15);
        }
        for p in &self.pickups {
            for v in [p.id as f64, p.kind as f64, p.item_id as f64, p.value as f64, p.area as f64] {
                h.m(v);
            }
        }
        for a in &self.arenas {
            h.m(a.id as f64);
            h.s(&a.map);
            for v in [a.x0, a.y0, a.x1, a.y1] {
                h.m(v as f64);
            }
        }
        for s in &self.stats {
            h.m(s.stat as f64);
            h.mods(&s.per_point);
        }
        let sc = &self.scaling;
        for arr in [&sc.spawn_mul, &sc.enemy_hp_mul, &sc.elite_hp_mul, &sc.boss_hp_mul, &sc.enemy_damage_mul] {
            h.m(-4.0);
            for v in arr.iter().flatten() {
                h.m(*v as f64);
            }
        }
        h.m(sc.enemy_hp_pct_per_minute as f64);
        h.m(sc.enemy_damage_pct_per_minute as f64);
        let so = &self.score;
        for v in [so.per_kill, so.per_elite_kill, so.per_boss_kill, so.per_run_level, so.per_job_tier, so.per_damage1000, so.per_heal100, so.per_revive, so.per_second, so.victory] {
            h.m(v as f64);
        }
        let rw = &self.reward;
        h.m(rw.score_for_full as f64);
        h.m(rw.min_seconds as f64);
        for v in rw.base_cap_pct.iter().flatten() {
            h.m(*v as f64);
        }
        for v in rw.job_cap_pct.iter().flatten() {
            h.m(*v as f64);
        }
        if let Some(t) = &self.element_pct {
            for row in t {
                for v in row {
                    h.m(*v as f64);
                }
            }
        }
        format!("h3-{:016x}", h.0)
    }
}

// ---------------------------------------------------------------------------------------------------- the loader

/// A reader over the generated bytes; a short or malformed file is an error, never a panic.
pub struct Rd<'a> {
    b: &'a [u8],
    o: usize,
}

impl<'a> Rd<'a> {
    pub fn new(b: &'a [u8]) -> Self {
        Rd { b, o: 0 }
    }
    pub fn pos(&self) -> usize {
        self.o
    }
    pub fn left(&self) -> usize {
        self.b.len() - self.o
    }
    pub fn bytes(&mut self, n: usize) -> Result<&'a [u8], String> {
        if self.o + n > self.b.len() {
            return Err(format!("horde data: cut short at byte {} (wanted {n} more)", self.o));
        }
        let s = &self.b[self.o..self.o + n];
        self.o += n;
        Ok(s)
    }
    pub fn u8(&mut self) -> Result<u8, String> {
        Ok(self.bytes(1)?[0])
    }
    pub fn i8(&mut self) -> Result<i8, String> {
        Ok(self.bytes(1)?[0] as i8)
    }
    pub fn u16(&mut self) -> Result<u16, String> {
        let s = self.bytes(2)?;
        Ok(u16::from_le_bytes([s[0], s[1]]))
    }
    pub fn i16(&mut self) -> Result<i16, String> {
        Ok(self.u16()? as i16)
    }
    pub fn i32(&mut self) -> Result<i32, String> {
        let s = self.bytes(4)?;
        Ok(i32::from_le_bytes([s[0], s[1], s[2], s[3]]))
    }
    pub fn u32(&mut self) -> Result<u32, String> {
        Ok(self.i32()? as u32)
    }
    pub fn u64(&mut self) -> Result<u64, String> {
        let s = self.bytes(8)?;
        Ok(u64::from_le_bytes([s[0], s[1], s[2], s[3], s[4], s[5], s[6], s[7]]))
    }
    pub fn f32(&mut self) -> Result<f32, String> {
        Ok(f32::from_bits(self.u32()?))
    }
    pub fn f64(&mut self) -> Result<f64, String> {
        Ok(f64::from_bits(self.u64()?))
    }
    pub fn str(&mut self) -> Result<String, String> {
        let n = self.u16()? as usize;
        String::from_utf8(self.bytes(n)?.to_vec()).map_err(|_| "horde data: a string is not UTF-8".to_string())
    }
    fn count(&mut self) -> Result<Option<usize>, String> {
        let n = self.i32()?;
        if n < -1 || n > 100_000 {
            return Err(format!("horde data: an array of {n} at byte {}", self.o));
        }
        Ok(if n < 0 { None } else { Some(n as usize) })
    }
    fn ints(&mut self) -> Result<Option<Vec<i32>>, String> {
        let Some(n) = self.count()? else { return Ok(None) };
        (0..n).map(|_| self.i32()).collect::<Result<Vec<_>, _>>().map(Some)
    }
    fn floats(&mut self) -> Result<Option<Vec<f32>>, String> {
        let Some(n) = self.count()? else { return Ok(None) };
        (0..n).map(|_| self.f32()).collect::<Result<Vec<_>, _>>().map(Some)
    }
    fn mods(&mut self) -> Result<Option<Vec<Option<ModDef>>>, String> {
        let Some(n) = self.count()? else { return Ok(None) };
        let mut v = Vec::with_capacity(n);
        for _ in 0..n {
            v.push(if self.u8()? != 0 { Some(ModDef { kind: self.u8()?, value: self.f32()?, arg: self.i32()? }) } else { None });
        }
        Ok(Some(v))
    }
    fn len(&mut self) -> Result<usize, String> {
        self.count()?.ok_or_else(|| "horde data: a list is null".to_string())
    }
}

pub const CONTENT_MAGIC: u32 = 0x4348_4B4C; // "LKHC"
pub const CONTENT_VERSION: i32 = 1;

/// tools/hordegolden/ContentWriter.cs Bytes, read back.
pub fn load(bytes: &[u8]) -> Result<Content, String> {
    let mut r = Rd::new(bytes);
    if r.u32()? != CONTENT_MAGIC {
        return Err("horde content: not a content file".into());
    }
    let v = r.i32()?;
    if v != CONTENT_VERSION {
        return Err(format!("horde content: version {v}, this module reads {CONTENT_VERSION}"));
    }
    let mut c = Content { stamp_cs: r.str()?, ..Default::default() };
    c.rules = RulesDef {
        reference_run_seconds: r.f32()?,
        final_boss_enemy_id: r.i32()?,
        final_boss_seconds: r.f32()?,
        final_boss_warn_seconds: r.f32()?,
        max_enemies: r.i32()?,
        start_hp: r.i32()?,
        hp_per_level: r.i32()?,
        move_speed: r.f32()?,
        move_accel_seconds: r.f32()?,
        move_decel_seconds: r.f32()?,
        wall_soft_cells: r.f32()?,
        knockback_keep_pct: r.f32()?,
        pickup_pull_speed: r.f32()?,
        pickup_pull_accel: r.f32()?,
        body_radius: r.f32()?,
        pickup_radius: r.f32()?,
        pickup_radius_per_level: r.f32()?,
        hurt_grace_seconds: r.f32()?,
        crit_chance: r.f32()?,
        crit_damage_pct: r.f32()?,
        start_weapon: r.i32()?,
        max_weapons: r.i32()?,
        max_passives: r.i32()?,
        exp_table: r.ints()?,
        exp_growth_pct: r.f32()?,
        max_level: r.i32()?,
        exp_share_pct: r.f32()?,
        party_exp_scale: r.floats()?,
        cards_per_offer: r.i32()?,
        choice_seconds: r.f32()?,
        choice_seconds_step: r.f32()?,
        choice_seconds_min: r.f32()?,
        job_marks: r.ints()?,
        weight_new_weapon: r.i32()?,
        weight_weapon_level: r.i32()?,
        weight_new_passive: r.i32()?,
        weight_passive_level: r.i32()?,
        weight_stat: r.i32()?,
        stat_card_points: r.i32()?,
        revive_seconds: r.f32()?,
        revive_radius: r.f32()?,
        revive_near_mul: r.f32()?,
        revive_hp_pct: r.f32()?,
        revive_safe_seconds: r.f32()?,
    };
    c.scaling = ScalingDef {
        spawn_mul: r.floats()?,
        enemy_hp_mul: r.floats()?,
        elite_hp_mul: r.floats()?,
        boss_hp_mul: r.floats()?,
        enemy_damage_mul: r.floats()?,
        enemy_hp_pct_per_minute: r.f32()?,
        enemy_damage_pct_per_minute: r.f32()?,
    };
    c.score = ScoreDef {
        per_kill: r.i32()?,
        per_elite_kill: r.i32()?,
        per_boss_kill: r.i32()?,
        per_run_level: r.i32()?,
        per_job_tier: r.i32()?,
        per_damage1000: r.i32()?,
        per_heal100: r.i32()?,
        per_revive: r.i32()?,
        per_second: r.i32()?,
        victory: r.i32()?,
    };
    c.reward = RewardDef {
        score_for_full: r.i32()?,
        seconds_for_full: if c.rules.reference_run_seconds > 0.0 { c.rules.reference_run_seconds } else { 900.0 },
        min_seconds: r.f32()?,
        base_cap_pct: r.floats()?,
        job_cap_pct: r.floats()?,
    };
    for _ in 0..r.len()? {
        let mut w = WeaponDef {
            id: r.i32()?,
            key: r.str()?,
            skill_id: r.i32()?,
            item_id: r.i32()?,
            kind: r.u8()?,
            target: r.u8()?,
            class: r.u8()?,
            element: r.u8()?,
            zone: r.u8()?,
            homing: r.u8()? != 0,
            summon_mob_id: r.i32()?,
            summon_hp: r.i32()?,
            weight: r.i32()?,
            levels: Vec::new(),
            branch_of: 0,
            branch: 0,
            branches: None,
            evolve_of: 0,
            needs_weapon: 0,
            needs_passive: 0,
            group: 0,
            hidden: false,
            trans_of: 0,
            trans_branch: 0,
            trans_branches: None,
            trans: 0,
            random_of: None,
        };
        for _ in 0..r.len()? {
            let mut l = WeaponLevel {
                damage: r.i32()?,
                cooldown: r.f32()?,
                count: r.i32()?,
                pierce: r.i32()?,
                speed: r.f32()?,
                range: r.f32()?,
                area: r.f32()?,
                arc: r.f32()?,
                spread: r.f32()?,
                burst: r.f32()?,
                delay: r.f32()?,
                duration: r.f32()?,
                tick: r.f32()?,
                heal: r.i32()?,
                heal_pct: r.f32()?,
                falloff: r.f32()?,
                effects: None,
                buffs: None,
            };
            if let Some(n) = r.count()? {
                let mut fx = Vec::with_capacity(n);
                for _ in 0..n {
                    fx.push(if r.u8()? != 0 { Some(EffectDef { kind: r.u8()?, chance: r.f32()?, duration: r.f32()?, power: r.f32()?, tick: r.f32()? }) } else { None });
                }
                l.effects = Some(fx);
            }
            l.buffs = r.mods()?;
            w.levels.push(l);
        }
        c.weapons.push(w);
    }
    for _ in 0..r.len()? {
        let mut p = PassiveDef {
            id: r.i32()?,
            key: r.str()?,
            skill_id: r.i32()?,
            item_id: r.i32()?,
            weight: r.i32()?,
            levels: Vec::new(),
            group: 0,
            gives_weapon: 0,
            trans_of: 0,
            trans: 0,
        };
        for _ in 0..r.len()? {
            p.levels.push(r.mods()?);
        }
        c.passives.push(p);
    }
    for _ in 0..r.len()? {
        c.jobs.push(JobDef {
            job_id: r.i32()?,
            key: r.str()?,
            tier: r.i32()?,
            next: r.ints()?,
            signature_weapon: r.i32()?,
            weapons: r.ints()?,
            passives: r.ints()?,
            mods: r.mods()?,
            stat_bonus: r.ints()?,
            sex: r.u8()?,
        });
    }
    for _ in 0..r.len()? {
        let mut e = EnemyDef {
            id: r.i32()?,
            mob_id: r.i32()?,
            key: r.str()?,
            rank: r.u8()?,
            behavior: r.u8()?,
            hp: r.i32()?,
            damage: r.i32()?,
            attack_interval: r.f32()?,
            speed: r.f32()?,
            radius: r.f32()?,
            exp: r.i32()?,
            element: r.u8()?,
            race: r.u8()?,
            size: r.u8()?,
            knockback_resist: r.f32()?,
            effect_resist: r.f32()?,
            shot_damage: r.i32()?,
            shot_range: r.f32()?,
            shot_speed: r.f32()?,
            keep_range: r.f32()?,
            charge_cooldown: r.f32()?,
            charge_windup: r.f32()?,
            charge_speed: r.f32()?,
            burst_area: r.f32()?,
            draw_scale: r.f32()?,
            abilities: Vec::new(),
            drops: Vec::new(),
        };
        for _ in 0..r.len()? {
            e.abilities.push(EnemyAbilityDef {
                kind: r.u8()?,
                cooldown: r.f32()?,
                first_after: r.f32()?,
                damage: r.i32()?,
                area: r.f32()?,
                delay: r.f32()?,
                speed: r.f32()?,
                range: r.f32()?,
                count: r.i32()?,
                enemy_id: r.i32()?,
                skill_id: r.i32()?,
            });
        }
        for _ in 0..r.len()? {
            e.drops.push(if r.u8()? != 0 { Some(DropDef { pickup_id: r.i32()?, chance: r.f32()?, scatter_cells: r.f32()? }) } else { None });
        }
        c.enemies.push(e);
    }
    for _ in 0..r.len()? {
        c.waves.push(WaveDef {
            from: r.f32()?,
            to: r.f32()?,
            enemy_id: r.i32()?,
            interval: r.f32()?,
            count: r.i32()?,
            max_alive: r.i32()?,
            pattern: r.u8()?,
            once: r.u8()? != 0,
            warn_seconds: r.f32()?,
        });
    }
    for _ in 0..r.len()? {
        c.pickups.push(PickupDef { id: r.i32()?, key: r.str()?, kind: r.u8()?, item_id: r.i32()?, value: r.i32()?, area: r.f32()? });
    }
    for _ in 0..r.len()? {
        c.arenas.push(ArenaDef { id: r.i32()?, map: r.str()?, x0: r.i32()?, y0: r.i32()?, x1: r.i32()?, y1: r.i32()? });
    }
    for _ in 0..r.len()? {
        c.stats.push(StatDef { stat: r.u8()?, per_point: r.mods()? });
    }
    if r.u8()? != 0 {
        let mut t = [[0i32; 10]; 10];
        for row in t.iter_mut() {
            for v in row.iter_mut() {
                *v = r.i32()?;
            }
        }
        c.element_pct = Some(t);
    }
    if c.is_v4() && r.left() > 0 {
        for w in &mut c.weapons {
            w.branch_of = r.i32()?;
            w.branch = r.i32()?;
            w.branches = r.ints()?;
            w.evolve_of = r.i32()?;
            w.needs_weapon = r.i32()?;
            w.needs_passive = r.i32()?;
            w.group = r.i32()?;
            w.hidden = r.u8()? != 0;
            w.trans_of = r.i32()?;
            w.trans_branch = r.i32()?;
            w.trans_branches = r.ints()?;
            w.trans = r.u8()?;
            w.random_of = r.ints()?;
        }
        for p in &mut c.passives {
            p.group = r.i32()?;
            p.gives_weapon = r.i32()?;
            p.trans_of = r.i32()?;
            p.trans = r.u8()?;
        }
    }
    if r.left() != 0 {
        return Err(format!("horde content: {} bytes after the tables", r.left()));
    }
    Ok(c)
}

/// The game's tables: server/data/horde/content.bin, embedded (HordeTables.Build() as tools/hordegolden wrote it).
static CONTENT_BIN: &[u8] = include_bytes!("../../data/horde/content.bin");

/// The game's content, parsed once per module instance.
pub fn game() -> &'static Content {
    static CELL: std::sync::OnceLock<Content> = std::sync::OnceLock::new();
    CELL.get_or_init(|| load(CONTENT_BIN).unwrap_or_else(|e| panic!("{e}")))
}
