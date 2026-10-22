//! skills/archer/tarotcardoffate.cpp: class SkillTarotCardOfFate : public SkillImpl (CG_TAROTCARD) and the file's
//! skill_tarotcard.

use crate::battle::{BCT_ENEMY, DMG_NORMAL};
use crate::db::misc::status::status_db;
use crate::db::skills::*;
use crate::inventory::{EQP_ARMOR, EQP_HELM, EQP_SHIELD};
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::sce;
use crate::skills::SkillImpl;
use crate::status::consts::*;
use crate::status::defs::{CLASS_BATTLEFIELD, SCSTART_NONE};
use crate::status::sc;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// script.hpp e_special_effects EF_TAROTCARD1 (EF_TAROTCARD1 .. EF_TAROTCARD14 follow it).
pub const EF_TAROTCARD1: i32 = 523;

pub struct SkillTarotCardOfFate;

impl SkillImpl for SkillTarotCardOfFate {
    fn skill_id(&self) -> u16 {
        CG_TAROTCARD
    }

    /// SkillTarotCardOfFate::castendNoDamageId: nothing on a target under the Sun card; an (8 * lv)% chance, never on
    /// the Emperium or battleground monsters (a player gets the failure); the SP is consumed only on success
    /// (skill_consume_requirement skips it), then the card's effect, its special effect (on the caster for the
    /// Lovers) and the no-damage packet. Failures consume nothing.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        if sce(c, target, SC_TAROTCARD).is_some() {
            // Target currently has the SUN tarot card effect and is immune to any other effect.
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        let roll = (c.rnd() % 100) as i32;
        let immune_mob = target.kind == crate::map::BL_MOB && {
            let emperium = crate::battle::mobif::guardian_guild_id(c, target).is_some() && crate::mob::get(c, target.id).map(|m| m.mob_id as u32 == crate::db::mobs::MOBID_EMPERIUM).unwrap_or(false);
            emperium || crate::battle::bl::class_type(c, target) == CLASS_BATTLEFIELD
        };
        if roll > skill_lv as i32 * 8 || immune_mob {
            if src.kind == crate::map::BL_PC {
                skill_fail(c, src, id);
            }
            *flag |= SKILL_NOCONSUME_REQ;
            return;
        }
        crate::status::hp::status_zap(c, src.id, 0, skill_get_sp(id, skill_lv) as i64, 0); // Consume SP only on success.
        let card = skill_tarotcard(c, src, target, id, skill_lv, tick); // Actual effect is executed here.
        let on = if card == 6 { super::fresh(c, src) } else { super::fresh(c, target) };
        crate::clif::specialeffect(c, &on, EF_TAROTCARD1 + card - 1, crate::clif::Target::Area);
        clif_skill_nodamage(c, Some(&super::fresh(c, src)), &super::fresh(c, target), id, skill_lv as i32, true);
    }
}

