//! map/path.cpp: walk paths (A* with rAthena's — and the client's — binary heap), shoot paths, distances.

use crate::map::{self, CellChk};

pub const MOVE_COST: i32 = 10;
pub const MOVE_DIAGONAL_COST: i32 = 14;
pub const MAX_WALKPATH: usize = 32;

/// enum directions (path.hpp): 0 north, 1 north-west … 7 north-east; -1 = DIR_CENTER.
pub const DIR_CENTER: i8 = -1;
pub const DIR_NORTH: u8 = 0;
pub const DIR_NORTHWEST: u8 = 1;
pub const DIR_WEST: u8 = 2;
pub const DIR_SOUTHWEST: u8 = 3;
pub const DIR_SOUTH: u8 = 4;
pub const DIR_SOUTHEAST: u8 = 5;
pub const DIR_EAST: u8 = 6;
pub const DIR_NORTHEAST: u8 = 7;
pub const DIR_MAX: u8 = 8;

/// unit.cpp dirx / diry.
pub const DIRX: [i16; 8] = [0, -1, -1, -1, 0, 1, 1, 1];
pub const DIRY: [i16; 8] = [1, 1, 0, -1, -1, -1, 0, 1];

/// path.cpp walk_choices[-dy + 1][dx + 1].
const WALK_CHOICES: [[i8; 3]; 3] = [
    [DIR_NORTHWEST as i8, DIR_NORTH as i8, DIR_NORTHEAST as i8],
    [DIR_WEST as i8, DIR_CENTER, DIR_EAST as i8],
    [DIR_SOUTHWEST as i8, DIR_SOUTH as i8, DIR_SOUTHEAST as i8],
];

fn walk_choice(dx: i32, dy: i32) -> u8 {
    WALK_CHOICES[(-dy + 1) as usize][(dx + 1) as usize] as u8
}

pub fn direction_diagonal(d: u8) -> bool {
    d == DIR_NORTHWEST || d == DIR_SOUTHWEST || d == DIR_SOUTHEAST || d == DIR_NORTHEAST
}

/// struct walkpath_data: the directions to step, in order.
#[derive(Clone, Debug, Default)]
pub struct WalkPath {
    pub path: Vec<u8>,
}

fn heuristic(x0: i32, y0: i32, x1: i32, y1: i32) -> i32 {
    MOVE_COST * ((x1 - x0).abs() + (y1 - y0).abs())
}

fn calc_index(x: i32, y: i32) -> usize {
    ((x + y * MAX_WALKPATH as i32) & (MAX_WALKPATH as i32 * MAX_WALKPATH as i32 - 1)) as usize
}

#[derive(Clone, Copy, Default)]
struct Node {
    parent: Option<usize>,
    x: i16,
    y: i16,
    g_cost: i16,
    f_cost: i16,
    open: bool,
    used: bool,
}

/// db.hpp BHEAP_* with NODE_MINTOPCMP, exactly as path.cpp uses them (PUSH2 / POP2 / UPDATE): the order of equal-cost
/// nodes decides which of several equally short paths is taken, and must match the client's.
struct Heap {
    v: Vec<usize>,
}

impl Heap {
    fn cmp(nodes: &[Node], a: usize, b: usize) -> i32 {
        nodes[a].f_cost as i32 - nodes[b].f_cost as i32
    }
    fn siftdown(&mut self, nodes: &[Node], start: usize, idx: usize) {
        let mut i = idx;
        while i > start {
            let parent = (i - 1) / 2;
            if Self::cmp(nodes, self.v[parent], self.v[i]) <= 0 {
                break;
            }
            self.v.swap(parent, i);
            i = parent;
        }
    }
    fn siftup(&mut self, nodes: &[Node], idx: usize) {
        let mut i = idx;
        let mut l = i * 2 + 1;
        while l < self.v.len() {
            let r = i * 2 + 2;
            if r >= self.v.len() || Self::cmp(nodes, self.v[l], self.v[r]) < 0 {
                self.v.swap(i, l);
                i = l;
            } else {
                self.v.swap(i, r);
                i = r;
            }
            l = i * 2 + 1;
        }
        self.siftdown(nodes, idx, i);
    }
    fn push2(&mut self, nodes: &[Node], n: usize) {
        let i = self.v.len();
        self.v.push(n);
        self.siftdown(nodes, 0, i);
    }
    fn pop2(&mut self, nodes: &[Node]) {
        let last = self.v.pop().unwrap();
        if self.v.is_empty() {
            return;
        }
        self.v[0] = last;
        self.siftup(nodes, 0);
    }
    fn update(&mut self, nodes: &[Node], n: usize) -> bool {
        match self.v.iter().position(|&e| e == n) {
            Some(i) => {
                self.siftdown(nodes, 0, i);
                self.siftup(nodes, i);
                true
            }
            None => {
                log::error!("heap_update_node: node not found");
                false
            }
        }
    }
}

