//! skills/taekwon/skill_factory_taekwon.cpp: SkillFactoryTaekwon::create and the taekwon family's classes (rAthena
//! e985006: Taekwon, Star Gladiator, Soul Linker, Star Emperor, Soul Reaper, Sky Emperor, Soul Ascetic), one file per
//! class file as rAthena has them (fallingstar.rs holds three classes, as fallingstar.cpp does).
//!
//! The end of this file holds the small helpers several class files share (each names the rAthena code it stands for).

use super::{SkillImpl, StatusSkillImpl};
use crate::db::skills::*;

pub mod allinthesky;
pub mod bookofcreatingstar;
pub mod circleofdirectionsandelementals;
pub mod counter;
pub mod curseexplosion;
pub mod dawnbreak;
pub mod documentofsunmoonandstar;
pub mod downkick;
pub mod esha;
pub mod eska;
pub mod eske;
pub mod esma;
pub mod espa;
pub mod estin;
pub mod estun;
pub mod eswhoo;
pub mod eswoo;
pub mod exorcismofmalicioussoul;
pub mod fairyssoul;
pub mod falconssoul;
pub mod fallingstar;
pub mod feelingthesunmoonandstars;
pub mod flashkick;
pub mod fullmoonkick;
pub mod golemssoul;
pub mod gravitycontrol;
pub mod hatredofthesunmoonandstars;
pub mod highjump;
pub mod jumpkick;
pub mod kaahi;
pub mod kaite;
pub mod kaizel;
pub mod kaupe;
pub mod kaute;
pub mod midnightkick;
pub mod mission;
pub mod newmoonkick;
pub mod noonblast;
pub mod novaexplosion;
pub mod prominencekick;
pub mod risingmoon;
pub mod risingsun;
pub mod run;
pub mod sevenwind;
pub mod shadowssoul;
pub mod skymoon;
pub mod skysun;
pub mod solarburst;
pub mod soulcollect;
pub mod soulcurse;
pub mod souldivision;
pub mod soulexplosion;
pub mod soulgathering;
pub mod soulofheavenandearth;
pub mod soulrevolution;
pub mod soulunity;
pub mod spiritofrebirth;
pub mod spiritofthealchemist;
pub mod spiritoftheartist;
pub mod spiritoftheassasin;
pub mod spiritoftheblacksmith;
pub mod spiritofthecrusader;
pub mod spiritofthehunter;
pub mod spiritoftheknight;
pub mod spiritofthemonk;
pub mod spiritofthepriest;
pub mod spiritoftherogue;
pub mod spiritofthesage;
pub mod spiritofthesoullinker;
pub mod spiritofthestargladiator;
pub mod spiritofthesupernovice;
pub mod spiritofthewizard;
pub mod starburst;
pub mod starcannon;
pub mod staremperoradvent;
pub mod starlightkick;
pub mod stormkick;
pub mod sunsetblast;
pub mod talismanofblacktortoise;
pub mod talismanofbluedragon;
pub mod talismanoffiveelements;
pub mod talismanoffourbearinggod;
pub mod talismanofmagician;
pub mod talismanofprotection;
pub mod talismanofredphoenix;
pub mod talismanofsoulstealing;
pub mod talismanofwarrior;
pub mod talismanofwhitetiger;
pub mod totemoftutelary;
pub mod turnkick;
pub mod twinklinggalaxy;
pub mod warmthofthemoon;
pub mod warmthofthestars;
pub mod warmthofthesun;

