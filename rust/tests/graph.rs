use eppstein::Graph;

/// Small deterministic generator (SplitMix64), the same as in the benchmarks.
struct Rng(u64);

impl Rng {
    fn next(&mut self) -> u64 {
        self.0 = self.0.wrapping_add(0x9E37_79B9_7F4A_7C15);
        let mut z = self.0;
        z = (z ^ (z >> 30)).wrapping_mul(0xBF58_476D_1CE4_E5B9);
        z = (z ^ (z >> 27)).wrapping_mul(0x94D0_49BB_1331_11EB);
        z ^ (z >> 31)
    }
    fn below(&mut self, n: u64) -> u64 {
        self.next() % n
    }
}

fn eppstein_example() -> Graph {
    let mut g = Graph::new();
    let file = concat!(env!("CARGO_MANIFEST_DIR"), "/../testdata/golden/eppstein-example.csv");
    g.import_edges(std::io::BufReader::new(std::fs::File::open(file).unwrap()))
        .unwrap();
    g
}

fn weights(g: &mut Graph, s: &str, t: &str, k: usize) -> Vec<i64> {
    g.paths(s, t).take(k).map(|p| p.weight()).collect()
}

/// Sorted weights of all s-t walks up to max_weight (None if too many states).
fn walk_weights(edges: &[(usize, usize, i64)], s: usize, t: usize, max_weight: i64) -> Option<Vec<i64>> {
    let mut result = Vec::new();
    let mut stack = vec![(s, 0i64)];
    let mut visited = 0;
    while let Some((v, w)) = stack.pop() {
        visited += 1;
        if visited > 200_000 {
            return None;
        }
        if v == t && w > 0 {
            result.push(w);
        }
        for &(a, b, ew) in edges {
            if a == v && ew >= 0 && w + ew <= max_weight {
                stack.push((b, w + ew));
            }
        }
    }
    result.sort();
    Some(result)
}

fn simple_weights(n: usize, edges: &[(usize, usize, i64)], s: usize, t: usize) -> Vec<i64> {
    fn visit(v: usize, w: i64, t: usize, edges: &[(usize, usize, i64)], seen: &mut Vec<bool>, out: &mut Vec<i64>) {
        if v == t {
            out.push(w);
            return;
        }
        seen[v] = true;
        for &(a, b, ew) in edges {
            if a == v && ew >= 0 && !seen[b] {
                visit(b, w + ew, t, edges, seen, out);
            }
        }
        seen[v] = false;
    }
    let mut out = Vec::new();
    if s != t {
        visit(s, 0, t, edges, &mut vec![false; n], &mut out);
    }
    out.sort();
    out
}

#[test]
fn eppstein_example_first_paths() {
    let mut g = eppstein_example();
    let first = weights(&mut g, "S", "T", 6);
    assert_eq!(first, vec![55, 58, 59, 61, 62, 63]);
    let p = g.find_shortest_path("s", "t");
    assert_eq!(g.vertex_names(&p), "S,D,E,F,J,T");
    assert_eq!(g.distance(g.vertex_index("S").unwrap()), Some(55));
}

