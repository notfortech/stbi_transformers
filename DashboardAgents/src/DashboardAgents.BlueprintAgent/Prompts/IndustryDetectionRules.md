# IndustryDetectionRules.md

## Purpose

Defines the rules, signals, scoring logic, and fallback behaviour for industry detection in the Analytics Blueprint Generator.

Industry detection happens at Step 1 of the agent workflow. All subsequent steps — capability mapping, semantic model design, KPI framework, dashboard architecture — depend on a correct industry detection result.

This file is used by both the local JavaScript detection function and the AI model system prompt.

---

## Detection Architecture

The detection system uses two layers:

**Layer 1 — Signal Scoring (JavaScript)**
Runs client-side before the AI call. Scores each industry pack by counting signal matches in the input text. Multi-word signals score higher (3 points vs 1 point for single-word signals). Returns a ranked list of candidate industries and a confidence score.

**Layer 2 — AI Confirmation (System Prompt)**
The AI receives the detected industry and top candidates. It confirms, overrides, or escalates to clarification based on the full semantic context of the requirements — not just keyword matching.

---

## Signal Scoring Rules

### SC-1 — Multi-Word Signal Bonus
Single-word signals: 1 point per match
Multi-word signals (2+ words): 3 points per match
Rationale: multi-word signals are more specific and less likely to match by accident.

### SC-2 — Confidence Calibration
Raw score is normalised against the maximum achievable score for the top-matched pack.
Minimum confidence floor: 40 (to avoid 0% on partial matches).
Maximum confidence ceiling: 97 (reserve 98–100 for explicit user selection).

### SC-3 — Explicit Selection Override
When the user selects an industry from the dropdown or chip, confidence is set to 97 regardless of signal score.
signals_matched is set to: ["Industry explicitly selected by user"]

### SC-4 — Low Confidence Escalation
If the top candidate confidence is below 70 after signal scoring:
- Return top 3 candidate industries with scores
- Do NOT proceed to blueprint generation
- Ask one clarifying question
- Wait for user response or industry selection

---

## Industry Signal Lists

### NDIS
Primary signals (high specificity):
participant, ndis plan, support worker, sil, billable hours, proda, support category, plan utilisation, schads, registered provider, support coordination, plan manager, ndia, participant funding, service agreement, ndis price, worker screening, supported independent living, capacity building, disability support, sda, early childhood, plan review, myplace

Detection confidence: HIGH — these terms are domain-exclusive

---

### Government
Primary signals (high specificity):
appropriation, program outcome, grants, acquittal, fte establishment, departmental, ministerial, anao, portfolio, aps, ses, local government, budget paper, senate estimates, public service, grant round, pgpa, cost centre, budget variance, kpi achievement, annual report, procurement, austender, corporate plan, myefo

Detection confidence: HIGH — APS, ANAO, PGPA, AusTender are government-exclusive

---

### Professional Services
Primary signals:
billable hours, utilisation, timesheets, engagements, wip, realisation, recovery rate, charge-out rate, bill rate, write-off, clients, consultants, practice area, service line, retainer, advisory, consulting, partners, revenue per consultant, debtors, work in progress, fee earner, proposal, pipeline, engagement letter

Detection confidence: MEDIUM — some terms (clients, consultants) also appear in other industries. Confirm with multi-word signals.

---

### Property Management
Primary signals:
property management, landlord, tenant, tenancy, rent roll, arrears, vacancy, letting, maintenance, inspection, routine inspection, property manager, disbursement, trust account, lease, bond, portfolio, occupancy, days vacant, propertyme, property tree, console, rest professional, rockend

Detection confidence: HIGH — trust account, rent roll, tenancy are property-exclusive

---

### Real Estate Sales
Primary signals (distinguish from Property Management):
real estate, listings, vendor, buyer, commission, auction, clearance rate, days on market, exchange, settlement, agent performance, appraisal, open home, rea, domain, private treaty, under offer, sold, withdrawn, passed in, real estate agency, property sales

Detection confidence: HIGH when sales-specific signals (auction clearance, exchange, settlement) appear
Overlap risk: Real Estate and Property Management overlap — use sales-specific signals to distinguish

---

### Healthcare
Primary signals:
patient, admission, discharge, bed, ward, ed, emergency department, length of stay, alos, drg, mbs, medicare, ahpra, nurse, doctor, specialist, theatre, waitlist, triage, inpatient, outpatient, readmission, clinical, hospital, clinic, gp, bulk billing

Detection confidence: HIGH — AHPRA, DRG, MBS, ALOS are healthcare-exclusive

---

### Aged Care
Primary signals:
aged care, residential care, nursing home, racf, home care package, an-acc, acfi, care minutes, rn ratio, acqsc, resident, rad, dap, means-tested fee, chsp, hcp, level 1 2 3 4, restrictive practices, my aged care

Detection confidence: HIGH — AN-ACC, RACF, ACQSC are aged care-exclusive
Overlap risk: Aged Care and Healthcare share some clinical terms — AN-ACC, RACF, care minutes distinguish aged care

---

### NDIS vs Aged Care — Disambiguation Rule
If both NDIS and Aged Care signals appear in the same requirements:
- NDIS signals: participant, support worker, plan, NDIA, PRODA
- Aged Care signals: resident, AN-ACC, RACF, care minutes, RAD, DAP
- Whichever has higher signal score wins
- If tied, ask the user to confirm

