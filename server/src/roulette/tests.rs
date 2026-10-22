//! Native tests of the roulette rules over the embedded table (sql-files/roulette_default_data.sql as
//! itemdb_parse_roulette_db loads it) with fixed rolls — the oracle's LOKI_RND / the module's debug_rng semantics:
//! rnd() is k, rnd_value(min, max) is min + k % (max - min + 1).

use super::*;
use crate::db::items::roulette::RouletteItem;

struct Fixed(u32);

impl Rolls for Fixed {
    fn rnd(&mut self) -> u32 {
        self.0
    }
    fn rnd_value(&mut self, min: i32, max: i32) -> i32 {
        let (lo, hi) = if min > max { (max, min) } else { (min, max) };
        lo + (self.0 as i64 % (hi as i64 - lo as i64 + 1)) as i32
    }
}

const NOW: Tick = 1_000_000;

fn fresh() -> PcRoulette {
    PcRoulette::new(2000001)
}

/// A pc_additem stand-in that records what it was asked for and answers `res`.
fn recorder(res: u8, log: &mut Vec<(u32, i32)>) -> impl FnMut(u32, i32) -> u8 + '_ {
    move |nameid, amount| {
        log.push((nameid, amount));
        res
    }
}

fn no_setreg() -> impl FnMut(&'static str, i32) {
    |var, v| panic!("pc_setreg2({var}, {v}) was not expected")
}

#[test]
fn the_table_is_the_default_data_dealt_out_by_level() {
    let db = rd();
    assert_eq!(db.len(), MAX_ROULETTE_LEVEL);
    for i in 0..MAX_ROULETTE_LEVEL {
        assert_eq!(db.items(i), MAX_ROULETTE_COLUMNS - i, "level {i}");
        // the first slot of every level is the losing one (Silver_Coin on 1-4, Gold_Coin on 5-7), no other is
        assert_eq!(db.slot(i, 0).2, 1, "level {i}");
        assert!((1..db.items(i)).all(|j| db.slot(i, j).2 == 0), "level {i}");
        assert!((0..db.items(i)).all(|j| db.slot(i, j).1 == 1), "level {i}");
    }
    let ids = |i: usize| (0..db.items(i)).map(|j| db.slot(i, j).0).collect::<Vec<_>>();
    assert_eq!(ids(0), vec![675, 671, 678, 604, 522, 12609, 12523, 985, 984]);
    assert_eq!(ids(1), vec![675, 671, 603, 608, 607, 12522, 6223, 6224]);
    assert_eq!(ids(2), vec![675, 671, 12108, 617, 12514, 7444, 969]);
    assert_eq!(ids(3), vec![675, 671, 616, 12516, 22777, 6231]);
    assert_eq!(ids(4), vec![671, 12246, 12263, 12831, 6235]);
    assert_eq!(ids(5), vec![671, 12766, 6234, 6233]);
    // "KRO lists this twice"
    assert_eq!(ids(6), vec![671, 6233, 6233]);
    // out of range reads as nothing
    assert_eq!(db.slot(7, 0), (0, 0, 0));
    assert_eq!(db.slot(6, 3), (0, 0, 0));
}

#[test]
fn info_ack_lists_the_42_slots() {
    let w = roulette_info_ack(rd());
    assert_eq!(w.len(), 512); // sizeof(packet_roulette_info_ack), the header probe's size for 0x0a1c
    assert_eq!(u16::from_le_bytes([w[0], w[1]]), 0x0a1c);
    assert_eq!(u16::from_le_bytes([w[2], w[3]]), 512);
    assert_eq!(u32::from_le_bytes([w[4], w[5], w[6], w[7]]), 1);
    let entry = |n: usize| {
        let o = 8 + n * 12;
        let u16_at = |p: usize| u16::from_le_bytes([w[p], w[p + 1]]);
        (u16_at(o), u16_at(o + 2), u32::from_le_bytes([w[o + 4], w[o + 5], w[o + 6], w[o + 7]]), u16_at(o + 8), u16_at(o + 10))
    };
    assert_eq!(entry(0), (0, 0, crate::db::items::client_nameid(675), 1, 0));
    assert_eq!(entry(8), (0, 8, crate::db::items::client_nameid(984), 1, 0));
    assert_eq!(entry(9), (1, 0, crate::db::items::client_nameid(675), 1, 0));
    assert_eq!(entry(41), (6, 2, crate::db::items::client_nameid(6233), 1, 0));
}