/// path.cpp add_path: returns true on error (index collision).
#[allow(clippy::too_many_arguments)]
fn add_path(heap: &mut Heap, tp: &mut [Node], x: i32, y: i32, g_cost: i32, parent: usize, h_cost: i32) -> bool {
    let i = calc_index(x, y);
    if tp[i].used && tp[i].x as i32 == x && tp[i].y as i32 == y {
        if g_cost < tp[i].g_cost as i32 {
            tp[i].g_cost = g_cost as i16;
            tp[i].parent = Some(parent);
            tp[i].f_cost = (g_cost + h_cost) as i16;
            if !tp[i].open {
                heap.push2(tp, i);
            } else if !heap.update(tp, i) {
                return true;
            }
            tp[i].open = true;
        }
        return false;
    }
    // path.cpp checks `tp[i].x || tp[i].y`: an index already taken by another node is an error. The (0,0) node can never
    // be taken by accident there because it is never pushed with the zeroed slot's coordinates; `used` expresses the
    // same test without the coordinate aliasing.
    if tp[i].used {
        return true;
    }
    tp[i] = Node { parent: Some(parent), x: x as i16, y: y as i16, g_cost: g_cost as i16, f_cost: (g_cost + h_cost) as i16, open: true, used: true };
    heap.push2(tp, i);
    false
}

/// path.cpp path_search: `flag & 1` = easy path only (diagonal first, then straight), `flag & 2` = path_search_long.
pub fn path_search(m: u16, x0: i16, y0: i16, x1: i16, y1: i16, flag: i32, cell: CellChk) -> Option<WalkPath> {
    if flag & 2 != 0 {
        return if path_search_long(m, x0, y0, x1, y1, cell) { Some(WalkPath::default()) } else { None };
    }
    let md = crate::data::maps().get(m)?;
    let (x0, y0, x1, y1) = (x0 as i32, y0 as i32, x1 as i32, y1 as i32);
    // Do not check the starting cell (that would get you stuck).
    if x0 < 0 || x0 >= md.xs as i32 || y0 < 0 || y0 >= md.ys as i32 {
        return None;
    }
    if x1 < 0 || x1 >= md.xs as i32 || y1 < 0 || y1 >= md.ys as i32 || map::getcellp(md, x1 as i16, y1 as i16, cell) {
        return None;
    }
    if flag & 1 != 0 {
        let mut dx = (x1 - x0).signum();
        let mut dy = (y1 - y0).signum();
        let (mut x, mut y) = (x0, y0);
        let mut path = Vec::new();
        while path.len() < MAX_WALKPATH {
            path.push(walk_choice(dx, dy));
            x += dx;
            y += dy;
            if x == x1 {
                dx = 0;
            }
            if y == y1 {
                dy = 0;
            }
            if dx == 0 && dy == 0 {
                break;
            }
            if map::getcellp(md, x as i16, y as i16, cell) {
                break;
            }
        }
        if x == x1 && y == y1 {
            return Some(WalkPath { path });
        }
        return None;
    }
    // A* (the client always uses it; easy paths cut corners the client walks around).
    let mut tp = vec![Node::default(); MAX_WALKPATH * MAX_WALKPATH];
    let mut heap = Heap { v: Vec::with_capacity(256) };
    let xs = md.xs as i32 - 1;
    let ys = md.ys as i32 - 1;
    let i = calc_index(x0, y0);
    tp[i] = Node { parent: None, x: x0 as i16, y: y0 as i16, g_cost: 0, f_cost: heuristic(x0, y0, x1, y1) as i16, open: true, used: true };
    heap.push2(&tp, i);
    let current = loop {
        if heap.v.is_empty() {
            return None;
        }
        let cur = heap.v[0];
        heap.pop2(&tp);
        let (x, y, g) = (tp[cur].x as i32, tp[cur].y as i32, tp[cur].g_cost as i32);
        tp[cur].open = false;
        if x == x1 && y == y1 {
            break cur;
        }
        const N: i32 = 1;
        const W: i32 = 2;
        const S: i32 = 4;
        const E: i32 = 8;
        let mut allowed = 0;
        let c = |xx: i32, yy: i32| map::getcellp(md, xx as i16, yy as i16, cell);
        if y < ys && !c(x, y + 1) {
            allowed |= N;
        }
        if y > 0 && !c(x, y - 1) {
            allowed |= S;
        }
        if x < xs && !c(x + 1, y) {
            allowed |= E;
        }
        if x > 0 && !c(x - 1, y) {
            allowed |= W;
        }
        let chk = |d: i32| allowed & d == d;
        let mut e = false;
        if chk(S | E) && !c(x + 1, y - 1) {
            e |= add_path(&mut heap, &mut tp, x + 1, y - 1, g + MOVE_DIAGONAL_COST, cur, heuristic(x + 1, y - 1, x1, y1));
        }
        if chk(E) {
            e |= add_path(&mut heap, &mut tp, x + 1, y, g + MOVE_COST, cur, heuristic(x + 1, y, x1, y1));
        }
        if chk(N | E) && !c(x + 1, y + 1) {
            e |= add_path(&mut heap, &mut tp, x + 1, y + 1, g + MOVE_DIAGONAL_COST, cur, heuristic(x + 1, y + 1, x1, y1));
        }
        if chk(N) {
            e |= add_path(&mut heap, &mut tp, x, y + 1, g + MOVE_COST, cur, heuristic(x, y + 1, x1, y1));
        }
        if chk(N | W) && !c(x - 1, y + 1) {
            e |= add_path(&mut heap, &mut tp, x - 1, y + 1, g + MOVE_DIAGONAL_COST, cur, heuristic(x - 1, y + 1, x1, y1));
        }
        if chk(W) {
            e |= add_path(&mut heap, &mut tp, x - 1, y, g + MOVE_COST, cur, heuristic(x - 1, y, x1, y1));
        }
        if chk(S | W) && !c(x - 1, y - 1) {
            e |= add_path(&mut heap, &mut tp, x - 1, y - 1, g + MOVE_DIAGONAL_COST, cur, heuristic(x - 1, y - 1, x1, y1));
        }
        if chk(S) {
            e |= add_path(&mut heap, &mut tp, x, y - 1, g + MOVE_COST, cur, heuristic(x, y - 1, x1, y1));
        }
        if e {
            return None;
        }
    };
    let mut rev = Vec::new();
    let mut it = current;
    while let Some(p) = tp[it].parent {
        rev.push(walk_choice(tp[it].x as i32 - tp[p].x as i32, tp[it].y as i32 - tp[p].y as i32));
        it = p;
    }
    // `len > sizeof(wpd->path)`: path[] holds MAX_WALKPATH directions.
    if rev.len() > MAX_WALKPATH {
        return None;
    }
    rev.reverse();
    Some(WalkPath { path: rev })
}