/// SkillFactoryTaekwon::create.
pub fn create(skill_id: u16) -> Option<Box<dyn SkillImpl>> {
    match skill_id {
        SG_FEEL => Some(Box::new(feelingthesunmoonandstars::SkillFeelingtheSunMoonandStars)),
        SG_FUSION => Some(Box::new(StatusSkillImpl::new_toggle(skill_id, true))),
        SG_HATE => Some(Box::new(hatredofthesunmoonandstars::SkillHatredoftheSunMoonandStars)),
        SG_MOON_COMFORT => Some(Box::new(StatusSkillImpl::new(skill_id))),
        SG_MOON_WARM => Some(Box::new(warmthofthemoon::SkillWarmthoftheMoon)),
        SG_STAR_COMFORT => Some(Box::new(StatusSkillImpl::new(skill_id))),
        SG_STAR_WARM => Some(Box::new(warmthofthestars::SkillWarmthoftheStars)),
        SG_SUN_COMFORT => Some(Box::new(StatusSkillImpl::new(skill_id))),
        SG_SUN_WARM => Some(Box::new(warmthofthesun::SkillWarmthoftheSun)),
        SJ_BOOKOFCREATINGSTAR => Some(Box::new(bookofcreatingstar::SkillBookofCreatingStar)),
        SJ_BOOKOFDIMENSION => Some(Box::new(StatusSkillImpl::new(skill_id))),
        SJ_DOCUMENT => Some(Box::new(documentofsunmoonandstar::SkillDocumentofSunMoonAndStar)),
        SJ_FALLINGSTAR => Some(Box::new(fallingstar::SkillFallingStar)),
        SJ_FALLINGSTAR_ATK => Some(Box::new(fallingstar::SkillFallingStarAttack)),
        SJ_FALLINGSTAR_ATK2 => Some(Box::new(fallingstar::SkillFallingStarAttack2)),
        SJ_FLASHKICK => Some(Box::new(flashkick::SkillFlashKick)),
        SJ_FULLMOONKICK => Some(Box::new(fullmoonkick::SkillFullMoonKick)),
        SJ_GRAVITYCONTROL => Some(Box::new(gravitycontrol::SkillGravityControl)),
        SJ_LIGHTOFMOON => Some(Box::new(StatusSkillImpl::new(skill_id))),
        SJ_LIGHTOFSTAR => Some(Box::new(StatusSkillImpl::new(skill_id))),
        SJ_LIGHTOFSUN => Some(Box::new(StatusSkillImpl::new(skill_id))),
        SJ_LUNARSTANCE => Some(Box::new(StatusSkillImpl::new_toggle(skill_id, true))),
        SJ_NEWMOONKICK => Some(Box::new(newmoonkick::SkillNewMoonKick)),
        SJ_NOVAEXPLOSING => Some(Box::new(novaexplosion::SkillNovaExplosion)),
        SJ_PROMINENCEKICK => Some(Box::new(prominencekick::SkillProminenceKick)),
        SJ_SOLARBURST => Some(Box::new(solarburst::SkillSolarBurst)),
        SJ_STAREMPEROR => Some(Box::new(staremperoradvent::SkillStarEmperorAdvent)),
        SJ_STARSTANCE => Some(Box::new(StatusSkillImpl::new_toggle(skill_id, true))),
        SJ_SUNSTANCE => Some(Box::new(StatusSkillImpl::new_toggle(skill_id, true))),
        SJ_UNIVERSESTANCE => Some(Box::new(StatusSkillImpl::new_toggle(skill_id, true))),
        SKE_ALL_IN_THE_SKY => Some(Box::new(allinthesky::SkillAllInTheSky)),
        SKE_DAWN_BREAK => Some(Box::new(dawnbreak::SkillDawnBreak)),
        SKE_ENCHANTING_SKY => Some(Box::new(StatusSkillImpl::new(skill_id))),
        SKE_MIDNIGHT_KICK => Some(Box::new(midnightkick::SkillMidnightKick)),
        SKE_NOON_BLAST => Some(Box::new(noonblast::SkillNoonBlast)),
        SKE_RISING_MOON => Some(Box::new(risingmoon::SkillRisingMoon)),
        SKE_RISING_SUN => Some(Box::new(risingsun::SkillRisingSun)),
        SKE_SKY_MOON => Some(Box::new(skymoon::SkillSkyMoon)),
        SKE_SKY_SUN => Some(Box::new(skysun::SkillSkySun)),
        SKE_STAR_BURST => Some(Box::new(starburst::SkillStarBurst)),
        SKE_STAR_CANNON => Some(Box::new(starcannon::SkillStarCannon)),
        SKE_STAR_LIGHT_KICK => Some(Box::new(starlightkick::SkillStarLightKick)),
        SKE_SUNSET_BLAST => Some(Box::new(sunsetblast::SkillSunsetBlast)),
        SKE_TWINKLING_GALAXY => Some(Box::new(twinklinggalaxy::SkillTwinklingGalaxy)),
        SL_ALCHEMIST => Some(Box::new(spiritofthealchemist::SkillSpiritoftheAlchemist)),
        SL_ASSASIN => Some(Box::new(spiritoftheassasin::SkillSpiritoftheAssasin)),
        SL_BARDDANCER => Some(Box::new(spiritoftheartist::SkillSpiritoftheArtist)),
        SL_BLACKSMITH => Some(Box::new(spiritoftheblacksmith::SkillSpiritoftheBlacksmith)),
        SL_CRUSADER => Some(Box::new(spiritofthecrusader::SkillSpiritoftheCrusader)),
        SL_HIGH => Some(Box::new(spiritofrebirth::SkillSpiritofRebirth)),
        SL_HUNTER => Some(Box::new(spiritofthehunter::SkillSpiritoftheHunter)),
        SL_KAAHI => Some(Box::new(kaahi::SkillKaahi)),
        SL_KAITE => Some(Box::new(kaite::SkillKaite)),
        SL_KAIZEL => Some(Box::new(kaizel::SkillKaizel)),
        SL_KAUPE => Some(Box::new(kaupe::SkillKaupe)),
        SL_KNIGHT => Some(Box::new(spiritoftheknight::SkillSpiritoftheKnight)),
        SL_MONK => Some(Box::new(spiritofthemonk::SkillSpiritoftheMonk)),
        SL_PRIEST => Some(Box::new(spiritofthepriest::SkillSpiritofthePriest)),
        SL_ROGUE => Some(Box::new(spiritoftherogue::SkillSpiritoftheRogue)),
        SL_SAGE => Some(Box::new(spiritofthesage::SkillSpiritoftheSage)),
        SL_SKA => Some(Box::new(eska::SkillEska)),
        SL_SKE => Some(Box::new(eske::SkillEske)),
        SL_SMA => Some(Box::new(esma::SkillEsma)),
        SL_SOULLINKER => Some(Box::new(spiritofthesoullinker::SkillSpiritoftheSoulLinker)),
        SL_STAR => Some(Box::new(spiritofthestargladiator::SkillSpiritoftheStarGladiator)),
        SL_STIN => Some(Box::new(estin::SkillEstin)),
        SL_STUN => Some(Box::new(estun::SkillEstun)),
        SL_SUPERNOVICE => Some(Box::new(spiritofthesupernovice::SkillSpiritoftheSupernovice)),
        SL_SWOO => Some(Box::new(eswoo::SkillEswoo)),
        SL_WIZARD => Some(Box::new(spiritofthewizard::SkillSpiritoftheWizard)),
        SOA_CIRCLE_OF_DIRECTIONS_AND_ELEMENTALS => Some(Box::new(circleofdirectionsandelementals::SkillCircleOfDirectionsAndElementals)),
        SOA_EXORCISM_OF_MALICIOUS_SOUL => Some(Box::new(exorcismofmalicioussoul::SkillExorcismOfMaliciousSoul)),
        SOA_SOUL_GATHERING => Some(Box::new(soulgathering::SkillSoulGathering)),
        SOA_SOUL_OF_HEAVEN_AND_EARTH => Some(Box::new(soulofheavenandearth::SkillSoulOfHeavenAndEarth)),
        SOA_TALISMAN_OF_BLACK_TORTOISE => Some(Box::new(talismanofblacktortoise::SkillTalismanOfBlackTortoise)),
        SOA_TALISMAN_OF_BLUE_DRAGON => Some(Box::new(talismanofbluedragon::SkillTalismanOfBlueDragon)),
        SOA_TALISMAN_OF_FIVE_ELEMENTS => Some(Box::new(talismanoffiveelements::SkillTalismanOfFiveElements)),
        SOA_TALISMAN_OF_FOUR_BEARING_GOD => Some(Box::new(talismanoffourbearinggod::SkillTalismanOfFourBearingGod)),
        SOA_TALISMAN_OF_MAGICIAN => Some(Box::new(talismanofmagician::SkillTalismanOfMagician)),
        SOA_TALISMAN_OF_PROTECTION => Some(Box::new(talismanofprotection::SkillTalismanOfProtection)),
        SOA_TALISMAN_OF_RED_PHOENIX => Some(Box::new(talismanofredphoenix::SkillTalismanOfRedPhoenix)),
        SOA_TALISMAN_OF_SOUL_STEALING => Some(Box::new(talismanofsoulstealing::SkillTalismanOfSoulStealing)),
        SOA_TALISMAN_OF_WARRIOR => Some(Box::new(talismanofwarrior::SkillTalismanOfWarrior)),
        SOA_TALISMAN_OF_WHITE_TIGER => Some(Box::new(talismanofwhitetiger::SkillTalismanOfWhiteTiger)),
        SOA_TOTEM_OF_TUTELARY => Some(Box::new(totemoftutelary::SkillTotemOfTutelary)),
        SP_CURSEEXPLOSION => Some(Box::new(curseexplosion::SkillCurseExplosion)),
        SP_KAUTE => Some(Box::new(kaute::SkillKaute)),
        SP_SHA => Some(Box::new(esha::SkillEsha)),
        SP_SOULCOLLECT => Some(Box::new(soulcollect::SkillSoulCollect)),
        SP_SOULCURSE => Some(Box::new(soulcurse::SkillSoulCurse)),
        SP_SOULDIVISION => Some(Box::new(souldivision::SkillSoulDivision)),
        SP_SOULEXPLOSION => Some(Box::new(soulexplosion::SkillSoulExplosion)),
        SP_SOULFAIRY => Some(Box::new(fairyssoul::SkillFairysSoul)),
        SP_SOULFALCON => Some(Box::new(falconssoul::SkillFalconsSoul)),
        SP_SOULGOLEM => Some(Box::new(golemssoul::SkillGolemsSoul)),
        SP_SOULREAPER => Some(Box::new(StatusSkillImpl::new(skill_id))),
        SP_SOULREVOLVE => Some(Box::new(soulrevolution::SkillSoulRevolution)),
        SP_SOULSHADOW => Some(Box::new(shadowssoul::SkillShadowsSoul)),
        SP_SOULUNITY => Some(Box::new(soulunity::SkillSoulUnity)),
        SP_SPA => Some(Box::new(espa::SkillEspa)),
        SP_SWHOO => Some(Box::new(eswhoo::SkillEswhoo)),
        TK_COUNTER => Some(Box::new(counter::SkillCounter)),
        TK_DODGE => Some(Box::new(StatusSkillImpl::new_toggle(skill_id, true))),
        TK_DOWNKICK => Some(Box::new(downkick::SkillDownKick)),
        TK_HIGHJUMP => Some(Box::new(highjump::SkillHighJump)),
        TK_JUMPKICK => Some(Box::new(jumpkick::SkillJumpKick)),
        TK_MISSION => Some(Box::new(mission::SkillMission)),
        TK_READYCOUNTER => Some(Box::new(StatusSkillImpl::new_toggle(skill_id, true))),
        TK_READYDOWN => Some(Box::new(StatusSkillImpl::new_toggle(skill_id, true))),
        TK_READYSTORM => Some(Box::new(StatusSkillImpl::new_toggle(skill_id, true))),
        TK_READYTURN => Some(Box::new(StatusSkillImpl::new_toggle(skill_id, true))),
        TK_RUN => Some(Box::new(run::SkillRun)),
        TK_SEVENWIND => Some(Box::new(sevenwind::SkillSevenWind)),
        TK_STORMKICK => Some(Box::new(stormkick::SkillStormKick)),
        TK_TURNKICK => Some(Box::new(turnkick::SkillTurnKick)),
        _ => None,
    }
}

