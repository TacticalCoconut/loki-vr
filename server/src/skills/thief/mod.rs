//! skills/thief/skill_factory_thief.cpp: SkillFactoryThief::create and the thief family's classes (rAthena e985006):
//! Thief, Assassin / Assassin Cross, Rogue / Stalker, Guillotine Cross, Shadow Chaser, Shadow Cross and Abyss Chaser.
//! One file per class file of rAthena's `src/map/skills/thief/` (abyssflame.rs, chainreactionshot.rs and
//! fromtheabyss.rs hold two classes each, as their .cpp files do).

use super::{SkillImpl, StatusSkillImpl};
use crate::db::skills::*;

pub mod abyssdagger;
pub mod abyssflame;
pub mod abysssquare;
pub mod antidote;
pub mod autoshadowspell;
pub mod backslide;
pub mod backstab;
pub mod bloodylust;
pub mod bodypainting;
pub mod chainreactionshot;
pub mod chaospanic;
pub mod chasingbreak;
pub mod chasingshot;
pub mod cloaking;
pub mod cloakingexceed;
pub mod closeconfine;
pub mod counterinstinct;
pub mod counterslash;
pub mod createdeadlypoison;
pub mod createnewpoison;
pub mod crossimpact;
pub mod crossripperslasher;
pub mod crossslash;
pub mod dancingknife;
pub mod darkclaw;
pub mod darkillusion;
pub mod deftstab;
pub mod detoxify;
pub mod dimensiondoor;
pub mod divestall;
pub mod divestarmor;
pub mod divesthelm;
pub mod divestshield;
pub mod divestweapon;
pub mod doubleattack;
pub mod emergencyescape;
pub mod enchantdeadlypoison;
pub mod enchantpoison;
pub mod envenom;
pub mod ext;
pub mod eternalslash;
pub mod fatalmenace;
pub mod fatalshadowcrow;
pub mod feintbomb;
pub mod findstone;
pub mod frenzyshot;
pub mod fromtheabyss;
pub mod grimtooth;
pub mod hallucinationwalk;
pub mod hiding;
pub mod hitandsliding;
pub mod impactcrater;
pub mod invisibility;
pub mod maelstrom;
pub mod manhole;
pub mod masqueradeenervation;
pub mod masqueradegloomy;
pub mod masqueradeignorance;
pub mod masqueradelaziness;
pub mod masqueradeunlucky;
pub mod masqueradeweakness;
pub mod meteorassault;
pub mod mug;
pub mod omegaabyssstrike;
pub mod phantommenace;
pub mod poisoningweapon;
pub mod poisonsmoke;
pub mod remover;
pub mod reproduce;
pub mod rollingcutter;
pub mod sandattack;
pub mod savageimpact;
pub mod scribble;
pub mod shadowform;
pub mod shadowstab;
pub mod sightlessmind;
pub mod snatch;
pub mod sonicblow;
pub mod souldestroyer;
pub mod steal;
pub mod stealth;
pub mod stonefling;
pub mod stripaccessory;
pub mod stripshadow;
pub mod throwvenomknife;
pub mod triangleshot;
pub mod unluckyrush;
pub mod venomdust;
pub mod venompressure;
pub mod venomsplasher;
pub mod weaponcrush;

