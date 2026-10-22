//! skills/archer/skill_factory_archer.cpp: SkillFactoryArcher::create and the archer family's classes (rAthena
//! e985006: Archer, Hunter, Bard, Dancer, Sniper, Clown, Gypsy, Ranger, Minstrel, Wanderer, Troubadour, Trouvere,
//! Windhawk), one file per class as rAthena has them (roseblossom.rs and severerainstorm.rs hold two classes each, as
//! their .cpp files do).
//!
//! The end of this file holds the bodies that several of the family's .cpp files repeat word for word (the Minstrel /
//! Wanderer party songs, the Troubadour / Trouvere songs); each class file says which one it uses.

use super::{SkillImpl, StatusSkillImpl};
use crate::db::skills::*;
use crate::skill::area::{foreach_in_range, skill_area_sub, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::clif_skill_nodamage;
use crate::skills::novice::{party_foreachsamemap_area_sub, pc_checkskill, sce};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

pub mod acousticrhythm;
pub mod aimedbolt;
pub mod ainrhapsody;
pub mod amp;
pub mod anklesnare;
pub mod arrowshower;
pub mod arrowstorm;
pub mod battletheme;
pub mod beaststrafing;
pub mod blastmine;
pub mod blitzbeat;
pub mod camouflage;
pub mod chargearrow;
pub mod circleofnaturessound;
pub mod classicalpluck;
pub mod claymoretrap;
pub mod clusterbomb;
pub mod cobalttrap;
pub mod concentration;
pub mod crescivebolt;
pub mod dancewithawarg;
pub mod dazzler;
pub mod deepblindtrap;
pub mod deepsleeplullaby;
pub mod detect;
pub mod detonator;
pub mod dominionimpulse;
pub mod doublestrafe;
pub mod downtempo;
pub mod echosong;
pub mod electricshocker;
pub mod encore;
pub mod falconassault;
pub mod fearbreeze;
pub mod firingtrap;
pub mod flametrap;
pub mod flasher;
pub mod focusballet;
pub mod focusedarrowstrike;
pub mod freezingtrap;
pub mod friggssong;
pub mod galestorm;
pub mod geffenianocturn;
pub mod gloomyday;
pub mod greatecho;
pub mod gypsyskiss;
pub mod harmoniclick;
pub mod harmonize;
pub mod hawkboomerang;
pub mod hawkmastery;
pub mod hawkrush;
pub mod hipshaker;
pub mod iceboundtrap;
pub mod impressiveriff;
pub mod improvisedsong;
pub mod jawaiiserenade;
pub mod ladyluck;
pub mod landmine;
pub mod leradsdew;
pub mod longingforfreedom;
pub mod lullaby;
pub mod magentatrap;
pub mod magicstrings;
pub mod maizetrap;
pub mod makingarrow;
pub mod marionettecontrol;
pub mod melodyofsink;
pub mod melodystrike;
pub mod mentalsensing;
pub mod metallicfury;
pub mod metallicsound;
pub mod moonlitserenade;
pub mod musicalinterlude;
pub mod nipelheimrequiem;
pub mod pangvoice;
pub mod perfecttablature;
pub mod phantasmicarrow;
pub mod poemofthenetherworld;
pub mod powerchord;
pub mod pronmarch;
pub mod removetrap;
pub mod retrospection;
pub mod reverberation;
pub mod rhythmicalwave;
pub mod rhythmshooting;
pub mod rokicapriccio;
pub mod roseblossom;
pub mod sandman;
pub mod saturdaynightfever;
pub mod sensitivekeen;
pub mod severerainstorm;
pub mod shelteringbliss;
pub mod shockwavetrap;
pub mod skidtrap;
pub mod skilledspecialsinger;
pub mod slingingarrow;
pub mod slowgrace;
pub mod solidtrap;
pub mod songoflutie;
pub mod songofmana;
pub mod soundblend;
pub mod soundofdestruction;
pub mod springtrap;
pub mod swifttrap;
pub mod swingdance;
pub mod symphonyoflovers;
pub mod talkiebox;
pub mod tarotcardoffate;
pub mod unbarringoctave;
pub mod unchainedserenade;
pub mod unlimitedhummingvoice;
pub mod valleyofdeath;
pub mod verduretrap;
pub mod voiceofsiren;
pub mod vulcanarrow;
pub mod wandofhermode;
pub mod warcryofbeyond;
pub mod wargbite;
pub mod wargdash;
pub mod wargmastery;
pub mod wargrider;
pub mod wargstrike;
pub mod wildwalk;
pub mod windmillrushattack;
pub mod windwalker;
pub mod winkofcharm;

/// SkillFactoryArcher::create (the switch in rAthena's order).
pub fn create(skill_id: u16) -> Option<Box<dyn SkillImpl>> {
    // std::make_unique<StatusSkillImpl>(skill_id)
    let status = || -> Option<Box<dyn SkillImpl>> { Some(Box::new(StatusSkillImpl::new(skill_id))) };
    match skill_id {
        AC_CHARGEARROW => Some(Box::new(chargearrow::SkillChargeArrow)),
        AC_CONCENTRATION => Some(Box::new(concentration::SkillConcentration)),
        AC_DOUBLE => Some(Box::new(doublestrafe::SkillDoubleStrafe)),
        AC_MAKINGARROW => Some(Box::new(makingarrow::SkillMakingArrow)),
        AC_SHOWER => Some(Box::new(arrowshower::SkillArrowShower)),
        BA_APPLEIDUN => Some(Box::new(songoflutie::SkillSongofLutie)),
        BA_ASSASSINCROSS => Some(Box::new(impressiveriff::SkillImpressiveRiff)),
        BA_DISSONANCE => Some(Box::new(unchainedserenade::SkillUnchainedSerenade)),
        BA_FROSTJOKER => Some(Box::new(unbarringoctave::SkillUnbarringOctave)),
        BA_MUSICALSTRIKE => Some(Box::new(melodystrike::SkillMelodyStrike)),
        BA_PANGVOICE => Some(Box::new(pangvoice::SkillPangVoice)),
        BA_POEMBRAGI => Some(Box::new(magicstrings::SkillMagicStrings)),
        BA_WHISTLE => Some(Box::new(perfecttablature::SkillPerfectTablature)),
        BD_ADAPTATION => Some(Box::new(amp::SkillAmp)),
        BD_DRUMBATTLEFIELD => Some(Box::new(battletheme::SkillBattleTheme)),
        BD_ENCORE => Some(Box::new(encore::SkillEncore)),
        BD_ETERNALCHAOS => Some(Box::new(downtempo::SkillDownTempo)),
        BD_INTOABYSS => Some(Box::new(powerchord::SkillPowerChord)),
        BD_LULLABY => Some(Box::new(lullaby::SkillLullaby)),
        BD_RICHMANKIM => Some(Box::new(mentalsensing::SkillMentalSensing)),
        BD_RINGNIBELUNGEN => Some(Box::new(harmoniclick::SkillHarmonicLick)),
        BD_ROKISWEIL => Some(Box::new(classicalpluck::SkillClassicalPluck)),
        BD_SIEGFRIED => Some(Box::new(acousticrhythm::SkillAcousticRhythm)),
        CG_ARROWVULCAN => Some(Box::new(vulcanarrow::SkillVulcanArrow)),
        CG_HERMODE => Some(Box::new(wandofhermode::SkillWandOfHermode)),
        CG_LONGINGFREEDOM => Some(Box::new(longingforfreedom::SkillLongingForFreedom)),
        CG_MARIONETTE => Some(Box::new(marionettecontrol::SkillMarionetteControl)),
        CG_MOONLIT => Some(Box::new(shelteringbliss::SkillShelteringBliss)),
        CG_SPECIALSINGER => Some(Box::new(skilledspecialsinger::SkillSkilledSpecialSinger)),
        CG_TAROTCARD => Some(Box::new(tarotcardoffate::SkillTarotCardOfFate)),
        DC_DONTFORGETME => Some(Box::new(slowgrace::SkillSlowGrace)),
        DC_FORTUNEKISS => Some(Box::new(ladyluck::SkillLadyLuck)),
        DC_HUMMING => Some(Box::new(focusballet::SkillFocusBallet)),
        DC_SCREAM => Some(Box::new(dazzler::SkillDazzler)),
        DC_SERVICEFORYOU => Some(Box::new(gypsyskiss::SkillGypsysKiss)),
        DC_THROWARROW => Some(Box::new(slingingarrow::SkillSlingingArrow)),
        DC_UGLYDANCE => Some(Box::new(hipshaker::SkillHipShaker)),
        DC_WINKCHARM => Some(Box::new(winkofcharm::SkillWinkofCharm)),
        HT_ANKLESNARE => Some(Box::new(anklesnare::SkillAnkleSnare)),
        HT_BLASTMINE => Some(Box::new(blastmine::SkillBlastMine)),
        HT_BLITZBEAT => Some(Box::new(blitzbeat::SkillBlitzBeat)),
        HT_CLAYMORETRAP => Some(Box::new(claymoretrap::SkillClaymoreTrap)),
        HT_DETECTING => Some(Box::new(detect::SkillDetect)),
        HT_FLASHER => Some(Box::new(flasher::SkillFlasher)),
        HT_FREEZINGTRAP => Some(Box::new(freezingtrap::SkillFreezingTrap)),
        HT_LANDMINE => Some(Box::new(landmine::SkillLandMine)),
        HT_PHANTASMIC => Some(Box::new(phantasmicarrow::SkillPhantasmicArrow)),
        HT_POWER => Some(Box::new(beaststrafing::SkillBeastStrafing)),
        HT_REMOVETRAP => Some(Box::new(removetrap::SkillRemoveTrap)),
        HT_SANDMAN => Some(Box::new(sandman::SkillSandman)),
        HT_SHOCKWAVE => Some(Box::new(shockwavetrap::SkillShockwaveTrap)),
        HT_SKIDTRAP => Some(Box::new(skidtrap::SkillSkidTrap)),
        HT_SPRINGTRAP => Some(Box::new(springtrap::SkillSpringTrap)),
        HT_TALKIEBOX => Some(Box::new(talkiebox::SkillTalkieBox)),
        MI_ECHOSONG => Some(Box::new(echosong::SkillEchoSong)),
        MI_HARMONIZE => Some(Box::new(harmonize::SkillHarmonize)),
        MI_RUSH_WINDMILL => Some(Box::new(windmillrushattack::SkillWindmillRushAttack)),
        RA_AIMEDBOLT => Some(Box::new(aimedbolt::SkillAimedBolt)),
        RA_ARROWSTORM => Some(Box::new(arrowstorm::SkillArrowStorm)),
        RA_CAMOUFLAGE => Some(Box::new(camouflage::SkillCamouflage)),
        RA_CLUSTERBOMB => Some(Box::new(clusterbomb::SkillClusterBomb)),
        RA_COBALTTRAP => Some(Box::new(cobalttrap::SkillCobaltTrap)),
        RA_DETONATOR => Some(Box::new(detonator::SkillDetonator)),
        RA_ELECTRICSHOCKER => Some(Box::new(electricshocker::SkillElectricShocker)),
        RA_FEARBREEZE => Some(Box::new(fearbreeze::SkillFearBreeze)),
        RA_FIRINGTRAP => Some(Box::new(firingtrap::SkillFiringTrap)),
        RA_ICEBOUNDTRAP => Some(Box::new(iceboundtrap::SkillIceboundTrap)),
        RA_MAGENTATRAP => Some(Box::new(magentatrap::SkillMagentaTrap)),
        RA_MAIZETRAP => Some(Box::new(maizetrap::SkillMaizeTrap)),
        RA_SENSITIVEKEEN => Some(Box::new(sensitivekeen::SkillSensitiveKeen)),
        RA_UNLIMIT => status(),
        RA_VERDURETRAP => Some(Box::new(verduretrap::SkillVerdureTrap)),
        RA_WUGBITE => Some(Box::new(wargbite::SkillWargBite)),
        RA_WUGDASH => Some(Box::new(wargdash::SkillWargDash)),
        RA_WUGMASTERY => Some(Box::new(wargmastery::SkillWargMastery)),
        RA_WUGRIDER => Some(Box::new(wargrider::SkillWargRider)),
        RA_WUGSTRIKE => Some(Box::new(wargstrike::SkillWargStrike)),
        SN_FALCONASSAULT => Some(Box::new(falconassault::SkillFalconAssault)),
        SN_SHARPSHOOTING => Some(Box::new(focusedarrowstrike::SkillFocusedArrowStrike)),
        SN_SIGHT => status(),
        SN_WINDWALK => Some(Box::new(windwalker::SkillWindWalker)),
        TR_AIN_RHAPSODY => Some(Box::new(ainrhapsody::SkillAinRhapsody)),
        TR_GEF_NOCTURN => Some(Box::new(geffenianocturn::SkillGeffeniaNocturn)),
        TR_JAWAII_SERENADE => Some(Box::new(jawaiiserenade::SkillJawaiiSerenade)),
        TR_KVASIR_SONATA => status(),
        TR_METALIC_FURY => Some(Box::new(metallicfury::SkillMetallicFury)),
        TR_MUSICAL_INTERLUDE => Some(Box::new(musicalinterlude::SkillMusicalInterlude)),
        TR_MYSTIC_SYMPHONY => status(),
        TR_NIPELHEIM_REQUIEM => Some(Box::new(nipelheimrequiem::SkillNipelheimRequiem)),
        TR_PRON_MARCH => Some(Box::new(pronmarch::SkillPronMarch)),
        TR_RETROSPECTION => Some(Box::new(retrospection::SkillRetrospection)),
        TR_RHYTHMICAL_WAVE => Some(Box::new(rhythmicalwave::SkillRhythmicalWave)),
        TR_RHYTHMSHOOTING => Some(Box::new(rhythmshooting::SkillRhythmShooting)),
        TR_ROKI_CAPRICCIO => Some(Box::new(rokicapriccio::SkillRokiCapriccio)),
        TR_ROSEBLOSSOM => Some(Box::new(roseblossom::SkillRoseBlossom)),
        TR_ROSEBLOSSOM_ATK => Some(Box::new(roseblossom::SkillRoseBlossomAttack)),
        TR_SOUNDBLEND => Some(Box::new(soundblend::SkillSoundBlend)),
        WA_MOONLIT_SERENADE => Some(Box::new(moonlitserenade::SkillMoonlitSerenade)),
        WA_SWING_DANCE => Some(Box::new(swingdance::SkillSwingDance)),
        WA_SYMPHONY_OF_LOVER => Some(Box::new(symphonyoflovers::SkillSymphonyOfLovers)),
        WH_CALAMITYGALE => status(),
        WH_CRESCIVE_BOLT => Some(Box::new(crescivebolt::SkillCresciveBolt)),
        WH_DEEPBLINDTRAP => Some(Box::new(deepblindtrap::SkillDeepBlindTrap)),
        WH_FLAMETRAP => Some(Box::new(flametrap::SkillFlameTrap)),
        WH_GALESTORM => Some(Box::new(galestorm::SkillGaleStorm)),
        WH_HAWKBOOMERANG => Some(Box::new(hawkboomerang::SkillHawkBoomerang)),
        WH_HAWKRUSH => Some(Box::new(hawkrush::SkillHawkRush)),
        WH_HAWK_M => Some(Box::new(hawkmastery::SkillHawkMastery)),
        WH_SOLIDTRAP => Some(Box::new(solidtrap::SkillSolidTrap)),
        WH_SWIFTTRAP => Some(Box::new(swifttrap::SkillSwiftTrap)),
        WH_WILD_WALK => Some(Box::new(wildwalk::SkillWildWalk)),
        WH_WIND_SIGN => status(),
        WM_BEYOND_OF_WARCRY => Some(Box::new(warcryofbeyond::SkillWarcryOfBeyond)),
        WM_DANCE_WITH_WUG => Some(Box::new(dancewithawarg::SkillDanceWithAWarg)),
        WM_DEADHILLHERE => Some(Box::new(valleyofdeath::SkillValleyOfDeath)),
        WM_DOMINION_IMPULSE => Some(Box::new(dominionimpulse::SkillDominionImpulse)),
        WM_FRIGG_SONG => Some(Box::new(friggssong::SkillFriggsSong)),
        WM_GLOOMYDAY => Some(Box::new(gloomyday::SkillGloomyDay)),
        WM_GREAT_ECHO => Some(Box::new(greatecho::SkillGreatEcho)),
        WM_LERADS_DEW => Some(Box::new(leradsdew::SkillLeradsDew)),
        WM_LULLABY_DEEPSLEEP => Some(Box::new(deepsleeplullaby::SkillDeepSleepLullaby)),
        WM_MELODYOFSINK => Some(Box::new(melodyofsink::SkillMelodyOfSink)),
        WM_METALICSOUND => Some(Box::new(metallicsound::SkillMetallicSound)),
        WM_POEMOFNETHERWORLD => Some(Box::new(poemofthenetherworld::SkillPoemOfTheNetherworld)),
        WM_RANDOMIZESPELL => Some(Box::new(improvisedsong::SkillImprovisedSong)),
        WM_REVERBERATION => Some(Box::new(reverberation::SkillReverberation)),
        WM_SATURDAY_NIGHT_FEVER => Some(Box::new(saturdaynightfever::SkillSaturdayNightFever)),
        WM_SEVERE_RAINSTORM => Some(Box::new(severerainstorm::SkillSevereRainstorm)),
        WM_SEVERE_RAINSTORM_MELEE => Some(Box::new(severerainstorm::SkillSevereRainstormMelee)),
        WM_SIRCLEOFNATURE => Some(Box::new(circleofnaturessound::SkillCircleOfNaturesSound)),
        WM_SONG_OF_MANA => Some(Box::new(songofmana::SkillSongOfMana)),
        WM_SOUND_OF_DESTRUCTION => Some(Box::new(soundofdestruction::SkillSoundOfDestruction)),
        WM_UNLIMITED_HUMMING_VOICE => Some(Box::new(unlimitedhummingvoice::SkillUnlimitedHummingVoice)),
        WM_VOICEOFSIREN => Some(Box::new(voiceofsiren::SkillVoiceOfSiren)),
        _ => None,
    }
}

// ---------------------------------------------------------------------------------------------- battle_config

/// conf/battle/skill.conf `skill_removetrap_type: 0` (battle_config.skill_removetrap_type: Remove Trap gives back one
/// trap, not every item the skill required).
pub fn skill_removetrap_type() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::SKILL_REMOVETRAP_TYPE) != 0
}
/// conf/battle/skill.conf `tarotcard_equal_chance: no` (battle_config.tarotcard_equal_chance: the official chances).
pub fn tarotcard_equal_chance() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::TAROTCARD_EQUAL_CHANCE) != 0
}

