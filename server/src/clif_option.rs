//! clif.cpp clif_parse_RemoveOption (CZ_REQ_CARTOFF 0x012a / 0x0b35): the client asks to put away its cart, falcon,
//! Peco Peco, dragon or Mado Gear (wave E archer agent: the falcon's release; one function for every option).

use crate::status::defs::{OPTION_DRAGON, OPTION_FALCON, OPTION_MADOGEAR, OPTION_RIDING};
use crate::world::Ctx;

/// clif.cpp clif_parse_RemoveOption (NEW_CARTS, config/core.hpp for PACKETVER >= 20120201): the cart goes only when
/// no mount / falcon / Mado Gear option is set ("priority to remove this option before we can clear cart"); else
/// those options are all cleared.
pub fn clif_parse_remove_option(c: &Ctx, aid: u32) {
    let Some(u) = crate::unit::get(c, aid) else { return };
    let option = u.sc.option;
    let mounts = OPTION_RIDING | OPTION_FALCON | OPTION_DRAGON | OPTION_MADOGEAR;
    if option & mounts == 0 && crate::status::sc::has_sce(c, aid, crate::status::consts::SC_PUSH_CART) {
        crate::cart::pc_setcart(c, aid, 0);
    } else {
        // priority to remove this option before we can clear cart
        crate::skilltree::pc_setoption(c, aid, option & !mounts, 0);
    }
}