/// SkillFactoryThief::create (the arms in rAthena's order).
pub fn create(skill_id: u16) -> Option<Box<dyn SkillImpl>> {
    // std::make_unique<StatusSkillImpl>(skill_id) / (skill_id, true)
    let status = |end_if_running: bool| -> Option<Box<dyn SkillImpl>> { Some(Box::new(StatusSkillImpl::new_toggle(skill_id, end_if_running))) };
    match skill_id {
        ABC_ABYSS_DAGGER => Some(Box::new(abyssdagger::SkillAbyssDagger)),
        ABC_ABYSS_FLAME => Some(Box::new(abyssflame::SkillAbyssFlame)),
        ABC_ABYSS_FLAME_ATK => Some(Box::new(abyssflame::SkillAbyssFlameAttack)),
        ABC_ABYSS_SLAYER => status(false),
        ABC_ABYSS_SQUARE => Some(Box::new(abysssquare::SkillAbyssSquare)),
        ABC_ABYSS_STRIKE => Some(Box::new(omegaabyssstrike::SkillOmegaAbyssStrike)),
        ABC_CHAIN_REACTION_SHOT => Some(Box::new(chainreactionshot::SkillChainReactionShot)),
        ABC_CHAIN_REACTION_SHOT_ATK => Some(Box::new(chainreactionshot::SkillChainReactionShotAttack)),
        ABC_CHASING_BREAK => Some(Box::new(chasingbreak::SkillChasingBreak)),
        ABC_CHASING_SHOT => Some(Box::new(chasingshot::SkillChasingShot)),
        ABC_DEFT_STAB => Some(Box::new(deftstab::SkillDeftStab)),
        ABC_FRENZY_SHOT => Some(Box::new(frenzyshot::SkillFrenzyShot)),
        ABC_FROM_THE_ABYSS => Some(Box::new(fromtheabyss::SkillFromTheAbyss)),
        ABC_FROM_THE_ABYSS_ATK => Some(Box::new(fromtheabyss::SkillFromTheAbyssAttack)),
        ABC_HIT_AND_SLIDING => Some(Box::new(hitandsliding::SkillHitAndSliding)),
        ABC_STRIP_SHADOW => Some(Box::new(stripshadow::SkillStripShadow)),
        ABC_UNLUCKY_RUSH => Some(Box::new(unluckyrush::SkillUnluckyRush)),
        ASC_BREAKER => Some(Box::new(souldestroyer::SkillSoulDestroyer)),
        ASC_CDP => Some(Box::new(createdeadlypoison::SkillCreateDeadlyPoison)),
        ASC_EDP => Some(Box::new(enchantdeadlypoison::SkillEnchantDeadlyPoison)),
        ASC_METEORASSAULT => Some(Box::new(meteorassault::SkillMeteorAssault)),
        AS_CLOAKING => Some(Box::new(cloaking::SkillCloaking)),
        AS_ENCHANTPOISON => Some(Box::new(enchantpoison::SkillEnchantPoison)),
        AS_GRIMTOOTH => Some(Box::new(grimtooth::SkillGrimtooth)),
        AS_POISONREACT => status(false),
        AS_SONICBLOW => Some(Box::new(sonicblow::SkillSonicBlow)),
        AS_SPLASHER => Some(Box::new(venomsplasher::SkillVenomSplasher)),
        AS_VENOMDUST => Some(Box::new(venomdust::SkillVenomDust)),
        AS_VENOMKNIFE => Some(Box::new(throwvenomknife::SkillThrowVenomKnife)),
        GC_ANTIDOTE => Some(Box::new(antidote::SkillAntidote)),
        GC_CLOAKINGEXCEED => Some(Box::new(cloakingexceed::SkillCloakingExceed)),
        GC_COUNTERSLASH => Some(Box::new(counterslash::SkillCounterSlash)),
        GC_CREATENEWPOISON => Some(Box::new(createnewpoison::SkillCreateNewPoison)),
        GC_CROSSIMPACT => Some(Box::new(crossimpact::SkillCrossImpact)),
        GC_CROSSRIPPERSLASHER => Some(Box::new(crossripperslasher::SkillCrossRipperSlasher)),
        GC_DARKCROW => Some(Box::new(darkclaw::SkillDarkClaw)),
        GC_DARKILLUSION => Some(Box::new(darkillusion::SkillDarkIllusion)),
        GC_HALLUCINATIONWALK => Some(Box::new(hallucinationwalk::SkillHallucinationWalk)),
        GC_PHANTOMMENACE => Some(Box::new(phantommenace::SkillPhantomMenace)),
        GC_POISONINGWEAPON => Some(Box::new(poisoningweapon::SkillPoisoningWeapon)),
        GC_POISONSMOKE => Some(Box::new(poisonsmoke::SkillPoisonSmoke)),
        GC_ROLLINGCUTTER => Some(Box::new(rollingcutter::SkillRollingCutter)),
        GC_VENOMIMPRESS => status(false),
        GC_VENOMPRESSURE => Some(Box::new(venompressure::SkillVenomPressure)),
        GC_WEAPONBLOCKING => status(true),
        GC_WEAPONCRUSH => Some(Box::new(weaponcrush::SkillWeaponCrush)),
        RG_BACKSTAP => Some(Box::new(backstab::SkillBackStab)),
        RG_CLEANER => Some(Box::new(remover::SkillRemover)),
        RG_CLOSECONFINE => Some(Box::new(closeconfine::SkillCloseConfine)),
        RG_GRAFFITI => Some(Box::new(scribble::SkillScribble)),
        RG_INTIMIDATE => Some(Box::new(snatch::SkillSnatch)),
        RG_RAID => Some(Box::new(sightlessmind::SkillSightlessMind)),
        RG_STEALCOIN => Some(Box::new(mug::SkillMug)),
        RG_STRIPARMOR => Some(Box::new(divestarmor::SkillDivestArmor)),
        RG_STRIPHELM => Some(Box::new(divesthelm::SkillDivestHelm)),
        RG_STRIPSHIELD => Some(Box::new(divestshield::SkillDivestShield)),
        RG_STRIPWEAPON => Some(Box::new(divestweapon::SkillDivestWeapon)),
        SC_AUTOSHADOWSPELL => Some(Box::new(autoshadowspell::SkillAutoShadowSpell)),
        SC_BLOODYLUST => Some(Box::new(bloodylust::SkillBloodyLust)),
        SC_BODYPAINT => Some(Box::new(bodypainting::SkillBodyPainting)),
        SC_CHAOSPANIC => Some(Box::new(chaospanic::SkillChaosPanic)),
        SC_DEADLYINFECT => status(false),
        SC_DIMENSIONDOOR => Some(Box::new(dimensiondoor::SkillDimensionDoor)),
        SC_ENERVATION => Some(Box::new(masqueradeenervation::SkillMasqueradeEnervation)),
        SC_ESCAPE => Some(Box::new(emergencyescape::SkillEmergencyEscape)),
        SC_FATALMENACE => Some(Box::new(fatalmenace::SkillFatalMenace)),
        SC_FEINTBOMB => Some(Box::new(feintbomb::SkillFeintBomb)),
        SC_GROOMY => Some(Box::new(masqueradegloomy::SkillMasqueradeGloomy)),
        SC_IGNORANCE => Some(Box::new(masqueradeignorance::SkillMasqueradeIgnorance)),
        SC_INVISIBILITY => Some(Box::new(invisibility::SkillInvisibility)),
        SC_LAZINESS => Some(Box::new(masqueradelaziness::SkillMasqueradeLaziness)),
        SC_MAELSTROM => Some(Box::new(maelstrom::SkillMaelstrom)),
        SC_MANHOLE => Some(Box::new(manhole::SkillManHole)),
        SC_REPRODUCE => Some(Box::new(reproduce::SkillReproduce)),
        SC_SHADOWFORM => Some(Box::new(shadowform::SkillShadowForm)),
        SC_STRIPACCESSARY => Some(Box::new(stripaccessory::SkillStripAccessory)),
        SC_TRIANGLESHOT => Some(Box::new(triangleshot::SkillTriangleShot)),
        SC_UNLUCKY => Some(Box::new(masqueradeunlucky::SkillMasqueradeUnlucky)),
        SC_WEAKNESS => Some(Box::new(masqueradeweakness::SkillMasqueradeWeakness)),
        SHC_CROSS_SLASH => Some(Box::new(crossslash::SkillCrossSlash)),
        SHC_DANCING_KNIFE => Some(Box::new(dancingknife::SkillDancingKnife)),
        SHC_ENCHANTING_SHADOW => status(false),
        SHC_ETERNAL_SLASH => Some(Box::new(eternalslash::SkillEternalSlash)),
        SHC_FATAL_SHADOW_CROW => Some(Box::new(fatalshadowcrow::SkillFatalShadowCrow)),
        SHC_IMPACT_CRATER => Some(Box::new(impactcrater::SkillImpactCrater)),
        SHC_POTENT_VENOM => status(false),
        SHC_SAVAGE_IMPACT => Some(Box::new(savageimpact::SkillSavageImpact)),
        SHC_SHADOW_EXCEED => status(false),
        SHC_SHADOW_STAB => Some(Box::new(shadowstab::SkillShadowStab)),
        ST_CHASEWALK => Some(Box::new(stealth::SkillStealth)),
        ST_FULLSTRIP => Some(Box::new(divestall::SkillDivestAll)),
        ST_PRESERVE => status(false),
        ST_REJECTSWORD => Some(Box::new(counterinstinct::SkillCounterInstinct)),
        TF_BACKSLIDING => Some(Box::new(backslide::SkillBackSlide)),
        TF_DETOXIFY => Some(Box::new(detoxify::SkillDetoxify)),
        TF_DOUBLE => Some(Box::new(doubleattack::SkillDoubleAttack)),
        TF_HIDING => Some(Box::new(hiding::SkillHiding)),
        TF_PICKSTONE => Some(Box::new(findstone::SkillFindStone)),
        TF_POISON => Some(Box::new(envenom::SkillEnvenom)),
        TF_SPRINKLESAND => Some(Box::new(sandattack::SkillSandAttack)),
        TF_STEAL => Some(Box::new(steal::SkillSteal)),
        TF_THROWSTONE => Some(Box::new(stonefling::SkillStoneFling)),
        _ => None,
    }
}

