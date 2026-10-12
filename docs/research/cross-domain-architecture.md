# Domain-agnostic Digital Surgical Twin architecture

Researched 2026-10-13. Architecture is **our synthesis** from the sources in `surgical-dst-landscape.md`; it is a proposal, not a published standard. Source URLs are in the landscape note and referenced here by short name.

## Summary

A domain-agnostic DST can be split into six modules with stable interfaces, plus a time/uncertainty backbone. Domains differ almost entirely in three places: the **anatomy model** (rigid, quasi-rigid, deforming, periodic), the **tracking modality**, and the **registration drift source**. The workflow model and AR guidance layer are largely reusable. Dental is the favourable special case: near-rigid anatomy, small working volume, strong CBCT, tool and anatomy both trackable.

## 1. Modules

### 1.1 Anatomy state
- Contents: segmented structures (meshes and/or volume), per-structure material class, mutable state (e.g. removed bone/dentin, instrumented canal, debris), uncertainty per structure.
- Source: DICOM/NIfTI -> segmentation (nnU-Net / DentalSegmentator for dental) -> mesh + labelmap. Keep both: meshes for rendering and collision, labelmap/SDF for material removal.
- Design rule: anatomy is a **versioned state** (immutable base from preop imaging + a log of intraoperative edits), so the twin can be replayed and compared with plan. Twin-S does this for bone drilling (landscape, NEURO).
- Interface: `IAnatomyModel { GetStructure(id); ApplyEdit(edit); Query(point) -> label, distance; Uncertainty(id) }`.

### 1.2 Instrument tracking
- Interface: `IPoseSource { Pose(timestamp) -> SE3 + covariance + validity }` for every tracked rigid body (tool, patient reference, camera/headset).
- Modalities: optical (IR marker tools; line-of-sight), EM (no line-of-sight, metal interference; relevant ENT, cardiac catheters), inertial, marker-based vision (fiducials/QR), markerless vision (instrument pose nets such as SurgRIPE-style), robot kinematics, headset inside-out tracking.
- Make the modality a plug-in. The twin core only sees SE3 + covariance + latency.
- Tool model library: tool geometry, tip offset (calibration), working-limit parameters per tool (e.g. file length, bur diameter). Clinical values must come from manufacturer data and be flagged for review.

### 1.3 Registration
- Chain: image space -> patient reference frame -> tracker frame -> world/headset frame. Each link carries its own transform, TRE estimate, timestamp.
- Rigid first (point/surface/fiducial; ICP). Deformable as an optional correction layer (see 1.4).
- Drift: continuous monitoring (reference-frame jiggle, fiducial reprojection, residual to surface), **confidence-gated overlays** (dim/hide when estimated error exceeds the task tolerance). The intro game already implements the idea (registration error in mm, dimming, warning). Keep that behaviour as a first-class contract.
- Error budget to design against (from literature): HMD pooled 2.6 mm (marker), ~2 mm achievable, implant guidance ~1.1-1.4 mm, nav endo ~0.6-1.25 mm. [See landscape.]
- Latency compensation: timestamp every sample, predict pose to display time (Kalman / constant-velocity), report motion-to-photon time.

### 1.4 Deformation and physiology
Pluggable model behind `IDeformationModel { Step(dt, constraints); Displacement(point) }`.
| Tier | Model | Fits | Notes |
|---|---|---|---|
| 0 | None (rigid) | dental, bone, most ortho | Detect violation (jaw motion) rather than model |
| 1 | Periodic motion (gating, 4D) | cardiac, lung/liver respiration | Needs ECG/respiratory signal |
| 2 | Surface-driven reduced model (XPBD/PBD, FEM reduced, learning surrogate) | lap liver, brain shift | Obi (no cutting), SOFA (offline/bridge), CRESSim-MPM (cutting) |
| 3 | Full FEM/MPM with cutting | training sims | Heavy; outside real-time headset budget |
Evidence: brain-shift corrections reach 1.5 mm TRE only with intraoperative US; learned synthetic-MRI approach showed no gain over classical baseline (preprint). Treat deformable registration as research-grade.

### 1.5 Workflow model
- Representation: hierarchical state machine (phase -> step -> action) with guards from twin state, plus a probabilistic layer later. The intro game's four stages (access, working length, shaping, obturation) are already this shape.
- Inputs: instrument-anatomy events (tool entered structure, depth, force), tracking confidence, optionally video-based recognition (Cholec80-style foundation models, 80-90% depending on paper, not comparable across protocols).
- Outputs: current step, expected next step, deviation flags, time-in-step.
- Make procedures data (graph asset), not code, so other domains swap the graph: access -> ... -> closure. Procedure content (steps, safe distances) is clinical and must be authored/reviewed by clinicians.

