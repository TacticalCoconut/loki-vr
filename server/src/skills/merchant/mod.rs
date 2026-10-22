//! skills/merchant/skill_factory_merchant.cpp: SkillFactoryMerchant::create and the merchant family's classes (rAthena
//! e985006): Merchant, Blacksmith / Whitesmith / Mechanic / Meister, Alchemist / Creator / Genetic / Biolo. One file per
//! class file as rAthena has them (acidifiedzone*.rs, crazyweed.rs, fireexpansion.rs, hellsplant.rs, magmaeruption.rs and
//! slingitem.rs hold two classes each, as their .cpp files do). MC_VENDING's SkillVending is the vending agent's
//! (skill_vending.rs, wave D).

use super::{SkillImpl, StatusSkillImpl};
use crate::db::skills::*;

pub mod abrbattlewarior;
pub mod abrdualcannon;
pub mod abrinfinity;
pub mod abrmothernet;
pub mod aciddemonstration;
pub mod acidifiedzonefire;
pub mod acidifiedzoneground;
pub mod acidifiedzonewater;
pub mod acidifiedzonewind;
pub mod acidterror;
pub mod adrenalinerush;
pub mod advancedadrenalinerush;
pub mod advanceprotection;
pub mod aidberserkpotion;
pub mod aidcondensedpotion;
pub mod aidpotion;
pub mod alchemicalweapon;
pub mod analyze;
pub mod armcannon;
pub mod attackmachine;
pub mod axeboomerang;
pub mod axestomp;
pub mod axetornado;
pub mod backsideslide;
pub mod biochemicalhelm;
pub mod bionicpharmacy;
pub mod bomb;
pub mod boostknuckle;
pub mod callhomunculus;
pub mod cartcannon;
pub mod cartrevolution;
pub mod carttermination;
pub mod carttornado;
pub mod changecart;
pub mod changematerial;
pub mod coldslower;
pub mod crazyuproar;
pub mod crazyweed;
pub mod createbomb;
pub mod creeper;
pub mod decoratecart;
pub mod demonicfire;
pub mod dustexplosion;
pub mod emergencycool;
pub mod energycannonade;
pub mod explosivepowder;
pub mod fawmagicdecoy;
pub mod fawremoval;
pub mod fawsilversniper;
pub mod fireexpansion;
pub mod flamelauncher;
pub mod frontsideslide;
pub mod fullprotection;
pub mod greed;
pub mod hammerfall;
pub mod hellsplant;
pub mod helltree;
pub mod homunculusresurrection;
pub mod howlingofmandragora;
pub mod illusiondoping;
pub mod infraredscan;
pub mod itemappraisal;
pub mod magmaeruption;
pub mod magneticfield;
pub mod mammonite;
pub mod manufacturemachine;
pub mod mayhemicthorns;
pub mod mightysmash;
pub mod mixcooking;
pub mod mysterypowder;
pub mod neutralbarrier;
pub mod pilebunker;
pub mod plantcultivation;
pub mod powerfulswing;
pub mod powerswing;
pub mod powerthrust;
pub mod preparepotion;
pub mod repair;
pub mod rushquake;
pub mod rushstrike;
pub mod selfdestruction;
pub mod skill_vending;
pub mod slingitem;
pub mod sparkblaster;
pub mod specialpharmacy;
pub mod sporeexplosion;
pub mod stealthfield;
pub mod summonflora;
pub mod summonmarinesphere;
pub mod synthesizedshield;
pub mod syntheticarmor;
pub mod thewholeprotection;
pub mod thorntrap;
pub mod triplelaser;
pub mod twilightalchemy1;
pub mod twilightalchemy2;
pub mod twilightalchemy3;
pub mod upgradeweapon;
pub mod vaporize;
pub mod vulcanarm;
pub mod wallofthorns;
pub mod weaponperfection;
pub mod weaponrepair;
pub mod woodenfairy;
pub mod woodenwarrior;