// ---------------------------------------------------------------------------------------------- family helpers

/// The Renewal body of every Bard / Dancer / Clown / Gypsy song class's castendNoDamageId
/// (`#ifdef RENEWAL skill_castend_song(src, getSkillId(), skill_lv, tick); #endif`; skillunit's port). Their
/// castendPos2 is `#ifndef RENEWAL` only (the pre-Renewal song units): an empty body in this build.
pub(crate) fn song_castend_nodamage(c: &Ctx, skill_id: u16, src: &Unit, skill_lv: u16, tick: Tick) {
    crate::skillunit::castend::skill_castend_song(c, src, skill_id, skill_lv, tick);
}

/// The castendPos2 of the trap classes and of the other classes that only place their unit (anklesnare.cpp,
/// blastmine.cpp, … verduretrap.cpp, the Windhawk traps, poemofthenetherworld.cpp, shelteringbliss.cpp):
/// `flag |= 1; // Set flag to 1 to prevent deleting ammo (it will be deleted on group-delete).` then
/// `skill_unitsetting(src, getSkillId(), skill_lv, x, y, 0)` (skillunit's port).
pub(crate) fn unit_castend_pos2(c: &Ctx, skill_id: u16, src: &Unit, x: i16, y: i16, skill_lv: u16, flag: &mut i32) {
    *flag |= 1;
    crate::skill::iface::skill_unitsetting(c, src, skill_id, skill_lv, x, y, 0);
}

