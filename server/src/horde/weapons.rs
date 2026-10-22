//! Horde's simulation, second half: client/Assets/Loki/Runtime/Horde/Core/HordeSim.Weapons.cs ported rule for rule:
//! modifiers, aiming, the ten weapon kinds, summons and the lasting effects (shots, zones, traps, warnings, orbits).
//! See sim.rs for how the port is held to the C# engine.

use super::content::*;
use super::math::{self, clamp, dist2, isqrt, round, ticks, units, TICK_RATE, UNIT};
use super::sim::*;

/// AddMod (HordeSim.Weapons.cs:11-54).
fn add_mod(s: &mut Stats, m: &ModDef, mul: f64) {
    let v = m.value as f64 * mul;
    let arg = if m.arg < 0 { 0 } else { (m.arg % 10) as usize };
    match m.kind {
        modk::MAX_HP => s.max_hp_flat += v,
        modk::MAX_HP_PCT => s.max_hp_pct += v,
        modk::REGEN => s.regen += v,
        modk::MOVE_SPEED_PCT => s.move_pct += v,
        modk::DAMAGE_PCT => s.damage_pct += v,
        modk::MELEE_DAMAGE_PCT => s.melee_pct += v,
        modk::RANGED_DAMAGE_PCT => s.ranged_pct += v,
        modk::MAGIC_DAMAGE_PCT => s.magic_pct += v,
        modk::COOLDOWN_PCT => s.cooldown_pct += v,
        modk::AREA_PCT => s.area_pct += v,
        modk::PROJECTILE_COUNT => s.projectile_count += v,
        modk::PROJECTILE_SPEED_PCT => s.projectile_speed_pct += v,
        modk::DURATION_PCT => s.duration_pct += v,
        modk::PICKUP_RADIUS_PCT => s.pickup_radius_pct += v,
        modk::CRIT_CHANCE => s.crit += v,
        modk::CRIT_DAMAGE_PCT => s.crit_damage += v,
        modk::EXP_GAIN_PCT => s.exp_gain_pct += v,
        modk::DEFENCE => s.defence += v,
        modk::DAMAGE_TAKEN_PCT => s.damage_taken_pct += v,
        modk::DODGE => s.dodge += v,
        modk::HEAL_POWER_PCT => s.heal_power_pct += v,
        modk::REVIVE_SPEED_PCT => s.revive_speed_pct += v,
        modk::STR => s.stats_added[0] += v,
        modk::AGI => s.stats_added[1] += v,
        modk::VIT => s.stats_added[2] += v,
        modk::INT => s.stats_added[3] += v,
        modk::DEX => s.stats_added[4] += v,
        modk::LUK => s.stats_added[5] += v,
        modk::DAMAGE_VS_RACE_PCT => s.vs_race[arg] += v,
        modk::DAMAGE_VS_ELEMENT_PCT => s.vs_element[arg] += v,
        modk::ELEMENT_DAMAGE_PCT => s.element_damage[arg] += v,
        modk::DAMAGE_VS_BOSS_PCT => s.vs_boss += v,
        modk::EFFECT_CHANCE_PCT => s.effect_chance += v,
        modk::KNOCKBACK_PCT => s.knockback_pct += v,
        modk::HP_ON_KILL => s.hp_on_kill += v,
        _ => {}
    }
}

fn add_mods(s: &mut Stats, mods: &Option<Vec<Option<ModDef>>>, mul: f64) {
    if let Some(mods) = mods {
        for m in mods.iter().flatten() {
            add_mod(s, m, mul);
        }
    }
}

impl Sim {
    /// Recalc (HordeSim.Weapons.cs:58-88): sums the player's modifiers: passives (their level's), the current job's,
    /// buffs, then its stat points through the content's Stats; max HP and walking speed follow.
    pub(super) fn recalc(&mut self, pi: usize, init: bool) {
        let c = self.c;
        let r = &c.rules;
        let p = &mut self.players[pi];
        let mut s = Stats { crit: r.crit_chance as f64, ..Default::default() };
        for op in &p.passives {
            let d = &c.passives[op.def];
            if op.level < 1 || d.levels.is_empty() {
                continue;
            }
            add_mods(&mut s, &d.levels[(op.level.min(d.levels.len() as i32) - 1) as usize], 1.0);
        }
        if let Some(j) = c.job(p.job_id) {
            add_mods(&mut s, &c.jobs[j].mods, 1.0);
        }
        for b in &p.buffs {
            if b.weapon_id == RISE_BUFF_ID {
                s.move_pct += 60.0;
                s.cooldown_pct += ((1.0 / (1.0 + 60.0 / 100.0) - 1.0) * 100.0) as f32 as f64;
            } else if b.weapon_id == OBJ_SHRINE_BUFF_ID {
                s.cooldown_pct += -23.1f32 as f64;
            } else {
                add_mods(&mut s, &c.weapons[b.mods.0].levels[b.mods.1].buffs, 1.0);
            }
        }
        if self.arrow_buff_until[pi] > self.tick {
            s.projectile_count += 1.0;
        }
        for sd in &c.stats {
            let i = sd.stat as usize;
            if i >= 6 {
                continue;
            }
            let Some(per_point) = &sd.per_point else { continue };
            let pts = p.stat_points[i] as f64 + s.stats_added[i];
            if pts == 0.0 {
                continue;
            }
            for m in per_point.iter().flatten() {
                if m.kind < modk::STR || m.kind > modk::LUK {
                    add_mod(&mut s, m, pts);
                }
            }
        }
        p.s = s;
        let old_max = p.max_hp;
        p.max_hp = 1.max(round(((r.start_hp + r.hp_per_level * (p.level - 1)) as f64 + p.s.max_hp_flat) * 0.1f64.max(1.0 + p.s.max_hp_pct / 100.0)));
        if !init && p.max_hp > old_max && !p.fallen {
            p.hp = p.max_hp.min(p.hp + (p.max_hp - old_max));
        }
        if p.hp > p.max_hp {
            p.hp = p.max_hp;
        }
        p.speed_per_tick = 1.max(round(r.move_speed as f64 * 0.2f64.max(1.0 + p.s.move_pct / 100.0) * UNIT as f64 / TICK_RATE as f64));
        // the ramp: the client derives the same two integers from the speed it is sent (HordeClient.Step)
        p.accel_per_tick = math::ramp_per_tick(p.speed_per_tick, r.move_accel_seconds);
        p.decel_per_tick = math::ramp_per_tick(p.speed_per_tick, r.move_decel_seconds);
    }

    fn cooldown_ticks(&self, pi: usize, lv: &WeaponLevel) -> i32 {
        1.max(ticks(lv.cooldown as f64 * 0.25f64.max(1.0 + self.players[pi].s.cooldown_pct / 100.0)))
    }
    pub(super) fn area_units(&self, pi: usize, cells: f32) -> i32 {
        1.max(units(cells as f64 * 0.2f64.max(1.0 + self.players[pi].s.area_pct / 100.0)))
    }
    fn duration_ticks(&self, pi: usize, seconds: f32) -> i32 {
        1.max(ticks(seconds as f64 * 0.2f64.max(1.0 + self.players[pi].s.duration_pct / 100.0)))
    }
    fn count_of(&self, pi: usize, lv: &WeaponLevel, counts: bool) -> i32 {
        1.max(lv.count + if counts { self.players[pi].s.projectile_count.floor() as i32 } else { 0 })
    }

    /// The level an OwnedWeapon is at: OwnedWeapon.Lv (HordeSim.cs:1276).
    pub(super) fn lv_of(&self, pi: usize, widx: usize) -> (usize, usize) {
        let ow = &self.players[pi].weapons[widx];
        let n = self.c.weapons[ow.def].levels.len() as i32;
        (ow.def, 0.max(ow.level.min(n) - 1) as usize)
    }

    // =============================================================================================== aiming

    pub(super) fn query(&self, x: i32, y: i32, r: i32) -> Vec<i32> {
        let mut q = Vec::with_capacity(32);
        self.grid.query(x, y, r, &self.enemies, &mut q);
        q
    }

    /// FindTarget (HordeSim.Weapons.cs:100-129): the enemy slot a weapon aims at, or -1 (none within range).
    pub(super) fn find_target(&mut self, x: i32, y: i32, t: u8, range: i32, area: i32, exclude: Option<&[i32]>) -> i32 {
        let q = self.query(x, y, range);
        let mut best = -1;
        let mut best_key: i64 = 0;
        let r2 = range as i64 * range as i64;
        let mut seen = 0;
        for &i in &q {
            let e = &self.enemies[i as usize];
            if exclude.is_some_and(|ex| ex.contains(&i)) {
                continue;
            }
            let d = dist2(x, y, e.x, e.y);
            if d > r2 {
                continue;
            }
            let key: i64 = match t {
                targeting::RANDOM => {
                    seen += 1;
                    if self.rng.next(seen) == 0 {
                        best = i;
                    }
                    continue;
                }
                targeting::STRONGEST => -((e.hp as i64) << 20) + d / 1024,
                targeting::FARTHEST => -d,
                targeting::DENSEST => {
                    let a2 = area as i64 * area as i64;
                    let (ex, ey) = (e.x, e.y);
                    let near = q.iter().filter(|&&j| j != i && dist2(ex, ey, self.enemies[j as usize].x, self.enemies[j as usize].y) <= a2).count() as i64;
                    -(near << 32) + d / 1024
                }
                _ => d,
            };
            if best < 0 || key < best_key {
                best = i;
                best_key = key;
            }
        }
        best
    }

    /// LowestAlly (HordeSim.Weapons.cs:131-141).
    fn lowest_ally(&self, pi: usize, range: i32) -> Option<usize> {
        let (px, py) = (self.players[pi].x, self.players[pi].y);
        let mut best = None;
        let mut best_f = 2.0f64;
        for (qi, q) in self.players.iter().enumerate() {
            if !q.present || q.fallen || dist2(q.x, q.y, px, py) > range as i64 * range as i64 {
                continue;
            }
            let f = q.hp as f64 / 1.max(q.max_hp) as f64;
            if f < best_f {
                best_f = f;
                best = Some(qi);
            }
        }
        best
    }

