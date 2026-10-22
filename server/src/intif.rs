//! map/intif.cpp + char/inter.cpp: the map server ↔ char server link, as a queue.
//!
//! rAthena's map server asks the char server for party work with intif_* packets on the inter socket and acts on the
//! answers (intif_parse_*) when they arrive: always after the map-server function that sent the request has
//! returned (and after every packet the map server sent to clients meanwhile). The module has both servers in one
//! transaction, so a request is queued here ([`send`]), and at the end of the reducer ([`drain`], called by
//! world::run before the packets are flushed) each queued message is handled in order: requests by the char server
//! (int_party.rs), whose answers are queued behind whatever is already waiting, and answers by the map server
//! (party.rs). That is the order in which rAthena's two processes see these messages over their one TCP link.

use std::cell::RefCell;
use std::collections::VecDeque;

use crate::int_guild::{GuildMember, GuildPosition, MmoGuild};
use crate::int_party::Party;
use crate::party::PartyMember;
use crate::world::Ctx;

/// One message on the inter link. `To char` variants are intif_* requests (their packet id in the doc), `to map`
/// variants are mapif_* answers.
#[derive(Clone, Debug)]
pub enum Inter {
    // ---- map → char (char/int_party.cpp inter_party_parse_frommap)
    /// 0x3020 intif_create_party.
    CreateParty { name: String, item: u8, item2: u8, leader: PartyMember },
    /// 0x3021 intif_request_partyinfo.
    PartyInfo { party_id: u32, char_id: u32 },
    /// 0x3022 intif_party_addmember.
    PartyAddMember { party_id: u32, member: PartyMember },
    /// 0x3023 intif_party_changeoption.
    PartyChangeOption { party_id: u32, account_id: u32, exp: u16, item: u16 },
    /// 0x3024 intif_party_leave.
    PartyLeave { party_id: u32, account_id: u32, char_id: u32, name: String, kind: u8 },
    /// 0x3025 intif_party_changemap.
    PartyChangeMap { party_id: u32, account_id: u32, char_id: u32, online: u8, lv: u16, map: String },
    /// 0x3026 intif_break_party.
    BreakParty { party_id: u32 },
    /// 0x3029 intif_party_leaderchange.
    PartyLeaderChange { party_id: u32, account_id: u32, char_id: u32 },
    /// 0x302A intif_party_sharelvlupdate.
    PartyShareLevel { share_lvl: u32 },
    /// chrif_removefriend (0x2b07 → char_mapif.cpp chmapif_parse_askrmfriend): the friend is not online.
    RemoveFriend { char_id: u32, friend_id: u32 },
    /// 0x3080 intif_create_pet (char/int_pet.cpp mapif_parse_CreatePet).
    CreatePet { account_id: u32, char_id: u32, pet_class: i16, pet_lv: i16, egg_id: u32, equip: u32, intimate: i16, hungry: i16, rename_flag: i8, incubate: i8, name: String },
    /// 0x3083 intif_delete_petdata (mapif_parse_DeletePet).
    DeletePet { pet_id: u32 },
    /// 0x3081 intif_request_petdata (mapif_parse_LoadPet) and 0x3082 intif_save_petdata (mapif_parse_SavePet).
    LoadPet { account_id: u32, char_id: u32, pet_id: u32 },
    SavePet { account_id: u32, pet: crate::int_pet::SPet },
    /// 0x3006 intif_rename (char/inter.cpp mapif_parse_NameChangeRequest; type 0 PC, 1 PET, 2 HOM).
    NameChange { account_id: u32, char_id: u32, kind: u8, name: Vec<u8> },