---

### Retail
Primary signals:
retail, store, pos, product, sku, inventory, margin, same-store sales, shrinkage, category, merchandise, loyalty, basket, checkout, e-commerce, shopify, click-and-collect, returns, supplier

Detection confidence: MEDIUM — retail terms are common. Confirm with POS, SKU, or store-specific signals.
Default prevention: NEVER default to Retail. Only select when signals clearly indicate it.

---

### Manufacturing
Primary signals:
production, manufacturing, oee, throughput, work order, shift, machine, downtime, scrap, defect, bom, production line, gmp, iso 9001, quality control, first pass yield, on-time delivery, mes, erp

Detection confidence: HIGH — OEE, BOM, first pass yield are manufacturing-exclusive

---

### Human Resources
Primary signals:
headcount, attrition, turnover, recruitment, time to hire, hr, people, workforce, employee, engagement, performance review, learning, fte, hris, payroll, leave, absenteeism, diversity, gender pay gap

Detection confidence: MEDIUM — many HR terms are generic. Require multiple signals or HRIS-specific language.
Overlap risk: HR signals appear in every industry that has employees. Look for HR as the primary focus, not a secondary data element.

---

### Education
Primary signals:
students, enrolments, attendance, course, teacher, campus, naplan, atar, rto, tafe, university, eftsl, commencement, completion, pass rate, asqa, teqsa, training package, vet, fee-free tafe

Detection confidence: HIGH — NAPLAN, EFTSL, ASQA, RTO are education-exclusive

---

### Finance (Broking / Advisory)
Primary signals:
mortgage broking, financial planning, trail commission, upfront commission, fum, funds under management, settlement, loan, lender, afsl, asic, clawback, adviser, broker, soa, review, superannuation

Detection confidence: HIGH — trail commission, AFSL, FUM, clawback are finance-exclusive
Overlap risk: Finance terms appear in all industries at the accounting level. Focus on advisory/broking signals.

---

### Construction
Primary signals:
construction, builder, contractor, project, cost code, progress claim, variation, subcontractor, retention, practical completion, pc, defects, program, schedule, preliminaries, efc, cost to complete, tendering, qbcc, civil

Detection confidence: HIGH — progress claim, variation, subcontractor, retention are construction-exclusive

---

## Fallback and Ambiguity Rules

### FB-1 — No Match Fallback
If no industry scores above 20 signal points, return confidence = 0 and ask:
> "I could not detect your industry from the requirements. Please describe what your organisation does, or select an industry from the list."

Do NOT fall back to Retail or any default industry.

### FB-2 — Close Score Tie
If the top two candidates are within 5 signal points of each other, present both as candidates and ask the user to confirm.

### FB-3 — Explicit Override Always Wins
User-selected industry always overrides detection. Set confidence to 97. Do not re-detect.

### FB-4 — AI Semantic Override
The AI model may override the Layer 1 signal score based on semantic understanding of the full requirements.
If the AI overrides, it must document the reason in detection.signals_matched.
Example: ["AI semantic override: requirements describe care minute obligations and AN-ACC subsidies — Aged Care selected over Healthcare"]

---

## Tier Classification

Tiers classify the depth and completeness of the knowledge pack for that industry.

| Tier | Meaning |
|---|---|
| 1 | Full knowledge pack — fact tables, dimensions, measures, KPIs, pages, security, governance all defined |
| 2 | Partial knowledge pack — capabilities and KPIs defined, fact/dimension detail requires AI generation |
| 3 | Minimal pack — detection signals and capabilities only, full schema generated by AI |

Current Tier 1 packs: NDIS, Government, Professional Services, Property Management
Current Tier 2 packs: Healthcare, Aged Care, Real Estate, Retail, Manufacturing, Education
Current Tier 3 packs: Finance, Construction, HR, Marketing, Logistics, Insurance, Hospitality

For Tier 2 and 3 packs, the AI model generates the full schema based on industry signals and capability mappings. Confidence is capped at 80 for Tier 2 and 70 for Tier 3 unless explicit schema is provided.

---

## Detection Output Format

The detection result passed to the blueprint must include:

```json
{
  "industry": "string — confirmed industry name",
  "confidence": "number 0-97",
  "tier": "number 1-3",
  "signals_matched": ["string — top signals that drove detection"],
  "pack_applied": "string — pack code used",
  "capability_domain": "string — primary domain code if detected",
  "domain_confidence": "number 0-100"
}
```

---

## Domain Detection Within Industry

After industry is confirmed, the agent detects the primary capability domain.

Domain detection uses the business goal statement, not industry signals.

| Domain Code | Trigger Language |
|---|---|
| OPS | deliver, efficiency, scheduling, throughput, process, operations, workflow |
| FIN | revenue, margin, cost, budget, funding, claims, billing, financial sustainability |
| CX | outcomes, satisfaction, NPS, experience, churn, retention, participant goals |
| REV | growth, pipeline, sales, commission, listings, conversions, new business |
| COMP | compliance, incident, regulatory, audit, obligations, risk, reportable |
| WFM | workforce, utilisation, rostering, capacity, overtime, staff, employees |

If multiple domains score equally, select the one with the most specific trigger language in the requirements.
Document domain confidence in the detection output.
