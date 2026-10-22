//! skills/elemental/skill_factory_elemental.cpp: SkillFactoryElemental::create and the elemental family's classes
//! (the Sorcerer's spirits and their Expanded forms; rAthena e985006), one file per rAthena file: firebomb.rs,
//! firewave.rs, hurricanerage.rs, rocklauncher.rs, typhoonmissile.rs and waterscrew.rs hold two classes each (the
//! skill and its _ATK hit), as their .cpp files do.
//!
//! Also here, for the classes: the body the "option" classes repeat in each of their .cpp files
//! ([`option_castend_nodamage_id`]) and the Expanded elementals' ratio tail ([`add_master_level_ratio`]).

use super::SkillImpl;
use crate::battle::DMG_SINGLE;
use crate::db::skills::*;
use crate::map::BL_ELEM;
use crate::skill::clif::{clif_skill_damage, clif_skill_nodamage, DMGVAL_IGNORE};
use crate::skills::novice::{sce, status_get_amotion};
use crate::status::sc;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub mod ageofice;
pub mod aquaplay;
pub mod avalanche;
pub mod blast;
pub mod circleoffire;
pub mod coldforce;
pub mod coolair;
pub mod cooler;
pub mod crystalarmor;
pub mod cursedsoil;
pub mod deadlypoison;
pub mod deeppoisoning;
pub mod earthcare;
pub mod eyesofstorm;
pub mod firearrow;
pub mod firebomb;
pub mod firecloak;
pub mod firemantle;
pub mod firewave;
pub mod flamearmor;
pub mod flamerock;
pub mod flametechnic;
pub mod gracebreeze;
pub mod gust;
pub mod heater;
pub mod hurricanerage;
pub mod iceneedle;
pub mod petrology;
pub mod poisonshield;
pub mod powerofgaia;
pub mod pyrotechnic;
pub mod rocklauncher;
pub mod solidskin;
pub mod stonehammer;
pub mod stonerain;
pub mod stoneshield;
pub mod stormwind;
pub mod strongprotection;
pub mod tidalweapon;
pub mod tropic;
pub mod typhoonmissile;
pub mod upheaval;
pub mod waterbarrier;
pub mod waterdrop;
pub mod waterscreen;
pub mod waterscrew;
pub mod wildstorm;
pub mod windcurtain;
pub mod windslasher;
pub mod windstep;
pub mod zephyr;

