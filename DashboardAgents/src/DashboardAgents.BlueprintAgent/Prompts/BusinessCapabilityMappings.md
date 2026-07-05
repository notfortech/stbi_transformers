# BusinessCapabilityMappings.md

## Purpose

Translate business goals into capability packs.

Each capability pack defines exactly what to include in the semantic model, KPI framework, and dashboard when that capability is selected.

The agent loads ONLY the capability packs that match the detected primary and secondary domains.

Loading all packs for an industry regardless of the business goal is the primary cause of generic, overlapping blueprints.

---

## Capability Domain Reference

| Code | Domain | Primary Focus |
|---|---|---|
| OPS | Operations | Service delivery, scheduling, workflow, throughput, activity volumes |
| FIN | Finance | Revenue, margin, cost, funding, claims, budget, billing |
| CX | Customer / Participant Outcomes | Experience, satisfaction, outcomes, churn, NPS, plan achievement |
| REV | Sales / Revenue Growth | Pipeline, commissions, listings, conversions, new business |
| COMP | Compliance / Risk | Incidents, audits, regulatory obligations, reportable events |
| WFM | Workforce | Utilisation, rostering, capacity, overtime, workforce cost |

---

## How to Use This File

Step 5 of the workflow requires the agent to:

1. Identify the primary domain (one only)
2. Identify secondary domains (maximum two)
3. Load the primary domain pack fully
4. Load secondary domain packs for entities and KPIs only
5. Ignore all other domain packs for this blueprint

---

# NDIS CAPABILITY PACKS

---

## NDIS — OPS — Service Delivery Operations

**Domain:** Operations
**Business Goals This Pack Serves:**
- Improve service delivery efficiency
- Increase scheduled hours delivered
- Reduce no-shows and cancellations
- Optimise shift scheduling

**Primary Fact Tables**

| Table | Grain | Key Columns |
|---|---|---|
| FactServiceDelivery | One row per session | ParticipantKey, WorkerKey, SupportItemKey, DateKey, PlannedHours, ActualHours, CancelledFlag, CancellationReason |
| FactRoster | One row per scheduled shift | WorkerKey, ParticipantKey, ShiftTypeKey, DateKey, ScheduledHours, ConfirmedFlag |

**Required Dimensions**
- DimParticipant (SCD2) — NDIS Number, Name, Region, SIL Flag, Status, PlanEnd
- DimSupportWorker (SCD2) — WorkerID, Name, EmploymentType, Region, Certifications
- DimSupportItem (Standard) — ItemNumber, Name, SupportCategory, Unit
- DimShiftType (Standard) — ShiftName, DayType, Period (AM/PM/Night/Sleepover)
- DimLocation (Standard) — SA2, SA3, State, NDISRegion, Metro/Remote
- DimDate (Standard) — FY July spine

**Core Measures**
```
Delivered Hours = SUM(FactServiceDelivery[ActualHours])
Planned Hours = SUM(FactRoster[ScheduledHours])
Service Delivery Rate % = DIVIDE([Delivered Hours], [Planned Hours], 0)
Cancellation Rate % = DIVIDE(CALCULATE(COUNTROWS(FactServiceDelivery), FactServiceDelivery[CancelledFlag] = TRUE()), COUNTROWS(FactServiceDelivery), 0)
Active Participants = DISTINCTCOUNT(FactServiceDelivery[ParticipantKey])
Sessions Delivered = COUNTROWS(FactServiceDelivery)
Avg Hours per Participant per Week = DIVIDE([Delivered Hours], [Active Participants] * 52, 0)
Shifts Unfilled = CALCULATE(COUNTROWS(FactRoster), FactRoster[ConfirmedFlag] = FALSE())
Shift Fill Rate % = DIVIDE(CALCULATE(COUNTROWS(FactRoster), FactRoster[ConfirmedFlag] = TRUE()), COUNTROWS(FactRoster), 0)
```

**KPI Framework**

| KPI | Good | Warn | Critical | Owner | Cadence |
|---|---|---|---|---|---|
| Service Delivery Rate % | > 90% | 80-90% | < 80% | Operations Manager | Weekly |
| Shift Fill Rate % | > 95% | 88-95% | < 88% | Rostering Manager | Daily |
| Cancellation Rate % | < 5% | 5-10% | > 10% | Operations Manager | Weekly |
| Active Participants | Growing | Stable | Declining | CEO | Monthly |
| Avg Hours per Participant per Week | On plan | 5-10% below plan | > 10% below plan | Operations Manager | Weekly |

**Dashboard Pages**
1. Operations Executive Summary — delivery rate, sessions, cancellations, fill rate, trend
2. Shift Scheduling — fill rate by region/worker, unfilled shifts (action list), shift type breakdown
3. Service Delivery Performance — delivery rate by participant, support category, region
4. Cancellation Analysis — reasons, trends, cost impact, participant impact
5. Participant Activity — sessions by participant, frequency, support item breakdown

**Executive Questions**
- Are we delivering the hours that participants are funded for, and where are the gaps?
- Which regions and support categories have the highest cancellation rates?
- How many shifts remain unfilled this week, and which participants are affected?
- Are we seeing patterns in cancellations by worker, day type, or support category?
- Which participants have received significantly fewer hours than planned this month?

**Design Assumptions**
- Service delivery is recorded at session level with actual start and end times
- Cancelled sessions are flagged with a cancellation reason code
- Rostered shifts are maintained in a scheduling system separate from service delivery records

**Design Risks**
- Cancelled vs short-notice cancellation distinction may affect billing rules under NDIS pricing
- Session-level data may not be available if provider uses daily summaries only

---

## NDIS — FIN — Financial Sustainability and Funding

**Domain:** Finance
**Business Goals This Pack Serves:**
- Improve financial sustainability
- Optimise funding utilisation
- Improve claim acceptance and recognised revenue
- Manage gross margin and worker cost
- Reduce revenue at risk

**Primary Fact Tables**

| Table | Grain | Key Columns |
|---|---|---|
| FactPlanUtilisation | One row per participant × funding category × plan period | ParticipantKey, FundingCategoryKey, DateKey, AllocatedAmount, UtilisedAmount, RemainingAmount, DaysRemaining |
| FactClaims | One row per claim line | ClaimKey, ParticipantKey, SupportItemKey, ClaimDateKey, ServiceDateKey, ClaimedAmount, StatusKey, NDIAErrorCode |
| FactRoster | One row per shift (for cost) | WorkerKey, ParticipantKey, DateKey, ShiftTypeKey, ScheduledHours, WorkerCost |

**Required Dimensions**
- DimParticipant (SCD2) — NDIS Number, Name, Region, PlanStart, PlanEnd, PlanBudget, SILFlag
- DimFundingCategory (Standard) — CategoryCode, CategoryName (Core/Capacity/Capital), BudgetType
- DimSupportItem (Standard) — ItemNumber, Name, SupportCategory, Unit, PriceLimitAUD
- DimClaimStatus (Standard) — Status (Accepted/Rejected/Pending/Resubmitted), NDIAErrorCode
- DimDate (Standard) — FY July spine, fiscal month, fiscal quarter, fiscal year

