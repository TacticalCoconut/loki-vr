//! The class architecture of rAthena's `src/map/skills/` (e985006): every skill's own code lives in a class derived
//! from `SkillImpl` (skills/skill_impl.hpp), created once per skill by `SkillFactoryImpl::create` (skill_factory.cpp)
//! when the skill database finishes loading (skill.cpp SkillDatabase::loadingFinished: `it.second->impl =
//! SkillFactoryImpl::getInstance()->create(skill_id)`), and called by the skill pipeline through `skill->impl->…`.
//!
//! Here:
//! - [`SkillImpl`] is the trait with every virtual method of `SkillImpl` and the same default behaviour (no-op);
//! - [`StatusSkillImpl`], [`WeaponSkillImpl`] and [`RecursiveDamageSplash`] are the three generic classes of
//!   skill_impl.cpp (a class "deriving" from SkillImplRecursiveDamageSplash implements [`RecursiveDamageSplash`] for
//!   the virtuals it overrides and forwards castendDamageId / castendPos2 to [`splash_castend_damage_id`] /
//!   [`splash_castend_pos2`], which [`splash_skill_defaults!`] writes for it);
//! - [`skill_impl`] is `skill_db.find(id)->impl`: built once from the family factories in skill_factory.cpp's order
//!   ([`FACTORIES`]), for the skills of the skill database only.
//!
//! One file per family (`skills/<family>/mod.rs`, rAthena's `skill_factory_<family>.cpp`) holds that family's `create`
//! and one file per class, as rAthena does. The families whose classes are not ported yet answer `None` (the next
//! wave's agents port them on top of this trait); the pipeline then takes rAthena's own "no impl" path.

use crate::battle::calc::Damage;
use crate::db::skills::*;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub mod acolyte;
pub mod archer;
pub mod custom;
pub mod elemental;
pub mod gunslinger;
pub mod homunculus;
pub mod mage;
pub mod mercenary;
pub mod merchant;
pub mod ninja;
pub mod novice;
pub mod npc;
pub mod other;
pub mod summoner;
pub mod swordman;
pub mod taekwon;
pub mod thief;

/// skills/skill_impl.hpp `class SkillImpl`: one skill's own behaviour. Every method defaults to rAthena's no-op.
///
/// `src` / `target` are the units as they are when the pipeline calls (block_list pointers in rAthena); a method that
/// changes a unit (status changes, damage, moves) and then needs it again re-reads it (`crate::unit::get`).
#[allow(unused_variables, clippy::too_many_arguments)]
pub trait SkillImpl: Send + Sync {
    /// SkillImpl::getSkillId.
    fn skill_id(&self) -> u16;

    /// Effect of the skill - replaces switch statements in skill_castend_nodamage_id.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {}

    /// Effect of the skill - replaces switch statements in skill_castend_damage_id.
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {}

    /// Effect of the skill - replaces switch statements in skill_castend_pos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {}

    /// Calculate skill damage ratio - replaces battle_calc_attack_skill_ratio() switch.
    fn calculate_skill_ratio(&self, c: &Ctx, wd: &Damage, src: &Unit, target: &Unit, skill_lv: u16, base_skillratio: &mut i32, mflag: i32) {}

    /// Modify hit rate for this skill - replaces hit rate switch statements.
    fn modify_hit_rate(&self, c: &Ctx, hit_rate: &mut i16, src: &Unit, target: &Unit, skill_lv: u16) {}

    /// Apply additional effects on the target after damage - called from skill_additional_effect.
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, attack_type: i32, dmg_lv: u8) {}

    /// Apply additional effects on the caster after damage - called from skill_counter_additional_effect.
    fn apply_counter_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, attack_type: &mut i32) {}

    /// Allows modifying the damage data right after initialization.
    fn modify_damage_data(&self, c: &Ctx, dmg: &mut Damage, src: &Unit, target: &Unit, skill_lv: u16) {}

    /// Allows modifying the element type of attack.
    fn modify_element(&self, c: &Ctx, dmg: &Damage, src: &Unit, target: &Unit, skill_lv: u16, element: &mut i32, flag: i32) {}
}

// ---------------------------------------------------------------------------------------------- StatusSkillImpl