    /// Heal (HordeSim.Weapons.cs:143-154).
    fn heal(&mut self, healer: usize, qi: usize, lv: &WeaponLevel, weapon_id: i32) {
        let power = self.players[healer].s.heal_power_pct;
        let q = &mut self.players[qi];
        if q.fallen || !q.present {
            return;
        }
        let h = (lv.heal as f64 + lv.heal_pct as f64 / 100.0 * q.max_hp as f64) * 0f64.max(1.0 + power / 100.0);
        let amount = round(h);
        if amount <= 0 {
            return;
        }
        let before = q.hp;
        q.hp = q.max_hp.min(q.hp + amount);
        let got = q.hp - before;
        if got <= 0 {
            return;
        }
        let (x, y) = (q.x, q.y);
        if qi != healer {
            self.players[healer].healing += got as i64;
        }
        self.add_event(ev::HEAL, 0, qi as i32, healer as i32, weapon_id, got, x, y);
    }

    // =============================================================================================== weapons

    /// CastActive (HordeSim.Weapons.cs): manually casts an owned active weapon at (target_x, target_y) in simulation units.
    pub fn cast_active(&mut self, slot: i32, weapon_id: i32, target_x: i32, target_y: i32) -> bool {
        let Some(pi) = self.player_idx(slot) else { return false };
        if self.players[pi].fallen || self.over || self.paused {
            return false;
        }
        self.manual_cast_used[pi] = true;
        let c = self.c;
        let want_root = if weapon_id >= 6000 {
            weapon_id - 6000
        } else if weapon_id >= 5000 {
            weapon_id - 5000
        } else if weapon_id >= 3000 {
            weapon_id - 3000
        } else if weapon_id >= 2000 {
            weapon_id - 2000
        } else if weapon_id >= 1000 {
            weapon_id - 1000
        } else {
            weapon_id
        };
        let Some(wi) = self.players[pi].weapons.iter().position(|ow| c.weapons[ow.def].id == weapon_id || (c.is_v4() && c.weapons[ow.def].root_id() == want_root)) else {
            return false;
        };
        if c.weapons[self.players[pi].weapons[wi].def].levels.is_empty() {
            return false;
        }
        let tick = self.tick;
        if tick < self.players[pi].weapons[wi].active_next_tick {
            return false;
        }
        let lvi = self.lv_of(pi, wi);
        let w = &c.weapons[lvi.0];
        let lv = &w.levels[lvi.1];
        let ow_level = self.players[pi].weapons[wi].level;
        let (px, py) = (self.players[pi].x, self.players[pi].y);
        let tx = clamp(target_x, self.min_x, self.max_x);
        let ty = clamp(target_y, self.min_y, self.max_y);
        let dmg_mul = if c.is_v4() { if want_root == 183 { 5.0 } else { 3.5 } } else { 1.35 };
        let area_mul: f32 = if c.is_v4() { 1.45 } else { 1.0 };
        let dur_mul: f32 = if c.is_v4() { 1.75 } else { 1.0 };
        let fired = match w.kind {
            weapon_kind::AREA_AT_TARGET => {
                let area = self.area_units(pi, (if lv.area > 0.0 { lv.area } else { 2.0 }) * area_mul);
                let n = self.count_of(pi, lv, true) + if c.is_v4() { 2 } else { 0 };
                for i in 0..n {
                    let ang = i as f64 * 2.399963229728653;
                    let dist = if i == 0 { 0.0 } else { area as f64 * 0.45 * ((i as f64).sqrt() / (n.max(1) as f64).sqrt()) };
                    let ox = clamp(tx + round(math::cos(ang) * dist), self.min_x, self.max_x);
                    let oy = clamp(ty + round(math::sin(ang) * dist), self.min_y, self.max_y);
                    let mut f = self.new_fx(fxk::WARNING);
                    f.owner_slot = slot;
                    f.def_id = w.id;
                    f.level = ow_level;
                    f.x = ox;
                    f.y = oy;
                    f.radius = area;
                    f.end_tick = tick + 1.max(ticks((lv.delay as f64 * 0.4) + i as f64 * 0.09));
                    f.widx = wi;
                    f.lv = lvi;
                    f.base_damage = lv.damage as f64 * dmg_mul;
                    self.begin_fx(f);
                }
                true
            }
            weapon_kind::GROUND_ZONE => {
                let area = self.area_units(pi, (if lv.area > 0.0 { lv.area } else { 2.0 }) * area_mul);
                let n = self.count_of(pi, lv, true);
                let ang = math::atan2((ty - py) as f64, (tx - px) as f64);
                let perp = ang + std::f64::consts::PI * 0.5;
                if (w.zone & zone_flags::BLOCKS_ENEMIES) != 0 {
                    let mut f = self.new_fx(fxk::ZONE);
                    f.owner_slot = slot;
                    f.def_id = w.id;
                    f.level = ow_level;
                    f.x = tx;
                    f.y = ty;
                    f.radius = units(0.45).max(area * 55 / 100);
                    f.range = units(1.4).max(area * 2.max(n) * 42 / 100);
                    f.angle = (perp * 180.0 / std::f64::consts::PI).round();
                    f.every = 1.max(ticks(lv.tick as f64));
                    f.next_tick = tick + 1;
                    f.end_tick = tick + self.duration_ticks(pi, (if lv.duration > 0.0 { lv.duration } else { 4.0 }) * dur_mul);
                    f.widx = wi;
                    f.lv = lvi;
                    f.base_damage = lv.damage as f64 * dmg_mul;
                    f.heal = lv.heal * if c.is_v4() { 2 } else { 1 };
                    f.heal_pct = lv.heal_pct as f64 * if c.is_v4() { 1.75 } else { 1.0 };
                    f.zone_flags = w.zone;
                    f.follows = false;
                    self.begin_fx(f);
                } else {
                    for i in 0..n {
                        let off = (i as f64 - (n - 1) as f64 * 0.5) * (area as f64 * 1.15);
                        let zx = clamp(tx + round(math::cos(perp) * off), self.min_x, self.max_x);
                        let zy = clamp(ty + round(math::sin(perp) * off), self.min_y, self.max_y);
                        let mut f = self.new_fx(fxk::ZONE);
                        f.owner_slot = slot;
                        f.def_id = w.id;
                        f.level = ow_level;
                        f.x = zx;
                        f.y = zy;
                        f.radius = area;
                        f.every = 1.max(ticks(lv.tick as f64));
                        f.next_tick = tick + 1;
                        f.end_tick = tick + self.duration_ticks(pi, (if lv.duration > 0.0 { lv.duration } else { 4.0 }) * dur_mul);
                        f.widx = wi;
                        f.lv = lvi;
                        f.base_damage = lv.damage as f64 * dmg_mul;
                        f.heal = lv.heal * if c.is_v4() { 2 } else { 1 };
                        f.heal_pct = lv.heal_pct as f64 * if c.is_v4() { 1.75 } else { 1.0 };
                        f.zone_flags = w.zone;
                        f.follows = false;
                        self.begin_fx(f);
                    }
                }
                true
            }
            weapon_kind::BOLT => {
                let ang = math::atan2((ty - py) as f64, (tx - px) as f64);
                let range = units(if lv.range > 0.0 { lv.range as f64 } else { 10.0 });
                let speed = 1.max(units((if lv.speed > 0.0 { lv.speed as f64 } else { 12.0 }) * 0.2f64.max(1.0 + self.players[pi].s.projectile_speed_pct / 100.0) / TICK_RATE as f64));
                self.shoot(pi, wi, lvi, ang, speed, range, -1);
                true
            }
            _ => {
                let area = self.area_units(pi, (if lv.area > 0.0 { lv.area } else { 2.5 }) * area_mul);
                let strike_dmg = if c.is_v4() { (lv.damage as f64 * dmg_mul).max(180.0 + self.players[pi].level as f64 * 12.0) } else { lv.damage as f64 * dmg_mul };
                self.strike_area(pi, wi, lvi, tx, ty, area, strike_dmg);
                true
            }
        };
        if fired {
            if c.is_v4() {
                if matches!(want_root, 28 | 45 | 111 | 170) {
                    let p = &mut self.players[pi];
                    p.safe_until = p.safe_until.max(tick + ticks(3.5));
                    let h = 1.max(p.max_hp / 4);
                    let before = p.hp;
                    p.hp = p.max_hp.min(p.hp + h);
                    if p.hp > before {
                        let (got, x, y) = (p.hp - before, p.x, p.y);
                        self.add_event(ev::HEAL, 0, slot, slot, w.id, got, x, y);
                    }
                }
                let shock_rad = self.area_units(pi, (if lv.area > 0.0 { lv.area } else { 2.5 }) * 1.35);
                let q = self.query(tx, ty, shock_rad + units(1.5));
                let keep = self.kb_keep_pct;
                for &i in &q {
                    let ei = i as usize;
                    if !self.enemies[ei].alive {
                        continue;
                    }
                    let (ex, ey, erad, edef) = (self.enemies[ei].x, self.enemies[ei].y, self.enemies[ei].radius, self.enemies[ei].def);
                    let rr = (shock_rad + erad) as i64;
                    if dist2(tx, ty, ex, ey) > rr * rr {
                        continue;
                    }
                    if c.enemies[edef].effect_resist < 100.0 {
                        let e = &mut self.enemies[ei];
                        e.stun_until = e.stun_until.max(tick + ticks(2.2));
                        if want_root == 120 {
                            e.frozen_until = e.frozen_until.max(tick + ticks(3.0));
                        }
                    }
                    if c.enemies[edef].knockback_resist < 100.0 {
                        let dx = (ex - tx) as i64;
                        let dy = (ey - ty) as i64;
                        let d = 1.max(isqrt(dx * dx + dy * dy)) as i64;
                        let v0 = math::impulse_for(units(2.5), keep) as i64;
                        let e = &mut self.enemies[ei];
                        e.kb_vx = clamp(e.kb_vx + (dx * v0 / d) as i32, -8 * UNIT, 8 * UNIT);
                        e.kb_vy = clamp(e.kb_vy + (dy * v0 / d) as i32, -8 * UNIT, 8 * UNIT);
                    }
                }
                self.players[pi].weapons[wi].active_next_tick = tick + self.active_cooldown_ticks(pi, w.id);
            } else {
                let cd = self.cooldown_ticks(pi, lv);
                self.players[pi].weapons[wi].active_next_tick = tick + 4.max(cd * 7 / 10);
                self.players[pi].weapons[wi].next_tick = tick + cd;
            }
        }
        fired
    }

