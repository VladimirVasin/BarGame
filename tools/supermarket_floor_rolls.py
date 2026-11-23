"""Installed floor roll bounds in shop metres. Rear offcuts are older replacement stock."""
EDGES = (-8., -7.05, -5.15, -3.25, -1.35, .55, 2.45, 4.35, 6.25, 8.)
CROSS_CUTS = {1: -2.65, 3: 2.75, 5: -.85, 7: 3.25}


def pieces():
    for column, (left, right) in enumerate(zip(EDGES, EDGES[1:])):
        cuts = [-5.5, 5.5]
        if column in CROSS_CUTS:
            cuts.insert(1, CROSS_CUTS[column])
        for start, end in zip(cuts, cuts[1:]):
            repair = (column == 1 and end == -2.65) or (column == 7 and start == 3.25)
            yield left, right, start, end, repair
