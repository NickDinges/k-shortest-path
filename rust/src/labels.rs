//! Vertex labels: all label text in one string (arena) and an open-addressing hash table of vertex
//! indices. Each label is stored once, with 8 bytes of overhead per vertex instead of two heap
//! strings and a map entry.

use crate::NONE;

#[derive(Default)]
pub(crate) struct Labels {
    text: String,
    end: Vec<u32>,   // label i is text[end[i-1]..end[i]]
    slots: Vec<u32>, // vertex indices, NONE = empty; length is a power of two
}

/// FNV-1a: simple and fast for short keys (labels are not attacker-controlled hash keys here).
fn hash(s: &str) -> u64 {
    let mut h: u64 = 0xcbf2_9ce4_8422_2325;
    for b in s.bytes() {
        h = (h ^ b as u64).wrapping_mul(0x0100_0000_01b3);
    }
    h ^ (h >> 29)
}

impl Labels {
    pub(crate) fn len(&self) -> usize {
        self.end.len()
    }

    pub(crate) fn clear(&mut self) {
        self.text.clear();
        self.end.clear();
        self.slots.clear();
    }

    pub(crate) fn get_label(&self, i: usize) -> &str {
        let start = if i == 0 { 0 } else { self.end[i - 1] as usize };
        &self.text[start..self.end[i] as usize]
    }

    /// Index of a label (exact match).
    pub(crate) fn find(&self, label: &str) -> Option<usize> {
        if self.slots.is_empty() {
            return None;
        }
        let mask = self.slots.len() - 1;
        let mut i = hash(label) as usize & mask;
        loop {
            let v = self.slots[i];
            if v == NONE {
                return None;
            }
            if self.get_label(v as usize) == label {
                return Some(v as usize);
            }
            i = (i + 1) & mask;
        }
    }

    /// Adds a label that is not present yet and returns its index.
    pub(crate) fn push(&mut self, label: &str) -> usize {
        let index = self.end.len();
        if (index + 1) * 2 > self.slots.len() {
            self.grow();
        }
        self.text.push_str(label);
        assert!(self.text.len() <= u32::MAX as usize, "label text exceeds 4 GB");
        self.end.push(self.text.len() as u32);
        self.insert_slot(index as u32);
        index
    }

    fn insert_slot(&mut self, v: u32) {
        let mask = self.slots.len() - 1;
        let mut i = hash(self.get_label(v as usize)) as usize & mask;
        while self.slots[i] != NONE {
            i = (i + 1) & mask;
        }
        self.slots[i] = v;
    }

    fn grow(&mut self) {
        let size = (self.slots.len() * 2).max(16);
        self.slots = vec![NONE; size];
        for v in 0..self.end.len() as u32 {
            self.insert_slot(v);
        }
    }
}

impl std::ops::Index<usize> for Labels {
    type Output = str;
    fn index(&self, i: usize) -> &str {
        self.get_label(i)
    }
}
