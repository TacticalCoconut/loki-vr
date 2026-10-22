//! skills/homunculus/skill_factory_homunculus.cpp: SkillFactoryHomunculus::create and the homunculus family's classes
//! (rAthena e985006), one file per class as rAthena has them (`homunculus_<name>.cpp` → `homunculus_<name>.rs`).
//! The family helpers below are the small homunculus.hpp / status.hpp macros the classes use (BL_CAST(BL_HOM, …),
//! status_get_str, the "Yommy formula" of the grappler combos, the ground skills' castendPos2).

use super::{SkillImpl, StatusSkillImpl};
use crate::db::skills::*;
use crate::homunculus::data::HomData;
use crate::map::BL_HOM;
use crate::unit::Unit;
use crate::world::Ctx;

pub mod homunculus_absolutezephyr;
pub mod homunculus_avoid;
pub mod homunculus_benedictionofchaos;
pub mod homunculus_bioexplosion;
pub mod homunculus_blastforge;
pub mod homunculus_blazingandfurious;
pub mod homunculus_caprice;
pub mod homunculus_castling;
pub mod homunculus_change;
pub mod homunculus_continualbreakcombo;
pub mod homunculus_defense;
pub mod homunculus_erasercutter;
pub mod homunculus_eternalquickcombo;
pub mod homunculus_glanzenspies;
pub mod homunculus_goldenetone;
pub mod homunculus_graniticarmor;
pub mod homunculus_healingtouch;
pub mod homunculus_heiligepferd;
pub mod homunculus_holypole;
pub mod homunculus_lavaslide;
pub mod homunculus_lightofregene;
pub mod homunculus_magmaflow;
pub mod homunculus_midnightfrenzy;
pub mod homunculus_moonlight;
pub mod homunculus_needleofparalyze;
pub mod homunculus_needlestinger;
pub mod homunculus_overedboost;
pub mod homunculus_painkiller;
pub mod homunculus_poisonmist;
pub mod homunculus_pyroclastic;
pub mod homunculus_sbr44;
pub mod homunculus_silentbreeze;
pub mod homunculus_silverveinrush;
pub mod homunculus_sonicclaw;
pub mod homunculus_steelhorn;
pub mod homunculus_stonewall;
pub mod homunculus_stylechange;
pub mod homunculus_summonlegion;
pub mod homunculus_tempering;
pub mod homunculus_theonefighterrises;
pub mod homunculus_tinderbreaker;
pub mod homunculus_toxinofmandara;
pub mod homunculus_twistercutter;
pub mod homunculus_volcanicash;
pub mod homunculus_xenoslasher;

