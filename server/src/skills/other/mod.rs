//! skills/other/skill_factory_other.cpp: SkillFactoryOther::create and the "other" family's classes (rAthena e985006):
//! the all-class skills (ALL_*), the family skills of married players and adopted babies (WE_*), the cash / Eclage /
//! event / GM skills and the ABR summons' skills, one file per class as rAthena has them.
//!
//! The end of this file holds the helpers the class files share: pc.cpp's family lookups (pc_ismarried,
//! pc_get_partner, pc_get_father, pc_get_mother, pc_get_child: besides these classes, rAthena calls them from
//! skill_castend_id's WE_CALL* checks, trade.cpp, clif.cpp and script.cpp (pc_ismarried), whose ports read the same
//! fields directly) and the body the eight recall classes repeat.

use super::{SkillImpl, StatusSkillImpl, WeaponSkillImpl};
use crate::db::skills::*;
use crate::unit::Unit;
use crate::world::Ctx;

pub mod baby;
pub mod battlebuster;
pub mod callallfamily;
pub mod callbaby;
pub mod callparent;
pub mod catcry;
pub mod cheerup;
pub mod christmascarol;
pub mod dualcannonfire;
pub mod equipswitch;
pub mod gmsandman;
pub mod guardiansrecall;
pub mod ilookuptoyou;
pub mod imissyou;
pub mod infinitybuster;
pub mod iwillprotectyou;
pub mod netrepair;
pub mod netsupport;
pub mod niflheimrecall;
pub mod odinsrecall;
pub mod oneforever;
pub mod openbuyingstore;
pub mod partyassumptio;
pub mod partyblessing;
pub mod partyflee;
pub mod partyincreaseagi;
pub mod peonymamy;
pub mod pronterarecall;
pub mod rayofprotection;
pub mod returntoeclage;
pub mod returntoeldicastes;
pub mod returntoglastheim;
pub mod returntolighthalzen;
pub mod returntothanatos;
pub mod ro20thanniversaryfirecracker;
pub mod sadagui;
pub mod sequoiadust;
pub mod snowflip;
pub mod summernightdream;
pub mod weaponenchantment;

/// SkillFactoryOther::create.
pub fn create(skill_id: u16) -> Option<Box<dyn SkillImpl>> {
    // std::make_unique<StatusSkillImpl>(skill_id)
    let status = || -> Option<Box<dyn SkillImpl>> { Some(Box::new(StatusSkillImpl::new(skill_id))) };
    match skill_id {
        ABR_BATTLE_BUSTER => Some(Box::new(battlebuster::SkillBattleBuster)),
        ABR_DUAL_CANNON_FIRE => Some(Box::new(dualcannonfire::SkillDualCannonFire)),
        ABR_INFINITY_BUSTER => Some(Box::new(infinitybuster::SkillInfinityBuster)),
        ABR_NET_REPAIR => Some(Box::new(netrepair::SkillNetRepair)),
        ABR_NET_SUPPORT => Some(Box::new(netsupport::SkillNetSupport)),
        ALL_BUYING_STORE => Some(Box::new(openbuyingstore::SkillOpenBuyingStore)),
        ALL_CATCRY => Some(Box::new(catcry::SkillCatCry)),
        ALL_DREAM_SUMMERNIGHT => Some(Box::new(summernightdream::SkillSummerNightDream)),
        ALL_EQSWITCH => Some(Box::new(equipswitch::SkillEquipSwitch)),
        ALL_EVENT_20TH_ANNIVERSARY => Some(Box::new(ro20thanniversaryfirecracker::SkillRo20thAnniversaryFirecracker)),
        ALL_FULL_THROTTLE => status(),
        ALL_GLASTHEIM_RECALL => Some(Box::new(returntoglastheim::SkillReturnToGlastHeim)),
        ALL_GUARDIAN_RECALL => Some(Box::new(guardiansrecall::SkillGuardiansRecall)),
        ALL_LIGHTHALZEN_RECALL => Some(Box::new(returntolighthalzen::SkillReturnToLighthalzen)),
        ALL_NIFLHEIM_RECALL => Some(Box::new(niflheimrecall::SkillNiflheimRecall)),
        ALL_ODINS_POWER => status(),
        ALL_ODINS_RECALL => Some(Box::new(odinsrecall::SkillOdinsRecall)),
        ALL_PARTYFLEE => Some(Box::new(partyflee::SkillPartyFlee)),
        ALL_PRONTERA_RECALL => Some(Box::new(pronterarecall::SkillPronteraRecall)),
        ALL_RAY_OF_PROTECTION => Some(Box::new(rayofprotection::SkillRayOfProtection)),
        ALL_REVERSEORCISH => status(),
        ALL_THANATOS_RECALL => Some(Box::new(returntothanatos::SkillReturnToThanatos)),
        ALL_WEWISH => Some(Box::new(christmascarol::SkillChristmasCarol)),
        CASH_ASSUMPTIO => Some(Box::new(partyassumptio::SkillPartyAssumptio)),
        CASH_BLESSING => Some(Box::new(partyblessing::SkillPartyBlessing)),
        CASH_INCAGI => Some(Box::new(partyincreaseagi::SkillPartyIncreaseAgi)),
        ECLAGE_RECALL => Some(Box::new(returntoeclage::SkillReturnToEclage)),
        ECL_PEONYMAMY => Some(Box::new(peonymamy::SkillPeonyMamy)),
        ECL_SADAGUI => Some(Box::new(sadagui::SkillSadagui)),
        ECL_SEQUOIADUST => Some(Box::new(sequoiadust::SkillSequoiaDust)),
        ECL_SNOWFLIP => Some(Box::new(snowflip::SkillSnowFlip)),
        GM_SANDMAN => Some(Box::new(gmsandman::SkillGmSandman)),
        ITEM_ENCHANTARMS => Some(Box::new(weaponenchantment::SkillWeaponEnchantment)),
        ITM_TOMAHAWK => Some(Box::new(WeaponSkillImpl { id: skill_id })),
        RETURN_TO_ELDICASTES => Some(Box::new(returntoeldicastes::SkillReturnToEldicastes)),
        WE_BABY => Some(Box::new(baby::SkillBaby)),
        WE_CALLALLFAMILY => Some(Box::new(callallfamily::SkillCallAllFamily)),
        WE_CALLBABY => Some(Box::new(callbaby::SkillCallBaby)),
        WE_CALLPARENT => Some(Box::new(callparent::SkillCallParent)),
        WE_CALLPARTNER => Some(Box::new(imissyou::SkillIMissYou)),
        WE_CHEERUP => Some(Box::new(cheerup::SkillCheerUp)),
        WE_FEMALE => Some(Box::new(ilookuptoyou::SkillILookUpToYou)),
        WE_MALE => Some(Box::new(iwillprotectyou::SkillIWillProtectYou)),
        WE_ONEFOREVER => Some(Box::new(oneforever::SkillOneForever)),
        _ => None,
    }
}

