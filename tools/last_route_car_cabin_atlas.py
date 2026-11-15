"""Shared tile layout for the sedan's upholstery and moulded cabin parts."""

PATCH_GRID = 4
PATCHES = (
    "dash_top", "fascia", "driver_door", "passenger_door",
    "armrest", "driver_cloth", "passenger_cloth", "rear_cloth",
    "headliner", "footwell", "glovebox_flock", "glovebox_lid",
    "bakelite", "vent_grille", "vinyl_binding", "cabin_liner",
)
ROLE_PATCHES = {
    "dash_lip": "dash_top",
    "door_armrest": "armrest",
    "driver_seat": "driver_cloth",
    "passenger_seat": "passenger_cloth",
    "rear_bench": "rear_cloth",
    "headlining": "headliner",
    "sun_visor": "headliner",
    "cabin_floor": "footwell",
    "floor_mats": "footwell",
    "glovebox_compartment": "glovebox_flock",
    "glovebox_lid": "glovebox_lid",
    "radio_knob": "bakelite",
    "lever_knobs": "bakelite",
    "ashtray": "bakelite",
    "dash_vents": "vent_grille",
    "steering_wheel": "vinyl_binding",
    "seat_welting": "vinyl_binding",
    "transmission_tunnel": "cabin_liner",
    "interior_shell": "cabin_liner",
    "cabin_lamp_housing": "bakelite",
}


def cabin_patch(name, role, top_facing):
    if role == "dashboard":
        return "dash_top" if top_facing else "fascia"
    if role == "door_card":
        return "driver_door" if "Driver" in name else "passenger_door"
    return ROLE_PATCHES.get(role)
