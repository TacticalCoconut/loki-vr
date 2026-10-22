//! skills/swordman/skill_factory_swordman.cpp: SkillFactorySwordman::create and the swordman family's classes (rAthena e985006),
//! one file per class as rAthena has them (dragonbreath.rs, hackandslasher.rs and servantweapon.rs hold two classes
//! each, as their .cpp files do).

use super::{SkillImpl, StatusSkillImpl};
use crate::db::skills::*;
use crate::unit::Unit;
use crate::world::Ctx;

pub mod abundance;
pub mod autoberserk;
pub mod banding;
pub mod banishingpoint;
pub mod bash;
pub mod battlechant;
pub mod bowlingbash;
pub mod brandishspear;
pub mod cannonspear;
pub mod chargeattack;
pub mod counterattack;
pub mod crossrain;
pub mod crushstrike;
pub mod dragonbreath;
pub mod dragonhowling;
pub mod dragonicaura;
pub mod dragonicbreath;
pub mod dragonicpierce;
pub mod earthdrive;
pub mod enchantblade;
pub mod fightingspirit;
pub mod forceofvanguard;
pub mod giantgrowth;
pub mod gloriadomini;
pub mod grandcross;
pub mod grandjudgement;
pub mod guardianshield;
pub mod hackandslasher;
pub mod hesperuslit;
pub mod holycross;
pub mod hundredspear;
pub mod ignitionbreak;
pub mod imperialcross;
pub mod imperialpressure;
pub mod judgementcross;
pub mod kingsgrace;
pub mod luxanima;
pub mod madnesscrusher;
pub mod magnum;
pub mod martyrsreckoning;
pub mod milleniumshield;
pub mod moonslasher;
pub mod overbrand;
pub mod overslash;
pub mod phantomthrust;
pub mod pierce;
pub mod piety;
pub mod pinpointattack;
pub mod provoke;
pub mod radiantspear;
pub mod rageburst;
pub mod rayofgenesis;
pub mod refresh;
pub mod relax;
pub mod resistantsouls;
pub mod sacrifice;
pub mod selfprovoke;
pub mod servantweapon;
pub mod servantweapondemolition;
pub mod servantweaponphantom;
pub mod servantweaponsign;
pub mod shieldboomerang;
pub mod shieldchain;
pub mod shieldpress;
pub mod shieldreflect;
pub mod shieldshooting;
pub mod shieldspell;
pub mod smite;
pub mod sonicwave;
pub mod spearboomerang;
pub mod spearstab;
pub mod spiralpierce;
pub mod stonehardskin;
pub mod stormblast;
pub mod stormslash;
pub mod trample;
pub mod traumaticblow;
pub mod ultimatesacrifice;
pub mod vitalityactivation;
pub mod vitalstrike;
pub mod windcutter;

