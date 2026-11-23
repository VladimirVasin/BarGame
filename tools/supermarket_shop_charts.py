"""Fixture material charts laid out left to right, top row first."""
CASE, LINER, STEEL, FRAME, DECK, RAIL, BELT, GLASS = range(8)

FIXTURES = {
    "Shelf Cold Cabinet": CASE,
    "Shelf Cold Recess": LINER,
    "Shelf Cold Tiers": STEEL,
    "Cold Shelf Ticket Channels": RAIL,
    "Cold Cabinet Toe Vent": STEEL,
    "Checkout Counter": FRAME,
    "Checkout Front Panels": DECK,
    "Checkout Rails Scanner Tray And Bag Rack": STEEL,
    "Checkout Conveyor Belt": BELT,
    "Checkout Scanner Glass": GLASS,
}
for gondola in ("Dry", "Pantry"):
    for part, chart in (("Frame", FRAME), ("Tiers", DECK), ("Rails", RAIL),
                        ("End Caps", FRAME), ("Brackets", STEEL)):
        FIXTURES[f"Shelf {gondola} {part}"] = chart


def chart_uv(cell, point, axes, bounds):
    low, high = bounds
    # Keep the coating's grain proportional on small parts. The liner's frost
    # is registered to the bottom and sides of each individual back panel.
    if cell in (STEEL, FRAME, RAIL, BELT) and high[axes[1]]-low[axes[1]] > high[axes[0]]-low[axes[0]]:
        axes = axes[::-1]
    s, t = [(point[a]-low[a]) / max(high[a]-low[a], .35) for a in axes]
    inset = 5 / 512
    return ((cell % 4 + inset + s*(1-2*inset))/4,
            (1 - cell//4 + inset + t*(1-2*inset))/2)