/// SkillFactoryElemental::create.
pub fn create(skill_id: u16) -> Option<Box<dyn SkillImpl>> {
    match skill_id {
        EL_AQUAPLAY => Some(Box::new(aquaplay::SkillAquaPlay)),
        EL_BLAST => Some(Box::new(blast::SkillBlast)),
        EL_CHILLY_AIR => Some(Box::new(coolair::SkillCoolAir)),
        EL_CIRCLE_OF_FIRE => Some(Box::new(circleoffire::SkillCircleOfFire)),
        EL_COOLER => Some(Box::new(cooler::SkillCooler)),
        EL_CURSED_SOIL => Some(Box::new(cursedsoil::SkillCursedSoil)),
        EL_FIRE_ARROW => Some(Box::new(firearrow::SkillFireArrow)),
        EL_FIRE_BOMB => Some(Box::new(firebomb::SkillFireBomb)),
        EL_FIRE_BOMB_ATK => Some(Box::new(firebomb::SkillFireBombAttack)),
        EL_FIRE_CLOAK => Some(Box::new(firecloak::SkillFireCloak)),
        EL_FIRE_MANTLE => Some(Box::new(firemantle::SkillFireMantle)),
        EL_FIRE_WAVE => Some(Box::new(firewave::SkillFireWave)),
        EL_FIRE_WAVE_ATK => Some(Box::new(firewave::SkillFireWaveAttack)),
        EL_GUST => Some(Box::new(gust::SkillGust)),
        EL_HEATER => Some(Box::new(heater::SkillHeater)),
        EL_HURRICANE => Some(Box::new(hurricanerage::SkillHurricaneRage)),
        EL_HURRICANE_ATK => Some(Box::new(hurricanerage::SkillHurricaneRageAttack)),
        EL_ICE_NEEDLE => Some(Box::new(iceneedle::SkillIceNeedle)),
        EL_PETROLOGY => Some(Box::new(petrology::SkillPetrology)),
        EL_POWER_OF_GAIA => Some(Box::new(powerofgaia::SkillPowerOfGaia)),
        EL_PYROTECHNIC => Some(Box::new(pyrotechnic::SkillPyrotechnic)),
        EL_ROCK_CRUSHER => Some(Box::new(rocklauncher::SkillRockLauncher)),
        EL_ROCK_CRUSHER_ATK => Some(Box::new(rocklauncher::SkillRockLauncherAttack)),
        EL_SOLID_SKIN => Some(Box::new(solidskin::SkillSolidSkin)),
        EL_STONE_HAMMER => Some(Box::new(stonehammer::SkillStoneHammer)),
        EL_STONE_RAIN => Some(Box::new(stonerain::SkillStoneRain)),
        EL_STONE_SHIELD => Some(Box::new(stoneshield::SkillStoneShield)),
        EL_TIDAL_WEAPON => Some(Box::new(tidalweapon::SkillTidalWeapon)),
        EL_TROPIC => Some(Box::new(tropic::SkillTropic)),
        EL_TYPOON_MIS => Some(Box::new(typhoonmissile::SkillTyphoonMissile)),
        EL_TYPOON_MIS_ATK => Some(Box::new(typhoonmissile::SkillTyphoonMissileAttack)),
        EL_UPHEAVAL => Some(Box::new(upheaval::SkillUpheaval)),
        EL_WATER_BARRIER => Some(Box::new(waterbarrier::SkillWaterBarrier)),
        EL_WATER_DROP => Some(Box::new(waterdrop::SkillWaterDrop)),
        EL_WATER_SCREEN => Some(Box::new(waterscreen::SkillWaterScreen)),
        EL_WATER_SCREW => Some(Box::new(waterscrew::SkillWaterScrew)),
        EL_WATER_SCREW_ATK => Some(Box::new(waterscrew::SkillWaterScrewAttack)),
        EL_WILD_STORM => Some(Box::new(wildstorm::SkillWildStorm)),
        EL_WIND_CURTAIN => Some(Box::new(windcurtain::SkillWindCurtain)),
        EL_WIND_SLASH => Some(Box::new(windslasher::SkillWindSlasher)),
        EL_WIND_STEP => Some(Box::new(windstep::SkillWindStep)),
        EL_ZEPHYR => Some(Box::new(zephyr::SkillZephyr)),
        EM_EL_AGE_OF_ICE => Some(Box::new(ageofice::SkillAgeOfIce)),
        EM_EL_AVALANCHE => Some(Box::new(avalanche::SkillAvalanche)),
        EM_EL_COLD_FORCE => Some(Box::new(coldforce::SkillColdForce)),
        EM_EL_CRYSTAL_ARMOR => Some(Box::new(crystalarmor::SkillCrystalArmor)),
        EM_EL_DEADLY_POISON => Some(Box::new(deadlypoison::SkillDeadlyPoison)),
        EM_EL_DEEP_POISONING => Some(Box::new(deeppoisoning::SkillDeepPoisoning)),
        EM_EL_EARTH_CARE => Some(Box::new(earthcare::SkillEarthCare)),
        EM_EL_EYES_OF_STORM => Some(Box::new(eyesofstorm::SkillEyesOfStorm)),
        EM_EL_FLAMEARMOR => Some(Box::new(flamearmor::SkillFlameArmor)),
        EM_EL_FLAMEROCK => Some(Box::new(flamerock::SkillFlameRock)),
        EM_EL_FLAMETECHNIC => Some(Box::new(flametechnic::SkillFlameTechnic)),
        EM_EL_GRACE_BREEZE => Some(Box::new(gracebreeze::SkillGraceBreeze)),
        EM_EL_POISON_SHIELD => Some(Box::new(poisonshield::SkillPoisonShield)),
        EM_EL_STORM_WIND => Some(Box::new(stormwind::SkillStormWind)),
        EM_EL_STRONG_PROTECTION => Some(Box::new(strongprotection::SkillStrongProtection)),
        _ => None,
    }
}

