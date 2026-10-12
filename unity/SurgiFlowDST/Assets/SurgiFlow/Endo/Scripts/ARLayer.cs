// ARLayer.cs
// Simulated AR layer + adaptive prompts. Port of intro-root-canal-game/src/ar.py.
//
// Azuma's three AR properties as implemented here:
//   1. real + virtual   -> the (simulated) tooth section is overlaid with virtual guidance graphics
//   2. real-time        -> overlays and prompts update every frame from the twin state
//   3. registered in 3D -> every overlay is anchored in the tooth frame and drawn through a *tracked* pose;
//                          tracking error / latency can be injected (R) to show what mis-registration looks like.
//
// CLINICIAN-REVIEW: the 0.5 mm registration-confidence gate and all prompt thresholds are game tuning.
using System.Collections.Generic;
using UnityEngine;

namespace SurgiFlow.Endo
{
    /// <summary>Simulates the real patient motion (true pose) and an optical tracker's estimate of it (est pose).</summary>
    public sealed class Tracker
    {
        public bool PatientMotion;                       // jaw moves (M)
        public bool Degraded;                            // inject latency + bias + jitter (R)
        public float LatencyS = 0.25f;
        public Vector2 BiasMm = new Vector2(1.1f, -0.6f);
        public float JitterMm = 0.075f;
        public const float ConfidenceGateMm = 0.5f;      // CLINICIAN-REVIEW: game tuning

        public Vector2 TruePoseMm { get; private set; }  // tooth-frame origin offset in mm
        public Vector2 EstPoseMm { get; private set; }

        private float _t;
        private readonly Queue<(float t, Vector2 p)> _history = new Queue<(float, Vector2)>();
        private readonly SeededRng _rng = new SeededRng(3);

        public void Update(float dt)
        {
            _t += dt;
            Vector2 p = Vector2.zero;
            if (PatientMotion)
            {
                p.x = 0.6f * Mathf.Sin(_t * 1.3f) + 0.2f * Mathf.Sin(_t * 3.7f);
                p.y = 0.35f * Mathf.Sin(_t * 0.9f + 1f);
            }
            TruePoseMm = p;
            _history.Enqueue((_t, p));
            while (_history.Count > 0 && _history.Peek().t < _t - 1f) _history.Dequeue();

            if (Degraded)
            {
                float target = _t - LatencyS;
                Vector2 h = p;
                foreach (var (ht, hp) in _history) if (ht >= target) { h = hp; break; }
                h += BiasMm + new Vector2(_rng.Gauss(0f, JitterMm), _rng.Gauss(0f, JitterMm));
                EstPoseMm = h;
            }
            else EstPoseMm = p;
        }

        public float RegistrationErrorMm => (EstPoseMm - TruePoseMm).magnitude;
    }

    public sealed class Prompt
    {
        public int Priority; public string Novice, Expert; public Vector2? Anchor; public int Level;   // 0 info, 1 warn, 2 danger
        public Prompt(int priority, string novice, string expert, Vector2? anchor = null, int level = 0)
        { Priority = priority; Novice = novice; Expert = expert; Anchor = anchor; Level = level; }
    }

    /// <summary>Adaptive prompts: content depends on stage, live twin state, the user's expertise setting,
    /// their error history and the AR registration quality.</summary>
    public sealed class PromptEngine
    {
        public bool Expert;
        public Prompt Current;
        private float _hold;

        /// <summary>An 'expert' who keeps making errors is switched back to guided prompts (adaptive).</summary>
        public bool NoviceWording(EndoTwin t) => !Expert || t.Errors >= 3;

