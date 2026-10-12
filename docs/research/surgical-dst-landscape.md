# Surgical Digital Twin landscape (cross-domain)

Researched 2026-10-13 by the researcher agent. Sources were search-result summaries plus one partial fetch (arXiv 2512.00019, first ~100k of 168k chars). Items marked **[unverified]** were not opened in full text. Clinical numbers are copied from sources and need clinician review before any use in the product.

Tags: **[DENTAL-NOW]** = informs the first prototype. **[BANKED]** = store for later domains. Domain tags: DENT, CARD, ORTHO, NEURO, LAP, ENT, GEN (domain-agnostic).

## Summary

- "SurgiFlow" as a named product/system returned no hits; treat the name as ours (web search 2026-10, no match). The nearest conceptual neighbours are the "surgical digital twin" surveys and Twin-S (skull base).
- The literature agrees on the loop (physical entity <-> virtual model <-> bidirectional data) but most real systems are *navigation + visualisation*, not true closed-loop twins. Very few report adaptive/cognitive-load-aware guidance; this is a genuine gap that SurgiFlow's adaptive prompt engine (already prototyped in the Python intro game) can target. [GEN]
- Strongest evidence bases: ortho/spine AR navigation, dental implant navigation/robotics, ENT/neuro navigation. Weakest: AR registered to a beating heart; deformable-tissue AR in laparoscopy (still research-grade).

## 1. Surveys and frameworks (GEN)

| Source | Year / venue | Why it matters | Tag |
|---|---|---|---|
| Khan et al., "A Comprehensive Survey on Surgical Digital Twin", https://arxiv.org/html/2512.00019v1 | Dec 2025, arXiv (preprint) | Layered architecture: acquisition (CT/MRI, US, endoscopy, optical tracking, robot kinematics, physiology) -> modelling (geometric, biomechanical, physiological, scene/semantic, process) -> integration (edge/cloud, 5G) -> application. Table of latency by transport: edge ~1-50 ms, hybrid edge-cloud ~10-100+ ms, cloud-only ~50-500+ ms. Lists open challenges: latency vs fidelity, uncertainty calibration, validation/benchmarks, regulation, human factors. Visible text named no open-source frameworks and little on ENT/dental/ortho. | GEN, DENTAL-NOW (architecture) |
| Asciak et al., "Digital twin assisted surgery, concept, opportunities, and challenges", https://pmc.ncbi.nlm.nih.gov/articles/11736137 (also https://strathprints.strath.ac.uk/91815/) | 2025, npj Digital Medicine 8:32 | Defines "Digital Twin-Assisted Surgery"; translation obstacles. Perspective piece, not validation. | GEN |
| Mekki et al., digital twins across perioperative care, https://www.open-access.bcu.ac.uk/16384/ | 2025, npj Digital Medicine **[unverified details]** | Finds the field largely experimental. | GEN |
| Narrative review of DTs in surgery 2015-2025, https://discovery.researcher.life/article/digital-twin-technology-in-surgery-a-narrative-review-of-applications-evidence-and-implementation-challenges/872390a1513139dc8e765d8cee522b3a | 2025 **[unverified, abstract only]** | Reports orthopaedics has the strongest evidence base. | BANKED |
| Kyeremeh et al., conceptual framework for DTs in training and navigation, https://strathprints.strath.ac.uk/96715/ | 2025, The Surgeon | Says pilot/validation trials still needed. | GEN |
| ARAILIS (medical DT + AR for laparoscopy) **[unverified, via search summary]** | 2022, IEEE ICC Workshops | States the core challenge: mapping the twin to deforming anatomy. | BANKED (LAP) |

## 2. Open-source / reusable tooling

