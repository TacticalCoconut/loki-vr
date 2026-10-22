//! skills/other/odinsrecall.cpp: class SkillOdinsRecall : public SkillImpl (ALL_ODINS_RECALL).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::npc::{mapflag, MF_NOTELEPORT};
use crate::skill::clif::{clif_skill_nodamage, clif_skill_teleportmessage, NOTIFY_MAPINFO_CANT_TP};
use crate::skills::SkillImpl;
use crate::unit::{self, Unit, CLR_TELEPORT};
use crate::world::{Ctx, Tick};

pub struct SkillOdinsRecall;

impl SkillImpl for SkillOdinsRecall {
    fn skill_id(&self) -> u16 {
        ALL_ODINS_RECALL
    }

    /// SkillOdinsRecall::castendNoDamageId (AL_TELEPORT's code): levels 1-2 are refused on noteleport maps and in a
    /// duel (duel_allow_teleport); an autocast, level 1 from a Fly Wing (skillitem AL_TELEPORT) or with
    /// skip_teleport_lv1_menu, or level 3 warps at once (level 1 random, else the save point); else the packet and the
    /// warp menu ("Random" and the save map). A non-player target is warped to a random cell (unit_warp).
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, target: &Unit, skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.kind != BL_PC {
            if let Some(t) = unit::get(c, target.id) {
                use crate::script::buildins::script2::{unitctl::Bl, unitmove};
                unitmove::unit_warp(c, &Bl::Unit(t), -1, -1, -1, CLR_TELEPORT);
            }
            return;
        }
        let aid = src.id;
        if mapflag(c, target.map, MF_NOTELEPORT) && skill_lv <= 2 {
            clif_skill_teleportmessage(c, src, NOTIFY_MAPINFO_CANT_TP);
            return;
        }
        if !crate::skill::conf::duel_allow_teleport() && crate::skill::iface::duel_group(c, aid) != 0 && skill_lv <= 2 {
            // duel restriction [LuzZza]
            let output = crate::data::msg_txt(365).replacen("%s", skill_get_name(ALL_ODINS_RECALL), 1);
            crate::clif::displaymessage(c, src, &output); //"Duel: Can't use %s in duel."
            return;
        }
        let st = crate::skill::state::get(c, aid);
        if st.autocast || ((st.skillitem == AL_TELEPORT as i16 || crate::skillunit::conf::skip_teleport_lv1_menu()) && skill_lv == 1) || skill_lv == 3 {
            if skill_lv == 1 {
                crate::script::buildins::pcinfo::pc_randomwarp(c, aid, CLR_TELEPORT, false);
            } else if let Some(ch) = crate::pc::character_of(c, aid) {
                // mapindex_name2id(save_point.map): an unknown map is index 0 (pc_setpos refuses it)
                if let Some(m) = crate::data::maps().id_of(&ch.save_map) {
                    crate::pc::setpos(c, aid, m, ch.save_x as i16, ch.save_y as i16, CLR_TELEPORT);
                }
            }
            return;
        }
        clif_skill_nodamage(c, Some(src), target, id, skill_lv as i32, true);
        let mut maps = vec!["Random".to_string()];
        maps.push(crate::pc::character_of(c, aid).map(|ch| ch.save_map).unwrap_or_default());
        crate::skillunit::clif::clif_skill_warppoint(c, src, id, skill_lv, &maps);
    }
}
