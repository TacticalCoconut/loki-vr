//! skills/ninja/skill_factory_ninja.cpp: SkillFactoryNinja::create and the ninja family's classes (rAthena
//! e985006), one file per class as rAthena has them (the file names are rAthena's; 16thnight.cpp is the module
//! `sixteenthnight`, a Rust module name cannot start with a digit).

use super::{SkillImpl, StatusSkillImpl};
use crate::db::skills::*;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

#[path = "16thnight.rs"]
pub mod sixteenthnight;
pub mod castninjaspell;
pub mod coldbloodedcannon;
pub mod crimsonfireformation;
pub mod crimsonfirepetal;
pub mod darkdragonnightmare;
pub mod darkeningcannon;
pub mod distortedcrescent;
pub mod earthcharm;
pub mod emptyshadow;
pub mod finalstrike;
pub mod firecharm;
pub mod fourcolorscharm;
pub mod goldendragoncannon;
pub mod hiddenwater;
pub mod huumashurikenconstruct;
pub mod huumashurikengrasp;
pub mod icecharm;
pub mod icemeteor;
pub mod illusionbewitch;
pub mod illusiondeath;
pub mod illusionshadow;
pub mod illusionshock;
pub mod improviseddefense;
pub mod infiltrate;
pub mod kamaitachi;
pub mod kocrossslash;
pub mod kunaidistortion;
pub mod kunaiexplosion;
pub mod kunainightmare;
pub mod kunairefraction;
pub mod kunairotation;
pub mod kunaisplash;
pub mod lightningstrikeofdestruction;
pub mod makibishi;
pub mod meltaway;
pub mod mirage;
pub mod mirrorimage;
pub mod moonlightfantasy;
pub mod nightmareerasion;
pub mod ominousmoonlight;
pub mod ragingfiredragon;
pub mod rapidthrow;
pub mod redflamecannon;
pub mod releaseninjaspell;
pub mod shadowdance;
pub mod shadowflash;
pub mod shadowhiding;
pub mod shadowhunting;
pub mod shadowleap;
pub mod shadownightmare;
pub mod shadowslash;
pub mod shadowtrampling;
pub mod shadowwarrior;
pub mod soulcutter;
pub mod spearofice;
pub mod swirlingpetal;
pub mod throwhuumashuriken;
pub mod throwkunai;
pub mod throwshuriken;
pub mod throwzeny;
pub mod thunderingcannon;
pub mod vanishingslash;
pub mod windblade;
pub mod windcharm;

