"""
lib/generic_report_engine.py — the no-AI, rule-based engine behind the
"Report Generator" screen.

Given a connected dataset (already loaded via data_loader.py) and the local
template registry, this profiles columns by role (numeric / date /
categorical / identifier), picks the best-matching template by a fixed rule
(not AI), and computes real KPI/chart values directly with pandas
aggregations. No DAX, no LLM call, no network I/O — deterministic arithmetic
against the tables it was given, nothing else.

Templates are intentionally decoupled from any specific dataset: a template
is just {sections: [...]} describing which KPI/trend/breakdown sections to
compute, matched against whatever numeric/date/categorical columns are
actually found. Adding a new (or industry-specific) template means adding a
JSON file to templates/ and an entry in templates/index.json — no code
change here.
"""

from __future__ import annotations
import re
from dataclasses import dataclass

import pandas as pd

IDENTIFIER_HINTS = ("id", "key", "code", "uuid", "guid")
_CAMEL_BOUNDARY_RE = re.compile(r"(?<=[a-z0-9])(?=[A-Z])")


@dataclass
class ColumnProfile:
    table: str
    column: str
    role: str  # "numeric" | "date" | "categorical" | "identifier" | "text"
    distinct_count: int


def _looks_like_identifier(name: str) -> bool:
    """Matches OrderId, order_id, CustomerID, Id, etc. — splits camelCase/
    PascalCase and snake_case into tokens and checks whether the *last*
    token is an identifier-style suffix, so "Valid"/"Grid"-type words that
    merely contain "id" as a substring aren't misflagged."""
    spaced = _CAMEL_BOUNDARY_RE.sub("_", name)
    tokens = [t for t in re.split(r"[_\s]+", spaced.lower()) if t]
    return bool(tokens) and tokens[-1] in IDENTIFIER_HINTS


def profile_table(table: str, df: pd.DataFrame) -> list[ColumnProfile]:
    profiles = []
    n = len(df)
    for col in df.columns:
        name = str(col)
        series = df[col]
        distinct = int(series.nunique(dropna=True))

        if _looks_like_identifier(name) and n > 0 and (distinct / n) > 0.9:
            role = "identifier"
        elif pd.api.types.is_datetime64_any_dtype(series) or isinstance(
            series.dtype, pd.PeriodDtype
        ):
            # Already a native date/datetime dtype (the normal case for a real Excel/SQL
            # DATE column read via openpyxl). Classify directly as "date" rather than
            # falling into the numeric check below: pd.to_numeric() silently succeeds on
            # datetime64 (converting each value to epoch nanoseconds) instead of returning
            # NaN, which previously made date columns pass the ">90% numeric" check and get
            # misclassified as "numeric" — producing nonsensical KPIs like "Sum of OrderDate"
            # (actually the sum of nanosecond timestamps).
            role = "date"
        else:
            numeric = pd.to_numeric(series, errors="coerce")
            if numeric.notna().mean() > 0.9 and not _looks_like_identifier(name):
                role = "numeric"
            else:
                dt = pd.to_datetime(series, errors="coerce")
                if dt.notna().mean() > 0.8 and dt.dropna().dt.year.median() > 1990:
                    role = "date"
                elif 1 < distinct <= max(25, int(n * 0.2)):
                    role = "categorical"
                else:
                    role = "identifier" if distinct > 25 else "text"

        profiles.append(ColumnProfile(table=table, column=name, role=role, distinct_count=distinct))
    return profiles


def profile_tables(tables: dict[str, pd.DataFrame]) -> list[ColumnProfile]:
    out: list[ColumnProfile] = []
    for name, df in tables.items():
        out.extend(profile_table(name, df))
    return out


def pick_primary_table(tables: dict[str, pd.DataFrame]) -> str:
    """MVP is single-table: picks the table with the most rows as the report's
    primary source. Multi-table joins are a follow-up story, not this one."""
    return max(tables, key=lambda t: len(tables[t]))


