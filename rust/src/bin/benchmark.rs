//! Timing benchmark on grid graphs, identical to the C# (Eppstein.Benchmarks) and Go
//! (go/cmd/benchmark) programs: same generator (SplitMix64, seed 1), same options, same output,
//! so the last column (weight of the last path) must match across the languages.
//!
//!   cargo run --release --bin benchmark -- [size...] [--k N] [--local] [--ratio R]
//!       [--build strings|index|dimacs] [--two-way] [--simple] [--ch]
//!       [--arc-flags N] [--reps N] [--weights]
//!
//! --reps N repeats the first-path query N more times in the same process (steady state),
//! --weights enumerates only the weights of the next paths, --arc-flags N enables arc flags with N
//! regions. A "stats:" line on standard error reports the steady-state times and the bytes
//! allocated while the next paths were enumerated (there is no garbage collector: gc_count is 0).
//!
//! Memory is measured with a counting allocator: bytes allocated by the graph after the first
//! query (live heap, comparable to the C# and Go numbers after a garbage collection).

use std::alloc::{GlobalAlloc, Layout, System};
use std::fmt::Write as _;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::time::Instant;

use eppstein::Graph;

struct Counting;
static ALLOCATED: AtomicUsize = AtomicUsize::new(0);
static TOTAL: AtomicUsize = AtomicUsize::new(0);

unsafe impl GlobalAlloc for Counting {
    unsafe fn alloc(&self, layout: Layout) -> *mut u8 {
        ALLOCATED.fetch_add(layout.size(), Ordering::Relaxed);
        TOTAL.fetch_add(layout.size(), Ordering::Relaxed);
        System.alloc(layout)
    }
    unsafe fn dealloc(&self, ptr: *mut u8, layout: Layout) {
        ALLOCATED.fetch_sub(layout.size(), Ordering::Relaxed);
        System.dealloc(ptr, layout)
    }
    unsafe fn realloc(&self, ptr: *mut u8, layout: Layout, new_size: usize) -> *mut u8 {
        ALLOCATED.fetch_add(new_size, Ordering::Relaxed);
        TOTAL.fetch_add(new_size.saturating_sub(layout.size()), Ordering::Relaxed);
        ALLOCATED.fetch_sub(layout.size(), Ordering::Relaxed);
        System.realloc(ptr, layout, new_size)
    }
}

#[global_allocator]
static GLOBAL: Counting = Counting;

/// SplitMix64: tiny deterministic generator, implemented identically in C#, Go and Rust.
struct SplitMix(u64);

impl SplitMix {
    fn next(&mut self) -> u64 {
        self.0 = self.0.wrapping_add(0x9E37_79B9_7F4A_7C15);
        let mut z = self.0;
        z = (z ^ (z >> 30)).wrapping_mul(0xBF58_476D_1CE4_E5B9);
        z = (z ^ (z >> 27)).wrapping_mul(0x94D0_49BB_1331_11EB);
        z ^ (z >> 31)
    }
    fn weight(&mut self) -> i64 {
        1 + (self.next() % 100) as i64
    }
}

/// Grid edges (vertex = y * size + x): right and lower neighbor, every 10th vertex also back to
/// the left; with two_way every street in both directions (the back edges are dropped).
fn grid_edges(size: usize, two_way: bool) -> Vec<(usize, usize, i64)> {
    let mut rnd = SplitMix(1);
    let mut edges = Vec::new();
    for y in 0..size {
        for x in 0..size {
            let v = y * size + x;
            if x + 1 < size {
                edges.push((v, v + 1, rnd.weight()));
            }
            if y + 1 < size {
                edges.push((v, v + size, rnd.weight()));
            }
            if x > 0 && v % 10 == 0 {
                edges.push((v, v - 1, rnd.weight()));
            }
        }
    }
    if two_way {
        let one_way: Vec<_> = edges.into_iter().filter(|e| e.1 > e.0).collect();
        let mut both = one_way.clone();
        both.extend(one_way.iter().map(|&(t, h, w)| (h, t, w)));
        edges = both;
    }
    edges
}

struct Options {
    k: usize,
    local: bool,
    ratio: f64,
    build: String,
    two_way: bool,
    simple: bool,
    ch: bool,
    arc_flags: usize,
    reps: usize,
    weights: bool,
}

fn ms(t: Instant) -> f64 {
    t.elapsed().as_secs_f64() * 1000.0
}

