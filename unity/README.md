# SurgiFlow DST — Unity (C#)

The SurgiFlow Digital Surgical Twin, built in Unity with C#. This work is under process.

## SurgiFlowDST — 3D root canal trainer
The first Unity project. It brings the introductory root canal game into 3D:

- **Tooth model:** generated from the root canal anatomy dataset, so every case is a slightly different patient.
- **Digital twin:** the procedure state in C# (access opening, working length, cleaning & shaping, filling).
- **AR layer:** overlays anchored to the anatomy, a tracking-error and patient-motion demo, and adaptive prompts.
- **Tests:** edit-mode tests for the twin logic and play-mode tests for the scene.

## Open and play
1. Unity Hub → **Add** → **Add project from disk** → select `unity/SurgiFlowDST`.
2. Open it with Unity **6000.6.4f1**.
3. Open `Assets/Scenes/EndoSim` and press **Play**. If the scene is empty, run **SurgiFlow → Build Endo Scene** from the menu.

| Key | Action |
|---|---|
| ↑ / ↓ (Shift = fine) | Move the instrument |
| Enter | Next stage |
| W | Record working length |
| N | Next file |
| I (hold) | Irrigate |
| O (hold) | Fill the canal |
| Right mouse / C | Camera |
| R / M | Tracking error / patient motion |
| Tab | Novice / expert prompts |
| F1 | Help |
| F5 / F6 | New patient / next tooth type |
| F9 | Save the session log |