    // ---- map → char (char/int_guild.cpp inter_guild_parse_frommap, char/int_storage.cpp, char/char_mapif.cpp)
    /// 0x3030 intif_guild_create.
    CreateGuild { account_id: u32, name: String, master: GuildMember },
    /// 0x3031 intif_guild_request_info.
    GuildInfo { guild_id: i32 },
    /// 0x3032 intif_guild_addmember.
    GuildAddMember { guild_id: i32, member: GuildMember },
    /// 0x3033 intif_guild_change_gm (`len` = strlen(name) + 1).
    GuildMasterChange { guild_id: i32, name: String, len: usize },
    /// 0x3034 intif_guild_leave (flag 0 leave, 1 expulsion; mes 40 bytes).
    GuildLeave { guild_id: i32, account_id: u32, char_id: u32, flag: u8, mes: String },
    /// 0x3035 intif_guild_memberinfoshort.
    GuildMemberInfoShort { guild_id: i32, account_id: u32, char_id: u32, online: u8, lv: u16, class: u16 },
    /// 0x3036 intif_guild_break.
    BreakGuild { guild_id: i32 },
    /// 0x3039 intif_guild_change_basicinfo (the value of the type's field).
    GuildBasicInfoChange { guild_id: i32, kind: u16, value: i64 },
    /// 0x303a intif_guild_change_memberinfo.
    GuildMemberInfoChange { guild_id: i32, account_id: u32, char_id: u32, kind: u16, value: i64 },
    /// 0x303b intif_guild_position.
    GuildPosition { guild_id: i32, idx: i32, pos: GuildPosition },
    /// 0x303c intif_guild_skillup.
    GuildSkillUp { guild_id: i32, skill_id: u16, account_id: u32, max: i32 },
    /// 0x303d intif_guild_alliance.
    GuildAlliance { guild_id1: i32, guild_id2: i32, account_id1: u32, account_id2: u32, flag: i32 },
    /// 0x303e intif_guild_notice.
    GuildNotice { guild_id: i32, mes1: String, mes2: String },
    /// 0x303f intif_guild_emblem.
    GuildEmblem { guild_id: i32, data: Vec<u8> },
    /// 0x3042 intif_guild_emblem_version.
    GuildEmblemVersion { guild_id: i32, version: i32 },
    /// chrif save / auth (0x2b01 final save → char_set_char_offline, auth → char_set_char_online):
    /// inter_guild_CharOnline / inter_guild_CharOffline with the character's guild id (-1 = look it up).
    GuildCharOnline { char_id: u32 },
    GuildCharOffline { char_id: u32 },
    /// 0x3018 intif_request_guild_storage.
    LoadGuildStorage { account_id: u32, guild_id: i32 },
    /// 0x3019 intif_send_guild_storage (the map server's copy of the storage goes with it).
    SaveGuildStorage { account_id: u32, guild_id: i32, items: Vec<crate::inventory::Item> },

    // ---- char → map (map/intif.cpp intif_parse)
    /// 0x3820 mapif_party_created (party None = failure).
    PartyCreated { account_id: u32, char_id: u32, party: Option<(u32, String)> },
    /// 0x3821 mapif_party_info / mapif_party_noinfo (party None).
    PartyInfoAck { party_id: u32, char_id: u32, party: Option<Party> },
    /// 0x3822 mapif_party_memberadded.
    PartyMemberAdded { party_id: u32, account_id: u32, char_id: u32, flag: u8 },
    /// 0x3823 mapif_party_optionchanged.
    PartyOptionChanged { party_id: u32, account_id: u32, exp: u16, item: u16, flag: u8 },
    /// 0x3824 mapif_party_withdraw.
    PartyWithdraw { party_id: u32, account_id: u32, char_id: u32, name: String, kind: u8 },
    /// 0x3825 mapif_party_membermoved.
    PartyMove { party_id: u32, account_id: u32, char_id: u32, online: u8, lv: u16, map: String },
    /// 0x3826 mapif_party_broken.
    PartyBroken { party_id: u32 },