/// SkillFactorySwordman::create.
pub fn create(skill_id: u16) -> Option<Box<dyn SkillImpl>> {
    // std::make_unique<StatusSkillImpl>(skill_id) / (skill_id, true)
    let status = |end_if_running: bool| -> Option<Box<dyn SkillImpl>> { Some(Box::new(StatusSkillImpl::new_toggle(skill_id, end_if_running))) };
    match skill_id {
        CR_AUTOGUARD => status(true),
        CR_DEFENDER => status(true),
        CR_DEVOTION => Some(Box::new(sacrifice::SkillSacrifice)),
        CR_GRANDCROSS => Some(Box::new(grandcross::SkillGrandCross)),
        CR_HOLYCROSS => Some(Box::new(holycross::SkillHolyCross)),
        CR_PROVIDENCE => Some(Box::new(resistantsouls::SkillResistantSouls)),
        CR_REFLECTSHIELD => Some(Box::new(shieldreflect::SkillShieldReflect)),
        CR_SHIELDBOOMERANG => Some(Box::new(shieldboomerang::SkillShieldBoomerang)),
        CR_SHIELDCHARGE => Some(Box::new(smite::SkillSmite)),
        CR_SHRINK => status(true),
        CR_SPEARQUICKEN => status(false),
        DK_CHARGINGPIERCE => status(false),
        DK_DRAGONIC_AURA => Some(Box::new(dragonicaura::SkillDragonicAura)),
        DK_DRAGONIC_BREATH => Some(Box::new(dragonicbreath::SkillDragonicBreath)),
        DK_DRAGONIC_PIERCE => Some(Box::new(dragonicpierce::SkillDragonicPierce)),
        DK_HACKANDSLASHER => Some(Box::new(hackandslasher::SkillHackAndSlasher)),
        DK_HACKANDSLASHER_ATK => Some(Box::new(hackandslasher::SkillHackAndSlasherAttack)),
        DK_MADNESS_CRUSHER => Some(Box::new(madnesscrusher::SkillMadnessCrusher)),
        DK_SERVANTWEAPON => Some(Box::new(servantweapon::SkillServantWeapon)),
        DK_SERVANTWEAPON_ATK => Some(Box::new(servantweapon::SkillServantWeaponAttack)),
        DK_SERVANT_W_DEMOL => Some(Box::new(servantweapondemolition::SkillServantWeaponDemolition)),
        DK_SERVANT_W_PHANTOM => Some(Box::new(servantweaponphantom::SkillServantWeaponPhantom)),
        DK_SERVANT_W_SIGN => Some(Box::new(servantweaponsign::SkillServantWeaponSign)),
        DK_STORMSLASH => Some(Box::new(stormslash::SkillStormSlash)),
        DK_VIGOR => status(false),
        IG_ATTACK_STANCE => status(true),
        IG_CROSS_RAIN => Some(Box::new(crossrain::SkillCrossRain)),
        IG_GRAND_JUDGEMENT => Some(Box::new(grandjudgement::SkillGrandJudgement)),
        IG_GUARD_STANCE => status(true),
        IG_GUARDIAN_SHIELD => Some(Box::new(guardianshield::SkillGuardianShield)),
        IG_HOLY_SHIELD => status(false),
        IG_IMPERIAL_CROSS => Some(Box::new(imperialcross::SkillImperialCross)),
        IG_IMPERIAL_PRESSURE => Some(Box::new(imperialpressure::SkillImperialPressure)),
        IG_JUDGEMENT_CROSS => Some(Box::new(judgementcross::SkillJudgementCross)),
        IG_OVERSLASH => Some(Box::new(overslash::SkillOverSlash)),
        IG_RADIANT_SPEAR => Some(Box::new(radiantspear::SkillRadiantSpear)),
        IG_REBOUND_SHIELD => status(false),
        IG_SHIELD_SHOOTING => Some(Box::new(shieldshooting::SkillShieldShooting)),
        IG_ULTIMATE_SACRIFICE => Some(Box::new(ultimatesacrifice::SkillUltimateSacrifice)),
        KN_AUTOCOUNTER => Some(Box::new(counterattack::SkillCounterAttack)),
        KN_BOWLINGBASH => Some(Box::new(bowlingbash::SkillBowlingBash)),
        KN_BRANDISHSPEAR => Some(Box::new(brandishspear::SkillBrandishSpear)),
        KN_CHARGEATK => Some(Box::new(chargeattack::SkillChargeAttack)),
        KN_ONEHAND => status(false),
        KN_PIERCE => Some(Box::new(pierce::SkillPierce)),
        KN_SPEARBOOMERANG => Some(Box::new(spearboomerang::SkillSpearBoomerang)),
        KN_SPEARSTAB => Some(Box::new(spearstab::SkillSpearStab)),
        KN_TWOHANDQUICKEN => status(false),
        LG_BANDING => Some(Box::new(banding::SkillBanding)),
        LG_BANISHINGPOINT => Some(Box::new(banishingpoint::SkillBanishingPoint)),
        LG_CANNONSPEAR => Some(Box::new(cannonspear::SkillCannonSpear)),
        LG_EARTHDRIVE => Some(Box::new(earthdrive::SkillEarthDrive)),
        LG_EXEEDBREAK => status(false),
        LG_FORCEOFVANGUARD => Some(Box::new(forceofvanguard::SkillForceOfVanguard)),
        LG_HESPERUSLIT => Some(Box::new(hesperuslit::SkillHesperusLit)),
        LG_INSPIRATION => status(false),
        LG_KINGS_GRACE => Some(Box::new(kingsgrace::SkillKingsGrace)),
        LG_MOONSLASHER => Some(Box::new(moonslasher::SkillMoonSlasher)),
        LG_OVERBRAND => Some(Box::new(overbrand::SkillOverBrand)),
        LG_PIETY => Some(Box::new(piety::SkillPiety)),
        LG_PINPOINTATTACK => Some(Box::new(pinpointattack::SkillPinpointAttack)),
        LG_PRESTIGE => status(false),
        LG_RAGEBURST => Some(Box::new(rageburst::SkillRageBurst)),
        LG_RAYOFGENESIS => Some(Box::new(rayofgenesis::SkillRayOfGenesis)),
        LG_REFLECTDAMAGE => status(true),
        LG_SHIELDPRESS => Some(Box::new(shieldpress::SkillShieldPress)),
        LG_SHIELDSPELL => Some(Box::new(shieldspell::SkillShieldSpell)),
        LG_TRAMPLE => Some(Box::new(trample::SkillTrample)),
        LK_AURABLADE => status(false),
        LK_BERSERK => status(false),
        LK_CONCENTRATION => status(false),
        LK_HEADCRUSH => Some(Box::new(traumaticblow::SkillTraumaticBlow)),
        LK_JOINTBEAT => Some(Box::new(vitalstrike::SkillVitalStrike)),
        LK_PARRYING => status(false),
        LK_SPIRALPIERCE => Some(Box::new(spiralpierce::SkillSpiralPierce)),
        LK_TENSIONRELAX => Some(Box::new(relax::SkillRelax)),
        PA_GOSPEL => Some(Box::new(battlechant::SkillBattleChant)),
        PA_PRESSURE => Some(Box::new(gloriadomini::SkillGloriaDomini)),
        PA_SACRIFICE => Some(Box::new(martyrsreckoning::SkillMartyrsReckoning)),
        PA_SHIELDCHAIN => Some(Box::new(shieldchain::SkillShieldChain)),
        RK_ABUNDANCE => Some(Box::new(abundance::SkillAbundance)),
        RK_CRUSHSTRIKE => Some(Box::new(crushstrike::SkillCrushStrike)),
        RK_DEATHBOUND => status(false),
        RK_DRAGONBREATH => Some(Box::new(dragonbreath::SkillDragonBreath)),
        RK_DRAGONBREATH_WATER => Some(Box::new(dragonbreath::SkillDragonBreathWater)),
        RK_DRAGONHOWLING => Some(Box::new(dragonhowling::SkillDragonHowling)),
        RK_ENCHANTBLADE => Some(Box::new(enchantblade::SkillEnchantBlade)),
        RK_FIGHTINGSPIRIT => Some(Box::new(fightingspirit::SkillFightingSpirit)),
        RK_GIANTGROWTH => Some(Box::new(giantgrowth::SkillGiantGrowth)),
        RK_HUNDREDSPEAR => Some(Box::new(hundredspear::SkillHundredSpear)),
        RK_IGNITIONBREAK => Some(Box::new(ignitionbreak::SkillIgnitionBreak)),
        RK_LUXANIMA => Some(Box::new(luxanima::SkillLuxAnima)),
        RK_MILLENNIUMSHIELD => Some(Box::new(milleniumshield::SkillMilleniumShield)),
        RK_PHANTOMTHRUST => Some(Box::new(phantomthrust::SkillPhantomThrust)),
        RK_REFRESH => Some(Box::new(refresh::SkillRefresh)),
        RK_SONICWAVE => Some(Box::new(sonicwave::SkillSonicWave)),
        RK_STONEHARDSKIN => Some(Box::new(stonehardskin::SkillStoneHardSkin)),
        RK_STORMBLAST => Some(Box::new(stormblast::SkillStormBlast)),
        RK_VITALITYACTIVATION => Some(Box::new(vitalityactivation::SkillVitalityActivation)),
        RK_WINDCUTTER => Some(Box::new(windcutter::SkillWindCutter)),
        SM_AUTOBERSERK => Some(Box::new(autoberserk::SkillAutoBerserk)),
        SM_BASH => Some(Box::new(bash::SkillBash)),
        SM_ENDURE => status(false),
        SM_MAGNUM => Some(Box::new(magnum::SkillMagnumBreak)),
        SM_PROVOKE => Some(Box::new(provoke::SkillProvoke)),
        SM_SELFPROVOKE => Some(Box::new(selfprovoke::SkillProvokeSelf)),
        _ => None,
    }
}

