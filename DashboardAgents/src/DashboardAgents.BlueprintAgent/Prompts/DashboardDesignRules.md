# DashboardDesignRules.md

## Purpose

Defines Power BI dashboard design principles for the Analytics Blueprint Generator.

These rules govern semantic model design, visual selection, page structure, slicer strategy, self-review criteria, quality framework scoring, and governance design.

This file does NOT contain data quality rules, audit rules, performance rules, or data profiling logic.

---

## Scope

These rules apply to:

- Semantic model design (star schema)
- Measure design and DAX patterns
- Visual type selection
- Page structure and layout
- Slicer and filter design
- Drill-through design
- Storytelling and executive clarity
- KPI design and ownership
- Governance design
- Audit readiness assessment
- Quality framework scoring
- Confidence score calibration
- Self-review gate evaluation

These rules do NOT apply to:

- Data quality assessment
- Data profiling or completeness scoring
- Report rendering performance
- Dataset refresh evaluation
- Missing value or duplicate detection

---

## SEMANTIC MODEL DESIGN RULES

### SM-1 — Star Schema Only
Always use a star schema: one or more fact tables surrounded by dimension tables.
Never use snowflake schema unless a hierarchy exceeds three levels and denormalisation is impractical — document the exception.
Never use flat tables or wide tables as the primary reporting layer.

### SM-2 — Grain Definition Is Mandatory
Every fact table must have a defined grain statement.
Format: "One row = one [business event] per [entity] per [time unit]"
Example: "One row = one service delivery session per participant per support item"
A fact table without a defined grain is a design defect.

### SM-3 — Dimension Classification
Every dimension must be classified as either Standard or SCD Type 2.
Standard — attributes are stable and do not change meaningfully over time.
SCD2 — attributes change over time and historical values must be preserved for accurate analysis.
Document the justification for SCD2 classification in the blueprint.

### SM-4 — Conformed Dimensions
Dim_Date must be shared across all fact tables as a conformed dimension.
Dim_Location (or equivalent geographic dimension) must be shared where geography is relevant.
Conformed dimensions enable cross-fact-table analysis without many-to-many issues.

### SM-5 — Role-Playing Date Dimensions
When a fact table has multiple date columns (e.g. submission date and service date), each additional date column must use an inactive relationship to Dim_Date.
Activate via USERELATIONSHIP() in the relevant DAX measures.
Document every role-playing relationship in the blueprint relationships table.

### SM-6 — Relationship Direction
All relationships must be Single direction by default.
Bidirectional relationships must be explicitly justified and documented.
Never use bidirectional relationships to resolve many-to-many — use a bridge table.

### SM-7 — Measure Table
All DAX measures must be hosted in a dedicated _Measures table (an island table with no relationships).
Never embed measures in fact or dimension tables.

### SM-8 — Display Folder Hierarchy
Every measure must belong to a display folder using Domain / Subdomain notation.
Examples: "Revenue / Base", "Revenue / Time Intelligence", "Utilisation / Ratios", "Compliance / Safety"
Single-level folders are not acceptable for production blueprints.

---

## DAX DESIGN RULES

### DAX-1 — DIVIDE() Is Mandatory
Every division operation must use DIVIDE([Numerator], [Denominator], 0).
The raw / operator must never appear in a production measure.
The third DIVIDE() parameter must always be specified — use 0 to suppress blank, or BLANK() explicitly where blank is preferred.

### DAX-2 — YTD Time Intelligence
Every financial and volume KPI must have a YTD measure.
Pattern: TOTALYTD([BaseMeasure], Dim_Date[Date], "[FYEnd]")
The fiscal year end string must match the deployment context (e.g. "30/06" for Australian FY).

### DAX-3 — Prior Year Comparison
Every financial KPI shown on an Executive page must have a prior year equivalent.
Pattern: CALCULATE([BaseMeasure], SAMEPERIODLASTYEAR(Dim_Date[Date]))

### DAX-4 — Dependencies Documented
Complex measures (those referencing other measures) must list their dependencies.
This supports impact analysis when a base measure definition changes.

### DAX-5 — No Hardcoded Values
Avoid hardcoded dollar amounts, dates, or thresholds inside DAX measures.
Use parameters or dimension table lookups for values that may change.

---

## EXECUTIVE DASHBOARD RULES

### ED-1 — Executive Summary Is Always Page 1
Every blueprint must start with an Executive Summary page using the Executive layout.
The Executive Summary answers three questions: What happened? Why did it happen? What action is required?

### ED-2 — KPI Cards First
The top row of every Executive page must contain at least 3 KPI Card or KPI visual types.
These cards establish the headline story before any trend or breakdown visuals appear.

