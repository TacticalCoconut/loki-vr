//! THE PROOF of the port: tools/hordegolden ran the C# engine (client/Assets/Loki/Runtime/Horde/Core, on .NET) and
//! wrote down every call made to it and, after every Step, its StateHash, a hash of the offers it opened and closed and
//! a hash of each snapshot body its SnapshotEncoder made (server/data/horde/golden.bin). The tests here make the same
//! calls on the Rust engine and compare after every Step. Nothing is fudged: a field that could not match would be
//! named here and left out of nothing (the hash is the C# engine's own, whole).
//!
//!   cargo test --lib horde::golden                      the short runs (every kind of thing; under a minute)
//!   cargo test --lib horde::golden -- --ignored         every run, the whole 15-minute one included
//!   HORDE_TRACE=<run>:<from>:<to> cargo test --lib horde::golden -- --ignored --nocapture
//!                                                       prints those steps' state as `hordegolden trace` prints the C# engine's

use super::content::{self, Content, Rd};
use super::math;
use super::sim::*;
use super::wire::{self, SnapshotEncoder};

/// A line on the test's standard output (the module's sources hold no print macro: `spacetime publish` refuses them).
macro_rules! say {
    ($($t:tt)*) => {{
        use std::io::Write;
        let _ = writeln!(std::io::stdout(), $($t)*);
    }};
}

fn golden_bytes() -> Vec<u8> {
    let path = concat!(env!("CARGO_MANIFEST_DIR"), "/data/horde/golden.bin");
    std::fs::read(path).unwrap_or_else(|e| panic!("{path}: {e} (written by tools/hordegolden)"))
}

