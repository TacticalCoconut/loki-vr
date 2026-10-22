//! The `skill_damage` map flag's data (map.hpp struct s_skill_damage): mapdata->damage_adjust (every skill) and
//! mapdata->skill_damage (a `std::unordered_map<uint16, s_skill_damage>` of single skills), as map.cpp
//! map_setmapflag_sub MF_SKILL_DAMAGE and map_skill_damage_add fill them. battle.cpp battle_skill_damage_map reads
//! them, @mapinfo lists them (the single skills in the unordered_map's libstdc++ order, skill/umap.rs, as
//! skillunit's skill_duration list does).

use spacetimedb::{table, Table};

use crate::skill::umap::Umap;
use crate::world::Ctx;

/// map.hpp e_skill_damage_type: SKILLDMG_PC, SKILLDMG_MOB, SKILLDMG_BOSS, SKILLDMG_OTHER, SKILLDMG_MAX, SKILLDMG_CASTER.
pub const SKILLDMG_MAX: usize = 4;

/// map.hpp struct s_skill_damage: the caster types (BL_* bits) and a rate per target type (%, capped -100..100000).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct SkillDamage {
    pub caster: u16,
    pub rate: [i32; SKILLDMG_MAX],
}

/// mapdata->damage_adjust, for a map that has one (no row = `{}`).
#[table(accessor = map_damage_adjust)]
pub struct MapDamageAdjust {
    #[primary_key]
    pub map: u16,
    pub caster: u16,
    pub rate: Vec<i32>,
}

/// One mapdata->skill_damage entry; key = map << 16 | skill id.
#[table(accessor = map_skill_damage, index(accessor = by_map, btree(columns = [map])))]
pub struct MapSkillDamage {
    #[primary_key]
    pub key: u32,
    pub map: u16,
    pub skill_id: u16,
    pub caster: u16,
    pub rate: Vec<i32>,
}

/// mapdata->skill_damage's hash table order per map (skill/umap.rs).
#[table(accessor = map_skill_damage_order)]
pub struct MapSkillDamageOrder {
    #[primary_key]
    pub map: u16,
    pub order: Vec<u16>,
    pub buckets: u64,
    pub next_resize: u64,
}

fn key(m: u16, skill_id: u16) -> u32 {
    ((m as u32) << 16) | skill_id as u32
}

fn rates(v: &[i32]) -> [i32; SKILLDMG_MAX] {
    let mut r = [0; SKILLDMG_MAX];
    for (d, s) in r.iter_mut().zip(v) {
        *d = *s;
    }
    r
}

fn order(c: &Ctx, m: u16) -> Umap {
    c.db().map_skill_damage_order().map().find(m).map(|r| Umap { order: r.order, buckets: r.buckets, next_resize: r.next_resize }).unwrap_or_default()
}

fn put_order(c: &Ctx, m: u16, u: Umap) {
    let row = MapSkillDamageOrder { map: m, order: u.order, buckets: u.buckets, next_resize: u.next_resize };
    if c.db().map_skill_damage_order().map().find(m).is_some() {
        c.db().map_skill_damage_order().map().update(row);
    } else {
        c.db().map_skill_damage_order().insert(row);
    }
}

/// mapdata->damage_adjust (`{}` when the map has none).
pub fn damage_adjust(c: &Ctx, m: u16) -> SkillDamage {
    c.db().map_damage_adjust().map().find(m).map(|r| SkillDamage { caster: r.caster, rate: rates(&r.rate) }).unwrap_or_default()
}

fn put_damage_adjust(c: &Ctx, m: u16, d: SkillDamage) {
    let row = MapDamageAdjust { map: m, caster: d.caster, rate: d.rate.to_vec() };
    if c.db().map_damage_adjust().map().find(m).is_some() {
        c.db().map_damage_adjust().map().update(row);
    } else {
        c.db().map_damage_adjust().insert(row);
    }
}

/// map.cpp map_setmapflag_sub MF_SKILL_DAMAGE with status on and `!args->flag_val` (the every-skill adjustment):
/// false ("Skill damage adjustment without casting type") when the caster is 0; else `damage_adjust.caster |=
/// caster` and the rates, capped to -100..100000.
pub fn set_damage_adjust(c: &Ctx, m: u16, args: &SkillDamage) -> bool {
    if args.caster == 0 {
        let name = crate::data::maps().get(m).map(|d| d.name.clone()).unwrap_or_default();
        log::error!("map_setmapflag: Skill damage adjustment without casting type for map {name}.");
        return false;
    }
    let mut d = damage_adjust(c, m);
    d.caster |= args.caster;
    for i in 0..SKILLDMG_MAX {
        d.rate[i] = args.rate[i].clamp(-100, 100000);
    }
    put_damage_adjust(c, m, d);
    true
}

/// map.cpp map_skill_damage_add: an existing entry takes the new rates and keeps its caster bits too
/// (`args->skill_damage.caster |= m->skill_damage[skill_id].caster`), else `insert({ skill_id, args })`.
pub fn map_skill_damage_add(c: &Ctx, m: u16, skill_id: u16, args: &SkillDamage) {
    let k = key(m, skill_id);
    match c.db().map_skill_damage().key().find(k) {
        Some(mut r) => {
            r.caster |= args.caster;
            r.rate = args.rate.to_vec();
            c.db().map_skill_damage().key().update(r);
        }
        None => {
            c.db().map_skill_damage().insert(MapSkillDamage { key: k, map: m, skill_id, caster: args.caster, rate: args.rate.to_vec() });
            let mut u = order(c, m);
            u.insert(skill_id);
            put_order(c, m, u);
        }
    }
}

