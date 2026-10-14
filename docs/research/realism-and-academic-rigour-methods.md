# Making Unity AR/XR Simulations Realistic and Academically Credible

Purpose: reference notes on moving a Unity AR/XR simulation (here, the SurgiFlow root canal trainer and later DST modules) away from "game" design and toward realism and academic use.

How to read this file:
- **[Sourced]** means a finding taken from the linked source.
- **[Proposal]** means an engineering suggestion for this project, not a published result.
- Nothing here is clinical guidance. Any clinical value must still go through `docs/review-queue.md` and `// CLINICIAN-REVIEW`.
- Search date: 2026-10-14. Search summaries were not cross-checked against the full papers, so read the primary source before citing it.

---

## 1. Principle: replace "game feel" with measurable fidelity

| Game design habit | Realism / academic replacement |
|---|---|
| Points, "score out of 100" | Objective, named metrics (error in mm, time, forces, % canal cleaned) with units |
| Win / lose screens | Structured debrief: what happened, why it matters, how it compares to a reference |
| Arbitrary difficulty tuning | Parameters traced to a cited dataset, with the source shown in the UI |
| Hidden rules | Every threshold visible, documented and tagged for expert review |
| Cartoon colours and HUD chrome | Colours, scale and layout that follow what the clinician sees in practice |
| No data out | Time-series log (CSV/JSON) per session for statistics |

**[Proposal]** Keep the existing session log (CSV + JSON) as the primary product and treat the on-screen score as a secondary view of it.

---

## 2. Visual realism in Unity

