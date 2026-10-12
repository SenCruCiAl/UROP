// ToothData.cs
// Tooth dataset + patient-specific canal geometry. Port of intro-root-canal-game/src/anatomy.py.
//
// UNITS: everything here is in MILLIMETRES in the "tooth frame":
//   x = mesio-distal (lateral) offset, d = depth from the incisal/occlusal reference point (positive = apical).
// Conversion to Unity world units happens ONLY at the render boundary (see Units).
//
// CLINICIAN-REVIEW: all numbers below come from the project's CSV (published means/SDs, secondary sources)
// or are game-tuning values. None are verified clinical rules. See docs/review-queue.md.
using System;
using UnityEngine;

namespace SurgiFlow.Endo
{
    /// <summary>The single place where millimetres become Unity units (1 Unity unit = 10 mm).</summary>
    public static class Units
    {
        public const float MmToUnity = 0.1f;

        /// <summary>Tooth-frame (x, depth) in mm -> Unity local position (y up, depth goes down).</summary>
        public static Vector3 ToothToLocal(float xMm, float depthMm, float zMm = 0f)
            => new Vector3(xMm * MmToUnity, -depthMm * MmToUnity, zMm * MmToUnity);
    }

    /// <summary>Small deterministic RNG so the same seed always gives the same "patient" and session.</summary>
    public sealed class SeededRng
    {
        private readonly System.Random _r;
        public SeededRng(int seed) { _r = new System.Random(seed); }
        public double Next() => _r.NextDouble();
        public float Gauss(float mean, float sd)
        {
            double u1 = 1.0 - _r.NextDouble(), u2 = _r.NextDouble();
            double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            return (float)(mean + sd * z);
        }
    }

    /// <summary>Population statistics for one tooth/canal (one row of root_canal_anatomy.csv).</summary>
    public sealed class ToothSpec
    {
        public string Tooth, Canal, Source;
        public float MeanLength, SdLength, CrownLength, CrownWidth, CurvatureDeg, CurvatureSd, ApicalDiameter;
        public string Label => $"{Tooth} ({Canal} canal)";

        private static ToothSpec Make(string tooth, string canal, float len, float sd, float crownL, float crownW,
                                      float curv, float curvSd, float apical, string src) => new ToothSpec
        {
            Tooth = tooth, Canal = canal, MeanLength = len, SdLength = sd, CrownLength = crownL, CrownWidth = crownW,
            CurvatureDeg = curv, CurvatureSd = curvSd, ApicalDiameter = apical, Source = src
        };

        // CLINICIAN-REVIEW: values copied from intro-root-canal-game/data/root_canal_anatomy.csv (single-canal teeth only).
        public static readonly ToothSpec[] Library =
        {
            Make("Maxillary central incisor", "Single", 21.8f, 1.6f, 10.5f, 8.5f, 5f, 2f, 0.40f,
                 "researchgate.net root-canal-length-of-the-anterior-upper-teeth (secondary)"),
            Make("Maxillary canine", "Single", 23.4f, 2.3f, 10.0f, 7.5f, 8f, 3f, 0.35f,
                 "researchgate.net root-canal-length-of-the-anterior-upper-teeth (secondary)"),
            Make("Mandibular central incisor", "Labial", 20.71f, 1.69f, 9.0f, 5.4f, 7f, 3f, 0.30f,
                 "pmc.ncbi.nlm.nih.gov/articles/PMC11628873 (search summary)"),
        };
    }

    /// <summary>A single simulated patient: landmarks and canal profile sampled from the population statistics.</summary>
    public sealed class PatientTooth
    {
        public const float SampleStep = 0.1f;          // mm resolution of the canal model
        public const float ApicalOffsetMm = 0.5f;      // CLINICIAN-REVIEW: AAE range is 0.5-1.0 mm short of the apex
        public const float ChamberHalfWidth = 1.7f;    // CLINICIAN-REVIEW: game value

