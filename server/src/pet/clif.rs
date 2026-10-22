//! The pet packets of clif.cpp: clif_catch_process, clif_pet_roulette, clif_sendegg, clif_send_petdata,
//! clif_send_petstatus, clif_pet_emotion, clif_pet_food, clif_pet_autofeed_status, clif_pet_evolution_result, and the
//! pet request handlers clif_parse_PetMenu, clif_parse_CatchPet, clif_parse_SelectEgg, clif_parse_SendEmotion,
//! clif_parse_ChangePetName, clif_parse_pet_evolution, plus clif_parse_configuration (CZ_CONFIG, whose
//! CONFIG_PET_AUTOFEED arm turns autofeed on; the other arms are ported with it, nobody else dispatched the packet).

use super::data::{self, PetData, PET_INTIMATE_CORDIAL, PET_INTIMATE_LOYAL};
use crate::clif::{self, Target};
use crate::net::packets::*;
use crate::unit;
use crate::world::Ctx;

/// clif.hpp enum e_changestate_pet.
pub const CHANGESTATEPET_INIT: i8 = 0;
pub const CHANGESTATEPET_INTIMACY: i8 = 1;
pub const CHANGESTATEPET_HUNGER: i8 = 2;
pub const CHANGESTATEPET_ACCESSORY: i8 = 3;
pub const CHANGESTATEPET_PERFORMANCE: i8 = 4;
pub const CHANGESTATEPET_HAIRSTYLE: i8 = 5;
pub const CHANGESTATEPET_UPDATE_EGG: i8 = 6;

/// clif.hpp enum class e_pet_evolution_result.
pub const PET_EVOLUTION_FAIL_UNKNOWN: u32 = 0x0;
pub const PET_EVOLUTION_FAIL_NOTEXIST_CALLPET: u32 = 0x1;
pub const PET_EVOLUTION_FAIL_NOT_PETEGG: u32 = 0x2;
pub const PET_EVOLUTION_FAIL_RECIPE: u32 = 0x3;
pub const PET_EVOLUTION_FAIL_MATERIAL: u32 = 0x4;
pub const PET_EVOLUTION_FAIL_RG_FAMILIAR: u32 = 0x5;
pub const PET_EVOLUTION_SUCCESS: u32 = 0x6;

/// Sends to the player `aid` (clif_send(…, sd, SELF)).
fn send_self(c: &Ctx, aid: u32, buf: &[u8]) {
    if let Some(sd) = unit::get(c, aid) {
        clif::send(c, buf, &sd, Target::SelfOnly);
    }
}

