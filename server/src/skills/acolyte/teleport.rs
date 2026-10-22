//! skills/acolyte/teleport.cpp: class SkillTeleport : public SkillImpl (AL_TELEPORT).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::skill::clif::{clif_skill_nodamage, clif_skill_teleportmessage, NOTIFY_MAPINFO_CANT_TP};
use crate::skills::SkillImpl;
use crate::unit::{self, Unit, CLR_TELEPORT};
use crate::world::{Ctx, Tick};

pub struct SkillTeleport;

impl SkillImpl for SkillTeleport {
    fn skill_id(&self) -> u16 {
        AL_TELEPORT
    }

    /// SkillTeleport::castendNoDamageId: a player gets the destination menu ("Random", and the save point from level
    /// 2), or goes at once (autocast, item cast / skip_teleport_lv1_menu at level 1, level 3); anything else warps
    /// to a random cell.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.kind != BL_PC {
            crate::mob::unit::unit_warp(c, target.id, -1, -1, -1, CLR_TELEPORT);
            return;
        }
        let aid = src.id;
        if crate::npc::mapflag(c, target.map, crate::npc::MF_NOTELEPORT) && skill_lv <= 2 {
            clif_skill_teleportmessage(c, src, NOTIFY_MAPINFO_CANT_TP);
            return;
        }
        if !crate::skillunit::conf::duel_allow_teleport() && crate::skill::iface::duel_group(c, aid) != 0 && skill_lv <= 2 {
            // duel restriction [LuzZza]
            let output = crate::data::msg_txt(365).replacen("%s", skill_get_name(id), 1);
            crate::clif::displaymessage(c, src, &output); // "Duel: Can't use %s in duel."
            return;
        }
        let st = crate::skill::state::get(c, aid);
        if st.autocast || ((st.skillitem == id as i16 || crate::skillunit::conf::skip_teleport_lv1_menu()) && skill_lv == 1) || skill_lv == 3 {
            if skill_lv == 1 {
                crate::script::buildins::pcinfo::pc_randomwarp(c, aid, CLR_TELEPORT, false);
            } else if let Some(ch) = crate::pc::character_of(c, aid) {
                // pc_setpos(sd, mapindex_name2id(sd->status.save_point.map), …): an unknown map is index 0, which
                // pc_setpos refuses
                if let Some(m) = crate::data::maps().id_of(&ch.save_map) {
                    crate::pc::setpos(c, aid, m, ch.save_x as i16, ch.save_y as i16, CLR_TELEPORT);
                }
            }
            return;
        }
        let t = unit::get(c, target.id).unwrap_or_else(|| target.clone());
        clif_skill_nodamage(c, Some(src), &t, id, skill_lv as i32, true);
        let mut maps: Vec<String> = vec!["Random".to_string()];
        if skill_lv != 1 {
            if let Some(ch) = crate::pc::character_of(c, aid) {
                maps.push(ch.save_map.clone());
            }
        }
        let sd = unit::get(c, aid).unwrap_or_else(|| src.clone());
        crate::skillunit::clif::clif_skill_warppoint(c, &sd, id, skill_lv, &maps);
    }
}