// ---------------------------------------------------------------------------------------------- family helpers

/// The success rate the six Masquerade classes compute the same way (masqueradeenervation.cpp,
/// masqueradegloomy.cpp, masqueradeignorance.cpp, masqueradelaziness.cpp, masqueradeunlucky.cpp,
/// masqueradeweakness.cpp):
/// `rate = status_get_lv(src) / 10 + rnd_value(sstatus->dex / 12, sstatus->dex / 4) + (sd ? sd->status.job_level : 50)
/// + 10 * skill_lv - (status_get_lv(target) / 10 + rnd_value(tstatus->agi / 6, tstatus->agi / 3) + tstatus->luk / 10
/// + (dstsd ? (dstsd->max_weight / 10 - dstsd->weight / 10) / 100 : 0)); rate = cap_value(rate, skill_lv +
/// sstatus->dex / 20, 100);`
/// The weight term is unsigned (uint32) in rAthena, which makes the whole subtraction unsigned before the int32
/// assignment: the wrapping i32 arithmetic here gives the same bits. The two rolls are taken left to right.
pub(crate) fn masquerade_rate(c: &crate::world::Ctx, src: &crate::unit::Unit, target: &crate::unit::Unit, skill_lv: u16) -> i32 {
    use crate::map::BL_PC;
    let sstatus = crate::status::status_get_status_data(c, src);
    let tstatus = crate::status::status_get_status_data(c, target);
    let sdex = sstatus.dex as i32;
    let tagi = tstatus.agi as i32;
    let job = if src.kind == BL_PC { crate::pc::character_of(c, src.id).map(|ch| ch.job_level as i32).unwrap_or(0) } else { 50 };
    let plus = (crate::status::status_get_lv(c, src) / 10).wrapping_add(c.rnd_value(sdex / 12, sdex / 4)).wrapping_add(job).wrapping_add(10 * skill_lv as i32);
    let t_lv = crate::status::status_get_lv(c, target) / 10;
    let t_roll = c.rnd_value(tagi / 6, tagi / 3);
    let weight: u32 = if target.kind == BL_PC {
        crate::inventory::st(c, target.id).map(|w| (w.max_weight / 10).wrapping_sub(w.weight / 10) / 100).unwrap_or(0)
    } else {
        0
    };
    let minus = t_lv.wrapping_add(t_roll).wrapping_add(tstatus.luk as i32 / 10).wrapping_add(weight as i32);
    let rate = plus.wrapping_sub(minus);
    // cap_value(a, min, max): ((a) >= (max)) ? (max) : ((a) <= (min)) ? (min) : (a)
    let min = skill_lv as i32 + sdex / 20;
    if rate >= 100 {
        100
    } else if rate <= min {
        min
    } else {
        rate
    }
}