    // ---- extras agent: achievements (char/int_achievement.cpp inter_achievement_parse_frommap, map/intif.cpp)
    /// 0x3063 intif_achievement_save (map → char).
    AchievementSave { char_id: u32, achievements: Vec<crate::achievement::Achievement> },
    /// 0x3064 intif_achievement_reward (map → char).
    AchievementReward { char_id: u32, achievement_id: i32, nameid: u32, amount: u16, char_name: String, achievement_name: String },
    /// 0x3863 mapif_achievement_save (char → map).
    AchievementSaveAck { char_id: u32, success: bool },
    /// 0x3864 mapif_achievement_reward (char → map).
    AchievementRewardAck { char_id: u32, achievement_id: i32, rewarded: u32 },
    // ---- extras agent: the fame lists (map/chrif.cpp ↔ char/char_mapif.cpp, the same link)
    /// 0x2b10 chrif_updatefamelist (map → char).
    FameUpdate { char_id: u32, fame: i32, ranktype: u8 },
    /// 0x2b1a chrif_buildfamelist (map → char).
    FameBuild,
    /// 0x2b1b chmapif_send_fame_list (char → map).
    FameListRecv { lists: Vec<Vec<crate::fame::FameEntry>> },
    /// 0x2b22 chmapif_update_fame_list (char → map).
    FameListAck { ranktype: u8, index: u8, fame: i32 },
    /// RoDEX mail, both directions (int_mail.rs).
    Mail(crate::int_mail::MailInter),
    /// The auction, both directions (int_auction.rs AuctionInter: 0x3050-0x3055 / 0x3850-0x3855).
    Auction(crate::int_auction::AuctionInter),
    /// The map ↔ char ↔ login messages of the account @commands (atcmd agent: atcommand_accounts.rs AccInter).
    Accounts(crate::atcommand_atcmd::accounts::AccInter),
    /// The Vending skill's cart round trip and the autotrade restore's logins, both directions (vending.rs).
    Vending(crate::vending::VendingInter),
    /// The homunculus messages, both directions (int_homun.rs HomInter: 0x3090-0x3093 / 0x3890-0x3893).
    Homun(crate::int_homun::HomInter),
    /// Mercenaries and elementals, both directions (0x3070-0x3073 / 0x307c-0x307f and their answers; companion.rs).
    MercElem(crate::companion::MercElemInter),
    /// 0x3880 mapif_pet_created (intif_parse_CreatePet).
    PetCreated { account_id: u32, pet_class: i16, pet_id: u32 },
    /// 0x3881 mapif_pet_info (flag 0) / mapif_pet_noinfo (flag 1) (intif_parse_RecvPetData).
    PetInfo { account_id: u32, pet: crate::int_pet::SPet, flag: u8 },
    /// 0x3882 mapif_save_pet_ack (intif_parse_SavePetOk) and 0x3883 mapif_delete_pet_ack (intif_parse_DeletePetOk).
    SavePetAck { account_id: u32, flag: u8 },
    DeletePetAck { flag: u8 },
    /// 0x3806 mapif_namechange_ack (intif_parse_ChangeNameOk).
    NameChangeAck { account_id: u32, char_id: u32, kind: u8, flag: u8, name: Vec<u8> },
    /// 0x3830 mapif_guild_created (guild id 0 = failure).
    GuildCreated { account_id: u32, guild_id: i32 },
    /// 0x3831 mapif_guild_info / mapif_guild_noinfo (guild None).
    GuildInfoAck { guild_id: i32, guild: Option<Box<MmoGuild>> },
    /// 0x3832 mapif_guild_memberadded.
    GuildMemberAdded { guild_id: i32, account_id: u32, char_id: u32, flag: u8 },
    /// 0x3834 mapif_guild_withdraw.
    GuildWithdraw { guild_id: i32, account_id: u32, char_id: u32, flag: u8, name: String, mes: String },
    /// 0x3835 mapif_guild_memberinfoshort.
    GuildMemberInfoShortAck { guild_id: i32, account_id: u32, char_id: u32, online: u8, lv: u16, class: u16 },
    /// 0x3836 mapif_guild_broken.
    GuildBroken { guild_id: i32, flag: u8 },
    /// 0x3839 mapif_guild_basicinfochanged.
    GuildBasicInfoChanged { guild_id: i32, kind: u16, value: i64 },
    /// 0x383a mapif_guild_memberinfochanged.
    GuildMemberInfoChanged { guild_id: i32, account_id: u32, char_id: u32, kind: u16, value: i64 },
    /// 0x383b mapif_guild_position.
    GuildPositionAck { guild_id: i32, idx: i32, pos: GuildPosition },
    /// 0x383c mapif_guild_skillupack.
    GuildSkillUpAck { guild_id: i32, skill_id: u16, account_id: u32 },
    /// 0x383d mapif_guild_alliance.
    GuildAllianceAck { guild_id1: i32, guild_id2: i32, account_id1: u32, account_id2: u32, flag: u8, name1: String, name2: String },
    /// 0x383e mapif_guild_notice.
    GuildNoticeAck { guild_id: i32, mes1: String, mes2: String },
    /// 0x383f mapif_guild_emblem.
    GuildEmblemAck { guild_id: i32, emblem_id: i32, data: Vec<u8> },
    /// 0x3841 mapif_guild_emblem_version.
    GuildEmblemVersionAck { guild_id: i32, emblem_id: i32 },
    /// 0x3843 mapif_guild_master_changed.
    GuildMasterChanged { guild_id: i32, account_id: u32, char_id: u32, time: i64 },
    /// 0x3818 mapif_load_guild_storage (items None: the guild does not exist, guild id 0).
    LoadGuildStorageAck { account_id: u32, guild_id: i32, flag: u8, storage: Option<(u16, Vec<crate::inventory::Item>)> },
    /// 0x3819 mapif_save_guild_storage_ack.
    SaveGuildStorageAck { account_id: u32, guild_id: i32, fail: u8 },
    // ---- woe agent: castles (char/int_guild.cpp, map/intif.cpp; woe/int_castle.rs, woe.rs)
    /// 0x3040 intif_guild_castle_dataload (map → char): the castles to load, in the map server's castle_db order.
    GuildCastleDataLoad { castle_ids: Vec<i32> },
    /// 0x3041 intif_guild_castle_datasave (map → char): WFIFOW castle id, WFIFOB index, WFIFOL value.
    GuildCastleDataSave { castle_id: u16, index: u8, value: i32 },
    /// 0x3840 mapif_guild_castle_dataload (char → map): the castles' data, in the request's order.
    GuildCastleDataLoadAck { castles: Vec<crate::woe::CastleData> },
}

