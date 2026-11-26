"""Metric contacts shared by the playground models and the child animation bank."""
BEAM_END = 2.72
BEAM_TOP = .22
RUNG_X = .18
RUNG_Y = 1.485
RUNGS = tuple(round(.68 + i * .24, 2) for i in range(7))
STEP_TOP = .18
STEP_FIRST = .40
STEP_LAST = 2.51
RUNG_END = 2.92

SLIDE_END = 4.36
SLIDE_STEPS = ((.36, .19), (.63, .38), (.90, .57), (1.17, .76))
SLIDE_RAIL = ((.33, .90), (1.32, 1.48))
SLIDE_DECK = .76
SLIDE_PROFILE = ((1.93, .76), (2.13, .73), (2.39, .59), (2.67, .42),
                 (2.96, .26), (3.25, .13), (3.52, .069), (3.76, .06))
BRIDGE_LANE = 1.72
BRIDGE_CROSS_TIME = 4.5

SEESAW_PIVOT = .395
SEESAW_DOCK = 1.61
SEESAW_SEAT = 1.08
SEESAW_GRIP = (.172, .725, .85)
SEESAW_FOOT = (.175, .17, 1.06)
BALANCE_STEP_TOP = .18