#[test]
fn bonus_item_comes_from_the_starting_level_up() {
    // bronze: levels 0..6 → rnd_value(0, 6) = 3, slot 3 % 6 = 3
    let mut r = fresh();
    r.bronze = 1;
    roulette_generate_bonus(&mut r, rd(), true, &mut Fixed(3));
    assert_eq!(r.bonus_item_id, 12516);
    // 10 silver: levels 2..6 → 2 + 3 % 5 = 5, slot 3 % 4 = 3
    let mut r = fresh();
    r.silver = 10;
    roulette_generate_bonus(&mut r, rd(), true, &mut Fixed(3));
    assert_eq!(r.bonus_item_id, 6233);
    // 10 gold (and 9 silver): levels 4..6 → 4 + 3 % 3 = 4, slot 3 % 5 = 3
    let mut r = fresh();
    r.silver = 9;
    r.gold = 10;
    roulette_generate_bonus(&mut r, rd(), true, &mut Fixed(3));
    assert_eq!(r.bonus_item_id, 12831);
    // no points: from level 0
    let mut r = fresh();
    roulette_generate_bonus(&mut r, rd(), true, &mut Fixed(2));
    assert_eq!(r.bonus_item_id, 12108);
}

#[test]
fn opening_again_drops_the_bonus_item() {
    // roulette_generate_bonus's else branch: a bonus item already there is cleared, not kept
    let mut r = fresh();
    r.bonus_item_id = 12516;
    roulette_generate_bonus(&mut r, rd(), true, &mut Fixed(3));
    assert_eq!(r.bonus_item_id, 0);
    roulette_generate_bonus(&mut r, rd(), true, &mut Fixed(3));
    assert_eq!(r.bonus_item_id, 12516);
    // bonus reward off: never one
    roulette_generate_bonus(&mut r, rd(), false, &mut Fixed(3));
    assert_eq!(r.bonus_item_id, 0);
    roulette_generate_bonus(&mut r, rd(), false, &mut Fixed(3));
    assert_eq!(r.bonus_item_id, 0);
}

#[test]
fn no_points_no_turn() {
    let mut r = fresh();
    r.silver = 9;
    r.gold = 9;
    let mut log = Vec::new();
    let res = roulette_generate(&mut r, rd(), true, NOW, &mut Fixed(1), &mut recorder(0, &mut log), &mut no_setreg());
    assert_eq!(res, Some(GENERATE_ROULETTE_NO_ENOUGH_POINT));
    assert!(log.is_empty());
    assert_eq!((r.stage, r.prize_stage, r.prize_idx, r.claim_prize, r.tick), (0, 0, -1, false, 0));
    // the ack: stage 0, index 0 (prizeIdx -1 is sent as 0)
    let p = generate_ack(&r, GENERATE_ROULETTE_NO_ENOUGH_POINT, r.prize_stage, 0, r.bonus_item_id).to_vec();
    assert_eq!(p.len(), 23);
    assert_eq!(&p[0..7], &[0x20, 0x0a, 2, 0, 0, 0, 0]);
    assert_eq!(&p[11..23], &[9, 0, 0, 0, 9, 0, 0, 0, 0, 0, 0, 0]);
}

#[test]
fn a_bronze_turn_that_wins() {
    let mut r = fresh();
    r.bronze = 3;
    let mut regs = Vec::new();
    let mut log = Vec::new();
    let res = roulette_generate(&mut r, rd(), true, NOW, &mut Fixed(1), &mut recorder(0, &mut log), &mut |v, n| regs.push((v, n)));
    assert_eq!(res, Some(GENERATE_ROULETTE_SUCCESS));
    assert_eq!(regs, vec![(ROULETTE_BRONZE_VAR, 2)]);
    assert!(log.is_empty());
    assert_eq!(r.bronze, 2);
    // slot 1 % 9 = 1 (Gold_Coin, not losing): one level up, the prize waits; 9 - 0 - 3 = 6 s of wheel
    assert_eq!((r.stage, r.prize_stage, r.prize_idx, r.claim_prize, r.tick), (1, 0, 1, true, NOW + 6000));
    // the window reopened now shows step 0 and the slot
    let o = open_ack(&r);
    assert_eq!((o.Step, o.Idx), (0, 1));
    assert_eq!(o.to_vec().len(), 25);
}

