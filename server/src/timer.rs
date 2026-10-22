//! rAthena's timer system (common/timer.cpp) as tables: the live timers (`timer`), rAthena's binary heap of them
//! ([`heap`]: the order do_timer pops them in, equal ticks included), and one scheduled "wakeup" row that SpacetimeDB
//! fires at the top of the heap. Every add_timer / delete_timer / do_timer call in the
//! ported code maps onto [`add`], [`delete`] and [`on_wakeup`], with the same semantics: a timer function gets the
//! timer's own tick unless it runs more than a second late (then the current tick), and interval timers are re-armed
//! from their previous due tick.

use spacetimedb::{reducer, table, ReducerContext, ScheduleAt, Table, Timestamp};

use crate::pc::pc as _;
use crate::world::{Ctx, Tick};

pub mod heap;

/// What a timer calls (rAthena's TimerFunc pointers). Numbered explicitly: the value is stored in rows.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[repr(u16)]
pub enum Func {
    /// unit.cpp unit_walktoxy_timer (target = unit id, data = the step's speed).
    UnitWalk = 1,
    /// unit.cpp unit_delay_walktoxy_timer (data = x << 16 | y).
    UnitDelayWalk = 2,
    /// clif.cpp clif_delayquit (target = account id).
    DelayQuit = 3,
    /// pc.cpp pc_invincible_timer.
    PcInvincible = 4,
    /// script.cpp run_script_timer (target 0, data = script_state id; the char id is the state's sleep.charid).
    ScriptSleep = 5,
    /// npc.cpp npc_timerevent (target = NPC id; the timer_event_data is the `npc_timer_ted` row of this timer id).
    NpcTimerEvent = 6,
    /// npc.cpp npc_event_do_clock (interval 1000 ms).
    NpcEventDoClock = 7,
    /// npc.cpp npc_secure_timeout_timer (target = account id).
    NpcSecureTimeout = 8,
    /// pc.cpp pc_close_npc_timer (target = account id, data = flag).
    PcCloseNpc = 9,
    /// pc.cpp pc_eventtimer (target = account id; the event name is in the player's eventtimer slot).
    PcEventTimer = 10,
    /// status.cpp status_change_timer (target = unit id, data = sc_type).
    StatusChangeTimer = 100,
    /// status.cpp status_change_start_timer (data = the `sc_delay` row id).
    StatusChangeStartDelay = 101,
    /// status.cpp status_natural_heal_timer (interval NATURAL_HEAL_INTERVAL).
    StatusNaturalHeal = 102,
    /// pc.cpp pc_bonus_script_timer (target = account id, data = the `bonus_script` row id).
    StatusBonusScript = 103,
    /// pc.cpp pc_endautobonus (target = account id, data = which autobonus list: 1, 2 or 3).
    StatusEndAutobonus = 104,
    /// status.cpp status_clear_lastEffect_timer (target = unit id).
    StatusClearLastEffect = 105,
    /// map.cpp map_clearflooritem_timer (target = floor item id).
    MapClearFloorItem = 130,
    /// pc.cpp pc_inventory_rental_end (target = account id).
    PcInventoryRental = 131,
    /// party.cpp party_send_xy_timer (interval party_update_interval).
    PartySendXy = 180,
    /// char.cpp char_clan_member_cleanup (interval 60 minutes; extras block 350-379).
    ClanMemberCleanup = 350,
    /// char/int_mail.cpp mail_return_timer (interval 1 minute).
    MailReturnTimer = 320,
    /// char/int_mail.cpp mail_delete_timer (interval 1 minute).
    MailDeleteTimer = 321,
    /// cashshop.cpp sale_start_timer (target = the item id).
    SaleStartTimer = 322,
    /// cashshop.cpp sale_end_timer (target = the item id).
    SaleEndTimer = 323,
    /// mob.cpp mob_ai_hard (interval MIN_MOBTHINKTIME).
    MobAiHard = 200,
    /// mob.cpp mob_ai_lazy (interval MIN_MOBTHINKTIME * 10).
    MobAiLazy = 201,
    /// mob.cpp mob_delayspawn (target = monster id).
    MobDelaySpawn = 202,
    /// mob.cpp mob_delay_item_drop (target = monster id).
    MobDelayItemDrop = 203,
    /// mob.cpp mob_timer_delete (target = monster id).
    MobTimerDelete = 204,
    /// mob.cpp mob_respawn (target = monster id, data = HP percentage).
    MobRespawn = 205,
    /// mob.cpp mob_attacked (target = monster id, data = attacker id).
    MobAttacked = 206,
    /// mob.cpp mob_norm_attacked (target = monster id, data = attacker id).
    MobNormAttacked = 207,
    /// map.cpp map_removemobs_timer (target = map id).
    MapRemoveMobs = 208,
    /// mob.cpp mvptomb_delayspawn (target = tomb NPC id).
    MvpTombDelaySpawn = 209,
    /// clif.cpp clif_clearunit_delayed_sub (target = unit id, data = type << 48 | map << 32 | x << 16 | y).
    ClifClearUnitDelayed = 210,
    /// unit.cpp unit_walktobl_sub (target = unit id, data = the chase target).
    UnitWalktoblSub = 211,
    /// unit.cpp unit_delay_walktobl_timer (target = unit id, data = the target).
    UnitDelayWalktobl = 212,
    /// unit.cpp unit_attack_timer (target = unit id).
    UnitAttack = 240,
    /// battle.cpp battle_delay_damage_sub (data = the `battle_delay_damage` row id).
    BattleDelayDamage = 241,
    /// unit.cpp unit_step_timer (target = unit id).
    UnitStep = 242,
    /// pc.cpp pc_respawn_timer (target = account id).
    PcRespawn = 245,
    /// pc.cpp pc_calc_pvprank_timer (target = account id).
    PcCalcPvprank = 246,
    /// char/int_guild.cpp guild_save_timer (re-armed by itself: autosave_interval / number of guilds).
    GuildSaveTimer = 290,
    /// guild.cpp guild_payexp_timer (interval GUILD_PAYEXP_INTERVAL).
    GuildPayexpTimer = 291,
    /// guild.cpp guild_send_xy_timer (interval GUILD_SEND_XY_INTERVAL).
    GuildSendXyTimer = 292,
    /// pc.cpp pc_autosave (autosave.rs).
    PcAutosave = 430,
    /// pc.cpp pc_follow_timer (target = account id).
    PcFollow = 400,
    /// chrif_divorce's round trip: char_divorce_char_sql + chrif_divorceack (target = char id, data = partner char id).
    ChrifDivorce = 401,
    /// pc.cpp pc_autotrade_timer (target = account id; vending block 570-579).
    PcAutotradeTimer = 570,
    /// skill.cpp skill_castend_id (target = the caster; skillcore block 440-499).
    SkillCastendId = 440,
    /// skill.cpp skill_castend_pos (target = the caster).
    SkillCastendPos = 441,
    /// skill.cpp skill_timerskill (target = the source, data = the ud->skilltimerskill slot).
    SkillTimerskill = 442,
    /// skill.cpp skill_blockpc_end (target = account id, data = skill id).
    SkillBlockpcEnd = 443,
    /// skill.cpp skill_keep_using (target = account id).
    SkillKeepUsing = 444,
    /// pc.cpp pc_spiritball_timer (target = account id).
    PcSpiritball = 445,
    /// pc.cpp pc_spiritcharm_timer (target = account id).
    PcSpiritcharm = 446,
    /// unit.cpp unit_shadowscar_timer (target = unit id).
    UnitShadowScar = 447,
    /// skill.cpp skill_unit_timer (interval SKILLUNITTIMER_INTERVAL; skillunit block 500-539).
    SkillUnitTimer = 500,
    /// pet.cpp pet_hungry (target = the owner's account id; pet block 660-689).
    PetHungry = 660,
    /// pet.cpp pet_ai_hard (interval MIN_PETTHINKTIME).
    PetAiHard = 661,
    /// pet.cpp pet_skill_bonus_timer (target = the owner's account id).
    PetSkillBonusTimer = 662,
    /// pet.cpp pet_delay_item_drop (target = the pet's block id).
    PetDelayItemDrop = 663,
    /// pet.cpp pet_skill_support_timer (target = the owner's account id).
    PetSkillSupportTimer = 664,
    /// pet.cpp pet_recovery_timer (target = the owner's account id).
    PetRecoveryTimer = 665,
    /// pet.cpp pet_heal_timer (target = the owner's account id).
    PetHealTimer = 666,
    /// pet.cpp pet_endautobonus (target = the owner's account id, data = the pet's autobonus list 1, 2 or 3).
    PetEndAutobonus = 667,
    /// unit.cpp unit_teleport_timer for a pet (target = the pet's block id, data = the distance).
    UnitTeleportTimer = 668,
    /// unit.cpp unit_resume_running (target = the running unit; archer block 720-739).
    UnitResumeRunning = 720,
    /// homunculus.cpp hom_hungry (target = the master's account id).
    HomHungry = 860,
    /// skill.cpp skill_blockhomun_end (target = the homunculus' block id, data = skill id).
    SkillBlockhomunEnd = 861,
    /// instance.cpp instance_delete_timer (target = instance id): an instance's keep or idle timer.
    InstanceDelete = 610,
    /// instance.cpp instance_subscription_timer (INSTANCE_INTERVAL): the subscription queue.
    InstanceSubscription = 611,
    /// mob.cpp mob_spawn_guardian_sub (target = monster id, data = guild id; woe block 580-609).
    MobSpawnGuardianSub = 580,
    /// battleground.cpp bg_send_xy_timer (interval bg_update_interval; wave D bg block 630-659).
    BgSendXyTimer = 630,
    /// battleground.cpp bg_on_ready_loopback (data = queue id).
    BgOnReadyLoopback = 631,
    /// battleground.cpp bg_on_ready_expire (data = queue id).
    BgOnReadyExpire = 632,
    /// battleground.cpp bg_on_ready_start (data = queue id).
    BgOnReadyStart = 633,
    /// pc.cpp pc_on_expire_active (target = account id).
    PcOnExpireActive = 634,
    /// mercenary.cpp merc_contract_end (target = the master's account id; mercelem block 900-949).
    MercContractEnd = 900,
    /// elemental.cpp elemental_summon_end (target = the master's account id).
    ElementalSummonEnd = 901,
    /// elemental.cpp elemental_ai_timer (interval MIN_ELETHINKTIME).
    ElementalAiTimer = 902,
    /// skill.cpp skill_blockmerc_end (target = the mercenary, data = skill id).
    SkillBlockmercEnd = 903,
    /// unit.cpp unit_teleport_timer for a mercenary / an elemental (target = its block id, data = the distance).
    CompanionTeleportTimer = 904,
    /// npc.cpp npc_dynamicnpc_removal_timer (target = the dynamic NPC's id; npcload block 970-989).
    NpcDynamicnpcRemoval = 970,
    /// pc.cpp pc_macro_detector_timeout (target = the player).
    PcMacroDetectorTimeout = 990,
    /// char/int_auction.cpp auction_end_timer (target = the auction id; gauction block 1040-1059).
    AuctionEndTimer = 1040,
}

