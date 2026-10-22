//! skills/summoner/skill_factory_summoner.cpp: SkillFactorySummoner::create and the summoner family's classes (rAthena
//! e985006): Summoner (Doram, SU_*) and Spirit Handler (SH_*), one file per class as rAthena has them
//! (catnipmeteor.rs, lunaticcarrotbeat.rs and pickypeck.rs hold two classes each, as their .cpp files do).
//!
//! The end of this file holds the small helpers the class files repeat in rAthena (the "commune" test of the Spirit
//! Handler skills, the Colors of Hyun Rok element switch, `BL_CAST(BL_PC, src)->status.party_id`).

use super::{SkillImpl, StatusSkillImpl};
use crate::db::skills::*;
use crate::unit::Unit;
use crate::world::Ctx;

pub mod bite;
pub mod blessingofmysticalcreatures;
pub mod bunchofshrimp;
pub mod catnipmeteor;
pub mod catnippowdering;
pub mod chattering;
pub mod chulhobattering;
pub mod chulhosonicclaw;
pub mod colorsofhyunrok;
pub mod grooming;
pub mod hiss;
pub mod hogogongstrike;
pub mod howlingofchulho;
pub mod hyunrokbreeze;
pub mod hyunrokcannon;
pub mod hyunrokspiritpower;
pub mod kisulrampage;
pub mod kisulwaterspraying;
pub mod lope;
pub mod lunaticcarrotbeat;
pub mod marinefestivalofkisul;
pub mod meowmeow;
pub mod nyanggrass;
pub mod pickypeck;
pub mod powerofflock;
pub mod purring;
pub mod sandyfestivalofkisul;
pub mod scaroftarou;
pub mod scratch;
pub mod silvervineroottwist;
pub mod silvervinestemspear;
pub mod spiritofsavage;
pub mod tastyshrimpparty;
pub mod tunabelly;
pub mod tunaparty;

/// SkillFactorySummoner::create.
pub fn create(skill_id: u16) -> Option<Box<dyn SkillImpl>> {
    // std::make_unique<StatusSkillImpl>(skill_id) / (skill_id, true)
    let status = |end_if_running: bool| -> Option<Box<dyn SkillImpl>> { Some(Box::new(StatusSkillImpl::new_toggle(skill_id, end_if_running))) };
    match skill_id {
        SH_BLESSING_OF_MYSTICAL_CREATURES => Some(Box::new(blessingofmysticalcreatures::SkillBlessingofMysticalCreatures)),
        SH_CHUL_HO_BATTERING => Some(Box::new(chulhobattering::SkillChulhoBattering)),
        SH_CHUL_HO_SONIC_CLAW => Some(Box::new(chulhosonicclaw::SkillChulhoSonicClaw)),
        SH_COLORS_OF_HYUN_ROK => Some(Box::new(colorsofhyunrok::SkillColorsofHyunrok)),
        SH_HOGOGONG_STRIKE => Some(Box::new(hogogongstrike::SkillHogogongStrike)),
        SH_HOWLING_OF_CHUL_HO => Some(Box::new(howlingofchulho::SkillHowlingofChulho)),
        SH_HYUN_ROKS_BREEZE => Some(Box::new(hyunrokbreeze::SkillHyunrokBreeze)),
        SH_HYUN_ROK_CANNON => Some(Box::new(hyunrokcannon::SkillHyunrokCannon)),
        SH_HYUN_ROK_SPIRIT_POWER => Some(Box::new(hyunrokspiritpower::SkillHyunrokSpiritPower)),
        SH_KI_SUL_RAMPAGE => Some(Box::new(kisulrampage::SkillKisulRampage)),
        SH_KI_SUL_WATER_SPRAYING => Some(Box::new(kisulwaterspraying::SkillKisulWaterSpraying)),
        SH_MARINE_FESTIVAL_OF_KI_SUL => Some(Box::new(marinefestivalofkisul::SkillMarineFestivalofKisul)),
        SH_SANDY_FESTIVAL_OF_KI_SUL => Some(Box::new(sandyfestivalofkisul::SkillSandyFestivalofKisul)),
        SH_TEMPORARY_COMMUNION => status(false),
        SU_ARCLOUSEDASH => status(false),
        SU_BITE => Some(Box::new(bite::SkillBite)),
        SU_BUNCHOFSHRIMP => Some(Box::new(bunchofshrimp::SkillBunchofShrimp)),
        SU_CHATTERING => Some(Box::new(chattering::SkillChattering)), // FIX ME: this skill seems to be StatusSkillImpl
        SU_CN_METEOR => Some(Box::new(catnipmeteor::SkillCatnipMeteor)),
        SU_CN_METEOR2 => Some(Box::new(catnipmeteor::SkillCatnipMeteor2)),
        SU_CN_POWDERING => Some(Box::new(catnippowdering::SkillCatnipPowdering)),
        SU_FRESHSHRIMP => status(false),
        SU_GROOMING => Some(Box::new(grooming::SkillGrooming)), // FIX ME: this skill seems to be StatusSkillImpl
        SU_HIDE => status(true),
        SU_HISS => Some(Box::new(hiss::SkillHiss)),
        SU_LOPE => Some(Box::new(lope::SkillLope)),
        SU_LUNATICCARROTBEAT => Some(Box::new(lunaticcarrotbeat::SkillLunaticCarrotBeat)),
        SU_LUNATICCARROTBEAT2 => Some(Box::new(lunaticcarrotbeat::SkillLunaticCarrotBeat2)),
        SU_MEOWMEOW => Some(Box::new(meowmeow::SkillMeowMeow)),
        SU_NYANGGRASS => Some(Box::new(nyanggrass::SkillNyangGrass)),
        SU_PICKYPECK => Some(Box::new(pickypeck::SkillPickyPeck)),
        SU_PICKYPECK_DOUBLE_ATK => Some(Box::new(pickypeck::SkillPickyPeckDoubleAttack)),
        SU_POWEROFFLOCK => Some(Box::new(powerofflock::SkillPowerofFlock)),
        SU_PURRING => Some(Box::new(purring::SkillPurring)),
        SU_SCAROFTAROU => Some(Box::new(scaroftarou::SkillScarofTarou)),
        SU_SCRATCH => Some(Box::new(scratch::SkillScratch)),
        SU_SHRIMPARTY => Some(Box::new(tastyshrimpparty::SkillTastyShrimpParty)),
        SU_STOOP => status(false),
        SU_SVG_SPIRIT => Some(Box::new(spiritofsavage::SkillSpiritofSavage)),
        SU_SV_ROOTTWIST => Some(Box::new(silvervineroottwist::SkillSilvervineRootTwist)),
        SU_SV_STEMSPEAR => Some(Box::new(silvervinestemspear::SkillSilvervineStemSpear)),
        SU_TUNABELLY => Some(Box::new(tunabelly::SkillTunaBelly)),
        SU_TUNAPARTY => Some(Box::new(tunaparty::SkillTunaParty)),
        _ => None,
    }
}