    /// StepWeapons (HordeSim.Weapons.cs:158-175).
    pub(super) fn step_weapons(&mut self) {
        let c = self.c;
        for pi in 0..self.players.len() {
            if !self.players[pi].present || self.players[pi].fallen {
                continue;
            }
            for wi in 0..self.players[pi].weapons.len() {
                if c.is_v4() && self.ygg_off(pi, wi) {
                    continue;
                }
                let def = &c.weapons[self.players[pi].weapons[wi].def];
                if def.levels.is_empty() {
                    continue;
                }
                let lvi = self.lv_of(pi, wi);
                let lv = &c.weapons[lvi.0].levels[lvi.1];
                let tick = self.tick;
                if self.players[pi].weapons[wi].burst_left > 0 && tick >= self.players[pi].weapons[wi].burst_next {
                    let ow = &mut self.players[pi].weapons[wi];
                    ow.burst_left -= 1;
                    ow.burst_next = tick + 1.max(ticks(lv.burst as f64));
                    self.fire_once(pi, wi, lvi, true);
                }
                if def.kind == weapon_kind::ORBITER {
                    self.step_orbiter_weapon(pi, wi, lvi);
                    continue;
                }
                if c.is_v4() && self.manual_cast_used[pi] && Self::is_active_weapon(def.id) {
                    continue;
                }
                if tick < self.players[pi].weapons[wi].next_tick {
                    continue;
                }
                if self.fire_once(pi, wi, lvi, false) {
                    self.players[pi].weapons[wi].next_tick = tick + self.cooldown_ticks(pi, lv);
                } else {
                    self.players[pi].weapons[wi].next_tick = tick + 2; // nothing to aim at: look again shortly
                }
            }
        }
        self.step_summons();
    }

    /// AimAngle (HordeSim.Weapons.cs:177-187): (angle, target slot) or None (nothing to aim at).
    fn aim_angle(&mut self, pi: usize, wi: usize, range: i32, area: i32) -> Option<(f64, i32)> {
        let p = &self.players[pi];
        let t = self.c.weapons[p.weapons[wi].def].target;
        if t == targeting::FACING || t == targeting::SELF {
            return Some((p.face_deg as f64 * std::f64::consts::PI / 180.0, -1));
        }
        let (px, py) = (p.x, p.y);
        let target = self.find_target(px, py, if t == targeting::LOWEST_HP_ALLY || t == targeting::RANDOM_ALLY { targeting::NEAREST } else { t }, range, area, None);
        if target < 0 {
            return None;
        }
        let e = &self.enemies[target as usize];
        Some((math::atan2((e.y - py) as f64, (e.x - px) as f64), target))
    }

    /// FireOnce (HordeSim.Weapons.cs:190-361): one use of a weapon (or one more of a burst). False: nothing to aim at
    /// (the cooldown does not start).
    fn fire_once(&mut self, pi: usize, wi: usize, lvi: (usize, usize), burst: bool) -> bool {
        let c = self.c;
        let w = &c.weapons[self.players[pi].weapons[wi].def];
        let lv = &c.weapons[lvi.0].levels[lvi.1];
        let pi_f = std::f64::consts::PI;
        let tick = self.tick;
        let slot = pi as i32;
        let ow_level = self.players[pi].weapons[wi].level;
        let (px, py) = (self.players[pi].x, self.players[pi].y);
        let range = units(if lv.range > 0.0 { lv.range as f64 } else { 8.0 });
        match w.kind {
            weapon_kind::BOLT => {
                let au = self.area_units(pi, lv.area);
                let Some((ang, target)) = self.aim_angle(pi, wi, range, au) else { return false };
                let count = self.count_of(pi, lv, true);
                let speed = 1.max(units(lv.speed as f64 * 0.2f64.max(1.0 + self.players[pi].s.projectile_speed_pct / 100.0) / TICK_RATE as f64));
                if c.is_v4() && w.trans == trans_kind::BOLT_NOVA {
                    let n = 8.max(count);
                    for i in 0..n {
                        let a = ang + i as f64 * (pi_f * 2.0 / n as f64);
                        self.shoot(pi, wi, lvi, a, speed, range, -1);
                    }
                    return true;
                }
                if lv.spread > 0.0 || burst {
                    let n = if burst { 1 } else { count };
                    let spread = lv.spread as f64 * pi_f / 180.0;
                    for i in 0..n {
                        self.shoot(pi, wi, lvi, ang + (i as f64 - (n - 1) as f64 / 2.0) * spread, speed, range, target);
                    }
                } else {
                    self.shoot(pi, wi, lvi, ang, speed, range, target);
                    if count > 1 {
                        let ow = &mut self.players[pi].weapons[wi];
                        ow.burst_left = count - 1;
                        ow.burst_next = tick + 1.max(ticks(lv.burst as f64));
                    }
                }
                true
            }
            weapon_kind::MELEE_ARC => {
                let reach = self.area_units(pi, if lv.range > 0.0 { lv.range } else { 2.0 });
                let Some((ang, _)) = self.aim_angle(pi, wi, reach + units(1.0), 0) else { return false };
                let half = (if lv.arc > 0.0 { lv.arc } else { 90.0 }) as f64 * pi_f / 360.0;
                let base_dmg = lv.damage as f64;
                let q = self.query(px, py, reach + units(1.5));
                for &i in &q {
                    let e = &self.enemies[i as usize];
                    if !e.alive {
                        continue;
                    }
                    let rr = reach + e.radius;
                    if dist2(px, py, e.x, e.y) > rr as i64 * rr as i64 {
                        continue;
                    }
                    let mut a = math::atan2((e.y - py) as f64, (e.x - px) as f64) - ang;
                    while a > pi_f {
                        a -= 2.0 * pi_f;
                    }
                    while a < -pi_f {
                        a += 2.0 * pi_f;
                    }
                    if a.abs() > half && half < pi_f {
                        continue;
                    }
                    self.hit_enemy(pi, wi, lvi, i as usize, base_dmg, px, py);
                }
                self.add_event_abc(ev::SWING, 0, slot, -1, w.id, ow_level, px, py, (ang * 180.0 / pi_f) as f32, (half * 360.0 / pi_f) as f32, reach as f32 / UNIT as f32, 0, 0);
                if c.is_v4() {
                    self.on_trans_melee_swing(pi, wi, lvi, ang, reach);
                }
                if !burst && self.count_of(pi, lv, false) > 1 {
                    let n = self.count_of(pi, lv, false);
                    let ow = &mut self.players[pi].weapons[wi];
                    ow.burst_left = n - 1;
                    ow.burst_next = tick + 1.max(ticks(lv.burst as f64));
                }
                true
            }
            weapon_kind::NOVA => {
                let area = self.area_units(pi, if lv.area > 0.0 { lv.area } else { 2.0 });
                let q = self.query(px, py, area + units(1.5));
                for &i in &q {
                    let e = &self.enemies[i as usize];
                    let rr = area + e.radius;
                    if e.alive && dist2(px, py, e.x, e.y) <= rr as i64 * rr as i64 {
                        self.hit_enemy(pi, wi, lvi, i as usize, lv.damage as f64, px, py);
                    }
                }
                self.add_event_abc(ev::NOVA, 0, slot, -1, w.id, ow_level, px, py, area as f32 / UNIT as f32, 0.0, 0.0, 0, 0);
                if c.is_v4() {
                    self.on_trans_nova(pi, wi, lvi, area);
                }
                if !burst && self.count_of(pi, lv, false) > 1 {
                    let n = self.count_of(pi, lv, false);
                    let ow = &mut self.players[pi].weapons[wi];
                    ow.burst_left = n - 1;
                    ow.burst_next = tick + 1.max(ticks(lv.burst as f64));
                }
                true
            }
            weapon_kind::AREA_AT_TARGET => {
                let area = self.area_units(pi, if lv.area > 0.0 { lv.area } else { 1.5 });
                let target = self.find_target(px, py, if w.target == targeting::SELF { targeting::NEAREST } else { w.target }, range, area, None);
                if target < 0 && w.target != targeting::SELF {
                    return false;
                }
                let (tx, ty) = if target >= 0 { (self.enemies[target as usize].x, self.enemies[target as usize].y) } else { (px, py) };
                let mut f = self.new_fx(fxk::WARNING);
                f.owner_slot = slot;
                f.def_id = w.id;
                f.level = ow_level;
                f.x = tx;
                f.y = ty;
                f.radius = area;
                f.end_tick = tick + 1.max(ticks(lv.delay as f64));
                f.widx = wi;
                f.lv = lvi;
                f.base_damage = lv.damage as f64;
                self.begin_fx(f);
                let n = self.count_of(pi, lv, true);
                if !burst && n > 1 {
                    let ow = &mut self.players[pi].weapons[wi];
                    ow.burst_left = n - 1;
                    ow.burst_next = tick + 1.max(ticks(lv.burst as f64));
                }
                true
            }
            weapon_kind::GROUND_ZONE => {
                let area = self.area_units(pi, if lv.area > 0.0 { lv.area } else { 1.5 });
                let n = if burst { 1 } else { self.count_of(pi, lv, true) };
                let mut any = false;
                if c.is_v4() && (w.zone & zone_flags::BLOCKS_ENEMIES) != 0 && w.target != targeting::SELF && (w.zone & zone_flags::FOLLOWS_OWNER) == 0 {
                    let t = if w.target == targeting::LOWEST_HP_ALLY || w.target == targeting::RANDOM_ALLY { targeting::NEAREST } else { w.target };
                    let target = self.find_target(px, py, t, range, area, None);
                    if target < 0 {
                        return false;
                    }
                    let (tx0, ty0) = (self.enemies[target as usize].x, self.enemies[target as usize].y);
                    let ang = math::atan2((ty0 - py) as f64, (tx0 - px) as f64);
                    let perp = ang + pi_f * 0.5;
                    let mut f = self.new_fx(fxk::ZONE);
                    f.owner_slot = slot;
                    f.def_id = w.id;
                    f.level = ow_level;
                    f.x = tx0;
                    f.y = ty0;
                    f.radius = units(0.45).max(area * 55 / 100);
                    f.range = units(1.4).max(area * 2.max(n) * 42 / 100);
                    f.angle = (perp * 180.0 / pi_f).round();
                    f.every = 1.max(ticks(lv.tick as f64));
                    f.next_tick = tick + 1;
                    f.end_tick = tick + self.duration_ticks(pi, if lv.duration > 0.0 { lv.duration } else { 3.0 });
                    f.widx = wi;
                    f.lv = lvi;
                    f.base_damage = lv.damage as f64;
                    f.heal = lv.heal;
                    f.heal_pct = lv.heal_pct as f64;
                    f.zone_flags = w.zone;
                    f.follows = false;
                    self.begin_fx(f);
                    return true;
                }
                let mut used: Vec<i32> = Vec::new();
                for i in 0..n {
                    let (mut tx, mut ty) = (px, py);
                    if w.target != targeting::SELF && (w.zone & zone_flags::FOLLOWS_OWNER) == 0 {
                        let t = if w.target == targeting::LOWEST_HP_ALLY || w.target == targeting::RANDOM_ALLY { targeting::NEAREST } else { w.target };
                        let target = self.find_target(px, py, t, range, area, Some(&used));
                        if target < 0 {
                            if i == 0 {
                                return false;
                            }
                            break;
                        }
                        used.push(target);
                        tx = self.enemies[target as usize].x;
                        ty = self.enemies[target as usize].y;
                    }
                    let mut f = self.new_fx(fxk::ZONE);
                    f.owner_slot = slot;
                    f.def_id = w.id;
                    f.level = ow_level;
                    f.x = tx;
                    f.y = ty;
                    f.radius = area;
                    f.every = 1.max(ticks(lv.tick as f64));
                    f.next_tick = tick + 1;
                    f.end_tick = tick + self.duration_ticks(pi, if lv.duration > 0.0 { lv.duration } else { 3.0 });
                    f.widx = wi;
                    f.lv = lvi;
                    f.base_damage = lv.damage as f64;
                    f.heal = lv.heal;
                    f.heal_pct = lv.heal_pct as f64;
                    f.zone_flags = w.zone;
                    f.follows = (w.zone & zone_flags::FOLLOWS_OWNER) != 0;
                    self.begin_fx(f);
                    any = true;
                }
                any
            }
            weapon_kind::CHAIN => {
                let t = if w.target == targeting::SELF || w.target == targeting::FACING { targeting::NEAREST } else { w.target };
                let first = self.find_target(px, py, t, range, 0, None);
                if first < 0 {
                    return false;
                }
                let jumps = self.count_of(pi, lv, true);
                let jump = self.area_units(pi, if lv.area > 0.0 { lv.area } else { 3.0 });
                let mut hit: Vec<i32> = Vec::new();
                let (mut fx, mut fy, mut cur) = (px, py, first);
                let mut dmg = lv.damage as f64;
                let mut k = 0;
                while k <= jumps && cur >= 0 {
                    let (ex, ey) = (self.enemies[cur as usize].x, self.enemies[cur as usize].y);
                    self.add_event_abc(ev::CHAIN, 0, slot, cur, w.id, ow_level, fx, fy, 0.0, 0.0, 0.0, ex, ey);
                    fx = ex;
                    fy = ey;
                    hit.push(cur);
                    self.hit_enemy(pi, wi, lvi, cur as usize, dmg, fx, fy);
                    dmg *= 0f32.max(lv.falloff) as f64 / 100.0;
                    cur = self.find_target(fx, fy, targeting::NEAREST, jump, 0, Some(&hit));
                    k += 1;
                }
                if c.is_v4() {
                    self.on_trans_chain(pi, wi, lvi, first, jumps, jump, &mut hit);
                }
                true
            }
            weapon_kind::AURA => {
                let area = self.area_units(pi, if lv.area > 0.0 { lv.area } else { 3.0 });
                if w.target == targeting::LOWEST_HP_ALLY || w.target == targeting::RANDOM_ALLY {
                    let Some(qi) = self.lowest_ally(pi, range) else { return false };
                    let no_buffs = lv.buffs.as_ref().map_or(true, |b| b.is_empty());
                    if self.players[qi].hp >= self.players[qi].max_hp && no_buffs {
                        return false;
                    }
                    self.heal(pi, qi, lv, w.id);
                    self.give_buff(pi, qi, lvi, w.id);
                    let (qx, qy) = (self.players[qi].x, self.players[qi].y);
                    self.add_event_abc(ev::NOVA, 0, slot, qi as i32, w.id, ow_level, qx, qy, 1.0, 0.0, 0.0, 0, 0);
                    if c.is_v4() {
                        self.on_trans_aura_pulse(pi, wi, lvi, area);
                    }
                    return true;
                }
                for qi in 0..self.players.len() {
                    let q = &self.players[qi];
                    if !q.present || q.fallen || dist2(q.x, q.y, px, py) > area as i64 * area as i64 {
                        continue;
                    }
                    self.heal(pi, qi, lv, w.id);
                    self.give_buff(pi, qi, lvi, w.id);
                }
                if lv.damage > 0 {
                    let q = self.query(px, py, area + units(1.5));
                    for &i in &q {
                        let e = &self.enemies[i as usize];
                        let rr = area + e.radius;
                        if e.alive && dist2(px, py, e.x, e.y) <= rr as i64 * rr as i64 {
                            self.hit_enemy(pi, wi, lvi, i as usize, lv.damage as f64, px, py);
                        }
                    }
                }
                self.add_event_abc(ev::NOVA, 0, slot, -1, w.id, ow_level, px, py, area as f32 / UNIT as f32, 0.0, 0.0, 0, 0);
                if c.is_v4() {
                    self.on_trans_aura_pulse(pi, wi, lvi, area);
                }
                true
            }
            weapon_kind::TRAP => {
                let max = self.count_of(pi, lv, true);
                let mut mine = 0;
                let mut oldest: Option<usize> = None;
                for (i, f0) in self.fx.iter().enumerate() {
                    if f0.kind == fxk::TRAP && f0.owner_slot == slot && f0.widx == wi && !f0.ended {
                        mine += 1;
                        if oldest.is_none() {
                            oldest = Some(i);
                        }
                    }
                }
                if mine >= max {
                    if let Some(o) = oldest {
                        self.end_fx(o, false);
                    }
                }
                let mut f = self.new_fx(fxk::TRAP);
                f.owner_slot = slot;
                f.def_id = w.id;
                f.level = ow_level;
                f.x = px;
                f.y = py;
                f.radius = self.area_units(pi, if lv.area > 0.0 { lv.area } else { 1.0 });
                f.arm_tick = tick + ticks(lv.delay as f64);
                f.end_tick = tick + self.duration_ticks(pi, if lv.duration > 0.0 { lv.duration } else { 20.0 });
                f.widx = wi;
                f.lv = lvi;
                f.base_damage = lv.damage as f64;
                self.begin_fx(f);
                true
            }
            weapon_kind::SUMMON => {
                let alive = self.summons.iter().filter(|s| s.owner_slot == slot && s.widx == wi).count() as i32;
                if alive >= self.count_of(pi, lv, true) || self.summons.len() >= MAX_SUMMONS {
                    return true;
                }
                let sm = Summon {
                    id: self.next_summon_id,
                    owner_slot: slot,
                    x: px,
                    y: py,
                    widx: wi,
                    max_hp: 0.max(w.summon_hp),
                    hp: 0.max(w.summon_hp),
                    until: if lv.duration > 0.0 { tick + self.duration_ticks(pi, lv.duration) } else { i32::MAX },
                    next_attack: tick + 1,
                };
                self.next_summon_id = if self.next_summon_id >= 65535 { 1 } else { self.next_summon_id + 1 };
                let id = sm.id;
                self.summons.push(sm);
                self.add_event(ev::SUMMON, 0, slot, id, w.id, ow_level, px, py);
                true
            }
            _ => false,
        }
    }

