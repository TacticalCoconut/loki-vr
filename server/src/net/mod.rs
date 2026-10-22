//! The map-server connection as reducer calls. A client sends the bytes of one or more RO packets with the
//! `packet` reducer (exactly what it would write on rAthena's map-server socket); clif.cpp clif_parse frames them
//! with packet_db and hands each to its clif_parse_* function. Replies go out through world.rs (`packet_out`).

pub mod packets;
pub mod wire;

use spacetimedb::{reducer, ReducerContext};

use crate::world::{self, Ctx};

/// mmo.hpp CHAT_SIZE_MAX.
pub const CHAT_SIZE_MAX: usize = 256;

/// What clif_parse leaves in a client's receive buffer when it stops for more data (fewer than 2 bytes, or a
/// variable-length packet with fewer than 4): those bytes go in front of the client's next `packet` call.
#[spacetimedb::table(accessor = rfifo)]
pub struct Rfifo {
    #[primary_key]
    pub identity: spacetimedb::Identity,
    pub data: Vec<u8>,
}

#[reducer]
pub fn packet(rc: &ReducerContext, data: Vec<u8>) {
    use spacetimedb::Table;
    if crate::release::reject_if_outdated_package(rc) {
        return;
    }
    world::run(rc, |c| {
        crate::npc::ensure_world(c);
        let who = c.sender();
        let mut buf = c.db().rfifo().identity().find(who).map(|r| r.data).unwrap_or_default();
        if !buf.is_empty() {
            c.db().rfifo().identity().delete(who);
        }
        buf.extend_from_slice(&data);
        // common/socket.cpp:255-257, 378, 1031-1035: a client's receive buffer is RFIFO_SIZE (2048) bytes and never
        // grows; recv_to_fifo reads at most what fits, func_parse runs on that (clif_parse: at most three packets a
        // pass, clif.cpp:25679), and a buffer that is still full after the parse is "an invalid packet (too big and
        // not parsed)": set_eof. So a client that delivers 2048 bytes or more at once (with what was left from before)
        // has the first three packets of its first 2048 bytes handled and is then disconnected; the rest is never
        // read. (Checked first of all, so that an oversized call costs no more than this.)
        if buf.len() >= crate::rules::RFIFO_SIZE {
            log::warn!("packet: {} sent {} bytes at once (receive buffer {}): disconnecting", who, buf.len(), crate::rules::RFIFO_SIZE);
            buf.truncate(crate::rules::RFIFO_SIZE);
            if parse_some(c, &buf, CLIF_PARSE_PACKETS_PER_PASS).is_some() {
                drop_session(c);
            }
            return;
        }
        if let Some(rest) = parse(c, &buf) {
            if rest < buf.len() {
                c.db().rfifo().insert(Rfifo { identity: who, data: buf[rest..].to_vec() });
            }
        }
    });
}

/// The client closed its map-server link but keeps its SpacetimeDB connection (leaving for the login screen, or a
/// failed map load): to rAthena that is the socket closing (clif_quitsave, with prevent_logout's delay).
#[reducer]
pub fn map_disconnect(rc: &ReducerContext) {
    world::run(rc, |c| crate::pc::on_client_gone(c, c.sender()));
}

/// clif.cpp clif_parse over one buffer. Returns where the bytes it leaves for the next receive start (None: the
/// session was closed, nothing is kept). (rAthena handles at most three packets per socket per server loop and the
/// rest in the next ones; a reducer call handles its whole buffer.)
fn parse(c: &Ctx, data: &[u8]) -> Option<usize> {
    parse_some(c, data, usize::MAX)
}

/// clif.cpp:25679 `for( pnum = 0; pnum < 3; ++pnum )`: the packets clif_parse handles in one pass of the server loop.
const CLIF_PARSE_PACKETS_PER_PASS: usize = 3;

