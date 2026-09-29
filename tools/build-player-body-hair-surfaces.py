#!/usr/bin/env python3
"""Publish/verify Hero V2 skin and hair data maps without changing colour atlases."""
import argparse
import player_body_hair_surfaces


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Verify all deterministic map/report bytes without writes")
    arguments = parser.parse_args()
    player_body_hair_surfaces.publish_maps(arguments.check)