/// `map_session_data* sd = BL_CAST(BL_PC, src); … sd == nullptr || sd->status.party_id == 0`: the caster is not a
/// player in a party.
pub(crate) fn no_party(c: &Ctx, src: &Unit) -> bool {
    if src.kind != crate::map::BL_PC {
        return true;
    }
    crate::pc::character_of(c, src.id).map(|ch| ch.party_id == 0).unwrap_or(true)
}

/// `sd->status.job_level` of a player (None when `src` is not one).
pub(crate) fn job_level(c: &Ctx, src: &Unit) -> Option<i32> {
    if src.kind != crate::map::BL_PC {
        return None;
    }
    crate::pc::character_of(c, src.id).map(|ch| ch.job_level as i32)
}

/// A unit as it is now (a call before may have changed it), or the caller's copy when it is gone.
pub(crate) fn fresh(c: &Ctx, u: &Unit) -> Unit {
    crate::unit::get(c, u.id).unwrap_or_else(|| u.clone())
}

/// The castendNoDamageId body that songofmana.cpp, dancewithawarg.cpp, leradsdew.cpp, circleofnaturessound.cpp and
/// unlimitedhummingvoice.cpp each repeat word for word (WM_SONG_OF_MANA, WM_DANCE_WITH_WUG, WM_LERADS_DEW,
/// WM_SIRCLEOFNATURE, WM_UNLIMITED_HUMMING_VOICE):
/// ```text
/// if( flag&1 ) {	// These affect all party members near the caster.
///     if( sc && sc->getSCE(type) )
///         sc_start2(src,target,type,100,skill_lv,pc_checkskill(sd, WM_LESSON),skill_get_time(getSkillId(),skill_lv));
/// } else if( sd ) {
///     if( sc_start2(src,target,type,100,skill_lv,pc_checkskill(sd, WM_LESSON),skill_get_time(getSkillId(),skill_lv)) )
///         party_foreachsamemap(skill_area_sub,sd,skill_get_splash(getSkillId(),skill_lv),src,getSkillId(),skill_lv,tick,flag|BCT_PARTY|1,skill_castend_nodamage_id);
///     clif_skill_nodamage(src,*target,getSkillId(),skill_lv);
/// }
/// ```
/// (`sc` is the caster's status_change; pc_checkskill(nullptr, …) is 0.)
pub(crate) fn wm_party_song(c: &Ctx, skill_id: u16, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
    let type_ = skill_get_sc(skill_id) as i32;
    let time = skill_get_time(skill_id, skill_lv) as i64;
    if flag & 1 != 0 {
        // These affect all party members near the caster.
        if sce(c, src, type_).is_some() {
            sc::sc_start2(c, Some(src), target, type_, 100, skill_lv as i32, pc_checkskill(c, src, WM_LESSON), time);
        }
    } else if src.kind == crate::map::BL_PC {
        if sc::sc_start2(c, Some(src), target, type_, 100, skill_lv as i32, pc_checkskill(c, src, WM_LESSON), time) {
            let s = fresh(c, src);
            party_foreachsamemap_area_sub(c, &s, skill_get_splash(skill_id, skill_lv) as i16, &s, skill_id, skill_lv, tick, flag | crate::battle::BCT_PARTY | 1, skill_castend_nodamage_id);
        }
        clif_skill_nodamage(c, Some(src), &fresh(c, target), skill_id, skill_lv as i32, true);
    }
}