/// [`parse`], stopping after `max_packets` packets (only the pass that ends a connection whose receive buffer is full
/// is limited: see `packet`).
fn parse_some(c: &Ctx, data: &[u8], max_packets: usize) -> Option<usize> {
    let mut p = 0usize;
    let mut handled = 0usize;
    while data.len() - p >= 2 {
        if handled == max_packets {
            break;
        }
        handled += 1;
        let cmd = u16::from_le_bytes([data[p], data[p + 1]]);
        let Some(len) = packets::packet_len(cmd).filter(|&l| l != 0) else {
            log::warn!("clif_parse: Received unsupported packet (packet 0x{cmd:04x}, {} bytes received) from {}", data.len() - p, c.sender());
            drop_session(c);
            return None;
        };
        let mut len = len as i32;
        if len == -1 {
            // variable-length packet
            if data.len() - p < 4 {
                return Some(p);
            }
            len = u16::from_le_bytes([data[p + 2], data[p + 3]]) as i32;
            if !(4..=32768).contains(&len) {
                log::warn!("clif_parse: Received packet 0x{cmd:04x} specifies invalid packet_len ({len})");
                drop_session(c);
                return None;
            }
        }
        let len = len as usize;
        if data.len() - p < len {
            log::warn!("clif_parse: Received packet 0x{cmd:04x} with expected packet length {len}, but only {} bytes remaining", data.len() - p);
            drop_session(c);
            return None;
        }
        if packets::parser_name(cmd) == "clif_parse_WantToConnection" {
            // (a character of this client that is only waiting out clif_delayquit is a closed connection, not this
            // one) clif_parse_WantToConnection_sub compares the whole rest of the receive buffer with the packet's
            // length; a refused connect packet drops the rest (RFIFOSKIP, set_eof)
            if !crate::pc::want_to_connection(c, &data[p..p + len], data.len() - p) {
                return None;
            }
            p += len;
            continue;
        }
        dispatch(c, cmd, &data[p..p + len]);
        p += len;
        // clif_parse tests eof before each packet: a handler that closed this session (set_eof) ends the parse here,
        // and do_close drops what the client sent after it (session.rs)
        if crate::session::close_if_eof(c, c.sender()) {
            return None;
        }
    }
    Some(p)
}

/// set_eof: the connection is closed as rAthena would close the socket — the character leaves.
fn drop_session(c: &Ctx) {
    crate::pc::on_client_gone(c, c.sender());
}

/// do_close: what was left in a closed connection's receive buffer goes with it.
pub fn clear_rfifo(c: &Ctx, who: spacetimedb::Identity) {
    c.db().rfifo().identity().delete(who);
}