impl Func {
    fn from_u16(v: u16) -> Option<Func> {
        Some(match v {
            1 => Func::UnitWalk,
            2 => Func::UnitDelayWalk,
            3 => Func::DelayQuit,
            4 => Func::PcInvincible,
            5 => Func::ScriptSleep,
            6 => Func::NpcTimerEvent,
            7 => Func::NpcEventDoClock,
            8 => Func::NpcSecureTimeout,
            9 => Func::PcCloseNpc,
            10 => Func::PcEventTimer,
            100 => Func::StatusChangeTimer,
            101 => Func::StatusChangeStartDelay,
            102 => Func::StatusNaturalHeal,
            103 => Func::StatusBonusScript,
            104 => Func::StatusEndAutobonus,
            105 => Func::StatusClearLastEffect,
            130 => Func::MapClearFloorItem,
            131 => Func::PcInventoryRental,
            180 => Func::PartySendXy,
            350 => Func::ClanMemberCleanup,
            320 => Func::MailReturnTimer,
            321 => Func::MailDeleteTimer,
            322 => Func::SaleStartTimer,
            323 => Func::SaleEndTimer,
            200 => Func::MobAiHard,
            201 => Func::MobAiLazy,
            202 => Func::MobDelaySpawn,
            203 => Func::MobDelayItemDrop,
            204 => Func::MobTimerDelete,
            205 => Func::MobRespawn,
            206 => Func::MobAttacked,
            207 => Func::MobNormAttacked,
            208 => Func::MapRemoveMobs,
            209 => Func::MvpTombDelaySpawn,
            210 => Func::ClifClearUnitDelayed,
            211 => Func::UnitWalktoblSub,
            212 => Func::UnitDelayWalktobl,
            240 => Func::UnitAttack,
            241 => Func::BattleDelayDamage,
            242 => Func::UnitStep,
            245 => Func::PcRespawn,
            246 => Func::PcCalcPvprank,
            290 => Func::GuildSaveTimer,
            291 => Func::GuildPayexpTimer,
            292 => Func::GuildSendXyTimer,
            430 => Func::PcAutosave,
            400 => Func::PcFollow,
            401 => Func::ChrifDivorce,
            570 => Func::PcAutotradeTimer,
            440 => Func::SkillCastendId,
            441 => Func::SkillCastendPos,
            442 => Func::SkillTimerskill,
            443 => Func::SkillBlockpcEnd,
            444 => Func::SkillKeepUsing,
            445 => Func::PcSpiritball,
            446 => Func::PcSpiritcharm,
            447 => Func::UnitShadowScar,
            500 => Func::SkillUnitTimer,
            660 => Func::PetHungry,
            661 => Func::PetAiHard,
            662 => Func::PetSkillBonusTimer,
            663 => Func::PetDelayItemDrop,
            664 => Func::PetSkillSupportTimer,
            665 => Func::PetRecoveryTimer,
            666 => Func::PetHealTimer,
            667 => Func::PetEndAutobonus,
            668 => Func::UnitTeleportTimer,
            720 => Func::UnitResumeRunning,
            860 => Func::HomHungry,
            861 => Func::SkillBlockhomunEnd,
            610 => Func::InstanceDelete,
            611 => Func::InstanceSubscription,
            580 => Func::MobSpawnGuardianSub,
            630 => Func::BgSendXyTimer,
            631 => Func::BgOnReadyLoopback,
            632 => Func::BgOnReadyExpire,
            633 => Func::BgOnReadyStart,
            634 => Func::PcOnExpireActive,
            900 => Func::MercContractEnd,
            901 => Func::ElementalSummonEnd,
            902 => Func::ElementalAiTimer,
            903 => Func::SkillBlockmercEnd,
            904 => Func::CompanionTeleportTimer,
            970 => Func::NpcDynamicnpcRemoval,
            990 => Func::PcMacroDetectorTimeout,
            1040 => Func::AuctionEndTimer,
            _ => return None,
        })
    }
}