/// map.cpp map_setmapflag_sub MF_SKILL_DAMAGE with status off (and map_flags_init): `damage_adjust = {}`,
/// `skill_damage.clear()` (the unordered_map keeps its bucket count).
pub fn clear(c: &Ctx, m: u16) {
    c.db().map_damage_adjust().map().delete(m);
    let keys: Vec<u32> = c.db().map_skill_damage().by_map().filter(m).map(|r| r.key).collect();
    for k in keys {
        c.db().map_skill_damage().key().delete(k);
    }
    if c.db().map_skill_damage_order().map().find(m).is_some() {
        let mut u = order(c, m);
        u.clear();
        put_order(c, m, u);
    }
}

/// mapdata->skill_damage[skill_id], when present.
pub fn skill_damage(c: &Ctx, m: u16, skill_id: u16) -> Option<SkillDamage> {
    c.db().map_skill_damage().key().find(key(m, skill_id)).map(|r| SkillDamage { caster: r.caster, rate: rates(&r.rate) })
}

/// mapdata->skill_damage in its iteration order: (skill id, entry).
pub fn skill_damage_list(c: &Ctx, m: u16) -> Vec<(u16, SkillDamage)> {
    order(c, m).order.into_iter().filter_map(|id| skill_damage(c, m, id).map(|d| (id, d))).collect()
}

/// Every map that has damage data (map_flags_init clears them all).
pub fn maps_with_data(c: &Ctx) -> Vec<u16> {
    let mut maps: Vec<u16> = c.db().map_damage_adjust().iter().map(|r| r.map).collect();
    maps.extend(c.db().map_skill_damage().iter().map(|r| r.map));
    maps.extend(c.db().map_skill_damage_order().iter().map(|r| r.map));
    maps.sort_unstable();
    maps.dedup();
    maps
}

/// The world is built again (a new map server: every map_data is new, no bucket count kept).
pub fn on_world_rebuilt(c: &Ctx) {
    for m in c.db().map_damage_adjust().iter().map(|r| r.map).collect::<Vec<_>>() {
        c.db().map_damage_adjust().map().delete(m);
    }
    for k in c.db().map_skill_damage().iter().map(|r| r.key).collect::<Vec<_>>() {
        c.db().map_skill_damage().key().delete(k);
    }
    for m in c.db().map_skill_damage_order().iter().map(|r| r.map).collect::<Vec<_>>() {
        c.db().map_skill_damage_order().map().delete(m);
    }
}

/// map.cpp map_data_copy: `memcpy(&dst->damage_adjust, &src->damage_adjust)` and
/// `dst->skill_damage.insert(src->skill_damage.begin(), src->skill_damage.end())` (in the source's order; a key the
/// destination already has keeps its entry).
pub fn copy(c: &Ctx, dst: u16, src: u16) {
    let d = damage_adjust(c, src);
    if d == SkillDamage::default() {
        c.db().map_damage_adjust().map().delete(dst);
    } else {
        put_damage_adjust(c, dst, d);
    }
    for (skill_id, e) in skill_damage_list(c, src) {
        if skill_damage(c, dst, skill_id).is_none() {
            map_skill_damage_add(c, dst, skill_id, &e);
        }
    }
}

/// npc.cpp npc_parse_mapflag MF_SKILL_DAMAGE's `sscanf(w4, "%30[^,],%23[^,],%11d,%11d,%11d,%11d[^\n]", …) >= 3`: the
/// skill name, the caster (digits or a constant name) and one to four rates, the rates not read left at 0.
pub fn parse_w4(w4: &[u8]) -> Option<(String, String, [i32; SKILLDMG_MAX])> {
    let mut s = crate::cstr::Scan::new(w4);
    let skill = s.set(30, b",")?;
    if !s.lit(b',') {
        return None;
    }
    let caster = s.set(23, b",")?;
    if !s.lit(b',') {
        return None;
    }
    let mut rate = [0; SKILLDMG_MAX];
    rate[0] = s.int(11)? as i32;
    for r in rate.iter_mut().skip(1) {
        if !s.lit(b',') {
            break;
        }
        match s.int(11) {
            Some(v) => *r = v as i32,
            None => break,
        }
    }
    Some((skill, caster, rate))
}

#[cfg(test)]
mod tests {
    use super::*;

    /// "%30[^,],%23[^,],%11d,%11d,%11d,%11d": at least the skill, the caster and one rate; later rates optional.
    #[test]
    fn skill_damage_mapflag_fields() {
        assert_eq!(parse_w4(b"all,BL_PC,50,40,30,20"), Some(("all".into(), "BL_PC".into(), [50, 40, 30, 20])));
        assert_eq!(parse_w4(b"MG_FIREBOLT,1,-10"), Some(("MG_FIREBOLT".into(), "1".into(), [-10, 0, 0, 0])));
        assert_eq!(parse_w4(b"all,BL_PC,50,40"), Some(("all".into(), "BL_PC".into(), [50, 40, 0, 0])));
        assert_eq!(parse_w4(b"all,BL_PC"), None);
        assert_eq!(parse_w4(b"all"), None);
    }
}