/// path.cpp path_search_long: is a straight shot from (x0,y0) to (x1,y1) unblocked by `cell`?
pub fn path_search_long(m: u16, x0: i16, y0: i16, x1: i16, y1: i16, cell: CellChk) -> bool {
    let Some(md) = crate::data::maps().get(m) else { return false };
    let (mut x0, mut y0, mut x1, mut y1) = (x0 as i32, y0 as i32, x1 as i32, y1 as i32);
    let mut dx = x1 - x0;
    if dx < 0 {
        std::mem::swap(&mut x0, &mut x1);
        std::mem::swap(&mut y0, &mut y1);
        dx = -dx;
    }
    let dy = y1 - y0;
    let weight = if dx > dy.abs() { dx } else { (y1 - y0).abs() };
    let (mut wx, mut wy) = (0, 0);
    while x0 != x1 || y0 != y1 {
        wx += dx;
        wy += dy;
        if wx >= weight {
            wx -= weight;
            x0 += 1;
        }
        if wy >= weight {
            wy -= weight;
            y0 += 1;
        } else if wy < 0 {
            wy += weight;
            y0 -= 1;
        }
        if (x0 != x1 || y0 != y1) && map::getcellp(md, x0 as i16, y0 as i16, cell) {
            return false;
        }
    }
    true
}

/// path.cpp check_distance (CIRCULAR_AREA is off): square distance.
pub fn check_distance(dx: i32, dy: i32, distance: i32) -> bool {
    dx.abs().max(dy.abs()) <= distance
}

/// path.cpp distance.
pub fn distance(dx: i32, dy: i32) -> i32 {
    dx.abs().max(dy.abs())
}

/// path.cpp distance_client.
pub fn distance_client(dx: i32, dy: i32) -> i32 {
    let d = ((dx * dx + dy * dy) as f64).sqrt() - 0.1;
    if d < 0.0 {
        0
    } else {
        d as i32
    }
}

pub fn check_distance_client(dx: i32, dy: i32, distance: i32) -> bool {
    distance_client(dx, dy) <= distance.max(0)
}