#[test]
fn a_losing_turn_ends_the_run() {
    let mut r = fresh();
    r.bronze = 1;
    let mut regs = Vec::new();
    let mut log = Vec::new();
    // slot 9 % 9 = 0: Silver_Coin, flag 1
    let res = roulette_generate(&mut r, rd(), true, NOW, &mut Fixed(9), &mut recorder(0, &mut log), &mut |v, n| regs.push((v, n)));
    assert_eq!(res, Some(GENERATE_ROULETTE_LOSING));
    assert_eq!(regs, vec![(ROULETTE_BRONZE_VAR, 0)]);
    assert_eq!((r.stage, r.prize_stage, r.prize_idx, r.claim_prize), (0, 0, 0, true));
    // clif_roulette_open while the losing prize waits: Step = stage - 1 = -1
    let o = open_ack(&r);
    assert_eq!((o.Step, o.Idx), (-1, 0));
    assert_eq!(o.to_vec()[7], 0xff);
}

#[test]
fn silver_and_gold_start_higher_and_turn_shorter() {
    // 10 silver (no bronze): level 2, slot 3 % 7 = 3 (Old_Violet_Box), wheel 9 - 2 - 3 = 4 s
    let mut r = fresh();
    r.silver = 25;
    r.gold = 30;
    let mut regs = Vec::new();
    let mut log = Vec::new();
    let res = roulette_generate(&mut r, rd(), true, NOW, &mut Fixed(3), &mut recorder(0, &mut log), &mut |v, n| regs.push((v, n)));
    assert_eq!(res, Some(GENERATE_ROULETTE_SUCCESS));
    assert_eq!(regs, vec![(ROULETTE_SILVER_VAR, 15)]);
    assert_eq!((r.silver, r.gold), (15, 30));
    assert_eq!((r.stage, r.prize_stage, r.prize_idx, r.tick), (3, 2, 3, NOW + 4000));
    // 10 gold (silver below 10): level 4, slot 3 % 5 = 3, wheel 9 - 4 - 3 = 2 s
    let mut r = fresh();
    r.silver = 9;
    r.gold = 10;
    let mut regs = Vec::new();
    let res = roulette_generate(&mut r, rd(), true, NOW, &mut Fixed(3), &mut recorder(0, &mut log), &mut |v, n| regs.push((v, n)));
    assert_eq!(res, Some(GENERATE_ROULETTE_SUCCESS));
    assert_eq!(regs, vec![(ROULETTE_GOLD_VAR, 0)]);
    assert_eq!((r.silver, r.gold), (9, 0));
    assert_eq!((r.stage, r.prize_stage, r.prize_idx, r.tick), (5, 4, 3, NOW + 2000));
    assert!(log.is_empty());
}

#[test]
fn the_wheel_blocks_until_its_tick() {
    let mut r = fresh();
    r.bronze = 2;
    r.claim_prize = true;
    r.prize_idx = 1;
    r.stage = 1;
    r.tick = NOW + 1;
    let before = r.clone();
    let mut log = Vec::new();
    assert_eq!(roulette_generate(&mut r, rd(), true, NOW, &mut Fixed(1), &mut recorder(0, &mut log), &mut no_setreg()), None);
    assert_eq!(roulette_recv_item(&mut r, rd(), true, NOW, &mut Fixed(1), &mut recorder(0, &mut log)), None);
    assert_eq!(r, before);
    assert!(log.is_empty());
    // DIFF_TICK(tick, now) > 0 only: at the tick itself the request goes through
    assert_eq!(roulette_recv_item(&mut r, rd(), true, NOW + 1, &mut Fixed(1), &mut recorder(0, &mut log)), Some(RECV_ITEM_SUCCESS));
    assert_eq!(log, vec![(671, 1)]);
}

#[test]
fn turning_again_claims_the_prize_and_starts_over() {
    // after a win at level 0 (stage 1, prize 671 waiting), CZ_REQ_GENERATE_ROULETTE hands the prize out, which resets
    // the stage: the next turn is paid again and plays level 0, not level 1
    let mut r = fresh();
    r.bronze = 1;
    r.stage = 1;
    r.prize_stage = 0;
    r.prize_idx = 1;
    r.claim_prize = true;
    r.tick = NOW - 1;
    let mut regs = Vec::new();
    let mut log = Vec::new();
    let res = roulette_generate(&mut r, rd(), true, NOW, &mut Fixed(2), &mut recorder(0, &mut log), &mut |v, n| regs.push((v, n)));
    assert_eq!(log, vec![(671, 1)]);
    assert_eq!(regs, vec![(ROULETTE_BRONZE_VAR, 0)]);
    assert_eq!(res, Some(GENERATE_ROULETTE_SUCCESS));
    assert_eq!((r.stage, r.prize_stage, r.prize_idx, r.claim_prize), (1, 0, 2, true));
    // getitem found no bonus item and rolled one (the bronze was still there: levels 0..6 → 2, slot 2)
    assert_eq!(r.bonus_item_id, 12108);
}