/// The castendNoDamageId body that echosong.cpp, windmillrushattack.cpp, moonlitserenade.cpp, swingdance.cpp and
/// symphonyoflovers.cpp each repeat (MI_ECHOSONG, MI_RUSH_WINDMILL, WA_MOONLIT_SERENADE, WA_SWING_DANCE,
/// WA_SYMPHONY_OF_LOVER):
/// ```text
/// uint16 lesson_lv = (sd != nullptr) ? pc_checkskill(sd, WM_LESSON) : skill_get_max(WM_LESSON);
/// if( sd == nullptr || sd->status.party_id == 0 || (flag & 1) ) {
///     clif_skill_nodamage(src, *target, getSkillId(), skill_lv);
///     sc_start2(src, target, type, 100, skill_lv, lesson_lv, skill_get_time(getSkillId(), skill_lv));
/// } else {
///     party_foreachsamemap(skill_area_sub, sd, skill_get_splash(getSkillId(), skill_lv), src, getSkillId(), skill_lv, tick, flag|BCT_PARTY|1, skill_castend_nodamage_id);
///     sc_start2(src, target, type, 100, skill_lv, lesson_lv, skill_get_time(getSkillId(), skill_lv));
///     clif_skill_nodamage(src, *target, getSkillId(), skill_lv);
/// }
/// ```
/// (moonlitserenade.cpp / swingdance.cpp / symphonyoflovers.cpp write the lesson level inline as
/// `((sd) ? pc_checkskill(sd, WM_LESSON) : skill_get_max(WM_LESSON))`, the same value.)
pub(crate) fn lesson_party_song(c: &Ctx, skill_id: u16, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
    let type_ = skill_get_sc(skill_id) as i32;
    let lesson_lv = if src.kind == crate::map::BL_PC { pc_checkskill(c, src, WM_LESSON) } else { skill_get_max(WM_LESSON) } as u16 as i32;
    let time = skill_get_time(skill_id, skill_lv) as i64;
    if no_party(c, src) || flag & 1 != 0 {
        clif_skill_nodamage(c, Some(src), target, skill_id, skill_lv as i32, true);
        sc::sc_start2(c, Some(src), &fresh(c, target), type_, 100, skill_lv as i32, lesson_lv, time);
    } else {
        party_foreachsamemap_area_sub(c, src, skill_get_splash(skill_id, skill_lv) as i16, src, skill_id, skill_lv, tick, flag | crate::battle::BCT_PARTY | 1, skill_castend_nodamage_id);
        sc::sc_start2(c, Some(src), &fresh(c, target), type_, 100, skill_lv as i32, lesson_lv, time);
        clif_skill_nodamage(c, Some(src), &fresh(c, target), skill_id, skill_lv as i32, true);
    }
}

