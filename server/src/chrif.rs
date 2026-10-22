//! chrif.cpp chrif_save for a player who STAYS in the world (every call site but map_quit, whose CSAVE_QUIT sequence
//! is pc.rs `quit`): the one save every system calls where rAthena calls `chrif_save(sd, flag)`.
//!
//! In rAthena the map server keeps the character in memory and the char server writes it to SQL when it is saved. Here
//! most of the character (status, registries, skills, hotkeys, friends, bonus scripts) is a table row written as it
//! changes, so those parts of chrif_save have nothing to do. What a save does change, and what a later login can
//! see, is kept: the save position (pc_makesavestatus), the item tables (the char server gives new items their SQL
//! ids, which decide their order at the next login), a dirty storage, the guild storage, the quest log and the
//! achievement log (their row order is the order of the saves).

use crate::world::Ctx;

/// chrif.hpp enum e_chrif_save_opt.
pub const CSAVE_NORMAL: i32 = 0x00;
pub const CSAVE_QUIT: i32 = 0x01;
pub const CSAVE_CHANGE_MAPSERV: i32 = 0x02;
pub const CSAVE_AUTOTRADE: i32 = 0x04;
pub const CSAVE_INVENTORY: i32 = 0x08;
pub const CSAVE_CART: i32 = 0x10;
pub const CSAVE_QUITTING: i32 = CSAVE_QUIT | CSAVE_CHANGE_MAPSERV | CSAVE_AUTOTRADE;

/// chrif.cpp chrif_save. `flag` is CSAVE_NORMAL, CSAVE_INVENTORY and / or CSAVE_CART; the quitting flags belong to
/// map_quit (pc.rs `quit`) and autotrade (vending), which keep their own sequences.
pub fn chrif_save(c: &Ctx, aid: u32, flag: i32) {
    let Some(p) = crate::pc::get(c, aid) else { return };
    crate::pc::makesavestatus(c, aid);
    // (flag & CSAVE_QUITTING: chrif_save_scdata, chrif_skillcooldown_save, chrif_auth_logout: map_quit)
    // chrif_bsdata_save(sd, false): the bonus scripts are rows, their timers keep running
    // if (sd->storage.dirty) storage_storagesave(sd);
    if crate::inventory::st(c, aid).map(|s| s.storage_dirty).unwrap_or(false) {
        crate::storage::storage_tosql(c, aid);
    }
    if flag & CSAVE_INVENTORY != 0 {
        crate::inventory::inventory_tosql(c, p.char_id); // intif_storage_save(sd, &sd->inventory)
    }
    if flag & CSAVE_CART != 0 {
        crate::cart::cart_tosql(c, p.char_id); // intif_storage_save(sd, &sd->cart)
    }
    // For data sync
    if p.storage_flag == 2 {
        crate::guild_storage::storage_guild_storagesave(c, aid, crate::guild::guild_id_of(c, aid), flag & CSAVE_QUIT != 0);
    }
    // (premium storages are not in this build; sd->vars_dirty: the registries are rows; 0x2b01: the status is a row)
    // if( sd->status.pet_id > 0 && sd->pd ) intif_save_petdata(sd->status.account_id,&sd->pd->pet);
    crate::pet::chrif_save_pet(c, aid);
    // if( hom_is_active(sd->hd) ) hom_save(sd->hd); (chrif.cpp:332-333, homunculus.rs)
    crate::homunculus::chrif_save(c, aid);
    // mercenary_save / elemental_save (chrif.cpp:334-337; mercenary.rs, elemental.rs), and the char server's
    // char_mmo_char_tosql writes the owner's mercenary_owner row (mercenary_owner_tosql)
    crate::mercenary::chrif_save(c, aid);
    crate::elemental::chrif_save(c, aid);
    crate::mercenary::chrif_save_owner(c, aid);
    // if (sd->save_quest) intif_quest_save(sd);
    crate::quest::chrif_save(c, aid);
    // if (sd->achievement_data.save) intif_achievement_save(sd);
    crate::achievement::chrif_save(c, aid);
}