        public List<Prompt> Candidates(EndoTwin t, float regErrMm)
        {
            var p = t.P; var o = new List<Prompt>();
            var tip = new Vector2(p.CenterlineX(t.Depth), t.Depth);

            if (regErrMm > Tracker.ConfidenceGateMm)
                o.Add(new Prompt(95, $"AR registration error {regErrMm:F1} mm - overlays are not trustworthy. Press R to restore tracking.",
                                 $"Reg. err {regErrMm:F1} mm", null, 2));
            if (t.Stage == Stage.Failed)
            { o.Add(new Prompt(100, $"FAILED: {t.FailReason}. Press F5 to retry.", "FAILED - F5", tip, 2)); return o; }
            if (t.Stage == Stage.Complete)
            { o.Add(new Prompt(100, $"Procedure complete. Score {t.Score()}/100. Press F5 for a new patient.", $"Done {t.Score()}/100")); return o; }

            switch (t.Stage)
            {
                case Stage.Access:
                    if (t.Depth < p.ChamberRoof)
                        o.Add(new Prompt(10, $"Drill (DOWN arrow / wheel) through enamel & dentin. Pulp roof in {p.ChamberRoof - t.Depth:F1} mm.",
                                         $"Roof -{p.ChamberRoof - t.Depth:F1}", new Vector2(0, p.ChamberRoof)));
                    else
                        o.Add(new Prompt(20, "Pulp chamber reached. Stop! Press ENTER to begin working length.", "Chamber OK - ENTER", new Vector2(0, p.ChamberRoof)));
                    if (t.Depth > p.ChamberFloor - 0.6f)
                        o.Add(new Prompt(90, "DANGER: bur is at the chamber floor - withdraw (UP) now!", "FLOOR!", new Vector2(0, p.ChamberFloor), 2));
                    break;

                case Stage.WorkingLength:
                    float? loc = t.ApexLocator();
                    if (loc == null) o.Add(new Prompt(10, "Advance the #10 K-file into the canal orifice.", "Enter canal", new Vector2(0, p.ChamberFloor)));
                    else if (loc > 2f) o.Add(new Prompt(10, $"Keep advancing. Apex locator: {loc:F1} mm. Hold SHIFT for fine control near the apex.", $"EAL {loc:F1}", tip));
                    else if (loc > 0.3f) o.Add(new Prompt(40, $"Approaching constriction ({loc:F1} mm). Target reading 0.5 -> press W to record.", $"EAL {loc:F1} - W", tip, 1));
                    else if (loc >= 0f) o.Add(new Prompt(50, "At apical constriction. Press W to record working length, then ENTER.", "Record W", tip));
                    else o.Add(new Prompt(80, "Past the apex! Withdraw the file (UP).", "Over apex!", tip, 2));
                    if (t.RecordedWl.HasValue)
                        o.Add(new Prompt(30, $"WL recorded {t.RecordedWl:F2} mm. ENTER to start shaping (or re-record).", "ENTER", new Vector2(0, t.RecordedWl.Value)));
                    break;

                case Stage.Shaping:
                    float wl = t.Wl;
                    if (t.Stress > 70f) o.Add(new Prompt(85, $"File stress {t.Stress:F0}% - pause (release keys) or withdraw to avoid separation.", $"Stress {t.Stress:F0}%", tip, 2));
                    if (t.Debris > 60f) o.Add(new Prompt(70, $"Debris {t.Debris:F0}% - irrigate (hold I) with NaOCl.", "Irrigate", tip, 1));
                    if (t.Depth > wl + 0.3f) o.Add(new Prompt(80, "Beyond working length - withdraw.", "> WL", tip, 2));
                    if (t.MaxDepthThisFile >= wl - 0.5f)
                    {
                        if (!t.IrrigatedSinceFile) o.Add(new Prompt(35, $"#{t.FileSize} reached WL. Irrigate (I), then N for next file.", "I then N", tip));
                        else o.Add(new Prompt(30, $"#{t.FileSize} at WL. Press N for the next file.", "N", tip));
                    }
                    else
                    {
                        if (p.CurvatureDeg > 15f && t.Depth > p.CurveStart - 1f)
                            o.Add(new Prompt(25, $"Canal curves {p.CurvatureDeg:F0} deg. Use SHIFT (slow) and short strokes.", $"Curve {p.CurvatureDeg:F0} deg",
                                             new Vector2(p.CenterlineX(p.CurveStart + 1), p.CurveStart + 1), 1));
                        o.Add(new Prompt(10, $"Advance ISO #{t.FileSize} to WL {wl:F1} mm ({Mathf.Max(0f, wl - t.Depth):F1} mm to go).", $"#{t.FileSize} -> {wl:F1}", tip));
                    }
                    break;

                case Stage.Obturation:
                    if (t.FillLevel < 1f)
                        o.Add(new Prompt(10, $"Hold O to condense gutta-percha. Fill {t.FillLevel * 100:F0}%.", $"Fill {t.FillLevel * 100:F0}%", new Vector2(p.CenterlineX(p.TargetWl), p.TargetWl)));
                    else
                        o.Add(new Prompt(20, "Canal sealed. Press ENTER to complete.", "ENTER", new Vector2(0, p.ChamberFloor)));
                    break;
            }
            return o;
        }

        public void Update(float dt, EndoTwin t, float regErrMm)
        {
            var c = Candidates(t, regErrMm);
            c.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            _hold -= dt;
            Prompt best = c.Count > 0 ? c[0] : null;
            // Hysteresis: keep a prompt at least 0.6 s unless something more urgent appears
            if (best != null && (Current == null || _hold <= 0f || best.Priority >= Current.Priority))
            {
                if (Current == null || best.Novice != Current.Novice) _hold = 0.6f;
                Current = best;
            }
        }

        public string Text(Prompt pr, EndoTwin t) => NoviceWording(t) ? pr.Novice : pr.Expert;
    }
}
