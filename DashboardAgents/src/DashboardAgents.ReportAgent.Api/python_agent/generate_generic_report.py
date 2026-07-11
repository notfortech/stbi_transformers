#!/usr/bin/env python3
"""
generate_generic_report.py — CLI entry point for the "Report Generator"
screen's deterministic engine.

No AI, no network I/O. Takes a connected dataset (an .xlsx workbook or a
directory of CSV files) and the local template registry, profiles columns,
matches a template by a fixed rule, computes real values, and writes a JSON
result the .NET wrapper hands back to the caller unmodified.

Usage:
    python3 generate_generic_report.py --workbook data.xlsx --templates-dir templates -o result.json
    python3 generate_generic_report.py --csv-dir ./tables --templates-dir templates --template-id generic-overview -o result.json
"""

from __future__ import annotations
import argparse
import glob
import json
import os
import sys

from lib import data_loader
from lib import security
from lib.generic_report_engine import profile_tables, match_template, build_report

MAX_TEMPLATE_INDEX_BYTES = 1 * 1024 * 1024


def load_templates(templates_dir: str) -> list[dict]:
    index_path = os.path.join(templates_dir, "index.json")
    if not os.path.exists(index_path):
        raise ValueError(f"Template registry not found at {index_path}.")
    if os.path.getsize(index_path) > MAX_TEMPLATE_INDEX_BYTES:
        raise ValueError("Template index exceeds the size cap.")
    with open(index_path, "r", encoding="utf-8") as fh:
        entries = json.load(fh)

    templates = []
    for entry in entries:
        resolved = security.assert_no_path_traversal(entry["file"], templates_dir)
        with open(resolved, "r", encoding="utf-8") as fh:
            spec = json.load(fh)
        spec["id"] = entry.get("id")
        spec["name"] = entry.get("name")
        spec["industry"] = entry.get("industry")
        spec.setdefault("requires", entry.get("requires", {}))
        templates.append(spec)
    return templates


def load_data(args) -> data_loader.LoadedData:
    if args.workbook:
        with open(args.workbook, "rb") as fh:
            raw = fh.read()
        return data_loader.load_workbook_bytes(raw, os.path.basename(args.workbook))
    if args.csv_dir:
        files = {}
        for path in sorted(glob.glob(os.path.join(args.csv_dir, "*.csv"))):
            with open(path, "rb") as fh:
                files[os.path.basename(path)] = fh.read()
        return data_loader.load_csv_bundle(files)
    raise ValueError("Provide --workbook or --csv-dir.")


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate a deterministic, no-AI report from a standard template.")
    parser.add_argument("--workbook", help="Path to a .xlsx workbook")
    parser.add_argument("--csv-dir", help="Directory of CSV files, as an alternative to --workbook")
    parser.add_argument("--templates-dir", required=True, help="Path to the template registry directory")
    parser.add_argument("--template-id", help="Force a specific template; default is the best rule-based match")
    parser.add_argument("-o", "--output", required=True, help="Write JSON result here")
    parser.add_argument("--timeout", type=int, default=90, help="Hard wall-clock timeout in seconds")
    parser.add_argument("--max-memory-mb", type=int, default=1536)
    args = parser.parse_args()

    security.install_alarm_timeout(args.timeout)
    security.apply_resource_limits(max_cpu_seconds=args.timeout, max_memory_mb=args.max_memory_mb)

    try:
        templates = load_templates(args.templates_dir)
        loaded = load_data(args)
        profiles = profile_tables(loaded.tables)

        if args.template_id:
            template = next((t for t in templates if t.get("id") == args.template_id), None)
            if template is None:
                raise ValueError(f"Unknown template id '{args.template_id}'.")
        else:
            template = match_template(templates, profiles)
        if template is None:
            raise ValueError("No template in the registry matches this dataset's column shape.")

        result = build_report(template, loaded.tables, profiles)
        result["warnings"] = loaded.warnings + result["warnings"]
    except (ValueError, data_loader.DataLoadError, json.JSONDecodeError) as e:
        print(f"Error: {e}", file=sys.stderr)
        return 2
    except Exception as e:  # noqa: BLE001 — never leak a raw traceback to an API caller
        print(f"Internal error while generating the report: {e}", file=sys.stderr)
        return 1

    with open(args.output, "w", encoding="utf-8") as fh:
        json.dump(result, fh, indent=2)
    print(f"Report written to {args.output} ({len(result['warnings'])} notes logged).", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