def match_template(templates: list[dict], profiles: list[ColumnProfile]) -> dict | None:
    """Fixed, rule-based matching only — never AI. Registry order is priority:
    the first template whose `requires` counts are satisfied wins."""
    numeric = sum(1 for p in profiles if p.role == "numeric")
    dates = sum(1 for p in profiles if p.role == "date")
    categorical = sum(1 for p in profiles if p.role == "categorical")

    for t in templates:
        req = t.get("requires", {})
        if numeric < req.get("minNumeric", 0):
            continue
        if dates < req.get("minDate", 0):
            continue
        if categorical < req.get("minCategorical", 0):
            continue
        return t
    return None


def build_slicers(df: pd.DataFrame, categorical_cols: list[str], max_columns: int = 5, max_values: int = 50) -> list[dict]:
    """Slicer options are always computed from the FULL (unfiltered) table so
    the available choices never shrink as the user applies a filter."""
    slicers = []
    for col in categorical_cols[:max_columns]:
        values = sorted(df[col].dropna().astype(str).unique().tolist())[:max_values]
        if values:
            slicers.append({"column": col, "values": values})
    return slicers


def apply_filters(
    df: pd.DataFrame, filters: dict[str, str] | None, filterable_cols: list[str], warnings: list[str]
) -> tuple[pd.DataFrame, dict[str, str]]:
    """Exact-match, AND-combined filtering — never a raw query string, only
    column=value pairs checked against the columns we already exposed as
    slicers, so a filter can never reach into a column the caller wasn't
    shown as filterable."""
    applied: dict[str, str] = {}
    if not filters:
        return df, applied
    for col, value in filters.items():
        if col not in filterable_cols:
            warnings.append(f'Ignored filter on "{col}" — not a recognized filterable column.')
            continue
        df = df[df[col].astype(str) == str(value)]
        applied[col] = value
    return df, applied


