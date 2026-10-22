//! skills/mercenary/skill_factory_mercenary.cpp: SkillFactoryMercenary::create and the mercenary family's classes
//! (rAthena e985006), one file per class as rAthena has them (`mercenary_<name>.cpp` -> `<name>.rs`).

use super::{SkillImpl, StatusSkillImpl};
use crate::db::skills::*;
use crate::unit::Unit;
use crate::world::Ctx;

pub mod arrowrepel;
pub mod arrowshower;
pub mod bash;
pub mod benediction;
pub mod blessing;
pub mod bowlingbash;
pub mod brandishspear;
pub mod compress;
pub mod crash;
pub mod decreaseagi;
pub mod doublestrafe;
pub mod focusedarrowstrike;
pub mod freezingtrap;
pub mod increaseagility;
pub mod kyrieeleison;
pub mod landmine;
pub mod lexdivina;
pub mod magnificat;
pub mod magnumbreak;
pub mod mentalcure;
pub mod mindblaster;
pub mod pierce;
pub mod provoke;
pub mod recuperate;
pub mod regain;
pub mod removetrap;
pub mod sacrifice;
pub mod sandman;
pub mod scapegoat;
pub mod sense;
pub mod shieldreflect;
pub mod sight;
pub mod skidtrap;
pub mod spiralpierce;
pub mod tender;

/// SkillFactoryMercenary::create.
pub fn create(skill_id: u16) -> Option<Box<dyn SkillImpl>> {
    // std::make_unique<StatusSkillImpl>(skill_id) / (skill_id, true)
    let status = |end_if_running: bool| -> Option<Box<dyn SkillImpl>> { Some(Box::new(StatusSkillImpl::new_toggle(skill_id, end_if_running))) };
    match skill_id {
        MA_CHARGEARROW => Some(Box::new(arrowrepel::SkillMercenaryArrowRepel)),
        MA_DOUBLE => Some(Box::new(doublestrafe::SkillMercenaryDoubleStrafe)),
        MA_FREEZINGTRAP => Some(Box::new(freezingtrap::SkillMercenaryFreezingTrap)),
        MA_LANDMINE => Some(Box::new(landmine::SkillMercenaryLandMine)),
        MA_REMOVETRAP => Some(Box::new(removetrap::SkillMercenaryRemoveTrap)),
        MA_SANDMAN => Some(Box::new(sandman::SkillMercenarySandman)),
        MA_SHARPSHOOTING => Some(Box::new(focusedarrowstrike::SkillMercenaryFocusedArrowStrike)),
        MA_SHOWER => Some(Box::new(arrowshower::SkillMercenaryArrowShower)),
        MA_SKIDTRAP => Some(Box::new(skidtrap::SkillMercenarySkidTrap)),
        MER_AUTOBERSERK => status(true),
        MER_BENEDICTION => Some(Box::new(benediction::SkillMercenaryBenediction)),
        MER_BLESSING => Some(Box::new(blessing::SkillMercenaryBlessing)),
        MER_COMPRESS => Some(Box::new(compress::SkillMercenaryCompress)),
        MER_CRASH => Some(Box::new(crash::SkillMercenaryCrash)),
        MER_DECAGI => Some(Box::new(decreaseagi::SkillMercenaryDecreaseAgi)),
        MER_ESTIMATION => Some(Box::new(sense::SkillMercenarySense)),
        MER_INCAGI => Some(Box::new(increaseagility::SkillMercenaryIncreaseAgility)),
        MER_INVINCIBLEOFF2 => Some(Box::new(mindblaster::SkillMercenaryMindBlaster)),
        MER_KYRIE => Some(Box::new(kyrieeleison::SkillMercenaryKyrieEleison)),
        MER_LEXDIVINA => Some(Box::new(lexdivina::SkillMercenaryLexDivina)),
        MER_MAGNIFICAT => Some(Box::new(magnificat::SkillMercenaryMagnificat)),
        MER_MENTALCURE => Some(Box::new(mentalcure::SkillMercenaryMentalCure)),
        MER_PROVOKE => Some(Box::new(provoke::SkillMercenaryProvoke)),
        MER_QUICKEN => status(false),
        MER_RECUPERATE => Some(Box::new(recuperate::SkillMercenaryRecuperate)),
        MER_REGAIN => Some(Box::new(regain::SkillMercenaryRegain)),
        MER_SCAPEGOAT => Some(Box::new(scapegoat::SkillMercenaryScapegoat)),
        MER_SIGHT => Some(Box::new(sight::SkillMercenarySight)),
        MER_TENDER => Some(Box::new(tender::SkillMercenaryTender)),
        ML_AUTOGUARD => status(true),
        ML_BRANDISH => Some(Box::new(brandishspear::SkillMercenaryBrandishSpear)),
        ML_DEFENDER => status(true),
        ML_DEVOTION => Some(Box::new(sacrifice::SkillMercenarySacrifice)),
        ML_PIERCE => Some(Box::new(pierce::SkillMercenaryPierce)),
        ML_SPIRALPIERCE => Some(Box::new(spiralpierce::SkillMercenarySpiralPierce)),
        MS_BASH => Some(Box::new(bash::SkillMercenaryBash)),
        MS_BERSERK => status(false),
        MS_BOWLINGBASH => Some(Box::new(bowlingbash::SkillMercenaryBowlingBash)),
        MS_MAGNUM => Some(Box::new(magnumbreak::SkillMercenaryMagnumBreak)),
        MS_PARRYING => status(false),
        MS_REFLECTSHIELD => Some(Box::new(shieldreflect::SkillMercenaryShieldReflect)),
        _ => None,
    }
}