    /// GiveBuff (HordeSim.Weapons.cs:363-370).
    fn give_buff(&mut self, from: usize, qi: usize, lvi: (usize, usize), weapon_id: i32) {
        let lv = &self.c.weapons[lvi.0].levels[lvi.1];
        if lv.buffs.as_ref().map_or(true, |b| b.is_empty()) {
            return;
        }
        let until = self.tick + self.duration_ticks(from, if lv.duration > 0.0 { lv.duration } else { 5.0 });
        let q = &mut self.players[qi];
        match q.buffs.iter_mut().find(|x| x.weapon_id == weapon_id && x.source_slot == from as i32) {
            None => {
                q.buffs.push(Buff { mods: lvi, until, source_slot: from as i32, weapon_id });
                self.recalc(qi, false);
            }
            Some(b) => {
                let changed = b.mods != lvi;
                b.mods = lvi;
                b.until = until;
                if changed {
                    self.recalc(qi, false);
                }
            }
        }
    }

    /// Shoot (HordeSim.Weapons.cs:372-382).
    #[allow(clippy::too_many_arguments)]
    fn shoot(&mut self, pi: usize, wi: usize, lvi: (usize, usize), ang: f64, speed: i32, range: i32, target: i32) {
        let c = self.c;
        let lv = &c.weapons[lvi.0].levels[lvi.1];
        let (px, py) = (self.players[pi].x, self.players[pi].y);
        let pierce = lv.pierce + if c.is_v4() { self.players[pi].count_passive_trans(c, passive_trans_kind::DEADEYE) } else { 0 };
        self.shoot_from(pi, wi, lvi, ang, speed, range, target, px, py, lv.damage as f64, pierce);
    }

    #[allow(clippy::too_many_arguments)]
    fn shoot_from(&mut self, pi: usize, wi: usize, lvi: (usize, usize), ang: f64, speed: i32, range: i32, target: i32, sx: i32, sy: i32, base_damage: f64, pierce_left: i32) {
        let c = self.c;
        let lv = &c.weapons[lvi.0].levels[lvi.1];
        let w = &c.weapons[self.players[pi].weapons[wi].def];
        let mut f = self.new_fx(fxk::SHOT);
        f.owner_slot = pi as i32;
        f.def_id = w.id;
        f.level = self.players[pi].weapons[wi].level;
        f.x = sx;
        f.y = sy;
        f.vx = round(math::cos(ang) * speed as f64);
        f.vy = round(math::sin(ang) * speed as f64);
        f.radius = units(0.3);
        f.end_tick = self.tick + 1.max(range / 1.max(speed));
        f.widx = wi;
        f.lv = lvi;
        f.base_damage = base_damage;
        f.pierce_left = pierce_left;
        f.burst_area = if lv.area > 0.0 { self.area_units(pi, lv.area) } else { 0 };
        f.target = if w.homing { target } else { -1 };
        self.begin_fx(f);
    }