/// SkillFactoryNinja::create (skill_factory_ninja.cpp), its arms in rAthena's order.
pub fn create(skill_id: u16) -> Option<Box<dyn SkillImpl>> {
    match skill_id {
        KG_KAGEHUMI => Some(Box::new(shadowtrampling::SkillShadowTrampling)),
        KG_KAGEMUSYA => Some(Box::new(shadowwarrior::SkillShadowWarrior)),
        KG_KYOMU => Some(Box::new(emptyshadow::SkillEmptyShadow)),
        KO_BAKURETSU => Some(Box::new(kunaiexplosion::SkillKunaiExplosion)),
        KO_DOHU_KOUKAI => Some(Box::new(earthcharm::SkillEarthCharm)),
        KO_GENWAKU => Some(Box::new(illusionbewitch::SkillIllusionBewitch)),
        KO_HAPPOKUNAI => Some(Box::new(kunaisplash::SkillKunaiSplash)),
        KO_HUUMARANKA => Some(Box::new(swirlingpetal::SkillSwirlingPetal)),
        KO_HYOUHU_HUBUKI => Some(Box::new(icecharm::SkillIceCharm)),
        KO_IZAYOI => Some(Box::new(sixteenthnight::Skill16thNight)),
        KO_JYUMONJIKIRI => Some(Box::new(kocrossslash::SkillKoCrossSlash)),
        KO_JYUSATSU => Some(Box::new(illusiondeath::SkillIllusionDeath)),
        KO_KAHU_ENTEN => Some(Box::new(firecharm::SkillFireCharm)),
        KO_KAIHOU => Some(Box::new(releaseninjaspell::SkillReleaseNinjaSpell)),
        KO_KAZEHU_SEIRAN => Some(Box::new(windcharm::SkillWindCharm)),
        KO_KYOUGAKU => Some(Box::new(illusionshock::SkillIllusionShock)),
        KO_MAKIBISHI => Some(Box::new(makibishi::SkillMakibishi)),
        KO_MEIKYOUSISUI => Some(Box::new(StatusSkillImpl::new(skill_id))),
        KO_MUCHANAGE => Some(Box::new(rapidthrow::SkillRapidThrow)),
        KO_SETSUDAN => Some(Box::new(soulcutter::SkillSoulCutter)),
        KO_YAMIKUMO => Some(Box::new(shadowhiding::SkillShadowHiding)),
        KO_ZANZOU => Some(Box::new(illusionshadow::SkillIllusionShadow)),
        KO_ZENKAI => Some(Box::new(castninjaspell::SkillCastNinjaSpell)),
        NJ_BAKUENRYU => Some(Box::new(ragingfiredragon::SkillRagingFireDragon)),
        NJ_BUNSINJYUTSU => Some(Box::new(mirrorimage::SkillMirrorImage)),
        NJ_HUUJIN => Some(Box::new(windblade::SkillWindBlade)),
        NJ_HUUMA => Some(Box::new(throwhuumashuriken::SkillThrowHuumaShuriken)),
        NJ_HYOUSENSOU => Some(Box::new(spearofice::SkillSpearOfIce)),
        NJ_HYOUSYOURAKU => Some(Box::new(icemeteor::SkillIceMeteor)),
        NJ_ISSEN => Some(Box::new(finalstrike::SkillFinalStrike)),
        NJ_KAENSIN => Some(Box::new(crimsonfireformation::SkillCrimsonFireFormation)),
        NJ_KAMAITACHI => Some(Box::new(kamaitachi::SkillKamaitachi)),
        NJ_KASUMIKIRI => Some(Box::new(vanishingslash::SkillVanishingSlash)),
        NJ_KIRIKAGE => Some(Box::new(shadowslash::SkillShadowSlash)),
        NJ_KOUENKA => Some(Box::new(crimsonfirepetal::SkillCrimsonFirePetal)),
        NJ_KUNAI => Some(Box::new(throwkunai::SkillThrowKunai)),
        NJ_NEN => Some(Box::new(StatusSkillImpl::new(skill_id))),
        NJ_RAIGEKISAI => Some(Box::new(lightningstrikeofdestruction::SkillLightningStrikeOfDestruction)),
        NJ_SHADOWJUMP => Some(Box::new(shadowleap::SkillShadowLeap)),
        NJ_SUITON => Some(Box::new(hiddenwater::SkillHiddenWater)),
        NJ_SYURIKEN => Some(Box::new(throwshuriken::SkillThrowShuriken)),
        NJ_TATAMIGAESHI => Some(Box::new(improviseddefense::SkillImprovisedDefense)),
        NJ_UTSUSEMI => Some(Box::new(StatusSkillImpl::new(skill_id))),
        NJ_ZENYNAGE => Some(Box::new(throwzeny::SkillThrowZeny)),
        OB_AKAITSUKI => Some(Box::new(ominousmoonlight::SkillOminousMoonlight)),
        OB_OBOROGENSOU => Some(Box::new(moonlightfantasy::SkillMoonlightFantasy)),
        OB_ZANGETSU => Some(Box::new(distortedcrescent::SkillDistortedCrescent)),
        SS_AKUMUKESU => Some(Box::new(nightmareerasion::SkillNightmareErasion)),
        SS_ANKOKURYUUAKUMU => Some(Box::new(darkdragonnightmare::SkillDarkDragonNightmare)),
        SS_ANTENPOU => Some(Box::new(darkeningcannon::SkillDarkeningCannon)),
        SS_FOUR_CHARM => Some(Box::new(fourcolorscharm::SkillFourColorsCharm)),
        SS_FUUMAKOUCHIKU => Some(Box::new(huumashurikenconstruct::SkillHuumaShurikenConstruct)),
        SS_FUUMASHOUAKU => Some(Box::new(huumashurikengrasp::SkillHuumaShurikenGrasp)),
        SS_HITOUAKUMU => Some(Box::new(kunainightmare::SkillKunaiNightmare)),
        SS_KAGEAKUMU => Some(Box::new(shadownightmare::SkillShadowNightmare)),
        SS_KAGEGARI => Some(Box::new(shadowhunting::SkillShadowHunting)),
        SS_KAGEGISSEN => Some(Box::new(shadowflash::SkillShadowFlash)),
        SS_KAGENOMAI => Some(Box::new(shadowdance::SkillShadowDance)),
        SS_KINRYUUHOU => Some(Box::new(goldendragoncannon::SkillGoldenDragonCannon)),
        SS_KUNAIKAITEN => Some(Box::new(kunairotation::SkillKunaiRotation)),
        SS_KUNAIKUSSETSU => Some(Box::new(kunairefraction::SkillKunaiRefraction)),
        SS_KUNAIWAIKYOKU => Some(Box::new(kunaidistortion::SkillKunaiDistortion)),
        SS_RAIDENPOU => Some(Box::new(thunderingcannon::SkillThunderingCannon)),
        SS_REIKETSUHOU => Some(Box::new(coldbloodedcannon::SkillColdBloodedCannon)),
        SS_SEKIENHOU => Some(Box::new(redflamecannon::SkillRedFlameCannon)),
        SS_SHIMIRU => Some(Box::new(infiltrate::SkillInfiltrate)),
        SS_SHINKIROU => Some(Box::new(mirage::SkillMirage)),
        SS_TOKEDASU => Some(Box::new(meltaway::SkillMeltAway)),
        _ => None,
    }
}

