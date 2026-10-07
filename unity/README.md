# Unity — SurgiFlow DSTs (planned)

This folder will hold the Unity projects for the SurgiFlow Digital Surgical Twins.

Plan:
1. Learn the Unity basics (scenes, GameObjects, C# scripts, input system).
2. Port the root-canal DST from `../prototype-python/` to Unity:
   - `twin.py` state machine → a C# `DigitalTwin` MonoBehaviour
   - `anatomy.py` + `data/root_canal_anatomy.csv` → a 3D tooth model driven by the same dataset
   - `ar.py` (tracking, registration, adaptive prompts) → AR overlay layer (AR Foundation later)
3. Add more SurgiFlow DST scenarios and publish builds.

Unity's `Library/`, `Temp/`, `Logs/`, `Build/` folders are already excluded in the root `.gitignore`.