/// SkillFactoryHomunculus::create, in rAthena's order.
pub fn create(skill_id: u16) -> Option<Box<dyn SkillImpl>> {
    // std::make_unique<StatusSkillImpl>(skill_id)
    let status = || -> Option<Box<dyn SkillImpl>> { Some(Box::new(StatusSkillImpl::new(skill_id))) };
    match skill_id {
        HAMI_BLOODLUST => status(),
        HAMI_CASTLE => Some(Box::new(homunculus_castling::SkillCastling)),
        HAMI_DEFENCE => Some(Box::new(homunculus_defense::SkillDefense)),
        HFLI_FLEET => status(),
        HFLI_MOON => Some(Box::new(homunculus_moonlight::SkillMoonlight)),
        HFLI_SBR44 => Some(Box::new(homunculus_sbr44::SkillSBR44)),
        HFLI_SPEED => status(),
        HLIF_AVOID => Some(Box::new(homunculus_avoid::SkillAvoid)),
        HLIF_CHANGE => Some(Box::new(homunculus_change::SkillChange)),
        HLIF_HEAL => Some(Box::new(homunculus_healingtouch::SkillHealingTouch)),
        HVAN_CAPRICE => Some(Box::new(homunculus_caprice::SkillCaprice)),
        HVAN_CHAOTIC => Some(Box::new(homunculus_benedictionofchaos::SkillBenedictionOfChaos)),
        HVAN_EXPLOSION => Some(Box::new(homunculus_bioexplosion::SkillBioExplosion)),
        MH_ABSOLUTE_ZEPHYR => Some(Box::new(homunculus_absolutezephyr::SkillAbsoluteZephyr)),
        MH_ANGRIFFS_MODUS => status(),
        MH_BLAST_FORGE => Some(Box::new(homunculus_blastforge::SkillBlastForge)),
        MH_BLAZING_AND_FURIOUS => Some(Box::new(homunculus_blazingandfurious::SkillBlazingAndFurious)),
        MH_CBC => Some(Box::new(homunculus_continualbreakcombo::SkillContinualBreakCombo)),
        MH_EQC => Some(Box::new(homunculus_eternalquickcombo::SkillEternalQuickCombo)),
        MH_ERASER_CUTTER => Some(Box::new(homunculus_erasercutter::SkillEraserCutter)),
        MH_GLANZEN_SPIES => Some(Box::new(homunculus_glanzenspies::SkillGlanzenSpies)),
        MH_GOLDENE_FERSE => status(),
        MH_GOLDENE_TONE => Some(Box::new(homunculus_goldenetone::SkillGoldeneTone)),
        MH_GRANITIC_ARMOR => Some(Box::new(homunculus_graniticarmor::SkillGraniticArmor)),
        MH_HEILIGE_PFERD => Some(Box::new(homunculus_heiligepferd::SkillHeiligePferd)),
        MH_HEILIGE_STANGE => Some(Box::new(homunculus_holypole::SkillHolyPole)),
        MH_LAVA_SLIDE => Some(Box::new(homunculus_lavaslide::SkillLavaSlide)),
        MH_LIGHT_OF_REGENE => Some(Box::new(homunculus_lightofregene::SkillLightOfRegene)),
        MH_MAGMA_FLOW => Some(Box::new(homunculus_magmaflow::SkillMagmaFlow)),
        MH_MIDNIGHT_FRENZY => Some(Box::new(homunculus_midnightfrenzy::SkillMidnightFrenzy)),
        MH_NEEDLE_OF_PARALYZE => Some(Box::new(homunculus_needleofparalyze::SkillNeedleOfParalyze)),
        MH_NEEDLE_STINGER => Some(Box::new(homunculus_needlestinger::SkillNeedleStinger)),
        MH_OVERED_BOOST => Some(Box::new(homunculus_overedboost::SkillOveredBoost)),
        MH_PAIN_KILLER => Some(Box::new(homunculus_painkiller::SkillPainKiller)),
        MH_POISON_MIST => Some(Box::new(homunculus_poisonmist::SkillPoisonMist)),
        MH_PYROCLASTIC => Some(Box::new(homunculus_pyroclastic::SkillPyroclastic)),
        MH_SILENT_BREEZE => Some(Box::new(homunculus_silentbreeze::SkillSilentBreeze)),
        MH_SILVERVEIN_RUSH => Some(Box::new(homunculus_silverveinrush::SkillSilverVeinRush)),
        MH_SONIC_CRAW => Some(Box::new(homunculus_sonicclaw::SkillSonicClaw)),
        MH_STAHL_HORN => Some(Box::new(homunculus_steelhorn::SkillSteelHorn)),
        MH_STEINWAND => Some(Box::new(homunculus_stonewall::SkillStoneWall)),
        MH_STYLE_CHANGE => Some(Box::new(homunculus_stylechange::SkillStyleChange)),
        MH_SUMMON_LEGION => Some(Box::new(homunculus_summonlegion::SkillSummonLegion)),
        MH_TEMPERING => Some(Box::new(homunculus_tempering::SkillTempering)),
        MH_THE_ONE_FIGHTER_RISES => Some(Box::new(homunculus_theonefighterrises::SkillTheOneFighterRises)),
        MH_TINDER_BREAKER => Some(Box::new(homunculus_tinderbreaker::SkillTinderBreaker)),
        MH_TOXIN_OF_MANDARA => Some(Box::new(homunculus_toxinofmandara::SkillToxinOfMandara)),
        MH_TWISTER_CUTTER => Some(Box::new(homunculus_twistercutter::SkillTwisterCutter)),
        MH_VOLCANIC_ASH => Some(Box::new(homunculus_volcanicash::SkillVolcanicAsh)),
        MH_XENO_SLASHER => Some(Box::new(homunculus_xenoslasher::SkillXenoSlasher)),
        _ => None,
    }
}

// ---------------------------------------------------------------------------------------------- family helpers

/// `BL_CAST(BL_HOM, bl)`: the homunculus row of a BL_HOM unit (None for any other unit, rAthena's nullptr).
pub(crate) fn hd_of(c: &Ctx, bl: &Unit) -> Option<HomData> {
    if bl.kind != BL_HOM {
        return None;
    }
    crate::homunculus::data::get(c, bl.id)
}

/// The unit as it is now (a status change or a move saved its row since the pipeline read it).
pub(crate) fn fresh(c: &Ctx, bl: &Unit) -> Unit {
    crate::unit::get(c, bl.id).unwrap_or_else(|| bl.clone())
}

/// status.hpp `status_get_str(bl)`: status_get_status_data(bl)->str.
pub(crate) fn status_get_str(c: &Ctx, bl: &Unit) -> i32 {
    crate::status::status_get_status_data(c, bl).str as i32
}

/// The "Yommy formula" of MH_TINDER_BREAKER / MH_CBC / MH_EQC:
/// `max(skill_lv, (status_get_str(src) / 7 - status_get_str(target) / 10)) * 1000` (cbasetypes.hpp's int32 max).
pub(crate) fn yommy_duration(c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16) -> i32 {
    yommy(skill_lv, status_get_str(c, src), status_get_str(c, target))
}

fn yommy(skill_lv: u16, src_str: i32, target_str: i32) -> i32 {
    (skill_lv as i32).max(src_str / 7 - target_str / 10) * 1000
}