/// A live timer (timer_data[tid] with a function). The order do_timer runs them in is [`heap`]'s; `by_due` is no longer
/// read by the loop (kept: dropping an index changes the schema).
#[table(accessor = timer, index(accessor = by_due, btree(columns = [due, id])), index(accessor = by_target, btree(columns = [target])))]
pub struct Timer {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub due: Tick,
    pub func: u16,
    pub target: u32,
    pub data: i64,
    /// 0 = once; otherwise add_timer_interval's interval.
    pub interval: i64,
}

#[table(accessor = wakeup, scheduled(on_wakeup))]
pub struct Wakeup {
    #[primary_key]
    #[auto_inc]
    pub scheduled_id: u64,
    pub scheduled_at: ScheduleAt,
    pub due: Tick,
}

/// The wakeup that is currently scheduled (one row, key 0).
#[table(accessor = timer_state)]
pub struct TimerState {
    #[primary_key]
    #[index(direct)]
    pub key: u8,
    pub pending_row: u64,
    pub pending_due: Tick,
}

fn state(c: &Ctx) -> TimerState {
    c.db().timer_state().key().find(0).unwrap_or(TimerState { key: 0, pending_row: 0, pending_due: i64::MAX })
}

fn put_state(c: &Ctx, s: TimerState) {
    if c.db().timer_state().key().find(0).is_some() {
        c.db().timer_state().key().update(s);
    } else {
        c.db().timer_state().insert(s);
    }
}

