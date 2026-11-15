"""Shared atlas coordinates for the midibus mesh and its texture baker."""

PAINT_SIDE = (.035, .225)
PAINT_END = (.425, .605)
PAINT_ROOF = .802


def paint_uv(vertex, normal):
    x, y, z = vertex
    ax, ay, az = map(abs, normal)
    if az >= max(ax, ay):
        return .026 + (y + 4.2) * .948 / 8.4, PAINT_ROOF + (x + 1.28) * .172 / 2.56
    if ax >= ay:
        return .026 + (y + 4.2) * .948 / 8.4, PAINT_SIDE[int(normal.x < 0)] + z * .156 / 2.95
    return .034 + (x + 1.28) * .932 / 2.56, PAINT_END[int(normal.y > 0)] + z * .151 / 2.95