def build_report(
    template: dict,
    tables: dict[str, pd.DataFrame],
    profiles: list[ColumnProfile],
    filters: dict[str, str] | None = None,
) -> dict:
    warnings: list[str] = []
    primary = pick_primary_table(tables)
    full_df = tables[primary]
    primary_profiles = [p for p in profiles if p.table == primary]

    numeric_cols = [p.column for p in primary_profiles if p.role == "numeric"]
    date_cols = [p.column for p in primary_profiles if p.role == "date"]
    categorical_cols = [p.column for p in primary_profiles if p.role == "categorical"]

    slicers = build_slicers(full_df, categorical_cols)
    df, applied_filters = apply_filters(full_df, filters, categorical_cols, warnings)
    if filters and df.empty:
        warnings.append("No rows match the selected filter(s) — showing an empty report.")

    kpis: list[dict] = []
    charts: list[dict] = []

    for section in template.get("sections", []):
        kind = section.get("kind")

        if kind == "kpi_row":
            # numeric_cols only ever contains columns profiled as role="numeric" — date
            # columns are excluded upstream in profile_table(), so a "sum"/"average" of a
            # date can no longer reach this loop at all.
            for col in numeric_cols[: section.get("max", 4)]:
                series = pd.to_numeric(df[col], errors="coerce").dropna()
                if series.empty:
                    continue
                agg_fns = {"sum": series.sum, "average": series.mean, "min": series.min, "max": series.max}
                for agg in section.get("agg", ["sum"]):
                    fn = agg_fns.get(agg)
                    if fn is None:
                        continue
                    kpis.append({
                        "label": f"{agg.title()} of {col}",
                        "value": round(float(fn()), 2),
                        "column": col,
                        "aggregation": agg,
                    })

        elif kind == "date_kpis":
            # Genuinely valid, date-appropriate measures — never a raw sum/average of a
            # date value. Both are deterministic against the data itself (anchored to the
            # column's own max date, not wall-clock "now"), so re-running against the same
            # file always reproduces the same numbers.
            for col in date_cols[: section.get("max", 1)]:
                dates = pd.to_datetime(df[col], errors="coerce").dropna()
                if dates.empty:
                    continue
                span_days = int((dates.max() - dates.min()).days)
                kpis.append({
                    "label": f"Data Span ({col})",
                    "value": float(span_days),
                    "column": col,
                    "aggregation": "date range (days)",
                })
                window_start = dates.max() - pd.Timedelta(days=30)
                recent_count = int((dates >= window_start).sum())
                kpis.append({
                    "label": f"Last 30 Days ({col})",
                    "value": float(recent_count),
                    "column": col,
                    "aggregation": "trailing 30-day count",
                })

        elif kind == "trend":
            if not date_cols or not numeric_cols:
                warnings.append("No date+numeric column pair found; trend chart skipped.")
                continue
            date_col = date_cols[0]
            value_cols = numeric_cols[: section.get("max", 3)]
            parsed_dates = pd.to_datetime(df[date_col], errors="coerce")

            monthly_by_col: dict[str, pd.Series] = {}
            for value_col in value_cols:
                tmp = pd.DataFrame({
                    "_d": parsed_dates,
                    "_v": pd.to_numeric(df[value_col], errors="coerce"),
                }).dropna(subset=["_d"])
                monthly = tmp.set_index("_d").resample("MS")["_v"].sum()
                if monthly.notna().any():
                    monthly_by_col[value_col] = monthly

            if not monthly_by_col:
                warnings.append(f'No usable rows to build a trend by "{date_col}".')
                continue

            # Align every series to the same set of months (union across all requested
            # value columns) so multi-series charts don't silently misalign x-axis points —
            # a month with no rows for a given measure is a real 0, not a gap.
            all_months = sorted(set().union(*(m.index for m in monthly_by_col.values())))
            title = f"{value_cols[0]} Trend" if len(monthly_by_col) == 1 else "Trend Comparison"
            charts.append({
                "type": "line",
                "title": title,
                "x": [d.strftime("%b %Y") for d in all_months],
                "series": [
                    {
                        "name": col,
                        "values": [round(float(monthly.get(m, 0.0)), 2) for m in all_months],
                    }
                    for col, monthly in monthly_by_col.items()
                ],
            })

        elif kind == "category_counts":
            # Record COUNT per category — distinct from "breakdown" below, which sums a
            # numeric value per category. Useful even when the count and the summed value
            # tell different stories (e.g. many small orders vs a few large ones).
            if not categorical_cols:
                warnings.append("No categorical column found; category count chart skipped.")
                continue
            for cat_col in categorical_cols[: section.get("max", 1)]:
                counts = (
                    df[cat_col].dropna().astype(str).value_counts().sort_values(ascending=False).head(10)
                )
                if counts.empty:
                    continue
                charts.append({
                    "type": "bar",
                    "title": f"Record Count by {cat_col}",
                    "categories": [str(c) for c in counts.index],
                    "series": [{"name": "Count", "values": [int(v) for v in counts.values]}],
                })

        elif kind == "breakdown":
            if not categorical_cols or not numeric_cols:
                warnings.append("No category+numeric column pair found; breakdown chart skipped.")
                continue
            value_col = numeric_cols[0]
            for cat_col in categorical_cols[: section.get("max", 2)]:
                grouped = (
                    df.assign(_v=pd.to_numeric(df[value_col], errors="coerce"))
                    .groupby(cat_col)["_v"].sum()
                    .sort_values(ascending=False)
                    .head(10)
                    .dropna()
                )
                if grouped.empty:
                    continue
                charts.append({
                    "type": "bar",
                    "title": f"{value_col} by {cat_col}",
                    "categories": [str(c) for c in grouped.index],
                    "series": [{"name": value_col, "values": [round(float(v), 2) for v in grouped.values]}],
                })

    return {
        "templateId": template.get("id"),
        "templateName": template.get("name"),
        "primaryTable": primary,
        "kpis": kpis,
        "charts": charts,
        "slicers": slicers,
        "appliedFilters": applied_filters,
        "warnings": warnings,
    }