/// The castendNoDamageId body that jawaiiserenade.cpp, musicalinterlude.cpp and pronmarch.cpp each repeat
/// (TR_JAWAII_SERENADE, TR_MUSICAL_INTERLUDE, TR_PRON_MARCH: party songs):
/// ```text
/// if (sd == nullptr || sd->status.party_id == 0 || (flag & 1))
///     sc_start4(src, bl, skill_get_sc(getSkillId()), 100, skill_lv, 0, flag, 0, skill_get_time(getSkillId(), skill_lv));
/// else if (sd) {
///     clif_skill_nodamage(bl, *bl, getSkillId(), skill_lv);
///     sd->skill_id_song = getSkillId();
///     sd->skill_lv_song = skill_lv;
///     if (skill_check_pc_partner(sd, getSkillId(), &skill_lv, AREA_SIZE, 0) > 0)
///         flag |= 2;
///     party_foreachsamemap(skill_area_sub, sd, skill_get_splash(getSkillId(), skill_lv), src, getSkillId(), skill_lv, tick, flag | BCT_PARTY | 1, skill_castend_nodamage_id);
/// }
/// ```
pub(crate) fn trouvere_party_song(c: &Ctx, skill_id: u16, src: &Unit, bl: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
    if no_party(c, src) || *flag & 1 != 0 {
        sc::sc_start4(c, Some(src), bl, skill_get_sc(skill_id) as i32, 100, skill_lv as i32, 0, *flag, 0, skill_get_time(skill_id, skill_lv) as i64);
    } else {
        let mut skill_lv = skill_lv;
        clif_skill_nodamage(c, Some(bl), bl, skill_id, skill_lv as i32, true);
        song_remember(c, src, skill_id, skill_lv);
        if crate::skill::iface::skill_check_pc_partner(c, src, skill_id, &mut skill_lv, crate::conf::area_size() as i32, 0) > 0 {
            *flag |= 2;
        }
        let s = fresh(c, src);
        party_foreachsamemap_area_sub(c, &s, skill_get_splash(skill_id, skill_lv) as i16, &s, skill_id, skill_lv, tick, *flag | crate::battle::BCT_PARTY | 1, skill_castend_nodamage_id);
    }
}