/// Makes sure a wakeup fires no later than `due`.
fn arm(c: &Ctx, due: Tick) {
    if c.executing_wakeup.get() != 0 {
        return;
    }
    let mut s = state(c);
    // Hibernate the world scheduler when no players are in the world on a live server (unit tests use synthetic epoch ticks <= 1e12).
    // `pc::authok` calls `timer::rearm(c)` the instant a player enters the world.
    if c.real_tick() > 1_000_000_000_000 && spacetimedb::Table::count(c.db().pc()) == 0 {
        if s.pending_row != 0 {
            c.db().wakeup().scheduled_id().delete(s.pending_row);
            s.pending_row = 0;
            s.pending_due = i64::MAX;
            put_state(c, s);
        }
        return;
    }
    // The pending wakeup must still exist: a scheduled row is consumed when its reducer runs, even if that run failed
    // and rolled back the state update — then nothing would ever be armed again.
    if s.pending_row != 0 && s.pending_row != c.executing_wakeup.get() && c.db().wakeup().scheduled_id().find(s.pending_row).is_none() {
        log::warn!("timer: pending wakeup {} (due {}) is gone; re-arming", s.pending_row, s.pending_due);
        s.pending_row = 0;
        s.pending_due = i64::MAX;
    }
    // A module update can leave the wakeup that was pending during the update with its row but no longer scheduled
    // (seen with `publish` over a running database): a pending wakeup more than a second overdue in another reducer
    // is replaced, or every later add would wait for it forever.
    if s.pending_row != 0 && s.pending_row != c.executing_wakeup.get() && s.pending_due < c.real_tick() - 1000 {
        log::warn!("timer: pending wakeup {} (due {}) is {} ms overdue; re-arming", s.pending_row, s.pending_due, c.real_tick() - s.pending_due);
        c.db().wakeup().scheduled_id().delete(s.pending_row);
        s.pending_row = 0;
        s.pending_due = i64::MAX;
    }
    if s.pending_row != 0 && s.pending_due <= due {
        return;
    }
    if s.pending_row != 0 && s.pending_row != c.executing_wakeup.get() {
        c.db().wakeup().scheduled_id().delete(s.pending_row);
    }
    let at = Timestamp::from_micros_since_unix_epoch(due.max(c.real_tick()) * 1000);
    let row = c.db().wakeup().insert(Wakeup { scheduled_id: 0, scheduled_at: ScheduleAt::Time(at), due });
    s.pending_row = row.scheduled_id;
    s.pending_due = due;
    put_state(c, s);
}