// ---------------------------------------------------------------------------------------------- shared helpers

/// `sd && sd->spiritcharm_type == type && sd->spiritcharm > 0 ? sd->spiritcharm : 0` (BL_CAST(BL_PC, src)): the
/// number of spirit charms of `type` a player caster holds, 0 for any other caster or charm type (the charm skills'
/// calculateSkillRatio test).
pub(crate) fn charms_of_type(c: &Ctx, src: &Unit, type_: i32) -> i32 {
    if src.kind != crate::map::BL_PC {
        return 0;
    }
    let s = crate::skill::state::get(c, src.id);
    if s.spiritcharm_type == type_ && s.spiritcharm > 0 {
        s.spiritcharm as i32
    } else {
        0
    }
}

/// The castendNoDamageId of the StatusSkillImpl subclasses that only add an animation (Skill16thNight,
/// SkillDistortedCrescent, SkillEmptyShadow, SkillShadowWarrior, and the tails of SkillMoonlightFantasy /
/// SkillOminousMoonlight): `StatusSkillImpl::castendNoDamageId(src, target, …); clif_skill_damage(*src, *target, tick,
/// status_get_amotion(src), 0, DMGVAL_IGNORE, 1, getSkillId(), skill_lv, DMG_SINGLE);`.
pub(crate) fn status_then_damage_animation(c: &Ctx, skill_id: u16, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick) {
    crate::skills::status_castend_nodamage_id(c, skill_id, false, src, target, skill_lv);
    let s = crate::unit::get_or_freed(c, src.id).unwrap_or_else(|| src.clone());
    let t = crate::unit::get_or_freed(c, target.id).unwrap_or_else(|| target.clone());
    let amotion = crate::skills::novice::status_get_amotion(c, &s);
    crate::skill::clif::clif_skill_damage(c, &s, &t, tick, amotion, 0, crate::skill::clif::DMGVAL_IGNORE, 1, skill_id, skill_lv as i32, crate::battle::DMG_SINGLE);
}

/// The castendNoDamageId of SkillEarthCharm / SkillFireCharm / SkillIceCharm / SkillWindCharm (the same body in the
/// four files): `if (sd) { ele_type = skill_get_ele(id, lv); clif_skill_nodamage(src, *target, id, lv);
/// pc_addspiritcharm(sd, skill_get_time(id, lv), MAX_SPIRITCHARM, ele_type); }`.
pub(crate) fn summon_charm(c: &Ctx, skill_id: u16, src: &Unit, target: &Unit, skill_lv: u16) {
    if src.kind != crate::map::BL_PC {
        return;
    }
    let ele_type = skill_get_ele(skill_id, skill_lv);
    crate::skill::clif::clif_skill_nodamage(c, Some(src), target, skill_id, skill_lv as i32, true);
    crate::skill::state::pc_addspiritcharm(c, src.id, skill_get_time(skill_id, skill_lv), crate::skill::state::MAX_SPIRITCHARM, ele_type);
}

// ---------------------------------------------------------------------------------------------- skill.cpp helpers

