//! CSV and DIMACS import.

use std::fmt;
use std::io::BufRead;

use crate::Graph;

/// Default vertex limit of [`Graph::import_dimacs`] (protects against huge declared counts).
pub const DEFAULT_MAX_IMPORT_VERTICES: usize = 100_000_000;

/// Error of an import, with the line number (0 if it concerns the whole input).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ImportError {
    pub line: usize,
    pub message: String,
}

impl ImportError {
    pub(crate) fn new(line: usize, message: impl Into<String>) -> Self {
        ImportError {
            line,
            message: message.into(),
        }
    }
}

impl fmt::Display for ImportError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        if self.line > 0 {
            write!(f, "line {}: {}", self.line, self.message)
        } else {
            f.write_str(&self.message)
        }
    }
}

impl std::error::Error for ImportError {}

/// Next whitespace-separated field and the rest.
fn next_field(s: &str) -> (&str, &str) {
    let s = s.trim_start();
    match s.find(char::is_whitespace) {
        Some(end) => (&s[..end], &s[end..]),
        None => (s, ""),
    }
}

impl Graph {
    /// Adds edges from CSV lines "tail,head,weight[,group]", creating missing vertices. Empty
    /// lines and lines starting with '#' are skipped. Returns the number of edges added; on an
    /// error, the edges before the bad line stay.
    pub fn import_edges<R: BufRead>(&mut self, reader: R) -> Result<usize, ImportError> {
        let mut count = 0;
        for (i, line) in reader.lines().enumerate() {
            let n = i + 1;
            let line = line.map_err(|e| ImportError::new(n, e.to_string()))?;
            let line = line.trim();
            if line.is_empty() || line.starts_with('#') {
                continue;
            }
            let fields: Vec<&str> = line.split(',').map(str::trim).collect();
            if fields.len() < 3 || fields.len() > 4 {
                return Err(ImportError::new(
                    n,
                    format!("expected tail,head,weight[,group] but found {} values", fields.len()),
                ));
            }
            let tail = self
                .add_vertex(fields[0])
                .ok_or_else(|| ImportError::new(n, format!("invalid vertex label {:?}", fields[0])))?;
            let head = self
                .add_vertex(fields[1])
                .ok_or_else(|| ImportError::new(n, format!("invalid vertex label {:?}", fields[1])))?;
            let weight: i64 = fields[2]
                .parse()
                .map_err(|_| ImportError::new(n, format!("invalid weight {:?}", fields[2])))?;
            self.add_edge_group(tail, head, weight, fields.get(3).copied().unwrap_or(""));
            count += 1;
        }
        Ok(count)
    }

    /// Adds a graph in DIMACS shortest-path format ("p sp N M", "a TAIL HEAD WEIGHT", "c ..."),
    /// vertex i becomes "V<i>". At most [`DEFAULT_MAX_IMPORT_VERTICES`] vertices.
    pub fn import_dimacs<R: BufRead>(&mut self, reader: R) -> Result<usize, ImportError> {
        self.import_dimacs_limit(reader, DEFAULT_MAX_IMPORT_VERTICES)
    }

    /// Like [`Graph::import_dimacs`] with a vertex limit.
    pub fn import_dimacs_limit<R: BufRead>(
        &mut self,
        mut reader: R,
        max_vertices: usize,
    ) -> Result<usize, ImportError> {
        let mut index: Option<Vec<usize>> = None;
        let mut count = 0;
        let mut line = String::new();
        let mut n = 0;
        loop {
            line.clear();
            n += 1;
            match reader.read_line(&mut line) {
                Ok(0) => break,
                Ok(_) => {}
                Err(e) => return Err(ImportError::new(n, e.to_string())),
            }
            let (kind, rest) = next_field(&line);
            match kind {
                "" | "c" => continue,
                "a" => {
                    let Some(index) = &index else {
                        return Err(ImportError::new(
                            n,
                            "edge before the problem line 'p sp VERTICES EDGES'",
                        ));
                    };
                    let (t, rest) = next_field(rest);
                    let (h, rest) = next_field(rest);
                    let (w, rest) = next_field(rest);
                    let (extra, _) = next_field(rest);
                    let (Ok(t), Ok(h), Ok(w), "") = (t.parse::<usize>(), h.parse::<usize>(), w.parse::<i64>(), extra)
                    else {
                        return Err(ImportError::new(n, "expected 'a TAIL HEAD WEIGHT'"));
                    };
                    if t < 1 || t >= index.len() || h < 1 || h >= index.len() {
                        return Err(ImportError::new(
                            n,
                            format!("vertex number out of range 1..{}", index.len() - 1),
                        ));
                    }
                    self.add_edge_group(index[t], index[h], w, "");
                    count += 1;
                }
                "p" => {
                    if index.is_some() {
                        return Err(ImportError::new(n, "more than one problem line"));
                    }
                    let fields: Vec<&str> = line.split_whitespace().collect();
                    let bad = || ImportError::new(n, "expected 'p sp VERTICES EDGES'");
                    if fields.len() != 4 || fields[1] != "sp" {
                        return Err(bad());
                    }
                    let declared: u64 = fields[2].parse().map_err(|_| bad())?;
                    let _: u64 = fields[3].parse().map_err(|_| bad())?;
                    let limit = max_vertices.min(i32::MAX as usize - 1);
                    if declared > limit as u64 {
                        return Err(ImportError::new(
                            n,
                            format!("{declared} vertices exceed the limit of {limit}"),
                        ));
                    }
                    let mut map = vec![0; declared as usize + 1];
                    for (i, slot) in map.iter_mut().enumerate().skip(1) {
                        *slot = self.add_vertex(&format!("V{i}")).expect("valid label");
                    }
                    index = Some(map);
                }
                other => return Err(ImportError::new(n, format!("unknown line type {other:?}"))),
            }
        }
        Ok(count)
    }
}