/// The castendNoDamageId body that ainrhapsody.cpp, geffenianocturn.cpp, nipelheimrequiem.cpp and rokicapriccio.cpp
/// repeat (TR_AIN_RHAPSODY, TR_GEF_NOCTURN, TR_NIPELHEIM_REQUIEM, TR_ROKI_CAPRICCIO: songs on the characters around
/// the caster), with the class's own `flag & 1` part as `on_target(c, src, target, skill_lv, flag)`:
/// ```text
/// if (flag & 1)
///     <on_target>
/// else if (sd) {
///     clif_skill_nodamage(target, *target, getSkillId(), skill_lv);
///     sd->skill_id_song = getSkillId();
///     sd->skill_lv_song = skill_lv;
///     if (skill_check_pc_partner(sd, getSkillId(), &skill_lv, AREA_SIZE, 0) > 0)
///         flag |= 2;
///     map_foreachinallrange(skill_area_sub, src, skill_get_splash(getSkillId(), skill_lv), BL_CHAR, src, getSkillId(), skill_lv, tick, flag | BCT_ENEMY | 1, skill_castend_nodamage_id);
/// }
/// ```
pub(crate) fn trouvere_area_song(c: &Ctx, skill_id: u16, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32, on_target: impl FnOnce(&Ctx, &Unit, &Unit, u16, i32)) {
    if *flag & 1 != 0 {
        on_target(c, src, target, skill_lv, *flag);
    } else if src.kind == crate::map::BL_PC {
        let mut skill_lv = skill_lv;
        clif_skill_nodamage(c, Some(target), target, skill_id, skill_lv as i32, true);
        song_remember(c, src, skill_id, skill_lv);
        if crate::skill::iface::skill_check_pc_partner(c, src, skill_id, &mut skill_lv, crate::conf::area_size() as i32, 0) > 0 {
            *flag |= 2;
        }
        let f = *flag | crate::battle::BCT_ENEMY | 1;
        let s = fresh(c, src);
        foreach_in_range(c, &s, skill_get_splash(skill_id, skill_lv) as i16, crate::map::BL_CHAR, Wall::None, |c, bl| {
            skill_area_sub(c, bl, &s, skill_id, skill_lv, tick, f, skill_castend_nodamage_id)
        });
    }
}

