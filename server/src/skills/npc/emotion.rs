//! skills/npc/emotion.cpp (SkillEmotion, NPC_EMOTION), rAthena e985006, and the body it shares with
//! skills/npc/emotionon.cpp (SkillEmotionOn, NPC_EMOTION_ON, emotionon.rs): a monster's emotion skill shows the emotion of its mob_skill_db entry (val[0]) and may change its
//! mode through the skill's status (skill_get_sc: SC_MODECHANGE): val[1] sets the mode, val[2] adds to it, val[3]
//! removes from it, val[4] ends the previous mode change first. The two classes differ in one step: NPC_EMOTION
//! unlocks the target when it sets a mode outside the idle / walk states (npc_emotion_behavior: no).

use super::super::SkillImpl;
use crate::db::mobs::{MD_ANGRY, MSS_IDLE, MSS_WALK};
use crate::db::skills::{skill_get_sc, skill_get_time, NPC_EMOTION};
use crate::map::BL_MOB;
use crate::status::consts::SC_NONE;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// conf/battle/skill.conf `npc_emotion_behavior: 0` (script/battle_flags.rs).
pub fn npc_emotion_behavior() -> bool {
    crate::battle_config::get(crate::battle_config::Bc::NPC_EMOTION_BEHAVIOR) != 0
}

/// The shared body of SkillEmotion / SkillEmotionOn::castendNoDamageId; `unlock` is NPC_EMOTION's extra
/// mob_unlocktarget step.
pub(super) fn emotion_castend(c: &Ctx, skill_id: u16, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, unlock: bool) {
    let t = skill_get_sc(skill_id) as i32;
    // status_get_sc(target): every unit kind with a status_change (None for the others)
    let has_tsc = crate::battle::bl::sc(c, target).is_some();
    let tsce = if has_tsc && t != SC_NONE { crate::status::sc::get_sce(c, target.id, t) } else { None };
    // mob_data* md = BL_CAST(BL_MOB, src)
    if src.kind != BL_MOB {
        return;
    }
    let Some(md) = crate::mob::get(c, src.id) else { return };
    if md.skill_idx < 0 || !has_tsc {
        return;
    }
    let Some(db) = crate::mob::db(c, &md) else { return };
    let Some(ms) = db.skill.get(md.skill_idx as usize) else { return };
    let val = ms.val;
    // clif_emotion(*target, static_cast<emotion_type>(val[0]))
    crate::clif::emotion(c, target, val[0] as u8);
    if val[4] != 0 && tsce.is_some() {
        crate::status::sc::status_change_end(c, target.id, t, 0);
    }
    // If mode gets set by NPC_EMOTION then the target should be reset [Playtester]
    if unlock && !npc_emotion_behavior() && md.skillstate != MSS_IDLE && md.skillstate != MSS_WALK && val[1] != 0 {
        crate::mob::ai::mob_unlocktarget(c, md.id, tick);
    }
    if val[1] != 0 || val[2] != 0 {
        if let Some(s) = crate::unit::get(c, src.id) {
            crate::status::sc::sc_start4(c, Some(&s), &s, t, 100, skill_lv as i32, val[1], val[2], val[3], skill_get_time(skill_id, skill_lv) as i64);
        }
    }
    // Reset aggressive state depending on resulting mode
    if !npc_emotion_behavior() {
        if let Some(mut md) = crate::mob::get(c, src.id) {
            md.aggressive = crate::mob::has_mode(&md, MD_ANGRY);
            crate::mob::put(c, &md);
        }
    }
}

/// skills/npc/emotion.cpp SkillEmotion (NPC_EMOTION).
pub struct SkillEmotion;

impl SkillImpl for SkillEmotion {
    fn skill_id(&self) -> u16 {
        NPC_EMOTION
    }
    /// SkillEmotion::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        emotion_castend(c, NPC_EMOTION, src, target, skill_lv, tick, true);
    }
}

#[cfg(test)]
mod tests {
    #[test]
    fn npc_emotion_behavior_matches_the_conf() {
        assert_eq!(crate::script::battle_flags::battle_get_value("npc_emotion_behavior"), super::npc_emotion_behavior() as i32);
    }
}