#[test]
fn random_graphs_match_brute_force() {
    let mut rng = Rng(42);
    for it in 0..600 {
        let n = 2 + rng.below(6) as usize;
        let mut edges = Vec::new();
        for a in 0..n {
            for b in 0..n {
                if rng.below(10) < 3 {
                    edges.push((a, b, 1 + rng.below(9) as i64));
                }
            }
        }
        let (s, t) = (rng.below(n as u64) as usize, rng.below(n as u64) as usize);
        if s == t {
            continue;
        }
        let mode = it % 4; // 0 plain, 1 arc flags, 2 contraction hierarchy, 3 simple paths
        let mut g = Graph::new();
        for v in 0..n {
            g.add_vertex(&format!("V{v}"));
        }
        for &(a, b, w) in &edges {
            g.add_edge(a, b, w);
        }
        match mode {
            1 => assert!(g.enable_arc_flags(1 + it % 3)),
            2 => g.enable_contraction_hierarchy(),
            3 => g.set_simple_paths_only(true),
            _ => {}
        }
        let detour = (it % 5 == 0).then_some((it % 7) as i64);
        if let Some(d) = detour {
            g.set_max_detour(d);
        }
        let mut expected = if mode == 3 {
            simple_weights(n, &edges, s, t)
        } else {
            match walk_weights(&edges, s, t, 25) {
                Some(w) if w.len() <= 5000 => w,
                _ => continue,
            }
        };
        if let (Some(d), Some(&first)) = (detour, expected.first()) {
            expected.retain(|&w| w <= first + d);
        }
        let mut actual = Vec::new();
        let mut paths = g.paths_by_index(s, t);
        while let Some(p) = paths.next() {
            let vertices = paths.graph().path_vertices(&p);
            assert_eq!((vertices[0], *vertices.last().unwrap()), (s, t));
            if mode == 3 {
                let mut sorted = vertices.clone();
                sorted.sort();
                sorted.dedup();
                assert_eq!(sorted.len(), vertices.len(), "not simple");
            }
            if mode != 3 && p.weight() > 25 {
                break;
            }
            actual.push(p.weight());
            if actual.len() > expected.len() {
                break;
            }
        }
        assert_eq!(
            actual, expected,
            "it={it} n={n} s={s} t={t} mode={mode} edges={edges:?}"
        );
    }
}

#[test]
fn contraction_hierarchy_matches_dijkstra() {
    let mut rng = Rng(7);
    for _ in 0..150 {
        let n = 1 + rng.below(119) as usize;
        let m = rng.below(4 * n as u64) as usize;
        let (mut plain, mut ch) = (Graph::new(), Graph::new());
        for v in 0..n {
            plain.add_vertex(&format!("V{v}"));
            ch.add_vertex(&format!("V{v}"));
        }
        for _ in 0..m {
            let (a, b) = (rng.below(n as u64) as usize, rng.below(n as u64) as usize);
            let w = match rng.below(10) {
                0 => 0,
                1 => -1,
                _ => 1 + rng.below(19) as i64,
            };
            plain.add_edge(a, b, w);
            ch.add_edge(a, b, w);
        }
        ch.enable_contraction_hierarchy();
        assert!(ch.precompute_contraction_hierarchy());
        for _ in 0..5 {
            let (s, t) = (rng.below(n as u64) as usize, rng.below(n as u64) as usize);
            let a: Vec<i64> = plain.paths_by_index(s, t).take(20).map(|p| p.weight()).collect();
            let b: Vec<i64> = ch.paths_by_index(s, t).take(20).map(|p| p.weight()).collect();
            assert_eq!(a, b);
            for v in 0..n {
                assert_eq!(plain.distance(v), ch.distance(v));
                if let Some(e) = ch.tree_edge(v) {
                    assert_eq!(
                        ch.distance(v),
                        Some(ch.edge_weight(e) + ch.distance(ch.edge_head(e)).unwrap())
                    );
                }
            }
        }
    }
}

#[test]
fn changes_invalidate_and_settings_validate() {
    let mut g = eppstein_example();
    let mut h = eppstein_example();
    g.enable_contraction_hierarchy();
    assert!(g.enable_arc_flags_regions("S,A,B,C;D,E,F;G,H,I,J,K,T"));
    assert!(!g.enable_arc_flags_regions("S,X"));
    assert_eq!(weights(&mut g, "S", "T", 30), weights(&mut h, "S", "T", 30));
    assert!(g.contraction_hierarchy_shortcuts() > 0 || g.contraction_hierarchy_enabled());
    g.edge_group_weights("beta", 1);
    h.edge_group_weights("BETA", 1);
    assert_eq!(weights(&mut g, "S", "T", 30), weights(&mut h, "S", "T", 30));
    g.disable_contraction_hierarchy();
    assert!(g.precompute_arc_flags());
    assert_eq!(g.arc_flags_region_count(), 3);
    assert_eq!(weights(&mut g, "S", "T", 30), weights(&mut h, "S", "T", 30));

    assert!(!g.set_max_detour(-1));
    assert!(!g.set_max_detour_ratio(f64::NAN));
    assert!(g.set_max_detour_ratio(0.1));
    let bounded = weights(&mut g, "S", "T", 1000);
    let shortest = bounded[0];
    assert!(bounded.iter().all(|&w| w <= shortest + shortest / 10));
    let p = g.find_shortest_path("S", "T");
    g.set_edge_weight(p.edges().next().unwrap(), 1000);
    assert!(!g.find_next_shortest_path().is_valid(), "query invalid after a change");
    assert!(!g.find_shortest_path("S", "nope").is_valid());
    assert!(!g.find_shortest_path("S", "S").is_valid());
}