/// skill_impl.cpp `class StatusSkillImpl`: starts the skill's status change (skill_get_sc) on the target for
/// skill_get_time; with `end_if_running` a running one is ended instead.
pub struct StatusSkillImpl {
    pub id: u16,
    pub end_if_running: bool,
}

impl StatusSkillImpl {
    /// StatusSkillImpl(skillId, end_if_running = false).
    pub fn new(id: u16) -> Self {
        StatusSkillImpl { id, end_if_running: false }
    }
    pub fn new_toggle(id: u16, end_if_running: bool) -> Self {
        StatusSkillImpl { id, end_if_running }
    }
}

/// StatusSkillImpl::castendNoDamageId (also what a class deriving from StatusSkillImpl calls as its parent's).
pub fn status_castend_nodamage_id(c: &Ctx, skill_id: u16, end_if_running: bool, src: &Unit, target: &Unit, skill_lv: u16) {
    use crate::status::sc;
    let type_ = skill_get_sc(skill_id) as i32;
    if type_ == crate::status::consts::SC_NONE {
        return;
    }
    if end_if_running {
        // status_get_sc(target) and tsc->hasSCE(type)
        if crate::battle::mobif::has_sc(target.kind) && sc::has_sce(c, target.id, type_) {
            let ended = sc::status_change_end(c, target.id, type_, 0) != 0;
            crate::skill::clif::clif_skill_nodamage(c, Some(src), target, skill_id, skill_lv as i32, ended);
            return;
        }
    }
    let ok = sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_time(skill_id, skill_lv) as i64);
    let target = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
    crate::skill::clif::clif_skill_nodamage(c, Some(src), &target, skill_id, skill_lv as i32, ok);
}

impl SkillImpl for StatusSkillImpl {
    fn skill_id(&self) -> u16 {
        self.id
    }
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        status_castend_nodamage_id(c, self.id, self.end_if_running, src, target, skill_lv);
    }
}

// ---------------------------------------------------------------------------------------------- WeaponSkillImpl

/// skill_impl.cpp `class WeaponSkillImpl`: a single weapon hit (skill_attack BF_WEAPON).
pub struct WeaponSkillImpl {
    pub id: u16,
}

/// WeaponSkillImpl::castendDamageId.
pub fn weapon_castend_damage_id(c: &Ctx, skill_id: u16, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
    crate::skill::attack::skill_attack(c, crate::battle::BF_WEAPON, src, src, target, skill_id, skill_lv, tick, flag);
}

impl SkillImpl for WeaponSkillImpl {
    fn skill_id(&self) -> u16 {
        self.id
    }
    fn castend_damage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        weapon_castend_damage_id(c, self.id, src, target, skill_lv, tick, *flag);
    }
}

// ---------------------------------------------------------------------------------------------- SkillImplRecursiveDamageSplash

/// skill_impl.cpp `class SkillImplRecursiveDamageSplash`: a splash attack around the target that calls
/// skill_castend_damage_id again (flag | 1) for every unit it finds. Its own virtuals, with their defaults.
#[allow(unused_variables)]
pub trait RecursiveDamageSplash: SkillImpl {
    /// SkillImplRecursiveDamageSplash::getSearchSize: skill_get_splash.
    fn get_search_size(&self, c: &Ctx, src: &Unit, skill_lv: u16) -> i16 {
        skill_get_splash(self.skill_id(), skill_lv) as i16
    }

    /// SkillImplRecursiveDamageSplash::getSplashSearchSize: skill_get_splash.
    fn get_splash_search_size(&self, c: &Ctx, src: &Unit, skill_lv: u16) -> i16 {
        skill_get_splash(self.skill_id(), skill_lv) as i16
    }

    /// SkillImplRecursiveDamageSplash::getSplashTarget: BL_CHAR | BL_SKILL.
    fn get_splash_target(&self, c: &Ctx, src: &Unit) -> u16 {
        crate::map::BL_CHAR | crate::map::BL_SKILL
    }

    /// SkillImplRecursiveDamageSplash::splashSearch.
    fn splash_search(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
        splash_search_default(self, c, src, target, skill_lv, tick, flag);
    }

