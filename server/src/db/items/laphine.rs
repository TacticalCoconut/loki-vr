//! itemdb.cpp LaphineSynthesisDatabase (db/re/laphine_synthesis.yml + import) and LaphineUpgradeDatabase
//! (db/re/laphine_upgrade.yml + import), keyed by the item that opens the window.

use std::collections::HashMap;
use std::sync::OnceLock;

/// itemdb.hpp struct s_laphine_synthesis_requirement.
#[derive(serde::Deserialize, Debug, Clone)]
pub struct LaphineSynthesisRequirement {
    /// `Item`.
    pub item_id: u32,
    /// `Amount` (default 1, capped to MAX_AMOUNT).
    pub amount: u16,
}

/// itemdb.hpp struct s_laphine_synthesis.
#[derive(serde::Deserialize, Debug, Clone)]
pub struct LaphineSynthesis {
    /// `Item`.
    pub item_id: u32,
    /// `MinimumRefine` (default 0) / `MaximumRefine` (default MAX_REFINE).
    pub minimum_refine: u16,
    pub maximum_refine: u16,
    /// `RequiredRequirementsCount` (requiredRequirements, default 1).
    pub required_requirements: u16,
    /// `Requirements`, in the unordered_map's iteration order.
    pub requirements: Vec<LaphineSynthesisRequirement>,
    /// `RewardGroup` (an IG_* item group id).
    pub reward_group_id: u16,
}

/// itemdb.hpp struct s_laphine_upgrade.
#[derive(serde::Deserialize, Debug, Clone)]
pub struct LaphineUpgrade {
    /// `Item`.
    pub item_id: u32,
    /// `TargetItems` (in file order, no duplicates).
    pub target_item_ids: Vec<u32>,
    /// `MinimumRefine` (default 0) / `MaximumRefine` (default MAX_REFINE).
    pub minimum_refine: u16,
    pub maximum_refine: u16,
    /// `RequiredRandomOptions` (default 0, capped to MAX_ITEM_RDM_OPT).
    pub required_random_options: u16,
    /// `CardsAllowed` (default false).
    pub cards_allowed: bool,
    /// `RandomOptionGroup` (id, default none).
    pub random_option_group: Option<u16>,
    /// `ResultRefine`, `ResultRefineMinimum`, `ResultRefineMaximum` (default 0).
    pub result_refine: u16,
    pub result_refine_minimum: u16,
    pub result_refine_maximum: u16,
}

crate::dataset!(laphine_synthesis_db, "items_laphine_synthesis", Vec<LaphineSynthesis>);
crate::dataset!(laphine_upgrade_db, "items_laphine_upgrade", Vec<LaphineUpgrade>);

static SYNTH: OnceLock<HashMap<u32, usize>> = OnceLock::new();
static UPGRADE: OnceLock<HashMap<u32, usize>> = OnceLock::new();

/// laphine_synthesis_db.find.
pub fn laphine_synthesis(item_id: u32) -> Option<&'static LaphineSynthesis> {
    let ix = SYNTH.get_or_init(|| laphine_synthesis_db().iter().enumerate().map(|(i, e)| (e.item_id, i)).collect());
    ix.get(&item_id).map(|&i| &laphine_synthesis_db()[i])
}

/// laphine_upgrade_db.find.
pub fn laphine_upgrade(item_id: u32) -> Option<&'static LaphineUpgrade> {
    let ix = UPGRADE.get_or_init(|| laphine_upgrade_db().iter().enumerate().map(|(i, e)| (e.item_id, i)).collect());
    ix.get(&item_id).map(|&i| &laphine_upgrade_db()[i])
}

impl LaphineSynthesis {
    /// requirements.find(item_id).
    pub fn requirement(&self, item_id: u32) -> Option<&LaphineSynthesisRequirement> {
        self.requirements.iter().find(|r| r.item_id == item_id)
    }
}
