# SurgiFlow Twin — UROP Project

**Real-time operating-room Digital Surgical Twins (DSTs) with adaptive AR prompts**

## About
The aim of this project is to build **Digital Surgical Twins (DSTs)**, called *SurgiFlow*. A DST is a live
virtual copy of the patient's anatomy, the surgical instruments and the state of the procedure, updated in
real time from the operator's actions. An **Augmented Reality (AR)** layer on top gives guidance that is anchored to the anatomy and
adapts to what the operator is doing. It follows **Azuma's three properties of AR**: it combines real and virtual content, it is
interactive in real time, and it is registered in 3D.

## Introduction: root canal interactive game
As an introduction, we built a small **interactive root canal game** in Python. You play through a
simplified root canal procedure with the keyboard and mouse:

1. **Access opening:** drill down to the pulp chamber.
2. **Working length:** guide a file to the root tip using an apex-locator reading.
3. **Cleaning & shaping:** work through the file sizes and irrigate the canal.
4. **Filling (obturation):** seal the canal.

A live dashboard shows what is happening inside the tooth, and on-screen AR-style prompts guide the player.
Tooth measurements come from published dental-anatomy data, so every game gives a slightly different patient.

| Working length with AR guidance | Overlay drift demo (tracking lag + patient motion) |
|---|---|
| ![Working length](docs/images/intro_game_2_working_length.png) | ![Overlay drift](docs/images/intro_game_3_shaping_misregistered.png) |

| Filling the canal | End of game |
|---|---|
| ![Obturation](docs/images/intro_game_4_obturation.png) | ![Result](docs/images/intro_game_5_result.png) |

Code, controls and data sources: [`intro-root-canal-game/`](intro-root-canal-game/)

**Play it:** open `intro-root-canal-game` and double-click `play_root_canal_game.bat`, or run:
```
cd intro-root-canal-game
pip install -r requirements.txt
python src/main.py
```

## Main work: SurgiFlow DST in Unity (under development)
The main project is being built in **Unity** with **C#**. The goal is a full SurgiFlow Digital Surgical Twin
with 3D anatomy, real-time instrument tracking and AR guidance. This work is currently under process.

The first Unity piece is a **3D root canal trainer** (`unity/SurgiFlowDST`, scene `EndoSim`). It brings the
introductory game into Unity:
- a 3D tooth model generated from the same anatomy dataset
- the digital twin of the procedure (access, working length, cleaning & shaping, filling), written in C#
- an AR guidance layer with anatomy-anchored overlays and adaptive prompts
- automated tests for the twin logic and the scene

**Open it:** in Unity Hub choose **Add → Add project from disk → `unity/SurgiFlowDST`**, open it with Unity
**6000.6.4f1**, load `Assets/Scenes/EndoSim` and press **Play**. Press **F1** in the game for the controls.

## Research
Background notes on surgical digital twins and how the DST will be structured are in [`docs/research/`](docs/research/).
Clinical values that still need checking by a clinician are listed in [`docs/review-queue.md`](docs/review-queue.md).

## Repository layout
```
intro-root-canal-game/   Introductory root canal interactive game (Python)
unity/SurgiFlowDST/      SurgiFlow DST in Unity / C# (under development)
docs/research/           Research notes on surgical digital twins
docs/images/             Screenshots
```