### ED-3 — Executive Storytelling
Every page must have a storytelling_flow description explaining the narrative the page tells.
The narrative must follow: State → Context → Action
Example: "State the current revenue position, contextualise against prior year and budget, identify which regions require action."

### ED-4 — Maximum Visual Density
Maximum 8 visuals per page for analytical pages.
Maximum 6 visuals per page for executive pages.
Pages exceeding these limits must be split into a summary page and a detail drill-through page.

---

## KPI VISUAL SELECTION RULES

| Data Pattern | Recommended Visual | Notes |
|---|---|---|
| Single KPI value | Card | Use for key headline numbers |
| KPI vs target | KPI Visual | Use goal line for target |
| KPI over time | Line Chart | Include prior year line |
| KPI by category | Horizontal Bar | Sort descending by value |
| KPI by category over time | Clustered Column | Add trend line |
| Multiple KPIs across entities | Matrix | Use conditional formatting |
| Ranked entities | Horizontal Bar | Sorted, consider top N filter |
| Geographic distribution | Filled Map or Map | Use when geography is the primary dimension |
| Detailed records | Table | Enable row-level drill |
| Part-to-whole | Donut or Treemap | Avoid pie charts |
| Decomposition | Decomposition Tree | For root cause exploration |
| Process funnel | Funnel Chart | For pipeline or conversion |
| Variance analysis | Waterfall Chart | For budget vs actual bridges |

---

## SLICER DESIGN RULES

### SL-1 — Maximum 5 Slicers Per Page
Pages with more than 5 slicers shift cognitive load to the user. Reduce by using page-level filters for fixed dimensions.

### SL-2 — Slicer Candidates
Recommended slicers: Date (fiscal year, quarter, month), Region, Business Unit, Service Category, Status.
Avoid slicers on high-cardinality columns (e.g. TransactionID, ParticipantID, ClaimID).

### SL-3 — Slicer Synchronisation
Slicers that appear on multiple pages should be synchronised so user selections persist across page navigation.
Document synced: true for each synchronised slicer in the blueprint.

---

## PAGE STRUCTURE RULES

### PS-1 — Mandatory Page Order
| Position | Page Type | Purpose |
|---|---|---|
| Page 1 | Executive Summary (Executive layout) | Headlines for senior leadership |
| Page 2 | Primary Operational Analysis (Analytical or Operational layout) | Core domain performance |
| Page 3 | Secondary Analysis or Trend (Analytical layout) | Trend and contextual detail |
| Page 4+ | Domain-specific pages | As required by business goals |
| Last page(s) | Detail drill-through (Detail layout) | Transaction-level detail |

### PS-2 — Layout Types
Every page must be assigned one of four layouts: Executive, Analytical, Operational, Detail.
The layout type drives visual selection and audience targeting.

### PS-3 — Audience Assignment
Every page must name its intended audience.
Example: "CEO, CFO, Board" or "Operations Manager, Team Leaders" or "Finance Manager, Plan Managers"

---

## DRILL-THROUGH DESIGN RULES

### DT-1 — Drill-Through Required for Summary Pages
Every summary entity must have a drill-through path to a transactional detail page.
Standard patterns:
- Region → Location → Service Delivery Log
- Participant → Plan → Claim Detail
- Program → Grant → Acquittal Detail
- Client → Engagement → Timesheet Detail
- Property → Tenancy → Payment History

### DT-2 — Detail Page Structure
Every drill-through target page must use the Detail layout.
Detail pages must include a back navigation instruction in the blueprint.
Detail pages must be triggered by specific dimension fields (trigger_fields in the blueprint JSON).

---

## KPI DESIGN RULES

### KPI-1 — Mandatory KPI Fields
Every KPI must include all of the following:
- name — descriptive business name
- measure_ref — exact measure name it references
- target_logic — how the target is set (e.g. "Prior year + 10%", "Industry benchmark", "Regulatory minimum")
- thresholds.good — value or range for Green status
- thresholds.warning — value or range for Amber status
- thresholds.critical — value or range for Red status
- owner — named business role (not a generic title)
- cadence — review frequency (Daily, Weekly, Monthly, Quarterly)
- actionability — what the owner should do when the KPI turns Critical
- business_goal_ref — which stated business goal this KPI measures
- data_source_ref — which fact table or measure provides the underlying data

### KPI-2 — No Orphaned KPIs
Every KPI must reference a stated business goal.
KPIs not tied to a business goal will be removed during self-review.

### KPI-3 — KPI Ownership Is Non-Negotiable
Every KPI must have a named owner role.
Generic titles like "Management" or "The Team" are not acceptable.
Named roles: "Operations Manager", "CFO", "Quality and Compliance Manager", "Principal"