#[cfg(test)]
mod tests {
    use crate::db::skills::*;

    /// skill_factory_thief.cpp's case labels (in its order): each one has a class here, and nothing else does.
    const RATHENA_CASES: &[u16] = &[ABC_ABYSS_DAGGER, ABC_ABYSS_FLAME, ABC_ABYSS_FLAME_ATK, ABC_ABYSS_SLAYER, ABC_ABYSS_SQUARE, ABC_ABYSS_STRIKE, ABC_CHAIN_REACTION_SHOT, ABC_CHAIN_REACTION_SHOT_ATK, ABC_CHASING_BREAK, ABC_CHASING_SHOT, ABC_DEFT_STAB, ABC_FRENZY_SHOT, ABC_FROM_THE_ABYSS, ABC_FROM_THE_ABYSS_ATK, ABC_HIT_AND_SLIDING, ABC_STRIP_SHADOW, ABC_UNLUCKY_RUSH, ASC_BREAKER, ASC_CDP, ASC_EDP, ASC_METEORASSAULT, AS_CLOAKING, AS_ENCHANTPOISON, AS_GRIMTOOTH, AS_POISONREACT, AS_SONICBLOW, AS_SPLASHER, AS_VENOMDUST, AS_VENOMKNIFE, GC_ANTIDOTE, GC_CLOAKINGEXCEED, GC_COUNTERSLASH, GC_CREATENEWPOISON, GC_CROSSIMPACT, GC_CROSSRIPPERSLASHER, GC_DARKCROW, GC_DARKILLUSION, GC_HALLUCINATIONWALK, GC_PHANTOMMENACE, GC_POISONINGWEAPON, GC_POISONSMOKE, GC_ROLLINGCUTTER, GC_VENOMIMPRESS, GC_VENOMPRESSURE, GC_WEAPONBLOCKING, GC_WEAPONCRUSH, RG_BACKSTAP, RG_CLEANER, RG_CLOSECONFINE, RG_GRAFFITI, RG_INTIMIDATE, RG_RAID, RG_STEALCOIN, RG_STRIPARMOR, RG_STRIPHELM, RG_STRIPSHIELD, RG_STRIPWEAPON, SC_AUTOSHADOWSPELL, SC_BLOODYLUST, SC_BODYPAINT, SC_CHAOSPANIC, SC_DEADLYINFECT, SC_DIMENSIONDOOR, SC_ENERVATION, SC_ESCAPE, SC_FATALMENACE, SC_FEINTBOMB, SC_GROOMY, SC_IGNORANCE, SC_INVISIBILITY, SC_LAZINESS, SC_MAELSTROM, SC_MANHOLE, SC_REPRODUCE, SC_SHADOWFORM, SC_STRIPACCESSARY, SC_TRIANGLESHOT, SC_UNLUCKY, SC_WEAKNESS, SHC_CROSS_SLASH, SHC_DANCING_KNIFE, SHC_ENCHANTING_SHADOW, SHC_ETERNAL_SLASH, SHC_FATAL_SHADOW_CROW, SHC_IMPACT_CRATER, SHC_POTENT_VENOM, SHC_SAVAGE_IMPACT, SHC_SHADOW_EXCEED, SHC_SHADOW_STAB, ST_CHASEWALK, ST_FULLSTRIP, ST_PRESERVE, ST_REJECTSWORD, TF_BACKSLIDING, TF_DETOXIFY, TF_DOUBLE, TF_HIDING, TF_PICKSTONE, TF_POISON, TF_SPRINKLESAND, TF_STEAL, TF_THROWSTONE, ];