### 1.6 AR guidance layer
- Consumes twin state + workflow step + registration confidence; emits **prompts** with priority, anchor (anatomy frame), modality (visual/audio/haptic), and verbosity.
- Adaptation inputs: expertise (novice/expert, as in intro game), error rate, step difficulty, time-pressure, optionally physiologic/gaze load. Literature shows 3D vs 2D guide benefit is task-dependent (temporal bone, NASA-TLX) so keep guide style a policy, not a constant. No real-time cognitive-load-adaptive system was found in the literature; start with rule-based adaptation and log data.
- Rendering concerns: occlusion handling and depth cues (occluder meshes, depth API), vergence-accommodation (adds ~1.5 mm error on optical see-through), viewpoint parallax. Hide overlays that would cover the instrument tip.
- Safety: guidance is advisory and must never be the sole source of a depth/limit decision in a clinical setting (unverified regulatory note: needs regulatory review).

### 1.7 Backbone
- Time: single monotonic clock, per-sample timestamps, ring buffers.
- Uncertainty: covariance on poses, TRE per registration link, confidence per structure.
- Event log: record everything (the intro game already writes 10 Hz CSV + JSON summary). Enables replay, evaluation, and ML later.
- Transport: in-process for Unity standalone; OpenIGTLink / ROS-style bridge for external trackers and Slicer; edge offload only if latency < ~50 ms hybrid per survey table (cloud 50-500+ ms is unsuitable for guidance loop).

## 2. How domains differ

| Domain | Anatomy | Motion | Typical tracking | Imaging | Registration challenge | Fit for first reuse |
|---|---|---|---|---|---|---|
| Dental / endo / implant | Rigid teeth+jaw, small soft tissue | Jaw/head motion, patient reference needed | Optical (nav systems), headset + fiducial, markerless teeth-based (pilot) | CBCT | Fiducial/reference stability on teeth, small tolerances (~1 mm) | First prototype |
| Ortho / spine | Rigid bone, soft tissue around | Low (breathing in spine) | Optical, HMD-AR, robot | CT, fluoro, 3D C-arm | Reference-frame movement, pooled HMD ~2.6 mm | Easiest next domain |
| Neuro (skull base, cranial) | Rigid skull + deforming brain | Brain shift, CSF loss | Optical, microscope tracking | MRI, CT, intra-op US/MRI | Brain shift after dural opening | Skull base: yes (Twin-S); parenchyma: tier 2 |
| ENT / sinus | Rigid bone, mucosa | Low | EM or optical, endoscope | CT | Endoscope calibration; EM metal interference | High overlap with dental (small cavity, rigid) |
| Laparoscopic / abdominal | Soft deforming organs | Respiration, insufflation, tool contact | Endoscope vision, robot kinematics | CT/MRI, intra-op US | Non-rigid registration, organ shift | Banked, hardest |
| Cardiac / vascular | Moving heart, vessels | Heartbeat + respiration | EM catheters, fluoro, mapping systems | CT, MRI, echo, fluoro | 4D registration; no published beating-heart AR found | Banked; start with static vascular planning |

## 3. What is reusable vs domain-specific

Reusable (implement once): timebase, pose/uncertainty types, registration chain and confidence gating, event log/replay, workflow engine, prompt arbitration, AR rendering helpers, error-budget display, evaluation harness.
Domain plug-ins: anatomy importer + segmentation recipe, tool library, tracker adapter, deformation tier, procedure graph, safety-zone definitions, prompt content.

## 4. Evaluation methods (for every domain)

- Geometric: TRE/FRE per link, target-localisation error vs ground truth (phantom with known targets, 3D-printed jaws as in navigation studies).
- Temporal: motion-to-photon latency, drift over time and under motion.
- Behavioural: task time, error rate, NASA-TLX, gaze; compare adaptive vs fixed prompts (the intro game's novice/expert switch is a ready A/B baseline).
- Report protocol clearly: workflow-recognition literature shows cross-paper numbers are not comparable.

## Open questions

- Pose-uncertainty representation that the AR layer can use simply (scalar radius vs full covariance).
- Whether to keep a volume (SDF) edit model in Unity at headset frame rates, or edit meshes only.
- How to author procedure graphs with clinicians, and who signs them off.
- Regulatory posture (research-use-only) to state in docs.