### KPI-4 — Actionability Statements
Every KPI must include an actionability statement.
Format: "If this KPI turns Critical, [Owner Role] should [specific action]."
Example: "If Claim Approval Rate turns Critical, the Finance Manager should review the top 5 rejection error codes and resubmit corrected claims within 48 hours."

---

## GOVERNANCE DESIGN RULES

### GOV-1 — Five Governance Roles Required
Every blueprint must define all five governance roles:
1. Data Owner — accountable for data accuracy and fitness for purpose
2. KPI Owner — defines and maintains KPI definitions, targets, and thresholds
3. Report Owner — accountable for report content, distribution, and sign-off
4. Business Steward — day-to-day governance of data definitions and business rules
5. Access Steward — manages user access, RLS, and security reviews

### GOV-2 — Review Cadence
Every blueprint must define a governance review cadence aligned to reporting frequency.
Example: "Monthly — financial KPIs; Weekly — operational KPIs; Quarterly — compliance KPIs"

### GOV-3 — Change Control
Every blueprint must document a change control statement for KPI and measure definition changes.
Example: "KPI definition changes require sign-off from KPI Owner and Report Owner before deployment."

---

## AUDIT READINESS DESIGN RULES

### AR-1 — PII Identification Is Mandatory
Every blueprint must identify all columns containing personally identifiable information.
PII includes: name, date of birth, address, identifier numbers, health information, financial information, contact details.
PII columns must be listed in the format: TableName[ColumnName]

### AR-2 — Sensitivity Label Assignment
Every blueprint must assign a Microsoft Purview sensitivity label.
| Label | When to Use |
|---|---|
| Internal | No PII, no financial data, low risk |
| Confidential | Client data, financial data, staff data |
| Highly Confidential | Health data, participant data, legal matter data |
| Official Sensitive | Government data under ISM classification |

### AR-3 — Source System Traceability
Every fact table must document its source system.
This enables audit tracing from a Power BI figure back to a source transaction.

### AR-4 — Compliance Obligations Documented
Every blueprint must list the industry-specific compliance obligations relevant to the data being reported.
These are design-time obligations — the blueprint does not perform compliance audits.

---

## QUALITY FRAMEWORK SCORING RULES

### QF-1 — Audit Readiness Score
Score is based on the audit readiness checklist (8 items).
Each item is PASS, WARN, or FAIL.
Score = (PASS items / total items) × 100.
Rating bands: Strong (≥85), Satisfactory (≥70), Developing (≥55), Requires Attention (<55).

### QF-2 — Dashboard Quality Score
Score is the average of 9 dimension scores (0–100 each):
executive_clarity, kpi_alignment, business_goal_alignment, visual_density, navigation_structure, drill_through_design, slicer_strategy, storytelling_quality, actionability.
Rating bands: High Quality (≥85), Good (≥70), Developing (≥55), Needs Improvement (<55).

### QF-3 — KPI Quality Score
Score is the average completeness rate across all KPIs evaluated against 6 dimensions:
ownership_defined, target_defined, traceable_to_source, goal_aligned, cadence_appropriate, actionable.
Rating bands: High Quality (≥85), Good (≥70), Developing (≥55), Needs Improvement (<55).

### QF-4 — Semantic Model Quality Score
Score is the average of 8 dimension scores:
star_schema_compliance, fact_dimension_separation, date_intelligence_readiness, conformed_dimensions, scalability, rls_readiness, measure_organisation, business_model_clarity.
Rating bands: Enterprise Grade (≥88), Production Ready (≥76), Developing (≥64), Needs Work (<64).

### QF-5 — Governance Framework Score
Score reflects presence and completeness of governance artefacts.
Rating bands: Mature (≥85), Defined (≥70), Developing (≥55), Ad Hoc (<55).

---

## CONFIDENCE SCORE RULES

### CS-1 — Seven-Dimension Confidence Model
Confidence is weighted across seven dimensions:

| Dimension | Weight | What It Measures |
|---|---|---|
| Industry Confidence | 20% | Certainty of industry detection |
| Capability Confidence | 15% | Alignment of pack capabilities to requirements |
| Goal Confidence | 15% | Specificity and clarity of stated business goals |
| Semantic Model Completeness | 20% | Fact/dimension coverage, DAX patterns, measure count |
| KPI Completeness | 12% | KPI count, ownership, thresholds, actionability |
| Dashboard Completeness | 10% | Page count, layout coverage, drill-through |
| Governance Completeness | 8% | Governance roles, compliance, security |