/// The Expanded elementals' calculateSkillRatio tail (ageofice.cpp, avalanche.cpp, deadlypoison.cpp, flamerock.cpp,
/// stormwind.cpp): `const s_elemental_data* ed = BL_CAST(BL_ELEM, src); if (ed) base_skillratio += base_skillratio *
/// status_get_lv(ed->master) / 100;` (int32 arithmetic; status_get_lv answers 0 through its nullpo_ret without a
/// master unit).
pub(crate) fn add_master_level_ratio(c: &Ctx, src: &Unit, base_skillratio: &mut i32) {
    if src.kind != BL_ELEM {
        return;
    }
    let master = crate::elemental::data::get(c, src.id).map(|ed| crate::elemental::master_of(c, &ed)).or_else(|| crate::companion::master_of(c, src.id));
    let lv = master.and_then(|m| unit::get(c, m)).map(|m| crate::status::status_get_lv(c, &m)).unwrap_or(0);
    *base_skillratio += *base_skillratio * lv / 100;
}

/// Where the option classes send their dummy damage packet (`clif_skill_damage(*src, *X, tick,
/// status_get_amotion(src), 0, DMGVAL_IGNORE, 1, skill_id, skill_lv, DMG_SINGLE)`), which is the only difference
/// between their .cpp files.
#[derive(Clone, Copy, PartialEq, Eq)]
pub(crate) enum OptionAnim {
    /// `clif_skill_damage(*src, *target, …)`: aquaplay, circleoffire, coolair, cooler, cursedsoil, firecloak,
    /// heater, petrology, pyrotechnic, solidskin, stoneshield, tropic, upheaval, waterdrop, windcurtain.
    Target,
    /// `clif_skill_damage(*src, *src, …)`: blast, gust, wildstorm.
    Caster,
    /// No dummy damage packet: the Expanded elementals' coldforce, crystalarmor, deeppoisoning, earthcare,
    /// eyesofstorm, flamearmor, flametechnic, gracebreeze, poisonshield, strongprotection.
    None,
}

