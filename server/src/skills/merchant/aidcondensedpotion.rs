//! skills/merchant/aidcondensedpotion.cpp: class SkillAidCondensedPotion : public SkillImpl (CR_SLIMPITCHER).
//!
//! castendPos2 runs the potion's script (potion_flag 1: the heal is recorded in potion_hp / potion_sp, pc.cpp's
//! globals, status/hp.rs here), scales it by the caster's skills and HPlus, and calls skill_castend_nodamage_id on
//! every party / guild member in the splash area; castendNoDamageId heals each of them with those globals.

use crate::battle::{BCT_GUILD, BCT_PARTY};
use crate::db::skills::*;
use crate::map::{BL_CHAR, BL_PC};
use crate::skill::area::{self, foreach_in_area, Wall};
use crate::skill::castend::skill_castend_nodamage_id;
use crate::skill::clif::{clif_skill_nodamage, skill_fail};
use crate::skill::SKILL_NOCONSUME_REQ;
use crate::skills::novice::pc_checkskill;
use crate::skills::SkillImpl;
use crate::status::consts::*;
use crate::status::hp as shp;
use crate::status::status_get_status_data;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

/// SkillAidCondensedPotion::SkillAidCondensedPotion() : SkillImpl(CR_SLIMPITCHER).
pub struct SkillAidCondensedPotion;

impl SkillImpl for SkillAidCondensedPotion {
    fn skill_id(&self) -> u16 {
        CR_SLIMPITCHER
    }

    /// SkillAidCondensedPotion::castendNoDamageId: the heal of one target of the splash (not an Emperium nor a
    /// battleground monster), from the globals castendPos2 set.
    fn castend_nodamage_id(&self, c: &Ctx, _src: &Unit, target: &Unit, _skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let dstsd = target.kind == BL_PC;
        // Updated to block Slim Pitcher from working on barricades and guardian stones.
        if super::is_emperium(c, target) || (target.kind == crate::map::BL_MOB && crate::battle::bl::class_type(c, target) == crate::db::mobs::CLASS_BATTLEFIELD as i32) {
            return;
        }
        let (potion_hp, potion_sp, _, _) = shp::potion_values();
        if potion_hp == 0 && potion_sp == 0 {
            return;
        }
        let tstatus = status_get_status_data(c, target);
        let mut hp = potion_hp;
        let mut sp = potion_sp;
        hp = hp.wrapping_mul(100 + tstatus.vit as i32 * 2) / 100;
        sp = sp.wrapping_mul(100 + tstatus.int_ as i32 * 2) / 100;
        if dstsd {
            if hp != 0 {
                hp = hp.wrapping_mul(100 + pc_checkskill(c, target, SM_RECOVERY) * 10 + crate::skill::misc::pc_skillheal2_bonus(c, target.id, id)) / 100;
            }
            if sp != 0 {
                sp = sp.wrapping_mul(100 + pc_checkskill(c, target, MG_SRECOVERY) * 10 + crate::skill::misc::pc_skillheal2_bonus(c, target.id, id)) / 100;
            }
        }
        if let Some(tsc) = crate::battle::bl::sc(c, target).filter(|s| !s.empty()) {
            let mut penalty: u8 = 0;
            if tsc.get(SC_WATER_INSIGNIA).map(|e| e.val1 == 2).unwrap_or(false) {
                hp += hp / 10;
                sp += sp / 10;
            }
            if let Some(e) = tsc.get(SC_CRITICALWOUND) {
                penalty = penalty.wrapping_add(e.val2 as u8);
            }
            if tsc.get(SC_DEATHHURT).map(|e| e.val3 == 1).unwrap_or(false) {
                penalty = penalty.wrapping_add(20);
            }
            if tsc.has(SC_NORECOVER_STATE) {
                penalty = 100;
            }
            if penalty > 0 {
                hp -= hp.wrapping_mul(penalty as i32) / 100;
                sp -= sp.wrapping_mul(penalty as i32) / 100;
            }
        }
        if hp > 0 {
            clif_skill_nodamage(c, None, target, AL_HEAL, hp, true);
        }
        if sp > 0 {
            let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
            clif_skill_nodamage(c, None, &t, MG_SRECOVERY, sp, true);
        }
        shp::status_heal(c, target.id, hp as i64, sp as i64, 0, 0);
    }

