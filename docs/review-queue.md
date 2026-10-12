# Clinician review queue

Everything below is **unverified game tuning or secondary-source data**, tagged `// CLINICIAN-REVIEW` in code.
Nothing here is a clinical rule. The simulation is a research/teaching prototype, not for patient use.

| # | Where | Item | Status |
|---|---|---|---|
| 1 | `ToothData.cs` `ToothSpec.Library` | Canal length/curvature/crown means+SDs (from project CSV, secondary sources) | Unreviewed |
| 2 | `ToothData.cs` `ApicalOffsetMm` = 0.5 | Target WL offset; AAE range is 0.5-1.0 mm short of the apex | Unreviewed |
| 3 | `ToothData.cs` chamber roof/floor, chamber half-width, enamel thickness | Illustrative geometry | Unreviewed |
| 4 | `EndoTwin.cs` stress/debris gains, decay, block thresholds | Instrument fatigue/torsion is one linear scalar; real limits are file-specific | Unreviewed |
| 5 | `EndoTwin.cs` `FloorFailMargin`, `BurRadiusMm`, straight-line drilling | Perforation rule + bur size | Unreviewed |
| 6 | `EndoTwin.cs` WL tolerance (<=0.5 excellent, <=1.0 acceptable) and scoring penalties | Scoring design | Unreviewed |
| 7 | `EndoTwin.cs` master apical file policy (curve > 20 deg => #30 else #40), ISO sequence 15-40 | Instrumentation sequence | Unreviewed |
| 8 | `ARLayer.cs` `ConfidenceGateMm` = 0.5, tracker bias/latency | Registration gate (literature suggests ~1-2.6 mm for HMD AR) | Unreviewed |
| 9 | `EndoSession.cs` ISO colour code | ISO 3630-1 handle colours | Confirm |
| 10 | Not modelled | Isolation (rubber dam), NaOCl extrusion/needle setback, ledge/transportation, fatigue vs torsion split, canal location (MB2), neighbouring structures, coronal seal | Needs research + clinician |

See the build-plan research notes, section 8.2, for the sourced hazard table.