**Core Measures**
```
Funding Utilised AUD = SUMX(FactPlanUtilisation, FactPlanUtilisation[UtilisedAmount])
Funding Allocated AUD = SUM(FactPlanUtilisation[AllocatedAmount])
Funding Utilisation % = DIVIDE([Funding Utilised AUD], [Funding Allocated AUD], 0)
Remaining Budget = SUM(FactPlanUtilisation[RemainingAmount])
Revenue = CALCULATE(SUM(FactClaims[ClaimedAmount]), DimClaimStatus[Status] = "Accepted")
Revenue at Risk = CALCULATE(SUM(FactClaims[ClaimedAmount]), DimClaimStatus[Status] = "Pending")
Claims Submitted = COUNTROWS(FactClaims)
Claims Accepted = CALCULATE(COUNTROWS(FactClaims), DimClaimStatus[Status] = "Accepted")
Claim Approval Rate % = DIVIDE([Claims Accepted], [Claims Submitted], 0)
Claims Rejected = CALCULATE(COUNTROWS(FactClaims), DimClaimStatus[Status] = "Rejected")
Worker Cost = SUM(FactRoster[WorkerCost])
Gross Margin = [Revenue] - [Worker Cost]
Gross Margin % = DIVIDE([Gross Margin], [Revenue], 0)
Projected Overspend = SUMX(FILTER(FactPlanUtilisation, FactPlanUtilisation[DaysRemaining] > 0), IF(DIVIDE(FactPlanUtilisation[UtilisedAmount], FactPlanUtilisation[DaysElapsed], 0) * FactPlanUtilisation[DaysRemaining] > FactPlanUtilisation[RemainingAmount], DIVIDE(FactPlanUtilisation[UtilisedAmount], FactPlanUtilisation[DaysElapsed], 0) * FactPlanUtilisation[DaysRemaining] - FactPlanUtilisation[RemainingAmount], 0))
Participants Underspend Risk = CALCULATE(DISTINCTCOUNT(FactPlanUtilisation[ParticipantKey]), FactPlanUtilisation[UtilisationPct] < 0.6, FactPlanUtilisation[DaysRemaining] < 60)
Participants Overspend Risk = CALCULATE(DISTINCTCOUNT(FactPlanUtilisation[ParticipantKey]), FactPlanUtilisation[UtilisationPct] > 0.9, FactPlanUtilisation[DaysRemaining] > 30)
Revenue YTD = TOTALYTD([Revenue], DimDate[Date], "30/06")
```

**KPI Framework**

| KPI | Good | Warn | Critical | Owner | Cadence |
|---|---|---|---|---|---|
| Revenue | > budget | 90-100% of budget | < 90% of budget | CFO | Monthly |
| Gross Margin % | > 15% | 10-15% | < 10% | CFO | Monthly |
| Claim Approval Rate % | > 95% | 90-95% | < 90% | Finance Manager | Weekly |
| Funding Utilisation % | 75-95% | 60-75% or > 95% | < 60% or > 100% | Operations | Monthly |
| Participants Underspend Risk | 0 | 1-5 | > 5 | Operations | Weekly |
| Participants Overspend Risk | 0 | 1-3 | > 3 | Operations | Weekly |
| Revenue at Risk | < 5% of revenue | 5-10% | > 10% | Finance | Weekly |

**Dashboard Pages**
1. Financial Executive Summary — revenue YTD, gross margin, claim approval rate, underspend/overspend risk counts, revenue trend
2. Funding Utilisation — utilisation % by participant and category, at-risk participants (table), burn rate analysis, plan expiry calendar
3. Claims Monitoring — approval rate trend, rejections by NDIA error code (bar), pending claims (action list), resubmission tracking
4. Revenue and Margin — revenue by funding category, margin by support category, worker cost vs revenue waterfall
5. Participant Financial Risk — underspend risk list, overspend risk list, projected overruns, plan expiry pipeline

**Executive Questions**
- What is our revenue this month, and how is gross margin tracking against target?
- Which participants are at risk of underspending their plan budget before expiry?
- Which participants are projected to overspend, and what is the total financial exposure?
- What is our claim acceptance rate, and which NDIA error codes are causing rejections?
- How much revenue is currently at risk (submitted but not yet accepted)?
- Which support categories and funding categories are delivering the best margin?
- Is our financial trajectory sustainable for the rest of this financial year?

**Design Assumptions**
- NDIS plan funding allocations are maintained per participant per funding category
- Claims are submitted via bulk upload or PRODA API and status is updated in the system
- Worker cost rates are available from payroll or award rate tables

**Design Risks**
- Revenue recognition basis (delivered, claimed, or accepted) requires CFO confirmation
- Burn rate calculation assumes linear spend — confirm if seasonal or event-driven patterns apply
- NDIS price limits are updated annually — historical measures must use price at time of service

---

## NDIS — CX — Participant Outcomes and Satisfaction

**Domain:** Customer / Participant Outcomes
**Business Goals This Pack Serves:**
- Improve participant outcomes and goal achievement
- Reduce participant churn and plan exits
- Improve participant satisfaction and NPS
- Track participant progress against NDIS goals
- Monitor support plan effectiveness

**Primary Fact Tables**

| Table | Grain | Key Columns |
|---|---|---|
| FactParticipantOutcomes | One row per participant per outcome domain per review period | ParticipantKey, OutcomeDomainKey, ReviewDateKey, BaselineScore, CurrentScore, TargetScore, GoalStatus |
| FactPlanReviews | One row per plan review event | ParticipantKey, ReviewDateKey, ReviewType, PlanContinuedFlag, PlanExitReason, SatisfactionScore, FundingChangePct |
| FactSatisfactionSurveys | One row per survey response | ParticipantKey, SurveyDateKey, NPSScore, OverallSatisfaction, SupportQuality, WorkerConsistency |

**Required Dimensions**
- DimParticipant (SCD2) — NDIS Number, Name, Region, DisabilityType, AgeGroup, SILFlag, PlanStart, PlanEnd, SupportCoordinator
- DimOutcomeDomain (Standard) — Domain (Daily Activities/Social Participation/Employment/Health/Home), GoalType
- DimReviewType (Standard) — Scheduled Annual / Plan Change / Early Exit / Tribunal
- DimSupportCoordinator (Standard) — Name, Region, Portfolio Size
- DimDate (Standard) — FY July spine

**Core Measures**
```
Participants Active = DISTINCTCOUNT(FactParticipantOutcomes[ParticipantKey])
Goal Achievement Rate % = DIVIDE(CALCULATE(COUNTROWS(FactParticipantOutcomes), FactParticipantOutcomes[GoalStatus] = "Achieved"), COUNTROWS(FactParticipantOutcomes), 0)
Goals On Track % = DIVIDE(CALCULATE(COUNTROWS(FactParticipantOutcomes), FactParticipantOutcomes[GoalStatus] = "On Track"), COUNTROWS(FactParticipantOutcomes), 0)
Goals At Risk = CALCULATE(COUNTROWS(FactParticipantOutcomes), FactParticipantOutcomes[GoalStatus] = "At Risk")
Avg Outcome Score = AVERAGE(FactParticipantOutcomes[CurrentScore])
Outcome Improvement = AVERAGE(FactParticipantOutcomes[CurrentScore] - FactParticipantOutcomes[BaselineScore])
NPS Score = AVERAGEX(FactSatisfactionSurveys, FactSatisfactionSurveys[NPSScore])
Satisfaction Score = AVERAGE(FactSatisfactionSurveys[OverallSatisfaction])
Worker Consistency Score = AVERAGE(FactSatisfactionSurveys[WorkerConsistency])
Plan Retention Rate % = DIVIDE(CALCULATE(COUNTROWS(FactPlanReviews), FactPlanReviews[PlanContinuedFlag] = TRUE()), COUNTROWS(FactPlanReviews), 0)
Participant Exits = CALCULATE(COUNTROWS(FactPlanReviews), FactPlanReviews[PlanContinuedFlag] = FALSE())
Reviews Due 30 Days = CALCULATE(COUNTROWS(FactPlanReviews), FactPlanReviews[ReviewDateKey] <= TODAY() + 30, FactPlanReviews[Status] = "Scheduled")
```

**KPI Framework**