### 2.1 Geometry and data source
- **[Sourced]** Real CT/MRI/DICOM data can be rendered in Unity with open tools. UnityVolumeRendering supports direct volume rendering, maximum intensity projection and isosurfaces, reads DICOM/NIFTI/NRRD, works in Built-in, URP and HDRP, and requires Unity 6+. It normalises imported size by default, so check scale before using it for measurements. ([UnityVolumeRendering docs](https://docsearch.algolia.com/mcp/docs/repo/mlavik1/unityvolumerendering), [Volume rendering in Unity](https://matiaslavik.codeberg.page/volume-renderer/))
- **[Sourced]** The core method is a 3D texture of density values, raymarched through a bounding box with back faces. ([Volume rendering in Unity](https://matiaslavik.codeberg.page/volume-renderer/))
- **[Sourced]** A VR access-cavity simulator has been built on CBCT-segmented anatomy, motivated by the scarcity and limited variety of extracted teeth. Another used micro-CT tooth models. ([AUC thesis](https://fount.aucegypt.edu/etds/2610), [Suebnukarn et al. 2011 via interniche](https://interniche.org/es/studies/access-cavity-preparation-training-using-haptic-virtual-reality-and-microcomputed-tomography))
- **[Proposal]** Replace the procedural revolved tooth with meshes segmented from micro-CT/CBCT (for example in 3D Slicer), keep a units-in-millimetres convention at import, and store the source scan ID, voxel size and segmentation method in the asset metadata.
- **[Proposal]** Keep the procedural generator as a fallback and as a way to create parametric variants, but label it as illustrative.

### 2.2 Materials and lighting
- **[Gap]** The searches did not return a published method for tissue shading (enamel/dentin translucency, wet surfaces, subsurface scattering). The items below are general real-time rendering practice, to be checked against an expert's reference photos.
- **[Proposal]** Use physically based materials (albedo, smoothness, normal map, ambient occlusion) for each tissue, with a separate roughness for wet versus dry surfaces.
- **[Proposal]** Add translucency or wrapped diffuse for enamel and pulp, and baked ambient occlusion in narrow canals. Prefer baked data over realtime lights and post-processing to stay within the project's 72 fps budget.
- **[Proposal]** Compare the render against clinical photographs and radiographs of the same view and keep the comparison images in `docs/images/` as evidence for face validity.

### 2.3 Showing the same data the clinician uses
- **[Proposal]** Add views the clinician already knows: a simulated periapical radiograph (projection of the model's density), an apex locator readout, and a depth ruler in millimetres. These also double as measurement tools for research.

---

## 3. Making AR elements behave like real ones

### 3.1 Blending virtual and real content (AR Foundation)
- **[Sourced]** Add the AR Occlusion Manager to the same GameObject as the AR Camera and AR Background Renderer so virtual objects are hidden behind real ones using per-pixel depth. ([Unity blog](https://unity.com/blog/technology/blend-virtual-content-and-the-real-world-with-unitys-ar-foundation-now-supporting-the))
- **[Sourced]** Combining occlusion with ARCore lighting estimation is described by Unity as giving near-seamless visual quality. ([Unity blog](https://unity.com/blog/technology/blend-virtual-content-and-the-real-world-with-unitys-ar-foundation-now-supporting-the))
- **[Sourced]** Environmental HDR lighting estimation provides main light direction, intensity, ambient spherical harmonics and an HDR cubemap, and is enabled when environment probes are enabled in an `AREnvironmentProbeManager` with a compatible camera configuration. ([Google ARCore docs](https://developers.google.com/ar/develop/unity-arf/lighting-estimation/developer-guide?hl=en))
- **[Gap]** The sources did not show how to feed the HDR cubemap into reflective materials. Check the official AR Foundation pages before relying on it.
- **[Proposal]** Test on the final target device. Occlusion and lighting estimation depend on device support, which is still undecided for this project.

### 3.2 Registration accuracy: measure it, do not assume it
- **[Sourced]** A 2022 systematic review of head-mounted AR registration reported mean errors of about 2.6 mm (pattern markers), 3.8 mm (surface markers) and 2.2 mm (manual alignment), and suggested mean error could serve as a minimum acceptable standard. ([UCL Discovery](https://discovery-pp.ucl.ac.uk/id/eprint/10166833))
- **[Sourced]** Another review of 76 studies found that most accuracy tests used few subjects or measurements and rarely measured effects on surgery time or success. ([Pérez-Pachón et al.](https://abdn.elsevierpure.com/files/160665719/P_rezPach_n_at_al_BV_ImageOverlaySurgery_AAM.pdf))
- **[Sourced]** A HoloLens 2 femur study reported a mean target registration error of 3.61 ± 2.18 mm, with errors varying by region and axis. ([imec publications](https://imec-publications.be/handle/20.500.12860/59513))
- **[Sourced]** SurgNavAR compared head-mounted display error against an NDI tracker as ground truth. ([arXiv 2603.29990](https://arxiv.org/pdf/2603.29990)) A SPIE paper proposes the same co-calibration idea for built-in tracking. ([SPIE](https://biomedicaloptics.spiedigitallibrary.org/conference-proceedings-of-spie/12034/120342J/A-co-calibration-framework-for-the-accuracy-assessment-of-vision/10.1117/12.2606815.full))
- **[Sourced]** Studies distinguish registration error from target registration error (measured at points not used for registration). ([search summary of the sources above](https://arxiv.org/abs/2511.02086v1))
- **[Proposal]** The current prototype's 0.5 mm confidence gate is a game value. Literature errors are several millimetres, so the gate should be replaced with a documented, device-specific number once a headset is chosen, and registration error should be logged with every session.
- **[Proposal]** Validate on a phantom (3D-printed tooth or block with known landmarks) and report mean ± SD target registration error with sample size.

---

## 4. Proving the simulator is valid (what academics will ask)

- **[Sourced]** The common framework is: face validity (does it look and feel realistic), content validity (does it teach the right things), and construct validity (does it separate novices from experts). Face and content are usually expert Likert surveys. ([McGill NeuroSim paper](https://neurosim.mcgill.ca/wp-content/uploads/2025/03/s11517-024-03053-8.pdf), [other validation studies](https://pmc.ncbi.nlm.nih.gov/articles/PMC5434268))
- **[Sourced]** Example designs: a spine simulator study used three expertise groups and found eight metrics that differed significantly; a robotic suturing study compared 18 novices with 12 experts. ([McGill NeuroSim paper](https://neurosim.mcgill.ca/wp-content/uploads/2025/03/s11517-024-03053-8.pdf))
- **[Sourced]** Caveats from these studies: small or single-centre samples, and survey results that reflect perception more than performance. ([search summary](https://pmc.ncbi.nlm.nih.gov/articles/PMC7137498))
- **[Proposal]** Plan a study from the start: pre-register metrics, recruit novice/intermediate/expert groups, log the same metrics for each participant, and keep the Likert questionnaire in the repo.
- **[Proposal]** Log, per session: participant ID (pseudonymised), time, path of the instrument tip, depth error, errors by type, registration error and any assistance shown. The existing CSV already covers part of this.

### Endodontic simulation evidence worth knowing
- **[Sourced]** An RCT of VR for minimally invasive endodontics (30 fourth-year students, dual haptic devices, micro-CT volume loss as primary outcome) found both groups improved and no significant effect of training method. ([Siriwan et al. 2025](https://repository.li.mahidol.ac.th/handle/123456789/112515))
- **[Sourced]** A study of 40 students on the Simodont haptic simulator found better access cavity shape and roof removal in the simulator group, but most students said tactile realism did not match a real handpiece. ([Frontiers in Oral Health 2025](https://public-pages-files-2025.frontiersin.org/journals/oral-health/articles/10.3389/froh.2025.1673147/text))
- **[Sourced]** Suebnukarn et al. (2011) randomised 40 students to micro-CT/haptic VR or extracted teeth in phantom heads, with a blinded expert scoring errors. The outcome was not visible in the retrieved summary. ([interniche](https://interniche.org/es/studies/access-cavity-preparation-training-using-haptic-virtual-reality-and-microcomputed-tomography))
- **Implication for this project:** a keyboard-driven simulator has no haptics, so it cannot claim tactile realism. Describe it as a visual and decision-making trainer, and add haptics only if the target hardware supports it.

---

## 5. Concrete next steps for this repo

1. Document the exact source (paper, table, mean/SD) next to each value in `ToothSpec.Library`, and show it in the UI. Currently several are marked "secondary" or "search summary" in the code.
2. Add per-session export of registration error and instrument path alongside the existing CSV.
3. Replace game wording ("score", "fail") with metric wording ("depth error", "separated instrument event") in the debrief.
4. Plan one pilot: a clinician face/content-validity survey on the radiograph, apex locator and canal visuals before any construct-validity study.
5. Obtain at least one micro-CT or CBCT-derived tooth model under a licence that allows research use, and record its provenance.
6. Pick the target headset or phone, then test occlusion, lighting estimation and measured registration error on it.

## 6. Open questions

- Which device is the real target? It decides occlusion, lighting and tracking options.
- Who is the clinical reviewer for the `CLINICIAN-REVIEW` queue?
- Is the goal training, research on AR guidance, or both? Study design depends on it.

## 7. Source list

- [UnityVolumeRendering docs](https://docsearch.algolia.com/mcp/docs/repo/mlavik1/unityvolumerendering)
- [Volume Rendering in Unity](https://matiaslavik.codeberg.page/volume-renderer/)
- [Unity AR Foundation occlusion and ARCore Depth API](https://unity.com/blog/technology/blend-virtual-content-and-the-real-world-with-unitys-ar-foundation-now-supporting-the)
- [ARCore lighting estimation for Unity](https://developers.google.com/ar/develop/unity-arf/lighting-estimation/developer-guide?hl=en)
- [HMD AR registration validation review (UCL)](https://discovery-pp.ucl.ac.uk/id/eprint/10166833)
- [Image overlay surgery review (Pérez-Pachón et al.)](https://abdn.elsevierpure.com/files/160665719/P_rezPach_n_at_al_BV_ImageOverlaySurgery_AAM.pdf)
- [HoloLens 2 femur registration study](https://imec-publications.be/handle/20.500.12860/59513)
- [SurgNavAR](https://arxiv.org/pdf/2603.29990)
- [Markerless HoloLens 2 registration](https://arxiv.org/abs/2511.02086v1)
- [Co-calibration framework (SPIE)](https://biomedicaloptics.spiedigitallibrary.org/conference-proceedings-of-spie/12034/120342J/A-co-calibration-framework-for-the-accuracy-assessment-of-vision/10.1117/12.2606815.full)
- [VR surgical simulator validity study (McGill)](https://neurosim.mcgill.ca/wp-content/uploads/2025/03/s11517-024-03053-8.pdf)
- [Siriwan et al. 2025 RCT](https://repository.li.mahidol.ac.th/handle/123456789/112515)
- [Simodont study, Frontiers in Oral Health 2025](https://public-pages-files-2025.frontiersin.org/journals/oral-health/articles/10.3389/froh.2025.1673147/text)
- [Suebnukarn et al. 2011 summary](https://interniche.org/es/studies/access-cavity-preparation-training-using-haptic-virtual-reality-and-microcomputed-tomography)
- [CBCT haptic access-cavity thesis (AUC)](https://fount.aucegypt.edu/etds/2610)