#[test]
fn a_stage_past_the_last_level_is_reset() {
    let mut r = fresh();
    r.stage = MAX_ROULETTE_LEVEL as i16;
    r.prize_stage = 6;
    r.prize_idx = 2;
    r.silver = 10;
    let mut regs = Vec::new();
    let mut log = Vec::new();
    let res = roulette_generate(&mut r, rd(), true, NOW, &mut Fixed(0), &mut recorder(0, &mut log), &mut |v, n| regs.push((v, n)));
    assert!(log.is_empty());
    assert_eq!(regs, vec![(ROULETTE_SILVER_VAR, 0)]);
    // level 2, slot 0: the losing Silver_Coin
    assert_eq!(res, Some(GENERATE_ROULETTE_LOSING));
    assert_eq!((r.stage, r.prize_stage, r.prize_idx), (0, 2, 0));
}

#[test]
fn getitem_doubles_the_bonus_item() {
    let mut r = fresh();
    r.prize_stage = 1;
    r.prize_idx = 1; // 671
    r.claim_prize = true;
    r.stage = 2;
    r.bonus_item_id = 671;
    let mut log = Vec::new();
    let res = clif_roulette_getitem(&mut r, rd(), true, &mut Fixed(2), &mut recorder(0, &mut log));
    assert_eq!(res, 0);
    assert_eq!(log, vec![(671, 2)]);
    // a new bonus item, and the turn state reset
    assert_eq!(r.bonus_item_id, 12108);
    assert_eq!((r.stage, r.prize_stage, r.prize_idx, r.claim_prize), (0, 0, -1, false));
    // without the bonus reward the same prize is single, and the bonus item is found on level 0 ≤ 1: dropped
    let mut r2 = fresh();
    r2.prize_stage = 1;
    r2.prize_idx = 1;
    r2.bonus_item_id = 671;
    let mut log = Vec::new();
    clif_roulette_getitem(&mut r2, rd(), false, &mut Fixed(2), &mut recorder(0, &mut log));
    assert_eq!(log, vec![(671, 1)]);
    assert_eq!(r2.bonus_item_id, 0);
}

#[test]
fn getitem_keeps_a_bonus_item_of_a_higher_level() {
    // 12516 is only on level 3: a prize from level 1 keeps it
    let mut r = fresh();
    r.prize_stage = 1;
    r.prize_idx = 2; // 603
    r.bonus_item_id = 12516;
    let mut log = Vec::new();
    clif_roulette_getitem(&mut r, rd(), true, &mut Fixed(2), &mut recorder(0, &mut log));
    assert_eq!(log, vec![(603, 1)]);
    assert_eq!(r.bonus_item_id, 12516);
    // 6233 is on levels 5 and 6: a prize from level 4 keeps it
    let mut r = fresh();
    r.prize_stage = 4;
    r.prize_idx = 1; // 12246
    r.bonus_item_id = 6233;
    clif_roulette_getitem(&mut r, rd(), true, &mut Fixed(2), &mut recorder(0, &mut log));
    assert_eq!(r.bonus_item_id, 6233);
    // a prize from level 3 (not the bonus item) drops 12516, found on level 3 ≤ 3, and rolls a new one
    let mut r = fresh();
    r.prize_stage = 3;
    r.prize_idx = 2; // 616
    r.bonus_item_id = 12516;
    let mut log = Vec::new();
    clif_roulette_getitem(&mut r, rd(), true, &mut Fixed(3), &mut recorder(0, &mut log));
    assert_eq!(log, vec![(616, 1)]);
    assert_eq!(r.bonus_item_id, 12516); // dropped, then rolled again: rnd_value(0, 6) = 3, slot 3
    let mut r = fresh();
    r.prize_stage = 3;
    r.prize_idx = 2;
    r.bonus_item_id = 12516;
    clif_roulette_getitem(&mut r, rd(), true, &mut Fixed(1), &mut recorder(0, &mut log));
    assert_eq!(r.bonus_item_id, 671); // rnd_value(0, 6) = 1, slot 1 % 8 = 1
}