| KPI | Good | Warn | Critical | Owner | Cadence |
|---|---|---|---|---|---|
| Goal Achievement Rate % | > 70% | 55-70% | < 55% | Quality Manager | Quarterly |
| Goals On Track % | > 80% | 65-80% | < 65% | Support Coordinator | Monthly |
| NPS Score | > 50 | 20-50 | < 20 | CEO | Quarterly |
| Plan Retention Rate % | > 90% | 80-90% | < 80% | Operations | Monthly |
| Participant Exits | < 3% of portfolio | 3-6% | > 6% | CEO | Monthly |
| Outcome Improvement | Positive trend | Flat | Declining | Quality Manager | Quarterly |

**Dashboard Pages**
1. Participant Outcomes Executive Summary — NPS, goal achievement %, plan retention rate, exits, satisfaction trend
2. Goal Tracking — goals by status (on track/at risk/achieved), by domain, by coordinator, at-risk participants (action list)
3. Satisfaction and NPS — NPS trend, satisfaction by support category, worker consistency scores, survey response rate
4. Plan Reviews — reviews due (calendar), exit reasons analysis, funding change at review, early exit flags
5. Outcome Progress — baseline vs current scores by domain, improvement by cohort, top and bottom performing participants by outcome

**Executive Questions**
- Are our participants achieving their NDIS goals, and which outcome domains need more support?
- What is our NPS score this quarter, and are participants satisfied with the quality of support?
- How many plans are at risk of early exit, and what are the primary reasons?
- Which support coordinators have the highest rate of goal achievement in their portfolio?
- Are participants improving against their baseline outcome scores across key life domains?
- What is our plan retention rate, and is it improving or declining?
- How many plan reviews are due in the next 30 days, and are we prepared?

**Design Assumptions**
- Participant outcome assessments are conducted at intake and at each plan review
- Satisfaction surveys are administered periodically — minimum annually
- NPS is captured as a numeric score (0-10) per survey response
- Goal status classification (On Track / At Risk / Achieved) is maintained in the care management system

**Design Risks**
- Outcome measurement tool and scoring methodology requires confirmation — not standardised across providers
- NPS data may be sparse for smaller providers — minimum sample size thresholds required for meaningful reporting

---

## NDIS — COMP — Compliance and Incident Management

**Domain:** Compliance / Risk
**Business Goals This Pack Serves:**
- Meet NDIS Practice Standards obligations
- Reduce incident rate and improve safety
- Manage reportable incident obligations
- Demonstrate compliance to NDIS Quality and Safeguards Commission
- Track restrictive practices

**Primary Fact Tables**

| Table | Grain | Key Columns |
|---|---|---|
| FactIncidents | One row per incident | IncidentKey, ParticipantKey, WorkerKey, DateKey, LocationKey, TypeKey, SeverityKey, ReportableFlag, OutcomeKey, DaysToResolve |
| FactRestrictivePractices | One row per restrictive practice use | ParticipantKey, PracticeTypeKey, DateKey, AuthorisationFlag, ReviewDue |
| FactComplianceObligations | One row per compliance obligation per period | ObligationKey, DueDateKey, CompletedFlag, CompletedDateKey, OverdueFlag |

**Required Dimensions**
- DimParticipant (SCD2) — NDIS Number, Name, Region, SILFlag, SupportCoordinator
- DimSupportWorker (SCD2) — WorkerID, Name, Region, Certifications
- DimIncidentType (Standard) — Category, Severity, ReportableFlag, NDIS Commission Code
- DimRestrictivePracticeType (Standard) — Practice Name, Authorisation Required, Review Frequency
- DimComplianceObligation (Standard) — Obligation Name, Standard Reference, Frequency, Responsible Role
- DimLocation (Standard) — SA2, State, NDISRegion, SILHouse
- DimDate (Standard) — FY July spine

**Core Measures**
```
Incidents Total = COUNTROWS(FactIncidents)
Incidents Reportable = CALCULATE(COUNTROWS(FactIncidents), FactIncidents[ReportableFlag] = TRUE())
Incidents Open = CALCULATE(COUNTROWS(FactIncidents), FactIncidents[Status] = "Open")
Incident Rate per 100 Participants = DIVIDE([Incidents Total], DISTINCTCOUNT(FactServiceDelivery[ParticipantKey]), 0) * 100
Serious Incidents = CALCULATE(COUNTROWS(FactIncidents), DimIncidentType[Severity] IN {"Serious","Critical"})
Avg Days to Resolve = AVERAGE(FactIncidents[DaysToResolve])
Reportable Incidents Overdue = CALCULATE(COUNTROWS(FactIncidents), FactIncidents[ReportableFlag] = TRUE(), FactIncidents[NDIANotificationOverdue] = TRUE())
Restrictive Practices Unauthorised = CALCULATE(COUNTROWS(FactRestrictivePractices), FactRestrictivePractices[AuthorisationFlag] = FALSE())
Compliance Obligations Due = CALCULATE(COUNTROWS(FactComplianceObligations), FactComplianceObligations[DueDateKey] <= TODAY() + 30, FactComplianceObligations[CompletedFlag] = FALSE())
Compliance Obligations Overdue = CALCULATE(COUNTROWS(FactComplianceObligations), FactComplianceObligations[OverdueFlag] = TRUE())
Compliance Completion Rate % = DIVIDE(CALCULATE(COUNTROWS(FactComplianceObligations), FactComplianceObligations[CompletedFlag] = TRUE()), COUNTROWS(FactComplianceObligations), 0)
```

**KPI Framework**

| KPI | Good | Warn | Critical | Owner | Cadence |
|---|---|---|---|---|---|
| Incident Rate per 100 Participants | < 5 | 5-10 | > 10 | Quality Manager | Monthly |
| Reportable Incidents Overdue | 0 | 1-2 | > 2 | Quality Manager | Weekly |
| Serious Incidents | 0 | 1-2 per quarter | > 2 per quarter | CEO | Weekly |
| Restrictive Practices Unauthorised | 0 | Any | Any | Compliance | Daily |
| Compliance Completion Rate % | > 95% | 85-95% | < 85% | Compliance | Monthly |
| Avg Days to Resolve Incident | < 14 days | 14-28 days | > 28 days | Quality Manager | Weekly |

**Dashboard Pages**
1. Compliance Executive Summary — incident rate, open incidents, reportable overdue, obligations overdue, compliance rate
2. Incident Register — open incidents (action list), incidents by type/severity/location, trend, resolution time
3. Reportable Incidents — NDIS Commission notifications due/overdue, serious incidents detail, outcome tracking
4. Restrictive Practices — practice register, unauthorised uses (alert list), review schedule, authorisation compliance
5. Compliance Obligations — obligations by standard, overdue obligations (action list), completion rate trend, upcoming due dates

**Executive Questions**
- What is our current incident rate, and is it trending up or down compared to last quarter?
- Are any reportable incidents outstanding past the NDIS Commission notification deadline?
- Are there any unauthorised restrictive practices in use that require immediate attention?
- Which SIL houses or participants have the highest concentration of incidents?
- What compliance obligations are due in the next 30 days, and which are overdue?
- Are there patterns in incident types, locations, or workers that suggest a systemic issue?

**Design Assumptions**
- Incidents are recorded in a compliance management or incident tracking system
- NDIS Commission reportable incident categories are applied to each incident record
- Restrictive practice authorisations are maintained with effective dates

**Design Risks**
- Incident severity classification must align to NDIS Commission taxonomy exactly
- Reportable incident notification timeframe obligations vary by category — build a DimIncidentType with notification days field

---

## NDIS — WFM — Workforce Planning and Management