/// Disarms the pending wakeup immediately when the last player leaves the world.
pub fn disarm_if_empty(c: &Ctx) {
    if c.real_tick() > 1_000_000_000_000 && spacetimedb::Table::count(c.db().pc()) == 0 {
        let mut s = state(c);
        if s.pending_row != 0 {
            c.db().wakeup().scheduled_id().delete(s.pending_row);
            s.pending_row = 0;
            s.pending_due = i64::MAX;
            put_state(c, s);
        }
    }
}

/// add_timer: returns the timer id (never 0, rAthena's INVALID_TIMER is -1; here `None` in callers' fields).
pub fn add(c: &Ctx, due: Tick, func: Func, target: u32, data: i64) -> u64 {
    heap::ensure_built(c);
    let t = c.db().timer().insert(Timer { id: 0, due, func: func as u16, target, data, interval: 0 });
    heap::push_timer(c, due, t.id);
    arm(c, due);
    t.id
}

/// add_timer_interval: an interval below 1 is refused (INVALID_TIMER, here 0).
pub fn add_interval(c: &Ctx, due: Tick, func: Func, target: u32, data: i64, interval: i64) -> u64 {
    if interval < 1 {
        log::error!("add_timer_interval: invalid interval (tick={due} {func:?} id={target} data={data} diff_tick={})", due - c.tick());
        return 0;
    }
    heap::ensure_built(c);
    let t = c.db().timer().insert(Timer { id: 0, due, func: func as u16, target, data, interval });
    heap::push_timer(c, due, t.id);
    arm(c, due);
    t.id
}

/// delete_timer. rAthena also checks that the timer still belongs to the caller's function, because its timer ids
/// are slots that the next add_timer reuses; the ids here come from an auto-increment column and are never handed
/// out twice, so a stale id can only name its own (already gone) timer and needs no function check.
pub fn delete(c: &Ctx, id: u64) {
    c.db().timer().id().delete(id);
}

/// delete_timer(tid, func) with rAthena's function check: -1 when the timer does not exist ("no such timer"), -2 when
/// it belongs to another function (the timer is left running), 0 when it is gone. For callers whose rAthena code
/// can name the wrong function for a live timer (unit_skillcastcancel: skill_castend_pos vs skill_castend_id).
pub fn delete_checked(c: &Ctx, id: u64, func: Func) -> i32 {
    let Some(t) = c.db().timer().id().find(id) else { return -1 };
    if t.func != func as u16 {
        return -2;
    }
    c.db().timer().id().delete(id);
    0
}

/// Drops every timer that names this unit (unit_free: a freed block_list's timers die with it).
pub fn delete_for_target(c: &Ctx, target: u32) {
    let ids: Vec<u64> = c.db().timer().by_target().filter(target).map(|t| t.id).collect();
    for id in ids {
        c.db().timer().id().delete(id);
    }
}

/// settick_timer (timer.cpp:330-353): moves a timer that is in the heap to a new tick (returns the tick). A timer that
/// is not in the heap (do_timer popped it: it is running, or gone) is an error there and here (None); a deleted timer
/// whose slot is still in the heap moves like any other, as in rAthena.
pub fn settick(c: &Ctx, id: u64, tick: Tick) -> Option<Tick> {
    heap::ensure_built(c);
    let Some((pos, due)) = heap::find(c, id) else {
        log::error!("settick_timer: no such timer {id}");
        return None;
    };
    let tick = if tick == -1 { 0 } else { tick }; // add 1ms to avoid the error value -1
    if due == tick {
        return Some(tick); // nothing to do, already in propper position
    }
    heap::move_slot(c, pos, id, tick);
    if let Some(mut t) = c.db().timer().id().find(id) {
        t.due = tick;
        c.db().timer().id().update(t);
    }
    arm(c, tick);
    Some(tick)
}

/// addtick_timer (timer.cpp:323-326): settick_timer(tid, timer_data[tid].tick + tick), i.e. `tick` is added to the
/// timer's own tick (callers that pass gettick() + delay move it far into the future, as in rAthena). The timer's
/// tick is its heap slot's, else its row's; a timer that has neither is an error (None, logged by settick's path).
pub fn addtick(c: &Ctx, id: u64, tick: Tick) -> Option<Tick> {
    heap::ensure_built(c);
    let Some(base) = heap::find(c, id).map(|(_, due)| due).or_else(|| due(c, id)) else {
        log::error!("addtick_timer: no such timer {id}");
        return None;
    };
    settick(c, id, base + tick)
}

/// The due tick of a live timer.
pub fn due(c: &Ctx, id: u64) -> Option<Tick> {
    c.db().timer().id().find(id).map(|t| t.due)
}

/// Arms the wakeup for the earliest timer again (cutover.rs: the server opens after standing still; `arm` replaces a
/// pending wakeup that is gone or overdue).
pub fn rearm(c: &Ctx) {
    heap::ensure_built(c);
    if let Some((next, _)) = heap::peek(c) {
        arm(c, next);
    }
}

