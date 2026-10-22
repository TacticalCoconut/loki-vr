//! battle.cpp battle_calc_magic_attack (RENEWAL): "magic"-type attacks and skills — the MATK base (or the skill's own
//! base damage), the skill's ratio (skill->impl->calculateSkillRatio), card fixes before S.MATK, MRes / MDEF,
//! pc_skillatk_bonus and the Climax buffs, Grand Cross' physical half, the elemental option bonuses, the element
//! table, then DAMAGE_DIV_FIX, battle_calc_damage, the GvG / battleground rates, the skill damage adjustments and
//! bAbsorbDmgMaxHP. Float arithmetic follows rAthena's float / double exactly.

use super::bl;
use super::calc::{
    battle_apply_div_fix, battle_blewcount_bonus, battle_calc_base_damage, battle_calc_defense_reduction, battle_calc_skill_base_damage, battle_calc_weapon_attack, battle_get_magic_element,
    battle_range_type, battle_skill_damage, initialize_weapon_data, is_infinite_defense, Damage,
};
use super::fix::{self, battle_attr_fix, battle_calc_cardfix, nk_has, Nk};
use super::*;
use crate::db::skills::*;
use crate::map::BL_PC;
use crate::status::consts::*;
use crate::status::defs::*;
use crate::unit::Unit;
use crate::world::Ctx;

/// MATK_RATE: scales the damage (100 = no change).
fn matk_rate(ad: &mut Damage, a: i64) {
    ad.damage = ad.damage * a / 100;
}

/// MATK_ADDRATE: adds a% of the damage.
fn matk_addrate(ad: &mut Damage, a: i64) {
    ad.damage += ad.damage * a / 100;
}

/// MATK_ADD: adds an absolute value.
fn matk_add(ad: &mut Damage, a: i64) {
    ad.damage += a;
}

/// `matk_min + rnd() % (matk_max - matk_min)` when max > min, else matk_min (the random MATK of a hit).
fn random_matk(c: &Ctx, st: &crate::status::data::StatusData) -> i64 {
    if st.matk_max > st.matk_min {
        st.matk_min as i64 + (c.rnd() % (st.matk_max - st.matk_min) as u32) as i64
    } else {
        st.matk_min as i64
    }
}