/// SkillFactoryMerchant::create (its case order).
pub fn create(skill_id: u16) -> Option<Box<dyn SkillImpl>> {
    // std::make_unique<StatusSkillImpl>(skill_id) / (skill_id, true)
    let status = |end_if_running: bool| -> Option<Box<dyn SkillImpl>> { Some(Box::new(StatusSkillImpl::new_toggle(skill_id, end_if_running))) };
    match skill_id {
        AM_ACIDTERROR => Some(Box::new(acidterror::SkillAcidTerror)),
        AM_BERSERKPITCHER => Some(Box::new(aidberserkpotion::SkillAidBerserkPotion)),
        AM_CALLHOMUN => Some(Box::new(callhomunculus::SkillCallHomunculus)),
        AM_CANNIBALIZE => Some(Box::new(summonflora::SkillSummonFlora)),
        AM_CP_ARMOR => Some(Box::new(syntheticarmor::SkillSyntheticArmor)),
        AM_CP_HELM => Some(Box::new(biochemicalhelm::SkillBiochemicalHelm)),
        AM_CP_SHIELD => Some(Box::new(synthesizedshield::SkillSynthesizedShield)),
        AM_CP_WEAPON => Some(Box::new(alchemicalweapon::SkillAlchemicalWeapon)),
        AM_DEMONSTRATION => Some(Box::new(bomb::SkillBomb)),
        AM_RESURRECTHOMUN => Some(Box::new(homunculusresurrection::SkillHomunculusResurrection)),
        AM_PHARMACY => Some(Box::new(preparepotion::SkillPreparePotion)),
        AM_POTIONPITCHER => Some(Box::new(aidpotion::SkillAidPotion)),
        AM_REST => Some(Box::new(vaporize::SkillVaporize)),
        AM_SPHEREMINE => Some(Box::new(summonmarinesphere::SkillSummonMarineSphere)),
        AM_TWILIGHT1 => Some(Box::new(twilightalchemy1::SkillTwilightAlchemy1)),
        AM_TWILIGHT2 => Some(Box::new(twilightalchemy2::SkillTwilightAlchemy2)),
        AM_TWILIGHT3 => Some(Box::new(twilightalchemy3::SkillTwilightAlchemy3)),
        BO_ACIDIFIED_ZONE_FIRE => Some(Box::new(acidifiedzonefire::SkillAcidifiedZoneFire)),
        BO_ACIDIFIED_ZONE_FIRE_ATK => Some(Box::new(acidifiedzonefire::SkillActifiedZoneFireAttack)),
        BO_ACIDIFIED_ZONE_GROUND => Some(Box::new(acidifiedzoneground::SkillAcidifiedZoneGround)),
        BO_ACIDIFIED_ZONE_GROUND_ATK => Some(Box::new(acidifiedzoneground::SkillActifiedZoneGroundAttack)),
        BO_ACIDIFIED_ZONE_WATER => Some(Box::new(acidifiedzonewater::SkillAcidifiedZoneWater)),
        BO_ACIDIFIED_ZONE_WATER_ATK => Some(Box::new(acidifiedzonewater::SkillActifiedZoneWaterAttack)),
        BO_ACIDIFIED_ZONE_WIND => Some(Box::new(acidifiedzonewind::SkillAcidifiedZoneWind)),
        BO_ACIDIFIED_ZONE_WIND_ATK => Some(Box::new(acidifiedzonewind::SkillActifiedZoneWindAttack)),
        BO_ADVANCE_PROTECTION => Some(Box::new(advanceprotection::SkillAdvanceProtection)),
        BO_BIONIC_PHARMACY => Some(Box::new(bionicpharmacy::SkillBionicPharmacy)),
        BO_CREEPER => Some(Box::new(creeper::SkillCreeper)),
        BO_DUST_EXPLOSION => Some(Box::new(dustexplosion::SkillDustExplosion)),
        BO_EXPLOSIVE_POWDER => Some(Box::new(explosivepowder::SkillExplosivePowder)),
        BO_HELLTREE => Some(Box::new(helltree::SkillHellTree)),
        BO_MAYHEMIC_THORNS => Some(Box::new(mayhemicthorns::SkillMayhemicThorns)),
        BO_MYSTERY_POWDER => Some(Box::new(mysterypowder::SkillMysteryPowder)),
        BO_RESEARCHREPORT => status(false),
        BO_THE_WHOLE_PROTECTION => Some(Box::new(thewholeprotection::SkillTheWholeProtection)),
        BO_WOODENWARRIOR => Some(Box::new(woodenwarrior::SkillWoodenWarrior)),
        BO_WOODEN_ATTACK => status(false),
        BO_WOODEN_FAIRY => Some(Box::new(woodenfairy::SkillWoodenFairy)),
        BO_WOODEN_THROWROCK => status(false),
        BS_ADRENALINE => Some(Box::new(adrenalinerush::SkillAdrenalineRush)),
        BS_ADRENALINE2 => Some(Box::new(advancedadrenalinerush::SkillAdvancedAdrenalineRush)),
        BS_GREED => Some(Box::new(greed::SkillGreed)),
        BS_HAMMERFALL => Some(Box::new(hammerfall::SkillHammerFall)),
        BS_MAXIMIZE => status(true),
        BS_OVERTHRUST => Some(Box::new(powerthrust::SkillPowerThrust)),
        BS_REPAIRWEAPON => Some(Box::new(weaponrepair::SkillWeaponRepair)),
        BS_WEAPONPERFECT => Some(Box::new(weaponperfection::SkillWeaponPerfection)),
        CR_ACIDDEMONSTRATION => Some(Box::new(aciddemonstration::SkillAcidDemonstration)),
        CR_CULTIVATION => Some(Box::new(plantcultivation::SkillPlantCultivation)),
        CR_FULLPROTECTION => Some(Box::new(fullprotection::SkillFullProtection)),
        CR_SLIMPITCHER => Some(Box::new(aidcondensedpotion::SkillAidCondensedPotion)),
        GN_BLOOD_SUCKER => status(false),
        GN_CARTBOOST => status(false),
        GN_CARTCANNON => Some(Box::new(cartcannon::SkillCartCannon)),
        GN_CART_TORNADO => Some(Box::new(carttornado::SkillCartTornado)),
        GN_CHANGEMATERIAL => Some(Box::new(changematerial::SkillChangeMaterial)),
        GN_CRAZYWEED => Some(Box::new(crazyweed::SkillCrazyWeed)),
        GN_CRAZYWEED_ATK => Some(Box::new(crazyweed::SkillCrazyWeedAttack)),
        GN_DEMONIC_FIRE => Some(Box::new(demonicfire::SkillDemonicFire)),
        GN_FIRE_EXPANSION => Some(Box::new(fireexpansion::SkillFireExpansion)),
        GN_FIRE_EXPANSION_ACID => Some(Box::new(fireexpansion::SkillFireExpansionAcid)),
        GN_HELLS_PLANT => Some(Box::new(hellsplant::SkillHellsPlant)),
        GN_HELLS_PLANT_ATK => Some(Box::new(hellsplant::SkillHellsPlantAttack)),
        GN_ILLUSIONDOPING => Some(Box::new(illusiondoping::SkillIllusionDoping)),
        GN_MAKEBOMB => Some(Box::new(createbomb::SkillCreateBomb)),
        GN_MANDRAGORA => Some(Box::new(howlingofmandragora::SkillHowlingOfMandragora)),
        GN_MIX_COOKING => Some(Box::new(mixcooking::SkillMixCooking)),
        GN_SLINGITEM => Some(Box::new(slingitem::SkillSlingItem)),
        GN_SLINGITEM_RANGEMELEEATK => Some(Box::new(slingitem::SkillSlingItemAttack)),
        GN_SPORE_EXPLOSION => Some(Box::new(sporeexplosion::SkillSporeExplosion)),
        GN_S_PHARMACY => Some(Box::new(specialpharmacy::SkillSpecialPharmacy)),
        GN_THORNS_TRAP => Some(Box::new(thorntrap::SkillThornTrap)),
        GN_WALLOFTHORN => Some(Box::new(wallofthorns::SkillWallOfThorns)),
        MC_CARTDECORATE => Some(Box::new(decoratecart::SkillDecorateCart)),
        MC_CARTREVOLUTION => Some(Box::new(cartrevolution::SkillCartRevolution)),
        MC_CHANGECART => Some(Box::new(changecart::SkillChangeCart)),
        MC_IDENTIFY => Some(Box::new(itemappraisal::SkillItemAppraisal)),
        MC_LOUD => Some(Box::new(crazyuproar::SkillCrazyUproar)),
        MC_MAMMONITE => Some(Box::new(mammonite::SkillMammonite)),
        MC_VENDING => Some(Box::new(skill_vending::SkillVending)),
        MT_AXE_STOMP => Some(Box::new(axestomp::SkillAxeStomp)),
        MT_A_MACHINE => Some(Box::new(attackmachine::SkillAttackMachine)),
        MT_D_MACHINE => status(false),
        MT_ENERGY_CANNONADE => Some(Box::new(energycannonade::SkillEnergyCannonade)),
        MT_MIGHTY_SMASH => Some(Box::new(mightysmash::SkillMightySmash)),
        MT_M_MACHINE => Some(Box::new(manufacturemachine::SkillManufactureMachine)),
        MT_POWERFUL_SWING => Some(Box::new(powerfulswing::SkillPowerfulSwing)),
        MT_RUSH_QUAKE => Some(Box::new(rushquake::SkillRushQuake)),
        MT_RUSH_STRIKE => Some(Box::new(rushstrike::SkillRushStrike)),
        MT_SPARK_BLASTER => Some(Box::new(sparkblaster::SkillSparkBlaster)),
        MT_SUMMON_ABR_BATTLE_WARIOR => Some(Box::new(abrbattlewarior::SkillAbrBattleWarrior)),
        MT_SUMMON_ABR_DUAL_CANNON => Some(Box::new(abrdualcannon::SkillAbrDualCannon)),
        MT_SUMMON_ABR_INFINITY => Some(Box::new(abrinfinity::SkillAbrInfinity)),
        MT_SUMMON_ABR_MOTHER_NET => Some(Box::new(abrmothernet::SkillAbrMotherNet)),
        MT_TRIPLE_LASER => Some(Box::new(triplelaser::SkillTripleLaser)),
        NC_ACCELERATION => status(false),
        NC_ANALYZE => Some(Box::new(analyze::SkillAnalyze)),
        NC_ARMSCANNON => Some(Box::new(armcannon::SkillArmCannon)),
        NC_AXEBOOMERANG => Some(Box::new(axeboomerang::SkillAxeBoomerang)),
        NC_AXETORNADO => Some(Box::new(axetornado::SkillAxeTornado)),
        NC_B_SIDESLIDE => Some(Box::new(backsideslide::SkillBackSideSlide)),
        NC_BOOSTKNUCKLE => Some(Box::new(boostknuckle::SkillBoostKnuckle)),
        NC_COLDSLOWER => Some(Box::new(coldslower::SkillColdSlower)),
        NC_DISJOINT => Some(Box::new(fawremoval::SkillFawRemoval)),
        NC_EMERGENCYCOOL => Some(Box::new(emergencycool::SkillEmergencyCool)),
        NC_FLAMELAUNCHER => Some(Box::new(flamelauncher::SkillFlameLauncher)),
        NC_F_SIDESLIDE => Some(Box::new(frontsideslide::SkillFrontSideSlide)),
        NC_HOVERING => status(false),
        NC_INFRAREDSCAN => Some(Box::new(infraredscan::SkillInfraredScan)),
        NC_MAGICDECOY => Some(Box::new(fawmagicdecoy::SkillFawMagicDecoy)),
        NC_MAGMA_ERUPTION => Some(Box::new(magmaeruption::SkillMagmaEruption)),
        NC_MAGMA_ERUPTION_DOTDAMAGE => Some(Box::new(magmaeruption::SkillMagmaEruptionDotDamage)),
        NC_MAGNETICFIELD => Some(Box::new(magneticfield::SkillMagneticField)),
        NC_NEUTRALBARRIER => Some(Box::new(neutralbarrier::SkillNeutralBarrier)),
        NC_PILEBUNKER => Some(Box::new(pilebunker::SkillPileBunker)),
        NC_POWERSWING => Some(Box::new(powerswing::SkillPowerSwing)),
        NC_REPAIR => Some(Box::new(repair::SkillRepair)),
        NC_SELFDESTRUCTION => Some(Box::new(selfdestruction::SkillSelfDestruction)),
        NC_SHAPESHIFT => status(false),
        NC_SILVERSNIPER => Some(Box::new(fawsilversniper::SkillFawSilverSniper)),
        NC_STEALTHFIELD => Some(Box::new(stealthfield::SkillStealthField)),
        NC_VULCANARM => Some(Box::new(vulcanarm::SkillVulcanArm)),
        WS_CARTBOOST => status(false),
        WS_CARTTERMINATION => Some(Box::new(carttermination::SkillCartTermination)),
        WS_MELTDOWN => status(false),
        WS_OVERTHRUSTMAX => status(false),
        WS_WEAPONREFINE => Some(Box::new(upgradeweapon::SkillUpgradeWeapon)),
        _ => None,
    }
}