    #[test]
    fn factory_matches_rathena() {
        assert_eq!(RATHENA_CASES.len(), 102);
        for &id in RATHENA_CASES {
            assert!(super::create(id).is_some(), "no class for skill {id}");
        }
        for s in skill_db().iter() {
            if super::create(s.nameid).is_some() {
                assert!(RATHENA_CASES.contains(&s.nameid), "{} has a class rAthena's factory does not create", s.name);
            }
        }
    }

    /// The battle_config values the class files keep as constants, against the reference server's table.
    #[test]
    fn battle_config_constants() {
        use crate::script::battle_flags::battle_get_value;
        assert_eq!(super::backstab::backstab_bow_penalty(), battle_get_value("backstab_bow_penalty"));
        assert_eq!(super::findstone::skill_drop_items_full(), battle_get_value("skill_drop_items_full"));
    }

    /// ITEMID_STONE (itemdb.hpp) is the Stone item; IG_POISON is a known item group with the Guillotine poisons.
    #[test]
    fn item_constants() {
        assert_eq!(crate::db::items::itemdb_exists(super::findstone::ITEMID_STONE).map(|d| d.name.as_str()), Some("Stone"));
        let ig = crate::data::constant("IG_POISON").expect("IG_POISON");
        assert!(crate::db::items::group::itemdb_group_item_exists(ig as u16, 12717), "Paralysis poison (12717) is in IG_POISON");
    }
}
