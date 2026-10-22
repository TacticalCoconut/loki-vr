//! skills/acolyte/convenio.cpp: class SkillConvenio : public SkillImpl (AB_CONVENIO).

use crate::db::skills::*;
use crate::map::BL_PC;
use crate::npc::{mapflag, mapflag_gvg2, MF_BATTLEGROUND, MF_NOTELEPORT, MF_PVP};
use crate::skill::clif::skill_fail;
use crate::skills::SkillImpl;
use crate::unit::{self, Unit, CLR_TELEPORT};
use crate::world::{Ctx, Tick};

pub struct SkillConvenio;

impl SkillImpl for SkillConvenio {
    fn skill_id(&self) -> u16 {
        AB_CONVENIO
    }

    /// SkillConvenio::castendNoDamageId: the party leader calls the living members on the same map (who allow
    /// /call) to its cell; not on no-teleport, PvP, battleground or GvG maps. Nobody called: the cast fails.
    fn castend_nodamage_id(&self, c: &Ctx, src: &Unit, _target: &Unit, _skill_lv: u16, _tick: Tick, _flag: &mut i32) {
        let id = self.skill_id();
        if src.kind != BL_PC {
            return;
        }
        let party_id = crate::pc::character_of(c, src.id).map(|ch| ch.party_id).unwrap_or(0);
        // Only usable in party
        let Some(p) = crate::party::party_search(c, party_id) else {
            skill_fail(c, src, id);
            return;
        };
        // Only usable as party leader.
        let i = p.data.iter().position(|d| d.sd == src.id);
        match i {
            Some(i) if p.party.member.get(i).is_some_and(|m| m.leader) => {}
            _ => {
                skill_fail(c, src, id);
                return;
            }
        }
        // Do the teleport part
        let mut count = 0;
        for d in p.data.iter() {
            let pl = d.sd;
            if pl == 0 || pl == src.id {
                continue;
            }
            let Some(pl_u) = unit::get(c, pl) else { continue };
            let Some(pl_ch) = crate::pc::character_of(c, pl) else { continue };
            if pl_ch.party_id != p.party.party_id || crate::pc::isdead(c, pl) || src.map != pl_u.map {
                continue;
            }
            // Respect /call configuration
            if pl_ch.disable_call != 0 {
                continue;
            }
            let m = src.map;
            if !(mapflag(c, m, MF_NOTELEPORT) || mapflag(c, m, MF_PVP) || mapflag(c, m, MF_BATTLEGROUND) || mapflag_gvg2(c, m)) {
                // pc_setpos(pl_sd, map_id2index(sd->m), sd->x, sd->y, CLR_TELEPORT) (the module's map id is the map
                // index); rAthena counts the member whatever pc_setpos answers
                let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
                crate::pc::setpos(c, pl, s.map, s.x, s.y, CLR_TELEPORT);
                count += 1;
            }
        }
        if count == 0 {
            let s = unit::get(c, src.id).unwrap_or_else(|| src.clone());
            skill_fail(c, &s, id);
        }
    }
}