// ---------------------------------------------------------------------------------------------- family helpers

/// pc.cpp pc_get_itemgroup_bonus_group(sd, group_id, bonuses): the value of the first bonus of that item group (the
/// potion pitchers' `IG_POTION` heal rates, sd->itemgrouphealrate / itemgroupsphealrate).
pub(crate) fn pc_get_itemgroup_bonus_group(bonuses: &[crate::status::data::ItemBonus], group_id: u16) -> i32 {
    for it in bonuses {
        if it.id == group_id {
            // int16 return value
            return it.val as i16 as i32;
        }
    }
    0
}

/// `mob_data* dstmd = BL_CAST(BL_MOB, target); dstmd && dstmd->mob_id == MOBID_EMPERIUM`.
pub(crate) fn is_emperium(c: &crate::world::Ctx, u: &crate::unit::Unit) -> bool {
    u.kind == crate::map::BL_MOB && crate::mob::get(c, u.id).map(|m| m.mob_id == crate::battle::mobif::MOBID_EMPERIUM as i32).unwrap_or(false)
}

/// The mob.cpp calls every summoning class of this family makes (abrbattlewarior.cpp, creeper.cpp, summonflora.cpp …):
/// `md->master_id = src->id; md->special_state.ai = ai; if (md->deletetimer != INVALID_TIMER)
/// delete_timer(md->deletetimer, mob_timer_delete); md->deletetimer = add_timer(due, mob_timer_delete, md->id, 0);
/// mob_spawn(md);` on the monster mob_once_spawn_sub made (`ai` < 0: special_state.ai is left as spawned). Returns
/// the monster's id.
pub(crate) fn summon_attach_and_spawn(c: &crate::world::Ctx, mut md: crate::mob::MobData, master_id: Option<u32>, ai: i32, due: crate::world::Tick) -> u32 {
    use crate::timer::{self, Func};
    if let Some(m) = master_id {
        md.master_id = m;
    }
    if ai >= 0 {
        md.special_ai = ai as u8;
    }
    if md.deletetimer != 0 {
        timer::delete_checked(c, md.deletetimer, Func::MobTimerDelete);
    }
    md.deletetimer = timer::add(c, due, Func::MobTimerDelete, md.id, 0);
    crate::mob::put(c, &md);
    crate::mob::spawn::mob_spawn(c, md.id);
    md.id
}