**Domain:** Workforce
**Business Goals This Pack Serves:**
- Improve support worker utilisation
- Manage overtime and award compliance
- Optimise workforce capacity by region
- Reduce workforce cost per delivered hour
- Improve workforce retention and reduce turnover

**Primary Fact Tables**

| Table | Grain | Key Columns |
|---|---|---|
| FactRoster | One row per scheduled shift | WorkerKey, ParticipantKey, ShiftTypeKey, DateKey, ScheduledHours, ActualHours, OvertimeHours, AvailableHours, WorkerCost |
| FactWorkerLeave | One row per leave period | WorkerKey, LeaveTypeKey, StartDateKey, EndDateKey, Days |
| FactWorkerRetention | One row per worker status change | WorkerKey, DateKey, EventType (Start/Exit/Return), ExitReason |

**Required Dimensions**
- DimSupportWorker (SCD2) — WorkerID, Name, EmploymentType (Permanent/Casual/Part-Time), Region, Certifications, AwardLevel, StartDate
- DimShiftType (Standard) — ShiftName, DayType (Weekday/Saturday/Sunday/PH), Period, OvertimeApplies, PenaltyRate
- DimLeaveType (Standard) — Leave Category (Annual/Sick/Personal/Unpaid/LWOP)
- DimLocation (Standard) — SA2, State, NDISRegion
- DimDate (Standard) — FY July spine, pay period flag, public holiday flag

**Core Measures**
```
Delivered Hours = SUM(FactRoster[ActualHours])
Available Hours = SUM(FactRoster[AvailableHours])
Scheduled Hours = SUM(FactRoster[ScheduledHours])
Worker Utilisation % = DIVIDE([Delivered Hours], [Available Hours], 0)
Overtime Hours = SUM(FactRoster[OvertimeHours])
Overtime % = DIVIDE([Overtime Hours], SUM(FactRoster[ActualHours]), 0)
Worker Cost Total = SUM(FactRoster[WorkerCost])
Cost per Delivered Hour = DIVIDE([Worker Cost Total], [Delivered Hours], 0)
Casual % = DIVIDE(CALCULATE(DISTINCTCOUNT(FactRoster[WorkerKey]), DimSupportWorker[EmploymentType] = "Casual"), DISTINCTCOUNT(FactRoster[WorkerKey]), 0)
Workers Active = DISTINCTCOUNT(FactRoster[WorkerKey])
Capacity Gap = [Scheduled Hours] - [Delivered Hours]
Workforce Turnover Rate % = DIVIDE(CALCULATE(COUNTROWS(FactWorkerRetention), FactWorkerRetention[EventType] = "Exit"), CALCULATE(DISTINCTCOUNT(FactRoster[WorkerKey]), DATEADD(DimDate[Date], -12, MONTH)), 0)
Workers Below Utilisation Target = CALCULATE(DISTINCTCOUNT(FactRoster[WorkerKey]), [Worker Utilisation %] < 0.70)
Workers Above Overtime Threshold = CALCULATE(DISTINCTCOUNT(FactRoster[WorkerKey]), [Overtime %] > 0.20)
Leave Rate % = DIVIDE(SUM(FactWorkerLeave[Days]), [Available Hours] / 8, 0)
```

**KPI Framework**

| KPI | Good | Warn | Critical | Owner | Cadence |
|---|---|---|---|---|---|
| Worker Utilisation % | 75-90% | 60-75% or > 90% | < 60% or > 95% | Workforce Manager | Weekly |
| Overtime % | < 10% | 10-20% | > 20% | Workforce Manager | Weekly |
| Cost per Delivered Hour | Decreasing | Stable | Increasing > 5% | CFO | Monthly |
| Workforce Turnover Rate % | < 15% | 15-25% | > 25% | HR Manager | Monthly |
| Casual % | < 30% | 30-50% | > 50% | Workforce Manager | Monthly |
| Workers Below Utilisation Target | 0 | 1-3 | > 3 | Workforce Manager | Weekly |

**Dashboard Pages**
1. Workforce Executive Summary — utilisation %, overtime %, cost per hour, turnover rate, casual vs permanent split
2. Utilisation Analysis — worker utilisation ranked (bar), workers below target (action list), capacity vs demand by region
3. Overtime and Cost — overtime % by worker and shift type (matrix), cost per hour trend, overtime cost impact ($)
4. Workforce Capacity — scheduled vs available hours by region, capacity gap by week, demand forecasting
5. Retention and Turnover — turnover rate trend, exit reasons, new starter pipeline, tenure distribution

**Executive Questions**
- What is our current workforce utilisation rate, and which workers are consistently under or over-utilised?
- How much overtime are we paying, and which shift types and regions are driving excess cost?
- What is our cost per delivered hour trending, and how does it compare to our pricing rates?
- How many workers are we losing each quarter, and what are the primary exit reasons?
- Do we have sufficient capacity by region to meet participant demand over the next 4 weeks?
- What is our casual-to-permanent ratio, and is it increasing our award risk or unpredictability?

**Design Assumptions**
- Roster data is maintained in a workforce management or rostering system
- Worker cost rates are available from payroll including award penalty rates by shift type
- Employment type is maintained in the worker record and updated when changed (SCD2)

**Design Risks**
- Award rate complexity (SCHADS Award) may require a separate rate card table linked to DimShiftType
- Overtime calculation rules vary by employment type and award — confirm methodology before building measures

---

# PROPERTY AND REAL ESTATE CAPABILITY PACKS

---

## Property — OPS — Tenancy and Portfolio Operations

**Domain:** Operations
**Business Goals:** Maximise occupancy, minimise vacancy, manage lease lifecycle

**Primary Fact Tables**
- FactPortfolioSnapshot — Grain: one row per property per month (VacantFlag, ManagerKey, WeeklyRent)
- FactLeaseActivity — Grain: one row per lease event (Start/Renewal/Vacate/Expiry/Break)

**Core Measures**
```
Properties Under Management = DISTINCTCOUNT(FactPortfolioSnapshot[PropertyKey])
Occupancy Rate % = DIVIDE(CALCULATE([Properties Under Management], FactPortfolioSnapshot[VacantFlag] = FALSE()), [Properties Under Management], 0)
Vacancy Rate % = 1 - [Occupancy Rate %]
Avg Days Vacant = AVERAGE(FactLeaseActivity[DaysVacant])
Lease Renewal Rate % = DIVIDE(CALCULATE(COUNTROWS(FactLeaseActivity), FactLeaseActivity[EventType] = "Renewal"), CALCULATE(COUNTROWS(FactLeaseActivity), FactLeaseActivity[EventType] IN {"Renewal","Vacated"}), 0)
Leases Expiring 90 Days = CALCULATE(COUNTROWS(FactLeaseActivity), FactLeaseActivity[LeaseEndDate] <= TODAY() + 90)
Portfolio Growth = [Properties Under Management] - CALCULATE([Properties Under Management], DATEADD(DimDate[Date], -1, MONTH))
```

**KPI Framework**

| KPI | Good | Warn | Critical |
|---|---|---|---|
| Occupancy Rate % | > 97% | 93-97% | < 93% |
| Avg Days Vacant | < 14 | 14-21 | > 21 |
| Lease Renewal Rate % | > 75% | 60-75% | < 60% |
| Leases Expiring 90 Days | < 10% portfolio | 10-20% | > 20% |

**Dashboard Pages:** Executive Summary, Occupancy and Vacancies, Lease Lifecycle, Portfolio Growth, Property Manager Performance

**Executive Questions:** What is our vacancy rate? Which managers have the worst vacancy performance? How many leases expire in 90 days?

---

## Property — FIN — Revenue and Arrears

**Domain:** Finance
**Business Goals:** Optimise rental revenue, reduce arrears, improve disbursements