// ---------------------------------------------------------------------------------------------- battle_config

/// conf/battle/skill.conf `allow_es_magic_player: no` (battle.cpp: battle_config.allow_es_magic_pc).
pub fn allow_es_magic_pc() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::ALLOW_ES_MAGIC_PLAYER) != 0
}

// ---------------------------------------------------------------------------------------------- shared helpers

/// The target rule of the Soul Linker's party-less support spells, written out in each of kaahi.cpp, kaite.cpp,
/// kaizel.cpp, kaupe.cpp and kaute.cpp (`if (sd) { if (!dstsd || !(…)) { stun; fail; return; } }`): true when the
/// caster may go on. Only a player caster is checked; a player target passes when the caster is soul linked by a
/// Soul Linker (SC_SPIRIT val2 == SL_SOULLINKER), the target is a Soul Linker, the caster itself, the caster's partner
/// or child, or (Kaute only: `soulunity`) is in Soul Unity.
pub(crate) fn soul_linker_target_ok(c: &Ctx, src: &Unit, target: &Unit, soulunity: bool) -> bool {
    use crate::db::jobs::ids::{MAPID_SECONDMASK, MAPID_SOUL_LINKER};
    use crate::status::consts::{SC_SOULUNITY, SC_SPIRIT};
    if src.kind != BL_PC {
        return true;
    }
    if target.kind != BL_PC {
        return false; // !dstsd
    }
    let Some(sd_ch) = crate::pc::character_of(c, src.id) else { return true };
    let Some(dst_ch) = crate::pc::character_of(c, target.id) else { return false };
    let dst_class = crate::status::data::sd_get(c, target.id).map(|s| s.class_).unwrap_or(0);
    crate::status::sc::get_sce(c, src.id, SC_SPIRIT).map(|e| e.val2 == SL_SOULLINKER as i32).unwrap_or(false)
        || (dst_class & MAPID_SECONDMASK) == MAPID_SOUL_LINKER
        || dst_ch.id == sd_ch.id
        || dst_ch.id == sd_ch.partner_id
        || dst_ch.id == sd_ch.child
        || (soulunity && crate::status::sc::has_sce(c, target.id, SC_SOULUNITY))
}

