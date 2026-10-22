//! The pet @commands of atcommand.cpp: @makeegg, @hatch, @petfriendly, @pethungry, @petrename, @pettalk, and the
//! pet part of @storeall / @dropall (pet_return_egg for a hatched egg, called from those commands' files).

use super::clif::clif_send_petstatus;
use super::data;
use crate::atcommand::Out;
use crate::data::msg_txt;
use crate::unit;
use crate::world::Ctx;

/// The pet commands (atcommand.rs chains here): Some(result) when `func` is one of them.
pub(crate) fn run(c: &Ctx, out: &Out, sd: u32, func: &str, _command: &str, message: &str) -> Option<i32> {
    Some(match func {
        "makeegg" => makeegg(c, out, sd, message),
        "hatch" => hatch(c, out, sd),
        "petfriendly" => petfriendly(c, out, sd, message),
        "pethungry" => pethungry(c, out, sd, message),
        "petrename" => petrename(c, out, sd),
        "pettalk" => pettalk(c, out, sd, message),
        _ => return None,
    })
}

/// stdlib strtoul(s, nullptr, 10) as an item id: leading blanks, an optional sign, the digits (a minus wraps).
fn strtoul(s: &str) -> u32 {
    let b = s.trim_start_matches([' ', '\t', '\n', '\r', '\x0b', '\x0c']).as_bytes();
    let (neg, digits) = match b.first() {
        Some(b'-') => (true, &b[1..]),
        Some(b'+') => (false, &b[1..]),
        _ => (false, b),
    };
    let mut v: u64 = 0;
    for &d in digits.iter().take_while(|d| d.is_ascii_digit()) {
        v = v.saturating_mul(10).saturating_add((d - b'0') as u64);
    }
    let v = v.min(u32::MAX as u64) as u32;
    if neg {
        v.wrapping_neg()
    } else {
        v
    }
}

/// ACMD_FUNC(makeegg): @makeegg <monster name / id / egg name / egg id>: a pet egg (through the char server).
fn makeegg(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    if message.is_empty() {
        out.say(msg_txt(1015)); // Please enter a monster/egg name/ID (usage: @makeegg <pet>).
        return -1;
    }
    // for monster name
    let has_spawn = crate::mob::spawn::mob_has_spawn_fn(c);
    let mut id = crate::db::mobs::mobdb_searchname(message, &has_spawn) as i32;
    if id == 0 {
        id = crate::cstr::atoi(message);
    }
    // pet_db.find(id): the uint16 key takes the int32's low bits
    let mut pet = crate::db::misc::pet::pet_db().find(id as u16);
    if pet.is_none() {
        // for egg name
        let nameid = match crate::db::items::itemdb_searchname(message) {
            Some(it) => it.nameid,
            None => strtoul(message),
        };
        pet = crate::db::misc::pet::pet_db_search(nameid as i32, crate::db::misc::pet::PetItemType::Egg);
    }
    let mut res = -1;
    if let Some(pet) = pet {
        if let Some(mdb) = crate::db::mobs::mob_db().find(pet.class_ as u32) {
            let char_id = crate::pc::get(c, sd).map(|p| p.char_id).unwrap_or(0);
            // intif_create_pet answers 1 (the char server is always there)
            super::intif_create_pet(sd, char_id, pet.class_ as i16, mdb.lv as i16, pet.egg_id, 0, pet.intimate as i16, 100, 0, 1, &mdb.jname);
            res = 0;
        }
    }
    match res {
        -1 => out.say(msg_txt(180)), // The monster/egg name/id doesn't exist.
        -2 => out.say(msg_txt(407)), //charserver link broken
        _ => {}
    }
    res
}

/// ACMD_FUNC(hatch): the incubator's egg list (msg 181 with a pet out).
fn hatch(c: &Ctx, out: &Out, sd: u32) -> i32 {
    if super::pet_id_of(c, sd) == 0 {
        super::clif::clif_sendegg(c, sd);
    } else {
        out.say(msg_txt(181)); // You already have a pet.
        return -1;
    }
    0
}