    /// SkillImplRecursiveDamageSplash::splashDamage: skill_attack of the skill's own type.
    fn splash_damage(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) -> i64 {
        let id = self.skill_id();
        crate::skill::attack::skill_attack(c, skill_get_type(id), src, src, target, id, skill_lv, tick, flag)
    }
}

/// The body of SkillImplRecursiveDamageSplash::splashSearch (for overrides that call the parent's).
pub fn splash_search_default<T: RecursiveDamageSplash + ?Sized>(this: &T, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
    use crate::battle::BCT_ENEMY;
    use crate::skill::area::{self, foreach_in_range, Wall};
    use crate::skill::{SD_LEVEL, SD_SPLASH};
    let id = this.skill_id();
    // if skill damage should be split among targets, count them
    // SD_LEVEL -> Forced splash damage -> count targets
    if flag & SD_LEVEL != 0 || skill_get_nk(id, &[NK_SPLASHSPLIT]) {
        let n = foreach_in_range(c, target, this.get_search_size(c, src, skill_lv), crate::map::BL_CHAR, Wall::None, |c, bl| {
            area::skill_area_sub(c, bl, src, id, skill_lv, tick, BCT_ENEMY, area::skill_area_sub_count)
        });
        // If there are no characters in the area, then it always counts as if there was one target
        // This happens when targetting skill units such as icewall
        area::set_temp(0, n.max(1));
    }
    // recursive invocation of skill_castend_damage_id() with flag|1
    let kinds = this.get_splash_target(c, src);
    foreach_in_range(c, target, this.get_splash_search_size(c, src, skill_lv), kinds, Wall::Config, |c, bl| {
        area::skill_area_sub(c, bl, src, id, skill_lv, tick, flag | BCT_ENEMY | SD_SPLASH | 1, crate::skill::castend::skill_castend_damage_id)
    });
}

/// SkillImplRecursiveDamageSplash::castendDamageId.
pub fn splash_castend_damage_id<T: RecursiveDamageSplash + ?Sized>(this: &T, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
    use crate::skill::area;
    use crate::skill::{SD_ANIMATION, SD_LEVEL};
    let id = this.skill_id();
    if *flag & 1 != 0 {
        // Recursive invocation
        let mut sflag = area::temp(0) & 0xFFF;
        let Some(skill) = skill_db().find(id) else { return };
        // status_get_sc(target) && getSCE(SC_HOVERING) && INF2_IGNOREHOVERING
        if crate::battle::mobif::has_sc(target.kind) && crate::status::sc::has_sce(c, target.id, crate::status::consts::SC_HOVERING) && skill.has_inf2(INF2_IGNOREHOVERING) {
            return; // Under Hovering characters are immune to select trap and ground target skills.
        }
        if *flag & SD_LEVEL != 0 {
            sflag |= SD_LEVEL; // -1 will be used in packets instead of the skill level
        }
        if area::temp(1) != target.id as i32 && !skill.has_inf2(INF2_ISNPC) {
            sflag |= SD_ANIMATION; // original target gets no animation (as well as all NPC skills)
        }
        this.splash_damage(c, src, target, skill_lv, tick, sflag);
    } else {
        area::set_temp(0, 0);
        area::set_temp(1, target.id as i32);
        area::set_temp(2, 0);
        this.splash_search(c, src, target, skill_lv, tick, *flag);
    }
}

/// SkillImplRecursiveDamageSplash::castendPos2.
pub fn splash_castend_pos2<T: RecursiveDamageSplash + ?Sized>(this: &T, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
    use crate::battle::BCT_ENEMY;
    use crate::skill::area::{self, foreach_in_area, Wall};
    // Cast center might be relevant later (e.g. for knockback direction)
    area::set_temp(4, x as i32);
    area::set_temp(5, y as i32);
    let size = this.get_splash_search_size(c, src, skill_lv);
    let id = this.skill_id();
    let kinds = this.get_splash_target(c, src);
    let f = *flag | BCT_ENEMY | 1;
    foreach_in_area(c, src.map, x - size, y - size, x + size, y + size, kinds, Wall::Config, |c, bl| {
        area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, crate::skill::castend::skill_castend_damage_id)
    });
}

