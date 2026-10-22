//! cashshop.cpp CashShopDatabase: db/item_cash.yml (no entries of its own) then db/import/item_cash.yml, which is
//! Loki's own shop (data/overlay/db/import/item_cash.yml). The sales table (SQL `sales`) is not part of it.

/// cashshop.hpp e_cash_shop_tab.
pub const CASHSHOP_TAB_NEW: u16 = 0;
pub const CASHSHOP_TAB_HOT: u16 = 1;
pub const CASHSHOP_TAB_LIMITED: u16 = 2;
pub const CASHSHOP_TAB_RENTAL: u16 = 3;
pub const CASHSHOP_TAB_PERMANENT: u16 = 4;
pub const CASHSHOP_TAB_SCROLLS: u16 = 5;
pub const CASHSHOP_TAB_CONSUMABLES: u16 = 6;
pub const CASHSHOP_TAB_OTHER: u16 = 7;
pub const CASHSHOP_TAB_SALE: u16 = 8;
pub const CASHSHOP_TAB_MAX: u16 = 9;

/// cashshop.hpp struct s_cash_item.
#[derive(serde::Deserialize, Debug, Clone)]
pub struct CashItem {
    /// `Item`.
    pub nameid: u32,
    /// `Price` (cash points, 1..MAX_CASHPOINT).
    pub price: u32,
}

/// cashshop.hpp struct s_cash_item_tab.
#[derive(serde::Deserialize, Debug, Clone)]
pub struct CashItemTab {
    /// `Tab` (CASHSHOP_TAB_*).
    pub tab: u16,
    /// `Items`, in file order (a repeated item updates its price in place).
    pub items: Vec<CashItem>,
}

crate::dataset!(cash_shop_db, "items_cash", Vec<CashItemTab>);

/// cash_shop_db.find(tab).
pub fn cash_shop_tab(tab: u16) -> Option<&'static CashItemTab> {
    cash_shop_db().iter().find(|t| t.tab == tab)
}

/// CashShopDatabase::findItemInTab.
pub fn find_item_in_tab(tab: u16, nameid: u32) -> Option<&'static CashItem> {
    cash_shop_tab(tab)?.items.iter().find(|i| i.nameid == nameid)
}
