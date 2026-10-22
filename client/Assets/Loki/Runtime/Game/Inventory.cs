using System;
using System.Collections.Generic;
using Loki.Net;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// The player's inventory, keyed by the client index (server index + 2). Layouts are rAthena's for PACKETVER 20211103:
    ///   0x0b08 ZC_INVENTORY_START  len.W invType.B name.?     (clears the list for invType 0)
    ///   0x0b09 NORMALITEM_INFO[]   (34 bytes each) / 0x0b39 EQUIPITEM_INFO[] (68 bytes each), after len.W invType.B
    ///   0x0b0b ZC_INVENTORY_END    invType.B flag.B
    ///   0x0b41 ZC_ITEM_PICKUP_ACK  (item added or pickup failure)       0x00af ZC_ITEM_THROW_ACK index.W count.W
    ///   0x07fa ZC_DELETE_ITEM_FROM_BODY deleteType.W index.W count.W    0x01c8 ZC_USE_ITEM_ACK index.W itemId.L AID.L amount.W result.B
    ///   0x0999 ZC_REQ_WEAR_EQUIP_ACK index.W wearLocation.L sprite.W result.B (0 = ok)
    ///   0x099a ZC_REQ_TAKEOFF_EQUIP_ACK index.W wearLocation.L flag.B (0 = ok)
    /// </summary>
    public sealed class Inventory
    {
        public sealed class Entry
        {
            public int Index, ItemId, Amount, Type; public uint Location, Wearing; public bool Identified; public int Refine;
            /// <summary>Enchant grade 0 none … 4 A (EQUIPITEM_INFO grade / ZC_ITEM_PICKUP_ACK byte 69).</summary>
            public int Grade; public bool Damaged;
            /// <summary>HireExpireDate ≠ 0 (rental: can't be dropped/traded) and bindOnEquipType == 2 (character/account bound).</summary>
            public bool Rental, Bound;
            /// <summary>Client wave 2: the favourite tab (list Flag bit 1 for normal items / bit 2 for equipment, 0x0b41 byte 65, ZC_INVENTORY_TAB
            /// 0x0908), the rental's end as a UNIX time (HireExpireDate) and bindOnEquipType (1 binds when first worn, 2 bound).</summary>
            public bool Favorite; public uint ExpireAt; public int BindType;
            /// <summary>slot card[4]: inserted card item ids (0 = empty). For equipment only.</summary>
            public int[] Cards = new int[4];
            /// <summary>Client wave 3 (w3equip): the five random options of EQUIPITEM_INFO / ZC_ITEM_PICKUP_ACK option_data (Id 0 = empty).</summary>
            public RandomOption[] Options = new RandomOption[5];
            public bool HasOptions { get { if (Options != null) foreach (var o in Options) if (o.Id > 0) return true; return false; } }
            /// <summary>Client wave 4: Location was widened to EQP_ARMS by <see cref="ApplyDualWield"/> (not by the server's list).</summary>
            public bool DualPatched;
            /// <summary>True when this item was newly gained via pickup/reward and its equipment slot has not yet been clicked.</summary>
            public bool IsNew;
            public bool IsEquip => Location != 0;
            public bool IsCard => Type == 6; // rAthena IT_CARD
            public GameDb.Item Info => GameDb.GetItem(ItemId);
        }

        /// <summary>One ItemOptions entry: index.W value.W param.B (packets_struct.hpp:450-454), filled from item.option[] by
        /// clif_add_random_options (clif.cpp:2809-2828: id = OptIndex, value, param = Param1; empty ones are all 0). The id is the
        /// client's EnumVAR number (datainfo\enumvar.lub; names in RandomOptionDb).</summary>
        public struct RandomOption { public int Id, Value, Param; }

        public readonly SortedDictionary<int, Entry> Items = new SortedDictionary<int, Entry>();
        /// <summary>EQP_AMMO (mmo.hpp equip_pos).</summary>
        public const uint AmmoSlot = 0x8000;
        /// <summary>Client index of the equipped ammunition from the last ZC_EQUIP_ARROW (-1 none seen).</summary>
        public int AmmoIndex { get; private set; } = -1;
        /// <summary>Client wave 2: inventory slots (100 + ZC_EXTEND_BODYITEM_SIZE) and the rentals' remaining time (item id → seconds, when heard).</summary>
        public int Capacity { get; private set; } = 100;
        public readonly Dictionary<int, (int seconds, float at)> RentalLeft = new Dictionary<int, (int, float)>();
        /// <summary>Seconds a rental item has left (-1 unknown): the server's countdown, else its absolute end time.</summary>
        public int SecondsLeft(Entry e)
        {
            if (RentalLeft.TryGetValue(e.ItemId, out var t)) return Mathf.Max(0, t.seconds - (int)(Time.time - t.at));
            if (e.ExpireAt != 0) return (int)Math.Max(0, (long)e.ExpireAt - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return -1;
        }
        /// <summary>CZ_INVENTORY_TAB 0x0907 index.W flag.B: 0 moves the item to the favourite tab, 1 back (clif.cpp:19895-19920).</summary>
        public static byte[] FavoritePacket(int index, bool favorite) => new PacketWriter(0x0907).U16((ushort)index).U8((byte)(favorite ? 0 : 1)).ToArray();
        public event Action Changed;
        /// <summary>Item gained (id, amount) — for pickup messages and sounds.</summary>
        public event Action<int, int> Gained;
        public event Action<string> Message;
        /// <summary>One of our own items was used successfully (item id).</summary>
        public event Action<int> Used;
        /// <summary>Returns true for an index whose next 0x0b41 is not a pickup (a trade window giving the item back): no Gained event.</summary>
        public Func<int, bool> QuietPickup;
        private uint _selfAid;

        public void SetSelf(uint aid) { _selfAid = aid; }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            int end = o + n;
            switch (id)
            {
                case 0x0b08: { r.Skip(4); if (r.U8() == 0) { Items.Clear(); Changed?.Invoke(); } return true; }
                case 0x0b0b: { Changed?.Invoke(); return true; }
                case 0x0b09:
                {
                    r.Skip(4); int inv = r.U8();
                    while (end - r.Position >= 34)
                    {
                        // NORMALITEM_INFO: index.W ITID.L type.B count.W WearState.L slot(4×L) HireExpireDate.L Flag.B
                        var e = ReadNormal(ref r);
                        if (inv == 0) Items[e.Index] = e;
                    }
                    if (inv == 0) Changed?.Invoke();
                    return true;
                }
                case 0x0b39:
                {
                    r.Skip(4); int inv = r.U8();
                    while (end - r.Position >= 68)
                    {
                        // EQUIPITEM_INFO: index.W ITID.L type.B location.L WearState.L slot(16) HireExpire.L bindOnEquipType.W wItemSpriteNumber.W
                        //                 option_count.B option_data(5×5) RefiningLevel.B grade.B Flag.B
                        var e = ReadEquip(ref r);
                        if (inv == 0) Items[e.Index] = e;
                    }
                    if (inv == 0) Changed?.Invoke();
                    return true;
                }
                case 0x0b41:
                {
                    // PACKET_ZC_ITEM_PICKUP_ACK: Index.W count.W nameid.L IsIdentified.B IsDamaged.B slot(16) location.L type.B result.B
                    //   HireExpireDate.L bindOnEquipType.W option_data(25) favorite.B look.W refiningLevel.B grade.B
                    r.Skip(2);
                    int idx = r.U16(), count = r.U16(); int itemId = (int)r.U32(); bool ident = r.U8() != 0; bool damaged = r.U8() != 0; var cards = ReadCards(ref r);
                    uint loc = r.U32(); int type = r.U8(); int result = r.U8(); uint expire = r.U32(); int bind = r.U16(); var opts = ReadOptions(ref r); bool fav = r.U8() != 0; r.Skip(2); int refine = r.U8(); int grade = r.U8();
                    if (result != 0) { Message?.Invoke(PickupFailText(result)); return true; }
                    bool quiet = QuietPickup != null && QuietPickup(idx);
                    if (Items.TryGetValue(idx, out var e) && e.ItemId == itemId) { e.Amount += count; if (!quiet) e.IsNew = true; }
                    else Items[idx] = new Entry { Index = idx, ItemId = itemId, Amount = count, Type = type, Location = loc, Identified = ident, Refine = refine, Grade = grade, Cards = cards, Options = opts, Rental = expire != 0, ExpireAt = expire, Bound = bind == 2, BindType = bind, Damaged = damaged, Favorite = fav, IsNew = !quiet };
                    if (!quiet) Gained?.Invoke(itemId, count);
                    Changed?.Invoke();
                    return true;
                }
                case 0x00af: { r.Skip(2); int idx = r.U16(), count = r.U16(); Remove(idx, count); return true; }
                case 0x07fa: { r.Skip(2); r.Skip(2); int idx = r.U16(), count = r.S16(); Remove(idx, count); return true; }
                case 0x01c8:
                {
                    // index.W itemId.L AID.L amount.W result.B — amount is what remains after use (pc_useitem sends amount-1 and deletes silently)
                    r.Skip(2); int idx = r.U16(); int pktItemId = (int)r.U32(); uint aid = r.U32(); int amount = r.S16(); int ok = r.U8();
                    if ((aid == _selfAid || _selfAid == 0) && ok != 0)
                    {
                        int usedId = pktItemId;
                        if (Items.TryGetValue(idx, out var e))
                        {
                            if (usedId == 0) usedId = e.ItemId;
                            if (amount <= 0) Items.Remove(idx); else e.Amount = amount;
                            Changed?.Invoke();
                        }
                        if (usedId > 0) Used?.Invoke(usedId);
                    }
                    else if (aid == _selfAid && ok == 0) Message?.Invoke(Lang.T("That item can't be used right now."));
                    return true;
                }
                case 0x0999:
                {
                    // wearLocation = the position(s) the item went to: for an accessory / a dual-wield weapon / a shadow accessory the one
                    // side pc_equipitem chose from the requested mask (pc.cpp:12088-12125), else every position of the item
                    r.Skip(2); int idx = r.U16(); uint loc = r.U32(); r.Skip(2); int res = r.U8();
                    LastWear = (idx, loc, res); WearAcks++;
                    if (res == 0 && Items.TryGetValue(idx, out var e)) { e.Wearing = loc; e.IsNew = false; Audio.LokiAudio.Instance?.PlayWav("se_equip_item_01.wav", null, 0.7f); Changed?.Invoke(); }
                    else if (res != 0) Message?.Invoke(res == 1 ? Lang.T("You can't equip that yet (level requirement).") : Lang.T("Couldn't equip that item.")); // e_equip_item_ack: 0 ok, 1 level, 2 fail (clif.hpp:523-532)
                    return true;
                }
                case 0x099a:
                {
                    // ZC_ACK_TAKEOFF_EQUIP_V5 index.W wearLocation.L flag.B: flag 0 = taken off (clif_unequipitemack inverts success for
                    // PACKETVER >= 20110824, clif.cpp:4338-4352). Also sent for the item a new one pushes out of its slot (pc.cpp:12150-12156)
                    // and for arrows dropped with their bow (pc.cpp:12461-12478).
                    r.Skip(2); int idx = r.U16(); uint loc = r.U32(); int flag = r.U8();
                    LastTakeOff = (idx, loc, flag); TakeOffAcks++;
                    if (flag == 0 && Items.TryGetValue(idx, out var e)) { e.Wearing = 0; if (idx == AmmoIndex) AmmoIndex = -1; Audio.LokiAudio.Instance?.PlayWav("se_equip_item_01.wav", null, 0.65f); Changed?.Invoke(); }
                    else if (flag != 0) Message?.Invoke(Lang.T("That can't be taken off right now."));
                    return true;
                }
                case 0x0bae:
                {
                    // ZC_ACK_TAKEOFF_EQUIP_ALL result.B (packets_struct.hpp:5179-5184): 1 = refused (pc_cant_act: an NPC dialog, a chat room,
                    // trading, vending, storage or a crafting / refine / stylist window open, pc.hpp:1156-1168), 0 = done: every regular slot was
                    // emptied first, each with its own 0x099a (clif_parse_unequipall, clif.cpp:22820-22844)
                    r.Skip(2); int res = r.U8(); LastTakeOffAll = res; TakeOffAllAcks++;
                    Debug.Log($"[Inventory] take off all → {(res == 0 ? "done" : "refused")}");
                    if (res != 0) Message?.Invoke(Lang.T("Close the open window first."));
                    Changed?.Invoke();
                    return true;
                }
                case 0x02da:
                {
                    // ZC_CONFIG_NOTIFY flag.B: whether others may open our equipment (show_equip), sent on every map change including
                    // the first (clif_equipcheckbox, clif.cpp:10300-10313 / 11015-11019; packets.hpp:1955-1959)
                    r.Skip(2); ShowEquip = r.U8() != 0; Debug.Log($"[Inventory] others may view my equipment: {ShowEquip}"); Changed?.Invoke(); return true;
                }
                case 0x02d9:
                {
                    // ZC_CONFIG type.L flag.L: type 0 = CONFIG_OPEN_EQUIPMENT_WINDOW (clif.hpp:719-726), the answer to our CZ_CONFIG
                    // (clif_parse_configuration, clif.cpp:17727-17765). Other types are left to Companions (2, 3) and LokiGame (5).
                    r.Skip(2); if (r.S32() != 0) return false;
                    ShowEquip = r.S32() != 0; Debug.Log($"[Inventory] others may view my equipment: {ShowEquip}"); Changed?.Invoke(); return true;
                }
                case 0x013c:
                {
                    // ZC_EQUIP_ARROW index.W: the equipped ammunition (clif_arrowequip). Equipping arrows gets no 0x0999 (pc_equipitem sends
                    // 0x013c + ZC_ACTION_FAILURE 3 for EQP_AMMO), and the inventory list sends it after the normal items (clif.cpp:3125-3126).
                    // Taking them off is a normal 0x099a.
                    r.Skip(2); int idx = r.S16();
                    foreach (var o2 in Items.Values) if ((o2.Wearing & AmmoSlot) != 0 && o2.Index != idx) o2.Wearing &= ~AmmoSlot;
                    if (Items.TryGetValue(idx, out var e)) e.Wearing = AmmoSlot;
                    AmmoIndex = idx;
                    Debug.Log($"[Inventory] ammunition equipped: index {idx} ({(e != null ? GameDb.GetItem(e.ItemId).English : "not in the list yet")})");
                    Changed?.Invoke();
                    return true;
                }
                // ---- client wave 2 (research/items_combat.md §A-C)
                case 0x0908:
                {
                    // ZC_INVENTORY_TAB index.W flag.B — 0 = now in the favourite tab (the lists say it the other way round: bit set = favourite)
                    r.Skip(2); int idx = r.S16(); bool fav = r.U8() == 0;
                    if (Items.TryGetValue(idx, out var e)) { e.Favorite = fav; Changed?.Invoke(); }
                    Debug.Log($"[Inventory] favourite {idx} = {fav}");
                    return true;
                }
                case 0x0b18:
                {
                    // ZC_EXTEND_BODYITEM_SIZE expansionSize.W: inventory slots = 100 + this (pc_authok at login, and after an expansion)
                    r.Skip(2); Capacity = 100 + r.S16(); Debug.Log($"[Inventory] capacity {Capacity}"); Changed?.Invoke(); return true;
                }
                case 0x0298:
                {
                    // ZC_CASH_TIME_COUNTER itemId.L seconds.L: a rental's remaining time (at login, hourly, and when one is added)
                    r.Skip(2); int itemId = (int)r.U32(); int secs = r.S32(); RentalLeft[itemId] = (secs, Time.time);
                    string when = secs >= 3600 ? $"{secs / 3600} h {secs % 3600 / 60} min" : secs >= 60 ? $"{secs / 60} min" : $"{secs} s";
                    Message?.Invoke(Lang.F("{0} (rental) expires in {1}.", GameDb.GetItem(itemId).Name, when));
                    Changed?.Invoke(); return true;
                }
                case 0x0299:
                {
                    // ZC_CASH_ITEM_DELETE index.W itemId.L: a rental ran out (its 0x07fa removal follows)
                    r.Skip(2); r.S16(); int itemId = (int)r.U32(); RentalLeft.Remove(itemId);
                    Message?.Invoke(Lang.F("{0} (rental) has expired and is gone.", GameDb.GetItem(itemId).Name));
                    return true;
                }
                case 0x02d3:
                {
                    // ZC_NOTIFY_BIND_ON_EQUIP index.W: wearing it bound it to the character
                    r.Skip(2); int idx = r.S16();
                    if (Items.TryGetValue(idx, out var e)) { e.Bound = true; e.BindType = 2; Message?.Invoke(Lang.F("{0} is now bound to you.", e.Info.Name)); Changed?.Invoke(); }
                    return true;
                }
                case 0x02bb:
                {
                    // ZC_EQUIPITEM_DAMAGED location.W gid.L (rAthena never sends it: breaking gear re-sends the whole list with the damaged bit)
                    r.Skip(2); int loc = r.U16(); uint gid = r.U32();
                    if (gid == _selfAid) Message?.Invoke(Lang.T("Your equipment was damaged."));
                    return true;
                }
                default: return false;
            }
        }

        /// <summary>NORMALITEM_INFO (34 bytes): index.W ITID.L type.B count.W WearState.L slot(4×L) HireExpireDate.L Flag.B (bit0 identified).
        /// WearState here is the item's equippable position (`id->equip`, clif.cpp:3002 — EQP_AMMO for arrows), not whether it is worn:
        /// stackables are never reported as worn by the list, so it lands in Location and Wearing stays 0.</summary>
        public static Entry ReadNormal(ref PacketReader r)
        {
            var e = new Entry();
            e.Index = r.U16(); e.ItemId = (int)r.U32(); e.Type = r.U8(); e.Amount = r.S16(); e.Location = r.U32(); e.Cards = ReadCards(ref r); e.ExpireAt = r.U32(); e.Rental = e.ExpireAt != 0;
            int flag = r.U8(); e.Identified = (flag & 1) != 0; e.Favorite = (flag & 2) != 0; // NORMALITEM_INFO Flag: IsIdentified, PlaceETCTab (packets_struct.hpp:418-448)
            return e;
        }

        /// <summary>EQUIPITEM_INFO (68 bytes): index.W ITID.L type.B location.L WearState.L slot(16) HireExpire.L bindOnEquipType.W
        /// wItemSpriteNumber.W option_count.B option_data(5×5) RefiningLevel.B grade.B Flag.B (bit0 identified).</summary>
        public static Entry ReadEquip(ref PacketReader r)
        {
            var e = new Entry { Amount = 1 };
            e.Index = r.U16(); e.ItemId = (int)r.U32(); e.Type = r.U8(); e.Location = r.U32(); e.Wearing = r.U32(); e.Cards = ReadCards(ref r); e.ExpireAt = r.U32(); e.Rental = e.ExpireAt != 0;
            // option_count is always 0 from rAthena ("Client ignores the value anyway and recalculates it", clif.cpp:2976-2979): skipped
            e.BindType = r.U16(); e.Bound = e.BindType == 2; r.Skip(2); r.Skip(1); e.Options = ReadOptions(ref r); e.Refine = r.U8(); e.Grade = r.U8(); int flag = r.U8();
            e.Identified = (flag & 1) != 0; e.Damaged = (flag & 2) != 0; e.Favorite = (flag & 4) != 0; // EQUIPITEM_INFO Flag: IsIdentified, IsDamaged, PlaceETCTab (packets_struct.hpp:457-507)
            return e;
        }

        public static int[] ReadCards(ref PacketReader r) => new[] { (int)r.U32(), (int)r.U32(), (int)r.U32(), (int)r.U32() };

        /// <summary>option_data: 5 × ItemOptions { index.W value.W param.B } (25 bytes; packets_struct.hpp:450-454, MAX_ITEM_OPTIONS =
        /// MAX_ITEM_RDM_OPT = 5, mmo.hpp:113). index and value are signed int16 in the struct.</summary>
        public static RandomOption[] ReadOptions(ref PacketReader r)
        {
            var o = new RandomOption[5];
            for (int i = 0; i < 5; i++) { o[i].Id = r.S16(); o[i].Value = r.S16(); o[i].Param = r.U8(); }
            return o;
        }

        // ---- client wave 3 (w3equip): the last equip / take-off / take-off-all answers (the equipment window's status line and the smoke)
        public (int index, uint location, int result) LastWear { get; private set; } = (-1, 0, -1);
        public (int index, uint location, int flag) LastTakeOff { get; private set; } = (-1, 0, -1);
        /// <summary>Last ZC_ACK_TAKEOFF_EQUIP_ALL result (-1 none yet, 0 done, 1 refused).</summary>
        public int LastTakeOffAll { get; private set; } = -1;
        public int WearAcks { get; private set; }
        public int TakeOffAcks { get; private set; }
        public int TakeOffAllAcks { get; private set; }

        /// <summary>The item worn at <paramref name="slotBit"/> (one EQP_* bit), or null. An item that covers several positions (a
        /// top + middle headgear, a two-handed weapon) is found at each of them.</summary>
        public Entry WornAt(uint slotBit)
        {
            foreach (var e in Items.Values) if ((e.Wearing & slotBit) != 0) return e;
            return null;
        }

        /// <summary>CZ_REQ_TAKEOFF_EQUIP_ALL 0x0bad (no body; packets_struct.hpp:5172-5176 for PACKETVER_RE_NUM >= 20211103, parsed by
        /// clif_parse_unequipall, clif_packetdb.hpp:2004). rAthena then takes off every item of EQI_ACC_L … EQI_HAND_R, i.e. not the
        /// costumes, the ammunition or the shadow gear (max = EQI_COSTUME_HEAD_TOP, clif.cpp:22832-22840; pc.hpp:73-96).</summary>
        public static byte[] TakeOffAllPacket() => new PacketWriter(0x0bad).ToArray();
        /// <summary>The positions 0x0bad empties (EQI_ACC_L … EQI_HAND_R through equip_bitmask, pc.cpp:693-715).</summary>
        public const uint TakeOffAllMask = 0x80 | 0x08 | 0x40 | 0x04 | 0x01 | 0x200 | 0x100 | 0x10 | 0x20 | 0x02;

        /// <summary>Whether other players may open our equipment window (show_equip), null until the server has said.</summary>
        public bool? ShowEquip { get; private set; }
        /// <summary>CZ_CONFIG 0x02d8 type.L flag.L with type 0 (CONFIG_OPEN_EQUIPMENT_WINDOW): lets others see our equipment
        /// (CZ_EQUIPWIN_MICROSCOPE 0x02d6 checks tsd->status.show_equip or the viewer's view_equipment permission, clif.cpp:17704-17718).</summary>
        public static byte[] ShowEquipPacket(bool on) => new PacketWriter(0x02d8).U32(0).U32(on ? 1u : 0u).ToArray();

        // ---- client wave 4 (w4items): dual wield after a job change
        /// <summary>The jobs whose map id has MAPID_ASSASSIN or MAPID_KAGEROUOBORO under MAPID_SECONDMASK (map.hpp:100-262), read from
        /// pc_jobid2mapid (pc.cpp:7519-): Assassin 12, Assassin Cross 4013, Baby Assassin 4035, Guillotine Cross 4059, Guillotine Cross (trans)
        /// 4065, Baby Guillotine Cross 4101, Shadow Cross 4254, Kagerou 4211, Oboro 4212, Baby Kagerou 4223, Baby Oboro 4224, Shinkiro 4304,
        /// Shiranui 4305 (job ids from mmo.hpp enum e_job).</summary>
        public static readonly HashSet<int> DualWieldJobs = new HashSet<int> { 12, 4013, 4035, 4059, 4065, 4101, 4254, 4211, 4212, 4223, 4224, 4304, 4305 };
        /// <summary>AS_LEFT (db/re/skill_db.yml Id 133).</summary>
        public const int SkillLeftHand = 133;
        /// <summary>pc_equippoint_sub's test (pc.cpp:1471-1472): AS_LEFT learned (level > 0), or an Assassin / Kagerou / Oboro line job.</summary>
        public static bool CanDualWield(int job, int leftHandLevel) => leftHandLevel > 0 || DualWieldJobs.Contains(job);

        /// <summary>
        /// A job change (or AS_LEFT) changes which weapons may go in the left hand: rAthena reports a one-handed dagger / sword / axe
        /// (W_DAGGER 1, W_1HSWORD 2, W_1HAXE 6) whose item-db position is EQP_HAND_R as EQP_ARMS for a dual-wielder (pc_equippoint_sub,
        /// pc.cpp:1464-1482), but only in its next inventory list — a job change sends none (pc_jobchange, pc.cpp:10850-11010), so until
        /// the next map load the left-hand list would miss them. This applies the same rule to the list the client holds: EQP_HAND_R →
        /// EQP_ARMS when the character can dual-wield; the widening is undone when it no longer can. A weapon the server itself listed as
        /// EQP_ARMS is left alone: five one-handed-type weapons of rAthena's item db are Both_Hand by themselves (13466, 13467, 13490,
        /// 28137, 28144), and the protocol can't tell them from a dual-wield report, so going back from a dual-wielding job keeps such a
        /// report until the next list. The weapon's SubType is the client data's (itemviews.tsv).
        /// </summary>
        public int ApplyDualWield(bool can)
        {
            int n = 0;
            foreach (var e in Items.Values)
            {
                if (e.Type != 5 || e.Wearing != 0) continue;
                int sub = Sprites.AppearanceDb.WeaponSubType(e.ItemId);
                if (sub != 1 && sub != 2 && sub != 6) continue;
                if (can && e.Location == 0x02) { e.Location = 0x22; e.DualPatched = true; n++; }
                else if (!can && e.DualPatched && e.Location == 0x22) { e.Location = 0x02; e.DualPatched = false; n++; }
            }
            if (n > 0) { Debug.Log($"[Inventory] dual wield {(can ? "on" : "off")}: {n} weapon(s) now {(can ? "EQP_ARMS" : "EQP_HAND_R")}"); Changed?.Invoke(); }
            return n;
        }

        /// <summary>ZC_CHANGE_ITEM_OPTION (Laphine upgrade) re-sends an item's options in place.</summary>
        public void PatchOptions(int idx, RandomOption[] options)
        {
            if (options == null || !Items.TryGetValue(idx, out var e)) return;
            e.Options = options; Changed?.Invoke();
        }

        /// <summary>ZC_ACK_ITEMIDENTIFY result 0: the server does not re-send the item, so flip the flag here.</summary>
        public void MarkIdentified(int idx) { if (Items.TryGetValue(idx, out var e)) { e.Identified = true; Changed?.Invoke(); } }

        /// <summary>ZC_ACK_ITEMCOMPOSITION result 0: one card leaves the inventory silently and goes into the first empty slot.</summary>
        public void ApplyCardInsert(int equipIdx, int cardIdx)
        {
            if (!Items.TryGetValue(cardIdx, out var card)) return;
            int cardId = card.ItemId;
            if (Items.TryGetValue(equipIdx, out var eq))
            {
                int slots = eq.Info.Slots;
                for (int i = 0; i < 4 && i < Mathf.Max(slots, 1); i++) if (eq.Cards[i] == 0) { eq.Cards[i] = cardId; break; }
            }
            Remove(cardIdx, 1);
            Changed?.Invoke();
        }

        /// <summary>Display name with refine level and inserted cards: "+7 Knife [Poring Card]".</summary>
        public static string GradeName(int g) => g == 1 ? "D" : g == 2 ? "C" : g == 3 ? "B" : g == 4 ? "A" : "-";
        public static string FullName(Entry e)
        {
            var info = e.Info;
            var sb = new System.Text.StringBuilder();
            if (!e.Identified && e.IsEquip) sb.Append("<color=#c9a0ff>").Append(Lang.T("(unidentified)")).Append("</color> ");
            if (e.Damaged) sb.Append("<color=#ff7a6a>").Append(Lang.T("(damaged)")).Append("</color> ");
            if (e.Refine > 0) sb.Append('+').Append(e.Refine).Append(' ');
            if (e.Grade > 0 && e.Grade <= 4) sb.Append("<color=#ffb347>[").Append(GradeName(e.Grade)).Append("]</color> ");
            sb.Append(info.Display);
            if (e.IsEquip && e.Cards != null)
            {
                bool any = false;
                foreach (var c in e.Cards)
                {
                    if (c <= 0 || c == 0x00FF || c == 0x00FE || c == 0xFF00) continue; // special card0 markers (forged/pet eggs) are not cards
                    if (GameDb.GetItem(c).Type != 6) continue;
                    sb.Append(any ? ", " : " <color=#9fd0ff>[").Append(GameDb.GetItem(c).Name); any = true;
                }
                if (any) sb.Append("]</color>");
            }
            // client wave 3 (w3equip): the number of random options, as roBrowserLegacy appends " [N Option]" (DB/DBManager.js:2517-2522)
            int opts = 0; if (e.Options != null) foreach (var o in e.Options) if (o.Id > 0) opts++;
            if (opts > 0) sb.Append(" <color=#b8f0a0>").Append(opts > 1 ? Lang.F("[{0} options]", opts) : Lang.T("[1 option]")).Append("</color>");
            return sb.ToString();
        }

        /// <summary>The refine UI / enchant grade / enchant window change an item in place and never re-send it (research/refine_enchant_craft_packets.md §5).</summary>
        public void Patch(int idx, int? refine = null, int? grade = null, int slot = -1, int card = 0)
        {
            if (!Items.TryGetValue(idx, out var e)) return;
            if (refine.HasValue) e.Refine = Mathf.Clamp(refine.Value, 0, 20);
            if (grade.HasValue) e.Grade = Mathf.Clamp(grade.Value, 0, 4);
            if (slot >= 0 && slot < 4) e.Cards[slot] = card;
            Changed?.Invoke();
        }

        /// <summary>An item leaves the inventory without a server packet (put into a trade window: the server never sends the delete).</summary>
        public void TakeOut(int idx, int count) => Remove(idx, count);

        private void Remove(int idx, int count)
        {
            if (!Items.TryGetValue(idx, out var e)) return;
            e.Amount -= count;
            if (e.Amount <= 0) Items.Remove(idx);
            Changed?.Invoke();
        }

        /// <summary>Why an item can't be dropped (null = it can): the checks pc_dropitem / pc_candrop make before 0x00af
        /// (research/qol_packets.md §7) — rental, bound, Trade: NoDrop in item_db; worn gear must be taken off first.</summary>
        public static string DropBlocker(Entry e)
        {
            if (e.Wearing != 0) return Lang.T("Take it off first.");
            if (e.Rental) return Lang.T("Rental items can't be dropped.");
            if (e.Bound) return Lang.T("Bound items can't be dropped.");
            if (e.Info.NoDrop) return Lang.T("This item can't be dropped.");
            if (e.IsEquip && e.Cards != null) foreach (var c in e.Cards) if (c > 0 && GameDb.GetItem(c).Type == 6 && GameDb.GetItem(c).NoDrop) return Lang.T("A card in it can't be dropped.");
            return null;
        }

        /// <summary>Counts how many of an item the player carries (for quest objectives that ask for items).</summary>
        public int Count(int itemId) { int c = 0; foreach (var e in Items.Values) if (e.ItemId == itemId) c += e.Amount; return c; }

        private static string PickupFailText(int result)
        {
            // e_additem_result (pc.hpp): 1 invalid, 2 overweight, 3 item, 4 over item count, 5 over amount, 6 refused (clif_parse_TakeItem's generic failure), 7 stack limit
            switch (result)
            {
                case 2: return Lang.T("You're carrying too much to pick that up.");
                case 4: case 5: case 7: return Lang.T("Your inventory is full.");
                case 6: return Lang.T("Couldn't pick that up (too far away, or it belongs to someone else for now).");
                default: return Lang.T("Couldn't pick that up.");
            }
        }
    }
}
