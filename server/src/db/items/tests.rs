//! Native tests: the loaded datasets against the counts rAthena prints when it loads the same files (the oracle's
//! map.log, "Done reading 'N' entries in '...'") and field values checked by hand against the YAML and the loader
//! rules.

use super::*;

#[test]
fn counts_match_the_oracle() {
    // db/re/item_db_usable.yml 6415 + item_db_equip.yml 12982 + item_db_etc.yml 9959 (+ the dummy item 499)
    assert_eq!(item_db().len(), 6415 + 12982 + 9959 + 1);
    // item_combos.yml: 8618 combos (5114 entries)
    assert_eq!(combo::item_combos().len(), 8618);
    let ro = randomopt::random_option_file();
    assert_eq!(ro.options.len(), 249);
    assert_eq!(ro.groups.len(), 108);
    assert_eq!(ro.constants.len(), 249);
    assert_eq!(group::item_groups().len(), 2996);
    assert_eq!(laphine::laphine_synthesis_db().len(), 179);
    assert_eq!(laphine::laphine_upgrade_db().len(), 104);
    assert_eq!(reform::item_reform_db().len(), 131);
    assert_eq!(enchant::item_enchant_db().len(), 138);
    assert_eq!(package::item_package_db().len(), 79);
    // db/import/item_cash.yml (Loki's shop): 7 tabs
    assert_eq!(cash::cash_shop_db().len(), 7);
    assert_eq!(refine::refine_db().len(), 4);
    assert_eq!(enchantgrade::enchantgrade_db().len(), 2);
    // stylist.yml: 71 options over 7 looks
    assert_eq!(stylist::stylist_db().iter().map(|l| l.entries.len()).sum::<usize>(), 71);
    // npc/re/merchants/barters/*.yml: 14+9+1+5+5+1+7+18+19+19+5+3+1+3+3+1
    assert_eq!(barter::barter_db().len(), 114);
    assert!(barter::barter_db().iter().all(|b| b.npc && b.map.is_none() && b.sprite == -1));
}