/// The body every Soul Linker spirit class shares (spiritofthealchemist.cpp … spiritofthewizard.cpp, spiritofrebirth.cpp):
/// `sc_start2(src, target, skill_get_sc(id), 100, skill_lv, id, skill_get_time(id, skill_lv))`; on success the
/// nodamage packet, `between` (Spirit of the Super Novice's death-counter roll) and the caster's own SC_SMA
/// (skill_get_time(SL_SMA, skill_lv)); on failure clif_skill_fail(sd, id) for a player caster.
pub(crate) fn spirit_castend_nodamage_id(c: &Ctx, id: u16, src: &Unit, target: &Unit, skill_lv: u16, between: impl FnOnce(&Ctx)) {
    use crate::status::consts::SC_SMA;
    use crate::status::sc;
    let type_ = skill_get_sc(id) as i32;
    if sc::sc_start2(c, Some(src), target, type_, 100, skill_lv as i32, id as i32, skill_get_time(id, skill_lv) as i64) {
        let t = crate::unit::get(c, target.id).unwrap_or_else(|| target.clone());
        crate::skill::clif::clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, true);
        between(c);
        let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
        sc::sc_start(c, Some(&s), &s, SC_SMA, 100, skill_lv as i32, skill_get_time(SL_SMA, skill_lv) as i64);
    } else if src.kind == BL_PC {
        crate::skill::clif::skill_fail(c, src, id);
    }
}

/// `sd->stellar_mark` / `sd->united_soul` ARR_FIND helpers: the index of `id` in `list[..n]`, else n.
pub(crate) fn arr_find(list: &[u32], n: usize, id: u32) -> usize {
    list.iter().take(n).position(|&x| x == id).unwrap_or(n)
}

use crate::map::BL_PC;
use crate::unit::Unit;
use crate::world::Ctx;

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn conf_matches_the_reference_server() {
        let flag = |name: &str| crate::script::battle_flags::BATTLE_FLAGS.iter().find(|(n, _)| *n == name).map(|(_, v)| *v as i64).unwrap();
        assert_eq!(flag("allow_es_magic_player"), allow_es_magic_pc() as i64);
    }
}
