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


def build_report(template: dict, tables: dict[str, pd.DataFrame], profiles: list[ColumnProfile]) -> dict:
    warnings: list[str] = []
    primary = pick_primary_table(tables)
    df = tables[primary]
    primary_profiles = [p for p in profiles if p.table == primary]

    numeric_cols = [p.column for p in primary_profiles if p.role == "numeric"]
    date_cols = [p.column for p in primary_profiles if p.role == "date"]
    categorical_cols = [p.column for p in primary_profiles if p.role == "categorical"]

    kpis: list[dict] = []
    charts: list[dict] = []

    for section in template.get("sections", []):
        kind = section.get("kind")

        if kind == "kpi_row":
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

        elif kind == "trend":
            if not date_cols or not numeric_cols:
                warnings.append("No date+numeric column pair found; trend chart skipped.")
                continue
            date_col, value_col = date_cols[0], numeric_cols[0]
            tmp = df.assign(
                _d=pd.to_datetime(df[date_col], errors="coerce"),
                _v=pd.to_numeric(df[value_col], errors="coerce"),
            ).dropna(subset=["_d"])
            monthly = tmp.set_index("_d").resample("MS")["_v"].sum().dropna()
            if monthly.empty:
                warnings.append(f'No usable rows to build a trend for "{value_col}" by "{date_col}".')
                continue
            charts.append({
                "type": "line",
                "title": f"{value_col} Trend",
                "x": [d.strftime("%b %Y") for d in monthly.index],
                "series": [{"name": value_col, "values": [round(float(v), 2) for v in monthly.values]}],
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
        "warnings": warnings,
    }