/// tarotcardoffate.cpp skill_tarotcard [Playtester]: draws a card (official chances, or 14 equal ones with
/// battle_config.tarotcard_equal_chance) and applies it to `target`. Returns the card number (1 .. 14).
pub fn skill_tarotcard(c: &Ctx, src: &Unit, target: &Unit, skill_id: u16, skill_lv: u16, tick: Tick) -> i32 {
    let card;
    if super::tarotcard_equal_chance() {
        // eAthena equal chances
        card = (c.rnd() % 14) as i32 + 1;
    } else {
        // Official chances
        let rate = (c.rnd() % 100) as i32;
        card = if rate < 10 {
            1 // THE FOOL
        } else if rate < 20 {
            2 // THE MAGICIAN
        } else if rate < 30 {
            3 // THE HIGH PRIESTESS
        } else if rate < 37 {
            4 // THE CHARIOT
        } else if rate < 47 {
            5 // STRENGTH
        } else if rate < 62 {
            6 // THE LOVERS
        } else if rate < 63 {
            7 // WHEEL OF FORTUNE
        } else if rate < 69 {
            8 // THE HANGED MAN
        } else if rate < 74 {
            9 // DEATH
        } else if rate < 82 {
            10 // TEMPERANCE
        } else if rate < 83 {
            11 // THE DEVIL
        } else if rate < 85 {
            12 // THE TOWER
        } else if rate < 90 {
            13 // THE STAR
        } else {
            14 // THE SUN
        };
    }
    let t = |c: &Ctx| super::fresh(c, target);
    let time2 = skill_get_time2(skill_id, skill_lv) as i64;
    // skill_get_time2(status_db.getSkill(type), 1): a status's own skill's duration
    let sc_time2 = |ty: i32| skill_get_time2(status_db().get_skill(ty), 1) as i64;
    match card {
        1 => {
            // THE FOOL - heals SP to 0
            crate::status::hp::status_percent_damage(c, Some(src), target.id, 0, 100, 0, false);
        }
        2 => {
            // THE MAGICIAN - matk halved
            sc::sc_start(c, Some(src), &t(c), SC_INCMATKRATE, 100, -50, time2);
        }
        3 => {
            // THE HIGH PRIESTESS - all buffs removed
            use crate::status::sc_clear::{SCCB_BUFFS, SCCB_CHEM_PROTECT};
            crate::status::sc_clear::status_change_clear_buffs(c, target.id, SCCB_BUFFS | SCCB_CHEM_PROTECT);
        }
        4 => {
            // THE CHARIOT - 1000 damage, random armor destroyed
            tarot_damage(c, src, target, skill_id, tick, 1000);
            let tt = t(c);
            if !sc::status_isdead(c, &tt) {
                let where_ = [EQP_ARMOR, EQP_SHIELD, EQP_HELM];
                let w = where_[(c.rnd() % 3) as usize] as u16;
                crate::skill::effect::skill_break_equip(c, src, &tt, w, 10000, BCT_ENEMY);
            }
        }
        5 => {
            // STRENGTH - atk halved
            sc::sc_start(c, Some(src), &t(c), SC_INCATKRATE, 100, -50, time2);
        }
        6 => {
            // THE LOVERS - 2000HP heal, random teleported
            crate::status::hp::status_heal(c, target.id, 2000, 0, 0, 0);
            let tt = t(c);
            if !crate::npc::mapflag_vs(c, tt.map) {
                use crate::script::buildins::script2::{unitctl::Bl, unitmove};
                unitmove::unit_warp(c, &Bl::Unit(tt), -1, -1, -1, crate::unit::CLR_TELEPORT);
            }
        }
        7 => {
            // WHEEL OF FORTUNE - random 2 other effects
            // Recursive call
            skill_tarotcard(c, src, target, skill_id, skill_lv, tick);
            skill_tarotcard(c, src, target, skill_id, skill_lv, tick);
        }
        8 => {
            // THE HANGED MAN - ankle, freeze or stoned
            let scs = [SC_ANKLE, SC_FREEZE, SC_STONEWAIT];
            let rand_eff = (c.rnd() % 3) as usize;
            let time = if rand_eff == 0 { time2 } else { sc_time2(scs[rand_eff]) };
            if scs[rand_eff] == SC_STONEWAIT {
                // sc_start2(src, target, SC_STONEWAIT, 100, skill_lv, src->id, time, skill_get_time(status_db.getSkill(SC_STONEWAIT), 1)): the last one is the start delay
                let delay = skill_get_time(status_db().get_skill(SC_STONEWAIT), 1);
                sc::status_change_start(c, Some(src), &t(c), SC_STONEWAIT, 100 * 100, skill_lv as i32, src.id as i32, 0, 0, time, SCSTART_NONE, delay);
            } else {
                sc::sc_start(c, Some(src), &t(c), scs[rand_eff], 100, skill_lv as i32, time);
            }
        }
        9 => {
            // DEATH - curse, coma and poison
            sc::status_change_start(c, Some(src), &t(c), SC_COMA, 10000, skill_lv as i32, 0, src.id as i32, 0, 0, SCSTART_NONE, 0);
            sc::sc_start(c, Some(src), &t(c), SC_CURSE, 100, skill_lv as i32, sc_time2(SC_CURSE));
            sc::sc_start2(c, Some(src), &t(c), SC_POISON, 100, skill_lv as i32, src.id as i32, sc_time2(SC_POISON));
        }
        10 => {
            // TEMPERANCE - confusion
            sc::sc_start(c, Some(src), &t(c), SC_CONFUSION, 100, skill_lv as i32, time2);
        }
        11 => {
            // THE DEVIL - 6666 damage, atk and matk halved, cursed
            tarot_damage(c, src, target, skill_id, tick, 6666);
            sc::sc_start(c, Some(src), &t(c), SC_INCATKRATE, 100, -50, time2);
            sc::sc_start(c, Some(src), &t(c), SC_INCMATKRATE, 100, -50, time2);
            // (rAthena passes skill_lv as the rate and 100 as val1 here)
            sc::sc_start(c, Some(src), &t(c), SC_CURSE, skill_lv as i32, 100, sc_time2(SC_CURSE));
        }
        12 => {
            // THE TOWER - 4444 damage
            tarot_damage(c, src, target, skill_id, tick, 4444);
        }
        13 => {
            // THE STAR - stun
            sc::sc_start(c, Some(src), &t(c), SC_STUN, 100, skill_lv as i32, sc_time2(SC_STUN));
        }
        _ => {
            // THE SUN - atk, matk, hit, flee and def reduced, immune to more tarot card effects
            // In renewal, this card gives the SC_TAROTCARD status change which makes you immune to other cards
            sc::sc_start(c, Some(src), &t(c), SC_TAROTCARD, 100, skill_lv as i32, time2);
            sc::sc_start(c, Some(src), &t(c), SC_INCATKRATE, 100, -20, time2);
            sc::sc_start(c, Some(src), &t(c), SC_INCMATKRATE, 100, -20, time2);
            sc::sc_start(c, Some(src), &t(c), SC_INCHITRATE, 100, -20, time2);
            sc::sc_start(c, Some(src), &t(c), SC_INCFLEERATE, 100, -20, time2);
            sc::sc_start(c, Some(src), &t(c), SC_INCDEFRATE, 100, -20, time2);
            return 14; // To make sure a valid number is returned
        }
    }
    card
}

/// `battle_fix_damage(src, target, dmg, 0, skill_id); clif_damage(*src, *target, tick, 0, 0, dmg, 0, DMG_NORMAL, 0,
/// false);` (the Chariot, the Devil, the Tower).
fn tarot_damage(c: &Ctx, src: &Unit, target: &Unit, skill_id: u16, tick: Tick, dmg: i64) {
    crate::battle::weapon_attack::battle_fix_damage(c, Some(src), &super::fresh(c, target), dmg, 0, skill_id);
    let s = super::fresh(c, src);
    let t = crate::unit::get_or_freed(c, target.id).unwrap_or_else(|| target.clone());
    crate::battle::clif::clif_damage(c, &s, &t, tick, 0, 0, dmg, 0, DMG_NORMAL, 0, false);
}

#[cfg(test)]
mod tests {
    #[test]
    fn effect_id_matches_the_script_constant() {
        assert_eq!(crate::data::constant("EF_TAROTCARD1"), Some(super::EF_TAROTCARD1 as i64));
    }
}