/// clif.cpp clif_catch_process: ZC_START_CAPTURE, the taming cursor.
pub fn clif_catch_process(c: &Ctx, aid: u32) {
    let p = PACKET_ZC_START_CAPTURE { PacketType: PACKET_ZC_START_CAPTURE::ID as i16 };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_pet_roulette: ZC_TRYCAPTURE_MONSTER (0 failure, 1 success).
pub fn clif_pet_roulette(c: &Ctx, aid: u32, success: bool) {
    let p = PACKET_ZC_TRYCAPTURE_MONSTER { PacketType: PACKET_ZC_TRYCAPTURE_MONSTER::ID as i16, result: success as i8 };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_sendegg: ZC_PETEGG_LIST (0x01a6), the eggs the player can hatch; the incubator's menu is open from
/// then on (menuskill_id SA_TAMINGMONSTER, menuskill_val -1). On a GvG map during Guild Wars with pet_disable_in_gvg
/// the player gets message 666 instead.
pub fn clif_sendegg(c: &Ctx, aid: u32) {
    let Some(sd) = unit::get(c, aid) else { return };
    if super::pet_no_gvg() && crate::status::map_flag_gvg2(c, sd.map) {
        // Disable pet hatching in GvG grounds during Guild Wars [Skotlex]
        clif::displaymessage(c, &sd, crate::data::msg_txt(666));
        return;
    }
    let mut w = Vec::with_capacity(crate::inventory::MAX_INVENTORY * 2 + 4);
    w.extend_from_slice(&0x01a6u16.to_le_bytes());
    w.extend_from_slice(&0u16.to_le_bytes());
    for r in crate::inventory::inv_list(c, aid) {
        let id = crate::inventory::data_of(&r.item);
        if r.item.nameid == 0 || id.r#type != crate::db::items::IT_PETEGG || r.item.amount <= 0 {
            continue;
        }
        w.extend_from_slice(&crate::clif_items::client_index(r.idx as usize).to_le_bytes());
    }
    let len = w.len() as u16;
    w[2..4].copy_from_slice(&len.to_le_bytes());
    clif::send(c, &w, &sd, Target::SelfOnly);
    crate::itemui::set_menu(c, aid, crate::db::skills::ids::SA_TAMINGMONSTER as i32, -1, crate::itemui::menu(c, aid).2);
}

/// clif.cpp clif_send_petdata: ZC_CHANGESTATE_PET. `sd` Some = to that player only, None = to the pet's area.
pub fn clif_send_petdata(c: &Ctx, sd: Option<u32>, pd: &PetData, data_type: i8) {
    let Some(pu) = data::unit_of(c, pd) else { return };
    let value: i32 = match data_type {
        CHANGESTATEPET_INIT => 0,
        CHANGESTATEPET_INTIMACY => pd.pet.intimate as i32,
        CHANGESTATEPET_HUNGER => pd.pet.hungry as i32,
        CHANGESTATEPET_ACCESSORY => pu.vd.head_bottom,
        CHANGESTATEPET_PERFORMANCE => {
            // data = 1~3: normal, 4: special
            let num = if pd.pet.intimate as i32 > PET_INTIMATE_LOYAL {
                if data::pet_db_of(pd).map(|p| p.s_perfor).unwrap_or(false) { 4 } else { 3 }
            } else if pd.pet.intimate as i32 > PET_INTIMATE_CORDIAL {
                //TODO: this is way too high
                2
            } else {
                1
            };
            c.rnd_value(1, num)
        }
        CHANGESTATEPET_HAIRSTYLE => super::pet_hair_style(),
        CHANGESTATEPET_UPDATE_EGG => {
            if pd.pet.intimate == 1 {
                0
            } else {
                1
            }
        }
        _ => return, // shouldn't happen
    };
    let p = PACKET_ZC_CHANGESTATE_PET { PacketType: PACKET_ZC_CHANGESTATE_PET::ID as i16, r#type: data_type, GID: pd.id as i32, data: value };
    match sd {
        Some(aid) => send_self(c, aid, &p.to_vec()),
        None => clif::send(c, &p.to_vec(), &pu, Target::Area),
    }
}

/// The BL_PET arms of clif.cpp clif_spawn / clif_move (`sd` None: to the area) and clif_getareachar_unit (to `sd`):
/// a pet that wears an accessory (vd->look[LOOK_HEAD_BOTTOM] != 0) sends CHANGESTATEPET_ACCESSORY after its unit
/// packet.
pub fn spawn_pet(c: &Ctx, pu: &unit::Unit, sd: Option<&unit::Unit>) {
    if pu.vd.head_bottom == 0 {
        return;
    }
    let Some(pd) = data::get(c, pu.id) else { return };
    clif_send_petdata(c, sd.map(|s| s.id), &pd, CHANGESTATEPET_ACCESSORY);
}

/// clif.cpp clif_send_petstatus: ZC_PROPERTY_PET, the pet's base data (PACKETVER >= 20081126: with its class).
pub fn clif_send_petstatus(c: &Ctx, aid: u32, pd: &PetData) {
    let p = PACKET_ZC_PROPERTY_PET {
        PacketType: PACKET_ZC_PROPERTY_PET::ID as i16,
        szName: pd.pet.name.clone(),
        bModified: if super::pet_rename() { 0 } else { pd.pet.rename_flag },
        nLevel: pd.pet.level,
        nFullness: pd.pet.hungry,
        nRelationship: pd.pet.intimate,
        ITID: pd.pet.equip as i16,
        job: pd.pet.class_,
    };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_pet_emotion: ZC_PET_ACT to the pet's area.
pub fn clif_pet_emotion(c: &Ctx, pd: &PetData, param: i32) {
    let Some(pu) = data::unit_of(c, pd) else { return };
    let p = PACKET_ZC_PET_ACT { packetType: PACKET_ZC_PET_ACT::ID as i16, GID: pd.id, data: param };
    clif::send(c, &p.to_vec(), &pu, Target::Area);
}

/// clif.cpp clif_pet_food: ZC_FEED_PET (0 failure, 1 success) with the food's client item id.
pub fn clif_pet_food(c: &Ctx, aid: u32, foodid: u32, success: bool) {
    let p = PACKET_ZC_FEED_PET { packetType: PACKET_ZC_FEED_PET::ID as i16, result: success as u8, itemId: crate::db::items::client_nameid(foodid) };
    send_self(c, aid, &p.to_vec());
}

/// clif.cpp clif_pet_autofeed_status (PACKETVER >= 20141008): with `force` or pet_autofeed_always the setting is always
/// sent (ON only with a pet whose autofeed is on); otherwise only when it is on.
pub fn clif_pet_autofeed_status(c: &Ctx, aid: u32, force: bool) {
    let Some(sd) = unit::get(c, aid) else { return };
    let pd = data::of_master(c, aid);
    if force || super::pet_autofeed_always() {
        // Always send ON or OFF
        match pd {
            Some(pd) if super::feature_pet_autofeed() => crate::pc::configuration(c, &sd, crate::pc::CONFIG_PET_AUTOFEED, pd.pet.autofeed),
            _ => crate::pc::configuration(c, &sd, crate::pc::CONFIG_PET_AUTOFEED, false),
        }
    } else {
        // Only send when enabled
        if let Some(pd) = pd {
            if super::feature_pet_autofeed() && pd.pet.autofeed {
                crate::pc::configuration(c, &sd, crate::pc::CONFIG_PET_AUTOFEED, true);
            }
        }
    }
}

/// clif.cpp clif_pet_evolution_result (PACKETVER >= 20141008): ZC_PET_EVOLUTION_RESULT (0x09fc) result.L.
pub fn clif_pet_evolution_result(c: &Ctx, aid: u32, result: u32) {
    let mut w = Vec::with_capacity(6);
    w.extend_from_slice(&0x09fcu16.to_le_bytes());
    w.extend_from_slice(&result.to_le_bytes());
    send_self(c, aid, &w);
}

// ---------------------------------------------------------------------------------------------- requests

fn field_u8(pkt: &[u8], cmd: u16, n: usize) -> Option<u8> {
    let o = *packet_pos(cmd).get(n)? as usize;
    pkt.get(o).copied()
}
fn field_u16(pkt: &[u8], cmd: u16, n: usize) -> Option<u16> {
    let o = *packet_pos(cmd).get(n)? as usize;
    Some(u16::from_le_bytes([*pkt.get(o)?, *pkt.get(o + 1)?]))
}
fn field_u32(pkt: &[u8], cmd: u16, n: usize) -> Option<u32> {
    let o = *packet_pos(cmd).get(n)? as usize;
    Some(u32::from_le_bytes([*pkt.get(o)?, *pkt.get(o + 1)?, *pkt.get(o + 2)?, *pkt.get(o + 3)?]))
}

/// The pet request handlers (net/mod.rs dispatches here by clif_parse_* name); false when `func` is not one of them.
pub fn parse(c: &Ctx, aid: u32, func: &str, pkt: &[u8]) -> bool {
    let cmd = u16::from_le_bytes([pkt[0], pkt[1]]);
    match func {
        // clif_parse_PetMenu (CZ_COMMAND_PET): 0 information, 1 feed, 2 performance, 3 return to egg, 4 unequip
        "clif_parse_PetMenu" => {
            if let Some(menunum) = field_u8(pkt, cmd, 0) {
                super::pet_menu(c, aid, menunum as i32);
            }
        }
        // clif_parse_CatchPet (CZ_TRYCAPTURE_MONSTER)
        "clif_parse_CatchPet" => {
            if let Some(target_id) = field_u32(pkt, cmd, 0) {
                super::pet_catch_process_end(c, aid, target_id);
            }
        }
        // clif_parse_SelectEgg (CZ_SELECT_PETEGG): only while the incubator's list is open
        "clif_parse_SelectEgg" => {
            let (id, val, _) = crate::itemui::menu(c, aid);
            if id != crate::db::skills::ids::SA_TAMINGMONSTER as i32 || val != -1 {
                return true;
            }
            if let Some(index) = field_u16(pkt, cmd, 0) {
                super::pet_select_egg(c, aid, (index as i16).wrapping_sub(2));
            }
            crate::itemui::clif_menuskill_clear(c, aid);
        }
        // clif_parse_SendEmotion (CZ_PET_ACT): the pet's emotion / talk to its area
        "clif_parse_SendEmotion" => {
            if let Some(pd) = data::of_master(c, aid) {
                if let Some(v) = field_u32(pkt, cmd, 0) {
                    clif_pet_emotion(c, &pd, v as i32);
                }
            }
        }
        // clif_parse_ChangePetName (CZ_RENAME_PET): the 24-byte name field
        "clif_parse_ChangePetName" => {
            let o = packet_pos(cmd).first().copied().unwrap_or(2) as usize;
            let raw = pkt.get(o..(o + crate::mob::NAME_LENGTH).min(pkt.len())).unwrap_or(&[]);
            super::pet_change_name(c, aid, raw);
        }
        // clif_parse_pet_evolution (CZ_PET_EVOLUTION, PACKETVER >= 20141008)
        "clif_parse_pet_evolution" => {
            let mut r = crate::net::wire::Reader::new(pkt);
            let Some(p) = PACKET_CZ_PET_EVOLUTION::read(&mut r) else { return true };
            match crate::db::misc::pet::pet_db_search(p.EvolvedPetEggID as i32, crate::db::misc::pet::PetItemType::Egg) {
                None => clif_pet_evolution_result(c, aid, PET_EVOLUTION_FAIL_NOT_PETEGG),
                Some(pet) => super::pet_evolution(c, aid, pet.class_ as i16),
            }
        }
        "clif_parse_configuration" => clif_parse_configuration(c, aid, cmd, pkt),
        _ => return false,
    }
    true
}

/// clif.cpp clif_parse_configuration (CZ_CONFIG): the equipment window, /call, pet and homunculus autofeed and the
/// costume switch, then the setting is echoed (clif_configuration).
fn clif_parse_configuration(c: &Ctx, aid: u32, cmd: u16, pkt: &[u8]) {
    use crate::pc::{CONFIG_CALL, CONFIG_HOMUNCULUS_AUTOFEED, CONFIG_OPEN_EQUIPMENT_WINDOW, CONFIG_PET_AUTOFEED};
    /// clif.hpp CONFIG_DISABLE_SHOWCOSTUMES.
    const CONFIG_DISABLE_SHOWCOSTUMES: u32 = 4;
    let (Some(type_), Some(flag)) = (field_u32(pkt, cmd, 0), field_u32(pkt, cmd, 1)) else { return };
    let flag = flag != 0;
    match type_ {
        CONFIG_OPEN_EQUIPMENT_WINDOW => {
            if let Some(mut ch) = crate::pc::character_of(c, aid) {
                ch.show_equip = flag as u8;
                crate::status::put_char(c, &ch);
            }
        }
        CONFIG_CALL => {
            if let Some(mut ch) = crate::pc::character_of(c, aid) {
                ch.disable_call = flag as u8;
                crate::status::put_char(c, &ch);
            }
        }
        CONFIG_PET_AUTOFEED => {
            // Player can not click this if he does not have a pet
            let Some(mut pd) = data::of_master(c, aid) else { return };
            if !super::feature_pet_autofeed() || !data::pet_db_of(&pd).map(|p| p.allow_autofeed).unwrap_or(false) {
                return;
            }
            pd.pet.autofeed = flag;
            data::put(c, &pd);
        }
        CONFIG_HOMUNCULUS_AUTOFEED => {
            // Player can not click this if he does not have a homunculus or it is vaporized (homunculus/clif.rs)
            if !crate::homunculus::clif::config_autofeed(c, aid, flag) {
                return;
            }
        }
        CONFIG_DISABLE_SHOWCOSTUMES => {
            if let Some(mut ch) = crate::pc::character_of(c, aid) {
                ch.disable_showcostumes = flag as u8;
                crate::status::put_char(c, &ch);
            }
            crate::pc_equip::pc_set_costume_view(c, aid);
        }
        _ => {
            log::warn!("clif_parse_configuration: received unknown configuration type '{type_}'...");
            return;
        }
    }
    if let Some(sd) = unit::get(c, aid) {
        crate::pc::configuration(c, &sd, type_, flag);
    }
}