/// skill.cpp skill_mirage_cast: a player's Shinkirou (SS_SHINKIROU) mirages repeat some Shiranui / Shinkirou skills
/// from their own cell: Kunai Distortion (a splash at (x, y) when the mirage is within the skill's range of it),
/// Shadow Dance / Darkening Cannon (a splash around the mirage), Shadow Flash (the path from the mirage to `bl`). The
/// mirages are the caster's SS_SHINKIROU unit groups (`ud->skillunits` order) and their first unit (`sug->unit`); every
/// hit carries SKILL_ALTDMG_FLAG (the skills' reduced mirage damage). Returns false when the source has no unit_data,
/// is not a player, or `flag & 1`.
#[allow(clippy::too_many_arguments)]
pub(crate) fn skill_mirage_cast(c: &Ctx, src: &Unit, bl: Option<&Unit>, skill_id: u16, skill_lv: u16, x: i16, y: i16, tick: Tick, flag: i32) -> bool {
    use crate::battle::BCT_ENEMY;
    use crate::map::BL_CHAR;
    use crate::skill::area::{self, foreach_in_area, foreach_in_range, map_foreachindir, map_foreachinpath, skill_attack_area, splash_target, Wall};
    use crate::skill::castend::skill_castend_damage_id;
    use crate::skill::{SD_ANIMATION, SD_SPLASH, SKILL_ALTDMG_FLAG};
    // unit_bl2ud(&src) == nullptr || src.type != BL_PC || (flag & 1)
    if src.kind != crate::map::BL_PC || flag & 1 != 0 {
        return false;
    }
    for sug in crate::skillunit::data::groups_of(c, src.id) {
        if sug.skill_id != SS_SHINKIROU {
            continue;
        }
        // skill_unit* su = sug->unit (the group's unit array; its first unit)
        let Some(su) = crate::skillunit::data::units_of(c, sug.group_id).into_iter().find(|u| u.idx == 0) else { continue };
        let su = su.bl;
        let src = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
        match skill_id {
            SS_KUNAIWAIKYOKU => {
                if crate::path::distance((x - su.x) as i32, (y - su.y) as i32) > skill_get_range(skill_id, skill_lv) {
                    continue;
                }
                crate::skill::clif::clif_skill_poseffect(c, &su, skill_id, skill_lv, x, y, tick);
                let range = skill_get_splash(skill_id, skill_lv) as i16;
                let f = flag | BCT_ENEMY | SKILL_ALTDMG_FLAG | 1;
                foreach_in_area(c, src.map, x - range, y - range, x + range, y + range, BL_CHAR, Wall::None, |c, t| {
                    area::skill_area_sub(c, t, &src, skill_id, skill_lv, tick, f, skill_castend_damage_id)
                });
            }
            SS_KAGENOMAI | SS_ANTENPOU => {
                // nodamage splash (`clif_skill_nodamage(su, *su, skill_id, skill_lv, tick)`: the tick is the bool
                // success argument, true)
                crate::skill::clif::clif_skill_nodamage(c, Some(&su), &su, skill_id, skill_lv as i32, tick != 0);
                let f = flag | BCT_ENEMY | SD_SPLASH | SD_ANIMATION | SKILL_ALTDMG_FLAG | 1;
                foreach_in_range(c, &su, skill_get_splash(skill_id, skill_lv) as i16, BL_CHAR, Wall::Config, |c, t| {
                    area::skill_area_sub(c, t, &src, skill_id, skill_lv, tick, f, skill_castend_damage_id)
                });
            }
            SS_KAGEGISSEN => {
                // damage splash (no caster passes a nullptr bl with this skill)
                let Some(bl) = bl else { continue };
                let (x, y) = (bl.x, bl.y);
                area::set_temp(1, 0);
                crate::skill::clif::clif_skill_nodamage(c, Some(&su), bl, skill_id, skill_lv as i32, tick != 0);
                let atk_type = skill_get_type(skill_id);
                let splash = skill_get_splash(skill_id, skill_lv) as i16;
                let maxcount = skill_get_maxcount(skill_id, skill_lv);
                if crate::skill::conf::skill_eightpath_algorithm() {
                    // Use official AoE algorithm
                    map_foreachindir(c, su.map, su.x, su.y, x, y, splash, maxcount, 0, splash_target(&src), |c, t| {
                        skill_attack_area(c, t, atk_type, &src, &src, skill_id, skill_lv, tick, SKILL_ALTDMG_FLAG, BCT_ENEMY)
                    });
                } else {
                    map_foreachinpath(c, su.map, su.x, su.y, x, y, splash, maxcount, splash_target(&src), |c, t| {
                        skill_attack_area(c, t, atk_type, &src, &src, skill_id, skill_lv, tick, SKILL_ALTDMG_FLAG, BCT_ENEMY)
                    });
                }
            }
            _ => {}
        }
    }
    true
}

