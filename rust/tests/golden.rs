//! Shared golden data in ../testdata/golden, also checked by the C# and Go tests.

use std::fs;
use std::io::BufReader;

use eppstein::Graph;
use serde_json::Value;

const DIR: &str = concat!(env!("CARGO_MANIFEST_DIR"), "/../testdata/golden");

fn check_all(ch: bool) {
    let cases: Vec<Value> = serde_json::from_str(&fs::read_to_string(format!("{DIR}/cases.json")).unwrap()).unwrap();
    assert!(cases.len() > 10);
    for c in &cases {
        let name = c["name"].as_str().unwrap();
        let file = c["graph"].as_str().unwrap();
        let mut g = Graph::new();
        let reader = BufReader::new(fs::File::open(format!("{DIR}/{file}")).unwrap());
        if file.ends_with(".gr") {
            g.import_dimacs(reader).unwrap();
        } else {
            g.import_edges(reader).unwrap();
        }
        if let Some(d) = c["maxDetour"].as_i64() {
            g.set_max_detour(d);
        }
        if let Some(r) = c["maxDetourRatio"].as_f64() {
            g.set_max_detour_ratio(r);
        }
        g.set_simple_paths_only(c["simplePathsOnly"].as_bool().unwrap_or(false));
        if ch {
            g.enable_contraction_hierarchy();
        }
        let expected: Vec<(i64, String)> = c["paths"]
            .as_array()
            .unwrap()
            .iter()
            .map(|p| {
                (
                    p["weight"].as_i64().unwrap(),
                    p["vertices"].as_str().unwrap().to_string(),
                )
            })
            .collect();
        let complete = c["complete"].as_bool().unwrap_or(false);
        let last = expected.last().map_or(i64::MIN, |p| p.0);

        let mut actual = Vec::new();
        let mut paths = g.paths(c["from"].as_str().unwrap(), c["to"].as_str().unwrap());
        while let Some(p) = paths.next() {
            if !complete && p.weight() > last {
                break;
            }
            actual.push((p.weight(), paths.graph().vertex_names(&p)));
            assert!(actual.len() <= expected.len(), "{name}: more paths than expected");
        }
        // Order among paths of equal weight is not compared
        let mut a = actual.clone();
        let mut e = expected.clone();
        a.sort();
        e.sort();
        assert_eq!(a, e, "{name} (ch={ch})");
        let weights: Vec<i64> = actual.iter().map(|p| p.0).collect();
        assert!(weights.windows(2).all(|w| w[0] <= w[1]), "{name}: not sorted");
    }
}

#[test]
fn golden_cases() {
    check_all(false);
}

#[test]
fn golden_cases_with_contraction_hierarchy() {
    check_all(true);
}