**Primary Fact Tables**
- FactRentPayments — Grain: one row per payment transaction
- FactArrears — Grain: one row per tenancy per snapshot date (weekly)
- FactDisbursements — Grain: one row per owner disbursement per period

**Core Measures**
```
Rent Collected = SUM(FactRentPayments[AmountReceived])
Rent Outstanding = SUM(FactArrears[OutstandingAmount])
Arrears Rate % = DIVIDE([Rent Outstanding], [Rent Roll Value], 0)
Tenancies in Arrears = COUNTROWS(FILTER(FactArrears, FactArrears[DaysInArrears] > 0))
Tenancies Over 14 Days = CALCULATE(COUNTROWS(FactArrears), FactArrears[DaysInArrears] > 14)
Management Fee Revenue = SUMX(FactDisbursements, FactDisbursements[RentCollected] * FactDisbursements[ManagementFeeRate])
Total PM Revenue = [Management Fee Revenue] + SUM(FactLeaseActivity[LettingFeeAmount]) + SUM(FactLeaseActivity[RenewalFeeAmount])
Rental Yield % = DIVIDE(SUM(DimLease[WeeklyRent]) * 52, SUM(DimProperty[PropertyValue]), 0)
```

**KPI Framework**

| KPI | Good | Warn | Critical |
|---|---|---|---|
| Arrears Rate % | < 2% | 2-4% | > 4% |
| Tenancies Over 14 Days | 0 | 1-3 | > 3 |
| Collection Rate % | > 98% | 95-98% | < 95% |
| Total PM Revenue | > budget | 90-100% | < 90% |

**Dashboard Pages:** Financial Executive Summary, Arrears Management, Revenue Analysis, Owner Disbursements, Fee Income Breakdown

**Executive Questions:** What is our arrears rate? Which managers have the highest outstanding balances? Is our management fee revenue growing?

---

## Property — WFM — Property Manager Productivity

**Domain:** Workforce
**Business Goals:** Optimise property manager portfolio load, improve productivity

**Primary Fact Tables**
- FactPortfolioSnapshot — Grain: one row per property per month (ManagerKey)
- FactInspections — Grain: one row per inspection
- FactMaintenanceJobs — Grain: one row per job

**Core Measures**
```
Properties per Manager = DIVIDE([Properties Under Management], DISTINCTCOUNT(FactPortfolioSnapshot[ManagerKey]), 0)
Inspections Overdue = CALCULATE(COUNTROWS(FactInspections), FactInspections[Status] = "Scheduled", FactInspections[DueDate] < TODAY())
Maintenance Overdue = CALCULATE(COUNTROWS(FactMaintenanceJobs), FactMaintenanceJobs[DaysOpen] > FactMaintenanceJobs[TargetResolutionDays])
Avg Resolution Days = AVERAGE(FactMaintenanceJobs[ResolutionDays])
Arrears Rate by Manager = DIVIDE(CALCULATE(SUM(FactArrears[OutstandingAmount]), ALLEXCEPT(DimPropertyManager, DimPropertyManager[ManagerKey])), CALCULATE([Rent Roll Value], ALLEXCEPT(DimPropertyManager, DimPropertyManager[ManagerKey])), 0)
```

**Dashboard Pages:** PM Productivity Summary, Portfolio Load Analysis, Inspection Compliance, Maintenance Performance by PM, PM Scorecard

---

## Property — REV — Real Estate Sales

**Domain:** Sales / Revenue Growth
**Business Goals:** Grow commission revenue, increase listings, improve agent performance

**Primary Fact Tables**
- FactListings — Grain: one row per listing
- FactSettlements — Grain: one row per settlement
- FactCommissions — Grain: one row per commission payment
- FactAppraisals — Grain: one row per appraisal

**Core Measures**
```
Active Listings = CALCULATE(COUNTROWS(FactListings), DimListingStatus[Status] = "Active")
Total Commission = SUM(FactCommissions[CommissionAmount])
Avg Days on Market = AVERAGE(FactSettlements[DaysOnMarket])
Auction Clearance Rate % = DIVIDE(CALCULATE(COUNTROWS(FactAuctions), FactAuctions[Result] = "Sold"), COUNTROWS(FactAuctions), 0)
Appraisal to Listing % = DIVIDE(COUNTROWS(FactListings), COUNTROWS(FactAppraisals), 0)
Sale Price vs List Price % = DIVIDE(AVERAGE(FactSettlements[SalePrice]), AVERAGE(FactListings[ListPrice]), 0) - 1
Commission per Agent = DIVIDE([Total Commission], DISTINCTCOUNT(FactCommissions[AgentKey]), 0)
```

**Dashboard Pages:** Sales Executive Summary, Agent Performance, Pipeline and Listings, Auction Results, Appraisal Conversion

---

## Property — COMP — Inspection and Regulatory Compliance

**Domain:** Compliance / Risk
**Business Goals:** Meet inspection obligations, manage legislative compliance

**Primary Fact Tables**
- FactInspections — Grain: one row per inspection
- FactComplianceEvents — Grain: one row per bond lodgement / tribunal / notice

**Core Measures**
```
Inspections Overdue = CALCULATE(COUNTROWS(FactInspections), FactInspections[DueDate] < TODAY(), FactInspections[CompletedFlag] = FALSE())
Inspection Compliance Rate % = DIVIDE(CALCULATE(COUNTROWS(FactInspections), FactInspections[CompletedOnTime] = TRUE()), COUNTROWS(FactInspections), 0)
Bond Lodgement Overdue = CALCULATE(COUNTROWS(FactComplianceEvents), FactComplianceEvents[EventType] = "BondLodgement", FactComplianceEvents[LodgedWithinDeadline] = FALSE())
Tribunal Matters Open = CALCULATE(COUNTROWS(FactComplianceEvents), FactComplianceEvents[EventType] = "Tribunal", FactComplianceEvents[Status] = "Open")
```

**Dashboard Pages:** Compliance Executive Summary, Inspection Calendar, Bond Compliance, Tribunal Register, Notice Tracking

---

# PROFESSIONAL SERVICES CAPABILITY PACKS

---

## ProServ — OPS — Project and Engagement Delivery

**Domain:** Operations
**Business Goals:** Improve project delivery, milestone achievement, resource deployment

**Primary Fact Tables**
- FactTimesheets — Grain: one row per timesheet entry per staff per day
- FactMilestones — Grain: one row per project milestone (planned vs actual)
- FactProjectBudget — Grain: one row per project per work package

**Core Measures**
```
Hours Recorded = SUM(FactTimesheets[HoursRecorded])
Billable Hours = CALCULATE([Hours Recorded], DimActivityType[BillableFlag] = TRUE())
Project Budget Hours = SUM(FactProjectBudget[BudgetHours])
Budget Hours Consumed % = DIVIDE([Hours Recorded], [Project Budget Hours], 0)
Milestones On Time % = DIVIDE(CALCULATE(COUNTROWS(FactMilestones), FactMilestones[DeliveredOnTime] = TRUE()), COUNTROWS(FactMilestones), 0)
Milestones Overdue = CALCULATE(COUNTROWS(FactMilestones), FactMilestones[PlannedDate] < TODAY(), FactMilestones[CompletedFlag] = FALSE())
Projects At Risk = CALCULATE(DISTINCTCOUNT(FactTimesheets[ProjectKey]), [Budget Hours Consumed %] > 0.90, FactMilestones[CompletedFlag] = FALSE())
```

**Dashboard Pages:** Delivery Executive Summary, Project Status, Milestone Tracking, Resource Deployment by Project, Budget Burn

---

## ProServ — FIN — Revenue, WIP, and Profitability

