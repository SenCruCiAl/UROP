# Plan: multi-domain DST in Unity, dental first

Date 2026-10-13. Research and planning only; no code written. Related: `surgical-dst-landscape.md`, `cross-domain-architecture.md`. Existing assets: Unity project `SurgiFlowDST` (under `unity/`, contents not inspected), Python intro game (`intro-root-canal-game/`) with a twin state machine, registration-error model and adaptive prompt engine, and 10 Hz session logging.

## Options

### A. Dental-only prototype, refactor later
Build everything for root canal in Unity directly.
- Pros: fastest demo; least abstraction.
- Cons: tooth-specific coupling (tooth coordinate frame, canal curves) makes ortho/ENT reuse a rewrite. Likely outcome per experience with prototypes, not a sourced claim.

### B. Full generic framework first
Design all six modules and several domains before any demo.
- Pros: clean.
- Cons: no validation, high risk of over-engineering; the literature shows even basic AR registered anatomy is hard (HMD ~2.6 mm pooled).

### C. Thin core + dental vertical slice (recommended)
Define a small set of interfaces (pose source, anatomy model, registration chain, workflow graph, prompt policy, event log) and implement only their dental instances. Keep domain content as data assets (ScriptableObjects/JSON) and deformation as an empty rigid tier.
- Pros: dental demo on time; second domain (ENT or ortho, both rigid) becomes a content+tracker swap; matches the architecture note.
- Cons: need discipline to keep interfaces thin; some rework when the first deformable domain arrives.

### D. Bridge to Slicer/SOFA early
Use 3D Slicer (+ PLUS/OpenIGTLink) for segmentation/registration/tracking and Unity only for display; SOFA for deformation.
- Pros: reuses mature research tooling; proven pattern (2023 Unity-Slicer OpenIGTLink AR study).
- Cons: Unity OpenIGTLink libraries are old/unclear maintenance; adds a second runtime and network latency; tough on standalone headsets.

## Recommendation

**Option C, using D only offline and optionally for tracking input.**
- Offline: segment CBCT with Slicer + DentalSegmentator, export meshes/labelmap to Unity as assets.
- Runtime: Unity-native, no external process required for the headset build; add an `IPoseSource` adapter for OpenIGTLink later if a hardware tracker is used.
- Keep SOFA, XPBD, cutting MPM, workflow video models and cardiac gating **banked**.

## Phased next steps (Unity)

Phase 0 (this week, no new features, planning only)
1. Confirm Unity version and target device (placeholders in `unity-ar-conventions`/`xr-perf-budget` skills) with the user. Fixes: AR Foundation vs OpenXR/Quest passthrough, which determines depth API and tracking options.
2. Decide the first headset/phone and 72+ fps budget (project skill).
3. Get a clinician advisor to review the root canal step list and safety margins; seed `docs/review-queue.md`.

Phase 1: core skeleton (dental data, mock tracking)
1. Namespaces/assemblies: `Surgiflow.Core` (time, SE3, uncertainty, event log), `Surgiflow.Anatomy`, `Surgiflow.Tracking`, `Surgiflow.Registration`, `Surgiflow.Workflow`, `Surgiflow.Guidance`, `Surgiflow.Domains.Dental`. Separate assembly definitions so domains do not reference each other.
2. Port the Python twin's state machine, registration-error model, prompt engine, and CSV/JSON logging to the interfaces above (they are already the right shapes).
3. Mock `IPoseSource` (keyboard/mouse + injectable lag, bias, noise, like the intro game's R/M keys).

Phase 2: anatomy pipeline
1. Pick a public/consented CBCT (or a 3D-printed phantom scan), segment with DentalSegmentator in Slicer, export FBX/glTF meshes; import with Unity import settings reviewed against Unity docs (submeshes per structure, URP shaders). Individual-tooth splitting is semi-manual per the Slicer forum.
2. Try UnityVolumeRendering (MIT, Unity 6) for desktop inspection only; use meshes for headset.
3. Implement material-removal state for the access cavity (mesh swap or SDF patch). Prototype in editor first.

Phase 3: tracking and registration
1. Fiducial/marker-based headset registration to a printed jaw phantom (HoloLens 2 literature shows < 1 mm in vitro on dental model; a Quest with passthrough is unverified for this).
2. Add reference-frame monitoring and confidence gating; measure TRE and latency against phantom targets.
3. Optional: OpenIGTLink adapter if an optical tracker is available.

Phase 4: guidance and evaluation
1. Rule-based adaptive prompts (novice/expert), occlusion-aware rendering, never covering the instrument tip.
2. Pilot study on phantom with NASA-TLX, time, errors; compare adaptive vs fixed prompts. Clinician review before any claim.

Phase 5: second-domain spike (banked trigger)
- Choose ENT sinus or ortho/spine (rigid, same architecture). Only build content, tracker adapter and procedure graph; if core changes are needed, record them as architecture debt.

## Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Markerless registration on jaw lacks clinical evidence | Overpromise | Start marker-based on phantom; markerless as research track |
| Registration error exceeds ~1 mm tolerance on headset | Misleading overlays | Confidence gating, show error budget, test on phantom first |
| Headset thermal/perf limits with volume rendering | Frame drops | Meshes on device; volume only on desktop; budget per `xr-perf-budget` |
| Clinical content invented or wrong | Safety, credibility | Source every value; mark `// CLINICIAN-REVIEW`; log in `docs/review-queue.md` |
| Over-abstraction | Delay | Only five thin interfaces in Phase 1; extract on second use |
| Unity soft-body limits (Obi no cutting, Unity weak for fluids per Frontiers 2023) | Blocks lap/cardiac later | Keep deformation behind an interface; revisit SOFA/MPM when needed |
| Unity Inference Engine operator gaps for ONNX models | ML features slip | Test model conversion early before committing to a learned component |
| Data privacy/ethics when using real CBCT | Compliance | Use public/phantom data; no patient data without IRB |
| Regulatory misunderstanding | Misuse | State research-use-only |

## Open questions for the user

1. Unity version and target device?
2. Is there phantom hardware (printed jaw) or optical tracker access?
3. Is there a clinical collaborator for review?
4. Which second domain is most interesting to the supervisor: ENT/skull base (closest to Twin-S) or ortho/spine (best evidence base)?

## Sources (see landscape note for the full list)

- Survey https://arxiv.org/html/2512.00019v1 ; Twin-S https://arxiv.org/pdf/2211.11863 ; Slicer-Unity AR via OpenIGTLink https://link.springer.com/article/10.1007/s11548-023-02977-0 ; UnityVolumeRendering https://github.com/mlavik1/UnityVolumeRendering ; Unity Inference Engine https://docs.unity3d.com/Packages/com.unity.ai.inference@2.4/manual/index.html ; Obi https://assetstore.unity.com/packages/tools/physics/obi-softbody-130029 ; HMD-AR registration review https://www.ncbi.nlm.nih.gov/pmc/articles/PMC10702732/
- Not searched yet (do before Phase 0 closes): official Unity docs on AR Foundation, XR Interaction Toolkit, glTFast/FBX import, URP mobile limits, Meta passthrough/depth occlusion APIs.