#[cfg(test)]
mod tests {
    /// The constants the family's classes take from the script constant table.
    #[test]
    fn constants_match_the_script_constants() {
        assert_eq!(crate::db::mobs::mob_db().find(crate::battle::mobif::MOBID_EMPERIUM).map(|m| m.sprite.clone()).as_deref(), Some("EMPELIUM"));
        assert_eq!(crate::data::constant("IG_POTION"), Some(super::IG_POTION as i64));
    }
}

/// itemdb.hpp IG_POTION (script constant).
pub(crate) const IG_POTION: u16 = 36;

/// status.cpp status_get_name (`mob_once_spawn_sub(src, …, status_get_name(*src), …)`): a player's fakename when set
/// (@fakename, atcmd_pc) else its name, an NPC's name, the other kinds' names; "Unknown" for the rest.
pub(crate) fn status_get_name(c: &crate::world::Ctx, u: &crate::unit::Unit) -> String {
    use crate::map::*;
    match u.kind {
        BL_PC => {
            let fake = crate::atcmd_pc::get(c, u.id).fakename;
            if !fake.is_empty() {
                fake
            } else {
                u.name.clone()
            }
        }
        BL_NPC => {
            use crate::npc::npc;
            c.db().npc().id().find(u.id).map(|n| n.name).unwrap_or_else(|| u.name.clone())
        }
        BL_MOB | BL_PET | BL_HOM | BL_MER | BL_ELEM => u.name.clone(),
        _ => "Unknown".into(),
    }
}

