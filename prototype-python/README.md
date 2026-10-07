# SurgiFlow Twin — Root Canal Digital Surgical Twin (DST) with Azuma AR

A small, interactive desktop simulation of an endodontic (root canal) procedure.
A **Digital Surgical Twin** mirrors the tooth and instruments in real time from *your*
keyboard/mouse input, and an **AR layer** built on Azuma's three properties gives adaptive prompts.

## Run it
Double-click **`run_simulation.bat`**, or in VS Code press **F5** (config “Run SurgiFlow Twin”), or:

```
pip install -r requirements.txt
python src/main.py
```
`python src/main.py --selftest` runs a scripted autopilot and saves screenshots in `outputs/`.

## The procedure (4 stages)
| Stage | What you do | What the twin checks |
|---|---|---|
| 1. Access opening | ↓ drives the bur down to the pulp roof, ENTER | Chamber-floor perforation = fail |
| 2. Working length | Advance ISO #10 K-file, read the apex locator, **W** at ~0.5 mm, ENTER | WL error vs apical constriction (apex − 0.5 mm) |
| 3. Cleaning & shaping | Take each file (#15 → master apical file) to WL, **I** to irrigate, **N** next file | File stress/separation, debris & blockage, over-instrumentation, skipped irrigation |
| 4. Obturation | Hold **O** to condense gutta-percha, ENTER | Fill completeness, voids from residual debris |

Controls: ↑/↓ or mouse wheel = move instrument · SHIFT = fine · A = AR on/off · R = inject tracker error ·
M = patient motion · E = novice/expert prompts · H = help · P = screenshot · F5 = new patient · TAB = tooth menu.

## Azuma (1997) AR principles — where they are in the code
1. **Combines real and virtual** — the rendered tooth/bone is the “real” scene; cyan/green/amber graphics
   (canal path, apical safe zone, WL line, tip distance, fiducials) are the virtual layer (`src/ar.py`, `Renderer.draw_ar`).
2. **Interactive in real time** — everything updates every frame from your input; FPS / frame latency shown on the dashboard.
3. **Registered in 3D** — overlays live in the tooth coordinate frame and are drawn through a *tracked* pose.
   Press **R** (+ **M**) to add 250 ms latency and bias: the virtual fiducials drift off the white real ones,
   the registration error (mm) is measured, overlays dim and the prompt engine warns you.

**Adaptive AR prompts:** prompts are chosen by priority from the live twin state, anchored to anatomy, and
switch between novice/expert wording — an “expert” who makes ≥3 errors is automatically moved back to guided prompts.

## Dataset (`data/root_canal_anatomy.csv`)
Canal lengths (mean ± SD), curvature and apical diameters collected from the web. Each run samples a new
“patient” from mean ± SD, so working length differs every time.
- Maxillary central incisor 21.8 ± 1.6 mm, canine 23.4 ± 2.3 mm — [ResearchGate table](https://www.researchgate.net/figure/The-root-canal-length-of-the-anterior-upper-teeth-according-to-their-typology_tbl2_322572259)
- Mandibular incisors — [Krishnan et al., Cureus 2024 (PMC11628873)](https://pmc.ncbi.nlm.nih.gov/articles/PMC11628873/) (CBCT root length 12.77 ± 1.13 mm); 20.71 ± 1.69 mm canal length figure came from a search summary, verify before citing
- Maxillary first premolar ~22.5 mm buccal / 21.4 mm palatal — [ResearchGate table](https://www.researchgate.net/figure/Tooth-length-and-root-length-measurement_tbl2_283164810)
- Maxillary first molar canal lengths, MB curvature 25.6 ± 7.4°, apical diameters (Vertucci) — [IntechOpen chapter](https://www.intechopen.com/chapters/67177)

Crown dimensions and some SDs are typical textbook approximations. This is an educational model, not a clinical tool.

## Outputs
Each session writes `outputs/session_<time>.csv` (10 Hz twin time-series: depth, apex locator, stress, debris,
cleanliness, fill) and `session_<time>_summary.json` (score, WL error, penalties, event log).

## Files
- `src/main.py` — app loop, input handling, autopilot self-test
- `src/twin.py` — the digital twin state machine + physics-lite models + logging
- `src/anatomy.py` — dataset loader and patient-specific tooth geometry
- `src/ar.py` — pose tracker, registration error, adaptive prompt engine
- `src/renderer.py` — drawing (real view, AR overlay, dashboard)