/// The castendPos2 of the homunculus ground skills (MH_BLAST_FORGE, MH_LAVA_SLIDE, MH_POISON_MIST, MH_STEINWAND,
/// MH_VOLCANIC_ASH, MH_XENO_SLASHER): `flag |= 1` and `skill_unitsetting(src, getSkillId(), skill_lv, x, y, 0)`.
pub(crate) fn unit_castend_pos2(c: &Ctx, skill_id: u16, src: &Unit, x: i16, y: i16, skill_lv: u16, flag: &mut i32) {
    // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).
    *flag |= 1;
    // Ammo should be deleted right away.
    crate::skill::iface::skill_unitsetting(c, src, skill_id, skill_lv, x, y, 0);
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Every skill SkillFactoryHomunculus::create handles (skill_factory_homunculus.cpp's case labels, in its order).
    const RATHENA_FACTORY: &[u16] = &[
        HAMI_BLOODLUST,
        HAMI_CASTLE,
        HAMI_DEFENCE,
        HFLI_FLEET,
        HFLI_MOON,
        HFLI_SBR44,
        HFLI_SPEED,
        HLIF_AVOID,
        HLIF_CHANGE,
        HLIF_HEAL,
        HVAN_CAPRICE,
        HVAN_CHAOTIC,
        HVAN_EXPLOSION,
        MH_ABSOLUTE_ZEPHYR,
        MH_ANGRIFFS_MODUS,
        MH_BLAST_FORGE,
        MH_BLAZING_AND_FURIOUS,
        MH_CBC,
        MH_EQC,
        MH_ERASER_CUTTER,
        MH_GLANZEN_SPIES,
        MH_GOLDENE_FERSE,
        MH_GOLDENE_TONE,
        MH_GRANITIC_ARMOR,
        MH_HEILIGE_PFERD,
        MH_HEILIGE_STANGE,
        MH_LAVA_SLIDE,
        MH_LIGHT_OF_REGENE,
        MH_MAGMA_FLOW,
        MH_MIDNIGHT_FRENZY,
        MH_NEEDLE_OF_PARALYZE,
        MH_NEEDLE_STINGER,
        MH_OVERED_BOOST,
        MH_PAIN_KILLER,
        MH_POISON_MIST,
        MH_PYROCLASTIC,
        MH_SILENT_BREEZE,
        MH_SILVERVEIN_RUSH,
        MH_SONIC_CRAW,
        MH_STAHL_HORN,
        MH_STEINWAND,
        MH_STYLE_CHANGE,
        MH_SUMMON_LEGION,
        MH_TEMPERING,
        MH_THE_ONE_FIGHTER_RISES,
        MH_TINDER_BREAKER,
        MH_TOXIN_OF_MANDARA,
        MH_TWISTER_CUTTER,
        MH_VOLCANIC_ASH,
        MH_XENO_SLASHER,
    ];

    /// The factory answers a class (with the right skill id) for every skill rAthena's factory handles, and for no
    /// other skill of the database; no family before this one in skill_factory.cpp's order claims them.
    #[test]
    fn factory_matches_rathena() {
        for &id in RATHENA_FACTORY {
            let imp = create(id).unwrap_or_else(|| panic!("no homunculus class for skill {id}"));
            assert_eq!(imp.skill_id(), id);
            assert_eq!(crate::skills::family_of(id), Some("homunculus"), "skill {id} is claimed by another family first");
        }
        for s in skill_db().iter() {
            assert_eq!(create(s.nameid).is_some(), RATHENA_FACTORY.contains(&s.nameid), "skill {}", s.name);
        }
    }

    /// The monster ids MH_SUMMON_LEGION spawns, against the script constants (mob.hpp).
    #[test]
    fn legion_ids_match_the_script_constants() {
        use homunculus_summonlegion::{MOBID_S_GIANT_HORNET, MOBID_S_HORNET, MOBID_S_LUCIOLA_VESPA};
        for (name, v) in [("MOBID_S_HORNET", MOBID_S_HORNET), ("MOBID_S_GIANT_HORNET", MOBID_S_GIANT_HORNET), ("MOBID_S_LUCIOLA_VESPA", MOBID_S_LUCIOLA_VESPA)] {
            if let Some(k) = crate::data::constant(name) {
                assert_eq!(k, v as i64, "{name}");
            }
        }
        assert_eq!((MOBID_S_HORNET, MOBID_S_GIANT_HORNET, MOBID_S_LUCIOLA_VESPA), (2158, 2159, 2160));
    }

    /// The battle_config values the family reads, against the reference server's battle_config table.
    #[test]
    fn battle_config_matches_the_conf() {
        use crate::script::battle_flags::battle_get_value;
        assert_eq!(homunculus_healingtouch::heal_exp(), battle_get_value("heal_exp"));
        assert_eq!(crate::homunculus::hvan_explosion_intimate() as i32, battle_get_value("hvan_explosion_intimate"));
    }

    /// The Yommy formula (int32 max of the level and the STR difference).
    #[test]
    fn yommy_formula() {
        assert_eq!(yommy(3, 20, 50), 3000);
        assert_eq!(yommy(1, 140, 10), 19000);
        assert_eq!(yommy(5, 0, 300), 5000);
    }
}