/// get_timer: (due, data) of a live timer.
pub fn get(c: &Ctx, id: u64) -> Option<(Tick, i64)> {
    c.db().timer().id().find(id).map(|t| (t.due, t.data))
}

/// do_timer: runs every timer that is due, earliest first, then arms the next wakeup.
/// Only the scheduler calls it: a scheduled reducer is an ordinary reducer that any client may call (spacetimedb 2.10.1
/// lib.rs "Restricting scheduled reducers"), and a client's call with a made-up row would run the timers early and
/// clear the pending wakeup. The scheduler's caller is the database itself.
#[reducer]
pub fn on_wakeup(rc: &ReducerContext, row: Wakeup) -> Result<(), String> {
    if rc.sender() != rc.database_identity() {
        return Err("on_wakeup is called by the scheduler only.".into());
    }
    // the world stands still while the owner exports or imports (cutover.rs): no timer runs; [`rearm`] starts them again
    if crate::cutover::frozen(&rc.db) {
        return Ok(());
    }
    crate::world::run(rc, |c| {
        c.executing_wakeup.set(row.scheduled_id);
        let mut s = state(c);
        if s.pending_row != 0 {
            if s.pending_row != row.scheduled_id {
                c.db().wakeup().scheduled_id().delete(s.pending_row);
            }
            s.pending_row = 0;
            s.pending_due = i64::MAX;
            put_state(c, s);
        }
        let _prof = crate::prof::wakeup_start();
        let now = c.real_tick();
        if now - row.due > 500 {
            log::warn!("timer: wakeup ran {} ms late", now - row.due);
        }
        heap::ensure_built(c);
        // A runaway chain (a timer re-adding itself for "now") must not spin forever inside one transaction.
        for _ in 0..20_000 {
            // timer.cpp:362-372: the top of the heap (the smallest tick; equal ticks in the heap's order), popped when
            // it is due
            let Some((due, id)) = heap::peek(c) else { break };
            if due - now > 0 {
                break; // no more expired timers to process
            }
            heap::pop_top(c);
            // timer.cpp:374: a timer deleted while in the heap has no function any more; its slot just goes
            let Some(t) = c.db().timer().id().find(id) else { continue };
            let tick = callback_tick(t.due, now);
            if t.interval > 0 {
                // the row stays during the callback (a delete_timer there ends the interval), with its next tick
                c.db().timer().id().update(Timer { due: interval_next(t.due, now, t.interval), ..t });
            } else {
                c.db().timer().id().delete(t.id);
            }
            match Func::from_u16(t.func) {
                Some(f) => {
                    // gettick() inside the callback is the callback's tick (world.rs Ctx::tick), then the pass's again
                    c.set_tick(tick);
                    crate::prof::count("timers", 1);
                    let _sw = crate::prof::span(|| format!("t:{f:?}"));
                    dispatch(c, t.id, f, tick, t.target, t.data);
                    c.set_tick(now);
                }
                None => log::error!("timer {} has unknown function {}", t.id, t.func),
            }
            // timer.cpp:383-406: an interval timer its callback did not delete goes back into the heap after the
            // callback (behind whatever the callback added)
            if t.interval > 0 {
                if let Some(t) = c.db().timer().id().find(t.id) {
                    heap::push_timer(c, t.due, t.id);
                }
            }
        }
        c.executing_wakeup.set(0);
        // timer.cpp:411: the next pass is due at the top of the heap (a deleted timer's slot included)
        if let Some((next, _)) = heap::peek(c) {
            arm(c, next);
        }
        crate::prof::wakeup_end();
    });
    Ok(())
}

/// The tick a timer function is called with (timer.cpp:376-380 do_timer): its own tick, or the pass's tick when it
/// runs more than a second late (`diff < -1000`).
pub fn callback_tick(due: Tick, now: Tick) -> Tick {
    if due - now < -1000 { now } else { due }
}

/// An interval timer's next tick (timer.cpp:401-404 do_timer): from its previous tick, or from the pass's tick when
/// it ran more than a second late.
pub fn interval_next(due: Tick, now: Tick, interval: i64) -> Tick {
    if due - now < -1000 { now + interval } else { due + interval }
}


