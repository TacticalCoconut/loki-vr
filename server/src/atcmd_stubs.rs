//! map/atcommand.cpp: the @commands whose systems another agent or a later phase ports (atcmd agent, wave D). Each
//! ACMD_FUNC below is a stub naming its owner; the owner's own command file (chained before this one in atcommand.rs
//! `run`) serves the command once it exists, so nothing here shadows it. Anything else the command table names that
//! no file serves ends here too.

use crate::unit::Unit;
use crate::world::Ctx;

/// (ACMD_FUNC, owner): the stubs, with the system the command acts on.
pub const OWNERS: &[(&str, &str)] = &[
    // (pet, woe, vending / duel, the skill pipeline's @commands, @spiritball / @soulball, @displayskillunit and
    // @reloadinstancedb are served by their owners' command files, chained before this one in atcommand.rs: pet,
    // woe, vending, skillcore, skillunit and instance agents, wave D; `@reload instancedb` is atcommand_admin.rs's;
    // the homunculus @commands are homunculus/cmd.rs's, homun agent, wave E)
    // (@loadnpc, @reloadnpcfile, @reloadscript and `@reload script`: atcommand_world.rs / atcommand_admin.rs with
    // npc_load.rs, wave F npcload; @roulette: roulette.rs, wave F roulette)
    // @mapexit: global_core->signal_shutdown() stops the map server (do_final: map_quit of every player, then the
    // process ends). A SpacetimeDB module cannot stop its host, and quitting every character while their clients stay
    // connected would not be the same thing: no port.
    ("mapexit", "none (the module cannot shut its SpacetimeDB host down)"),
];

/// The command has no port on this server yet: the reply atcommand.rs always gave for it ("%s is not available on
/// this server yet.", a Loki message: rAthena has every command), and 0 (no "%s failed.").
pub fn not_ported(c: &Ctx, fd: Option<&Unit>, func: &str, command: &str) -> i32 {
    let owner = OWNERS.iter().find(|(f, _)| *f == func).map(|(_, o)| *o).unwrap_or("unknown");
    log::info!("atcommand: {command} ({func}) is not ported yet (owner: {owner})");
    if let Some(u) = fd {
        crate::clif::displaymessage(c, u, &format!("{command} is not available on this server yet."));
    }
    0
}