/// skill.cpp skill_shimiru_check_cell (a map_foreachincell callback): 1 for a character, and for a skill unit of a
/// Shinkirou mirage group; 0 for any other skill unit (or a unit without group).
pub(crate) fn skill_shimiru_check_cell(c: &Ctx, target: &Unit) -> i32 {
    if target.kind == crate::map::BL_SKILL {
        return match crate::skillunit::data::group_of_unit(c, target.id) {
            Some(g) if g.skill_id == SS_SHINKIROU => 1,
            _ => 0,
        };
    }
    1
}

// ---------------------------------------------------------------------------------------------- Shinkirou / Shiranui bodies

/// The castendDamageId of the Shiranui / Shinkirou splash classes (SkillColdBloodedCannon, SkillDarkeningCannon,
/// SkillKunaiDistortion, SkillKunaiNightmare, SkillMeltAway, SkillShadowDance, SkillShadowHunting,
/// SkillShadowNightmare): `if (flag & 1) skill_attack(skill_get_type(getSkillId()), src, src, target, getSkillId(),
/// skill_lv, tick, flag);`.
pub(crate) fn splash_hit(c: &Ctx, skill_id: u16, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
    if flag & 1 != 0 {
        crate::skill::attack::skill_attack(c, skill_get_type(skill_id), src, src, target, skill_id, skill_lv, tick, flag);
    }
}

/// The castendNoDamageId tail of SkillDarkDragonNightmare / SkillDarkeningCannon / SkillKunaiNightmare /
/// SkillShadowDance / SkillShadowNightmare: `clif_skill_nodamage(src, *target, getSkillId(), skill_lv);
/// map_foreachinrange(skill_area_sub, target, skill_get_splash(getSkillId(), skill_lv), BL_CHAR, src, getSkillId(),
/// skill_lv, tick, flag | BCT_ENEMY | SD_SPLASH | 1, skill_castend_damage_id);`.
pub(crate) fn nodamage_splash(c: &Ctx, skill_id: u16, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
    use crate::skill::area::{self, foreach_in_range, Wall};
    let range = skill_get_splash(skill_id, skill_lv) as i16;
    let src = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
    let target = crate::unit::get_or_freed(c, target.id).unwrap_or_else(|| target.clone());
    crate::skill::clif::clif_skill_nodamage(c, Some(&src), &target, skill_id, skill_lv as i32, true);
    let f = flag | crate::battle::BCT_ENEMY | crate::skill::SD_SPLASH | 1;
    foreach_in_range(c, &target, range, crate::map::BL_CHAR, Wall::Config, |c, bl| {
        area::skill_area_sub(c, bl, &src, skill_id, skill_lv, tick, f, crate::skill::castend::skill_castend_damage_id)
    });
}

/// The castendPos2 splash of SkillColdBloodedCannon / SkillKunaiDistortion / SkillMeltAway / SkillShadowHunting:
/// `int32 i = skill_get_splash(getSkillId(), skill_lv); map_foreachinallarea(skill_area_sub, src->m, x - i, y - i,
/// x + i, y + i, BL_CHAR, src, getSkillId(), skill_lv, tick, flag | BCT_ENEMY | 1, skill_castend_damage_id);`.
#[allow(clippy::too_many_arguments)]
pub(crate) fn pos_splash(c: &Ctx, skill_id: u16, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: i32) {
    use crate::skill::area::{self, foreach_in_area, Wall};
    let i = skill_get_splash(skill_id, skill_lv) as i16;
    let src = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
    let f = flag | crate::battle::BCT_ENEMY | 1;
    foreach_in_area(c, src.map, x - i, y - i, x + i, y + i, crate::map::BL_CHAR, Wall::None, |c, bl| {
        area::skill_area_sub(c, bl, &src, skill_id, skill_lv, tick, f, crate::skill::castend::skill_castend_damage_id)
    });
}