    /// SkillAidCondensedPotion::castendPos2.
    fn castend_pos2(&self, c: &Ctx, src: &Unit, x: i16, y: i16, skill_lv: u16, tick: Tick, flag: &mut i32) {
        let id = self.skill_id();
        let f = *flag | BCT_PARTY | BCT_GUILD | 1;
        if src.kind == BL_PC {
            let aid = src.id;
            let require = crate::skill::condition::skill_get_requirement(c, src, id, skill_lv);
            let i_lv = (skill_lv % 11) as usize - 1;
            let itemid = require.itemid.get(i_lv).copied().unwrap_or(0);
            let j = crate::inventory::pc_search_inventory(c, aid, itemid);
            let slot = if j >= 0 { crate::inventory::inv_get(c, aid, j as usize) } else { None };
            let ok = j >= 0
                && itemid != 0
                && slot.as_ref().map(|s| crate::db::items::itemdb_exists(s.item.nameid).is_some() && (s.item.amount as i32) >= require.amount.get(i_lv).copied().unwrap_or(0)).unwrap_or(false);
            let Some(slot) = slot.filter(|_| ok) else {
                skill_fail(c, src, id);
                *flag |= SKILL_NOCONSUME_REQ;
                return;
            };
            shp::set_potion_flag(1);
            shp::set_potion_hpsp(0, 0);
            // run_script(sd->inventory_data[j]->script,0,sd->id,0)
            crate::inventory::run_item_script(c, aid, slot.item.nameid, crate::inventory::ItemScript::Use, 0);
            shp::set_potion_flag(0);
            //Apply skill bonuses
            let i_lv = pc_checkskill(c, src, CR_SLIMPITCHER) * 10 + pc_checkskill(c, src, AM_POTIONPITCHER) * 10 + pc_checkskill(c, src, AM_LEARNINGPOTION) * 5 + crate::skill::misc::pc_skillheal_bonus(c, aid, id);
            let (mut php, mut psp, _, _) = shp::potion_values();
            php = php.wrapping_mul(100 + i_lv) / 100;
            psp = psp.wrapping_mul(100 + i_lv) / 100;
            // Final heal increased by HPlus.
            // Is this the right place for this??? [Rytech]
            // Can HPlus also affect SP recovery???
            let sstatus = status_get_status_data(c, src);
            if sstatus.hplus > 0 {
                php += php.wrapping_mul(sstatus.hplus as i32) / 100;
                psp += psp.wrapping_mul(sstatus.hplus as i32) / 100;
            }
            shp::set_potion_hpsp(php, psp);
            if php > 0 || psp > 0 {
                let i = skill_get_splash(id, skill_lv) as i16;
                foreach_in_area(c, src.map, x - i, y - i, x + i, y + i, BL_CHAR, Wall::None, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id));
            }
        } else {
            // itemdb_search(skill_db.find(getSkillId())->require.itemid[skill_lv - 1])
            let nameid = skill_db().find(id).and_then(|s| s.require.itemid.get(skill_lv as usize - 1).copied()).unwrap_or(0);
            let idn = skill_get_max(CR_SLIMPITCHER) * 10;
            shp::set_potion_flag(1);
            shp::set_potion_hpsp(0, 0);
            // run_script(item->script,0,src->id,0): the item's use script with the monster as the attached object
            // (itemdb_search answers the dummy item for an unknown id, which has no script)
            crate::inventory::run_item_script(c, src.id, nameid, crate::inventory::ItemScript::Use, 0);
            shp::set_potion_flag(0);
            let (mut php, mut psp, _, _) = shp::potion_values();
            php = php.wrapping_mul(100 + idn) / 100;
            psp = psp.wrapping_mul(100 + idn) / 100;
            shp::set_potion_hpsp(php, psp);
            if php > 0 || psp > 0 {
                let i = skill_get_splash(id, skill_lv) as i16;
                foreach_in_area(c, src.map, x - i, y - i, x + i, y + i, BL_CHAR, Wall::None, |c, bl| area::skill_area_sub(c, bl, src, id, skill_lv, tick, f, skill_castend_nodamage_id));
            }
        }
    }
}