    /// StepOrbiterWeapon (HordeSim.Weapons.cs:384-397).
    fn step_orbiter_weapon(&mut self, pi: usize, wi: usize, lvi: (usize, usize)) {
        let c = self.c;
        let lv = &c.weapons[lvi.0].levels[lvi.1];
        let orbit_id = self.players[pi].weapons[wi].orbit_fx_id;
        let live = self.fx.iter().any(|f| f.id == orbit_id && f.kind == fxk::ORBIT && !f.ended);
        if live || self.tick < self.players[pi].weapons[wi].next_tick {
            return;
        }
        let tick = self.tick;
        let mut o = self.new_fx(fxk::ORBIT);
        o.owner_slot = pi as i32;
        o.def_id = c.weapons[self.players[pi].weapons[wi].def].id;
        o.level = self.players[pi].weapons[wi].level;
        o.x = self.players[pi].x;
        o.y = self.players[pi].y;
        o.count = self.count_of(pi, lv, true);
        o.range = self.area_units(pi, if lv.range > 0.0 { lv.range } else { 2.0 });
        o.radius = self.area_units(pi, if lv.area > 0.0 { lv.area } else { 0.5 });
        o.turns = if lv.speed > 0.0 { lv.speed as f64 } else { 0.5 };
        o.angle = 0.0;
        o.every = 1.max(ticks(lv.tick as f64));
        o.end_tick = if lv.duration > 0.0 { tick + self.duration_ticks(pi, lv.duration) } else { i32::MAX };
        o.widx = wi;
        o.lv = lvi;
        o.base_damage = lv.damage as f64;
        o.follows = true;
        let ow = &mut self.players[pi].weapons[wi];
        ow.orbit_fx_id = o.id;
        ow.orb_hits.clear();
        self.begin_fx(o);
    }

    /// RestartOrbit (HordeSim.Weapons.cs:400-405): a weapon whose level changed: its orbit (if any) is made again.
    pub(super) fn restart_orbit(&mut self, pi: usize, wi: usize) {
        let ow = &self.players[pi].weapons[wi];
        if self.c.weapons[ow.def].kind != weapon_kind::ORBITER {
            return;
        }
        let id = ow.orbit_fx_id;
        if let Some(i) = self.fx.iter().position(|f| f.id == id && !f.ended) {
            self.end_fx(i, false);
        }
        self.players[pi].weapons[wi].next_tick = self.tick + 1;
    }

    /// StepSummons (HordeSim.Weapons.cs:407-446).
    fn step_summons(&mut self) {
        let c = self.c;
        let mut i = self.summons.len();
        while i > 0 {
            i -= 1;
            let tick = self.tick;
            let (owner, widx) = (self.summons[i].owner_slot as usize, self.summons[i].widx);
            let wdef = &c.weapons[self.players[owner].weapons[widx].def];
            if !self.players[owner].present || tick >= self.summons[i].until || (self.summons[i].max_hp > 0 && self.summons[i].hp <= 0) {
                let s = self.summons.remove(i);
                self.add_event(ev::SUMMON, evf::KILL, s.owner_slot, s.id, wdef.id, 0, s.x, s.y);
                if c.is_v4() && self.players[owner].present && wdef.trans == trans_kind::SUMMON_EXPLODE {
                    let lvi = self.lv_of(owner, widx);
                    let lv = &c.weapons[lvi.0].levels[lvi.1];
                    let rad = self.area_units(owner, 2.8);
                    self.strike_area(owner, widx, lvi, s.x, s.y, rad, 60.0f64.max(lv.damage as f64 * 2.5));
                }
                continue;
            }
            let lvi = self.lv_of(owner, widx);
            let lv = &c.weapons[lvi.0].levels[lvi.1];
            let reach = units(if lv.range > 0.0 { lv.range as f64 } else { 1.5 });
            let (sx, sy) = (self.summons[i].x, self.summons[i].y);
            let target = self.find_target(sx, sy, targeting::NEAREST, units(10.0), 0, None);
            let sp = 1.max(units(((if lv.speed > 0.0 { lv.speed } else { 4.0 }) / TICK_RATE as f32) as f64));
            let (gx, gy) = if target >= 0 { (self.enemies[target as usize].x, self.enemies[target as usize].y) } else { (self.players[owner].x + units(1.0), self.players[owner].y) };
            let dd = dist2(sx, sy, gx, gy);
            let stop = if target >= 0 { reach * 3 / 4 } else { units(0.8) };
            if dd > stop as i64 * stop as i64 {
                let d = isqrt(dd) as i64;
                let s = &mut self.summons[i];
                s.x = clamp(s.x + ((gx - s.x) as i64 * sp as i64 / d) as i32, self.min_x, self.max_x);
                s.y = clamp(s.y + ((gy - s.y) as i64 * sp as i64 / d) as i32, self.min_y, self.max_y);
            }
            let (sx, sy) = (self.summons[i].x, self.summons[i].y);
            if target >= 0 && tick >= self.summons[i].next_attack {
                let tr = (reach + self.enemies[target as usize].radius) as i64;
                if dist2(sx, sy, gx, gy) <= tr * tr {
                    self.summons[i].next_attack = tick + 1.max(ticks(lv.tick as f64));
                    if lv.area > 0.0 {
                        let area = self.area_units(owner, lv.area);
                        let q = self.query(gx, gy, area + units(1.5));
                        for &j in &q {
                            let e = &self.enemies[j as usize];
                            let rr = (area + e.radius) as i64;
                            if e.alive && dist2(gx, gy, e.x, e.y) <= rr * rr {
                                self.hit_enemy(owner, widx, lvi, j as usize, lv.damage as f64, sx, sy);
                            }
                        }
                        let level = self.players[owner].weapons[widx].level;
                        self.add_event_abc(ev::STRIKE, 0, owner as i32, -1, wdef.id, level, gx, gy, area as f32 / UNIT as f32, 0.0, 0.0, 0, 0);
                    } else {
                        self.hit_enemy(owner, widx, lvi, target as usize, lv.damage as f64, sx, sy);
                    }
                }
            }
        }
    }

    // =============================================================================================== lasting effects

    /// NewFx (HordeSim.Weapons.cs:450-462): an effect with the next id no live effect holds. Not yet on the list.
    pub(super) fn new_fx(&mut self, kind: u8) -> Fx {
        let mut id = self.next_fx_id;
        for _ in 0..70000 {
            if !self.fx.iter().any(|f| f.id == id && !f.ended) {
                break;
            }
            id = if id >= 65535 { 1 } else { id + 1 };
        }
        self.next_fx_id = if id >= 65535 { 1 } else { id + 1 };
        let uid = self.next_fx_uid;
        self.next_fx_uid += 1;
        Fx {
            uid, id, kind, owner_slot: -1, owner_enemy: -1, def_id: 0, level: 0, enemy_def_id: 0, x: 0, y: 0, vx: 0, vy: 0, radius: 0, range: 0, angle: 0.0, turns: 0.0, count: 0, target: -1,
            follows: false, start_tick: self.tick, arm_tick: 0, end_tick: 0, widx: 0, lv: (0, 0), base_damage: 0.0, damage: 0, pierce_left: 0, next_tick: 0, every: 0, heal: 0, heal_pct: 0.0,
            zone_flags: 0, burst_area: 0, hit_list: Vec::new(), ended: false,
        }
    }

    /// BeginFx (HordeSim.Weapons.cs:464-468).
    pub(super) fn begin_fx(&mut self, f: Fx) {
        if self.fx.len() >= MAX_FX && f.kind != fxk::ORBIT && f.kind != fxk::ENEMY_WARNING {
            return; // too many: this one is not made
        }
        self.fx_begun.push(f.uid);
        self.fx.push(f);
    }

    /// EndFx (HordeSim.Weapons.cs:470-475), by the effect's place on the list.
    pub(super) fn end_fx(&mut self, i: usize, hit: bool) {
        if self.fx[i].ended {
            return;
        }
        self.fx[i].ended = true;
        self.fx_ended.push((self.fx[i].id, hit));
    }

    /// StepFx (HordeSim.Weapons.cs:477-512).
    pub(super) fn step_fx(&mut self) {
        let n = self.fx.len();
        for i in 0..n {
            if self.fx[i].ended {
                continue;
            }
            match self.fx[i].kind {
                fxk::SHOT => self.step_shot(i),
                fxk::ENEMY_SHOT => self.step_enemy_shot(i),
                fxk::ZONE => self.step_zone(i),
                fxk::TRAP => self.step_trap(i),
                fxk::WARNING => {
                    if self.tick >= self.fx[i].end_tick {
                        let f = &self.fx[i];
                        let (owner, widx, lvi, x, y, radius, dmg, def_id) = (f.owner_slot as usize, f.widx, f.lv, f.x, f.y, f.radius, f.base_damage, f.def_id);
                        if self.players[owner].present {
                            if def_id == 991 {
                                let q = self.query(x, y, radius + units(1.5));
                                for &j in &q {
                                    let e = &self.enemies[j as usize];
                                    let rr = (radius + e.radius) as i64;
                                    if e.alive && dist2(x, y, e.x, e.y) <= rr * rr {
                                        self.proc_strike(owner, j as usize, dmg, damage_class::MAGIC);
                                    }
                                }
                                self.add_event_abc(ev::STRIKE, 0, owner as i32, -1, 991, 1, x, y, radius as f32 / UNIT as f32, 0.0, 0.0, 0, 0);
                            } else {
                                self.strike_area(owner, widx, lvi, x, y, radius, dmg);
                                if self.c.is_v4() {
                                    self.on_trans_warning_strike(owner, i);
                                }
                            }
                        }
                        self.end_fx(i, false);
                    }
                }
                fxk::ENEMY_WARNING => {
                    if self.tick >= self.fx[i].end_tick {
                        let f = &self.fx[i];
                        let (x, y, radius, dmg, enemy_def, owner_enemy, def_id) = (f.x, f.y, f.radius, f.damage, f.enemy_def_id, f.owner_enemy, f.def_id);
                        let reach = (radius + units(self.c.rules.body_radius as f64)) as i64;
                        for qi in 0..self.players.len() {
                            let q = &self.players[qi];
                            if q.present && !q.fallen && dist2(q.x, q.y, x, y) <= reach * reach {
                                self.hurt_player_from(qi, dmg, enemy_def, owner_enemy, x, y, 2);
                            }
                        }
                        self.add_event_abc(ev::STRIKE, 0, -1, owner_enemy, def_id, dmg, x, y, radius as f32 / UNIT as f32, 0.0, 0.0, 0, 0);
                        self.end_fx(i, false);
                    }
                }
                fxk::ORBIT => self.step_orbit(i),
                _ => {}
            }
        }
        // Fx.RemoveAll(x => x.Ended): the ones taken off are kept for the snapshot encoder until clear_outputs
        let mut kept = Vec::with_capacity(self.fx.len());
        for f in self.fx.drain(..) {
            if f.ended {
                self.fx_removed.push(f);
            } else {
                kept.push(f);
            }
        }
        self.fx = kept;
    }