/// The splashSearch of SkillGoldenDragonCannon / SkillRedFlameCannon / SkillThunderingCannon:
/// `skill_mirage_cast(*src, nullptr, SS_ANTENPOU, skill_lv, 0, 0, tick, flag | BCT_WOS); clif_skill_nodamage(src,
/// *target, getSkillId(), skill_lv); SkillImplRecursiveDamageSplash::splashSearch(src, target, skill_lv, tick, flag);`.
pub(crate) fn cannon_splash_search<T: super::RecursiveDamageSplash + ?Sized>(this: &T, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: i32) {
    skill_mirage_cast(c, src, None, SS_ANTENPOU, skill_lv, 0, 0, tick, flag | crate::battle::BCT_WOS);
    let src = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
    let target = crate::unit::get_or_freed(c, target.id).unwrap_or_else(|| target.clone());
    crate::skill::clif::clif_skill_nodamage(c, Some(&src), &target, this.skill_id(), skill_lv as i32, true);
    super::splash_search_default(this, c, &src, &target, skill_lv, tick, flag);
}

/// `map_session_data* sd = BL_CAST(BL_PC, &src); if (sd != nullptr) element = sd->bonus.arrow_ele;` (the Huuma
/// Shuriken classes' modifyElement): the player's ammunition element, None for another caster.
pub(crate) fn arrow_ele(c: &Ctx, src: &Unit) -> Option<i32> {
    if src.kind != crate::map::BL_PC {
        return None;
    }
    crate::status::data::sd_get(c, src.id).map(|st| st.bonus.arrow_ele)
}

#[cfg(test)]
mod tests {
    use super::*;

    /// skill_factory_ninja.cpp's case labels, in its order.
    const RATHENA_ARMS: &[u16] = &[
        KG_KAGEHUMI, KG_KAGEMUSYA, KG_KYOMU, KO_BAKURETSU, KO_DOHU_KOUKAI, KO_GENWAKU, KO_HAPPOKUNAI, KO_HUUMARANKA,
        KO_HYOUHU_HUBUKI, KO_IZAYOI, KO_JYUMONJIKIRI, KO_JYUSATSU, KO_KAHU_ENTEN, KO_KAIHOU, KO_KAZEHU_SEIRAN,
        KO_KYOUGAKU, KO_MAKIBISHI, KO_MEIKYOUSISUI, KO_MUCHANAGE, KO_SETSUDAN, KO_YAMIKUMO, KO_ZANZOU, KO_ZENKAI,
        NJ_BAKUENRYU, NJ_BUNSINJYUTSU, NJ_HUUJIN, NJ_HUUMA, NJ_HYOUSENSOU, NJ_HYOUSYOURAKU, NJ_ISSEN, NJ_KAENSIN,
        NJ_KAMAITACHI, NJ_KASUMIKIRI, NJ_KIRIKAGE, NJ_KOUENKA, NJ_KUNAI, NJ_NEN, NJ_RAIGEKISAI, NJ_SHADOWJUMP,
        NJ_SUITON, NJ_SYURIKEN, NJ_TATAMIGAESHI, NJ_UTSUSEMI, NJ_ZENYNAGE, OB_AKAITSUKI, OB_OBOROGENSOU, OB_ZANGETSU,
        SS_AKUMUKESU, SS_ANKOKURYUUAKUMU, SS_ANTENPOU, SS_FOUR_CHARM, SS_FUUMAKOUCHIKU, SS_FUUMASHOUAKU,
        SS_HITOUAKUMU, SS_KAGEAKUMU, SS_KAGEGARI, SS_KAGEGISSEN, SS_KAGENOMAI, SS_KINRYUUHOU, SS_KUNAIKAITEN,
        SS_KUNAIKUSSETSU, SS_KUNAIWAIKYOKU, SS_RAIDENPOU, SS_REIKETSUHOU, SS_SEKIENHOU, SS_SHIMIRU, SS_SHINKIROU,
        SS_TOKEDASU,
    ];

    /// Every case of rAthena's factory has a class here, reporting its own skill id, and nothing else does.
    #[test]
    fn factory_arms_match_rathena() {
        for &id in RATHENA_ARMS {
            let imp = create(id).unwrap_or_else(|| panic!("no class for skill {id}"));
            assert_eq!(imp.skill_id(), id);
        }
        for s in skill_db().iter() {
            if !RATHENA_ARMS.contains(&s.nameid) {
                assert!(create(s.nameid).is_none(), "{} is not in rAthena's factory", s.name);
            }
        }
        assert_eq!(RATHENA_ARMS.len(), 68);
    }
}
