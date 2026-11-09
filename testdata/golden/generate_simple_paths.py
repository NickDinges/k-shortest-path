#!/usr/bin/env python3
"""Adds golden cases for simple paths (SimplePathsOnly) to cases.json.

The expected paths come from a brute-force depth-first enumeration of all simple paths, independent
of the library. Run from this folder: python3 generate_simple_paths.py
"""
import json
import math

LIMIT = 200  # paths per case; a case with more is cut before its last (possibly incomplete) weight group


def load_csv(name):
    edges = []
    for line in open(name):
        line = line.strip()
        if not line or line.startswith('#'):
            continue
        fields = [f.strip() for f in line.split(',')]
        edges.append((fields[0].upper(), fields[1].upper(), int(fields[2])))
    return edges


def simple_paths(edges, start, goal):
    out = {}
    for tail, head, weight in edges:
        if weight >= 0:  # negative edges are ignored by the library
            out.setdefault(tail, []).append((head, weight))
    result = []

    def visit(vertex, path, weight):
        if vertex == goal:
            result.append((weight, ','.join(path)))
            return
        for head, w in out.get(vertex, []):
            if head not in path:
                path.append(head)
                visit(head, path, weight + w)
                path.pop()

    if start != goal:
        visit(start, [start], 0)
    return sorted(result)


def make_case(name, graph, start, goal, max_detour=None, ratio=None):
    paths = simple_paths(load_csv(graph), start, goal)
    if paths and (max_detour is not None or ratio is not None):
        shortest = paths[0][0]
        bound = math.inf
        if max_detour is not None:
            bound = min(bound, max_detour)
        if ratio is not None:
            bound = min(bound, math.floor(ratio * shortest))
        paths = [p for p in paths if p[0] <= shortest + bound]
    complete = len(paths) <= LIMIT
    if not complete:
        paths = paths[:LIMIT + 1]
        last = paths[-1][0]
        paths = [p for p in paths if p[0] < last]
    case = {'name': name, 'graph': graph, 'from': start, 'to': goal}
    if max_detour is not None:
        case['maxDetour'] = max_detour
    if ratio is not None:
        case['maxDetourRatio'] = ratio
    case['simplePathsOnly'] = True
    case['complete'] = complete
    case['paths'] = [{'weight': w, 'vertices': v} for w, v in paths]
    return case


def main():
    cases = [c for c in json.load(open('cases.json')) if not c.get('simplePathsOnly')]
    by_name = {c['name']: c for c in cases}
    new = [
        make_case('simple-eppstein-example', 'eppstein-example.csv', 'S', 'T'),
        make_case('simple-eppstein-example-detour-20', 'eppstein-example.csv', 'S', 'T', max_detour=20),
        make_case('simple-eppstein-example-negative-weights', 'eppstein-example-negative.csv', 'S', 'T'),
        make_case('simple-eppstein-example-unreachable', 'eppstein-example.csv', 'T', 'S'),
        make_case('simple-eppstein-example-same-vertex', 'eppstein-example.csv', 'C', 'C'),
        make_case('simple-quickstart-roads', 'quickstart-roads.csv', 'HOME', 'OFFICE'),
        make_case('simple-quickstart-roads-two-way', 'quickstart-roads-two-way.csv', 'HOME', 'OFFICE'),
        make_case('simple-quickstart-roads-two-way-ratio-0.5', 'quickstart-roads-two-way.csv', 'HOME', 'OFFICE', ratio=0.5),
        make_case('simple-zero-weights-dag', 'zero-weights-dag.csv', 'A', 'F'),
    ]
    for i in range(12):  # same graphs and queries as the walk cases
        walk = next(c for name, c in by_name.items() if name.startswith('random-%02d' % i))
        new.append(make_case('simple-' + walk['name'], walk['graph'], walk['from'], walk['to'],
                             ratio=walk.get('maxDetourRatio')))
    json.dump(cases + new, open('cases.json', 'w'), indent=2)
    open('cases.json', 'a').write('\n')
    for c in new:
        print('%-46s %4d paths complete=%s' % (c['name'], len(c['paths']), c['complete']))


if __name__ == '__main__':
    main()