// ---------------------------------------------------------------------------------------------- family helpers

/// pc.cpp pc_checkskill_imperial_guard(BL_CAST(BL_PC, bl), flag): flag&1 IG_SHIELD_MASTERY with a shield, flag&2
/// IG_SPEAR_SWORD_M with a one-handed sword or a spear (uint8 count; nullpo_retr(0, sd) without a player).
pub(crate) fn pc_checkskill_imperial_guard(c: &Ctx, bl: &Unit, flag: i16) -> i32 {
    use crate::status::defs::{W_1HSPEAR, W_1HSWORD, W_2HSPEAR};
    if bl.kind != crate::map::BL_PC {
        return 0;
    }
    let Some(ch) = crate::pc::character_of(c, bl.id) else { return 0 };
    let mut count: u8 = 0;
    if flag & 1 != 0 && ch.shield > 0 {
        count = count.wrapping_add(crate::skill::checkskill(c, bl.id, IG_SHIELD_MASTERY));
    }
    let w = ch.weapon as i32;
    if flag & 2 != 0 && (w == W_1HSWORD || w == W_1HSPEAR || w == W_2HSPEAR) {
        count = count.wrapping_add(crate::skill::checkskill(c, bl.id, IG_SPEAR_SWORD_M));
    }
    count as i32
}