fn dispatch(c: &Ctx, cmd: u16, pkt: &[u8]) {
    let func = packets::parser_name(cmd);
    let who = c.sender();
    let sd = crate::pc::by_identity(c, who);
    if func.is_empty() {
        return;
    }
    let Some(sd) = sd else { return }; // only WantToConnection (see parse) is valid without a session
    if sd.disconnected {
        return;
    }
    let on_map = crate::unit::get(c, sd.account_id).map(|u| u.on_map).unwrap_or(false);
    if !on_map && func != "clif_parse_LoadEndAck" {
        return; // only LoadEndAck is valid while the player is not on a map
    }
    let aid = sd.account_id;
    match func {
        "clif_parse_LoadEndAck" => crate::pc::load_end_ack(c, aid),
        "clif_parse_RemoveOption" => crate::clif_option::clif_parse_remove_option(c, aid),
        "clif_parse_TickSend" => crate::parse::tick_send(c, aid, pkt),
        "clif_parse_WalkToXY" => crate::parse::walk_to_xy(c, aid, pkt),
        "clif_parse_QuitGame" => crate::parse::quit_game(c, aid),
        "clif_parse_GetCharNameRequest" => crate::parse::get_char_name_request(c, aid, pkt),
        "clif_parse_GlobalMessage" => crate::parse::global_message(c, aid, pkt),
        "clif_parse_ChangeDir" => crate::parse::change_dir(c, aid, pkt),
        "clif_parse_Emotion" => crate::parse::emotion(c, aid, pkt),
        "clif_parse_ActionRequest" => crate::parse::action_request(c, aid, pkt),
        "clif_parse_Restart" => crate::parse::restart(c, aid, pkt),
        "clif_parse_WisMessage" => crate::parse::wis_message(c, aid, pkt),
        "clif_parse_HowManyConnections" => crate::parse::how_many_connections(c, aid),
        "clif_parse_MapMove" => crate::parse::map_move(c, aid, pkt),
        "clif_parse_NpcClicked" => crate::script::npc_run::parse_npc_clicked(c, aid, pkt),
        "clif_parse_NpcSelectMenu" => crate::script::npc_run::parse_npc_select_menu(c, aid, pkt),
        "clif_parse_NpcNextClicked" => crate::script::npc_run::parse_npc_next_clicked(c, aid, pkt),
        "clif_parse_NpcAmountInput" => crate::script::npc_run::parse_npc_amount_input(c, aid, pkt),
        "clif_parse_NpcStringInput" => crate::script::npc_run::parse_npc_string_input(c, aid, pkt),
        "clif_parse_NpcCloseClicked" => crate::script::npc_run::parse_npc_close_clicked(c, aid, pkt),
        "clif_parse_StatusUp" => crate::status::progress::parse_status_up(c, aid, pkt),
        "clif_parse_traitstatus_up" => crate::status::progress::parse_traitstatus_up(c, aid, pkt),
        // skill tree and hotkeys (clif_skill.rs, hotkey.rs)
        "clif_parse_SkillUp" => crate::clif_skill::parse_skill_up(c, aid, pkt),
        // skill use (wave D skillcore: skill/parse.rs)
        "clif_parse_UseSkillToId" => crate::skill::parse::parse_use_skill_to_id(c, aid, pkt),
        "clif_parse_UseSkillToPos" => crate::skill::parse::parse_use_skill_to_pos(c, aid, pkt),
        "clif_parse_UseSkillToPosMoreInfo" => crate::skill::parse::parse_use_skill_to_pos_more_info(c, aid, pkt),
        "clif_parse_AutoSpell" => crate::skill::parse::parse_auto_spell(c, aid, pkt),
        "clif_parse_SkillSelectMenu" => crate::skill::parse::parse_skill_select_menu(c, aid, pkt),
        // skill item list window answer (wave E mage: SO_EL_ANALYSIS; GN_CHANGEMATERIAL is merchant's arm inside)
        "clif_parse_ItemListWindowSelected" => crate::skill::parse::parse_item_list_window_selected(c, aid, pkt),
        "clif_parse_StartUseSkillToId" => crate::skill::parse::parse_start_use_skill_to_id(c, aid, pkt),
        "clif_parse_StopUseSkillToId" => crate::skill::parse::parse_stop_use_skill_to_id(c, aid, pkt),
        "clif_parse_NoviceExplosionSpirits" => crate::skill::parse::parse_novice_explosion_spirits(c, aid, pkt),
        "clif_parse_Hotkey" => crate::hotkey::parse_hotkey(c, aid, pkt),
        "clif_parse_HotkeyRowShift" => crate::hotkey::parse_hotkey_row_shift(c, aid, pkt),
        // items (inventory.rs, pc_equip.rs, flooritem.rs, storage.rs, cart.rs, shop.rs)
        "clif_parse_UseItem" => crate::parse_items::use_item(c, aid, pkt),
        "clif_parse_EquipItem" => crate::parse_items::equip_item(c, aid, pkt),
        "clif_parse_UnequipItem" => crate::parse_items::unequip_item(c, aid, pkt),
        "clif_parse_DropItem" => crate::flooritem::parse_drop_item(c, aid, pkt),
        "clif_parse_TakeItem" => crate::flooritem::parse_take_item(c, aid, pkt),
        "clif_parse_MoveToKafra" => crate::storage::parse_move_to_kafra(c, aid, pkt),
        "clif_parse_MoveFromKafra" => crate::storage::parse_move_from_kafra(c, aid, pkt),
        "clif_parse_MoveToKafraFromCart" => crate::storage::parse_move_to_kafra_from_cart(c, aid, pkt),
        "clif_parse_MoveFromKafraToCart" => crate::storage::parse_move_from_kafra_to_cart(c, aid, pkt),
        "clif_parse_CloseKafra" => crate::storage::parse_close_kafra(c, aid),
        "clif_parse_PutItemToCart" => crate::cart::parse_put_item_to_cart(c, aid, pkt),
        "clif_parse_GetItemFromCart" => crate::cart::parse_get_item_from_cart(c, aid, pkt),
        "clif_parse_ChangeCart" => crate::cart::parse_change_cart(c, aid, pkt),
        "clif_parse_SelectCart" => crate::cart::parse_select_cart(c, aid, pkt),
        "clif_parse_NpcBuySellSelected" => crate::shop::parse_buy_sell_selected(c, aid, pkt),
        "clif_parse_NpcBuyListSend" => crate::shop::parse_buy_list_send(c, aid, pkt),
        "clif_parse_NpcSellListSend" => crate::shop::parse_sell_list_send(c, aid, pkt),
        "clif_parse_NPCShopClosed" => crate::shop::parse_shop_closed(c, aid),
        "clif_parse_NPCMarketClosed" => crate::shop::parse_market_closed(c, aid),
        "clif_parse_NPCMarketPurchase" => crate::shop::parse_market_purchase(c, aid, pkt),
        "clif_parse_barter_buy" => crate::barter::parse_barter_buy(c, aid, pkt),
        "clif_parse_barter_close" => crate::barter::parse_barter_close(c, aid),
        "clif_parse_barter_extended_buy" => crate::barter::parse_barter_extended_buy(c, aid, pkt),
        "clif_parse_barter_extended_close" => crate::barter::parse_barter_extended_close(c, aid),
        "clif_parse_equipswitch_add" => crate::parse_items::equipswitch_add(c, aid, pkt),
        "clif_parse_equipswitch_remove" => crate::parse_items::equipswitch_remove(c, aid, pkt),
        "clif_parse_equipswitch_request" => crate::parse_items::equipswitch_request(c, aid),
        "clif_parse_equipswitch_request_single" => crate::parse_items::equipswitch_request_single(c, aid, pkt),
        "clif_parse_questStateAck" => crate::quest::parse_quest_state_ack(c, aid, pkt),
        // parties (party.rs)
        "clif_parse_CreateParty" => crate::party::parse_create_party(c, aid, pkt),
        "clif_parse_CreateParty2" => crate::party::parse_create_party2(c, aid, pkt),
        "clif_parse_PartyInvite" => crate::party::parse_party_invite(c, aid, pkt),
        "clif_parse_PartyInvite2" => crate::party::parse_party_invite2(c, aid, pkt),
        "clif_parse_ReplyPartyInvite" => crate::party::parse_reply_party_invite(c, aid, pkt),
        "clif_parse_ReplyPartyInvite2" => crate::party::parse_reply_party_invite2(c, aid, pkt),
        "clif_parse_LeaveParty" => crate::party::parse_leave_party(c, aid),
        "clif_parse_RemovePartyMember" => crate::party::parse_remove_party_member(c, aid, pkt),
        "clif_parse_PartyChangeOption" => crate::party::parse_party_change_option(c, aid, pkt),
        "clif_parse_PartyMessage" => crate::party::parse_party_message(c, aid, pkt),
        "clif_parse_PartyChangeLeader" => crate::party::parse_party_change_leader(c, aid, pkt),
        "clif_parse_PartyBookingRegisterReq" => crate::party::parse_party_booking_register_req(c, aid, pkt),
        "clif_parse_PartyBookingSearchReq" => crate::party::parse_party_booking_search_req(c, aid, pkt),
        "clif_parse_PartyBookingDeleteReq" => crate::party::parse_party_booking_delete_req(c, aid),
        "clif_parse_PartyBookingUpdateReq" => crate::party::parse_party_booking_update_req(c, aid, pkt),
        "clif_parse_PartyTick" => crate::party::parse_party_tick(c, aid, pkt),
        "clif_parse_partybooking_join" => crate::party::parse_partybooking_join(c, aid, pkt),
        "clif_parse_partybooking_reply" => crate::party::parse_partybooking_reply(c, aid, pkt),
        // friends and whisper ignore lists (social.rs)
        "clif_parse_FriendsListAdd" => crate::social::parse_friends_list_add(c, aid, pkt),
        "clif_parse_FriendsListReply" => crate::social::parse_friends_list_reply(c, aid, pkt),
        "clif_parse_FriendsListRemove" => crate::social::parse_friends_list_remove(c, aid, pkt),
        "clif_parse_PMIgnore" => crate::social::parse_pm_ignore(c, aid, pkt),
        "clif_parse_PMIgnoreAll" => crate::social::parse_pm_ignore_all(c, aid, pkt),
        "clif_parse_PMIgnoreList" => crate::social::parse_pm_ignore_list(c, aid),
        // chat rooms (chat.rs)
        "clif_parse_CreateChatRoom" => crate::chat::parse_create_chat_room(c, aid, pkt),
        "clif_parse_ChatAddMember" => crate::chat::parse_chat_add_member(c, aid, pkt),
        "clif_parse_ChatRoomStatusChange" => crate::chat::parse_chat_room_status_change(c, aid, pkt),
        "clif_parse_ChangeChatOwner" => crate::chat::parse_change_chat_owner(c, aid, pkt),
        "clif_parse_KickFromChat" => crate::chat::parse_kick_from_chat(c, aid, pkt),
        "clif_parse_ChatLeave" => crate::chat::parse_chat_leave(c, aid),
        // trade (trade.rs)
        "clif_parse_TradeRequest" => crate::trade::parse_trade_request(c, aid, pkt),
        "clif_parse_TradeAck" => crate::trade::parse_trade_ack(c, aid, pkt),
        "clif_parse_TradeAddItem" => crate::trade::parse_trade_add_item(c, aid, pkt),
        "clif_parse_TradeOk" => crate::trade::parse_trade_ok(c, aid),
        "clif_parse_TradeCancel" => crate::trade::parse_trade_cancel(c, aid),
        "clif_parse_TradeCommit" => crate::trade::parse_trade_commit(c, aid),
        // bank (bank.rs), cash shop and sales (cashshop.rs)
        "clif_parse_BankOpen" => crate::bank::parse_bank_open(c, aid, pkt),
        "clif_parse_BankClose" => crate::bank::parse_bank_close(c, aid, pkt),
        "clif_parse_BankCheck" => crate::bank::parse_bank_check(c, aid, pkt),
        "clif_parse_BankDeposit" => crate::bank::parse_bank_deposit(c, aid, pkt),
        "clif_parse_BankWithdraw" => crate::bank::parse_bank_withdraw(c, aid, pkt),
        "clif_parse_cashshop_open_request" => crate::cashshop::parse_cashshop_open_request(c, aid, pkt),
        "clif_parse_cashshop_close" => crate::cashshop::parse_cashshop_close(c, aid),
        "clif_parse_CashShopReqTab" => crate::cashshop::parse_cash_shop_req_tab(c, aid),
        "clif_parse_cashshop_list_request" => crate::cashshop::parse_cashshop_list_request(c, aid),
        "clif_parse_cashshop_buy" => crate::cashshop::parse_cashshop_buy(c, aid, pkt),
        "clif_parse_npccashshop_buy" => crate::cashshop::parse_npccashshop_buy(c, aid, pkt),
        "clif_parse_sale_refresh" => crate::cashshop::parse_sale_refresh(c, aid, pkt),
        "clif_parse_sale_open" => crate::cashshop::parse_sale_open(c, aid, pkt),
        "clif_parse_sale_close" => crate::cashshop::parse_sale_close(c, aid, pkt),
        "clif_parse_sale_search" => crate::cashshop::parse_sale_search(c, aid, pkt),
        "clif_parse_sale_add" => crate::cashshop::parse_sale_add(c, aid, pkt),
        "clif_parse_sale_remove" => crate::cashshop::parse_sale_remove(c, aid, pkt),
        // buying store (buyingstore.rs), store search (searchstore.rs)
        "clif_parse_ReqOpenBuyingStore" => crate::buyingstore::parse_req_open_buying_store(c, aid, pkt),
        "clif_parse_ReqCloseBuyingStore" => crate::buyingstore::parse_req_close_buying_store(c, aid),
        "clif_parse_ReqClickBuyingStore" => crate::buyingstore::parse_req_click_buying_store(c, aid, pkt),
        "clif_parse_ReqTradeBuyingStore" => crate::buyingstore::parse_req_trade_buying_store(c, aid, pkt),
        "clif_parse_SearchStoreInfo" => crate::searchstore::parse_search_store_info(c, aid, pkt),
        "clif_parse_SearchStoreInfoNextPage" => crate::searchstore::parse_search_store_info_next_page(c, aid),
        "clif_parse_CloseSearchStoreInfo" => crate::searchstore::parse_close_search_store_info(c, aid),
        "clif_parse_SearchStoreInfoListItemClick" => crate::searchstore::parse_search_store_info_list_item_click(c, aid, pkt),
        // vending (vending.rs)
        "clif_parse_CloseVending" => crate::vending::parse_close_vending(c, aid),
        "clif_parse_OpenVending" => crate::vending::parse_open_vending(c, aid, pkt),
        "clif_parse_VendingListReq" => crate::vending::parse_vending_list_req(c, aid, pkt),
        "clif_parse_PurchaseReq" => crate::vending::parse_purchase_req(c, aid, pkt),
        "clif_parse_PurchaseReq2" => crate::vending::parse_purchase_req2(c, aid, pkt),
        // achievements and titles (achievement.rs)
        "clif_parse_AchievementCheckReward" => crate::achievement::parse_achievement_check_reward(c, aid, pkt),
        "clif_parse_change_title" => crate::achievement::parse_change_title(c, aid, pkt),
        // fame rankings (fame.rs)
        "clif_parse_ranklist" => crate::fame::parse_ranklist(c, aid, pkt),
        // attendance (attendance.rs), clans (clan.rs)
        "clif_parse_open_ui" => crate::attendance::parse_open_ui(c, aid, pkt),
        "clif_parse_attendance_request" => crate::attendance::parse_attendance_request(c, aid),
        "clif_parse_clan_chat" => crate::clan::parse_clan_chat(c, aid, pkt),
        // RoDEX mail (mail.rs)
        "clif_parse_Mail_refreshinbox" => crate::mail::parse_mail_refreshinbox(c, aid, pkt),
        "clif_parse_Mail_read" => crate::mail::parse_mail_read(c, aid, pkt),
        "clif_parse_Mail_getattach" => crate::mail::parse_mail_getattach(c, aid, pkt),
        "clif_parse_Mail_delete" => crate::mail::parse_mail_delete(c, aid, pkt),
        "clif_parse_Mail_return" => crate::mail::parse_mail_return(c, aid, pkt),
        "clif_parse_Mail_setattach" => crate::mail::parse_mail_setattach(c, aid, pkt),
        "clif_parse_Mail_winopen" => crate::mail::parse_mail_winopen(c, aid, pkt),
        "clif_parse_Mail_send" => crate::mail::parse_mail_send(c, aid, pkt),
        "clif_parse_Mail_beginwrite" => crate::mail::parse_mail_beginwrite(c, aid, pkt),
        "clif_parse_Mail_cancelwrite" => crate::mail::parse_mail_cancelwrite(c, aid),
        "clif_parse_Mail_Receiver_Check" => crate::mail::parse_mail_receiver_check(c, aid, pkt),
        // the Roulette of Fortune (roulette.rs; CZ_RECV_ROULETTE_ITEM's condition byte is not read)
        "clif_parse_roulette_open" => crate::roulette::parse_roulette_open(c, aid),
        "clif_parse_roulette_info" => crate::roulette::parse_roulette_info(c, aid),
        "clif_parse_roulette_close" => crate::roulette::parse_roulette_close(c, aid),
        "clif_parse_roulette_generate" => crate::roulette::parse_roulette_generate(c, aid),
        "clif_parse_roulette_item" => crate::roulette::parse_roulette_item(c, aid),
        // script2: progress bars, memo, GM packets (pc_misc.rs)
        "clif_parse_progressbar" => crate::pc_misc::parse_progressbar(c, aid),
        "clif_parse_RequestMemo" => crate::pc_misc::clif_parse_request_memo(c, aid),
        "clif_parse_Adopt_request" => crate::script::buildins::script2::family::clif_parse_adopt_request(c, aid, pkt),
        "clif_parse_Adopt_reply" => crate::script::buildins::script2::family::clif_parse_adopt_reply(c, aid, pkt),
        // skill units: the Warp Portal / Teleport destination menu (skillunit/castend.rs)
        "clif_parse_UseSkillMap" => crate::skillunit::castend::clif_parse_use_skill_map(c, aid, pkt),
        // CZ_STANDING_RESURRECTION: clif.cpp clif_parse_AutoRevive is pc_revive_item(sd) (atcmd agent: atcommand_inv.rs)
        "clif_parse_AutoRevive" => {
            crate::atcommand_atcmd::inv::pc_revive_item(c, aid);
        }
        // item windows: identify, cards, refine UI, weapon refine, repair, grade, enchant, reform, laphine, stylist, merge, crafting (itemui/)
        f if crate::itemui::parse(c, aid, f, pkt) => {}
        // guilds (clif_guild.rs)
        f if crate::clif_guild::parse(c, aid, f, pkt) => {}
        // instances: the destroy button, and the load confirmation that sends the instance window (clif_instance.rs)
        "clif_parse_MemorialDungeonCommand" => crate::clif_instance::parse_memorial_dungeon_command(c, aid, pkt),
        "clif_parse_blocking_playcancel" => crate::clif_instance::parse_blocking_playcancel(c, aid),
        // pets, and CZ_CONFIG (pet/clif.rs)
        f if crate::pet::clif::parse(c, aid, f, pkt) => {}
        f if crate::clif_bg::parse(c, aid, f, pkt) => {}
        // homunculi (homunculus/clif.rs)
        f if crate::homunculus::clif::parse(c, aid, f, pkt) => {}
        // mercenaries: CZ_MER_COMMAND, and the homunculus / mercenary requests HomMoveToMaster / HomMoveTo / HomAttack
        // (one handler each, both arms in clif.cpp's order: mercenary/clif.rs)
        f if crate::mercenary::clif::parse(c, aid, f, pkt) => {}
        // the Star Gladiator's Feeling confirmation and /doridori (pc_taekwon.rs)
        f if crate::pc_taekwon::parse(c, aid, f, pkt) => {}
        // the captcha / macro detector: /macro_register, /macro_detector and the answer window (macro_detector.rs)
        f if crate::macro_detector::parse(c, aid, f, pkt) => {}
        // the GM slash commands and right-click menu (clif_gm.rs), the other small client requests (clif_client.rs)
        f if crate::clif_gm::parse(c, aid, f, pkt) => {}
        f if crate::clif_client::parse(c, aid, f, pkt) => {}
        // the auction (auction.rs; its char-server half is int_auction.rs)
        f if crate::auction::parse(c, aid, f, pkt) => {}
        // every connect packet went to pc::want_to_connection in `parse` above (clif_parse_WantToConnection_sub needs the
        // rest of the receive buffer; an already logged-in session only gets its "invalid request" log there)
        "clif_parse_WantToConnection" => {}
        _ => crate::parse::not_yet(c, aid, cmd, func),
    }
}