thread_local! {
    static QUEUE: RefCell<VecDeque<Inter>> = const { RefCell::new(VecDeque::new()) };
}

/// A new reducer call starts with an empty link (a failed call may have left messages behind: its transaction was
/// rolled back, so they never happened).
pub fn reset() {
    QUEUE.with(|q| q.borrow_mut().clear());
}

/// WFIFOSET on the inter socket (either direction).
pub fn send(msg: Inter) {
    QUEUE.with(|q| q.borrow_mut().push_back(msg));
}

/// Handles every queued message in order (see the module doc). Messages queued while handling are handled too.
pub fn drain(c: &Ctx) {
    // a runaway exchange must not spin forever inside one transaction
    for _ in 0..100_000 {
        let Some(m) = QUEUE.with(|q| q.borrow_mut().pop_front()) else { return };
        match m {
            Inter::CreateParty { name, item, item2, leader } => crate::int_party::mapif_parse_create_party(c, name, item, item2, leader),
            Inter::PartyInfo { party_id, char_id } => crate::int_party::mapif_parse_party_info(c, party_id, char_id),
            Inter::PartyAddMember { party_id, member } => crate::int_party::mapif_parse_party_add_member(c, party_id, member),
            Inter::PartyChangeOption { party_id, account_id, exp, item } => crate::int_party::mapif_parse_party_change_option(c, party_id, account_id, exp, item),
            Inter::PartyLeave { party_id, account_id, char_id, name, kind } => crate::int_party::mapif_parse_party_leave(c, party_id, account_id, char_id, &name, kind),
            Inter::PartyChangeMap { party_id, account_id, char_id, online, lv, map } => crate::int_party::mapif_parse_party_change_map(c, party_id, account_id, char_id, online, lv, &map),
            Inter::BreakParty { party_id } => crate::int_party::mapif_parse_break_party(c, party_id),
            Inter::PartyLeaderChange { party_id, account_id, char_id } => crate::int_party::mapif_parse_party_leader_change(c, party_id, account_id, char_id),
            Inter::PartyShareLevel { share_lvl } => crate::int_party::mapif_parse_party_share_level(c, share_lvl),
            Inter::RemoveFriend { char_id, friend_id } => crate::social::char_removefriend(c, char_id, friend_id),
            Inter::CreatePet { account_id, char_id, pet_class, pet_lv, egg_id, equip, intimate, hungry, rename_flag, incubate, name } => {
                crate::int_pet::mapif_create_pet(c, account_id, char_id, pet_class, pet_lv, egg_id, equip, intimate, hungry, rename_flag, incubate, &name)
            }
            Inter::DeletePet { pet_id } => crate::int_pet::inter_pet_delete(c, pet_id),
            Inter::PetCreated { account_id, pet_class, pet_id } => {
                crate::pet::pet_get_egg(c, account_id, pet_class, pet_id);
            }
            m @ (Inter::LoadPet { .. } | Inter::SavePet { .. } | Inter::NameChange { .. } | Inter::PetInfo { .. } | Inter::SavePetAck { .. } | Inter::DeletePetAck { .. } | Inter::NameChangeAck { .. }) => {
                crate::pet::intif_dispatch(c, m)
            }
            m @ (Inter::CreateGuild { .. }
            | Inter::GuildInfo { .. }
            | Inter::GuildAddMember { .. }
            | Inter::GuildMasterChange { .. }
            | Inter::GuildLeave { .. }
            | Inter::GuildMemberInfoShort { .. }
            | Inter::BreakGuild { .. }
            | Inter::GuildBasicInfoChange { .. }
            | Inter::GuildMemberInfoChange { .. }
            | Inter::GuildPosition { .. }
            | Inter::GuildSkillUp { .. }
            | Inter::GuildAlliance { .. }
            | Inter::GuildNotice { .. }
            | Inter::GuildEmblem { .. }
            | Inter::GuildEmblemVersion { .. }
            | Inter::GuildCharOnline { .. }
            | Inter::GuildCharOffline { .. }
            | Inter::LoadGuildStorage { .. }
            | Inter::SaveGuildStorage { .. }
            | Inter::GuildCreated { .. }
            | Inter::GuildInfoAck { .. }
            | Inter::GuildMemberAdded { .. }
            | Inter::GuildWithdraw { .. }
            | Inter::GuildMemberInfoShortAck { .. }
            | Inter::GuildBroken { .. }
            | Inter::GuildBasicInfoChanged { .. }
            | Inter::GuildMemberInfoChanged { .. }
            | Inter::GuildPositionAck { .. }
            | Inter::GuildSkillUpAck { .. }
            | Inter::GuildAllianceAck { .. }
            | Inter::GuildNoticeAck { .. }
            | Inter::GuildEmblemAck { .. }
            | Inter::GuildEmblemVersionAck { .. }
            | Inter::GuildMasterChanged { .. }
            | Inter::LoadGuildStorageAck { .. }
            | Inter::SaveGuildStorageAck { .. }) => crate::guild::intif_dispatch(c, m),
            Inter::PartyCreated { account_id, char_id, party } => match party {
                Some((party_id, name)) => crate::party::party_created(c, account_id, char_id, 0, party_id, &name),
                None => crate::party::party_created(c, account_id, char_id, 1, 0, ""),
            },
            Inter::PartyInfoAck { party_id, char_id, party } => match party {
                Some(p) => crate::party::party_recv_info(c, &p, char_id),
                None => crate::party::party_recv_noinfo(c, party_id, char_id),
            },
            Inter::PartyMemberAdded { party_id, account_id, char_id, flag } => crate::party::party_member_added(c, party_id, account_id, char_id, flag),
            Inter::PartyOptionChanged { party_id, account_id, exp, item, flag } => crate::party::party_optionchanged(c, party_id, account_id, exp, item, flag),
            Inter::PartyWithdraw { party_id, account_id, char_id, name, kind } => crate::party::party_member_withdraw(c, party_id, account_id, char_id, &name, kind),
            Inter::PartyMove { party_id, account_id, char_id, online, lv, map } => crate::party::party_recv_movemap(c, party_id, account_id, char_id, online, lv, &map),
            Inter::PartyBroken { party_id } => {
                crate::party::party_broken(c, party_id);
            }
            Inter::AchievementSave { char_id, achievements } => crate::achievement::mapif_parse_achievement_save(c, char_id, &achievements),
            Inter::AchievementReward { char_id, achievement_id, nameid, amount, char_name, achievement_name } => {
                crate::achievement::mapif_parse_achievement_reward(c, char_id, achievement_id, nameid, amount, &char_name, &achievement_name)
            }
            Inter::AchievementSaveAck { char_id, success } => crate::achievement::intif_parse_achievementsave(c, char_id, success),
            Inter::AchievementRewardAck { char_id, achievement_id, rewarded } => crate::achievement::intif_parse_achievementreward(c, char_id, achievement_id, rewarded),
            Inter::FameUpdate { char_id, fame, ranktype } => crate::fame::chmapif_parse_updfamelist(c, char_id as i32, fame, ranktype),
            Inter::FameBuild => crate::fame::chmapif_parse_reqfamelist(c),
            Inter::FameListRecv { lists } => crate::fame::chrif_recvfamelist(c, &lists),
            Inter::FameListAck { ranktype, index, fame } => crate::fame::chrif_updatefamelist_ack(c, ranktype, index, fame),
            Inter::Mail(m) => crate::int_mail::dispatch(c, m),
            Inter::Auction(m) => crate::int_auction::dispatch(c, m),
            Inter::Accounts(m) => crate::atcommand_atcmd::accounts::dispatch(c, m),
            Inter::Vending(m) => crate::vending::intif_dispatch(c, m),
            Inter::Homun(m) => crate::int_homun::dispatch(c, m),
            Inter::MercElem(m) => crate::companion::intif_dispatch(c, m),
            m @ (Inter::GuildCastleDataLoad { .. } | Inter::GuildCastleDataSave { .. } | Inter::GuildCastleDataLoadAck { .. }) => crate::woe::intif_dispatch(c, m),
        }
    }
    log::error!("intif: the inter-server queue did not empty (runaway exchange); dropping the rest");
    reset();
}