        public readonly ToothSpec Spec;
        public readonly int Seed;
        public readonly float CanalLength, CurvatureDeg, CrownLength, CrownWidth, ApicalDiameter;
        public readonly float Apex, TargetWl, ChamberRoof, ChamberFloor, CurveStart;
        public readonly float[] Depths, OrigRadius;

        public PatientTooth(ToothSpec spec, int? seed = null)
        {
            Spec = spec;
            Seed = seed ?? new System.Random().Next(1_000_000);
            var rng = new SeededRng(Seed);
            float lo = spec.MeanLength - 2 * spec.SdLength, hi = spec.MeanLength + 2 * spec.SdLength;
            CanalLength = Mathf.Round(Mathf.Clamp(rng.Gauss(spec.MeanLength, spec.SdLength), lo, hi) * 100f) / 100f;
            CurvatureDeg = Mathf.Max(0f, rng.Gauss(spec.CurvatureDeg, spec.CurvatureSd));
            CrownLength = spec.CrownLength;
            CrownWidth = spec.CrownWidth;
            ApicalDiameter = spec.ApicalDiameter;

            Apex = CanalLength;                                    // radiographic apex
            TargetWl = Mathf.Round((Apex - ApicalOffsetMm) * 100f) / 100f;
            ChamberRoof = CrownLength * 0.55f;
            ChamberFloor = CrownLength + 1.5f;
            CurveStart = ChamberFloor + 0.55f * (Apex - ChamberFloor);

            int n = Mathf.CeilToInt((Apex + 2.5f) / SampleStep);
            Depths = new float[n];
            OrigRadius = new float[n];
            for (int i = 0; i < n; i++)
            {
                Depths[i] = i * SampleStep;
                OrigRadius[i] = OrigRadiusAt(Depths[i]);
            }
        }

        public int Count => Depths.Length;
        public int Index(float d) => Mathf.Clamp(Mathf.RoundToInt(d / SampleStep), 0, Depths.Length - 1);

        /// <summary>Canal centerline lateral offset (mm) at depth d.</summary>
        public float CenterlineX(float d)
        {
            if (d <= CurveStart) return 0f;
            float span = Mathf.Max(1f, Apex - CurveStart);
            float k = Mathf.Tan(CurvatureDeg * Mathf.Deg2Rad) / (2f * span);
            return k * (d - CurveStart) * (d - CurveStart);
        }

        private float OrigRadiusAt(float d)
        {
            if (d < ChamberRoof || d > Apex) return 0f;
            if (d <= ChamberFloor) return ChamberHalfWidth;
            float t = (d - ChamberFloor) / (Apex - ChamberFloor);
            return 0.55f * (1 - t) + (ApicalDiameter / 2f) * t;
        }

        /// <summary>Outer half-width of the tooth (mm) at depth d.</summary>
        public float HalfWidth(float d)
        {
            float cl = CrownLength, w = CrownWidth, rootCej = w * 0.34f;
            if (d <= cl)
            {
                float t = d / cl;
                if (t < 0.5f) return w / 2f * (0.78f + 0.22f * Mathf.Sin(Mathf.PI * t));
                return w / 2f + (rootCej - w / 2f) * Mathf.Pow((t - 0.5f) / 0.5f, 1.4f);
            }
            if (d > Apex + 0.5f) return 0f;
            if (d > Apex) return Mathf.Lerp(0.9f, 0.05f, (d - Apex) / 0.5f);   // rounded root tip
            float tt = Mathf.Min(1f, (d - cl) / (Apex - cl));
            return tt < 1f ? rootCej * Mathf.Pow(1 - tt, 0.8f) + 0.9f * tt : 0.9f;
        }

        /// <summary>Enamel thickness (mm) at depth d. CLINICIAN-REVIEW: illustrative, not measured.</summary>
        public float EnamelThickness(float d) => d >= CrownLength ? 0f : Mathf.Lerp(1.0f, 0.15f, d / CrownLength);
    }
}