**Domain:** Finance
**Business Goals:** Improve realisation rate, reduce debtor days, improve engagement margin

**Primary Fact Tables**
- FactTimesheets — Grain: one row per timesheet entry (with charge rate at time of entry)
- FactInvoices — Grain: one row per invoice
- FactReceipts — Grain: one row per payment
- FactWriteOffs — Grain: one row per write-off event
- FactExpenses — Grain: one row per expense

**Core Measures**
```
WIP Value = SUMX(FactTimesheets, FactTimesheets[BillableHours] * RELATED(DimStaff[ChargeRate]))
Fees Billed = SUM(FactInvoices[InvoiceAmount])
Realisation Rate % = DIVIDE([Fees Billed], [WIP Value], 0)
Write-off Rate % = DIVIDE(SUM(FactWriteOffs[WriteOffAmount]), [WIP Value], 0)
Debtors Outstanding = [Fees Billed] - SUM(FactReceipts[AmountReceived])
Debtor Days = DIVIDE([Debtors Outstanding], DIVIDE([Fees Billed], 365), 0)
Gross Margin % = DIVIDE([Fees Billed] - SUMX(FactTimesheets, FactTimesheets[HoursRecorded] * RELATED(DimStaff[CostRate])) - SUM(FactExpenses[ExpenseAmount]), [Fees Billed], 0)
Debtors Over 90 Days = CALCULATE(SUM(FactInvoices[InvoiceAmount]), FactInvoices[AgeDays] > 90, DimInvoiceStatus[Status] <> "Paid")
```

**KPI Framework**

| KPI | Good | Warn | Critical |
|---|---|---|---|
| Realisation Rate % | > 90% | 80-90% | < 80% |
| Debtor Days | < 45 | 45-60 | > 60 |
| Write-off Rate % | < 3% | 3-6% | > 6% |
| Gross Margin % | > 30% | 20-30% | < 20% |

**Dashboard Pages:** Financial Executive Summary, WIP and Billing, Debtor Management, Engagement Profitability, Write-off Analysis

---

## ProServ — WFM — Billable Utilisation and Capacity

**Domain:** Workforce
**Business Goals:** Maximise billable utilisation, manage capacity, right-size resource mix

**Primary Fact Tables**
- FactTimesheets — Grain: one row per timesheet entry per staff per day
- FactCapacityForecast — Grain: one row per staff per week (booked vs available)

**Core Measures**
```
Billable Utilisation % = DIVIDE([Billable Hours], SUM(FactTimesheets[AvailableHours]), 0)
Non-Billable Hours = SUM(FactTimesheets[AvailableHours]) - [Billable Hours]
Forward Capacity = SUM(FactCapacityForecast[AvailableHours]) - SUM(FactCapacityForecast[BookedHours])
Utilisation by Level = AVERAGEX(VALUES(DimStaff[Level]), [Billable Utilisation %])
Timesheet Compliance % = DIVIDE(CALCULATE(COUNTROWS(FactTimesheets), FactTimesheets[SubmittedOnTime] = TRUE()), COUNTROWS(FactTimesheets), 0)
Revenue per FTE = DIVIDE([Fees Billed], DISTINCTCOUNT(FactTimesheets[StaffKey]), 0)
```

**Dashboard Pages:** Utilisation Executive Summary, Staff Utilisation (ranked), Capacity Forecast (8 weeks), Billable vs Non-Billable, Timesheet Compliance

---

## ProServ — REV — Pipeline and Business Development

**Domain:** Sales / Revenue Growth
**Business Goals:** Grow weighted pipeline, improve win rate, increase new client revenue

**Primary Fact Tables**
- FactPipeline — Grain: one row per opportunity per stage transition

**Core Measures**
```
Weighted Pipeline Value = SUMX(FactPipeline, FactPipeline[OpportunityValue] * RELATED(DimPipelineStage[WinProbability]))
Win Rate % = DIVIDE(CALCULATE(COUNTROWS(FactPipeline), DimPipelineStage[Stage] = "Won"), CALCULATE(COUNTROWS(FactPipeline), DimPipelineStage[Stage] IN {"Won","Lost"}), 0)
Avg Deal Size = DIVIDE(SUM(FactPipeline[OpportunityValue]), COUNTROWS(FactPipeline), 0)
Pipeline Coverage = DIVIDE([Weighted Pipeline Value], [Revenue Target], 0)
```

**Dashboard Pages:** Pipeline Executive Summary, Pipeline by Stage, Win/Loss Analysis, New vs Existing Client Revenue, BD Activity by Partner

---

# GOVERNMENT CAPABILITY PACKS

---

## Government — FIN — Budget and Expenditure

**Domain:** Finance
**Business Goals:** Track expenditure vs appropriation, manage budget variance, prevent underspend and overspend

**Primary Fact Tables**
- FactExpenditure — Grain: one row per expenditure transaction
- FactBudget — Grain: one row per budget line per period

**Core Measures**
```
Budget Allocated = SUM(FactBudget[OriginalBudget])
Budget Revised = SUM(FactBudget[RevisedBudget])
Actual Expenditure = SUM(FactExpenditure[ActualAmount])
Committed Expenditure = SUM(FactExpenditure[CommittedAmount])
Total Expenditure = [Actual Expenditure] + [Committed Expenditure]
Budget Utilisation % = DIVIDE([Actual Expenditure], [Budget Revised], 0)
Budget Variance = [Budget Revised] - [Actual Expenditure]
Expenditure YTD = TOTALYTD([Actual Expenditure], DimDate[Date], "30/06")
```

**KPI Framework**

| KPI | Good | Warn | Critical |
|---|---|---|---|
| Budget Utilisation % | 85-100% YTD profile | 75-85% or > 100% | < 75% or > 105% |
| Budget Variance | Within profile | ±5-10% | > ±10% |

**Dashboard Pages:** Ministerial Financial Summary, Budget vs Actual by Program, Variance Analysis (Waterfall), YTD Expenditure Trend, Top Cost Centres

---

## Government — OPS — Program Performance and Service Delivery

**Domain:** Operations
**Business Goals:** Improve program KPI achievement, demonstrate outcome delivery

**Primary Fact Tables**
- FactProgramPerformance — Grain: one row per program per KPI per reporting period

**Core Measures**
```
Program KPI Achievement % = DIVIDE(SUM(FactProgramPerformance[ActualResult]), SUM(FactProgramPerformance[Target]), 0)
Programs On Track = CALCULATE(COUNTROWS(VALUES(DimProgram[ProgramKey])), FactProgramPerformance[AchievementFlag] = "On Track")
Programs Behind = CALCULATE(COUNTROWS(VALUES(DimProgram[ProgramKey])), FactProgramPerformance[AchievementFlag] = "Behind")
```

**Dashboard Pages:** Program Performance Summary, KPI Achievement by Program, Outcome Delivery Trend, Programs Behind Target (Action List)

---

## Government — COMP — Grants and Acquittal Compliance

**Domain:** Compliance / Risk
**Business Goals:** Manage grant acquittal compliance, reduce overdue acquittals

**Primary Fact Tables**
- FactGrants — Grain: one row per grant per milestone

**Core Measures**
```
Grants Awarded Value = CALCULATE(SUM(FactGrants[GrantAmount]), FactGrants[EventType] = "Awarded")
Grant Acquittal Rate % = DIVIDE(CALCULATE(COUNTROWS(FactGrants), FactGrants[EventType] = "Acquitted"), CALCULATE(COUNTROWS(FactGrants), FactGrants[EventType] = "Acquittal Due"), 0)
Overdue Acquittals = CALCULATE(COUNTROWS(FactGrants), FactGrants[AcquittalDueDate] < TODAY(), FactGrants[AcquittedFlag] = FALSE())
Overdue Acquittals Value = CALCULATE(SUM(FactGrants[GrantAmount]), FactGrants[AcquittalDueDate] < TODAY(), FactGrants[AcquittedFlag] = FALSE())
```