#[test]
fn a_losing_slot_is_never_doubled() {
    // the bonus item is the losing Silver_Coin of level 0: single amount, and the item (on level 0) is dropped
    let mut r = fresh();
    r.prize_stage = 0;
    r.prize_idx = 0;
    r.bonus_item_id = 675;
    let mut log = Vec::new();
    clif_roulette_getitem(&mut r, rd(), true, &mut Fixed(4), &mut recorder(0, &mut log));
    assert_eq!(log, vec![(675, 1)]);
    assert_eq!(r.bonus_item_id, 6235); // a new one: rnd_value(0, 6) = 4 → level 4, slot 4 % 5 = 4
}

#[test]
fn claiming_maps_pc_additem_results() {
    let prize = || {
        let mut r = fresh();
        r.prize_stage = 2;
        r.prize_idx = 3; // 617
        r.claim_prize = true;
        r.bonus_item_id = 6233; // levels 5 and 6: kept
        r
    };
    for (add, want) in [
        (inv::ADDITEM_SUCCESS, RECV_ITEM_SUCCESS),
        (inv::ADDITEM_INVALID, RECV_ITEM_OVERCOUNT),
        (inv::ADDITEM_OVERITEM, RECV_ITEM_OVERCOUNT),
        (inv::ADDITEM_OVERAMOUNT, RECV_ITEM_OVERCOUNT),
        (inv::ADDITEM_OVERWEIGHT, RECV_ITEM_OVERWEIGHT),
        (inv::ADDITEM_STACKLIMIT, RECV_ITEM_FAILED),
        (inv::ADDITEM_ITEM, RECV_ITEM_FAILED),
    ] {
        let mut r = prize();
        let mut log = Vec::new();
        assert_eq!(roulette_recv_item(&mut r, rd(), true, NOW, &mut Fixed(0), &mut recorder(add, &mut log)), Some(want), "{add}");
        assert_eq!(log, vec![(617, 1)]);
        // the prize is gone whatever pc_additem said
        assert_eq!((r.stage, r.prize_stage, r.prize_idx, r.claim_prize, r.bonus_item_id), (0, 0, -1, false, 6233));
    }
    // nothing to claim: RECV_ITEM_FAILED, no item
    let mut r = fresh();
    let mut log = Vec::new();
    assert_eq!(roulette_recv_item(&mut r, rd(), true, NOW, &mut Fixed(0), &mut recorder(0, &mut log)), Some(RECV_ITEM_FAILED));
    assert!(log.is_empty());
    assert_eq!(r, fresh());
    // getitem without a slot
    let mut log = Vec::new();
    assert_eq!(clif_roulette_getitem(&mut r, rd(), true, &mut Fixed(0), &mut recorder(0, &mut log)), RECV_ITEM_FAILED);
    assert!(log.is_empty());
}

#[test]
fn an_all_apples_table_plays_the_same_way() {
    // an empty db_roulette (every level filled with Apples, flag 0): never a losing slot
    let apples: Vec<Vec<RouletteItem>> =
        (0..MAX_ROULETTE_LEVEL).map(|i| vec![(crate::db::items::ITEMID_APPLE, 1, 0); MAX_ROULETTE_COLUMNS - i]).collect();
    let db = apples.as_slice();
    let mut r = fresh();
    r.bronze = 1;
    let mut log = Vec::new();
    let res = roulette_generate(&mut r, db, true, NOW, &mut Fixed(0), &mut recorder(0, &mut log), &mut |_, _| {});
    assert_eq!(res, Some(GENERATE_ROULETTE_SUCCESS));
    assert_eq!(r.stage, 1);
    let w = roulette_info_ack(db);
    assert_eq!(w.len(), 512);
    assert_eq!(u32::from_le_bytes([w[12], w[13], w[14], w[15]]), crate::db::items::client_nameid(crate::db::items::ITEMID_APPLE));
}

#[test]
fn parameters_are_the_script_constants() {
    let tsv = include_str!("../../tools/script_constants.tsv");
    let find = |name: &str| tsv.lines().find_map(|l| {
        let mut f = l.split('\t');
        (f.next() == Some(name)).then(|| f.next().and_then(|v| v.parse::<i64>().ok())).flatten()
    });
    assert_eq!(find("RouletteBronze"), Some(SP_ROULETTE_BRONZE));
    assert_eq!(find("RouletteSilver"), Some(SP_ROULETTE_SILVER));
    assert_eq!(find("RouletteGold"), Some(SP_ROULETTE_GOLD));
}