/// mob.hpp / itemdb.hpp constants the family's classes use (script constants; checked by the test below).
pub(crate) mod ids {
    pub const MOBID_RED_PLANT: i32 = 1078;
    pub const MOBID_BLUE_PLANT: i32 = 1079;
    pub const MOBID_GREEN_PLANT: i32 = 1080;
    pub const MOBID_YELLOW_PLANT: i32 = 1081;
    pub const MOBID_WHITE_PLANT: i32 = 1082;
    pub const MOBID_SHINING_PLANT: i32 = 1083;
    pub const MOBID_BLACK_MUSHROOM: i32 = 1084;
    pub const MOBID_MARINE_SPHERE: i32 = 1142;
    pub const MOBID_G_PARASITE: i32 = 1555;
    pub const MOBID_G_FLORA: i32 = 1575;
    pub const MOBID_G_HYDRA: i32 = 1579;
    pub const MOBID_G_MANDRAGORA: i32 = 1589;
    pub const MOBID_G_GEOGRAPHER: i32 = 1590;
    pub const MOBID_SILVERSNIPER: i32 = 2042;
    pub const MOBID_MAGICDECOY_FIRE: i32 = 2043;
    pub const MOBID_MAGICDECOY_WIND: i32 = 2046;
    pub const MOBID_BIONIC_WOODENWARRIOR: i32 = 20848;
    pub const MOBID_BIONIC_WOODEN_FAIRY: i32 = 20849;
    pub const MOBID_BIONIC_CREEPER: i32 = 20850;
    pub const MOBID_BIONIC_HELLTREE: i32 = 20851;
    pub const ITEMID_WHITE_POTION: u32 = 504;
    pub const ITEMID_WHITE_SLIM_POTION: u32 = 547;
    pub const ITEMID_EMPTY_BOTTLE: u32 = 713;
    pub const ITEMID_ALCOHOL: u32 = 970;
    pub const ITEMID_FIRE_BOTTLE: u32 = 7135;
    pub const ITEMID_ACID_BOTTLE: u32 = 7136;