**Dashboard Pages:** Grants Compliance Summary, Acquittal Status, Overdue Acquittals (Action List), Grant Expenditure vs Milestones, Recipients by Region

---

## Government — WFM — Workforce and Establishment

**Domain:** Workforce
**Business Goals:** Manage headcount within approved establishment, control workforce cost

**Primary Fact Tables**
- FactWorkforce — Grain: one row per employee per month (snapshot)

**Core Measures**
```
Total FTE = SUM(FactWorkforce[FTECount])
Approved Establishment = SUM(FactWorkforce[ApprovedFTE])
FTE Variance = [Approved Establishment] - [Total FTE]
Vacancy Count = CALCULATE(COUNTROWS(FactWorkforce), FactWorkforce[FTECount] = 0, DimPosition[EstablishmentFlag] = TRUE())
Turnover Rate % = DIVIDE(SUM(FactWorkforce[SeparationCount]), AVERAGE(FactWorkforce[HeadcountSnapshot]), 0)
```

**Dashboard Pages:** Workforce Executive Summary, FTE vs Establishment by Division, Vacancies (Action List), Turnover Trend, Classification Level Breakdown

---

# HEALTHCARE CAPABILITY PACKS

---

## Healthcare — OPS — Patient Flow and Bed Management

**Domain:** Operations
**Business Goals:** Improve patient throughput, manage bed occupancy, reduce length of stay

**Primary Fact Tables**
- FactAdmissions — Grain: one row per inpatient admission
- FactBedOccupancy — Grain: one row per bed per day (snapshot)
- FactEncounters — Grain: one row per ED or outpatient encounter

**Core Measures**
```
Inpatient Admissions = COUNTROWS(FactAdmissions)
ALOS = DIVIDE(SUM(FactAdmissions[LengthOfStayDays]), COUNTROWS(FactAdmissions), 0)
Bed Occupancy % = DIVIDE(SUM(FactBedOccupancy[OccupiedBeds]), SUM(FactBedOccupancy[AvailableBeds]), 0)
ED Presentations = CALCULATE(COUNTROWS(FactEncounters), DimEncounterType[TypeName] = "ED")
ED Seen Within Target % = DIVIDE(CALCULATE(COUNTROWS(FactEncounters), FactEncounters[SeenWithinTargetFlag] = TRUE()), [ED Presentations], 0)
30-Day Readmission Rate % = DIVIDE(CALCULATE(COUNTROWS(FactAdmissions), FactAdmissions[ReadmissionWithin30] = TRUE()), COUNTROWS(FactAdmissions), 0)
Theatre Utilisation % = DIVIDE(SUM(FactProcedures[ActualMinutes]), SUM(FactProcedures[BookedMinutes]), 0)
Elective Waitlist = CALCULATE(COUNTROWS(FactWaitlist), FactWaitlist[Status] = "Waiting")
```

**Dashboard Pages:** Clinical Operations Summary, Patient Flow (daily admissions/discharges), ED Performance, Theatre Utilisation, Waitlist Management

---

## Healthcare — FIN — Revenue Cycle and Billing

**Domain:** Finance
**Business Goals:** Improve billing collection rate, reduce outstanding receivables, optimise payer mix

**Primary Fact Tables**
- FactBilling — Grain: one row per billing transaction per item

**Core Measures**
```
Revenue = SUM(FactBilling[AmountBilled])
Revenue Collected = SUM(FactBilling[AmountReceived])
Collection Rate % = DIVIDE([Revenue Collected], [Revenue], 0)
Outstanding Receivables = [Revenue] - [Revenue Collected]
Revenue by Payer = CALCULATE([Revenue], ALLEXCEPT(DimPayer, DimPayer[PayerType]))
Cost Per Admission = DIVIDE(SUM(FactAdmissions[EpisodeCost]), COUNTROWS(FactAdmissions), 0)
```

**Dashboard Pages:** Revenue Cycle Summary, Revenue by Payer Type, Receivables Aging, MBS Item Performance, Cost per Episode by DRG

---

## Healthcare — WFM — Clinical Workforce

**Domain:** Workforce
**Business Goals:** Manage nurse-to-patient ratios, reduce agency staff cost, optimise practitioner utilisation

**Primary Fact Tables**
- FactWorkforceHours — Grain: one row per practitioner per shift

**Core Measures**
```
Nurse-to-Patient Ratio = DIVIDE(CALCULATE(SUM(FactWorkforceHours[ShiftHours]), DimPractitioner[RoleCategory] = "Nursing"), [Inpatient Admissions], 0)
Practitioner Utilisation % = DIVIDE(SUM(FactWorkforceHours[BillableHours]), SUM(FactWorkforceHours[AvailableHours]), 0)
Agency Staff Cost = CALCULATE(SUM(FactWorkforceHours[ShiftCost]), DimPractitioner[EmploymentType] = "Agency")
Overtime % = DIVIDE(SUM(FactWorkforceHours[OvertimeHours]), SUM(FactWorkforceHours[TotalHours]), 0)
```

**Dashboard Pages:** Workforce Executive Summary, Nurse-to-Patient Ratio by Ward, Practitioner Utilisation, Agency Cost Trend, Overtime by Department

---

## Healthcare — CX — Patient Outcomes and Safety

**Domain:** Customer / Participant Outcomes
**Business Goals:** Improve clinical outcomes, reduce readmissions, improve patient experience

**Primary Fact Tables**
- FactIncidents — Grain: one row per clinical incident
- FactPatientExperience — Grain: one row per survey response

**Core Measures**
```
Incidents per 1000 Bed Days = DIVIDE(COUNTROWS(FactIncidents), SUM(FactAdmissions[LengthOfStayDays]), 0) * 1000
Readmission Rate % = DIVIDE(CALCULATE(COUNTROWS(FactAdmissions), FactAdmissions[ReadmissionFlag] = TRUE()), COUNTROWS(FactAdmissions), 0)
Patient Satisfaction Score = AVERAGE(FactPatientExperience[SatisfactionScore])
```

**Dashboard Pages:** Clinical Outcomes Summary, Readmission Analysis, Incident Rate by Ward, Patient Experience Scores, Safety Trend

---

# AGED CARE CAPABILITY PACKS

---

## AgedCare — OPS — Residential Occupancy

**Domain:** Operations
**Business Goals:** Maximise bed occupancy, manage admissions and discharges, reduce vacancies

**Primary Fact Tables**
- FactResidentOccupancy — Grain: one row per resident per day (snapshot)

**Core Measures**
```
Residential Occupancy % = DIVIDE(SUM(FactResidentOccupancy[OccupiedBeds]), SUM(FactResidentOccupancy[ApprovedPlaces]), 0)
Occupied Bed Days = SUM(FactResidentOccupancy[OccupiedBedDays])
Waitlist Count = COUNTROWS(FactWaitlist)
New Admissions = CALCULATE(COUNTROWS(FactResidentOccupancy), FactResidentOccupancy[AdmissionFlag] = TRUE())
```

**Dashboard Pages:** Occupancy Summary, Daily Occupancy by Facility, Admissions and Discharges, Waitlist Management, Vacancy Analysis

---

## AgedCare — FIN — AN-ACC Revenue and Viability

**Domain:** Finance
**Business Goals:** Maximise AN-ACC subsidy, manage RAD/DAP revenue, improve financial viability