| Tool | What it gives | Link | Tag |
|---|---|---|---|
| 3D Slicer + SlicerIGT + PLUS Toolkit + OpenIGTLink | De-facto research stack for tracked-tool calibration, registration, transform streaming. A 2023 study built a Unity AR app talking to Slicer over OpenIGTLink (https://link.springer.com/article/10.1007/s11548-023-02977-0). Slicer forum: Unity OpenIGTLink implementations exist but are old; check maintenance (https://discourse.slicer.org/t/3dslicer-vr-and-unity/3289/19). | https://discourse.slicer.org/tag/openigtlink | DENTAL-NOW (offline segmentation/registration; bridge to Unity) |
| DentalSegmentator (nnU-Net, Slicer extension) | Open model: maxilla, mandible, upper teeth, lower teeth, mandibular canal on CT/CBCT. Dice ~92% internal, ~94% external CBCT (5 institutions). Does not split individual teeth. | https://pmc.ncbi.nlm.nih.gov/articles/PMC9555672 is a related result; confirm the DentalSegmentator paper itself (search summary only) **[unverified link]** | DENTAL-NOW |
| MONAI Label dental efforts | Experimental, multi-label whole-dentition not reliable; some teeth models proprietary. https://projectweek.na-mic.org/PW38_2023_GranCanaria/Projects/TeethSegmentation | | DENTAL-NOW (watch) |
| UnityVolumeRendering (mlavik1) | MIT, Unity 6, reads DICOM/NIfTI/NRRD, DVR/MIP/isosurface, clipping, transfer functions. Use mlavik1's repo, not the older forks. https://github.com/mlavik1/UnityVolumeRendering | | DENTAL-NOW |
| SOFA framework | Open C++ interactive medical simulation, scene-graph; used for lap liver twins (KIT phantom + SOFA twin; DejaVu project). https://www.sofa-framework.org/ ; KIT item https://ibt.kit.edu/download/DR_Hiwi_2024-10-23.pdf | | BANKED (LAP, CARD, NEURO) |
| Obi Softbody (Unity Asset Store, commercial) | XPBD particle solver in Unity, **no cutting/topology change**. https://assetstore.unity.com/packages/tools/physics/obi-softbody-130029 | | BANKED |
| CRESSim-MPM | GPU material-point-method for cutting/suturing, Unity integration (licence not checked). https://arxiv.org/html/2502.18437v1 | | BANKED |
| Frontiers VR 2023 open-source surgical sim paper | Notes Unity/Unreal limits for fluids and soft body; SOFA licensing patchy. https://www.frontiersin.org/journals/virtual-reality/articles/10.3389/frvir.2023.1130156/pdf | | GEN |
| Unity Inference Engine (formerly Sentis) | On-device ONNX inference, runs on Quest; operator coverage differs from ONNX Runtime, test each model. https://docs.unity3d.com/Packages/com.unity.ai.inference@2.4/manual/index.html ; Meta guide https://developers.meta.com/horizon/documentation/unity/unity-ai-unity-inference-engine/ | | GEN (instrument/phase models) |
| SurgRIPE benchmark | Markerless 6DoF instrument pose benchmark (MICCAI 2023 challenge; 2,841 labelled endoscope frames; data and tools public). https://arxiv.org/pdf/2501.02990 | | BANKED (LAP) |

## 3. Per-domain findings

### DENT (dental, endodontic, implant, oral-maxillofacial)

- **Dynamic navigation for endodontic access.** Meta-analysis (2021): canal location success 98.1% overall; static 98.5% vs dynamic 94.5%, not significantly different. https://www.ncbi.nlm.nih.gov/pmc/articles/PMC7999608/ . Navident in 3D-printed jaws: 93% canal location, mean apical deviation 0.63 mm, learning curve noted (2021, in vitro). 2025 in vitro calcified incisors: dynamic navigation apical 3D deviation 1.25 mm vs 1.96 mm freehand, angular 1.93 vs 5.71 deg (Urbone et al., J Funct Biomater). Huth et al. 2024 (Clin Oral Investig): guided access slower but more accurate and more tooth-preserving. Mostly in vitro; one clinical series lost a canal to tracking difficulty. [DENTAL-NOW]
- **Implant accuracy benchmarks (sets the bar for any dental AR).** 2024 meta-analysis (67 articles): mean deviation 1.11 mm entry, 1.40 mm apex, 3.51 deg. https://www.ncbi.nlm.nih.gov/pmc/articles/PMC10956322/ . Autonomous robot (6 clinical studies, 299 implants): ~0.6 mm, 1.40 deg, but practicality "cumbersome" https://pmc.ncbi.nlm.nih.gov/articles/PMC11696845 [unverified mapping of this URL to the autonomous review]. Robot vs static guides: statistically lower deviation, clinical relevance doubtful. https://www.ncbi.nlm.nih.gov/pmc/articles/PMC10817552/ [DENTAL-NOW]
- **AR in dentistry.** HoloLens 2 + custom marker on dental model, in vitro (2023): accuracy and precision under 1 mm in distance measures; viewing side and head movement dominate alignment error. https://www.mdpi.com/2076-3417/13/14/8315 [unverified mapping]. Markerless stereo jaw-to-teeth registration, single-subject pilot (2015): TRE < 1 mm. https://www.ncbi.nlm.nih.gov/pmc/articles/PMC4630916/ . Markerless HoloLens 2 face-profile registration for mandible/maxilla resection, 3 patients (2025): feasible, robustness of markerless tracking needs research. https://www.mdpi.com/2075-4426/12/12/2047 [unverified mapping]. **Gap: no clinical study of markerless HoloLens 2 implant placement with accuracy against a standard.** [DENTAL-NOW]
- **Haptic VR endodontic training.** Simodont-first training improved access-cavity shape, roof removal, internal form in 40 fourth-year students (2025, Front Oral Health, DOI 10.3389/froh.2025.1673147; https://www.ncbi.nlm.nih.gov/pmc/articles/PMC12626941/); no difference in pulpal floor damage. Root canal cleaning/shaping simulators are validated mainly by expert opinion (4-DoF haptic device, Brazilian group; https://sol.sbc.org.br/index.php/sbie/article/view/44840). Single-case endodontic microsurgery VR (2025). [DENTAL-NOW; evidence for training transfer is thin]
- Existing project data: root canal anatomy CSV already in `intro-root-canal-game/` with its own "verify before citing" flags. Do not reuse those numbers clinically without review.

### CARD (cardiac / cardiovascular)

- SentiAR CommandEP: FDA 510(k) 2020, holographic display of electroanatomic mapping and catheter position on HoloLens. Motion compensation not stated in sources. https://www.medicaldesignbriefs.com/component/content/article/37909-sentiar-announces-fda-510k-clearance-of-commandep-system-the-first-holographic-cardiac-ablation-guidance-system [BANKED]
- TWIN-VT (Johns Hopkins): MRI-based heart twins predict VT ablation targets, 10 patients, uncontrolled; planning, not live AR. https://reachmd.com/news/digital-twinguided-vt-ablation-shows-early-feasibility/2486513/ [BANKED; small uncontrolled, unverified clinically]
- Survey mentions FEops HEARTguide, HeartFlow FFRCT, interactive heart platform for emergency simulation (via arXiv 2512.00019). [BANKED]
- **Gap: no source found for AR registered to a beating heart in real time.** Needs gating (ECG/respiration) and 4D models. [BANKED]

### ORTHO (bone, spine, joint)

- Spine AR pedicle screws: meta-analysis 8 studies, 1,259 screws, ~97% accuracy; XVision most common. https://www.ncbi.nlm.nih.gov/pmc/articles/PMC10649145/ . 2025 narrative review: 93-100% across platforms, limits are cost, integration, lack of multicentre trials. https://www.ncbi.nlm.nih.gov/pmc/articles/PMC12296264 . HoloLens-vs-monitor study: 2.14 mm entry, 3.14 mm target, 6.44 deg (HoloLens version not stated). https://www.ncbi.nlm.nih.gov/pmc/articles/PMC7227090 [mapping unverified]. [BANKED; rigid-bone case is the easiest generalisation target after dental]
- HMD-AR validation pooled (2022): pattern markers 2.6 +/- 1.8 mm, surface markers 3.8 +/- 3.7 mm; ~2 mm feasible, vergence-accommodation adds ~1.5 mm. https://pmc.ncbi.nlm.nih.gov/articles/PMC9318659 [mapping unverified] , https://www.ncbi.nlm.nih.gov/pmc/articles/PMC10702732/ . [GEN, DENTAL-NOW as an error budget reference]

### NEURO

- Twin-S (skull base): optical tracking of tool, anatomy, and camera + real-time simulation of drilling that updates the virtual bone; ~1.39 mm average error during drilling, 28 FPS; mixed-reality microscope overlay highlights bone to remove; line-of-sight and clinical validation limits. https://arxiv.org/pdf/2211.11863 ; IJCARS https://link.springer.com/article/10.1007/s11548-023-02863-9 . **Closest published analogue to our architecture; bone removal updating the twin is directly relevant to access-cavity drilling.** [DENTAL-NOW (architecture), BANKED (domain)]
- Brain shift: Gerard et al. 2021 review of intra-op ultrasound correction. https://pubmed.ncbi.nlm.nih.gov/33628733/ ; https://pmc.ncbi.nlm.nih.gov/articles/PMC7897668 . Feature-based US method: TRE 3.3 -> 1.5 mm over 9 cases (https://pmc.ncbi.nlm.nih.gov/articles/PMC6151276). 2026 arXiv synthetic-MRI preprint: TRE 6.27 -> 5.86 mm, equal to NiftyReg baseline (https://arxiv.org/pdf/2606.07658; preprint). Limits: speckle, small FOV, resection cavity absent from preop. [BANKED]

### LAP (laparoscopic / abdominal)

- Registration to deforming organs is the open problem (ARAILIS). SOFA-based liver twin (KIT) and DejaVu (SOFA + images, deformable registration validation). https://ibt.kit.edu/download/DR_Hiwi_2024-10-23.pdf ; https://www.sofa-framework.org/?p=25691 . Real-to-sim PBD registration on da Vinci tissue manipulation (Liu, Yip et al.) https://arxiv.org/pdf/2011.00800v2 . Liver AR puncture under respiratory motion, mm-level, animal models (via arXiv 2512.00019). [BANKED]
- Instrument 6DoF: SurgRIPE, occlusion-robust keypoint+PnP with submillimetre claim (Xu & Giannarou 2024, https://pmc.ncbi.nlm.nih.gov/articles/PMC11665797), recurrent multi-view RGB tracking, ~40k images, four cameras (Agethen et al. IJCARS 2025). One paper states no public multi-camera 6DoF instrument dataset existed (https://arxiv.org/html/2305.03535v2). [BANKED; dental needs a different tracker, see architecture note]
- Workflow recognition: Cholec80 phase recognition, foundation models reporting ~80-90% accuracy/F1 depending on paper; protocols differ, do not compare across papers (https://arxiv.org/pdf/2305.13961). Papers: https://arxiv.org/pdf/2506.02692 , https://arxiv.org/pdf/2602.13633 , https://arxiv.org/pdf/2603.12430 (2025-2026 preprints). Non-lap: cataract datasets exist in the benchmark list. [GEN; for dental a small custom step-state machine is more realistic than a learned recogniser]

### ENT

- AR endoscopic sinus/skull base: cadaver TRE 1.28 +/- 0.45 mm and reduced workload (PLoS ONE 2016) https://www.ncbi.nlm.nih.gov/pmc/articles/PMC4710572/ . Review: modern navigation <1.0 mm; EM tracking affected by large metal instruments, optical needs line of sight. https://pmc.ncbi.nlm.nih.gov/articles/PMC7444769 [mapping unverified]. Commercial: Medtronic StealthStation ENT (EM), KARL STORZ NAV1 SinusTracker (AR elements; RCT 2020, results not retrieved). [BANKED; EM vs optical choice also applies to dental]

### GEN: adaptive guidance and human factors

- SurgeonAssist-Net: workflow-driven assistance on optical see-through HMD (MICCAI 2021). https://www.dgp.toronto.edu/~karan/papers/surgeon-assist-miccai2021.pdf
- 3D vs 2D AR guides measured with NASA-TLX in temporal bone surgery: 3D lowered load for cochlear implant, 2D better for Bonebridge (Sensors 2024, https://doaj.org/article/5e0423ae203c4f469d01144a5733c1ff) so guide form is task-dependent.
- Gaze-marker design vs cognitive load (MICCAI 2025, https://papers.miccai.org/miccai-2025/paper/4731_paper.pdf).
- **Gap: no system found that adapts guidance to measured cognitive load in real time.** [GEN, research opportunity]

## Relevance to SurgiFlow

1. The error budget for dental AR is ~1 mm (implant guidance 1.1-1.4 mm, nav endo 0.6-1.25 mm, HMD pooled ~2.6 mm). The intro game's drift demo should be calibrated against these.
2. Twin-S is the template: tracked tool + tracked anatomy + simulation that mutates the anatomy (bone removal) + overlay.
3. Dental is nearly rigid (teeth, jaw), so deformation work is banked, but jaw motion and soft-tissue retraction still matter for registration drift.
4. Evidence for training transfer and clinical AR in endodontics is thin: treat claims accordingly.

## Open questions

- Which DentalSegmentator and DentalSegmentator-adjacent licences allow our use? (verify)
- Can a HoloLens-class or Quest-class headset hold ~1 mm on a mobile jaw with markerless registration? No clinical evidence found.
- Is there an endodontic-specific step/phase dataset? Not found.
- Which specific URLs marked "[mapping unverified]" actually correspond to the cited claim? Re-open before citing in any paper.

## Clinician review queue (unverified clinical content)

All accuracy figures above; any endodontic step definitions derived from them; Vertucci/canal-length data in `intro-root-canal-game/data`.