/// ACMD_FUNC(petfriendly): @petfriendly <0-1000>.
fn petfriendly(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let friendly = crate::cstr::atoi(message);
    if message.is_empty() || friendly < 0 {
        out.say(msg_txt(1016)); // Please enter a valid value (usage: @petfriendly <0-1000>).
        return -1;
    }
    let Some(pd) = data::of_master(c, sd) else {
        out.say(msg_txt(184)); // Sorry, but you have no pet.
        return -1;
    };
    if !(0..=1000).contains(&friendly) {
        out.say(msg_txt(37)); // An invalid number was specified.
        return -1;
    }
    if friendly == pd.pet.intimate as i32 {
        out.say(msg_txt(183)); // Pet intimacy is already at maximum.
        return -1;
    }
    super::pet_set_intimate(c, pd.id, friendly);
    if let Some(pd) = data::get(c, pd.id) {
        clif_send_petstatus(c, sd, &pd);
    }
    out.say(msg_txt(182)); // Pet intimacy changed.
    0
}

/// ACMD_FUNC(pethungry): @pethungry <0-100>.
fn pethungry(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    let hungry = crate::cstr::atoi(message);
    if message.is_empty() || hungry < 0 {
        out.say(msg_txt(1017)); // Please enter a valid number (usage: @pethungry <0-100>).
        return -1;
    }
    let pd = data::of_master(c, sd);
    let Some(mut pd) = pd.filter(|_| super::pet_id_of(c, sd) != 0) else {
        out.say(msg_txt(184)); // Sorry, but you have no pet.
        return -1;
    };
    if !(0..=100).contains(&hungry) {
        out.say(msg_txt(37)); // An invalid number was specified.
        return -1;
    }
    if hungry == pd.pet.hungry as i32 {
        out.say(msg_txt(186)); // Pet hunger is already at maximum.
        return -1;
    }
    pd.pet.hungry = hungry as i16;
    data::put(c, &pd);
    clif_send_petstatus(c, sd, &pd);
    out.say(msg_txt(185)); // Pet hunger changed.
    0
}

/// ACMD_FUNC(petrename): the pet may be renamed once more (saved at once).
fn petrename(c: &Ctx, out: &Out, sd: u32) -> i32 {
    let pd = data::of_master(c, sd);
    let Some(mut pd) = pd.filter(|_| super::pet_id_of(c, sd) != 0) else {
        out.say(msg_txt(184)); // Sorry, but you have no pet.
        return -1;
    };
    if pd.pet.rename_flag == 0 {
        out.say(msg_txt(188)); // You can already rename your pet.
        return -1;
    }
    pd.pet.rename_flag = 0;
    data::put(c, &pd);
    super::intif_save_petdata(sd, &pd.pet);
    clif_send_petstatus(c, sd, &pd);
    out.say(msg_txt(187)); // You can now rename your pet.
    0
}

/// atcommand.cpp's pet emotion names (ACMD_FUNC(pettalk)'s emo[]), index = emotion_type.
const EMO: [&str; 81] = [
    "/!", "/?", "/ho", "/lv", "/swt", "/ic", "/an", "/ag", "/$", "/...",
    "/scissors", "/rock", "/paper", "/korea", "/lv2", "/thx", "/wah", "/sry", "/heh", "/swt2",
    "/hmm", "/no1", "/??", "/omg", "/O", "/X", "/hlp", "/go", "/sob", "/gg",
    "/kis", "/kis2", "/pif", "/ok", "-?-", "/indonesia", "/bzz", "/rice", "/awsm", "/meh",
    "/shy", "/pat", "/mp", "/slur", "/com", "/yawn", "/grat", "/hp", "/philippines", "/malaysia",
    "/singapore", "/brazil", "/fsh", "/spin", "/sigh", "/dum", "/crwd", "/desp", "/dice", "-dice2",
    "-dice3", "-dice4", "-dice5", "-dice6", "/india", "/love", "/russia", "-?-", "/mobile", "/mail",
    "/chinese", "/antenna1", "/antenna2", "/antenna3", "/hum", "/abs", "/oops", "/spit", "/ene", "/panic",
    "/whisp",
];

