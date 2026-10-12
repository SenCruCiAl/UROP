// EndoTwin.cs
// Digital Surgical Twin of a root canal procedure. Pure C# (no MonoBehaviour) so it can be unit-tested.
// Port of intro-root-canal-game/src/twin.py. The twin mirrors tooth + instrument state and is driven only
// by the user's inputs. It also keeps a time-series log for replay / analysis.
//
// CLINICIAN-REVIEW: every threshold/gain in this file (stress gain, debris gain, penalties, fail distances)
// is GAME TUNING, not a clinical value. They are collected as constants so a clinician can review them.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace SurgiFlow.Endo
{
    public enum Stage { Access, WorkingLength, Shaping, Obturation, Complete, Failed }
    public enum InstrumentKind { Bur, KFile, GuttaPercha }

    public static class StageText
    {
        public static string Name(Stage s)
        {
            switch (s)
            {
                case Stage.Access: return "Access opening";
                case Stage.WorkingLength: return "Working length";
                case Stage.Shaping: return "Cleaning & shaping";
                case Stage.Obturation: return "Obturation";
                case Stage.Complete: return "Complete";
                default: return "Failed";
            }
        }
        public static string Name(InstrumentKind k) => k == InstrumentKind.Bur ? "Bur" : k == InstrumentKind.KFile ? "K-file" : "Gutta-percha";
    }

    [Serializable] public sealed class TwinEvent { public float t; public int level; public string text; }
    [Serializable] public sealed class Penalty { public int points; public string reason; }

    [Serializable]
    public sealed class SessionSummary
    {
        public string tooth; public int patient_seed; public float canal_length_mm, curvature_deg, target_wl_mm, recorded_wl_mm;
        public string final_stage, fail_reason; public List<int> files_used; public float cleanliness_pct, peak_stress_pct;
        public int score; public List<Penalty> penalties; public List<TwinEvent> events;
    }

    public sealed class EndoTwin
    {
        // ---- Game-tuning constants (CLINICIAN-REVIEW: none of these are clinical values) ----
        public const float BurRadiusMm = 0.8f;
        public const float FloorFailMargin = 0.4f;       // bur deeper than chamber floor + this => perforation
        public const float StressGain = 1.6f, DebrisGain = 2.2f, StressDecayPerS = 18f;
        public const float DebrisBlockPct = 85f, DebrisBlockSetback = 2.5f;
        public const float ApexOverrunMm = 0.05f;        // tip beyond apex + this => over-instrumentation timer
        public static readonly int[] IsoSequence = { 15, 20, 25, 30, 35, 40 };

        public readonly PatientTooth P;
        public Stage Stage = Stage.Access;
        public float Time;
        public float Depth;                      // instrument tip depth (mm from reference)
        public float MaxDepthThisFile;
        public InstrumentKind Instrument = InstrumentKind.Bur;
        public int FileSize = 10;
        public readonly int Maf;                 // master apical file
        public readonly Queue<int> FileQueue = new Queue<int>();
        public readonly List<int> FilesDone = new List<int>();

        public float AccessDepth;
        public float? RecordedWl;
        public float WlError;

        public readonly float[] ShapedRadius, Cleaned;
        public float Debris, Stress, PeakStress;
        public bool Irrigating, IrrigatedSinceFile = true;
        public float FillLevel;                  // 0..1
        public float OverInstrumentationS;
        public readonly List<Penalty> Penalties = new List<Penalty>();
        public int Errors;
        public readonly List<TwinEvent> Events = new List<TwinEvent>();
        public string FailReason;
        public float FailDepth;
        public float ApexNoise;

        private readonly SeededRng _rng;
        private float _logTimer;
        private bool _obturatedAnnounced;
        private readonly StringBuilder _csv = new StringBuilder();

        public EndoTwin(PatientTooth patient)
        {
            P = patient;
            Maf = patient.CurvatureDeg > 20f ? 30 : 40;      // CLINICIAN-REVIEW: master apical file policy
            foreach (int s in IsoSequence) if (s <= Maf) FileQueue.Enqueue(s);
            ShapedRadius = (float[])patient.OrigRadius.Clone();
            Cleaned = new float[patient.Count];
            _rng = new SeededRng(patient.Seed + 7);
            _csv.AppendLine("t_s,stage,instrument,file_iso,depth_mm,apex_locator_mm,stress_pct,debris_pct,cleanliness_pct,fill_pct,irrigating");
            Log(0, $"Session started: {patient.Spec.Label}");
        }

        // ------------------------------------------------------------------ helpers
        public void Log(int level, string text)
        {
            Events.Add(new TwinEvent { t = Time, level = level, text = text });
            if (level >= 2) Errors++;
        }

        private void Penalise(int pts, string reason)
        {
            Penalties.Add(new Penalty { points = pts, reason = reason });
            Log(2, $"-{pts}: {reason}");
        }

        public float Wl => RecordedWl ?? P.TargetWl;
        public bool InCanal => Depth > P.ChamberFloor;
        public bool IsOver => Stage == Stage.Complete || Stage == Stage.Failed;

        /// <summary>Simulated electronic apex locator reading (mm to apex, 0 = APEX). Null if no reading.</summary>
        public float? ApexLocator()
        {
            if (Instrument == InstrumentKind.Bur || !InCanal) return null;
            return P.Apex - Depth + ApexNoise;
        }

        public float FileTipRadius => FileSize / 200f;

        public float Cleanliness()
        {
            int i0 = P.Index(P.ChamberFloor), i1 = P.Index(P.TargetWl);
            if (i1 <= i0) return 0f;
            float sum = 0f;
            for (int i = i0; i < i1; i++) sum += Cleaned[i];
            return sum / (i1 - i0);
        }

        public int Score()
        {
            int s = 100;
            foreach (var p in Penalties) s -= p.points;
            float c = Cleanliness();
            if (c < 0.85f && (Stage == Stage.Obturation || Stage == Stage.Complete)) s -= (int)((0.85f - c) * 60f);
            return Mathf.Max(0, s);
        }

        // ------------------------------------------------------------------ user actions
        /// <summary>User moved the instrument by delta mm (positive = deeper).</summary>
        public void Move(float delta, bool fine)
        {
            if (Stage == Stage.Complete || Stage == Stage.Failed || Stage == Stage.Obturation) return;
            float limit = P.Apex + 2f;
            if (Stage == Stage.Shaping && Debris >= DebrisBlockPct && delta > 0)
            {
                float block = Wl - DebrisBlockSetback;
                if (Depth + delta > block)
                {
                    if (Depth < block) Log(1, "Debris blockage - irrigate to clear the canal");
                    delta = Mathf.Max(0f, block - Depth);
                }
            }
            float nd = Mathf.Clamp(Depth + delta, 0f, limit);
            float moved = nd - Depth, old = Depth;
            Depth = nd;
            if (Mathf.Approximately(moved, 0f)) return;

            if (Stage == Stage.Access)
            {
                AccessDepth = Mathf.Max(AccessDepth, Depth);
                if (Depth > P.ChamberFloor + FloorFailMargin) Fail("Pulp-chamber floor perforated by the bur");
                return;
            }

            // Files: cutting on the down-stroke raises instrument stress
            if (moved > 0 && InCanal)
            {
                float curve = Depth > P.CurveStart ? 1 + P.CurvatureDeg / 15f : 1f;
                float speed = fine ? 1.0f : 1.35f;
                float size = FileSize / 15f;
                Stress += moved * size * curve * (1 + Debris / 100f) * speed * StressGain;
                PeakStress = Mathf.Max(PeakStress, Stress);
                if (Stress >= 100f) { Fail($"ISO {FileSize} file separated in the canal (instrument fatigue)"); return; }
            }

            if (Stage == Stage.Shaping && InCanal)
            {
                MaxDepthThisFile = Mathf.Max(MaxDepthThisFile, Depth);
                Shape(old, nd);
                Debris = Mathf.Min(100f, Debris + Mathf.Abs(moved) * FileSize / 40f * DebrisGain);
            }
        }

        private void Shape(float a, float b)
        {
            float lo = Mathf.Min(a, b), hi = Mathf.Max(a, b), tip = Depth;
            // Every point of the canal currently occupied by the file is enlarged to the file's taper
            int i0 = P.Index(P.ChamberFloor), i1 = P.Index(Mathf.Min(tip, P.Apex));
            for (int i = i0; i <= i1; i++)
            {
                float r = FileTipRadius + 0.01f * (tip - P.Depths[i]);      // ISO 0.02 taper -> radius 0.01/mm
                ShapedRadius[i] = Mathf.Max(ShapedRadius[i], Mathf.Min(r, 0.9f));
            }
            int j0 = P.Index(Mathf.Max(lo, P.ChamberFloor)), j1 = P.Index(Mathf.Min(hi, P.Apex));
            for (int j = j0; j <= j1; j++) Cleaned[j] = Mathf.Min(1f, Cleaned[j] + 0.06f * FileSize / 15f);
        }

        public void RecordWl()
        {
            if (Stage != Stage.WorkingLength) return;
            if (!InCanal) { Log(1, "Insert the #10 file into the canal before recording WL"); return; }
            RecordedWl = Mathf.Round(Depth * 100f) / 100f;
            WlError = RecordedWl.Value - P.TargetWl;
            float e = Mathf.Abs(WlError);
            string q = e <= 0.5f ? "excellent" : e <= 1.0f ? "acceptable" : "poor";       // CLINICIAN-REVIEW tolerance
            Log(e <= 1.0f ? 0 : 2, $"WL recorded {RecordedWl:F2} mm ({q}, error {WlError:+0.00;-0.00})");
        }

        public void NextStage()
        {
            switch (Stage)
            {
                case Stage.Access:
                    if (AccessDepth < P.ChamberRoof) { Log(1, "Pulp chamber not reached yet"); return; }
                    Stage = Stage.WorkingLength; Instrument = InstrumentKind.KFile; FileSize = 10; Depth = 0f;
                    int a = P.Index(P.ChamberRoof), b = P.Index(P.ChamberFloor);
                    for (int i = a; i <= b; i++) Cleaned[i] = 1f;       // coronal pulp removed after de-roofing
                    Log(0, "Access complete -> working-length determination with ISO #10 K-file");
                    break;
                case Stage.WorkingLength:
                    if (RecordedWl == null) { Log(1, "Record working length (W) first"); return; }
                    float e = Mathf.Abs(WlError);
                    if (e > 0.5f) Penalise(Mathf.RoundToInt((e - 0.5f) * 12f), $"WL error {WlError:+0.00;-0.00} mm");
                    Stage = Stage.Shaping; FileSize = FileQueue.Dequeue();
                    Depth = 0f; MaxDepthThisFile = 0f; Stress = 0f;
                    Log(0, $"Shaping: ISO #{FileSize} to WL {Wl:F2} mm (MAF #{Maf})");
                    break;
                case Stage.Shaping: NextFile(); break;
                case Stage.Obturation:
                    if (FillLevel < 1f) { Log(1, "Canal not fully obturated"); return; }
                    Stage = Stage.Complete;
                    Log(0, $"Procedure complete - score {Score()}");
                    break;
            }
        }

        public void NextFile()
        {
            if (Stage != Stage.Shaping) return;
            if (MaxDepthThisFile < Wl - 0.5f) { Log(1, $"#{FileSize} has not reached WL ({MaxDepthThisFile:F1}/{Wl:F1} mm)"); return; }
            if (!IrrigatedSinceFile) Penalise(4, $"No irrigation after #{FileSize}");
            FilesDone.Add(FileSize);
            IrrigatedSinceFile = false;
            if (FileQueue.Count > 0)
            {
                FileSize = FileQueue.Dequeue();
                Depth = 0f; MaxDepthThisFile = 0f; Stress = 0f;
                Log(0, $"Switched to ISO #{FileSize}");
            }
            else
            {
                if (Debris > 30f) Penalise(6, "Obturating a canal with residual debris (voids)");
                Stage = Stage.Obturation; Instrument = InstrumentKind.GuttaPercha; Depth = 0f;
                Log(0, "Shaping finished -> obturation (hold O)");
            }
        }

        private void Fail(string reason)
        {
            Stage = Stage.Failed; FailReason = reason; FailDepth = Depth;
            Log(3, reason);
        }

        // ------------------------------------------------------------------ per-frame update
        public void Tick(float dt, bool irrigating, bool filling, bool moving)
        {
            Time += dt;
            Irrigating = irrigating;
            if (IsOver) return;
            if (!moving) Stress = Mathf.Max(0f, Stress - StressDecayPerS * dt);
            ApexNoise += (_rng.Gauss(0f, 0.05f) - ApexNoise) * Mathf.Min(1f, dt * 4f);

            if (irrigating && (Stage == Stage.Shaping || Stage == Stage.WorkingLength || Stage == Stage.Obturation))
            {
                Debris = Mathf.Max(0f, Debris - 45f * dt);
                IrrigatedSinceFile = true;
                for (int i = 0; i < Cleaned.Length; i++)
                    if (ShapedRadius[i] > P.OrigRadius[i] + 0.02f) Cleaned[i] = Mathf.Min(1f, Cleaned[i] + 0.25f * dt);
            }

            if (Depth > P.Apex + ApexOverrunMm && Instrument != InstrumentKind.Bur)
            {
                OverInstrumentationS += dt;
                if (OverInstrumentationS > 1f)
                {
                    Penalise(5, "Instrument pushed beyond the apex (over-instrumentation)");
                    OverInstrumentationS = -2f;      // cool-down before the next penalty
                }
            }

            if (Stage == Stage.Obturation && filling)
            {
                FillLevel = Mathf.Min(1f, FillLevel + 0.14f * dt);
                if (FillLevel >= 1f && !_obturatedAnnounced) { _obturatedAnnounced = true; Log(0, "Canal obturated - press ENTER to finish"); }
            }

            _logTimer += dt;
            if (_logTimer >= 0.1f)
            {
                _logTimer = 0f;
                float? loc = ApexLocator();
                var ci = CultureInfo.InvariantCulture;
                _csv.Append(Time.ToString("F2", ci)).Append(',').Append(StageText.Name(Stage)).Append(',')
                    .Append(StageText.Name(Instrument)).Append(',').Append(FileSize).Append(',')
                    .Append(Depth.ToString("F2", ci)).Append(',').Append(loc.HasValue ? loc.Value.ToString("F2", ci) : "").Append(',')
                    .Append(Stress.ToString("F1", ci)).Append(',').Append(Debris.ToString("F1", ci)).Append(',')
                    .Append((Cleanliness() * 100f).ToString("F1", ci)).Append(',').Append((FillLevel * 100f).ToString("F1", ci)).Append(',')
                    .Append(irrigating ? 1 : 0).AppendLine();
            }
        }

        // ------------------------------------------------------------------ session output
        public SessionSummary Summary() => new SessionSummary
        {
            tooth = P.Spec.Label, patient_seed = P.Seed, canal_length_mm = P.CanalLength,
            curvature_deg = Mathf.Round(P.CurvatureDeg * 10f) / 10f, target_wl_mm = P.TargetWl,
            recorded_wl_mm = RecordedWl ?? -1f, final_stage = StageText.Name(Stage), fail_reason = FailReason ?? "",
            files_used = new List<int>(FilesDone), cleanliness_pct = Mathf.Round(Cleanliness() * 1000f) / 10f,
            peak_stress_pct = Mathf.Round(PeakStress * 10f) / 10f, score = Score(),
            penalties = new List<Penalty>(Penalties), events = new List<TwinEvent>(Events)
        };

        /// <summary>Writes session_*.csv and session_*_summary.json (same columns as the Python game). Returns the JSON path.</summary>
        public string Save(string dir = null)
        {
            dir = dir ?? Path.Combine(Application.persistentDataPath, "SurgiFlow");
            Directory.CreateDirectory(dir);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            File.WriteAllText(Path.Combine(dir, $"session_{stamp}.csv"), _csv.ToString());
            string json = Path.Combine(dir, $"session_{stamp}_summary.json");
            File.WriteAllText(json, JsonUtility.ToJson(Summary(), true));
            return json;
        }
    }
}