fn run(size: usize, o: &Options) {
    let edges = grid_edges(size, o.two_way);
    let labels: Vec<String> = (0..size * size)
        .map(|v| format!("V{}X{}", v % size, v / size))
        .collect();
    let vertex_list = labels.join(",");
    let dimacs = (o.build == "dimacs").then(|| {
        let mut text = format!("p sp {} {}\n", labels.len(), edges.len());
        for &(t, h, w) in &edges {
            writeln!(text, "a {} {} {}", t + 1, h + 1, w).unwrap();
        }
        text
    });

    let memory_before = ALLOCATED.load(Ordering::Relaxed);
    let watch = Instant::now();
    let mut g = Graph::new();
    match o.build.as_str() {
        "strings" => {
            g.create_vertices(&vertex_list);
            for &(t, h, w) in &edges {
                g.create_edges(&labels[t], &labels[h], w, "g");
            }
        }
        "index" => {
            for label in &labels {
                g.add_vertex(label);
            }
            for &(t, h, w) in &edges {
                g.add_edge_group(t, h, w, "g");
            }
        }
        "dimacs" => {
            g.import_dimacs(dimacs.as_ref().unwrap().as_bytes()).unwrap();
        }
        other => panic!("unknown --build {other}"),
    }
    let build_time = ms(watch);

    if o.ch {
        g.enable_contraction_hierarchy();
        let watch = Instant::now();
        g.precompute_contraction_hierarchy();
        eprintln!(
            "{size}x{size}: contraction {:.0} ms, {} shortcuts",
            ms(watch),
            g.contraction_hierarchy_shortcuts()
        );
    }

    let mut arc_flags_time = 0.0;
    if o.arc_flags > 0 {
        g.enable_arc_flags(o.arc_flags);
        let watch = Instant::now();
        g.precompute_arc_flags();
        arc_flags_time = ms(watch);
    }

    let watch = Instant::now();
    g.set_max_detour_ratio(o.ratio);
    g.set_simple_paths_only(o.simple);
    let (from, to) = if o.local {
        (size / 2, size / 2 + (size / 20).max(1))
    } else {
        (0, size - 1)
    };
    g.find_shortest_path_by_index(from * size + from, to * size + to);
    let first = ms(watch);
    let memory = ALLOCATED.load(Ordering::Relaxed).saturating_sub(memory_before) as f64 / 1e6;

    let watch = Instant::now();
    let mut p = g.find_shortest_path_by_index(from * size + from, to * size + to);
    let repeated = ms(watch);

    // Steady state: the same query again, reps times
    let mut warm = vec![repeated];
    for i in 0..o.reps {
        let watch = Instant::now();
        p = g.find_shortest_path_by_index(from * size + from, to * size + to);
        if i == 0 {
            warm.clear();
        }
        warm.push(ms(watch));
    }
    warm.sort_by(f64::total_cmp);

    let allocated = TOTAL.load(Ordering::Relaxed);
    let mut last = p.weight();
    let mut found = usize::from(p.is_valid());
    let next;
    if o.weights {
        // Weights without path objects: a new enumeration; the clock starts at its first result
        found = 0;
        let mut watch = Instant::now();
        for weight in g.path_weights_by_index(from * size + from, to * size + to) {
            if found == 0 {
                watch = Instant::now();
            }
            last = weight;
            found += 1;
            if found >= o.k {
                break;
            }
        }
        next = if found > 0 { ms(watch) } else { 0.0 };
    } else {
        let watch = Instant::now();
        for _ in 1..o.k {
            if !p.is_valid() {
                break;
            }
            p = g.find_next_shortest_path();
            if p.is_valid() {
                last = p.weight();
                found += 1;
            }
        }
        next = ms(watch);
    }
    eprintln!(
        "stats: warm_median_ms={:.3} warm_min_ms={:.3} gc_count=0 gc_pause_ms=0.00 alloc_mb={:.1} arcflags_ms={:.0}",
        warm[warm.len() / 2],
        warm[0],
        (TOTAL.load(Ordering::Relaxed) - allocated) as f64 / 1e6,
        arc_flags_time
    );
    println!(
        "| {size}x{size} | {} | {} | {build_time:.1} ms | {memory:.1} MB | {first:.1} ms | {repeated:.1} ms | {next:.1} ms | {found} | {last} |",
        size * size,
        edges.len()
    );
}

fn main() {
    let mut o = Options {
        k: 10000,
        local: false,
        ratio: f64::INFINITY,
        build: "strings".into(),
        two_way: false,
        simple: false,
        ch: false,
        arc_flags: 0,
        reps: 0,
        weights: false,
    };
    let mut sizes = Vec::new();
    let args: Vec<String> = std::env::args().skip(1).collect();
    let mut i = 0;
    while i < args.len() {
        match args[i].as_str() {
            "--k" => {
                i += 1;
                o.k = args[i].parse().unwrap();
            }
            "--local" => o.local = true,
            "--two-way" => o.two_way = true,
            "--simple" => o.simple = true,
            "--ch" => o.ch = true,
            "--weights" => o.weights = true,
            "--arc-flags" => {
                i += 1;
                o.arc_flags = args[i].parse().unwrap();
            }
            "--reps" => {
                i += 1;
                o.reps = args[i].parse().unwrap();
            }
            "--build" => {
                i += 1;
                o.build = args[i].clone();
            }
            "--ratio" => {
                i += 1;
                o.ratio = args[i].parse().unwrap();
            }
            s => sizes.push(s.parse().expect("grid size")),
        }
        i += 1;
    }
    if sizes.is_empty() {
        sizes = vec![50, 100, 200];
    }
    println!(
        "{}{}, build: {}{}{}{}{}{}",
        if o.local {
            "Local query (nearby vertices in the middle of the grid)"
        } else {
            "Corner to corner"
        },
        if o.ratio.is_infinite() {
            String::new()
        } else {
            format!(", MaxDetourRatio = {}", o.ratio)
        },
        o.build,
        if o.two_way { ", two-way streets" } else { "" },
        if o.simple { ", SimplePathsOnly" } else { "" },
        if o.ch { ", contraction hierarchy" } else { "" },
        if o.arc_flags > 0 {
            format!(", arc flags ({} regions)", o.arc_flags)
        } else {
            String::new()
        },
        if o.weights { ", weights only" } else { "" }
    );
    println!("| grid | vertices | edges | build graph | memory | first path | first path (repeated query) | next paths | paths found | weight of last path |");
    println!("|---|---|---|---|---|---|---|---|---|---|");
    for size in sizes {
        run(size, &o);
    }
}
