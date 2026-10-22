//! skills/merchant/fireexpansion.cpp: class SkillFireExpansion : public SkillImpl (GN_FIRE_EXPANSION); class
//! SkillFireExpansionAcid : public SkillImplRecursiveDamageSplash (GN_FIRE_EXPANSION_ACID).

use crate::battle::BCT_ENEMY;
use crate::db::skills::*;
use crate::inventory::{EQP_ARMOR, EQP_WEAPON};
use crate::map::{BL_CHAR, BL_PC};
use crate::skill::area::{self, foreach_in_area, Wall};
use crate::skill::castend::skill_castend_damage_id;
use crate::skill::effect::skill_break_equip;
use crate::skill::SD_LEVEL;
use crate::skills::novice::pc_checkskill;
use crate::skills::{RecursiveDamageSplash, SkillImpl};
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// SkillFireExpansion::SkillFireExpansion() : SkillImpl(GN_FIRE_EXPANSION).
pub struct SkillFireExpansion;

impl SkillImpl for SkillFireExpansion {
    fn skill_id(&self) -> u16 {
        GN_FIRE_EXPANSION
    }

    /// SkillFireExpansion::castendPos2: the first of the caster's unit groups (ud->skillunits order) that is a
    /// Demonic Fire whose first unit is less than 4 cells from the target cell changes by level: 1 a new Demonic Fire
    /// at the cell with the old one's remaining time, 2 Demonic Fire hits at level + 20 around it, 3 smoke powder,
    /// 4 tear gas, 5 Acid Demonstration hits (level 5, or the caster's higher level) around it; the old unit is
    /// deleted each time.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        use crate::skillunit::data;
        // struct unit_data* ud = unit_bl2ud(src); if (!ud) return;
        if !crate::skillunit::group::has_ud(src.kind) {
            return;
        }
        // std::find_if(ud->skillunits, sg->skill_id == GN_DEMONIC_FIRE && distance_xy(x, y, su->x, su->y) < 4) with
        // su = sg->unit (the group's first unit)
        let first_unit = |g: &data::SkillUnitGroup| data::units_of(c, g.group_id).into_iter().find(|u| u.idx == 0);
        let found = data::groups_of(c, src.id).into_iter().find_map(|g| {
            if g.skill_id != GN_DEMONIC_FIRE {
                return None;
            }
            let su = first_unit(&g)?;
            (crate::path::distance(x as i32 - su.bl.x as i32, y as i32 - su.bl.y as i32) < 4).then_some((g, su))
        });
        let Some((unit_group, su)) = found else { return };
        let (sux, suy) = (su.bl.x, su.bl.y);
        match skill_lv {
            1 => {
                // TODO: (rAthena's)
                let duration = (unit_group.limit - (tick - unit_group.tick)) as i32;
                crate::skill::iface::skill_delunit(c, su.id());
                crate::skill::iface::skill_unitsetting(c, src, GN_DEMONIC_FIRE, 1, x, y, duration);
                *flag |= 1;
            }
            2 => {
                let f = *flag | BCT_ENEMY | SD_LEVEL | 1;
                foreach_in_area(c, src.map, sux - 2, suy - 2, sux + 2, suy + 2, BL_CHAR, Wall::None, |c, bl| area::skill_area_sub(c, bl, src, GN_DEMONIC_FIRE, skill_lv + 20, tick, f, skill_castend_damage_id));
                crate::skill::iface::skill_delunit(c, su.id());
            }
            3 => {
                crate::skill::iface::skill_delunit(c, su.id());
                crate::skill::iface::skill_unitsetting(c, src, GN_FIRE_EXPANSION_SMOKE_POWDER, 1, x, y, 0);
                *flag |= 1;
            }
            4 => {
                crate::skill::iface::skill_delunit(c, su.id());
                crate::skill::iface::skill_unitsetting(c, src, GN_FIRE_EXPANSION_TEAR_GAS, 1, x, y, 0);
                *flag |= 1;
            }
            5 => {
                // Cast at Acid Demonstration at level 5 unless the user has a higher level learned.
                let mut acid_lv: u16 = 5;
                if src.kind == BL_PC && pc_checkskill(c, src, CR_ACIDDEMONSTRATION) > 5 {
                    acid_lv = pc_checkskill(c, src, CR_ACIDDEMONSTRATION) as u16;
                }
                let f = *flag | BCT_ENEMY | SD_LEVEL | 1;
                foreach_in_area(c, src.map, sux - 2, suy - 2, sux + 2, suy + 2, BL_CHAR, Wall::None, |c, bl| area::skill_area_sub(c, bl, src, GN_FIRE_EXPANSION_ACID, acid_lv, tick, f, skill_castend_damage_id));
                crate::skill::iface::skill_delunit(c, su.id());
            }
            _ => {}
        }
    }
}

/// SkillFireExpansionAcid::SkillFireExpansionAcid() : SkillImplRecursiveDamageSplash(GN_FIRE_EXPANSION_ACID).
pub struct SkillFireExpansionAcid;

impl SkillImpl for SkillFireExpansionAcid {
    fn skill_id(&self) -> u16 {
        GN_FIRE_EXPANSION_ACID
    }
    crate::splash_skill_defaults!();

    /// SkillFireExpansionAcid::applyAdditionalEffects: weapon and armor break, 1% per level (rate x100).
    fn apply_additional_effects(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _attack_type: i32, _dmg_lv: u8) {
        skill_break_equip(c, src, target, (EQP_WEAPON | EQP_ARMOR) as u16, 100 * skill_lv as i32, BCT_ENEMY);
    }
}

impl RecursiveDamageSplash for SkillFireExpansionAcid {}