#[test]
fn the_parity_scenario_step_by_step() {
    // wave_f/roulette.md's roulette_a.txt over the rules alone (open = roulette_generate_bonus + open_ack)
    let mut r = fresh();
    let mut got: Vec<(u32, i32)> = Vec::new();
    let mut regs: Vec<(&str, i32)> = Vec::new();
    let mut t = NOW;
    let k3 = &mut Fixed(3);
    roulette_generate_bonus(&mut r, rd(), true, k3);
    assert_eq!(r.bonus_item_id, 12516);
    assert_eq!(roulette_generate(&mut r, rd(), true, t, k3, &mut recorder(0, &mut got), &mut no_setreg()), Some(GENERATE_ROULETTE_NO_ENOUGH_POINT));
    (r.bronze, r.silver, r.gold) = (2, 10, 10); // @set
    roulette_generate_bonus(&mut r, rd(), true, k3);
    assert_eq!(r.bonus_item_id, 0);
    roulette_generate_bonus(&mut r, rd(), true, k3);
    assert_eq!(r.bonus_item_id, 12516);
    let res = roulette_generate(&mut r, rd(), true, t, k3, &mut recorder(0, &mut got), &mut |v, n| regs.push((v, n)));
    assert_eq!((res, r.prize_stage, r.prize_idx), (Some(GENERATE_ROULETTE_SUCCESS), 0, 3));
    assert_eq!(roulette_generate(&mut r, rd(), true, t, k3, &mut recorder(0, &mut got), &mut no_setreg()), None);
    assert_eq!(roulette_recv_item(&mut r, rd(), true, t + 1000, k3, &mut recorder(0, &mut got)), None);
    t += 6500;
    assert_eq!(roulette_recv_item(&mut r, rd(), true, t, k3, &mut recorder(0, &mut got)), Some(RECV_ITEM_SUCCESS));
    assert_eq!(roulette_recv_item(&mut r, rd(), true, t, k3, &mut recorder(0, &mut got)), Some(RECV_ITEM_FAILED));
    assert_eq!(r.bonus_item_id, 12516);
    let k9 = &mut Fixed(9);
    let res = roulette_generate(&mut r, rd(), true, t, k9, &mut recorder(0, &mut got), &mut |v, n| regs.push((v, n)));
    assert_eq!((res, r.prize_stage, r.prize_idx), (Some(GENERATE_ROULETTE_LOSING), 0, 0));
    t += 6500;
    roulette_generate_bonus(&mut r, rd(), true, k9);
    let o = open_ack(&r);
    assert_eq!((o.Step, o.Idx, o.AdditionItemID, o.BronzePoint, o.SilverPoint, o.GoldPoint), (-1, 0, 0, 0, 10, 10));
    let res = roulette_generate(&mut r, rd(), true, t, k9, &mut recorder(0, &mut got), &mut |v, n| regs.push((v, n)));
    assert_eq!((res, r.prize_stage, r.prize_idx, r.bonus_item_id), (Some(GENERATE_ROULETTE_SUCCESS), 2, 2, 671));
    t += 4500;
    let res = roulette_generate(&mut r, rd(), true, t, k9, &mut recorder(0, &mut got), &mut |v, n| regs.push((v, n)));
    assert_eq!((res, r.prize_stage, r.prize_idx, r.bonus_item_id), (Some(GENERATE_ROULETTE_SUCCESS), 4, 4, 6235));
    t += 2500;
    assert_eq!(roulette_recv_item(&mut r, rd(), true, t, k9, &mut recorder(0, &mut got)), Some(RECV_ITEM_SUCCESS));
    assert_eq!(r.bonus_item_id, 12108);
    let res = roulette_generate(&mut r, rd(), true, t, k9, &mut recorder(0, &mut got), &mut no_setreg());
    assert_eq!(res, Some(GENERATE_ROULETTE_NO_ENOUGH_POINT));
    roulette_generate_bonus(&mut r, rd(), true, k9); // @roulette
    assert_eq!(r.bonus_item_id, 0);
    assert_eq!(got, vec![(604, 1), (675, 1), (12108, 1), (6235, 2)]);
    assert_eq!(regs, vec![(ROULETTE_BRONZE_VAR, 1), (ROULETTE_BRONZE_VAR, 0), (ROULETTE_SILVER_VAR, 0), (ROULETTE_GOLD_VAR, 0)]);
}