    #[cfg(test)]
    mod tests {
        /// mob.hpp / itemdb.hpp enums are not script constants: compared with the monster and item databases.
        #[test]
        fn ids_match_the_databases() {
            let mob = |id: i32| crate::db::mobs::mob_db().find(id as u32).map(|m| m.sprite.clone()).unwrap_or_else(|| panic!("mob {id}"));
            let item = |id: u32| crate::db::items::itemdb_exists(id).map(|d| d.name.clone()).unwrap_or_else(|| panic!("item {id}"));
            let mobs: &[(i32, &str)] = &[
                (super::MOBID_RED_PLANT, "RED_PLANT"),
                (super::MOBID_BLUE_PLANT, "BLUE_PLANT"),
                (super::MOBID_GREEN_PLANT, "GREEN_PLANT"),
                (super::MOBID_YELLOW_PLANT, "YELLOW_PLANT"),
                (super::MOBID_WHITE_PLANT, "WHITE_PLANT"),
                (super::MOBID_SHINING_PLANT, "SHINING_PLANT"),
                (super::MOBID_BLACK_MUSHROOM, "BLACK_MUSHROOM"),
                (super::MOBID_MARINE_SPHERE, "MARINE_SPHERE"),
                (super::MOBID_G_PARASITE, "G_PARASITE"),
                (super::MOBID_G_FLORA, "G_FLORA"),
                (super::MOBID_G_HYDRA, "G_HYDRA"),
                (super::MOBID_G_MANDRAGORA, "G_MANDRAGORA"),
                (super::MOBID_G_GEOGRAPHER, "G_GEOGRAPHER"),
                (super::MOBID_SILVERSNIPER, "SILVERSNIPER"),
                (super::MOBID_MAGICDECOY_FIRE, "MAGICDECOY_FIRE"),
                (super::MOBID_MAGICDECOY_WIND, "MAGICDECOY_WIND"),
                (super::MOBID_BIONIC_WOODENWARRIOR, "SUMMON_WOODENWARRIOR"),
                (super::MOBID_BIONIC_WOODEN_FAIRY, "SUMMON_WOODEN_FAIRY"),
                (super::MOBID_BIONIC_CREEPER, "SUMMON_CREEPER"),
                (super::MOBID_BIONIC_HELLTREE, "SUMMON_HELLTREE"),
            ];
            for (id, n) in mobs {
                assert_eq!(mob(*id), *n, "mob {id}");
            }
            assert_eq!(item(super::ITEMID_WHITE_POTION), "White_Potion");
            assert_eq!(item(super::ITEMID_WHITE_SLIM_POTION), "White_Slim_Potion");
            assert_eq!(item(super::ITEMID_EMPTY_BOTTLE), "Empty_Bottle");
            assert_eq!(item(super::ITEMID_ALCOHOL), "Alchol");
            assert_eq!(item(super::ITEMID_FIRE_BOTTLE), "Fire_Bottle");
            assert_eq!(item(super::ITEMID_ACID_BOTTLE), "Acid_Bottle");
        }
    }
}
