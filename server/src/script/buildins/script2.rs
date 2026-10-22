//! The general script commands of wave C's script2 agent (server/ARCHITECTURE.md minus the systems other
//! agents own), one file per subject under buildins/script2/. Progress, stubs and owners: server/ARCHITECTURE.md.

pub mod admin;
pub mod chanscript;
pub mod effects;
pub mod family;
pub mod mapctl;
pub mod looks;
pub mod mapmisc;
pub mod misc2;
pub mod npcchat;
pub mod npcctl2;
pub mod pcre;
pub mod ui;
pub mod unitctl;
pub mod unitinfo;
pub mod unitmob;
pub mod unitmove;

use super::BuildinFn;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    unitctl::lookup(cfunc).or_else(|| unitinfo::lookup(cfunc)).or_else(|| mapctl::lookup(cfunc)).or_else(|| mapmisc::lookup(cfunc)).or_else(|| ui::lookup(cfunc)).or_else(|| looks::lookup(cfunc)).or_else(|| unitmove::lookup(cfunc)).or_else(|| effects::lookup(cfunc)).or_else(|| admin::lookup(cfunc)).or_else(|| family::lookup(cfunc)).or_else(|| npcctl2::lookup(cfunc)).or_else(|| misc2::lookup(cfunc)).or_else(|| pcre::lookup(cfunc)).or_else(|| npcchat::lookup(cfunc)).or_else(|| chanscript::lookup(cfunc))
}