**Primary Fact Tables**
- FactResidentOccupancy — Grain: one row per resident per day (classification × subsidy)
- FactAccommodationPayments — Grain: one row per RAD/DAP payment
- FactSubsidyPayments — Grain: one row per government payment batch

**Core Measures**
```
AN-ACC Subsidy Revenue = SUMX(FactResidentOccupancy, FactResidentOccupancy[OccupiedBedDays] * RELATED(DimANACC[BaseSubsidyRate]))
Avg AN-ACC Rate per Day = DIVIDE([AN-ACC Subsidy Revenue], [Occupied Bed Days], 0)
DAP Revenue = SUM(FactAccommodationPayments[DAPAmount])
RAD Balance = SUM(FactAccommodationPayments[RADBalance])
Total Revenue = [AN-ACC Subsidy Revenue] + [DAP Revenue] + SUM(FactSubsidyPayments[MeansTestedFeeAmount])
Revenue per Bed Day = DIVIDE([Total Revenue], [Occupied Bed Days], 0)
Cost per Bed Day = DIVIDE(SUM(FactWorkerHours[WorkerCost]), [Occupied Bed Days], 0)
```

**Dashboard Pages:** Financial Viability Summary, AN-ACC Classification Mix, Revenue per Bed Day by Facility, RAD and DAP Portfolio, Cost vs Revenue per Bed Day

---

## AgedCare — WFM — Care Minutes and Workforce Compliance

**Domain:** Workforce
**Business Goals:** Meet mandatory care minutes targets, manage RN hours obligation, control workforce cost

**Primary Fact Tables**
- FactWorkerHours — Grain: one row per worker per date by role type (RN/EN/PCW)

**Core Measures**
```
Care Minutes per Resident per Day = DIVIDE(SUM(FactWorkerHours[ActualMinutes]), COUNTROWS(FactResidentOccupancy), 0)
RN Minutes per Resident per Day = DIVIDE(CALCULATE(SUM(FactWorkerHours[ActualMinutes]), DimWorker[RoleType] = "RN"), COUNTROWS(FactResidentOccupancy), 0)
Care Minutes Compliance % = DIVIDE([Care Minutes per Resident per Day], 200, 0)
RN Minutes Compliance % = DIVIDE([RN Minutes per Resident per Day], 40, 0)
Workforce Cost = SUM(FactWorkerHours[WorkerCost])
```

**Dashboard Pages:** Care Minutes Compliance Summary, RN Hours by Facility, Workforce Cost per Bed Day, Rostered vs Actual by Role Type, Overtime Analysis

---

## AgedCare — CX — Resident Quality and Safety

**Domain:** Customer / Participant Outcomes
**Business Goals:** Improve ACQSC quality indicators, reduce falls and incidents, improve resident experience

**Primary Fact Tables**
- FactIncidents — Grain: one row per incident
- FactQualityIndicators — Grain: one row per QI per facility per submission period

**Core Measures**
```
Fall Rate per 1000 Bed Days = CALCULATE([Incident Rate per 1000 Bed Days], DimIncidentType[Category] = "Fall")
Incident Rate per 1000 Bed Days = DIVIDE(COUNTROWS(FactIncidents), [Occupied Bed Days], 0) * 1000
Pressure Injury Rate = CALCULATE([Incident Rate per 1000 Bed Days], DimIncidentType[Category] = "Pressure Injury")
QI Score by Indicator = AVERAGE(FactQualityIndicators[IndicatorValue])
Reportable Incidents = CALCULATE(COUNTROWS(FactIncidents), DimIncidentType[ReportableFlag] = TRUE())
```

**Dashboard Pages:** Quality and Safety Summary, Incident Rate by Type and Facility, ACQSC Quality Indicator Trends, Falls Prevention Dashboard, Reportable Incident Register

---

# ACCOUNTING / LEGAL / PROFESSIONAL SERVICES (SHARED BILLABLE PACKS)

---

## Billable — FIN — WIP and Billing Performance

**Domain:** Finance
**Business Goals:** Maximise realisation rate, reduce write-offs, improve debtor collections

*(See ProServ — FIN pack — identical structure applies)*

---

## Billable — WFM — Timesheet and Utilisation

**Domain:** Workforce
**Business Goals:** Maximise chargeable utilisation, manage capacity, ensure timesheet compliance

*(See ProServ — WFM pack — applies to Accounting, Legal, and Consulting equally)*

---

## Billable — COMP — Lodgement and Regulatory Compliance (Accounting/Legal)

**Domain:** Compliance / Risk
**Business Goals:** Meet ATO lodgement deadlines, manage trust account obligations, track regulatory compliance

**Primary Fact Tables**
- FactLodgements — Grain: one row per client per obligation type per period
- FactTrustTransactions — Grain: one row per trust account entry

**Core Measures**
```
Lodgements Due 30 Days = CALCULATE(COUNTROWS(FactLodgements), FactLodgements[DueDate] <= TODAY() + 30, FactLodgements[Status] = "Outstanding")
Lodgements Overdue = CALCULATE(COUNTROWS(FactLodgements), FactLodgements[DueDate] < TODAY(), FactLodgements[Status] = "Outstanding")
Trust Account Balance = SUM(FactTrustTransactions[Amount])
Lodgement Completion Rate % = DIVIDE(CALCULATE(COUNTROWS(FactLodgements), FactLodgements[Status] = "Lodged"), COUNTROWS(FactLodgements), 0)
```

**Dashboard Pages:** Compliance Summary, ATO Lodgement Calendar, Overdue Lodgements (Action List), Trust Account Summary, Client Obligation Register

---

# RETAIL CAPABILITY PACKS

---

## Retail — OPS — Store and Product Operations

**Domain:** Operations
**Business Goals:** Improve same-store sales, optimise product mix, reduce returns

**Primary Fact Tables**
- FactSales — Grain: one row per order line item
- FactReturns — Grain: one row per return line

**Core Measures**
```
Revenue = SUMX(FactSales, FactSales[Quantity] * FactSales[UnitPrice] - FactSales[DiscountAmt])
Gross Margin % = DIVIDE([Revenue] - SUMX(FactSales, FactSales[Quantity] * RELATED(DimProduct[UnitCost])), [Revenue], 0)
Same-Store Sales Growth % = DIVIDE([Revenue] - CALCULATE([Revenue], SAMEPERIODLASTYEAR(DimDate[Date])), CALCULATE([Revenue], SAMEPERIODLASTYEAR(DimDate[Date])), 0)
Return Rate % = DIVIDE(SUM(FactReturns[ReturnQty]), SUM(FactSales[Quantity]), 0)
Avg Transaction Value = DIVIDE([Revenue], DISTINCTCOUNT(FactSales[OrderID]), 0)
```

**Dashboard Pages:** Retail Operations Summary, Product Performance, Store Performance, Returns Analysis, Category Mix

---

## Retail — WFM — Inventory Management

**Domain:** Workforce *(note: Retail uses WFM slot for Inventory as primary operational constraint)*
**Business Goals:** Optimise stock levels, reduce stockouts and overstock

**Primary Fact Tables**
- FactInventory — Grain: one row per SKU per store per day (snapshot)

**Core Measures**
```
Stock Cover Days = DIVIDE(SUM(FactInventory[UnitsOnHand]), DIVIDE([Units Sold], 30), 0)
Inventory Turnover = DIVIDE([Units Sold], AVERAGE(FactInventory[UnitsOnHand]), 0)
Stockout Rate % = DIVIDE(CALCULATE(COUNTROWS(FactInventory), FactInventory[UnitsOnHand] = 0), COUNTROWS(FactInventory), 0)
```

**Dashboard Pages:** Inventory Executive Summary, Stock Cover by Category, Stockout Alert List, Overstock Analysis, Reorder Recommendations