/// `sd->skill_id_song = getSkillId(); sd->skill_lv_song = skill_lv;` (TR_RETROSPECTION casts it again).
fn song_remember(c: &Ctx, src: &Unit, skill_id: u16, skill_lv: u16) {
    crate::skill::state::with(c, src.id, |s| {
        s.skill_id_song = skill_id;
        s.skill_lv_song = skill_lv;
    });
}

/// The monster part of unbarringoctave.cpp / dazzler.cpp castendNoDamageId: "custom hack to make the mob display the
/// skill, because these skills don't show the skill use text themselves" (`snprintf(temp, sizeof(temp) /* 70 */,
/// "%s : %s !!", md->name, skill_get_desc(getSkillId())); clif_disp_overhead(md, temp);`).
pub(crate) fn mob_skill_overhead(c: &Ctx, src: &Unit, skill_id: u16) {
    if src.kind != crate::map::BL_MOB {
        return;
    }
    let Some(md) = crate::mob::get(c, src.id) else { return };
    let mut temp = crate::net::wire::latin1(&format!("{} : {} !!", md.name, skill_get_desc(skill_id)));
    temp.truncate(70 - 1);
    crate::clif_misc::disp_overhead(c, src, &temp, crate::clif::Target::Area);
}

/// `map_session_data* sd = BL_CAST(BL_PC, &src); if (sd != nullptr) element = sd->bonus.arrow_ele;` (the
/// modifyElement of metallicfury.cpp, rhythmicalwave.cpp, soundblend.cpp and reverberation.cpp).
pub(crate) fn arrow_element(c: &Ctx, src: &Unit, element: &mut i32) {
    if src.kind != crate::map::BL_PC {
        return;
    }
    if let Some(sd) = crate::status::data::sd_get(c, src.id) {
        *element = sd.bonus.arrow_ele;
    }
}

#[cfg(test)]
mod tests {
    /// The battle_config values above against the reference server's battle_config table.
    #[test]
    fn battle_config_matches_the_conf() {
        use crate::script::battle_flags::battle_get_value;
        assert_eq!(super::skill_removetrap_type() as i32, battle_get_value("skill_removetrap_type"));
        assert_eq!(super::tarotcard_equal_chance() as i32, battle_get_value("tarotcard_equal_chance"));
        assert_eq!(crate::itemui::skill_drop_items_full() as i32, battle_get_value("skill_drop_items_full"));
    }