    /// StrikeArea (HordeSim.Weapons.cs:514-524).
    #[allow(clippy::too_many_arguments)]
    pub(super) fn strike_area(&mut self, pi: usize, widx: usize, lvi: (usize, usize), x: i32, y: i32, radius: i32, dmg: f64) {
        let q = self.query(x, y, radius + units(1.5));
        for &i in &q {
            let e = &self.enemies[i as usize];
            let rr = (radius + e.radius) as i64;
            if e.alive && dist2(x, y, e.x, e.y) <= rr * rr {
                self.hit_enemy(pi, widx, lvi, i as usize, dmg, x, y);
            }
        }
        let ow = &self.players[pi].weapons[widx];
        let (id, level) = (self.c.weapons[ow.def].id, ow.level);
        self.add_event_abc(ev::STRIKE, 0, pi as i32, -1, id, level, x, y, radius as f32 / UNIT as f32, 0.0, 0.0, 0, 0);
    }

    /// StepShot (HordeSim.Weapons.cs:526-562).
    fn step_shot(&mut self, i: usize) {
        let owner = self.fx[i].owner_slot as usize;
        if !self.players[owner].present {
            self.end_fx(i, false);
            return;
        }
        let target = self.fx[i].target;
        if target >= 0 {
            let t = &self.enemies[target as usize];
            if t.alive {
                let (tx, ty) = (t.x, t.y);
                let f = &mut self.fx[i];
                let sp = isqrt(f.vx as i64 * f.vx as i64 + f.vy as i64 * f.vy as i64) as i64;
                let dx = (tx - f.x) as i64;
                let dy = (ty - f.y) as i64;
                let d = 1.max(isqrt(dx * dx + dy * dy)) as i64;
                f.vx = (f.vx * 3 + (dx * sp / d) as i32) / 4;
                f.vy = (f.vy * 3 + (dy * sp / d) as i32) / 4;
            }
        }
        {
            let f = &mut self.fx[i];
            f.x += f.vx;
            f.y += f.vy;
        }
        let (fx, fy, fr) = (self.fx[i].x, self.fx[i].y, self.fx[i].radius);
        if fx < self.min_x || fx > self.max_x || fy < self.min_y || fy > self.max_y {
            self.end_fx(i, false);
            return;
        }
        let q = self.query(fx, fy, fr + units(1.5));
        for &j in &q {
            let e = &self.enemies[j as usize];
            let key = j * 256 + e.serial;
            if !e.alive || self.fx[i].hit_list.contains(&key) {
                continue;
            }
            let rr = (fr + e.radius) as i64;
            if dist2(fx, fy, e.x, e.y) > rr * rr {
                continue;
            }
            let (widx, lvi, base, burst_area, vx, vy) = (self.fx[i].widx, self.fx[i].lv, self.fx[i].base_damage, self.fx[i].burst_area, self.fx[i].vx, self.fx[i].vy);
            if burst_area > 0 {
                self.strike_area(owner, widx, lvi, fx, fy, burst_area, base);
                self.end_fx(i, true);
                return;
            }
            self.fx[i].hit_list.push(key);
            self.hit_enemy(owner, widx, lvi, j as usize, base, fx - vx, fy - vy);
            let pierce = self.fx[i].pierce_left;
            self.fx[i].pierce_left -= 1;
            if pierce <= 0 {
                self.end_fx(i, true);
                return;
            }
            let falloff = self.c.weapons[lvi.0].levels[lvi.1].falloff;
            self.fx[i].base_damage *= 0f32.max(falloff) as f64 / 100.0;
            break;
        }
        if self.tick >= self.fx[i].end_tick && (!self.c.is_v4() || !self.step_trans_shot(i)) {
            self.end_fx(i, false);
        }
    }

    /// StepEnemyShot (HordeSim.Weapons.cs:564-581).
    fn step_enemy_shot(&mut self, i: usize) {
        {
            let f = &mut self.fx[i];
            f.x += f.vx;
            f.y += f.vy;
        }
        let (fx, fy, fr, dmg, def_id, owner_enemy) = (self.fx[i].x, self.fx[i].y, self.fx[i].radius, self.fx[i].damage, self.fx[i].def_id, self.fx[i].owner_enemy);
        if fx < self.min_x || fx > self.max_x || fy < self.min_y || fy > self.max_y || self.tick >= self.fx[i].end_tick {
            self.end_fx(i, false);
            return;
        }
        let is_v4 = self.c.is_v4();
        let blocked = self.fx.iter().any(|z| {
            if z.kind != fxk::ZONE || (z.zone_flags & zone_flags::BLOCKS_SHOTS) == 0 || z.ended {
                return false;
            }
            let (zx, zy) = if is_v4 { Self::wall_closest_point(z, fx, fy) } else { (z.x, z.y) };
            dist2(fx, fy, zx, zy) <= z.radius as i64 * z.radius as i64
        });
        if blocked {
            self.end_fx(i, true);
            return;
        }
        let body = (units(self.c.rules.body_radius as f64) + fr) as i64;
        for qi in 0..self.players.len() {
            let q = &self.players[qi];
            if !q.present || q.fallen || dist2(q.x, q.y, fx, fy) > body * body {
                continue;
            }
            self.hurt_player_from(qi, dmg, def_id, owner_enemy, fx, fy, 1);
            self.end_fx(i, true);
            return;
        }
    }

    /// StepZone (HordeSim.Weapons.cs:583-617).
    fn step_zone(&mut self, i: usize) {
        let c = self.c;
        let owner = self.fx[i].owner_slot as usize;
        let tick = self.tick;
        if !self.players[owner].present || tick >= self.fx[i].end_tick {
            if c.is_v4() && self.players[owner].present && tick >= self.fx[i].end_tick {
                self.step_trans_zone(owner, i, true);
            }
            self.end_fx(i, false);
            return;
        }
        if self.fx[i].follows {
            let (px, py) = (self.players[owner].x, self.players[owner].y);
            self.fx[i].x = px;
            self.fx[i].y = py;
        }
        if c.is_v4() {
            self.step_trans_zone(owner, i, false);
        }
        if tick < self.fx[i].next_tick {
            return;
        }
        self.fx[i].next_tick = tick + self.fx[i].every;
        let f = &self.fx[i];
        let (fx, fy, fr, f_range, f_zf, widx, lvi, base, heal, heal_pct, every, def_id) =
            (f.x, f.y, f.radius, f.range, f.zone_flags, f.widx, f.lv, f.base_damage, f.heal, f.heal_pct, f.every, f.def_id);
        let lv = &c.weapons[lvi.0].levels[lvi.1];
        if base > 0.0 || lv.effects.is_some() {
            let q_rad = fr + (if c.is_v4() && (f_zf & zone_flags::BLOCKS_ENEMIES) != 0 { f_range } else { 0 }) + units(1.5);
            let q = self.query(fx, fy, q_rad);
            for &j in &q {
                let (ex, ey, erad, ealive) = {
                    let e = &self.enemies[j as usize];
                    (e.x, e.y, e.radius, e.alive)
                };
                if !ealive {
                    continue;
                }
                let (zx, zy) = if c.is_v4() { Self::wall_closest_point(&self.fx[i], ex, ey) } else { (fx, fy) };
                let rr = (fr + erad) as i64;
                if dist2(zx, zy, ex, ey) > rr * rr {
                    continue;
                }
                if base > 0.0 {
                    self.hit_enemy(owner, widx, lvi, j as usize, base, zx, zy);
                } else {
                    self.apply_effects(owner, widx, lvi, j as usize, zx, zy);
                }
            }
        }
        if heal > 0 || heal_pct > 0.0 || lv.buffs.is_some() {
            for qi in 0..self.players.len() {
                let q = &self.players[qi];
                if !q.present || q.fallen || dist2(q.x, q.y, fx, fy) > fr as i64 * fr as i64 {
                    continue;
                }
                if heal > 0 || heal_pct > 0.0 {
                    self.heal(owner, qi, lv, def_id);
                }
                if lv.buffs.as_ref().is_some_and(|b| !b.is_empty()) {
                    let until = tick + every + 1;
                    let q = &mut self.players[qi];
                    match q.buffs.iter_mut().find(|x| x.weapon_id == def_id && x.source_slot == owner as i32) {
                        None => {
                            q.buffs.push(Buff { mods: lvi, until, source_slot: owner as i32, weapon_id: def_id });
                            self.recalc(qi, false);
                        }
                        Some(b) => b.until = b.until.max(until),
                    }
                }
            }
        }
    }

    /// StepTrap (HordeSim.Weapons.cs:619-634).
    fn step_trap(&mut self, i: usize) {
        let owner = self.fx[i].owner_slot as usize;
        if !self.players[owner].present || self.tick >= self.fx[i].end_tick {
            self.end_fx(i, false);
            return;
        }
        if self.tick < self.fx[i].arm_tick {
            return;
        }
        if self.c.is_v4() {
            self.step_trans_trap(owner, i, false);
        }
        let f = &self.fx[i];
        let (fx, fy, fr, widx, lvi, base) = (f.x, f.y, f.radius, f.widx, f.lv, f.base_damage);
        let q = self.query(fx, fy, fr + units(1.5));
        for &j in &q {
            let e = &self.enemies[j as usize];
            let rr = (fr + e.radius) as i64;
            if !e.alive || dist2(fx, fy, e.x, e.y) > rr * rr {
                continue;
            }
            self.strike_area(owner, widx, lvi, fx, fy, fr, base);
            if self.c.is_v4() {
                self.step_trans_trap(owner, i, true);
            }
            self.end_fx(i, true);
            return;
        }
    }