// ---------------------------------------------------------------------------------------------- family helpers

/// `s_mercenary_data* mer = BL_CAST(BL_MER, src); mer->master`: the unit of the master of the mercenary `bl` (None:
/// `bl` is no mercenary, or `mer->master == nullptr`, i.e. the master is not online).
pub(crate) fn mer_master(c: &Ctx, bl: &Unit) -> Option<Unit> {
    if bl.kind != crate::map::BL_MER {
        return None;
    }
    crate::skill::iface::mercenary_master_id(c, bl).and_then(|m| crate::unit::get(c, m))
}

/// `BL_CAST(BL_MER, bl)`: the mercenary's data when `bl` is one.
pub(crate) fn mer_data(c: &Ctx, bl: &Unit) -> Option<crate::mercenary::data::MercenaryData> {
    if bl.kind != crate::map::BL_MER {
        return None;
    }
    crate::mercenary::data::get(c, bl.id)
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Every skill of SkillFactoryMercenary::create's switch has a class here, reporting its own id.
    #[test]
    fn factory_covers_rathena_switch() {
        let ids = [
            MA_CHARGEARROW, MA_DOUBLE, MA_FREEZINGTRAP, MA_LANDMINE, MA_REMOVETRAP, MA_SANDMAN, MA_SHARPSHOOTING, MA_SHOWER, MA_SKIDTRAP, MER_AUTOBERSERK, MER_BENEDICTION, MER_BLESSING, MER_COMPRESS, MER_CRASH, MER_DECAGI,
            MER_ESTIMATION, MER_INCAGI, MER_INVINCIBLEOFF2, MER_KYRIE, MER_LEXDIVINA, MER_MAGNIFICAT, MER_MENTALCURE, MER_PROVOKE, MER_QUICKEN, MER_RECUPERATE, MER_REGAIN, MER_SCAPEGOAT, MER_SIGHT, MER_TENDER, ML_AUTOGUARD,
            ML_BRANDISH, ML_DEFENDER, ML_DEVOTION, ML_PIERCE, ML_SPIRALPIERCE, MS_BASH, MS_BERSERK, MS_BOWLINGBASH, MS_MAGNUM, MS_PARRYING, MS_REFLECTSHIELD,
        ];
        assert_eq!(ids.len(), 41);
        for id in ids {
            let imp = create(id).unwrap_or_else(|| panic!("no class for skill {id}"));
            assert_eq!(imp.skill_id(), id);
        }
        assert!(create(SM_BASH).is_none());
    }

    /// bowlingbash.rs's battle_config.bowling_bash_area against the reference server's battle_config table.
    #[test]
    fn bowling_bash_area_matches_the_conf() {
        assert_eq!(bowlingbash::bowling_bash_area() as i64, crate::script::battle_flags::battle_get_value("bowling_bash_area") as i64);
    }
}
