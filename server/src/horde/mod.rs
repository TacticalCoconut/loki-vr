//! Horde on the module (port wave I, ihorde): the co-op side mode of client waves 8-9, which on an rAthena server runs
//! in Loki Host's relay (tools/lokihost/Core/HordeService.cs around the client's own C# engine). Loki's own game, outside
//! the RO protocol: nothing here is ported from rAthena. What IS ported, rule for rule, is the C# engine
//! (client/Assets/Loki/Runtime/Horde/Core): [`sim`] + [`weapons`] (HordeSim), [`math`] (HordeMath and the engine's
//! floating point), [`content`] (the tables, read from the file tools/hordegolden writes from the C# tables),
//! [`wire`] (HordeWire, SnapshotEncoder); `golden` is the proof (state hashes against the C# engine's, step for step).
//! Around the engine: [`service`] (what HordeService.cs does in the relay: one reducer, fan-out rows, a scheduled
//! call while a run exists, the owner's settings) and [`reward`] (the real character's EXP through pc_gainexp).

pub mod content;
pub mod math;
pub mod reward;
pub mod service;
pub mod sim;
pub mod weapons;
pub mod wire;

#[cfg(test)]
mod golden;

pub use reward::on_enter_map;
pub use service::on_client_gone;