#[cfg(test)]
mod tests {
    /// The skill-unit ids trample.rs names (skill.hpp e_skill_unit_id, exported as script constants).
    #[test]
    fn unit_ids_match_the_script_constants() {
        use super::trample::*;
        let checks: &[(&str, u16)] = &[
            ("UNT_BLASTMINE", UNT_BLASTMINE),
            ("UNT_LANDMINE", UNT_LANDMINE),
            ("UNT_SHOCKWAVE", UNT_SHOCKWAVE),
            ("UNT_SANDMAN", UNT_SANDMAN),
            ("UNT_FLASHER", UNT_FLASHER),
            ("UNT_FREEZINGTRAP", UNT_FREEZINGTRAP),
            ("UNT_CLAYMORETRAP", UNT_CLAYMORETRAP),
            ("UNT_FIRINGTRAP", UNT_FIRINGTRAP),
            ("UNT_ICEBOUNDTRAP", UNT_ICEBOUNDTRAP),
            ("UNT_CLUSTERBOMB", UNT_CLUSTERBOMB),
        ];
        for (name, v) in checks {
            assert_eq!(crate::data::constant(name), Some(*v as i64), "{name}");
        }
    }

    /// skill/clif.rs's clif_devotion packet (0x1cf) against the generated packet length table.
    #[test]
    fn devotion_list_packet_length() {
        use crate::skill::clif::{ZC_DEVOTIONLIST, ZC_DEVOTIONLIST_LEN};
        assert_eq!(crate::net::packets::packet_len(ZC_DEVOTIONLIST), Some(ZC_DEVOTIONLIST_LEN as i16));
    }

    /// sacrifice.rs's battle_config.devotion_level_difference against the reference server's battle_config table.
    #[test]
    fn devotion_level_difference_matches_the_conf() {
        assert_eq!(super::sacrifice::devotion_level_difference(), crate::script::battle_flags::battle_get_value("devotion_level_difference"));
    }
}
