//! skills/npc/emotionon.cpp: class SkillEmotionOn : public SkillImpl (NPC_EMOTION_ON). Its body is SkillEmotion's
//! without the mob_unlocktarget step (emotion.rs emotion_castend).

use super::super::SkillImpl;
use super::emotion::emotion_castend;
use crate::db::skills::NPC_EMOTION_ON;
use crate::unit::Unit;
use crate::world::{Ctx, Tick};

/// skills/npc/emotionon.cpp SkillEmotionOn (NPC_EMOTION_ON).
pub struct SkillEmotionOn;

impl SkillImpl for SkillEmotionOn {
    fn skill_id(&self) -> u16 {
        NPC_EMOTION_ON
    }
    /// SkillEmotionOn::castendNoDamageId.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, tick: Tick, _flag: &mut i32) {
        emotion_castend(c, NPC_EMOTION_ON, src, target, skill_lv, tick, false);
    }
}