    /// Every case label of skill_factory_archer.cpp's switch creates a class for its own skill here, and the family
    /// creates nothing else.
    #[test]
    fn factory_matches_rathena() {
        use crate::db::skills::*;
        let cases: &[u16] = &[
            AC_CHARGEARROW, AC_CONCENTRATION, AC_DOUBLE, AC_MAKINGARROW, AC_SHOWER, BA_APPLEIDUN, BA_ASSASSINCROSS, BA_DISSONANCE, BA_FROSTJOKER,
            BA_MUSICALSTRIKE, BA_PANGVOICE, BA_POEMBRAGI, BA_WHISTLE, BD_ADAPTATION, BD_DRUMBATTLEFIELD, BD_ENCORE, BD_ETERNALCHAOS, BD_INTOABYSS,
            BD_LULLABY, BD_RICHMANKIM, BD_RINGNIBELUNGEN, BD_ROKISWEIL, BD_SIEGFRIED, CG_ARROWVULCAN, CG_HERMODE, CG_LONGINGFREEDOM, CG_MARIONETTE,
            CG_MOONLIT, CG_SPECIALSINGER, CG_TAROTCARD, DC_DONTFORGETME, DC_FORTUNEKISS, DC_HUMMING, DC_SCREAM, DC_SERVICEFORYOU, DC_THROWARROW,
            DC_UGLYDANCE, DC_WINKCHARM, HT_ANKLESNARE, HT_BLASTMINE, HT_BLITZBEAT, HT_CLAYMORETRAP, HT_DETECTING, HT_FLASHER, HT_FREEZINGTRAP,
            HT_LANDMINE, HT_PHANTASMIC, HT_POWER, HT_REMOVETRAP, HT_SANDMAN, HT_SHOCKWAVE, HT_SKIDTRAP, HT_SPRINGTRAP, HT_TALKIEBOX, MI_ECHOSONG,
            MI_HARMONIZE, MI_RUSH_WINDMILL, RA_AIMEDBOLT, RA_ARROWSTORM, RA_CAMOUFLAGE, RA_CLUSTERBOMB, RA_COBALTTRAP, RA_DETONATOR, RA_ELECTRICSHOCKER,
            RA_FEARBREEZE, RA_FIRINGTRAP, RA_ICEBOUNDTRAP, RA_MAGENTATRAP, RA_MAIZETRAP, RA_SENSITIVEKEEN, RA_UNLIMIT, RA_VERDURETRAP, RA_WUGBITE,
            RA_WUGDASH, RA_WUGMASTERY, RA_WUGRIDER, RA_WUGSTRIKE, SN_FALCONASSAULT, SN_SHARPSHOOTING, SN_SIGHT, SN_WINDWALK, TR_AIN_RHAPSODY,
            TR_GEF_NOCTURN, TR_JAWAII_SERENADE, TR_KVASIR_SONATA, TR_METALIC_FURY, TR_MUSICAL_INTERLUDE, TR_MYSTIC_SYMPHONY, TR_NIPELHEIM_REQUIEM,
            TR_PRON_MARCH, TR_RETROSPECTION, TR_RHYTHMICAL_WAVE, TR_RHYTHMSHOOTING, TR_ROKI_CAPRICCIO, TR_ROSEBLOSSOM, TR_ROSEBLOSSOM_ATK, TR_SOUNDBLEND,
            WA_MOONLIT_SERENADE, WA_SWING_DANCE, WA_SYMPHONY_OF_LOVER, WH_CALAMITYGALE, WH_CRESCIVE_BOLT, WH_DEEPBLINDTRAP, WH_FLAMETRAP, WH_GALESTORM,
            WH_HAWKBOOMERANG, WH_HAWKRUSH, WH_HAWK_M, WH_SOLIDTRAP, WH_SWIFTTRAP, WH_WILD_WALK, WH_WIND_SIGN, WM_BEYOND_OF_WARCRY, WM_DANCE_WITH_WUG,
            WM_DEADHILLHERE, WM_DOMINION_IMPULSE, WM_FRIGG_SONG, WM_GLOOMYDAY, WM_GREAT_ECHO, WM_LERADS_DEW, WM_LULLABY_DEEPSLEEP, WM_MELODYOFSINK,
            WM_METALICSOUND, WM_POEMOFNETHERWORLD, WM_RANDOMIZESPELL, WM_REVERBERATION, WM_SATURDAY_NIGHT_FEVER, WM_SEVERE_RAINSTORM,
            WM_SEVERE_RAINSTORM_MELEE, WM_SIRCLEOFNATURE, WM_SONG_OF_MANA, WM_SOUND_OF_DESTRUCTION, WM_UNLIMITED_HUMMING_VOICE, WM_VOICEOFSIREN,
        ];
        assert_eq!(cases.len(), 134, "skill_factory_archer.cpp has 134 case labels");
        for &id in cases {
            let imp = super::create(id).unwrap_or_else(|| panic!("no class for skill {id}"));
            assert_eq!(imp.skill_id(), id);
        }
        for id in 0..=u16::MAX {
            if !cases.contains(&id) {
                assert!(super::create(id).is_none(), "skill {id} is not in skill_factory_archer.cpp");
            }
        }
    }
}