#[test]
fn imports_report_line_numbers() {
    let mut g = Graph::new();
    assert_eq!(g.import_edges("# c\nA,B,1\n B , C , 2 , g\n".as_bytes()), Ok(2));
    assert_eq!(
        g.import_edges("A,B\n".as_bytes()).unwrap_err().to_string(),
        "line 1: expected tail,head,weight[,group] but found 2 values"
    );
    let mut d = Graph::new();
    assert_eq!(d.import_dimacs("c x\np sp 3 2\na 1 2 5\na 2 3 5\n".as_bytes()), Ok(2));
    assert_eq!(d.find_shortest_path("V1", "V3").weight(), 10);
    assert!(Graph::new()
        .import_dimacs("a 1 2 3\n".as_bytes())
        .unwrap_err()
        .to_string()
        .starts_with("line 1"));
    assert!(Graph::new()
        .import_dimacs_limit("p sp 2000 1\n".as_bytes(), 1000)
        .is_err());
    assert!(Graph::new().import_dimacs("p sp 2 1\na 1 3 1\n".as_bytes()).is_err());
}

#[test]
fn partition_metis_and_serialize() {
    let mut g = eppstein_example();
    let mut metis = Vec::new();
    g.write_metis_graph(&mut metis).unwrap();
    assert!(String::from_utf8(metis).unwrap().starts_with("13 "));
    let partition = "% x\n0\n0\n0\n0\n1\n1\n1\n2\n2\n2\n2\n2\n2\n";
    g.enable_arc_flags_from_partition(partition.as_bytes()).unwrap();
    assert_eq!(g.arc_flags_region_count(), 3);
    assert!(g.enable_arc_flags_from_partition("0\n".as_bytes()).is_err());
    let p = g.find_shortest_path("S", "T");
    let dot = g.serialize(&[p]);
    assert!(dot.contains("subgraph cluster_2") && dot.contains("color=\"red\"") && dot.contains("doublecircle"));
}

#[test]
fn simple_paths_two_way_roads() {
    let mut g = Graph::new();
    g.create_vertices("A,B,C,D");
    for (a, b, w) in [
        ("A", "B", 1),
        ("B", "D", 1),
        ("A", "C", 2),
        ("C", "D", 2),
        ("B", "C", 1),
    ] {
        g.create_edges(&format!("{a},{b}"), &format!("{b},{a}"), w, "");
    }
    g.set_simple_paths_only(true);
    let all = weights(&mut g, "A", "D", 100);
    assert_eq!(all, vec![2, 4, 4, 4]);
    assert!(g.spur_searches() > 0);
}

#[test]
fn many_labels_round_trip_case_insensitive() {
    let mut g = Graph::new();
    for i in 0..20_000 {
        assert_eq!(g.add_vertex(&format!("v{i}x")), Some(i));
    }
    assert_eq!(g.add_vertex("V77X"), Some(77));
    for i in (0..20_000).step_by(7) {
        assert_eq!(g.vertex_index(&format!("V{i}x")), Some(i));
        assert_eq!(g.label(i), format!("V{i}X"));
    }
    assert_eq!(g.vertex_index("V20000X"), None);
    assert_eq!(g.add_vertex("1abc"), None);
    assert_eq!(g.add_vertex("Straße"), Some(20_000)); // non-ASCII: Unicode upper case
    assert_eq!(g.vertex_index("STRASSE"), Some(20_000));
    assert!(g.create_vertices("A,B,,A"));
    assert_eq!(g.vertex_count(), 2);
}