    /// StepOrbit (HordeSim.Weapons.cs:643-666).
    fn step_orbit(&mut self, i: usize) {
        let c = self.c;
        let owner = self.fx[i].owner_slot as usize;
        let tick = self.tick;
        let (widx, lvi, def_id) = (self.fx[i].widx, self.fx[i].lv, self.fx[i].def_id);
        if !self.players[owner].present || self.players[owner].fallen {
            self.end_fx(i, false);
            return;
        }
        if tick >= self.fx[i].end_tick {
            self.end_fx(i, false);
            if def_id != 990 {
                let lv = &c.weapons[lvi.0].levels[lvi.1];
                self.players[owner].weapons[widx].next_tick = tick + self.cooldown_ticks(owner, lv);
            }
            return;
        }
        let (px, py) = (self.players[owner].x, self.players[owner].y);
        self.fx[i].x = px;
        self.fx[i].y = py;
        let r_range = if c.is_v4() && def_id != 990 { self.trans_orbit_range(i) } else { self.fx[i].range };
        let f = &self.fx[i];
        let (count, angle, turns, start, fr, every, base) = (f.count, f.angle, f.turns, f.start_tick, f.radius, f.every, f.base_damage);
        for k in 0..count {
            let (ox, oy) = orb_place(px, py, r_range, angle, turns, count, k, (tick - start) as f64);
            if c.is_v4() && def_id != 990 {
                self.step_trans_orbit_shoot(owner, i, ox, oy);
            }
            let q = self.query(ox, oy, fr + units(1.5));
            for &j in &q {
                let e = &self.enemies[j as usize];
                let rr = (fr + e.radius) as i64;
                if !e.alive || dist2(ox, oy, e.x, e.y) > rr * rr {
                    continue;
                }
                if def_id == 990 {
                    if (tick - start) % every.max(1) == 0 {
                        self.proc_strike(owner, j as usize, base, damage_class::MAGIC);
                    }
                    continue;
                }
                let key = (k << 20) | (j << 8) | e.serial;
                let hits = &mut self.players[owner].weapons[widx].orb_hits;
                if let Some(&last) = hits.get(&key) {
                    if tick - last < every {
                        continue;
                    }
                }
                hits.insert(key, tick);
                self.hit_enemy(owner, widx, lvi, j as usize, base, ox, oy);
            }
        }
        if def_id != 990 {
            let hits = &mut self.players[owner].weapons[widx].orb_hits;
            if hits.len() > 2000 {
                hits.clear();
            }
        }
    }

    // =============================================================================================== weapon transcendence (h18trans, point 30)

    fn pull_enemies_toward(&mut self, cx: i32, cy: i32, radius_units: i32, pull_units: i32) {
        let q = self.query(cx, cy, radius_units + units(1.5));
        let c = self.c;
        let (min_x, max_x, min_y, max_y) = (self.min_x, self.max_x, self.min_y, self.max_y);
        for &i in &q {
            let e = &mut self.enemies[i as usize];
            let kb_res = c.enemies[e.def].knockback_resist;
            if !e.alive || kb_res >= 100.0 {
                continue;
            }
            let rr = (radius_units + e.radius) as i64;
            let dx = (cx - e.x) as i64;
            let dy = (cy - e.y) as i64;
            let dd = dx * dx + dy * dy;
            if dd <= 16 || dd > rr * rr {
                continue;
            }
            let d = 1.max(isqrt(dd)) as i64;
            let step = ((d as i32) - units(0.35)).min(round(pull_units as f64 * 0.2f64.max(1.0 - kb_res as f64 / 100.0))) as i64;
            if step <= 0 {
                continue;
            }
            e.x = clamp(e.x + (dx * step / d) as i32, min_x, max_x);
            e.y = clamp(e.y + (dy * step / d) as i32, min_y, max_y);
        }
    }

    fn on_trans_melee_swing(&mut self, pi: usize, wi: usize, lvi: (usize, usize), ang: f64, reach: i32) {
        let tk = self.c.weapons[self.players[pi].weapons[wi].def].trans;
        if tk == trans_kind::ARC_VORTEX {
            let (px, py) = (self.players[pi].x, self.players[pi].y);
            self.pull_enemies_toward(px, py, (reach as f64 * 1.6) as i32, units(1.4));
        } else if tk == trans_kind::ARC_WAVE {
            let lv = &self.c.weapons[lvi.0].levels[lvi.1];
            let sp = 1.max(units(10.0 * 0.2f64.max(1.0 + self.players[pi].s.projectile_speed_pct / 100.0) / TICK_RATE as f64));
            let w_range = units(6.5).max(reach * 3);
            let (px, py) = (self.players[pi].x, self.players[pi].y);
            let base_dmg = 1.0f64.max(lv.damage as f64 * 0.65);
            let pierce = 3.max(lv.pierce + 3);
            for i in -1..=1 {
                self.shoot_from(pi, wi, lvi, ang + i as f64 * 0.22, sp, w_range, -1, px, py, base_dmg, pierce);
            }
        }
    }

    fn on_trans_nova(&mut self, pi: usize, wi: usize, lvi: (usize, usize), area: i32) {
        let w = &self.c.weapons[self.players[pi].weapons[wi].def];
        let lv = &self.c.weapons[lvi.0].levels[lvi.1];
        let tk = w.trans;
        let (px, py) = (self.players[pi].x, self.players[pi].y);
        let ow_level = self.players[pi].weapons[wi].level;
        let tick = self.tick;
        if tk == trans_kind::NOVA_ECHO {
            let mut f = self.new_fx(fxk::WARNING);
            f.owner_slot = pi as i32;
            f.def_id = w.id;
            f.level = ow_level;
            f.x = px;
            f.y = py;
            f.radius = round(area as f64 * 1.4);
            f.end_tick = tick + 1.max(ticks(0.32));
            f.widx = wi;
            f.lv = lvi;
            f.base_damage = 1.0f64.max(lv.damage as f64 * 0.85);
            f.count = 1;
            self.begin_fx(f);
        } else if tk == trans_kind::NOVA_IMPLODE {
            self.pull_enemies_toward(px, py, (area as f64 * 1.55) as i32, units(1.6));
            let dur = self.duration_ticks(pi, 3.0);
            let mut z = self.new_fx(fxk::ZONE);
            z.owner_slot = pi as i32;
            z.def_id = w.id;
            z.level = ow_level;
            z.x = px;
            z.y = py;
            z.radius = round(area as f64 * 0.85);
            z.every = 1.max(ticks(0.4));
            z.next_tick = tick + z.every;
            z.end_tick = tick + dur;
            z.widx = wi;
            z.lv = lvi;
            z.base_damage = 1.0f64.max(lv.damage as f64 * 0.35);
            self.begin_fx(z);
        }
    }

    fn on_trans_warning_strike(&mut self, pi: usize, fi: usize) {
        let f = &self.fx[fi];
        let (widx, lvi, fx, fy, fr, f_def_id, f_level, f_base_dmg, f_count) = (f.widx, f.lv, f.x, f.y, f.radius, f.def_id, f.level, f.base_damage, f.count);
        if widx >= self.players[pi].weapons.len() || f_count != 0 {
            return;
        }
        let tk = self.c.weapons[self.players[pi].weapons[widx].def].trans;
        let tick = self.tick;
        if tk == trans_kind::AREA_CASCADE {
            for k in 0..3 {
                let a = k as f64 * (std::f64::consts::PI * 2.0 / 3.0) + (tick & 7) as f64 * 0.2;
                let d = round(fr as f64 * 0.85) as f64;
                let sx = clamp(fx + round(math::cos(a) * d), self.min_x, self.max_x);
                let sy = clamp(fy + round(math::sin(a) * d), self.min_y, self.max_y);
                let mut w = self.new_fx(fxk::WARNING);
                w.owner_slot = pi as i32;
                w.def_id = f_def_id;
                w.level = f_level;
                w.x = sx;
                w.y = sy;
                w.radius = units(0.8).max(round(fr as f64 * 0.75));
                w.end_tick = tick + 1.max(ticks(0.25));
                w.widx = widx;
                w.lv = lvi;
                w.base_damage = 1.0f64.max(f_base_dmg * 0.55);
                w.count = 1;
                self.begin_fx(w);
            }
        } else if tk == trans_kind::AREA_SCORCH {
            let dur = self.duration_ticks(pi, 3.5);
            let mut z = self.new_fx(fxk::ZONE);
            z.owner_slot = pi as i32;
            z.def_id = f_def_id;
            z.level = f_level;
            z.x = fx;
            z.y = fy;
            z.radius = units(1.0).max(round(fr as f64 * 0.9));
            z.every = 1.max(ticks(0.45));
            z.next_tick = tick + z.every;
            z.end_tick = tick + dur;
            z.widx = widx;
            z.lv = lvi;
            z.base_damage = 1.0f64.max(f_base_dmg * 0.35);
            z.count = 1;
            self.begin_fx(z);
        }
    }

    fn step_trans_shot(&mut self, i: usize) -> bool {
        let owner = self.fx[i].owner_slot as usize;
        let widx = self.fx[i].widx;
        if widx >= self.players[owner].weapons.len() {
            return false;
        }
        let tk = self.c.weapons[self.players[owner].weapons[widx].def].trans;
        if tk != trans_kind::BOLT_RETURN {
            return false;
        }
        if self.fx[i].count == 0 && self.tick >= self.fx[i].end_tick {
            let tick = self.tick;
            let f = &mut self.fx[i];
            f.count = 1;
            f.vx = -f.vx;
            f.vy = -f.vy;
            f.target = -1;
            f.hit_list.clear();
            let flight = 4.max(tick - f.start_tick);
            f.end_tick = tick + flight;
            return true;
        }
        false
    }

    fn step_trans_zone(&mut self, pi: usize, fi: usize, expiring: bool) {
        let f = &self.fx[fi];
        let (widx, lvi, fx, fy, fr, f_def_id, f_level, f_base_dmg, f_heal, f_heal_pct, f_count, f_follows, f_next_tick) =
            (f.widx, f.lv, f.x, f.y, f.radius, f.def_id, f.level, f.base_damage, f.heal, f.heal_pct, f.count, f.follows, f.next_tick);
        if widx >= self.players[pi].weapons.len() {
            return;
        }
        let tk = self.c.weapons[self.players[pi].weapons[widx].def].trans;
        if expiring {
            if tk == trans_kind::ZONE_COLLAPSE && f_count == 0 {
                let rad = round(fr as f64 * 1.4);
                if f_base_dmg > 0.0 {
                    self.strike_area(pi, widx, lvi, fx, fy, rad, 40.0f64.max(f_base_dmg * 2.2));
                }
                if f_heal > 0 || f_heal_pct > 0.0 {
                    let lv = &self.c.weapons[lvi.0].levels[lvi.1];
                    for qi in 0..self.players.len() {
                        let q = &self.players[qi];
                        if q.present && !q.fallen && dist2(q.x, q.y, fx, fy) <= rad as i64 * rad as i64 {
                            self.heal(pi, qi, lv, f_def_id);
                        }
                    }
                    self.add_event_abc(ev::NOVA, 0, pi as i32, -1, f_def_id, f_level, fx, fy, rad as f32 / UNIT as f32, 0.0, 0.0, 0, 0);
                }
            }
            return;
        }
        if tk == trans_kind::ZONE_DRIFT && !f_follows {
            let t = self.find_target(fx, fy, targeting::NEAREST, units(10.0), 0, None);
            if t >= 0 {
                let e = &self.enemies[t as usize];
                let dx = (e.x - fx) as i64;
                let dy = (e.y - fy) as i64;
                let d = isqrt(dx * dx + dy * dy) as i64;
                if d > 8 {
                    let sp = 1.max(units(2.2 / TICK_RATE as f64)) as i64;
                    let nx = clamp(fx + (dx * sp / d) as i32, self.min_x, self.max_x);
                    let ny = clamp(fy + (dy * sp / d) as i32, self.min_y, self.max_y);
                    self.fx[fi].x = nx;
                    self.fx[fi].y = ny;
                }
            }
        } else if tk == trans_kind::ZONE_COLLAPSE && self.tick >= f_next_tick {
            self.pull_enemies_toward(fx, fy, (fr as f64 * 1.35) as i32, units(0.75));
        }
    }

