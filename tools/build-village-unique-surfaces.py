#!/usr/bin/env python3
"""Pack and paint the shared village close-view atlas without changing geometry."""
import argparse
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from village_unique_surfaces import prepare


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--layout-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    result = prepare(args.validate_only, args.layout_only)
    print("VILLAGE UNIQUE SURFACES OK: " + result["uv_signature"], flush=True)


if __name__ == "__main__":
    main()