#[test]
fn knife() {
    let it = itemdb_exists(1201).unwrap();
    assert_eq!((it.name.as_str(), it.ename.as_str()), ("Knife", "Knife"));
    assert_eq!(it.r#type, IT_WEAPON);
    assert_eq!(it.subtype, 1); // W_DAGGER
    assert_eq!((it.value_buy, it.value_sell), (50, 25)); // Sell = Buy / 2
    assert_eq!((it.weight, it.atk, it.matk, it.def, it.range, it.slots), (400, 17, 0, 0, 1, 3));
    assert_eq!(it.equip, 2); // EQP_HAND_R
    assert_eq!((it.weapon_level, it.armor_level, it.elv, it.elvmax), (1, 0, 1, MAX_LEVEL));
    assert_eq!(it.sex, SEX_BOTH);
    // base: Novice Swordman Mage Archer Merchant Thief Ninja; 2-1: SuperNovice Knight Wizard Hunter Blacksmith
    // Assassin KagerouOboro; 2-2: Crusader Sage BardDancer Alchemist Rogue SoulLinker
    assert_eq!(it.class_base, [623, 623, 238]);
    assert_eq!(it.class_upper, ITEMJ_ALL);
    assert!(!it.flag.no_refine && it.flag.available && !it.flag.gradable);
    assert_eq!(it.gm_lv_trade_override, 100);
    assert_eq!((it.item_usage.r#override, it.delay.sc), (100, SC_NONE));
    assert!(it.scripts().is_none());
    assert!(itemdb_isequip(1201) && !itemdb_isstackable(1201) && !itemdb_isidentified(1201));
    assert_eq!(search_aegisname("KNIFE").unwrap().nameid, 1201);
    // three items are named "Knife": the Name lookup keeps the last one loaded
    assert_eq!(itemdb_searchname("knife").unwrap().nameid, 1201); // AegisName first
    assert_eq!(index().by_name.get("knife").map(|&i| item_db()[i].nameid), Some(1203));
}

#[test]
fn red_potion() {
    let it = itemdb_exists(501).unwrap();
    assert_eq!((it.name.as_str(), it.ename.as_str()), ("Red_Potion", "Red Potion"));
    assert_eq!(it.r#type, IT_HEALING);
    assert_eq!((it.value_buy, it.value_sell, it.weight), (10, 5, 70));
    assert_eq!(it.class_base, [MAPID_ALL; 3]);
    assert!(it.flag.no_refine);
    assert_eq!(it.scripts().unwrap().script.as_deref(), Some("itemheal rand(45,65),0;\n"));
    assert!(it.scripts().unwrap().equip_script.is_none());
    assert!(itemdb_isstackable(501) && itemdb_isidentified(501) && !itemdb_isequip(501));
    assert_eq!(itemdb_searchname("RED POTION").unwrap().nameid, 501);
    assert_eq!(it.inventory_slot_needed(5), 1);
}

#[test]
fn cotton_shirt_and_jobs_masks() {
    let it = itemdb_exists(2301).unwrap();
    assert_eq!((it.r#type, it.equip, it.def, it.armor_level, it.weapon_level), (IT_ARMOR, 16, 10, 1, 0));
    assert_eq!((it.value_buy, it.value_sell, it.weight), (10, 5, 100));
    assert!(!it.flag.no_refine);
    // Diabolus Armor: Jobs Swordman Merchant Thief (base), Knight Blacksmith Assassin (2-1), Crusader Alchemist Rogue
    // (2-2); Classes Upper | All_Third | Fourth
    let d = itemdb_exists(2375).unwrap();
    assert_eq!(d.class_base, [98, 98, 98]);
    assert_eq!(d.class_upper, ITEMJ_UPPER | ITEMJ_THIRD | ITEMJ_THIRD_UPPER | ITEMJ_THIRD_BABY | ITEMJ_FOURTH);
    assert_eq!((d.def, d.slots, d.elv), (79, 1, 60));
    assert_eq!(d.inventory_slot_needed(3), 3);
}

#[test]
fn loader_rules() {
    // defaultGender: wedding rings, instruments, whips
    assert_eq!(itemdb_exists(2634).unwrap().sex, SEX_MALE);
    assert_eq!(itemdb_exists(2635).unwrap().sex, SEX_FEMALE);
    assert_eq!(itemdb_exists(1901).unwrap().sex, SEX_MALE); // Violin
    assert_eq!(itemdb_exists(1950).unwrap().sex, SEX_FEMALE); // Rope
    // only Sell given: Buy = Sell * 2
    let w = itemdb_exists(16794).unwrap();
    assert_eq!((w.value_buy, w.value_sell), (20, 10));
    assert!(w.flag.group && w.flag.trade_restriction.drop && !w.flag.trade_restriction.storage);
    // Type DelayConsume -> IT_USABLE + DELAYCONSUME_TEMP; item_noequip.txt 601,32 / 601,64 / 601,128
    let fw = itemdb_exists(601).unwrap();
    assert_eq!((fw.r#type, fw.flag.delay_consume, fw.flag.no_equip), (IT_USABLE, DELAYCONSUME_TEMP, 224));
    // 2483,1 and 2483,16
    let sg = itemdb_exists(2483).unwrap();
    assert_eq!(sg.flag.no_equip, 17);
    assert!(itemdb_is_no_equip(sg, &NoEquipMap::default()));
    assert!(!itemdb_is_no_equip(sg, &NoEquipMap { vs: true, pvp: true, ..Default::default() }));
    assert!(itemdb_is_no_equip(sg, &NoEquipMap { vs: true, gvg_te: true, ..Default::default() }));
    // a card: stackable, BuyingStore, DropEffect CLIENT
    let pc = itemdb_exists(4001).unwrap();
    assert_eq!((pc.r#type, pc.equip, pc.flag.drop_effect), (IT_CARD, 16, 1));
    assert!(pc.flag.buyingstore);
    assert_eq!(pc.scripts().unwrap().script.as_deref(), Some("bonus bLuk,2;\nbonus bFlee2,1;\n"));
    // Guard: a shield keeps its View
    assert_eq!(itemdb_exists(2101).unwrap().look, 1);
}

#[test]
fn dummy_item() {
    let d = itemdb_search(4_000_000);
    assert_eq!((d.nameid, d.name.as_str(), d.ename.as_str()), (ITEMID_DUMMY, "UNKNOWN_ITEM", "Unknown Item"));
    assert_eq!((d.weight, d.value_sell, d.value_buy, d.view_id, d.r#type), (1, 1, 0, UNKNOWN_ITEM_ID, IT_ETC));
    assert!(!d.flag.available);
    assert_eq!(d.class_base, [0; 3]);
    // put() directly: not reachable by name
    assert!(search_aegisname("UNKNOWN_ITEM").is_none());
    assert_eq!(client_nameid(4_000_000), UNKNOWN_ITEM_ID);
    assert_eq!(client_nameid(501), 501);
}

#[test]
fn trade_restrictions() {
    let ring = 2634; // Bridegroom ring: NoDrop NoTrade NoSell NoCart NoStorage NoGuildStorage NoMail NoAuction
    assert!(!itemdb_isrestricted(ring, &[0; 4], 0, 0, itemdb_isdropable_sub));
    assert!(itemdb_isrestricted(ring, &[0; 4], 100, 0, itemdb_isdropable_sub));
    assert!(itemdb_isrestricted(501, &[0; 4], 0, 0, itemdb_cantrade_sub));
    assert!(!itemdb_isrestricted(501, &[0; 4], 0, 0, itemdb_canpartnertrade_sub));
}

#[test]
fn combos() {
    // item_combos.yml's first entry: Dragon_Slayer + Dragon_Breath
    let c = combo::combo(1).unwrap();
    assert_eq!(c.nameid, vec![1166, 2527]);
    assert_eq!(c.script.as_deref(), Some("bonus2 bAddRace,RC_Dragon,5;\n"));
    // Dragon Breath is in the first twelve combos; unordered_map<uint16> with 8618 sequential keys iterates them
    // backwards within its bucket chain (checked against libstdc++)
    assert_eq!(itemdb_exists(2527).unwrap().combos, (1..=12).collect::<Vec<u16>>());
    assert_eq!(combo::item_combos()[0].id, 8618);
    assert!(combo::itemdb_combos(4001).all(|c| c.nameid.contains(&4001)));
    assert_eq!(combo::itemdb_combos(4001).count(), 2);
}

#[test]
fn groups_and_options() {
    let g = group::item_group(1).unwrap(); // IG_BLUEBOX
    let r = g.subgroup(6).unwrap();
    assert_eq!((r.algorithm, r.total_rate, r.data.len()), (group::GROUP_ALGORITHM_RANDOM, 8429, 1032));
    assert_eq!(r.total_rate, r.data.iter().map(|e| e.rate as u32).sum::<u32>());
    assert!(r.data.iter().all(|e| e.adj_rate == e.rate.clamp(1, 10000)));
    assert!(group::itemdb_group_item_exists(1, r.data[0].nameid));
    let mut n = 0;
    let mut rnd = |a: i32, b: i32| {
        n += 1;
        a + (n * 7919) % (b - a + 1)
    };
    let mut pool = group::SharedPool::default();
    assert!(group::get_random_itemsubgroup(r, group::GROUP_ALGORITHM_USEDB, &mut rnd, &mut pool).is_some());

    assert_eq!(randomopt::random_option(1).unwrap().name, "VAR_MAXHPAMOUNT");
    assert_eq!(randomopt::random_option_get_id("VAR_MAXHPAMOUNT"), Some(1));
    let rg = randomopt::random_option_group(1).unwrap();
    assert_eq!((rg.name.as_str(), rg.max_random, rg.slots.len()), ("Group_1", 0, 1));
    assert_eq!(rg.slots[0].1.len(), 6);
    let opts = rg.apply(&mut rnd);
    assert!(opts[0].id >= 3 && opts[1..].iter().all(|o| o.id == 0));
    assert!(randomopt::random_option_file().constants.iter().any(|(n, v)| n == "RDMOPT_VAR_MAXHPAMOUNT" && *v == 1));
}

#[test]
fn shops_and_windows() {
    // Loki's cash shop: New tab = Battle_Manual 300, Bubble_Gum 400
    let new = cash::cash_shop_tab(cash::CASHSHOP_TAB_NEW).unwrap();
    assert_eq!(new.items.iter().map(|i| (i.nameid, i.price)).collect::<Vec<_>>(), vec![(12208, 300), (12210, 400)]);
    assert_eq!(cash::find_item_in_tab(cash::CASHSHOP_TAB_OTHER, 12214).unwrap().price, 150);
    // refine.yml Weapon level 1, +0 -> +1: Normal = Phracon 50z 100%
    let knife = itemdb_exists(1201).unwrap();
    let li = refine::find_level_info(knife, 0).unwrap();
    assert_eq!((li.level, li.bonus), (0, 200));
    let c = li.cost(refine::REFINE_COST_NORMAL).unwrap();
    assert_eq!((c.nameid, c.zeny, c.chance), (1010, 50, 10000));
    assert!(refine::find_current_level_info(knife, 0).is_none());
    assert!(refine::find_level_info(itemdb_exists(501).unwrap(), 0).is_none());
    // enchantgrade.yml Armor level 2 grade None: +9 1000, +11 7000
    let eg = enchantgrade::enchantgrade(IT_ARMOR as u16).unwrap();
    let (_, grades) = eg.levels.iter().find(|(l, _)| *l == 2).unwrap();
    let none = &grades[0];
    assert_eq!((none.grade, none.chances[9], none.chances[11], none.bonus), (0, 1000, 7000, 10));
    assert_eq!(none.catalyst.amount_per_step, 1);
    // stylist.yml Hair_Color index 1: value 1, 100000z for both
    let e = stylist::stylist_entry(stylist::LOOK_HAIR_COLOR, 1).unwrap();
    assert_eq!((e.value, e.human.as_ref().unwrap().price, e.doram.as_ref().unwrap().price), (1, 100000, 100000));
    // barters/Pet_Groomer.yml
    let b = barter::barter("barter_Pet_Groomer").unwrap();
    let i0 = b.item(0).unwrap();
    assert_eq!(i0.nameid, 626);
    assert_eq!(i0.requirements.iter().map(|r| (r.nameid, r.amount, r.refine)).collect::<Vec<_>>(), vec![(512, 2, -1), (702, 1, -1)]);
    assert!(b.extended); // an item needs two different items: only the extended barter window can show it
    assert_eq!(barter::barter_db().iter().filter(|b| b.extended).count(), 22);
    // laphine_synthesis.yml Shadow_Mix_Recipe
    let recipe = search_aegisname("Shadow_Mix_Recipe").unwrap().nameid;
    let s = laphine::laphine_synthesis(recipe).unwrap();
    assert_eq!((s.reward_group_id, s.required_requirements, s.minimum_refine, s.maximum_refine), (532, 3, 0, 20));
    assert!(s.requirement(search_aegisname("S_Physical_Earring").unwrap().nameid).is_some_and(|r| r.amount == 1));
    // item_enchant.yml Id 1
    let en = enchant::item_enchant(1).unwrap();
    assert_eq!(en.order, vec![3, 2, 1]);
    assert_eq!((en.minimum_refine, en.reset.chance, en.reset.zeny), (7, 100000, 100000));
    assert_eq!(en.slot(3).unwrap().normal.zeny, 100000);
    assert_eq!(en.target_item_ids.len(), 2);
}
