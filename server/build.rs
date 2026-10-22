//! Build script: a fingerprint of every data file the module embeds (data/*.bin and data/json/*, written by
//! tools/datagen.py), exported as LOKI_DATA_FINGERPRINT for data.rs data_version(). A published module rebuilds the
//! world whenever any byte of that data changed (npc.rs ensure_world), not only when a sampled byte did.
//! With the feature `install_maps` the module embeds data/install/maps.bin instead of data/maps.bin (src/data.rs): the
//! fingerprint then covers data/install/ too, so the two builds never share one (publishing one over a database the
//! other built rebuilds the world: its NPC cells and spawn points were made on other maps). Without the feature the
//! fingerprint is what it always was: data/install/ is not read.

use std::fs;
use std::path::Path;

fn fnv(h: &mut u64, bytes: &[u8]) {
    for &b in bytes {
        *h ^= b as u64;
        *h = h.wrapping_mul(0x100000001b3);
    }
}

fn main() {
    let root = Path::new(env!("CARGO_MANIFEST_DIR")).join("data");
    let mut files = Vec::new();
    for dir in [root.clone(), root.join("json")] {
        println!("cargo:rerun-if-changed={}", dir.display());
        let Ok(rd) = fs::read_dir(&dir) else { continue };
        for e in rd.flatten() {
            let p = e.path();
            if p.is_file() {
                files.push(p);
            }
        }
    }
    files.sort();
    let mut h: u64 = 0xcbf29ce484222325;
    for p in &files {
        println!("cargo:rerun-if-changed={}", p.display());
        let name = p.strip_prefix(&root).unwrap_or(p).to_string_lossy().replace('\\', "/");
        fnv(&mut h, name.as_bytes());
        let data = fs::read(p).unwrap_or_default();
        fnv(&mut h, &(data.len() as u64).to_le_bytes());
        fnv(&mut h, &data);
    }
    if std::env::var_os("CARGO_FEATURE_INSTALL_MAPS").is_some() {
        fnv(&mut h, b"install_maps");
        for name in ["maps.bin", "mapstamp.txt"] {
            let p = root.join("install").join(name);
            println!("cargo:rerun-if-changed={}", p.display());
            // a build without the file fails in data.rs's include with the path; the fingerprint only has to differ
            let data = fs::read(&p).unwrap_or_default();
            fnv(&mut h, name.as_bytes());
            fnv(&mut h, &(data.len() as u64).to_le_bytes());
            fnv(&mut h, &data);
        }
    }
    println!("cargo:rustc-env=LOKI_DATA_FINGERPRINT={h}");
}