/// The castendNoDamageId each elemental "option" class writes out in its own .cpp (SkillAquaPlay … SkillWindCurtain,
/// see [`OptionAnim`]; windstep.rs and waterscreen.rs have their own variants): cast by an elemental
/// (`BL_CAST(BL_ELEM, src)`, nothing otherwise), `type` = skill_get_sc(skill) and `type2` = the status just before it
/// (`type - 1`, the elemental's own half). When the elemental has `type2` or the target has `type` running, rAthena
/// ends `type` on the elemental and `type2` on the target (crossed as written there; reproduced); otherwise the
/// skill animation on the elemental, the dummy damage packet, then `type2` on the elemental and `type` on the
/// target, both at 100% with val1 = skill_lv for skill_get_time.
#[allow(clippy::too_many_arguments)]
pub(crate) fn option_castend_nodamage_id(c: &Ctx, skill_id: u16, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, anim: OptionAnim) {
    let type_ = skill_get_sc(skill_id) as i32;
    if src.kind != BL_ELEM {
        return;
    }
    let type2 = type_ - 1;
    // (esc && esc->getSCE(type2)) || (tsc && tsc->getSCE(type))
    if sce(c, src, type2).is_some() || sce(c, target, type_).is_some() {
        sc::status_change_end(c, src.id, type_, 0);
        sc::status_change_end(c, target.id, type2, 0);
    } else {
        clif_skill_nodamage(c, Some(src), src, skill_id, skill_lv as i32, true);
        match anim {
            OptionAnim::Target => clif_skill_damage(c, src, target, tick, status_get_amotion(c, src), 0, DMGVAL_IGNORE, 1, skill_id, skill_lv as i32, DMG_SINGLE),
            OptionAnim::Caster => clif_skill_damage(c, src, src, tick, status_get_amotion(c, src), 0, DMGVAL_IGNORE, 1, skill_id, skill_lv as i32, DMG_SINGLE),
            OptionAnim::None => {}
        }
        let dur = skill_get_time(skill_id, skill_lv) as i64;
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        sc::sc_start(c, Some(&s), &s, type2, 100, skill_lv as i32, dur);
        let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        sc::sc_start(c, Some(&s), &t, type_, 100, skill_lv as i32, dur);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::status::consts::*;

    /// Every arm of skill_factory_elemental.cpp's switch (57) has its class here, reporting its own id, and the
    /// registry finds it in this family.
    #[test]
    fn factory_covers_every_arm() {
        let ids = [
            EL_AQUAPLAY, EL_BLAST, EL_CHILLY_AIR, EL_CIRCLE_OF_FIRE, EL_COOLER, EL_CURSED_SOIL, EL_FIRE_ARROW, EL_FIRE_BOMB, EL_FIRE_BOMB_ATK, EL_FIRE_CLOAK, EL_FIRE_MANTLE, EL_FIRE_WAVE,
            EL_FIRE_WAVE_ATK, EL_GUST, EL_HEATER, EL_HURRICANE, EL_HURRICANE_ATK, EL_ICE_NEEDLE, EL_PETROLOGY, EL_POWER_OF_GAIA, EL_PYROTECHNIC, EL_ROCK_CRUSHER, EL_ROCK_CRUSHER_ATK,
            EL_SOLID_SKIN, EL_STONE_HAMMER, EL_STONE_RAIN, EL_STONE_SHIELD, EL_TIDAL_WEAPON, EL_TROPIC, EL_TYPOON_MIS, EL_TYPOON_MIS_ATK, EL_UPHEAVAL, EL_WATER_BARRIER, EL_WATER_DROP,
            EL_WATER_SCREEN, EL_WATER_SCREW, EL_WATER_SCREW_ATK, EL_WILD_STORM, EL_WIND_CURTAIN, EL_WIND_SLASH, EL_WIND_STEP, EL_ZEPHYR, EM_EL_AGE_OF_ICE, EM_EL_AVALANCHE, EM_EL_COLD_FORCE,
            EM_EL_CRYSTAL_ARMOR, EM_EL_DEADLY_POISON, EM_EL_DEEP_POISONING, EM_EL_EARTH_CARE, EM_EL_EYES_OF_STORM, EM_EL_FLAMEARMOR, EM_EL_FLAMEROCK, EM_EL_FLAMETECHNIC, EM_EL_GRACE_BREEZE,
            EM_EL_POISON_SHIELD, EM_EL_STORM_WIND, EM_EL_STRONG_PROTECTION,
        ];
        assert_eq!(ids.len(), 57);
        for id in ids {
            let imp = create(id).unwrap_or_else(|| panic!("no class for skill {id}"));
            assert_eq!(imp.skill_id(), id);
            assert_eq!(crate::skills::family_of(id), Some("elemental"), "skill {id}");
        }
    }

    /// The option classes' `type - 1` pairing: the option skills' status (skill_db `Status`) follows the elemental's
    /// own status in sc_type (status.hpp), as the classes rely on.
    #[test]
    fn option_status_pairs() {
        let pairs = [
            (EL_AQUAPLAY, SC_AQUAPLAY_OPTION, SC_AQUAPLAY),
            (EL_BLAST, SC_BLAST_OPTION, SC_BLAST),
            (EL_CIRCLE_OF_FIRE, SC_CIRCLE_OF_FIRE_OPTION, SC_CIRCLE_OF_FIRE),
            (EL_CHILLY_AIR, SC_CHILLY_AIR_OPTION, SC_CHILLY_AIR),
            (EL_WATER_SCREEN, SC_WATER_SCREEN_OPTION, SC_WATER_SCREEN),
            (EL_WIND_STEP, SC_WIND_STEP_OPTION, SC_WIND_STEP),
            (EM_EL_STRONG_PROTECTION, SC_STRONG_PROTECTION_OPTION, SC_STRONG_PROTECTION),
            (EM_EL_POISON_SHIELD, SC_POISON_SHIELD_OPTION, SC_POISON_SHIELD),
        ];
        for (id, opt, own) in pairs {
            assert_eq!(skill_get_sc(id) as i32, opt, "skill {id}");
            assert_eq!(opt - 1, own, "skill {id}");
        }
    }
}
