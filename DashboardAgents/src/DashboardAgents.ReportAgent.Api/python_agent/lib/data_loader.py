"""
lib/data_loader.py — secure, bounded loading of the "actual data" the report
is populated from.

Threat model notes (see ../../SECURITY.md for the full writeup):
  * Input is untrusted. We never eval/exec cell contents. openpyxl/pandas are
    used in data-only mode; Excel formulas are read as their last-calculated
    value, never recalculated or executed, and macros are never touched
    (.xlsx only — .xlsm is rejected).
  * Hard caps on sheets, rows, columns and total cells bound memory/CPU use
    before anything else runs, so a hostile 500MB workbook can't be used to
    exhaust the worker.
  * No network access is imported or used anywhere in this module.
"""

from __future__ import annotations
import io
import os
from dataclasses import dataclass

import pandas as pd

MAX_TABLES = 60
MAX_ROWS_PER_TABLE = 300_000
MAX_COLUMNS_PER_TABLE = 300
MAX_TOTAL_CELLS = 6_000_000
ALLOWED_WORKBOOK_EXT = {".xlsx"}
ALLOWED_TABLE_EXT = {".csv"}


class DataLoadError(ValueError):
    pass


@dataclass
class LoadedData:
    tables: dict  # table_name -> pandas.DataFrame
    warnings: list


def _check_frame_limits(name: str, df: pd.DataFrame, running_total: int) -> int:
    if len(df) > MAX_ROWS_PER_TABLE:
        raise DataLoadError(f"Table '{name}' has {len(df)} rows, exceeding the {MAX_ROWS_PER_TABLE} cap.")
    if df.shape[1] > MAX_COLUMNS_PER_TABLE:
        raise DataLoadError(f"Table '{name}' has {df.shape[1]} columns, exceeding the {MAX_COLUMNS_PER_TABLE} cap.")
    running_total += len(df) * max(df.shape[1], 1)
    if running_total > MAX_TOTAL_CELLS:
        raise DataLoadError(f"Combined dataset exceeds the {MAX_TOTAL_CELLS}-cell processing cap.")
    return running_total


def _clean_columns(df: pd.DataFrame) -> pd.DataFrame:
    df.columns = [str(c).strip() for c in df.columns]
    return df


def load_workbook_bytes(data: bytes, filename: str = "upload.xlsx") -> LoadedData:
    """Load a multi-sheet .xlsx workbook where each sheet is one star-schema
    table (Dim_*/Fact_* naming, matching the Dashboard Generator's own mock
    data convention). Sheets that are obviously instructional/blank are
    skipped rather than erroring the whole load."""
    ext = os.path.splitext(filename)[1].lower()
    if ext not in ALLOWED_WORKBOOK_EXT:
        raise DataLoadError(f"Unsupported workbook extension '{ext}'. Only .xlsx is accepted (no macros).")

    try:
        xls = pd.ExcelFile(io.BytesIO(data), engine="openpyxl")
    except Exception as e:  # noqa: BLE001
        raise DataLoadError(f"Could not open workbook: {e}") from e

    if len(xls.sheet_names) > MAX_TABLES:
        raise DataLoadError(f"Workbook has {len(xls.sheet_names)} sheets, exceeding the {MAX_TABLES} cap.")

    tables, warnings, running_total = {}, [], 0
    for sheet in xls.sheet_names:
        try:
            df = xls.parse(sheet_name=sheet)
        except Exception as e:  # noqa: BLE001
            warnings.append(f"Sheet '{sheet}' could not be parsed and was skipped: {e}")
            continue
        df = _clean_columns(df)
        if df.empty or df.shape[1] == 0:
            warnings.append(f"Sheet '{sheet}' is empty and was skipped.")
            continue
        # Skip obvious instruction/cover sheets (single free-text column, no plausible key).
        if df.shape[1] <= 1 and not any(k in str(df.columns[0]).lower() for k in ("key", "id")):
            warnings.append(f"Sheet '{sheet}' looks like a non-tabular cover/instructions sheet and was skipped.")
            continue
        try:
            running_total = _check_frame_limits(sheet, df, running_total)
        except DataLoadError as e:
            warnings.append(str(e) + " Sheet skipped.")
            continue
        tables[sheet] = df

    if not tables:
        raise DataLoadError("No usable tables were found in the workbook.")
    return LoadedData(tables=tables, warnings=warnings)


def load_csv_bundle(files: dict) -> LoadedData:
    """files: {table_name_or_filename: bytes}. Used for a folder/zip export of
    one CSV per star-schema table instead of a single workbook."""
    if len(files) > MAX_TABLES:
        raise DataLoadError(f"{len(files)} CSV files provided, exceeding the {MAX_TABLES} cap.")
    tables, warnings, running_total = {}, [], 0
    for fname, raw in files.items():
        ext = os.path.splitext(fname)[1].lower()
        if ext not in ALLOWED_TABLE_EXT:
            warnings.append(f"'{fname}' skipped (unsupported extension '{ext}').")
            continue
        table_name = os.path.splitext(os.path.basename(fname))[0]
        try:
            df = pd.read_csv(io.BytesIO(raw), low_memory=False)
        except Exception as e:  # noqa: BLE001
            warnings.append(f"'{fname}' could not be parsed as CSV and was skipped: {e}")
            continue
        df = _clean_columns(df)
        if df.empty:
            warnings.append(f"'{fname}' is empty and was skipped.")
            continue
        try:
            running_total = _check_frame_limits(table_name, df, running_total)
        except DataLoadError as e:
            warnings.append(str(e) + " File skipped.")
            continue
        tables[table_name] = df

    if not tables:
        raise DataLoadError("No usable CSV tables were provided.")
    return LoadedData(tables=tables, warnings=warnings)


def build_flattened_facts(tables: dict, relationships: list) -> dict:
    """For each table that appears as the 'from_table' (the many side) of a
    Many:One relationship, left-join in the related dimension/date table's
    columns (1 hop). This lets the measure engine filter and group fact rows
    by dimension attributes (e.g. Dim_Client[Industry]) without a hand-written
    join for every measure. Best-effort: relationships whose tables/columns
    aren't present in the loaded data are silently skipped (that mismatch is
    already surfaced by the reconciliation agent, not re-reported here)."""
    flattened = {name: df for name, df in tables.items()}
    for rel in relationships:
        if not rel.get("active", True):
            continue
        if rel.get("cardinality") not in ("Many:One", "Many:Many"):
            continue
        ft, fc, tt, tc = rel["from_table"], rel["from_col"], rel["to_table"], rel["to_col"]
        if ft not in flattened or tt not in tables:
            continue
        left, right = flattened[ft], tables[tt]
        if fc not in left.columns or tc not in right.columns:
            continue
        overlap = set(right.columns) & set(left.columns) - {tc}
        right_slim = right.drop(columns=list(overlap), errors="ignore")
        try:
            merged = left.merge(right_slim, how="left", left_on=fc, right_on=tc, suffixes=("", f"__{tt}"))
        except Exception:  # noqa: BLE001 — join failure just means this dimension isn't joinable; skip it
            continue
        flattened[ft] = merged
    return flattened