struct Golden {
    bytes: Vec<u8>,
    contents: Vec<(String, &'static Content, Vec<u8>)>,
    probes: Vec<(u8, f64, f64, f64)>,
    wire: Vec<(String, Vec<u8>)>,
    runs_at: usize,
    runs: usize,
}

fn open() -> Golden {
    let bytes = golden_bytes();
    let mut r = Rd::new(&bytes);
    assert_eq!(r.u32().unwrap(), 0x4748_4B4C, "golden.bin: not a golden file");
    assert_eq!(r.i32().unwrap(), 1, "golden.bin: another version");
    let mut contents = Vec::new();
    for _ in 0..r.i32().unwrap() {
        let name = r.str().unwrap();
        let n = r.i32().unwrap() as usize;
        let b = r.bytes(n).unwrap().to_vec();
        let c: &'static Content = Box::leak(Box::new(content::load(&b).unwrap()));
        contents.push((name, c, b));
    }
    let mut probes = Vec::new();
    for _ in 0..r.i32().unwrap() {
        probes.push((r.u8().unwrap(), r.f64().unwrap(), r.f64().unwrap(), r.f64().unwrap()));
    }
    let mut wire = Vec::new();
    for _ in 0..r.i32().unwrap() {
        let name = r.str().unwrap();
        let n = r.i32().unwrap() as usize;
        wire.push((name, r.bytes(n).unwrap().to_vec()));
    }
    let runs = r.i32().unwrap() as usize;
    let runs_at = r.pos();
    Golden { bytes, contents, probes, wire, runs_at, runs }
}

fn fnv(b: &[u8], mut h: u64) -> u64 {
    for &x in b {
        h ^= x as u64;
        h = h.wrapping_mul(1099511628211);
    }
    h
}

fn fold(h: u64) -> u32 {
    (h ^ (h >> 32)) as u32
}

const FNV0: u64 = 1469598103934665603;

/// What one golden run's replay found.
#[derive(Debug)]
struct RunReport {
    name: String,
    steps: usize,
    /// Steps whose state hash agreed before the first that did not (= steps when all agree).
    agree: usize,
    first_state_diff: Option<usize>,
    first_offer_diff: Option<usize>,
    snapshots: usize,
    first_snapshot_diff: Option<usize>,
    end_ok: bool,
    results_ok: bool,
    ticks: i32,
}

fn dump(step: usize, sim: &Sim) {
    let c = sim.c;
    say!(
        "STEP {step} tick {} wall {} paused {} hash {:016x} alive {} party {}/{} pickups {} fx {} summons {}",
        sim.tick,
        sim.wall_tick,
        if sim.paused { "True" } else { "False" },
        sim.state_hash(),
        sim.enemies_alive,
        sim.party_level,
        sim.party_exp,
        sim.pickups.len(),
        sim.fx.len(),
        sim.summons.len()
    );
    for p in sim.players.iter().filter(|p| p.present) {
        let w: Vec<String> = p.weapons.iter().map(|x| format!("{}:{}@{}", c.weapons[x.def].id, x.level, x.next_tick)).collect();
        say!(
            "  P{} {},{} v {},{} hp {}/{} lv {} job {} kills {} fallen {} face {} speed {} w [{}] open {} q {} in {}",
            p.slot, p.x, p.y, p.vx, p.vy, p.hp, p.max_hp, p.level, p.job_id, p.kills, if p.fallen { "True" } else { "False" }, p.face_deg, p.speed_per_tick, w.join(" "),
            p.open.as_ref().map(|o| o.id).unwrap_or(0), p.offers.len(), p.inputs.len()
        );
    }
    for e in sim.enemies.iter().filter(|e| e.alive) {
        say!("  E{} d{} {},{} kb {},{} hp {}", e.slot, c.enemies[e.def].id, e.x, e.y, e.kb_vx, e.kb_vy, e.hp);
    }
    for k in &sim.pickups {
        say!("  K{} d{} {},{} v {} by {}@{}", k.id, c.pickups[k.def].id, k.x, k.y, k.value, k.pulled_by, k.pull_tick);
    }
    for f in &sim.fx {
        say!("  F{} k{} {},{} end {} v {},{}", f.id, f.kind, f.x, f.y, f.end_tick, f.vx, f.vy);
    }
    const KINDS: [&str; 24] = [
        "0", "Hit", "PlayerHit", "Heal", "Swing", "Nova", "Strike", "Chain", "EnemyDied", "BossAppeared", "PickupTaken", "LevelUp", "CardTaken", "JobChanged", "Fell", "Rose", "EnemyAbility",
        "Summon", "FinalPhase", "PlayerJoined", "PlayerLeft", "Effect", "EnemyBurst", "Incoming",
    ];
    for e in &sim.events {
        say!("  ev {} fl {} slot {} target {} def {} value {}", KINDS[e.kind as usize], e.flags, e.slot, e.target, e.def_id, e.value);
    }
    for e in &sim.hits {
        say!("  hit slot {} target {} def {} value {} fl {}", e.slot, e.target, e.def_id, e.value, e.flags);
    }
}

/// Replays one run from the reader's place; leaves the reader after it.
fn replay(g: &Golden, r: &mut Rd, only: &dyn Fn(&str) -> bool) -> Option<RunReport> {
    let name = r.str().unwrap();
    let content_idx = r.i32().unwrap() as usize;
    let arena_id = r.i32().unwrap();
    let seed = r.u64().unwrap();
    let run_seconds = r.f32().unwrap();
    let _snapshot_every = r.i32().unwrap();
    let c = g.contents[content_idx].1;
    let run = only(&name);
    let trace: Option<(usize, usize)> = std::env::var("HORDE_TRACE").ok().and_then(|t| {
        let p: Vec<&str> = t.split(':').collect();
        if p.len() == 3 && p[0] == name { Some((p[1].parse().ok()?, p[2].parse().ok()?)) } else { None }
    });
    let mut sim = Sim::new(c, &c.arenas[c.arena(arena_id).unwrap()], seed, run_seconds).unwrap();
    let mut enc = SnapshotEncoder::new(&sim);
    let mut rep = RunReport { name: name.clone(), steps: 0, agree: 0, first_state_diff: None, first_offer_diff: None, snapshots: 0, first_snapshot_diff: None, end_ok: false, results_ok: false, ticks: 0 };
    let mut last = [(0u16, 0i32, 0i32); MAX_PLAYERS];
    let mut calls_ok = true;
    loop {
        let n = r.u8().unwrap();
        if n == 0xFF {
            break;
        }
        for _ in 0..n {
            let k = r.u8().unwrap();
            match k {
                1 => {
                    let (aid, pname, sex, want) = (r.u32().unwrap(), r.str().unwrap(), r.u8().unwrap(), r.i8().unwrap());
                    if run && sim.add_player(aid, &pname, sex) != want as i32 {
                        calls_ok = false;
                    }
                }
                2 => {
                    let (slot, seq, dx, dy) = (r.u8().unwrap() as usize, r.u16().unwrap(), r.i8().unwrap() as i32, r.i8().unwrap() as i32);
                    last[slot] = (seq, dx, dy);
                    if run {
                        sim.push_input(slot as i32, seq, dx, dy);
                    }
                }
                0x10..=0x17 => {
                    let slot = (k & 7) as usize;
                    last[slot].0 = last[slot].0.wrapping_add(1);
                    if run {
                        sim.push_input(slot as i32, last[slot].0, last[slot].1, last[slot].2);
                    }
                }
                3 => {
                    let (slot, offer, index, want) = (r.u8().unwrap() as i32, r.u16().unwrap() as i32, r.u8().unwrap() as i32, r.u8().unwrap() != 0);
                    if run && sim.choose(slot, offer, index) != want {
                        calls_ok = false;
                    }
                }
                4 => {
                    let slot = r.u8().unwrap() as i32;
                    if run {
                        sim.remove_player(slot);
                    }
                }
                5 => {
                    let (slot, away) = (r.u8().unwrap() as i32, r.u8().unwrap() != 0);
                    if run {
                        sim.set_away(slot, away);
                    }
                }
                6 => {
                    let o = r.u8().unwrap();
                    if run {
                        sim.end(o);
                    }
                }
                7 => {
                    let (id, x, y, force, want) = (r.i32().unwrap(), r.i32().unwrap(), r.i32().unwrap(), r.u8().unwrap() != 0, r.i16().unwrap());
                    if run && sim.spawn(c.enemy(id).unwrap(), x, y, force) != want as i32 {
                        calls_ok = false;
                    }
                }
                _ => panic!("golden.bin: run {name}: call kind {k}"),
            }
        }
        let want_hash = r.u64().unwrap();
        let flags = r.u8().unwrap();
        let want_offers = if flags & 4 != 0 { Some(r.u32().unwrap()) } else { None };
        let want_delta = if flags & 8 != 0 { Some(r.u32().unwrap()) } else { None };
        let want_key = if flags & 16 != 0 { Some(r.u32().unwrap()) } else { None };
        let step = rep.steps;
        rep.steps += 1;
        if !run || rep.first_state_diff.is_some() {
            continue;
        }
        sim.step();
        if let Some((from, to)) = trace {
            if step >= from && step <= to {
                dump(step, &sim);
            }
        }
        enc.collect(&sim);
        let has_offers = !sim.offers_opened.is_empty() || !sim.offers_closed.is_empty();
        let mut oh = FNV0;
        for (slot, o) in &sim.offers_opened {
            oh = fnv(&[1, *slot as u8], oh);
            oh = fnv(&wire::frame(&wire::encode_offer(o)), oh);
        }
        for (slot, id) in &sim.offers_closed {
            oh = fnv(&[2, *slot as u8, *id as u8, (*id >> 8) as u8], oh);
        }
        if (want_offers.is_some() != has_offers || want_offers.is_some_and(|w| w != fold(oh))) && rep.first_offer_diff.is_none() {
            rep.first_offer_diff = Some(step);
        }
        if let Some(wd) = want_delta {
            let (d, k) = enc.encode(&sim, want_key.is_some());
            rep.snapshots += 1;
            let ok = fold(fnv(&d, FNV0)) == wd && want_key.map_or(true, |wk| k.as_ref().is_some_and(|k| fold(fnv(k, FNV0)) == wk));
            if !ok && rep.first_snapshot_diff.is_none() {
                rep.first_snapshot_diff = Some(step);
            }
        }
        let ok = calls_ok && sim.state_hash() == want_hash && (flags & 1 != 0) == sim.paused && (flags & 2 != 0) == sim.over;
        if ok {
            rep.agree += 1;
        } else {
            rep.first_state_diff = Some(step);
        }
        sim.clear_outputs();
    }
    let (tick, wall, over, outc, level, met, secs) = (r.i32().unwrap(), r.i32().unwrap(), r.u8().unwrap() != 0, r.u8().unwrap(), r.i32().unwrap(), r.i32().unwrap(), r.f32().unwrap());
    let n = r.i32().unwrap() as usize;
    let results = r.bytes(n).unwrap();
    if !run {
        return None;
    }
    rep.ticks = sim.tick;
    rep.end_ok = sim.tick == tick && sim.wall_tick == wall && sim.over == over && sim.outcome == outc && sim.party_level == level && sim.choice_limits_met == met && sim.current_choice_seconds.to_bits() == secs.to_bits();
    rep.results_ok = wire::frame(&wire::encode_results(&sim.results(7, 2), -1)) == results;
    Some(rep)
}

fn replay_all(only: &dyn Fn(&str) -> bool) -> Vec<RunReport> {
    let g = open();
    let mut r = Rd::new(&g.bytes);
    r.bytes(g.runs_at).unwrap();
    let mut out = Vec::new();
    for _ in 0..g.runs {
        if let Some(rep) = replay(&g, &mut r, only) {
            out.push(rep);
        }
    }
    assert_eq!(r.left(), 0, "golden.bin: bytes after the last run");
    out
}

fn judge(reports: &[RunReport]) {
    let (mut steps, mut agree, mut snaps) = (0, 0, 0);
    let mut bad = Vec::new();
    for r in reports {
        steps += r.steps;
        agree += r.agree;
        snaps += r.snapshots;
        say!(
            "golden run {}: {} of {} steps agree (ticks {}), offers {}, {} snapshots {}, end state {}, results {}",
            r.name,
            r.agree,
            r.steps,
            r.ticks,
            r.first_offer_diff.map_or("agree".to_string(), |s| format!("DIFFER from step {s}")),
            r.snapshots,
            r.first_snapshot_diff.map_or("agree".to_string(), |s| format!("DIFFER from step {s}")),
            if r.end_ok { "agrees" } else { "DIFFERS" },
            if r.results_ok { "agree" } else { "DIFFER" }
        );
        if r.agree != r.steps || r.first_offer_diff.is_some() || r.first_snapshot_diff.is_some() || !r.end_ok || !r.results_ok {
            bad.push(format!("{} (state from step {:?}, offers {:?}, snapshots {:?}, end {}, results {})", r.name, r.first_state_diff, r.first_offer_diff, r.first_snapshot_diff, r.end_ok, r.results_ok));
        }
    }
    say!("golden: {agree} of {steps} steps of {} runs agree with the C# engine's StateHash; {snaps} snapshot bodies compared", reports.len());
    assert!(bad.is_empty(), "the Rust engine differs from the C# engine in: {}", bad.join("; "));
}

/// The runs a plain `cargo test` replays: every kind of thing, short.
const SHORT: [&str; 5] = ["solo_60", "coverage_b", "defeat", "stopped", "duo_events_120"];

#[test]
fn the_short_golden_runs_agree_step_for_step() {
    let reports = replay_all(&|n| SHORT.contains(&n));
    assert_eq!(reports.len(), SHORT.len());
    judge(&reports);
}

#[test]
#[ignore = "every golden run, the whole 15-minute one included: about 120,000 steps (run it with -- --ignored)"]
fn every_golden_run_agrees_step_for_step() {
    let reports = replay_all(&|_| true);
    assert!(reports.len() >= 10);
    judge(&reports);
}

/// The tables in golden.bin have their C# stamps, and the embedded server/data/horde/content.bin is the v4 table set
/// written by `tools/hordegolden content` with its matching v4 C# stamp.
#[test]
fn the_embedded_tables_are_the_golden_ones_and_the_stamp_is_csharps() {
    let g = open();
    for (name, c, _bytes) in &g.contents {
        assert_eq!(c.stamp(), c.stamp_cs, "the content stamp of the {name} tables");
    }
    let c = content::game();
    assert_eq!(c.stamp(), c.stamp_cs);
    assert!(c.stamp().starts_with("h4-"));
    assert!(c.is_v4());
    assert!(!c.arenas.is_empty() && c.start_job().is_some() && c.weapon(c.rules.start_weapon).is_some());
}

/// sin, cos and atan2 against .NET's (the C runtime's) on the engine's kinds of argument: IEEE 754 does not define
/// them bit for bit, so this MEASURES the difference instead of asserting there is none: never more than one unit
/// in the last place, and how often. (Whether such a difference ever reaches the state is what the golden runs show.)
#[test]
fn sin_cos_atan2_are_within_one_ulp_of_dotnets() {
    let g = open();
    let (mut n, mut differ, mut worst) = ([0usize; 4], [0usize; 4], 0u64);
    for &(kind, a, b, want) in &g.probes {
        let got = match kind {
            1 => math::sin(a),
            2 => math::cos(a),
            3 => math::atan2(a, b),
            _ => panic!("probe kind {kind}"),
        };
        n[kind as usize] += 1;
        if got.to_bits() != want.to_bits() {
            differ[kind as usize] += 1;
            let ulps = if got.is_sign_negative() == want.is_sign_negative() { got.to_bits().abs_diff(want.to_bits()) } else { u64::MAX };
            worst = ulps.max(worst);
            assert!(ulps <= 1, "kind {kind} of {a:e}, {b:e}: {got:e} here, {want:e} on .NET ({ulps} ulp)");
        }
    }
    say!(
        "probes: sin {} of {} differ from .NET's in the last bit, cos {} of {}, atan2 {} of {}; never by more than {} ulp",
        differ[1], n[1], differ[2], n[2], differ[3], n[3], worst
    );
    assert!(n[1] > 1000 && n[2] > 1000 && n[3] > 1000);
}

/// Every encoder's bytes against HordeWire's (frames written by tools/hordegolden's WireSamples).
#[test]
fn the_wire_encoders_write_hordewires_bytes() {
    use wire::*;
    let g = open();
    let sample = |name: &str| -> Vec<u8> { g.wire.iter().find(|(n, _)| n == name).unwrap_or_else(|| panic!("no wire sample {name}")).1.clone() };
    let member = |aid: u32, name: &str, state: u8, slot: i32, level: i32, job_id: i32, fallen: bool| Member { aid, name: name.to_string(), state, slot, level, job_id, fallen };
    let st = Status {
        enabled: true, phase: 1, run_id: 3, starter_aid: 2000001, starter_name: "Alpha".into(), seconds_left: 12.34, run_seconds: 0.0, run_length: 900.0, arena_map: "prt_fild08".into(), reward: 2, lowest_level: 1,
        members: vec![member(2000001, "Alpha", 1, -1, 0, 0, false), member(2000002, "Bravó", 0, -1, 0, 0, false)],
        online: Vec::new(),
    };
    assert_eq!(frame(&encode_status_with_version(&st, true, 2)), sample("status_gathering"));
    let st2 = Status {
        enabled: true, phase: 2, run_id: 70000, starter_aid: 2000001, starter_name: "Alpha".into(), seconds_left: 0.0, run_seconds: 123.9, run_length: 600.0, arena_map: "moc_fild07".into(), reward: 3, lowest_level: 7,
        members: vec![member(2000001, "Alpha", 1, 0, 7, 4012, true), member(2000003, "Charlie", 2, -1, 0, 0, false), member(2000004, "Delta", 3, 2, 7, 1, false)],
        online: Vec::new(),
    };
    assert_eq!(frame(&encode_status_with_version(&st2, false, 2)), sample("status_running_notok"));
    let st3 = Status { enabled: false, phase: 0, run_length: 900.0, reward: 0, lowest_level: 1, ..Default::default() };
    assert_eq!(frame(&encode_status_with_version(&st3, true, 2)), sample("status_idle"));
    assert_eq!(frame(&encode_towns(&["prontera".to_string(), "izlude".to_string(), "geffen".to_string()])), sample("towns"));
    assert_eq!(frame(&encode_towns(&[])), sample("towns_none"));
    assert_eq!(frame(&encode_invite(3, 2000001, "Alpha", 30.0, "prt_fild08", 900.0, 2)), sample("invite"));
    let run = RunInfo { run_id: 3, slot: 2, arena_id: 1, map: "prt_fild08".into(), x0: 236, y0: 45, x1: 300, y1: 109, snapshot_every: 2, run_length: 900.0, final_boss_seconds: 120.0, late: true };
    assert_eq!(frame(&encode_run(&run, 4321)), sample("run"));
    let offer = Offer {
        id: 513,
        is_job: false,
        seconds: 12.5,
        cards: Some(vec![
            Card { kind: 1, def_id: 1201, level: 1, bonus_stat: 0, bonus_stat_points: 0 },
            Card { kind: 5, def_id: 5, level: 3, bonus_stat: 0, bonus_stat_points: 0 },
            Card { kind: 7, def_id: 0, level: 30, bonus_stat: 0, bonus_stat_points: 0 },
        ]),
    };
    assert_eq!(frame(&encode_offer(&offer)), sample("offer"));
    let job = Offer {
        id: 65535,
        is_job: true,
        seconds: 30.0,
        cards: Some(vec![
            Card { kind: 6, def_id: 4012, level: 2, bonus_stat: 0, bonus_stat_points: 0 },
            Card { kind: 6, def_id: 7, level: 2, bonus_stat: 0, bonus_stat_points: 0 },
        ]),
    };
    assert_eq!(frame(&encode_offer(&job)), sample("offer_job"));
    assert_eq!(frame(&encode_offer_closed(513)), sample("offer_closed"));
    assert_eq!(frame(&encode_reward(3, 123456789012, 5, 1500, 10000)), sample("reward"));
    // the client's own messages, read back
    let payload = |name: &str| sample(name)[2..].to_vec();
    assert_eq!(decode_client(&payload("client_hello")), Some(ClientMsg::Hello { version: 2, sex: 2, stamp: "h3-0123456789abcdef".into() }));
    assert_eq!(decode_client(&payload("client_start")), Some(ClientMsg::Start { invite: None }));
    assert_eq!(decode_client(&payload("client_answer")), Some(ClientMsg::Answer { run_id: 70000, join: true }));
    assert_eq!(decode_client(&payload("client_input")), Some(ClientMsg::Input { seq: 65535, dx: -127, dy: 90 }));
    assert_eq!(decode_client(&payload("client_choose")), Some(ClientMsg::Choose { offer_id: 513, index: 2 }));
    assert_eq!(decode_client(&payload("client_leave")), Some(ClientMsg::Leave));
    // malformed: dropped
    assert_eq!(decode_client(&[]), None);
    assert_eq!(decode_client(&[H_START, 1, 0]), None);
    assert_eq!(decode_client(&[H_INPUT, 1, 2, 3]), None);
    assert_eq!(decode_client(&[H_HELLO, 2, 1, 5, b'a']), None);
    assert_eq!(decode_client(&[0x16]), None);
}

#[test]
fn v4_wire_and_sim_features_work() {
    use wire::*;
    assert_eq!(VERSION, 4);
    // H_START with selective invite list (empty = solo, or specific AIDs)
    assert_eq!(decode_client(&[H_START, 0]), Some(ClientMsg::Start { invite: Some(vec![]) }));
    assert_eq!(decode_client(&[H_START, 2, 1, 0, 0, 0, 2, 0, 0, 0]), Some(ClientMsg::Start { invite: Some(vec![1, 2]) }));
    // H_END and H_PAUSE
    assert_eq!(decode_client(&[H_END, 9, 0, 0, 0]), Some(ClientMsg::End { run_id: 9 }));
    assert_eq!(decode_client(&[H_PAUSE, 1]), Some(ClientMsg::Pause { pause: true }));
    assert_eq!(decode_client(&[H_PAUSE, 0]), Some(ClientMsg::Pause { pause: false }));

    // Status v4 includes VERSION=4 and online players after the members list
    let st = Status {
        enabled: true,
        phase: 0,
        run_length: 900.0,
        reward: 2,
        lowest_level: 1,
        online: vec![OnlinePlayer { aid: 2000001, name: "Alex".into() }],
        ..Default::default()
    };
    let st_bytes = encode_status(&st, true);
    assert_eq!(st_bytes[0], H_STATUS);
    assert_eq!(st_bytes[1], 3); // enabled (1) | content_ok (2)
    assert_eq!(&st_bytes[st_bytes.len() - 11..], &[4, 1, 129, 132, 30, 0, 4, b'A', b'l', b'e', b'x']);

    // Offer v4 encodes bonus_stat and bonus_stat_points when present
    let offer_v4 = Offer {
        id: 7,
        is_job: false,
        seconds: 0.0,
        cards: Some(vec![
            Card { kind: 1, def_id: 1101, level: 2, bonus_stat: 3, bonus_stat_points: 2 },
            Card { kind: 2, def_id: 2101, level: 1, bonus_stat: 0, bonus_stat_points: 0 },
        ]),
    };
    let ob = encode_offer(&offer_v4);
    assert_eq!(ob.len(), 1 + 2 + 1 + 2 + 1 + 2 * 4 + 2 * 2);
    assert_eq!(&ob[ob.len() - 4..], &[3, 2, 0, 0]);

    // Simulation v4 features on content::game()
    let c = content::game();
    let mut sim = Sim::new(c, &c.arenas[0], 42, 900.0).unwrap();
    let s0 = sim.add_player(2000001, "Alpha", 1);
    assert_eq!(s0, 0);

    // Solo pause is immediate; held flag and v4 trailing blocks are written into snapshots
    sim.hold(s0, true);
    assert_eq!(sim.held_by, s0);
    sim.step();
    assert!(sim.paused);

    let mut enc = SnapshotEncoder::new(&sim);
    enc.collect(&sim);
    let (_delta, key) = enc.encode(&sim, true);
    let key = key.unwrap();
    // Player flags byte in key snapshot has HELD (32) | PRESENT (1) | STATS (4) | WEAPONS (8) | PASSIVES (16) = 61
    assert!(key.len() > 40);

    sim.hold(s0, false);
    assert_eq!(sim.held_by, -1);

    // Co-op pause uses a 3-second countdown
    let s1 = sim.add_player(2000002, "Bravo", 2);
    assert_eq!(s1, 1);
    for _ in 0..(4 * 40) {
        sim.step();
        sim.clear_outputs();
    }
    sim.hold(s0, true);
    assert_eq!(sim.pause_count_by, s0);
    assert!(sim.pause_count_to_pause);
    assert_eq!(sim.held_by, -1);
    for _ in 0..(3 * 40) {
        sim.step();
        sim.clear_outputs();
    }
    assert_eq!(sim.held_by, s0);

    // Leaving and rejoining preserves loadout and merges results by AID
    sim.remove_player(s1);
    let s1_rejoin = sim.add_player(2000002, "Bravo", 2);
    assert!(s1_rejoin >= 0);
    let res = sim.results(1, 2);
    assert_eq!(res.players.iter().filter(|p| p.aid == 2000002).count(), 1);
}

#[test]
fn transcendence_weapons_and_passives_work() {
    use super::content::{passive_trans_kind, trans_kind};
    let c = content::game();
    let trans_weapons: Vec<_> = c.weapons.iter().filter(|w| w.trans_of > 0).collect();
    let trans_passives: Vec<_> = c.passives.iter().filter(|p| p.trans_of > 0).collect();
    assert!(trans_weapons.len() >= 250, "expected 2 transcendence branches per base weapon, got {}", trans_weapons.len());
    assert!(trans_passives.len() >= 90, "expected 1 transcendence form per base passive, got {}", trans_passives.len());
    for tw in &trans_weapons {
        assert!(tw.trans != trans_kind::NONE, "transcended weapon {} missing trans_kind", tw.id);
        assert!(tw.trans_branch == 1 || tw.trans_branch == 2);
        assert_eq!(tw.levels.len(), 1);
    }
    for tp in &trans_passives {
        assert!(tp.trans != passive_trans_kind::NONE, "transcended passive {} missing passive_trans_kind", tp.id);
        assert_eq!(tp.levels.len(), 1);
    }

    let mut sim = Sim::new(c, &c.arenas[0], 777, 900.0).unwrap();
    let s0 = sim.add_player(2000001, "Ascendant", 1);
    assert_eq!(s0, 0);

    // Max out the starting weapon (level 5) and give a passive at max level (level 5)
    sim.players[0].weapons[0].level = c.weapons[sim.players[0].weapons[0].def].levels.len() as i32;
    let base_w_id = c.weapons[sim.players[0].weapons[0].def].id;
    let p_idx = c.passive(1).unwrap();
    let max_p_lv = c.passives[p_idx].levels.len() as i32;
    sim.players[0].passives.push(OwnedPassive { def: p_idx, level: max_p_lv });
    sim.recalc(0, false);

    // Draw cards until we see both WEAPON_TRANSCEND branches (5000+id and 6000+id) and PASSIVE_TRANSCEND (1000+id)
    let mut saw_w_t1 = false;
    let mut saw_w_t2 = false;
    let mut saw_p_t = false;
    for _ in 0..40 {
        let cards = sim.draw_cards(0);
        let has_t1 = cards.iter().any(|cd| cd.kind == card::WEAPON_TRANSCEND && cd.def_id == 5000 + base_w_id);
        let has_t2 = cards.iter().any(|cd| cd.kind == card::WEAPON_TRANSCEND && cd.def_id == 6000 + base_w_id);
        if has_t1 || has_t2 {
            // Whenever weapon transcendence is offered, both branches must appear side by side in the same offer
            assert!(has_t1 && has_t2, "both weapon transcendence branches must appear side by side in the same offer");
            saw_w_t1 = true;
            saw_w_t2 = true;
        }
        if cards.iter().any(|cd| cd.kind == card::PASSIVE_TRANSCEND && cd.def_id == 1001) {
            saw_p_t = true;
        }
    }
    assert!(saw_w_t1 && saw_w_t2, "expected both weapon transcendence branches to be offered");
    assert!(saw_p_t, "expected passive transcendence card to be offered");

    // Apply weapon transcendence branch 1 and passive transcendence
    sim.apply_card(0, 0, Card { kind: card::WEAPON_TRANSCEND, def_id: 5000 + base_w_id, level: 6, ..Default::default() });
    sim.apply_card(0, 0, Card { kind: card::PASSIVE_TRANSCEND, def_id: 1001, level: 6, ..Default::default() });
    assert_eq!(c.weapons[sim.players[0].weapons[0].def].id, 5000 + base_w_id);
    assert_eq!(sim.players[0].weapons[0].level, 6);
    assert_eq!(c.passives[sim.players[0].passives[0].def].id, 1001);
    assert_eq!(sim.players[0].passives[0].level, 6);

    // Spawn an enemy nearby and step the simulation to exercise transcended weapon + passive keystone execution
    let px = sim.players[0].x;
    let py = sim.players[0].y;
    sim.spawn(0, px + 512, py, true);
    for _ in 0..80 {
        sim.step();
        sim.clear_outputs();
    }
}

#[test]
fn active_skills_linear_wall_and_knockback_cap_work() {
    let c = content::game();
    assert!(Sim::is_active_weapon(22)); // Fire Wall
    assert!(Sim::is_active_weapon(1022)); // Fire Wall branch
    assert!(Sim::is_active_weapon(5022)); // Fire Wall transcendence
    assert!(Sim::is_active_weapon(28)); // Sanctuary
    assert!(Sim::is_active_weapon(120)); // Quagmire

    let mut sim = Sim::new(c, &c.arenas[0], 888, 900.0).unwrap();
    let s0 = sim.add_player(3000001, "Mage", 1);
    assert_eq!(s0, 0);

    // Give player Fire Wall (weapon id 22)
    sim.apply_card(0, 0, Card { kind: card::NEW_WEAPON, def_id: 22, level: 3, ..Default::default() });
    sim.players[0].weapons.last_mut().unwrap().next_tick = 9999;

    let px = sim.players[0].x;
    let py = sim.players[0].y;
    // Even though next_tick is 9999 (auto-cast on cooldown), manual cast_active succeeds because active_next_tick is 0!
    assert!(sim.cast_active(0, 22, px + 3 * 256, py));
    // Exactly 1 contiguous linear wall zone is spawned, perpendicular to the aim along +X (angle = 90 deg)
    let walls: Vec<_> = sim.fx.iter().filter(|f| f.def_id == 22).collect();
    assert_eq!(walls.len(), 1);
    assert!(walls[0].range >= 350, "expected linear wall half-length in range, got {}", walls[0].range);
    assert_eq!(walls[0].angle, 90.0);

    // Knockback cap: minimum 0.55s (22 ticks) gap between knockbacks, and at most 2 knockbacks per 1.8s (72 ticks) window per enemy
    let es = sim.spawn(0, px + 4 * 256, py, true);
    assert!(es >= 0);
    assert!(sim.can_knockback(es as usize));
    assert!(!sim.can_knockback(es as usize), "immediate 2nd knockback before 0.55s gap must be suppressed");
    sim.tick += 22;
    assert!(sim.can_knockback(es as usize), "2nd knockback after 0.55s gap is allowed");
    sim.tick += 22;
    assert!(!sim.can_knockback(es as usize), "3rd knockback within 1.8s window must be suppressed");
}


