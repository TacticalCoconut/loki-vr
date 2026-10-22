//! skills/mage/hocuspocus.cpp: class SkillHocusPocus : public SkillImpl (SA_ABRACADABRA).

use crate::db::skills::*;
use crate::map::{BL_MOB, BL_PC, BL_PET};
use crate::skill::clif::{clif_item_skill, clif_skill_nodamage};
use crate::skill::unit::{unit_skilluse_id, unit_skilluse_pos};
use crate::skills::SkillImpl;
use crate::unit::{self, Unit};
use crate::world::{Ctx, Tick};

pub struct SkillHocusPocus;

impl SkillImpl for SkillHocusPocus {
    fn skill_id(&self) -> u16 {
        SA_ABRACADABRA
    }

    /// SkillHocusPocus::castendNoDamageId: picks a skill from abra_db (db/abra_db.yml): up to `abra_db.size() * 3 + 1`
    /// tries of `abra_db.random()` (util::umap_random: the entry `rnd_value(0, size - 1)` places into the
    /// unordered_map's iteration, which `skill_misc().abra` holds) kept when `rnd() % 10000 < per[skill_lv - 1]`
    /// (the last one tried is used when none is kept). A player gets it as an item skill (ZC_AUTORUN_SKILL); a
    /// monster / pet casts it at once.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        let abra_db = &skill_misc().abra;

        if abra_db.is_empty() {
            clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
            return;
        }

        let mut abra_skill_id: i32;
        let mut abra_skill_lv: i32;
        let mut checked: usize = 0;
        let checked_max = abra_db.len() * 3;

        loop {
            // abra_db.random(): rnd_value<size_t>(0, size - 1)
            let pos = c.rnd_value(0, abra_db.len() as i32 - 1) as usize;
            let Some(abra_spell) = abra_random(pos) else { return };

            abra_skill_id = abra_spell.skill_id as i32;
            // min(skill_lv, skill_get_max(abra_skill_id)): cbasetypes' int32 min
            abra_skill_lv = (skill_lv as i32).min(skill_get_max(abra_skill_id as u16));

            // per[max(skill_lv - 1, 0)]: skill_lv is at most the skill's MaxLevel (10), inside per[MAX_SKILL_LEVEL];
            // an index past the array (undefined in rAthena) reads 0 here
            let per = abra_spell.per.get((skill_lv as i32 - 1).max(0) as usize).copied().unwrap_or(0);
            if ((c.rnd() % 10000) as i32) < per as i32 {
                break;
            }
            // while (checked++ < checked_max)
            let more = checked < checked_max;
            checked += 1;
            if !more {
                break;
            }
        }

        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);

        if src.kind == BL_PC {
            // player-casted
            crate::skill::state::with(c, src.id, |s| {
                s.abra_flag = 1;
                s.skillitem = abra_skill_id as i16;
                s.skillitemlv = abra_skill_lv as i16;
                s.skillitem_keep_requirement = false;
            });
            let sd = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            clif_item_skill(c, &sd, abra_skill_id as u16, abra_skill_lv as u16);
        } else {
            // mob-casted
            // unit_bl2ud(src): the unit types with a unit_data
            let Some(src_now) = unit::get(c, src.id) else { return };
            let inf = skill_get_inf(abra_skill_id as u16);
            if !crate::skillunit::group::has_ud(src_now.kind) {
                return;
            }
            let (sid, slv) = (abra_skill_id as u16, abra_skill_lv as u16);
            if inf & INF_SELF_SKILL as i32 != 0 || inf & INF_SUPPORT_SKILL as i32 != 0 {
                // target = ((TBL_PET*)src)->master for a pet (a pet without a master: nullptr, then src)
                let mut target_id = target.id;
                if src_now.kind == BL_PET {
                    target_id = crate::pet::data::get(c, src_now.id).map(|pd| pd.master).unwrap_or(0);
                }
                if target_id == 0 {
                    target_id = src_now.id;
                }
                unit_skilluse_id(c, src_now.id, target_id, sid, slv);
            } else {
                //Assume offensive skills
                let mut target_id: u32 = 0;
                if src_now.ud.target != 0 {
                    target_id = src_now.ud.target;
                } else {
                    match src_now.kind {
                        BL_MOB => target_id = crate::mob::get(c, src_now.id).map(|md| md.target_id).unwrap_or(0),
                        BL_PET => target_id = crate::pet::data::get(c, src_now.id).map(|pd| pd.target_id).unwrap_or(0),
                        _ => {}
                    }
                }
                if target_id == 0 {
                    return;
                }
                if skill_get_casttype(sid) == CastType::Ground {
                    // target = map_id2bl(target_id); if (!target) target = src;
                    let t = unit::get(c, target_id).unwrap_or_else(|| src_now.clone());
                    unit_skilluse_pos(c, src_now.id, t.x, t.y, sid, slv);
                } else {
                    unit_skilluse_id(c, src_now.id, target_id, sid, slv);
                }
            }
        }
    }
}
