# DashboardBlueprintSchema.md

## Purpose

Defines the canonical JSON output format for all dashboard blueprints produced by the Analytics Blueprint Generator.

Every generated blueprint must conform exactly to this schema.

This schema is the contract between the Blueprint Generator and any downstream consumer — including the UI renderer, the future Audit Agent, and any export or integration layer.

---

## What Is New In This Version

This schema adds five quality framework sections, a governance block, an expected targets block for Audit Agent compatibility, and a redesigned self-review with nine gates replacing the previous six-gate structure.

The confidence model is now weighted across seven dimensions instead of being a single inferred score.

---

## Complete Schema

```json
{
  "meta": {
    "title": "string — dashboard title",
    "industry": "string — confirmed industry name",
    "capability_domain": "string — primary domain code: OPS | FIN | CX | REV | COMP | WFM",
    "business_goal": "string — primary business goal extracted from requirements",
    "primary_audience": "string — intended audience for the executive summary",
    "fiscal_year_start": "string — July | January | April | October",
    "fiscal_year_end": "string — 30/06 | 31/12 | 31/03 | 30/09",
    "currency": "string — AUD | USD | GBP | EUR",
    "refresh_cadence": "string — Daily | Hourly | Weekly | Real-time",
    "generated_at": "string — ISO timestamp"
  },

  "detection": {
    "industry": "string",
    "confidence": "number 0-100",
    "tier": "number 1-3",
    "signals_matched": ["string"],
    "pack_applied": "string — pack code",
    "capability_domain": "string — primary domain code",
    "domain_confidence": "number 0-100"
  },

  "capabilities": ["string"],

  "data_model": {
    "fact_tables": [
      {
        "name": "string — e.g. Fact_ServiceDelivery",
        "grain": "string — one row = one [event] per [entity] per [time]",
        "source": "string — source system name",
        "columns": [
          {
            "name": "string",
            "type": "string — INT | DECIMAL | VARCHAR | DATE | BIT",
            "description": "string"
          }
        ]
      }
    ],
    "dimension_tables": [
      {
        "name": "string — e.g. Dim_Participant",
        "type": "string — Standard | SCD2",
        "scd_justification": "string — required when type is SCD2",
        "hierarchies": ["string — e.g. State > Region > Suburb > Participant"],
        "key_columns": ["string"]
      }
    ],
    "relationships": [
      {
        "from": "string — TableName[ColumnName]",
        "to": "string — TableName[ColumnName]",
        "cardinality": "string — Many:One | One:One | Many:Many",
        "direction": "string — Single | Both",
        "active": "boolean",
        "notes": "string — required for inactive and bidirectional relationships"
      }
    ],
    "date_table": {
      "name": "string — e.g. Dim_Date",
      "spine": "string — e.g. 2018-07-01 to 2030-06-30",
      "fiscal_offset": "number — months offset from January",
      "key_columns": ["string"]
    }
  },

  "measures": [
    {
      "name": "string",
      "table": "string — always _Measures",
      "format": "string — e.g. $#,##0 | 0.0% | #,##0",
      "dax": "string — complete DAX expression",
      "dependencies": ["string — measure names referenced"],
      "display_folder": "string — Domain / Subdomain format required",
      "description": "string — plain English description",
      "business_goal_ref": "string — which business goal this measure serves"
    }
  ],

  "kpis": [
    {
      "name": "string",
      "measure_ref": "string — exact measure name",
      "target_logic": "string — how the target is set",
      "thresholds": {
        "good": "string — value or range for Green",
        "warning": "string — value or range for Amber",
        "critical": "string — value or range for Red"
      },
      "owner": "string — named business role",
      "cadence": "string — Daily | Weekly | Monthly | Quarterly",
      "actionability": "string — what owner does when KPI turns Critical",
      "business_goal_ref": "string — which business goal this KPI measures",
      "data_source_ref": "string — fact table or measure providing underlying data"
    }
  ],

  "pages": [
    {
      "name": "string",
      "purpose": "string — what business question this page answers",
      "audience": "string — named roles who use this page",
      "layout": "string — Executive | Analytical | Operational | Detail",
      "storytelling_flow": "string — what happened > why > what action narrative",
      "slicers": [
        {
          "field": "string — Dimension[Column]",
          "type": "string — Dropdown | Slicer | Date Range | Tile",
          "synced": "boolean — true if synchronised across pages"
        }
      ],
      "visuals": [
        {
          "type": "string — Card | KPI | Line | Bar | Column | Matrix | Table | Map | Gauge | Waterfall | Funnel | Treemap | Scatter | Decomp Tree | Donut | Ribbon",
          "title": "string",
          "position": "string — Row N Col N",
          "measures": ["string — measure names used"],
          "notes": "string — design notes"
        }
      ],
      "drill_through": {
        "target_page": "string — name of detail page",
        "trigger_fields": ["string — Dimension[Column] that triggers the drill"]
      }
    }
  ],

  "executive_questions": ["string — minimum 8 questions"],

  "security": {
    "rls_required": "boolean",
    "roles": [
      {
        "name": "string — role name",
        "filter_table": "string — dimension table to filter",
        "filter_dax": "string — DAX filter expression using USERPRINCIPALNAME()",
        "business_owner": "string — named role accountable for this access definition",
        "access_level": "string — Read | Read + Export | Admin"
      }
    ],
    "sensitivity_label": "string — Internal | Confidential | Highly Confidential | Official Sensitive",
    "pii_columns": ["string — TableName[ColumnName] format"],
    "compliance_obligations": ["string — regulatory obligations relevant to this data"],
    "data_retention_notes": ["string — recommended retention periods"],
    "audit_trail_requirements": ["string — audit log and traceability requirements"],
    "notes": ["string — additional governance and security notes"]
  },

  "governance": {
    "data_owner": "string — named role",
    "kpi_owner": "string — named role or roles by domain",
    "report_owner": "string — named role",
    "business_steward": "string — named role",
    "access_steward": "string — named role",
    "review_cadence": "string — frequency per KPI domain",
    "change_control": "string — who approves KPI and measure definition changes",
    "roles_and_responsibilities": [
      {
        "role": "string",
        "responsibility": "string",
        "named_owner": "string — named role or individual"
      }
    ]
  },

  "semantic_notes": ["string"],

  "quality_frameworks": {

    "audit_readiness": {
      "score": "number 0-100",
      "rating": "string — Strong | Satisfactory | Developing | Requires Attention",
      "strengths": ["string"],
      "risks": ["string"],
      "missing_requirements": ["string"],
      "checklist": [
        {
          "item": "string — what is being checked",
          "status": "string — PASS | WARN | FAIL",
          "evidence": "string — what evidence exists in the blueprint",
          "priority": "string — Critical | High | Medium | Low"
        }
      ]
    },

    "dashboard_quality": {
      "score": "number 0-100",
      "rating": "string — High Quality | Good | Developing | Needs Improvement",
      "dimensions": {
        "executive_clarity":       { "score": "number", "notes": "string" },
        "kpi_alignment":           { "score": "number", "notes": "string" },
        "business_goal_alignment": { "score": "number", "notes": "string" },
        "visual_density":          { "score": "number", "notes": "string" },
        "navigation_structure":    { "score": "number", "notes": "string" },
        "drill_through_design":    { "score": "number", "notes": "string" },
        "slicer_strategy":         { "score": "number", "notes": "string" },
        "storytelling_quality":    { "score": "number", "notes": "string" },
        "actionability":           { "score": "number", "notes": "string" }
      },
      "strengths": ["string"],
      "risks": ["string"],
      "recommendations": ["string"]
    },

    "kpi_quality": {
      "score": "number 0-100",
      "rating": "string — High Quality | Good | Developing | Needs Improvement",
      "kpi_assessments": [
        {
          "kpi_name": "string",
          "ownership_defined": "boolean",
          "business_relevant": "boolean",
          "actionable": "boolean",
          "target_defined": "boolean",
          "traceable_to_source": "boolean",
          "goal_aligned": "boolean",
          "cadence_appropriate": "boolean",
          "risk": "string — risk statement or Low risk"
        }
      ],
      "coverage_assessment": "string — summary of KPI coverage across business goals",
      "missing_ownership": ["string — KPI names missing owners"],
      "missing_targets": ["string — KPI names missing target logic"],
      "recommendations": ["string"]
    },

    "semantic_model_quality": {
      "score": "number 0-100",
      "rating": "string — Enterprise Grade | Production Ready | Developing | Needs Work",
      "dimensions": {
        "star_schema_compliance":    { "score": "number", "notes": "string" },
        "fact_dimension_separation": { "score": "number", "notes": "string" },
        "date_intelligence_readiness": { "score": "number", "notes": "string" },
        "conformed_dimensions":      { "score": "number", "notes": "string" },
        "scalability":               { "score": "number", "notes": "string" },
        "rls_readiness":             { "score": "number", "notes": "string" },
        "measure_organisation":      { "score": "number", "notes": "string" },
        "business_model_clarity":    { "score": "number", "notes": "string" }
      },
      "strengths": ["string"],
      "risks": ["string"],
      "recommendations": ["string"]
    },

    "governance_framework": {
      "score": "number 0-100",
      "rating": "string — Mature | Defined | Developing | Ad Hoc",
      "industry_obligations": ["string — regulatory and compliance obligations"],
      "compliance_controls": ["string — recommended controls"],
      "data_stewardship": [
        {
          "domain": "string — data domain name",
          "owner": "string — named role",
          "responsibility": "string"
        }
      ],
      "recommended_policies": ["string"],
      "gaps": ["string — governance gaps requiring client input"]
    }
  },

  "expected_targets": {
    "description": "string — purpose statement for Audit Agent use",
    "fact_tables": ["string — expected fact table names"],
    "dimension_tables": ["string — expected dimension table names"],
    "kpi_names": ["string — expected KPI names"],
    "page_names": ["string — expected page names"],
    "security_roles": ["string — expected RLS role names"],
    "pii_columns": ["string — expected PII columns"],
    "measure_count_minimum": "number",
    "kpi_count_minimum": "number",
    "page_count_minimum": "number",
    "rls_role_count_minimum": "number",
    "compliance_checks": [
      {
        "check": "string — what the Audit Agent should verify",
        "expected": "string — expected value or true/false",
        "category": "string — DAX Quality | Star Schema | Security | Measure Organisation | Time Intelligence"
      }
    ]
  },

  "self_review": {
    "gates": [
      {
        "gate_name": "string — one of the 9 gate names",
        "status": "string — PASS | WARN | FAIL",
        "score": "number 0-100",
        "findings": ["string — specific findings"],
        "recommendations": ["string — how to resolve findings"]
      }
    ],
    "overall_verdict": "string — PASS | PASS_WITH_NOTES | REVISE",
    "composite_score": "number — average of all gate scores",
    "design_recommendations": [
      {
        "category": "string — Semantic Model | KPI Design | Dashboard Design | Security Design | Governance",
        "recommendation": "string — specific design recommendation",
        "rationale": "string — why this is recommended",
        "priority": "string — High | Medium | Low"
      }
    ],
    "assumptions": [
      "string — design assumption (never a data quality claim)"
    ],
    "design_risks": [
      {
        "risk": "string — design or implementation risk",
        "mitigation": "string — recommended action to mitigate",
        "category": "string — Data Integration | Security | Governance | Calculation | Architecture"
      }
    ],
    "implementation_gaps": [
      "string — what information is missing that would be needed for implementation"
    ],
    "warnings": ["string — non-blocking issues requiring attention"],
    "implementation_risks": ["string — risks that could affect production deployment"]
  },

  "confidence": {
    "score": "number 0-100 — weighted composite",
    "band": "string — Production Ready | Strong | Directional | Indicative | Insufficient",
    "basis": "string — explanation of how score was derived",
    "dimension_scores": {
      "industry_confidence":         "number 0-100",
      "capability_confidence":       "number 0-100",
      "goal_confidence":             "number 0-100",
      "semantic_model_completeness": "number 0-100",
      "kpi_completeness":            "number 0-100",
      "dashboard_completeness":      "number 0-100",
      "governance_completeness":     "number 0-100"
    },
    "assumptions": ["string"],
    "gaps": ["string"]
  }
}
```

