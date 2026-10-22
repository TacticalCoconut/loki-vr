//! itemdb.cpp ComboDatabase (db/re/item_combos.yml + db/import/item_combos.yml). Each `Combos` list of an entry
//! becomes one combo (items sorted by id, like the loader's std::sort); a combo that already exists (same item set)
//! is updated, `Clear: true` removes it, and ids are handed out in load order (combo_num). loadingFinished attaches
//! every combo to its items: [`super::ItemData::combos`].

use std::collections::HashMap;
use std::sync::OnceLock;

/// itemdb.hpp struct s_item_combo.
#[derive(serde::Deserialize, Debug, Clone)]
pub struct ItemCombo {
    /// combo_num at creation (1-based).
    pub id: u16,
    /// `Combo` (AegisNames resolved and sorted).
    pub nameid: Vec<u32>,
    /// The entry's `Script` (source text; None when absent).
    #[serde(default)]
    pub script: Option<String>,
}

crate::dataset!(item_combos, "items_combos", Vec<ItemCombo>);

static INDEX: OnceLock<HashMap<u16, usize>> = OnceLock::new();

/// ComboDatabase::find. The list itself ([`item_combos`]) is in the database's iteration order.
pub fn combo(id: u16) -> Option<&'static ItemCombo> {
    let ix = INDEX.get_or_init(|| item_combos().iter().enumerate().map(|(i, c)| (c.id, i)).collect());
    ix.get(&id).map(|&i| &item_combos()[i])
}

/// item_data::combos of an item: its combos, in the order pc_load_combo sees them.
pub fn itemdb_combos(nameid: u32) -> impl Iterator<Item = &'static ItemCombo> {
    super::itemdb_exists(nameid).into_iter().flat_map(|it| it.combos.iter().filter_map(|&c| combo(c)))
}
