//! pc.cpp pc_changelook: a look change the character keeps (sd->status, capped by conf/battle/client.conf), shown
//! with clif_changelook. Used by the stylist; the `changelook` / `setlook` script commands can call it too.

use crate::conf::{max_cloth_color, max_hair_color, max_hair_style, min_cloth_color, min_hair_color, min_hair_style};
use crate::inventory as inv;
use crate::world::Ctx;

/// script_constants LOOK_*.
pub const LOOK_HAIR: i32 = 1;
pub const LOOK_WEAPON: i32 = 2;
pub const LOOK_HEAD_BOTTOM: i32 = 3;
pub const LOOK_HEAD_TOP: i32 = 4;
pub const LOOK_HEAD_MID: i32 = 5;
pub const LOOK_HAIR_COLOR: i32 = 6;
pub const LOOK_CLOTHES_COLOR: i32 = 7;
pub const LOOK_SHIELD: i32 = 8;
pub const LOOK_SHOES: i32 = 9;
pub const LOOK_ROBE: i32 = 12;
pub const LOOK_BODY2: i32 = 13;

/// pc.cpp pc_changelook.
pub fn pc_changelook(c: &Ctx, aid: u32, look: i32, val: i32) {
    let Some(mut ch) = inv::status(c, aid) else { return };
    let mut val = val;
    match look {
        LOOK_HAIR => {
            // Use the battle_config limits! [Skotlex]
            val = val.clamp(min_hair_style(), max_hair_style());
            if ch.hair as i32 != val {
                ch.hair = val as u16;
                inv::put_status(c, &ch);
                if ch.guild_id != 0 {
                    // Update Guild Window. [Skotlex]
                    crate::guild::intif_guild_change_memberinfo(ch.guild_id as i32, aid, ch.id, crate::int_guild::GMI_HAIR, ch.hair as i64);
                }
            }
        }
        LOOK_WEAPON => {
            ch.weapon = val as u16;
            inv::put_status(c, &ch);
        }
        LOOK_HEAD_BOTTOM | LOOK_HEAD_TOP | LOOK_HEAD_MID => {
            match look {
                LOOK_HEAD_BOTTOM => ch.head_bottom = val as u16,
                LOOK_HEAD_TOP => ch.head_top = val as u16,
                _ => ch.head_mid = val as u16,
            }
            inv::put_status(c, &ch);
            if let Some(mut s) = inv::st(c, aid) {
                match look {
                    LOOK_HEAD_BOTTOM => s.setlook_head_bottom = val,
                    LOOK_HEAD_TOP => s.setlook_head_top = val,
                    _ => s.setlook_head_mid = val,
                }
                inv::put_st(c, &s);
            }
        }
        LOOK_HAIR_COLOR => {
            val = val.clamp(min_hair_color(), max_hair_color());
            if ch.hair_color as i32 != val {
                ch.hair_color = val as u16;
                inv::put_status(c, &ch);
                if ch.guild_id != 0 {
                    crate::guild::intif_guild_change_memberinfo(ch.guild_id as i32, aid, ch.id, crate::int_guild::GMI_HAIR_COLOR, ch.hair_color as i64);
                }
            }
        }
        LOOK_CLOTHES_COLOR => {
            val = val.clamp(min_cloth_color(), max_cloth_color());
            ch.clothes_color = val as u16;
            inv::put_status(c, &ch);
        }
        LOOK_SHIELD => {
            ch.shield = val as u16;
            inv::put_status(c, &ch);
        }
        LOOK_SHOES => {}
        LOOK_ROBE => {
            ch.robe = val as u16;
            inv::put_status(c, &ch);
            if let Some(mut s) = inv::st(c, aid) {
                s.setlook_robe = val;
                inv::put_st(c, &s);
            }
        }
        LOOK_BODY2 => {
            if val < 0 || val > u16::MAX as i32 || crate::db::jobs::job_db().find(val as u16).is_none() {
                return;
            }
            ch.body = val as u16;
            inv::put_status(c, &ch);
        }
        _ => {}
    }
    crate::pc_equip::clif_changelook(c, aid, look as u8, val);
}
