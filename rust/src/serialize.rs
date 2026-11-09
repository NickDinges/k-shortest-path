//! Graphviz (DOT) output.

use std::collections::HashMap;

use crate::paths::Path;
use crate::{Graph, NONE};

const COLORS: [&str; 8] = [
    "red",
    "blue",
    "darkgreen",
    "orange",
    "purple",
    "brown",
    "magenta",
    "cyan4",
];

fn quote(text: &str) -> String {
    let mut s = String::with_capacity(text.len() + 2);
    s.push('"');
    for c in text.chars() {
        match c {
            '"' | '\\' => {
                s.push('\\');
                s.push(c);
            }
            '\n' => s.push_str("\\n"),
            _ => s.push(c),
        }
    }
    s.push('"');
    s
}

impl Graph {
    /// The graph in Graphviz DOT format, with paths highlighted in different colors. After a
    /// query, vertices show their distance to the goal and tree edges are bold; arc-flag regions
    /// are drawn as clusters.
    pub fn serialize(&mut self, highlight: &[Path]) -> String {
        self.ensure_adjacency();
        let mut edge_colors: HashMap<u32, Vec<&str>> = HashMap::new();
        let mut highlighted = vec![false; self.labels.len()];
        for (index, p) in highlight.iter().filter(|p| p.is_valid()).enumerate() {
            let color = COLORS[index % COLORS.len()];
            for &e in &p.edges {
                let list = edge_colors.entry(e).or_default();
                if !list.contains(&color) {
                    list.push(color);
                }
                highlighted[self.edge_tail[e as usize] as usize] = true;
                highlighted[self.edge_head[e as usize] as usize] = true;
            }
        }
        let mut b = String::from("digraph Eppstein {\n    rankdir=LR;\n    node [shape=circle];\n");
        if self.arc.enabled && self.arc.valid && self.arc.region_count > 0 {
            for r in 0..self.arc.region_count {
                b += &format!("    subgraph cluster_{r} {{\n        label=\"Region {r}\";\n        style=dashed;\n");
                for v in 0..self.labels.len() {
                    if self.arc.vertex_region[v] as usize == r {
                        b += &format!("        {}\n", self.vertex_statement(v, &highlighted));
                    }
                }
                b += "    }\n";
            }
        } else {
            for v in 0..self.labels.len() {
                b += &format!("    {}\n", self.vertex_statement(v, &highlighted));
            }
        }
        for v in 0..self.labels.len() {
            for i in self.out_start[v]..self.out_start[v + 1] {
                let e = self.out_edges[i as usize];
                let w = self.edge_weight[e as usize];
                let mut attributes = vec![
                    format!("label={}", quote(&w.to_string())),
                    format!(
                        "tooltip={}",
                        quote(&format!("group {}", self.groups[self.edge_group[e as usize] as usize]))
                    ),
                ];
                let mut styles = Vec::new();
                if w < 0 {
                    styles.push("dashed");
                }
                if self.q.ready && self.distance(v).is_some() && self.q.tree_edge[v] == e && e != NONE {
                    styles.push("bold");
                }
                if let Some(colors) = edge_colors.get(&e) {
                    attributes.push(format!("color={}", quote(&colors.join(":"))));
                    attributes.push(format!("fontcolor={}", quote(colors[0])));
                    attributes.push("penwidth=2".to_string());
                }
                if !styles.is_empty() {
                    attributes.push(format!("style={}", quote(&styles.join(","))));
                }
                b += &format!(
                    "    {} -> {} [{}];\n",
                    quote(&self.labels[v]),
                    quote(&self.labels[self.edge_head[e as usize] as usize]),
                    attributes.join(", ")
                );
            }
        }
        b += "}\n";
        b
    }

    fn vertex_statement(&self, v: usize, highlighted: &[bool]) -> String {
        let mut label = self.labels[v].to_string();
        if self.q.ready {
            if let Some(d) = self.distance(v) {
                label += &format!("\n{d}");
            }
        }
        let mut attributes = vec![format!("label={}", quote(&label))];
        if self.q.ready && (v == self.q.source || v == self.q.target) {
            attributes.push("shape=doublecircle".to_string());
        }
        if highlighted[v] {
            attributes.push("penwidth=2".to_string());
        }
        format!("{} [{}];", quote(&self.labels[v]), attributes.join(", "))
    }
}