    fn trans_orbit_range(&self, fi: usize) -> i32 {
        let f = &self.fx[fi];
        let owner = f.owner_slot as usize;
        if f.widx < self.players[owner].weapons.len() {
            let tk = self.c.weapons[self.players[owner].weapons[f.widx].def].trans;
            if tk == trans_kind::ORBIT_SPIRAL {
                let wave = math::sin((self.tick - f.start_tick) as f64 * 0.12);
                return units(0.8).max(round(f.range as f64 * (1.0 + 0.42 * wave)));
            }
        }
        f.range
    }

    fn step_trans_orbit_shoot(&mut self, pi: usize, fi: usize, ox: i32, oy: i32) {
        let f = &self.fx[fi];
        let (widx, lvi, start, f_base_dmg) = (f.widx, f.lv, f.start_tick, f.base_damage);
        if widx >= self.players[pi].weapons.len() {
            return;
        }
        let tk = self.c.weapons[self.players[pi].weapons[widx].def].trans;
        if tk != trans_kind::ORBIT_SHOOT {
            return;
        }
        if (self.tick - start) % 1.max(ticks(1.5)) != 0 {
            return;
        }
        let target = self.find_target(ox, oy, targeting::NEAREST, units(8.0), 0, None);
        if target < 0 {
            return;
        }
        let (ex, ey) = (self.enemies[target as usize].x, self.enemies[target as usize].y);
        let sp = 1.max(units(10.0 / TICK_RATE as f64));
        let ang = math::atan2((ey - oy) as f64, (ex - ox) as f64);
        self.shoot_from(pi, widx, lvi, ang, sp, units(8.0), target, ox, oy, 1.0f64.max(f_base_dmg * 0.7), 2);
    }

    #[allow(clippy::too_many_arguments)]
    fn on_trans_chain(&mut self, pi: usize, wi: usize, lvi: (usize, usize), first: i32, jumps: i32, jump: i32, hit: &mut Vec<i32>) {
        let w = &self.c.weapons[self.players[pi].weapons[wi].def];
        let lv = &self.c.weapons[lvi.0].levels[lvi.1];
        let tk = w.trans;
        let ow_level = self.players[pi].weapons[wi].level;
        if tk == trans_kind::CHAIN_FORK && first >= 0 && self.enemies[first as usize].alive {
            let (mut fx, mut fy) = (self.enemies[first as usize].x, self.enemies[first as usize].y);
            let mut cur = self.find_target(fx, fy, targeting::NEAREST, jump, 0, Some(hit));
            let dmg = lv.damage as f64 * 0.85;
            let mut k = 0;
            while k < jumps && cur >= 0 {
                let (ex, ey) = (self.enemies[cur as usize].x, self.enemies[cur as usize].y);
                self.add_event_abc(ev::CHAIN, 0, pi as i32, cur, w.id, ow_level, fx, fy, 0.0, 0.0, 0.0, ex, ey);
                fx = ex;
                fy = ey;
                hit.push(cur);
                self.hit_enemy(pi, wi, lvi, cur as usize, dmg, fx, fy);
                cur = self.find_target(fx, fy, targeting::NEAREST, jump, 0, Some(hit));
                k += 1;
            }
        } else if tk == trans_kind::CHAIN_OVERLOAD {
            let rad = self.area_units(pi, 1.8);
            let tick = self.tick;
            let hit_copy = hit.clone();
            for idx in hit_copy {
                let ei = idx as usize;
                let (ex, ey) = (self.enemies[ei].x, self.enemies[ei].y);
                let eff_res = self.c.enemies[self.enemies[ei].def].effect_resist;
                if self.enemies[ei].alive && eff_res < 100.0 {
                    let e = &mut self.enemies[ei];
                    e.stun_until = e.stun_until.max(tick + ticks(0.6 * (1.0 - eff_res as f64 / 100.0)));
                }
                self.strike_area(pi, wi, lvi, ex, ey, rad, 1.0f64.max(lv.damage as f64 * 0.55));
            }
        }
    }

    fn on_trans_aura_pulse(&mut self, pi: usize, wi: usize, lvi: (usize, usize), area: i32) {
        let w = &self.c.weapons[self.players[pi].weapons[wi].def];
        let lv = &self.c.weapons[lvi.0].levels[lvi.1];
        let tk = w.trans;
        let (px, py) = (self.players[pi].x, self.players[pi].y);
        let ow_level = self.players[pi].weapons[wi].level;
        if tk == trans_kind::AURA_PULSE {
            let scorch = 45.0f64.max(if lv.damage > 0 { lv.damage as f64 * 0.6 } else { 55.0 });
            let q = self.query(px, py, area + units(1.5));
            for &i in &q {
                let ei = i as usize;
                let e = &self.enemies[ei];
                let rr = (area + e.radius) as i64;
                if e.alive && dist2(px, py, e.x, e.y) <= rr * rr {
                    self.hit_enemy(pi, wi, lvi, ei, scorch, px, py);
                }
            }
            for qi in 0..self.players.len() {
                let q = &self.players[qi];
                if !q.present || q.fallen || dist2(q.x, q.y, px, py) > area as i64 * area as i64 {
                    continue;
                }
                let h = 2.max(q.max_hp * 4 / 100);
                let before = q.hp;
                let new_hp = q.max_hp.min(before + h);
                let (qx, qy) = (q.x, q.y);
                self.players[qi].hp = new_hp;
                if new_hp > before {
                    self.add_event(ev::HEAL, 0, qi as i32, pi as i32, w.id, new_hp - before, qx, qy);
                }
            }
        } else if tk == trans_kind::AURA_NOVA {
            self.trans[pi].aura_pulses += 1;
            if self.trans[pi].aura_pulses >= 3 {
                self.trans[pi].aura_pulses = 0;
                let rad = round(area as f64 * 1.45);
                let blast = 90.0f64.max(lv.damage as f64 * 1.8);
                let tick = self.tick;
                let q = self.query(px, py, rad + units(1.5));
                for &i in &q {
                    let ei = i as usize;
                    let e = &self.enemies[ei];
                    let rr = (rad + e.radius) as i64;
                    if !e.alive || dist2(px, py, e.x, e.y) > rr * rr {
                        continue;
                    }
                    let eff_res = self.c.enemies[e.def].effect_resist;
                    if !self.hit_enemy(pi, wi, lvi, ei, blast, px, py) && self.enemies[ei].alive && eff_res < 100.0 {
                        let e = &mut self.enemies[ei];
                        e.stun_until = e.stun_until.max(tick + ticks(0.8 * (1.0 - eff_res as f64 / 100.0)));
                    }
                }
                self.add_event_abc(ev::STRIKE, 0, pi as i32, -1, w.id, ow_level, px, py, rad as f32 / UNIT as f32, 0.0, 0.0, 0, 0);
            }
        }
    }

    fn step_trans_trap(&mut self, pi: usize, fi: usize, triggered: bool) {
        let f = &self.fx[fi];
        let (widx, lvi, fx, fy, fr, f_def_id, f_level, f_base_dmg, f_count, f_arm_tick) =
            (f.widx, f.lv, f.x, f.y, f.radius, f.def_id, f.level, f.base_damage, f.count, f.arm_tick);
        if widx >= self.players[pi].weapons.len() {
            return;
        }
        let tk = self.c.weapons[self.players[pi].weapons[widx].def].trans;
        if !triggered {
            if tk == trans_kind::TRAP_SEEKER && self.tick >= f_arm_tick {
                let t = self.find_target(fx, fy, targeting::NEAREST, units(5.0), 0, None);
                if t >= 0 {
                    let e = &self.enemies[t as usize];
                    let dx = (e.x - fx) as i64;
                    let dy = (e.y - fy) as i64;
                    let d = isqrt(dx * dx + dy * dy) as i64;
                    if d > 8 {
                        let sp = 1.max(units(3.2 / TICK_RATE as f64)) as i64;
                        let nx = clamp(fx + (dx * sp / d) as i32, self.min_x, self.max_x);
                        let ny = clamp(fy + (dy * sp / d) as i32, self.min_y, self.max_y);
                        self.fx[fi].x = nx;
                        self.fx[fi].y = ny;
                    }
                }
            }
            return;
        }
        if tk == trans_kind::TRAP_CLUSTER && f_count == 0 {
            let tick = self.tick;
            let dur = self.duration_ticks(pi, 10.0);
            for k in 0..3 {
                let a = k as f64 * (std::f64::consts::PI * 2.0 / 3.0) + (tick & 7) as f64 * 0.3;
                let d = units(1.6) as f64;
                let mut m = self.new_fx(fxk::TRAP);
                m.owner_slot = pi as i32;
                m.def_id = f_def_id;
                m.level = f_level;
                m.x = clamp(fx + round(math::cos(a) * d), self.min_x, self.max_x);
                m.y = clamp(fy + round(math::sin(a) * d), self.min_y, self.max_y);
                m.radius = units(0.8).max(round(fr as f64 * 0.7));
                m.arm_tick = tick + ticks(0.35);
                m.end_tick = tick + dur;
                m.widx = widx;
                m.lv = lvi;
                m.base_damage = 1.0f64.max(f_base_dmg * 0.55);
                m.count = 1;
                self.begin_fx(m);
            }
        }
    }
}

/// OrbPlace (HordeSim.Weapons.cs:637-641): orb i's place at tick t (the client draws it with the same formula).
#[allow(clippy::too_many_arguments)]
pub fn orb_place(cx: i32, cy: i32, range: i32, angle_deg: f64, turns: f64, count: i32, i: i32, ticks_since_start: f64) -> (i32, i32) {
    let a = (angle_deg + 360.0 * turns * ticks_since_start / TICK_RATE as f64 + i as f64 * 360.0 / 1.max(count) as f64) * std::f64::consts::PI / 180.0;
    (cx + round(math::cos(a) * range as f64), cy + round(math::sin(a) * range as f64))
}