### CS-2 — Confidence Bands
| Score | Band | Meaning |
|---|---|---|
| 90–100 | Production Ready | Full requirements + schema, confirmed industry, strong governance |
| 75–89 | Strong | Confirmed industry, good requirements, some assumptions |
| 50–74 | Directional | Confirmed industry, partial requirements, significant assumptions |
| 25–49 | Indicative | Uncertain industry or very sparse input |
| 0–24 | Insufficient | Cannot produce a meaningful blueprint — seek clarification |

### CS-3 — Confidence Never Reflects Data Quality
Confidence scores never reflect data quality, data accuracy, data completeness, data validity, or dataset size.
These require access to actual data, which this agent does not have.

---

## SELF-REVIEW GATE RULES

### SR-1 — Nine Gates Required
Every blueprint self-review must evaluate exactly nine gates:

| Gate | Pass Criteria |
|---|---|
| Industry Alignment | Entities and KPIs match the detected industry pack |
| Capability Alignment | Fact tables match the primary capability domain |
| Business Goal Alignment | ≥80% of KPIs reference a specific business goal |
| KPI Coverage | All KPIs have owner, thresholds, actionability, and goal reference |
| Dashboard Quality | Executive page present, KPI cards first, ≤5 slicers, drill-through defined |
| Semantic Model Quality | DIVIDE() throughout, TOTALYTD present, display folders hierarchical |
| Governance Design Completeness | All 5 governance roles defined, review cadence and change control documented |
| Security Design Completeness | PII candidates identified, sensitivity label recommended, RLS design specified |
| Executive Reporting Readiness | Executive questions answered, storytelling flows defined, audience targeted |

### SR-2 — Gate Verdicts
Each gate returns PASS, WARN, or FAIL.
Overall verdict: PASS if all gates PASS. PASS_WITH_NOTES if any gate WARN. REVISE if any gate FAIL.

### SR-3 — Self-Review Scope Is Design Only
Self-review gates evaluate design completeness and design quality only.

The following are never evaluated by self-review:

- Data quality, accuracy, or completeness
- Report rendering performance
- Dataset query performance
- Refresh history or failure rates
- Data reliability or trustworthiness
- Whether RLS has been tested
- Whether compliance has been achieved

### SR-4 — Design Recommendations Replace Auto-Corrections
The self-review produces design recommendations, not auto-corrections.

Correct:
✓ "Consider adding drill-through from the Revenue Summary to the Transaction Detail page."
✓ "Consider a conformed Dim_Date across all fact tables to enable cross-fact time intelligence."
✓ "Consider role-playing date relationships for Fact_Invoices which has three date columns."

Prohibited:
✗ "Auto-corrected: measure 'Revenue' — division operator replaced with DIVIDE()"
✗ "Auto-corrected: column 'ClientID' — data type changed from VARCHAR to INT"
✗ "Auto-corrected: RLS role added for Operations Manager"

---

## DESIGN RECOMMENDATIONS RULES

### DR-1 — Design Recommendations Are Not Corrections
Every recommendation in the blueprint is a design consideration for implementation.
The agent never claims to have corrected, fixed, applied, or implemented anything.

### DR-2 — Recommendation Categories
Design recommendations must belong to one of these categories:

- Semantic Model: star schema improvements, relationship patterns, measure organisation
- KPI Design: threshold calibration, ownership assignment, actionability statements
- Dashboard Design: page structure, visual hierarchy, drill-through paths, storytelling
- Security Design: RLS patterns, sensitivity label guidance, PII candidate identification
- Governance: role assignment guidance, review cadence, change control process

### DR-3 — Correct Recommendation Patterns

| Category | Example Recommendation |
|---|---|
| Semantic Model | Consider conformed dimensions to enable cross-fact analysis |
| Semantic Model | Consider role-playing date tables for facts with multiple date columns |
| Semantic Model | Consider calculation groups for consistent time intelligence |
| KPI Design | Consider adding actionability statements to all KPIs |
| KPI Design | Consider aligning KPI cadence to the reporting frequency |
| Dashboard Design | Consider drill-through from summary entities to transaction detail |
| Dashboard Design | Consider an executive scorecard as a simplified entry point |
| Security Design | Consider a user-to-region mapping table to drive RLS filters |
| Security Design | Consider sensitivity label escalation for pages showing PII |
| Governance | Consider a KPI change management policy |
| Governance | Consider a report certification process before executive distribution |

### DR-4 — Prohibited Recommendation Patterns

Never produce recommendations that claim the agent has implemented, verified, or corrected anything:

✗ "Added DIVIDE() to prevent divide-by-zero errors"
✗ "Fixed column naming to match Power BI conventions"
✗ "RLS role for Finance Manager was added"
✗ "Sensitivity label was set to Confidential"
✗ "Star schema compliance was achieved"
✗ "Data quality check passed"