fn dispatch(c: &Ctx, tid: u64, f: Func, tick: Tick, target: u32, data: i64) {
    match f {
        Func::UnitWalk => crate::unit::walktoxy_timer(c, tid, tick, target, data),
        Func::UnitDelayWalk => crate::unit::delay_walktoxy_timer(c, tick, target, data),
        Func::DelayQuit => crate::pc::delayquit_timer(c, target),
        Func::PcInvincible => crate::pc::invincible_timer(c, tid, target),
        Func::ScriptSleep => crate::script::npc_run::run_script_timer(c, tid, data as u64),
        Func::NpcTimerEvent => crate::script::npc_run::npc_timerevent(c, tid, tick, target),
        Func::NpcEventDoClock => crate::script::npc_run::npc_event_do_clock(c),
        Func::NpcSecureTimeout => crate::script::npc_run::npc_secure_timeout_timer(c, tid, target),
        Func::PcCloseNpc => crate::script::npc_run::pc_close_npc(c, target, data as i32),
        Func::PcEventTimer => crate::script::npc_run::pc_eventtimer(c, tid, target),
        Func::StatusChangeTimer => crate::status::sc::status_change_timer(c, tid, tick, target, data as i32),
        Func::StatusChangeStartDelay => crate::status::sc::start_timer(c, data as u64),
        Func::StatusNaturalHeal => crate::status::hp::status_natural_heal_timer(c, tick),
        Func::StatusBonusScript => crate::status::bonus::pc_bonus_script_timer(c, tid, target, data),
        Func::StatusEndAutobonus => crate::status::bonus::pc_endautobonus(c, tid, target, data),
        Func::StatusClearLastEffect => crate::status::sc::status_clear_last_effect_timer(c, target),
        Func::MapClearFloorItem => crate::flooritem::map_clearflooritem_timer(c, tid, target),
        Func::PcInventoryRental => crate::inventory::pc_inventory_rental_end(c, tid, target),
        Func::PartySendXy => crate::party::party_send_xy_timer(c),
        Func::ClanMemberCleanup => crate::clan::char_clan_member_cleanup(c),
        Func::MailReturnTimer => crate::int_mail::mail_return_timer(c),
        Func::MailDeleteTimer => crate::int_mail::mail_delete_timer(c),
        Func::SaleStartTimer => crate::cashshop::sale_start_timer(c, target),
        Func::SaleEndTimer => crate::cashshop::sale_end_timer(c, tid, target),
        Func::MobAiHard => crate::mob::ai::mob_ai_hard(c, tick),
        Func::MobAiLazy => crate::mob::ai::mob_ai_lazy(c, tick),
        Func::MobDelaySpawn => crate::mob::spawn::mob_delayspawn(c, tid, target),
        Func::MobDelayItemDrop => crate::mob::dead::mob_delay_item_drop(c, target),
        Func::MobTimerDelete => crate::mob::spawn::mob_timer_delete(c, tid, target),
        Func::MobRespawn => crate::mob::dead::mob_respawn(c, target, data),
        Func::MobAttacked => crate::mob::ai::mob_attacked(c, tick, target, data),
        Func::MobNormAttacked => crate::mob::ai::mob_norm_attacked(c, tick, target, data),
        Func::MapRemoveMobs => crate::mob::spawn::map_removemobs_timer(c, tid, target),
        Func::MvpTombDelaySpawn => crate::mob::dead::mvptomb_delayspawn(c, tid, target),
        Func::ClifClearUnitDelayed => crate::mob::clif::clearunit_delayed_sub(c, target, data),
        Func::UnitWalktoblSub => crate::mob::unit::unit_walktobl_sub(c, tick, target, data),
        Func::UnitDelayWalktobl => crate::mob::unit::unit_delay_walktobl_timer(c, target, data),
        Func::UnitAttack => crate::battle::unit_attack::unit_attack_timer(c, tid, tick, target),
        Func::UnitStep => {
            crate::battle::unit_attack::unit_step_timer(c, tid, tick, target);
        }
        Func::BattleDelayDamage => crate::battle::weapon_attack::battle_delay_damage_sub(c, tick, data as u64),
        Func::PcRespawn => crate::battle::pc_death::pc_respawn_timer(c, tid, tick, target),
        Func::PcCalcPvprank => crate::battle::pvp::pc_calc_pvprank_timer(c, tid, tick, target, data),
        Func::GuildSaveTimer => crate::int_guild::guild_save_timer(c, tick),
        Func::GuildPayexpTimer => crate::guild::guild_payexp_timer(c),
        Func::GuildSendXyTimer => crate::guild::guild_send_xy_timer(c),
        Func::PcAutosave => crate::autosave::pc_autosave(c),
        Func::PcFollow => crate::script::buildins::script2::unitmove::pc_follow_timer(c, tid, tick, target),
        Func::ChrifDivorce => crate::script::buildins::script2::family::chrif_divorce_timer(c, target, data as u32),
        Func::PcAutotradeTimer => crate::autotrade::pc_autotrade_timer(c, target),
        Func::SkillCastendId => {
            crate::skill::castend::skill_castend_id(c, tid, tick, target, data);
        }
        Func::SkillCastendPos => {
            crate::skill::castend::skill_castend_pos(c, tid, tick, target, data);
        }
        Func::SkillTimerskill => crate::skill::timerskill::skill_timerskill(c, tid, tick, target, data),
        Func::SkillBlockpcEnd => {
            crate::skill::cooldown::skill_blockpc_end(c, tid, target, data);
        }
        Func::SkillKeepUsing => crate::skill::castend::skill_keep_using(c, tid, target),
        Func::PcSpiritball => crate::skill::state::pc_spiritball_timer(c, tid, target),
        Func::PcSpiritcharm => crate::skill::state::pc_spiritcharm_timer(c, tid, target),
        Func::UnitShadowScar => crate::skill::unit::unit_shadowscar_timer(c, tid, target),
        Func::SkillUnitTimer => crate::skillunit::timer::skill_unit_timer(c, tid, tick),
        Func::PetHungry => {
            crate::pet::pet_hungry(c, tid, tick, target);
        }
        Func::PetAiHard => crate::pet::ai::pet_ai_hard(c, tick),
        Func::PetSkillBonusTimer => {
            crate::pet::bonus::pet_skill_bonus_timer(c, tid, tick, target);
        }
        Func::PetDelayItemDrop => {
            crate::pet::ai::pet_delay_item_drop(c, target);
        }
        Func::PetSkillSupportTimer => {
            crate::pet::bonus::pet_skill_support_timer(c, tid, tick, target);
        }
        Func::PetRecoveryTimer => {
            crate::pet::bonus::pet_recovery_timer(c, tid, target);
        }
        Func::PetHealTimer => {
            crate::pet::bonus::pet_heal_timer(c, tid, tick, target);
        }
        Func::PetEndAutobonus => {
            crate::pet::bonus::pet_endautobonus(c, tid, target, data);
        }
        Func::UnitTeleportTimer => {
            // unit_teleport_timer: a homunculus' (homunculus/unit.rs), else a pet's
            if !crate::homunculus::unit::unit_teleport_timer(c, tid, target, data) {
                crate::pet::ai::unit_teleport_timer(c, tid, target, data);
            }
        }
        Func::HomHungry => {
            crate::homunculus::hom_hungry(c, tid, tick, target);
        }
        Func::SkillBlockhomunEnd => {
            crate::homunculus::cooldown::skill_blockhomun_end(c, target, data);
        }
        Func::UnitResumeRunning => crate::battle::unit_attack::unit_resume_running(c, tid, target),
        Func::InstanceDelete => crate::instance::instance_delete_timer(c, target),
        Func::InstanceSubscription => crate::instance::instance_subscription_timer(c),
        Func::MobSpawnGuardianSub => {
            crate::woe::guardian::mob_spawn_guardian_sub(c, target, data);
        }
        Func::BgSendXyTimer => crate::battleground::bg_send_xy_timer(c),
        Func::BgOnReadyLoopback => crate::battleground::bg_on_ready_loopback(c, data),
        Func::BgOnReadyExpire => crate::battleground::bg_on_ready_expire(c, data),
        Func::BgOnReadyStart => crate::battleground::bg_on_ready_start(c, data),
        Func::PcOnExpireActive => crate::battleground::pc_on_expire_active(c, tid, target),
        Func::MercContractEnd => {
            crate::mercenary::merc_contract_end(c, tid, target);
        }
        Func::ElementalSummonEnd => {
            crate::elemental::elemental_summon_end(c, tid, target);
        }
        Func::ElementalAiTimer => crate::elemental::elemental_ai_timer(c, tick),
        Func::SkillBlockmercEnd => {
            crate::mercenary::skill::skill_blockmerc_end(c, tid, target, data);
        }
        Func::CompanionTeleportTimer => crate::companion::unit::unit_teleport_timer(c, tid, target, data),
        Func::NpcDynamicnpcRemoval => crate::npc_load::npc_dynamicnpc_removal_timer(c, tid, target),
        Func::PcMacroDetectorTimeout => crate::macro_detector::pc_macro_detector_timeout(c, target),
        Func::AuctionEndTimer => crate::int_auction::auction_end_timer(c, target),
    }
}

#[cfg(test)]
mod tests {
    use super::{callback_tick, interval_next};

    /// timer.cpp:376-380: a timer function gets its own tick unless the pass runs it more than a second late.
    #[test]
    fn callback_tick_is_the_due_tick_up_to_one_second_late() {
        assert_eq!(callback_tick(10_000, 10_015), 10_000); // SpacetimeDB's ~15 ms wakeup latency
        assert_eq!(callback_tick(10_000, 11_000), 10_000); // diff -1000 is not < -1000
        assert_eq!(callback_tick(10_000, 11_001), 11_001); // diff -1001: the pass's tick
        assert_eq!(callback_tick(10_000, 10_000), 10_000);
    }

    /// timer.cpp:401-404: an interval timer is re-armed from its own tick, or from the pass's tick when it ran more
    /// than a second late.
    #[test]
    fn interval_rearms_from_the_due_tick_up_to_one_second_late() {
        assert_eq!(interval_next(10_000, 10_015, 100), 10_100);
        assert_eq!(interval_next(10_000, 11_000, 100), 10_100);
        assert_eq!(interval_next(10_000, 11_001, 100), 11_101);
    }
}