---

## Mandatory Fields

Every blueprint must include all of the following. Missing any field is a schema violation.

| Section | Mandatory Fields |
|---|---|
| meta | All fields |
| detection | industry, confidence, pack_applied |
| capabilities | At least 5 items |
| data_model | At least 2 fact tables, at least 3 dimension tables, at least 5 relationships, date_table |
| measures | At least 10 measures |
| kpis | At least 6 KPIs |
| pages | At least 5 pages including one Executive layout |
| executive_questions | At least 8 questions |
| security | All fields — rls_required, sensitivity_label, pii_columns |
| governance | All 5 named roles, review_cadence, change_control |
| quality_frameworks | All 5 frameworks — audit_readiness, dashboard_quality, kpi_quality, semantic_model_quality, governance_framework |
| expected_targets | All fields — for Audit Agent compatibility |
| self_review | All 9 gates, overall_verdict, composite_score, design_recommendations, assumptions, design_risks, implementation_gaps |
| confidence | score, band, basis, all 7 dimension_scores |

---

## Prohibited Content

The blueprint must never contain any of the following. These statements are impossible without access to actual data or deployed reports:

**Data Quality (never assess without actual data):**
- Data quality findings or assessments
- Missing value counts or percentages
- Duplicate record analysis or counts
- Data accuracy percentages or ratings
- Data reliability statements
- Data completeness scores
- Statements that data has been cleaned or validated