// ---------------------------------------------------------------------------------------------- shared helpers

/// `pc_checkskill( sd, commune ) > 0 || ( sc != nullptr && sc->getSCE( SC_TEMPORARY_COMMUNION ) != nullptr )`: the
/// Spirit Handler skills' bonus when the caster has learnt the spirit's Commune skill or is under Temporary Communion
/// (sd = BL_CAST(BL_PC, src): 0 for a non-player).
pub(crate) fn communes(c: &Ctx, src: &Unit, commune: u16) -> bool {
    use crate::skills::novice::{pc_checkskill, sce};
    pc_checkskill(c, src, commune) > 0 || sce(c, src, crate::status::consts::SC_TEMPORARY_COMMUNION).is_some()
}

/// The modifyElement override the Hyun Rok skills repeat (catnipmeteor.cpp, hyunrokbreeze.cpp, hyunrokcannon.cpp,
/// hyunrokspiritpower.cpp): the first Colors of Hyun Rok status the caster has sets the element.
pub(crate) fn colors_of_hyun_rok_element(c: &Ctx, src: &Unit, element: &mut i32) {
    use crate::status::consts::*;
    use crate::status::defs::{ELE_DARK, ELE_EARTH, ELE_FIRE, ELE_HOLY, ELE_WATER, ELE_WIND};
    // status_change* sc = status_get_sc(&src); if( sc != nullptr && !sc->empty() )
    if !crate::battle::mobif::has_sc(src.kind) {
        return;
    }
    let has = |t: i32| crate::status::sc::has_sce(c, src.id, t);
    if has(SC_COLORS_OF_HYUN_ROK_1) {
        *element = ELE_WATER;
    } else if has(SC_COLORS_OF_HYUN_ROK_2) {
        *element = ELE_WIND;
    } else if has(SC_COLORS_OF_HYUN_ROK_3) {
        *element = ELE_EARTH;
    } else if has(SC_COLORS_OF_HYUN_ROK_4) {
        *element = ELE_FIRE;
    } else if has(SC_COLORS_OF_HYUN_ROK_5) {
        *element = ELE_DARK;
    } else if has(SC_COLORS_OF_HYUN_ROK_6) {
        *element = ELE_HOLY;
    }
}

/// `map_session_data* sd = BL_CAST(BL_PC, src)`: the player's saved status (sd->status: party_id, base_level …),
/// None when `src` is not a player.
pub(crate) fn sd_status(c: &Ctx, src: &Unit) -> Option<crate::character::Character> {
    if src.kind != crate::map::BL_PC {
        return None;
    }
    crate::pc::character_of(c, src.id)
}

/// `sd == nullptr || sd->status.party_id == 0 || flag&1`: the target branch of the party skills (the party branch
/// runs party_foreachsamemap otherwise).
pub(crate) fn solo_branch(c: &Ctx, src: &Unit, flag: i32) -> bool {
    match sd_status(c, src) {
        None => true,
        Some(ch) => ch.party_id == 0 || flag & 1 != 0,
    }
}

/// `skillratio += skillratio * status_get_hp(src) / status_get_max_hp(src)` (the Spirit of Life bonus): an int32 times
/// the uint32 HP is computed in uint32 (it wraps as rAthena's does), divided by the uint32 max HP and added back to the
/// int32. A zero max HP (never for a unit on the map) adds nothing instead of rAthena's division by zero.
pub(crate) fn add_hp_ratio(c: &Ctx, src: &Unit, skillratio: &mut i32) {
    let st = crate::status::status_get_status_data(c, src);
    let add = (*skillratio as u32).wrapping_mul(st.hp).checked_div(st.max_hp).unwrap_or(0);
    *skillratio = skillratio.wrapping_add(add as i32);
}