/// The two SkillImpl methods a SkillImplRecursiveDamageSplash subclass inherits (castendDamageId, castendPos2): put
/// `crate::splash_skill_defaults!();` inside its `impl SkillImpl for …` block when it does not override them.
#[macro_export]
macro_rules! splash_skill_defaults {
    () => {
        fn castend_damage_id(&self, c: &$crate::world::Ctx, src: &$crate::unit::Unit, target: &$crate::unit::Unit, skill_lv: u16, tick: $crate::world::Tick, flag: &mut i32) {
            $crate::skills::splash_castend_damage_id(self, c, src, target, skill_lv, tick, flag);
        }
        fn castend_pos2(&self, c: &$crate::world::Ctx, src: &$crate::unit::Unit, x: i16, y: i16, skill_lv: u16, tick: $crate::world::Tick, flag: &mut i32) {
            $crate::skills::splash_castend_pos2(self, c, src, x, y, skill_lv, tick, flag);
        }
    };
}

/// The generic SkillImplRecursiveDamageSplash (a factory that returns the base class itself).
pub struct SkillImplRecursiveDamageSplash {
    pub id: u16,
}

impl SkillImpl for SkillImplRecursiveDamageSplash {
    fn skill_id(&self) -> u16 {
        self.id
    }
    crate::splash_skill_defaults!();
}

impl RecursiveDamageSplash for SkillImplRecursiveDamageSplash {}

// ---------------------------------------------------------------------------------------------- factory / registry

/// One family's `SkillFactory<Family>::create`.
pub type FamilyCreate = fn(u16) -> Option<Box<dyn SkillImpl>>;

/// skill_factory.cpp SkillFactoryImpl::create's `factories` list, in its order (custom first, to allow overwriting).
pub const FACTORIES: &[(&str, FamilyCreate)] = &[
    ("custom", custom::create),
    ("acolyte", acolyte::create),
    ("archer", archer::create),
    ("elemental", elemental::create),
    ("gunslinger", gunslinger::create),
    ("homunculus", homunculus::create),
    ("mage", mage::create),
    ("mercenary", mercenary::create),
    ("merchant", merchant::create),
    ("ninja", ninja::create),
    ("npc", npc::create),
    ("novice", novice::create),
    ("other", other::create),
    ("summoner", summoner::create),
    ("swordman", swordman::create),
    ("taekwon", taekwon::create),
    ("thief", thief::create),
];

/// SkillFactoryImpl::create: the first family that knows the skill.
pub fn create(skill_id: u16) -> Option<Box<dyn SkillImpl>> {
    FACTORIES.iter().find_map(|(_, f)| f(skill_id))
}

struct Registry(std::collections::HashMap<u16, Box<dyn SkillImpl>>);

fn registry() -> &'static Registry {
    static CELL: std::sync::OnceLock<Registry> = std::sync::OnceLock::new();
    CELL.get_or_init(|| {
        // SkillDatabase::loadingFinished: an impl for every skill of the database the factories know
        let mut m = std::collections::HashMap::new();
        for s in skill_db().iter() {
            if let Some(imp) = create(s.nameid) {
                m.insert(s.nameid, imp);
            }
        }
        Registry(m)
    })
}

/// `skill_db.find(skill_id)->impl` (None: no skill, or no class for it yet).
pub fn skill_impl(skill_id: u16) -> Option<&'static dyn SkillImpl> {
    registry().0.get(&skill_id).map(|b| b.as_ref())
}

/// The family whose `create` answers for the skill (diagnostics for the pipeline's "no impl" warnings).
pub fn family_of(skill_id: u16) -> Option<&'static str> {
    FACTORIES.iter().find(|(_, f)| f(skill_id).is_some()).map(|(n, _)| *n)
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Every class reports the skill id its factory created it for, and no two families claim the same skill.
    #[test]
    fn factories_are_consistent() {
        for s in skill_db().iter() {
            let mut owners = Vec::new();
            for (name, f) in FACTORIES {
                if let Some(imp) = f(s.nameid) {
                    assert_eq!(imp.skill_id(), s.nameid, "{name} creates a class with another skill id for {}", s.name);
                    owners.push(*name);
                }
            }
            assert!(owners.len() <= 1, "skill {} is created by {owners:?}", s.name);
        }
    }
}