**Performance (never assess without deployed reports):**
- Report rendering performance benchmarks
- Model query performance metrics
- Dataset refresh failure reports
- Performance bottleneck findings
- Slow report findings

**Audit-Style Claims (never claim without testing):**
- Auto-corrections applied
- RLS has been verified or tested
- Security is complete
- Compliance has been achieved
- Controls are effective
- Data types have been fixed
- Columns have been standardised

**Prohibited response to requests for the above:**

> "This agent produces analytics design blueprints and architecture recommendations. Data quality assessment, performance profiling, and compliance auditing require access to actual deployed systems — which are outside the scope of this tool."

---

## Nine Self-Review Gates

The self_review.gates array must contain exactly these nine gates in this order:

| # | Gate Name | What It Evaluates |
|---|---|---|
| 1 | Industry Alignment | Entities and KPIs match the detected industry pack |
| 2 | Capability Alignment | Fact tables match the primary capability domain |
| 3 | Business Goal Alignment | ≥80% of KPIs reference a specific business goal |
| 4 | KPI Coverage | All KPIs have owner, thresholds, actionability, and goal reference |
| 5 | Dashboard Quality | Executive page present, KPI cards first, ≤5 slicers, drill-through defined |
| 6 | Semantic Model Quality | DIVIDE() throughout, TOTALYTD present, display folders hierarchical |
| 7 | Governance Design Completeness | All 5 governance roles defined, review cadence and change control documented |
| 8 | Audit Readiness | PII identified, sensitivity label assigned, RLS configured |
| 9 | Security Readiness | RLS filter uses USERPRINCIPALNAME(), matches declared requirement |

---

## Confidence Dimension Weights

| Dimension | Weight |
|---|---|
| industry_confidence | 20% |
| capability_confidence | 15% |
| goal_confidence | 15% |
| semantic_model_completeness | 20% |
| kpi_completeness | 12% |
| dashboard_completeness | 10% |
| governance_completeness | 8% |

Confidence scores never reflect data quality, data validity, data completeness, or dataset size.