// ---------------------------------------------------------------------------------------------- shared helpers

/// `map_session_data* sd = BL_CAST(BL_PC, bl)`: the player's saved status (sd->status), None for a non-player.
pub(crate) fn sd_status(c: &Ctx, bl: &Unit) -> Option<crate::character::Character> {
    if bl.kind != crate::map::BL_PC {
        return None;
    }
    crate::pc::character_of(c, bl.id)
}

/// pc.cpp pc_ismarried: `sd->status.partner_id > 0` gives the partner's char id, else 0.
fn pc_ismarried(ch: &crate::character::Character) -> u32 {
    if ch.partner_id > 0 {
        ch.partner_id
    } else {
        0
    }
}

/// pc.cpp pc_get_partner: the married partner when online (map_charid2sd), else None.
pub(crate) fn pc_get_partner(c: &Ctx, sd: &Unit) -> Option<Unit> {
    let ch = sd_status(c, sd)?;
    if pc_ismarried(&ch) == 0 {
        return None;
    }
    crate::pc::charid2sd(c, ch.partner_id)
}

/// `sd->class_ & JOBL_BABY` (the player's mapid class).
fn is_baby(c: &Ctx, sd: &Unit) -> bool {
    crate::status::data::sd_get(c, sd.id).map(|s| s.class_ & crate::db::jobs::JOBL_BABY != 0).unwrap_or(false)
}

/// pc.cpp pc_get_father: a baby's father when online, else None.
pub(crate) fn pc_get_father(c: &Ctx, sd: &Unit) -> Option<Unit> {
    let ch = sd_status(c, sd)?;
    if !is_baby(c, sd) || ch.father == 0 {
        return None;
    }
    crate::pc::charid2sd(c, ch.father)
}

/// pc.cpp pc_get_mother: a baby's mother when online, else None.
pub(crate) fn pc_get_mother(c: &Ctx, sd: &Unit) -> Option<Unit> {
    let ch = sd_status(c, sd)?;
    if !is_baby(c, sd) || ch.mother == 0 {
        return None;
    }
    crate::pc::charid2sd(c, ch.mother)
}

/// pc.cpp pc_get_child: a married player's adopted child when online, else None.
pub(crate) fn pc_get_child(c: &Ctx, sd: &Unit) -> Option<Unit> {
    let ch = sd_status(c, sd)?;
    if pc_ismarried(&ch) == 0 || ch.child == 0 {
        // charid2sd returns nullptr if not found
        return None;
    }
    crate::pc::charid2sd(c, ch.child)
}

/// The castendNoDamageId of the recall classes (guardiansrecall.cpp, niflheimrecall.cpp, pronterarecall.cpp,
/// returntoeclage.cpp, returntoeldicastes.cpp, returntoglastheim.cpp, returntolighthalzen.cpp, returntothanatos.cpp:
/// the same code with their destination): a player caster starts the skill's status for its cooldown on the target
/// (before the map test, as rAthena does), then is warped to (x, y) on `map`; an unknown map fails the skill without
/// consuming its requirements. mapindex_name2id: the module's map id stands for rAthena's map index (every map is on
/// this server).
#[allow(clippy::too_many_arguments)]
pub(crate) fn recall_castend_nodamage_id(c: &Ctx, skill_id: u16, src: &Unit, target: &Unit, skill_lv: u16, flag: &mut i32, map: &str, x: i16, y: i16) {
    let type_ = skill_get_sc(skill_id) as i32;
    if src.kind != crate::map::BL_PC {
        return;
    }
    let mapindex = crate::data::maps().id_of(map);
    crate::status::sc::sc_start(c, Some(src), target, type_, 100, skill_lv as i32, skill_get_cooldown(skill_id, skill_lv) as i64);
    let Some(m) = mapindex else {
        //Given map not found?
        let s = crate::unit::get(c, src.id).unwrap_or_else(|| src.clone());
        crate::skill::clif::skill_fail(c, &s, skill_id);
        *flag |= crate::skill::SKILL_NOCONSUME_REQ;
        return;
    };
    crate::pc::setpos(c, src.id, m, x, y, crate::unit::CLR_TELEPORT);
}