/// battle.cpp battle_calc_magic_attack (RENEWAL).
pub fn battle_calc_magic_attack(c: &Ctx, src: &Unit, target: &Unit, skill_id: u16, skill_lv: u16, mflag: i32) -> Damage {
    let mut mflag = mflag;
    let mut ad = Damage::default();
    let sstatus = bl::status(c, src);
    let tstatus = bl::status(c, target);

    // Initial Values
    // Set to 1 because magic damage on plants is 1 per hit; if target is not a plant this gets reinitialized to 0 later
    ad.damage = 1;
    ad.div_ = skill_get_num(skill_id, skill_lv) as i16;
    // Amotion should be 0 for ground skills.
    ad.amotion = if skill_get_inf(skill_id) & INF_GROUND_SKILL as i32 != 0 { 0 } else { sstatus.amotion as i32 };
    ad.dmotion = tstatus.dmotion as i32;
    ad.blewcount = skill_get_blewcount(skill_id, skill_lv);
    ad.flag = BF_MAGIC | BF_SKILL;
    ad.miscflag = mflag;
    ad.dmg_lv = ATK_DEF;

    let skill = skill_db().find(skill_id);
    let nk: Nk = skill.map(|s| s.nk).unwrap_or(0);
    let mut imdef = nk_has(nk, NK_IGNOREDEFENSE);

    let sd = bl::sd(c, src);
    let tsd = bl::sd(c, target);
    // (s_elemental_data* ed = BL_CAST(BL_ELEM, src): not read by the renewal code; `sc = status_get_sc(src)` is
    // read fresh where rAthena reads it)

    // Initialize variables that will be used afterwards (int16 s_ele)
    let s_ele = battle_get_magic_element(c, &ad, src, target, skill_id, skill_lv) as i16 as i32;

    // Set miscellaneous data that needs be filled
    if let Some(sd) = &sd {
        state::with(c, sd.aid, |b| b.arrow_atk = false);
        ad.blewcount += battle_blewcount_bonus(sd, skill_id);
    }

    // Skill Range Criteria
    ad.flag |= battle_range_type(c, src, target, skill_id, skill_lv);

    // Update Damage data based on skill
    if let Some(imp) = crate::skills::skill_impl(skill_id) {
        imp.modify_damage_data(c, &mut ad, src, target, skill_lv);
    }

    // Infinite defense (plant mode)
    let infdef = is_infinite_defense(c, target, ad.flag);

    if !infdef {
        // No need to do the math for plants
        let mut skillratio: i32 = 100; // Skill dmg modifiers.
        if let Some(sd) = &sd {
            skillratio += sd.st.bonus.skill_ratio;
        }
        // Some skills do not use S.MATK and skillratio
        let mut has_skillratio = false;
        // Damage was set to 1 to simulate plant damage; if not plant, need to reinitialize damage with 0
        ad.damage = 0;

        // Calc base damage according to skill
        match skill_id {
            AL_HEAL | PR_BENEDICTIO | PR_SANCTUARY | AB_HIGHNESSHEAL => {
                ad.damage = crate::skill::misc::skill_calc_heal(c, src, Some(target), skill_id, skill_lv, false) as i64;
            }
            PR_ASPERSIO => ad.damage = 40,
            ALL_RESURRECTION | PR_TURNUNDEAD => {
                // Undead check is on skill_castend_damageid code.
                // 10 * skill_lv + luk + int + lv + 300 - 300 * hp / max_hp: the last term is uint32 arithmetic
                let left = 10 * skill_lv as i32 + sstatus.luk as i32 + sstatus.int_ as i32 + bl::lv_of(c, src) + 300;
                // (a max_hp of 0 divides by zero in rAthena; the module keeps the division defined)
                let right = 300u32.wrapping_mul(tstatus.hp) / tstatus.max_hp.max(1);
                let mut i = (left as u32).wrapping_sub(right) as i32;
                if i > 700 {
                    i = 700;
                }
                if ((c.rnd() % 1000) as i32) < i && !super::mobif::has_mode(tstatus.mode, MD_STATUSIMMUNE) {
                    ad.damage = tstatus.hp as i64;
                } else {
                    let v = random_matk(c, &sstatus);
                    matk_add(&mut ad, v);
                    matk_rate(&mut ad, skill_lv as i64);
                }
            }
            NPC_DARKBREATH => {
                // tstatus->hp * (…) / 100: uint32 arithmetic
                let pct: i32 = if skill_lv <= 5 { 100 / (2 * (6 - skill_lv as i32)) } else { 50 };
                ad.damage = (tstatus.hp.wrapping_mul(pct as u32) / 100) as i64;
            }
            PF_SOULBURN => ad.damage = tstatus.sp.wrapping_mul(2) as i64,
            AB_RENOVATIO => ad.damage = (bl::lv_of(c, src) * 10 + sstatus.int_ as i32) as i64,
            NPC_EARTHQUAKE => {
                if mflag & crate::skill::NPC_EARTHQUAKE_FLAG != 0 {
                    ad.flag |= crate::skill::NPC_EARTHQUAKE_FLAG; // Pass flag to battle_calc_damage
                    mflag &= !crate::skill::NPC_EARTHQUAKE_FLAG; // Remove before NK_SPLASHSPLIT check
                }
                // TODO: This code is only accurate for pre-renewal
                // In renewal, monsters should use NPC_EARTHQUAKE_K instead, but it's not implemented yet
                if sd.is_some() {
                    let str2 = sstatus.str as i32 * 2;
                    ad.damage = str2 as i64 + battle_calc_weapon_attack(c, src, target, skill_id, skill_lv, mflag).damage;
                } else {
                    ad.damage = battle_calc_base_damage(c, src, false, tstatus.size, 0);
                }
                let lv = skill_lv as i32;
                matk_rate(&mut ad, (200 + 100 * lv + 100 * (lv / 2) + if lv > 4 { 100 } else { 0 }) as i64);
                if nk_has(nk, NK_SPLASHSPLIT) && mflag > 1 {
                    ad.damage /= mflag as i64;
                }
            }
            NPC_ICEMINE | NPC_FLAMECROSS => {
                ad.damage = sstatus.rhw.atk as i64 * 20 * skill_lv as i64;
            }
            _ => {
                let v = random_matk(c, &sstatus);
                matk_add(&mut ad, v);
                if nk_has(nk, NK_SPLASHSPLIT) {
                    // Divide MATK in case of multiple targets skill
                    if mflag > 0 {
                        ad.damage /= mflag as i64;
                    } else {
                        log::error!("0 enemies targeted by {skill_id}:{}, divide per 0 avoided!", skill_get_name(skill_id));
                    }
                }
                if let Some(imp) = crate::skills::skill_impl(skill_id) {
                    imp.calculate_skill_ratio(c, &ad, src, target, skill_lv, &mut skillratio, mflag);
                }
                if let Some(s) = bl::sc(c, src) {
                    // Insignia's increases the damage of offensive magic by a fixed percentage depending on the element.
                    let lv3 = |t: i32| s.get(t).map(|e| e.val1 == 3).unwrap_or(false);
                    if (lv3(SC_FIRE_INSIGNIA) && s_ele == ELE_FIRE)
                        || (lv3(SC_WATER_INSIGNIA) && s_ele == ELE_WATER)
                        || (lv3(SC_WIND_INSIGNIA) && s_ele == ELE_WIND)
                        || (lv3(SC_EARTH_INSIGNIA) && s_ele == ELE_EARTH)
                    {
                        skillratio += 25;
                    }
                }
                has_skillratio = true;
                // Constant/misc additions from skills
                if skill_id == WZ_FIREPILLAR {
                    matk_add(&mut ad, 100 + 50 * skill_lv as i64);
                }
            }
        }

        // Damage modifier need to be applied before SMATK
        ad.damage += battle_calc_cardfix(c, BF_MAGIC, src, target, nk, s_ele, 0, ad.damage, 0, ad.flag) as i64;

        // Additionnal damage from W_2HSTAFF: must be before smatk and after battle_calc_cardfix
        if let Some(sd) = &sd {
            if sd.ch.weapon as i32 == W_2HSTAFF {
                let i = bl::checkskill(c, Some(sd), AG_TWOHANDSTAFF);
                if i > 0 {
                    ad.damage += ad.damage * i as i64 / 100;
                }
            }
        }

        if has_skillratio {
            // S.MATK needs to be applied before the skill ratio to prevent rounding issues
            if sd.is_some() && sstatus.smatk > 0 {
                ad.damage += ad.damage * sstatus.smatk as i64 / 100;
            }
            matk_rate(&mut ad, skillratio as i64);
        }

        if let Some(sd) = &sd {
            // Ignore Defense?
            let b = &sd.st.bonus;
            if !imdef
                && (b.ignore_mdef_ele & (1 << tstatus.def_ele) != 0
                    || b.ignore_mdef_ele & (1 << ELE_ALL) != 0
                    || b.ignore_mdef_race & (1 << tstatus.race) != 0
                    || b.ignore_mdef_race & (1 << RC_ALL) != 0
                    || b.ignore_mdef_class & (1 << tstatus.class_) != 0
                    || b.ignore_mdef_class & (1 << CLASS_ALL) != 0)
            {
                imdef = true;
            }
        }

        if let Some(tsd) = &tsd {
            let i = bl::sub_skillatk_bonus(tsd, skill_id);
            if i != 0 {
                ad.damage -= ad.damage * i as i64 / 100;
            }
        }

        // MRes reduces magical damage by a percentage and is calculated before MDEF and other reductions.
        if ad.damage != 0 && tstatus.mres > 0 {
            // (MRes flat reduction is already applied)
            let mut mres: i16 = tstatus.mres;
            // % MRes ignored (int16)
            let mut ignore_mres: i16 = 0;
            if let Some(sd) = &sd {
                // (in case other bonuses are implemented) % MRes ignored is the sum of all types of % MRes
                let ib = &sd.st.indexed_bonus;
                let at = |v: &Vec<i32>, i: usize| v.get(i).copied().unwrap_or(0);
                ignore_mres = (ignore_mres as i32 + at(&ib.ignore_mres_by_race, tstatus.race as usize) + at(&ib.ignore_mres_by_race, RC_ALL as usize)) as i16;
            }
            // Attacker status's that pierce MRes.
            if let Some(e) = bl::sc(c, src).as_ref().and_then(|s| s.get(SC_A_VITA)) {
                ignore_mres = (ignore_mres as i32 + e.val2) as i16;
            }
            // % MRes ignored is capped to 50% on official server
            ignore_mres = (ignore_mres as i32).min(conf::max_res_mres_ignored()) as i16;
            mres = (mres as f64 - (ignore_mres as i32 * mres as i32) as f64 / 100.0) as i16;
            let frac = (mres as f64 / (mres as f64 + 400.0)) as f32 as f64;
            ad.damage -= (frac * 80.0 / 100.0 * ad.damage as f64) as i64;
        }

        if !imdef {
            // Bonuses ignoring Mdef are added together
            let mut mdef: i16 = tstatus.mdef;
            let mdef2: i32 = tstatus.mdef2 as i32;
            let mut i: i32 = 0; // Bonus ratio that ignores Mdef
            if let Some(e) = bl::sc(c, src).as_ref().and_then(|s| s.get(SC_EXPIATIO)) {
                i += 5 * e.val1;
            }
            if let Some(sd) = &sd {
                let ib = &sd.st.indexed_bonus;
                let at = |v: &Vec<i32>, i: usize| v.get(i).copied().unwrap_or(0);
                i += at(&ib.ignore_mdef_by_race, tstatus.race as usize)
                    + at(&ib.ignore_mdef_by_race, RC_ALL as usize)
                    + at(&ib.ignore_mdef_by_class, tstatus.class_ as usize)
                    + at(&ib.ignore_mdef_by_class, CLASS_ALL as usize);
                for r in bl::race2_of(c, target) {
                    i += at(&ib.ignore_mdef_by_race2, r as usize);
                }
            }
            i = i.clamp(0, 100);
            // On renewal, Mdef is rounded after calculation
            if i > 0 {
                mdef = ((mdef as f64 - (mdef as i32 * i) as f64 / 100.0) as f32) as i16;
            }
            // RE MDEF Reduction: Damage = Magic Attack * (1000+eMDEF)/(1000+eMDEF) - sMDEF
            if mdef < 0 {
                mdef = 0; // Negative eMDEF is treated as 0 on official
            }
            ad.damage = ad.damage * (1000 + mdef as i64) / (1000 + mdef as i64 * 10) - mdef2 as i64;
        }

        // pc_skillatk_bonus must be after def reduction and before the damages applied when the target is hit
        let mut i: i32 = 0;
        if let Some(sd) = &sd {
            i += bl::skillatk_bonus(sd, skill_id);
        }
        // Buffs added to pc_skillatk_bonus (renewal) ratio (rAthena reads `sc` without a null check here)
        let sc = bl::sc(c, src);
        let climax = sc.as_ref().and_then(|s| s.get(SC_CLIMAX)).map(|e| e.val1);
        match skill_id {
            AG_DESTRUCTIVE_HURRICANE => match climax {
                Some(3) => i += 150,
                Some(5) => i -= 20,
                _ => {}
            },
            AG_VIOLENT_QUAKE_ATK => match climax {
                Some(1) => i -= 50,
                Some(3) => i += 200,
                _ => {}
            },
            AG_ALL_BLOOM_ATK => {
                if climax == Some(3) {
                    i += 300;
                }
            }
            AG_CRYSTAL_IMPACT => match climax {
                Some(3) => i += 50,
                Some(4) => i -= 50,
                _ => {}
            },
            AG_CRYSTAL_IMPACT_ATK => {
                if climax == Some(4) {
                    i += 150;
                }
            }
            SU_CN_METEOR | SU_CN_METEOR2 => {
                if let Some(e) = sc.as_ref().and_then(|s| s.get(SC_COLORS_OF_HYUN_ROK_BUFF)) {
                    i += e.val2;
                }
            }
            _ => {}
        }
        if i != 0 {
            ad.damage = (ad.damage as f64 + (ad.damage * i as i64) as f64 / 100.0) as i64;
        }

        // Apply the physical part of the skill's damage. [Skotlex]
        if matches!(skill_id, CR_GRANDCROSS | NPC_GRANDDARKNESS) {
            // Renewal ATK = Take total atk; Final Damage = ((ATK+MATK)/2)*RATIO - (tDEF + tMDEF)
            // No need to go through the whole physical damage code
            let mut wd = initialize_weapon_data(c, src, target, skill_id, skill_lv, mflag);
            battle_calc_skill_base_damage(c, &mut wd, src, target, skill_id, skill_lv);
            // Calculate ATK
            if sd.is_some() {
                wd.damage = wd.status_atk + wd.weapon_atk + wd.equip_atk + wd.percent_atk;
            }
            // Combine ATK and MATK
            ad.damage = (wd.damage + ad.damage) / 2;
            // Ratio
            skillratio += 40 * skill_lv as i32;
            matk_rate(&mut ad, skillratio as i64);
            // Total defense reduction (renewal only)
            battle_calc_defense_reduction(c, &mut ad, src, target, skill_id, skill_lv);
            let t = bl::status(c, target);
            ad.damage -= (t.mdef as i32 + t.mdef2 as i32) as i64;
        }

        if ad.damage < 1 {
            ad.damage = 1;
        } else if let Some(s) = bl::sc(c, src) {
            // only applies when hit
            // !TODO: shouldn't they be with pc_skillatk_bonus?
            let st = bl::status(c, src);
            let bonus = (6 + st.int_ as i32 / 4) as i64 + ((st.dex as i32 - 10).max(0) / 30) as i64;
            match skill_id {
                MG_LIGHTNINGBOLT | MG_THUNDERSTORM => {
                    if s.has(SC_GUST_OPTION) {
                        ad.damage += bonus;
                    }
                }
                MG_FIREBOLT | MG_FIREWALL => {
                    if s.has(SC_PYROTECHNIC_OPTION) {
                        ad.damage += bonus;
                    }
                }
                MG_COLDBOLT | MG_FROSTDIVER => {
                    if s.has(SC_AQUAPLAY_OPTION) {
                        ad.damage += bonus;
                    }
                }
                WZ_EARTHSPIKE | WZ_HEAVENDRIVE => {
                    if s.has(SC_PETROLOGY_OPTION) {
                        ad.damage += bonus;
                    }
                }
                _ => {}
            }
        }

        let t = bl::status(c, target);
        if !nk_has(nk, NK_IGNOREELEMENT) {
            ad.damage = battle_attr_fix(c, Some(src), Some(target), ad.damage, s_ele, t.def_ele as i32, t.ele_lv as i32, 0);
        }

        if matches!(skill_id, CR_GRANDCROSS | NPC_GRANDDARKNESS) {
            if src.id == target.id {
                // Grand Cross on self first applies attr_fix, then cardfix and finally halves the damage
                if src.kind == BL_PC {
                    ad.damage /= 2;
                } else {
                    ad.damage = 0;
                }
            } else {
                // Grand Cross on target applies attr_fix, then cardfix and then attr_fix a second time
                let t = bl::status(c, target);
                ad.damage = battle_attr_fix(c, Some(src), Some(target), ad.damage, s_ele, t.def_ele as i32, t.ele_lv as i32, 0);
            }
        }
    } // Hint: Against plants damage will still be 1 at this point

    // Apply DAMAGE_DIV_FIX and check for min damage
    battle_apply_div_fix(&mut ad, skill_id);

    let d = ad.damage;
    ad.damage = fix::battle_calc_damage(c, src, target, &mut ad, d, skill_id, skill_lv);
    if crate::npc::mapflag_gvg2(c, target.map) {
        ad.damage = fix::battle_calc_gvg_damage(c, src, target, ad.damage, skill_id, ad.flag);
    } else if crate::npc::mapflag(c, target.map, crate::npc::MF_BATTLEGROUND) {
        ad.damage = fix::battle_calc_bg_damage(c, src, target, ad.damage, skill_id, ad.flag);
    }

    // Skill damage adjustment
    let skill_damage = battle_skill_damage(c, src, Some(target), skill_id);
    if skill_damage != 0 {
        matk_addrate(&mut ad, skill_damage as i64);
    }

    fix::battle_absorb_damage(c, target, &mut ad);

    // battle_do_reflect(BF_MAGIC, &ad, …): commented out in rAthena (WIP [lighta]); magic has its own handler at
    // skill_attack
    ad
}