/// ACMD_FUNC(pettalk): @pettalk <message>: the pet says it overhead ("<pet name> : <message>"), or shows an emotion for
/// "/<emotion>" (one per second; /dice rolls one of the six dice).
fn pettalk(c: &Ctx, out: &Out, sd: u32, message: &str) -> i32 {
    if crate::conf::min_chat_delay() > 0 {
        let Some(mut p) = crate::pc::get(c, sd) else { return -1 };
        if p.cantalk_tick - c.tick() > 0 {
            return 0;
        }
        p.cantalk_tick = c.tick() + crate::conf::min_chat_delay();
        crate::pc::put(c, &p);
    }
    let pd = data::of_master(c, sd);
    let Some(pd) = pd.filter(|_| super::pet_id_of(c, sd) != 0) else {
        out.say(msg_txt(184)); // Sorry, but you have no pet.
        return -1;
    };
    let block = crate::pc::get(c, sd).map(|p| p.block_action & crate::pc::PCBLOCK_CHAT != 0).unwrap_or(false);
    if crate::status::sc::cant(c, sd, crate::status::consts::SCS_NOCHAT) || block {
        return -1; //no "chatting" while muted.
    }
    // sscanf(message, "%99[^\n]", mes) < 1
    let mes: String = message.split('\n').next().unwrap_or("").chars().take(99).collect();
    if message.is_empty() || mes.is_empty() {
        out.say(msg_txt(1224)); // Please enter a message (usage: @pettalk <message>).
        return -1;
    }
    if message.starts_with('/') {
        // pet emotion processing
        const ET_DICE1: usize = 58;
        const ET_DICE6: usize = 63;
        let mut i = EMO.iter().position(|e| e.eq_ignore_ascii_case(message)).unwrap_or(EMO.len());
        if i == ET_DICE1 {
            i = c.rnd_value(ET_DICE1 as i32, ET_DICE6 as i32) as usize; // randomize /dice
        }
        if i < EMO.len() {
            let Some(mut p) = crate::pc::get(c, sd) else { return -1 };
            let now = c.tick() / 1000;
            if p.emotionlasttime + 1 >= now {
                // not more than 1 per second
                p.emotionlasttime = now;
                crate::pc::put(c, &p);
                return 0;
            }
            p.emotionlasttime = now;
            crate::pc::put(c, &p);
            if let Some(pu) = unit::get(c, pd.id) {
                crate::clif::emotion(c, &pu, i as u8);
            }
            return 0;
        }
    }
    // snprintf(temp, sizeof temp, "%s : %s", pd->pet.name, mes) (CHAT_SIZE_MAX)
    let mut temp = crate::net::wire::latin1(&format!("{} : {}", pd.pet.name, mes));
    temp.truncate(crate::net::CHAT_SIZE_MAX - 1);
    if let Some(pu) = unit::get(c, pd.id) {
        crate::clif_misc::disp_overhead(c, &pu, &temp, crate::clif::Target::Area);
    }
    0
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn emotion_names_and_strtoul() {
        assert_eq!(EMO.len(), 81); // ET_MAX is 88; rAthena's list has 81 names
        assert_eq!(EMO.iter().position(|e| *e == "/dice"), Some(58)); // ET_DICE1
        assert_eq!(EMO.iter().position(|e| *e == "/ok"), Some(33)); // ET_OK
        assert_eq!(strtoul("9001"), 9001);
        assert_eq!(strtoul("  12abc"), 12);
        assert_eq!(strtoul("abc"), 0);
        assert_eq!(strtoul("-1"), u32::MAX);
    }
}
