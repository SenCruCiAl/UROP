// ToothModel.cs
// Builds and updates the procedural 3D "cutaway" tooth from the twin's state. No imported assets, so it is
// light enough for an 8 GB laptop (~10k vertices in total, updated at <= 30 Hz).
//
// Scene layout (Unity units; 1 unit = 10 mm, see Units):
//   - Outer shell   : the back half (z <= 0) of the revolved tooth surface   (enamel crown / cementum root)
//   - Cut face      : the flat section plane at z = 0: enamel, dentin and the cavity (pulp / canal / bur hole)
//   - Canal wall    : the back half of the cavity tube, vertex-coloured by pulp / cleaned / debris / irrigant / gutta-percha
//   - Bone + PDL    : flat section of the periodontal ligament and alveolar bone around the root
// The viewer looks from +z, so the half-removed tooth shows its inside, like a sectioned teaching model.
using UnityEngine;

namespace SurgiFlow.Endo
{
    public sealed class ToothModel : MonoBehaviour
    {
        // Colours (illustrative, not clinical).
        static readonly Color Enamel = new Color(0.96f, 0.96f, 0.93f);
        static readonly Color DentinOuter = new Color(0.85f, 0.72f, 0.46f);
        static readonly Color DentinInner = new Color(0.94f, 0.85f, 0.62f);
        static readonly Color Cementum = new Color(0.88f, 0.80f, 0.60f);
        static readonly Color Pdl = new Color(0.62f, 0.30f, 0.30f);
        static readonly Color BoneInner = new Color(0.90f, 0.82f, 0.70f);
        static readonly Color BoneCortical = new Color(0.78f, 0.70f, 0.58f);
        static readonly Color Pulp = new Color(0.80f, 0.30f, 0.34f);
        static readonly Color CleanWall = new Color(0.93f, 0.88f, 0.76f);
        static readonly Color ShapedWall = new Color(0.86f, 0.83f, 0.78f);
        static readonly Color DebrisCol = new Color(0.34f, 0.24f, 0.12f);
        static readonly Color Irrigant = new Color(0.42f, 0.72f, 0.98f);
        static readonly Color Gutta = new Color(0.96f, 0.58f, 0.66f);
        static readonly Color FailCol = new Color(1.0f, 0.15f, 0.1f);

        const int RingSegs = 12;                     // half-ring resolution of the canal tube / shell
        const int CutVertsPerRow = 8;
        const float BoneHalfWidth = 9.5f, PdlThickness = 0.3f, BoneBelowApex = 2.4f;

        [SerializeField] Material material;
        public Material Material { get => material; set => material = value; }

        PatientTooth _t;
        Mesh _shell, _cut, _canal, _bone;
        Vector3[] _cutV, _canalV; Color[] _cutC, _canalC;
        int _rows;
        float[] _hw, _et;

        public PatientTooth Tooth => _t;

        public void Build(PatientTooth tooth)
        {
            _t = tooth;
            _rows = Mathf.Min(_t.Count, _t.Index(_t.Apex + BoneBelowApex) + 1);
            _hw = new float[_rows]; _et = new float[_rows];
            for (int i = 0; i < _rows; i++) { _hw[i] = _t.HalfWidth(_t.Depths[i]); _et[i] = _t.EnamelThickness(_t.Depths[i]); }

            BuildShell(); BuildBone(); BuildCutTopology(); BuildCanalTopology();
        }

        // ------------------------------------------------------------ static meshes
        MeshFilter Child(string name, ref Mesh mesh)
        {
            var tr = transform.Find(name);
            GameObject go = tr ? tr.gameObject : new GameObject(name);
            go.transform.SetParent(transform, false);
            var mf = go.GetComponent<MeshFilter>(); if (mf == null) mf = go.AddComponent<MeshFilter>();     // (no ?? : Unity fake-null)
            var mr = go.GetComponent<MeshRenderer>(); if (mr == null) mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            if (mf.sharedMesh != null) Destroy(mf.sharedMesh);      // F5 restarts rebuild: don't leak the old mesh
            mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mf.sharedMesh = mesh;
            return mf;
        }

        void BuildShell()
        {
            Child("OuterShell", ref _shell);
            int step = 2, rows = (_t.Index(_t.Apex + 0.5f) / step) + 1;
            var v = new Vector3[rows * (RingSegs + 1)]; var n = new Vector3[v.Length]; var c = new Color[v.Length];
            for (int r = 0; r < rows; r++)
            {
                int i = r * step; float d = _t.Depths[i], cx = _t.CenterlineX(d), hw = _hw[i];
                for (int k = 0; k <= RingSegs; k++)
                {
                    float th = Mathf.PI * k / RingSegs, cs = Mathf.Cos(th), sn = Mathf.Sin(th);
                    int idx = r * (RingSegs + 1) + k;
                    v[idx] = Units.ToothToLocal(cx + hw * cs, d, -hw * sn);
                    n[idx] = new Vector3(cs, 0, -sn);
                    c[idx] = d < _t.CrownLength ? Enamel : Cementum;
                }
            }
            _shell.SetVertices(v); _shell.SetNormals(n); _shell.SetColors(c);
            _shell.SetTriangles(GridIndices(rows, RingSegs + 1), 0);
            _shell.RecalculateBounds();
        }

        void BuildBone()
        {
            Child("BonePdl", ref _bone);
            int i0 = _t.Index(_t.CrownLength + 1.5f), rows = _rows - i0;
            var v = new Vector3[rows * 6]; var n = new Vector3[v.Length]; var c = new Color[v.Length];
            for (int r = 0; r < rows; r++)
            {
                int i = i0 + r; float d = _t.Depths[i], cx = _t.CenterlineX(d), hw = _hw[i];
                float oL = cx - hw, oR = cx + hw;
                Vector3[] p = {
                    new Vector3(-BoneHalfWidth, d, 0), new Vector3(oL - PdlThickness, d, 0), new Vector3(oL, d, 0),
                    new Vector3(oR, d, 0), new Vector3(oR + PdlThickness, d, 0), new Vector3(BoneHalfWidth, d, 0) };
                Color[] col = { BoneCortical, BoneInner, Pdl, Pdl, BoneInner, BoneCortical };
                for (int k = 0; k < 6; k++) { v[r * 6 + k] = Units.ToothToLocal(p[k].x, p[k].y, -0.02f); n[r * 6 + k] = Vector3.forward; c[r * 6 + k] = col[k]; }
            }
            var tris = new System.Collections.Generic.List<int>();
            for (int r = 0; r < rows - 1; r++)
                foreach (int s in new[] { 0, 1, 3, 4 })     // bone, PDL, PDL, bone (segment 2 is the tooth itself)
                    Quad(tris, r * 6 + s, r * 6 + s + 1, (r + 1) * 6 + s, (r + 1) * 6 + s + 1);
            _bone.SetVertices(v); _bone.SetNormals(n); _bone.SetColors(c); _bone.SetTriangles(tris, 0);
            _bone.RecalculateBounds();
        }

        void BuildCutTopology()
        {
            Child("CutFace", ref _cut);
            _cutV = new Vector3[_rows * CutVertsPerRow]; _cutC = new Color[_cutV.Length];
            var n = new Vector3[_cutV.Length];
            for (int i = 0; i < n.Length; i++) n[i] = Vector3.forward;
            var tris = new System.Collections.Generic.List<int>();
            for (int r = 0; r < _rows - 1; r++)
                for (int s = 0; s < CutVertsPerRow; s += 2)  // (outerL,enamelL) (dentinL,cavityL) (cavityR,dentinR) (enamelR,outerR)
                    Quad(tris, r * CutVertsPerRow + s, r * CutVertsPerRow + s + 1, (r + 1) * CutVertsPerRow + s, (r + 1) * CutVertsPerRow + s + 1);
            _cut.vertices = _cutV; _cut.SetNormals(n); _cut.SetTriangles(tris, 0);
        }

        void BuildCanalTopology()
        {
            Child("CanalWall", ref _canal);
            _canalV = new Vector3[_rows * (RingSegs + 1)]; _canalC = new Color[_canalV.Length];
            var n = new Vector3[_canalV.Length];
            for (int r = 0; r < _rows; r++)
                for (int k = 0; k <= RingSegs; k++)
                {
                    float th = Mathf.PI * k / RingSegs;
                    n[r * (RingSegs + 1) + k] = new Vector3(-Mathf.Cos(th), 0, Mathf.Sin(th));   // inward, toward the lumen axis
                }
            _canal.vertices = _canalV; _canal.SetNormals(n); _canal.SetTriangles(GridIndices(_rows, RingSegs + 1), 0);
        }

        static void Quad(System.Collections.Generic.List<int> t, int a, int b, int c, int d)
        { t.Add(a); t.Add(b); t.Add(c); t.Add(b); t.Add(d); t.Add(c); }

        static int[] GridIndices(int rows, int cols)
        {
            var t = new System.Collections.Generic.List<int>((rows - 1) * (cols - 1) * 6);
            for (int r = 0; r < rows - 1; r++)
                for (int k = 0; k < cols - 1; k++)
                    Quad(t, r * cols + k, r * cols + k + 1, (r + 1) * cols + k, (r + 1) * cols + k + 1);
            return t.ToArray();
        }

        // ------------------------------------------------------------ dynamic update
        /// <summary>Radius of the cavity (mm) at sample i: the shaped canal, or the bur hole drilled from the crown.</summary>
        float Cavity(EndoTwin tw, int i)
        {
            float r = tw.ShapedRadius[i];
            if (_t.Depths[i] <= tw.AccessDepth) r = Mathf.Max(r, EndoTwin.BurRadiusMm);
            return Mathf.Min(r, Mathf.Max(0f, _hw[i] - 0.08f));
        }

        public void Refresh(EndoTwin tw)
        {
            float fillFront = _t.TargetWl - tw.FillLevel * (_t.TargetWl - _t.ChamberFloor);
            for (int i = 0; i < _rows; i++)
            {
                float d = _t.Depths[i], cx = _t.CenterlineX(d), hw = _hw[i], r = Cavity(tw, i), et = _et[i];
                float oL = cx - hw, oR = cx + hw;
                float cL = cx - r, cR = cx + r;
                float eL = Mathf.Min(oL + et, cL), eR = Mathf.Max(oR - et, cR);

                // --- cut face: 8 verts per row, duplicated at colour boundaries so bands stay crisp
                int b = i * CutVertsPerRow;
                _cutV[b + 0] = Units.ToothToLocal(oL, d); _cutV[b + 1] = Units.ToothToLocal(eL, d);
                _cutV[b + 2] = Units.ToothToLocal(eL, d); _cutV[b + 3] = Units.ToothToLocal(cL, d);
                _cutV[b + 4] = Units.ToothToLocal(cR, d); _cutV[b + 5] = Units.ToothToLocal(eR, d);
                _cutV[b + 6] = Units.ToothToLocal(eR, d); _cutV[b + 7] = Units.ToothToLocal(oR, d);
                bool crown = d < _t.CrownLength;
                Color outerC = crown ? Enamel : Cementum;
                _cutC[b + 0] = outerC; _cutC[b + 1] = outerC; _cutC[b + 6] = outerC; _cutC[b + 7] = outerC;
                _cutC[b + 2] = DentinOuter; _cutC[b + 3] = DentinInner; _cutC[b + 4] = DentinInner; _cutC[b + 5] = DentinOuter;

                // --- canal wall: half tube, one colour per row
                Color wall = CanalColor(tw, i, d, r, fillFront);
                int cb = i * (RingSegs + 1);
                for (int k = 0; k <= RingSegs; k++)
                {
                    float th = Mathf.PI * k / RingSegs;
                    _canalV[cb + k] = Units.ToothToLocal(cx + r * Mathf.Cos(th), d, -r * Mathf.Sin(th));
                    _canalC[cb + k] = wall;
                }
            }
            _cut.vertices = _cutV; _cut.colors = _cutC; _cut.RecalculateBounds();
            _canal.vertices = _canalV; _canal.colors = _canalC; _canal.RecalculateBounds();
        }

        Color CanalColor(EndoTwin tw, int i, float d, float r, float fillFront)
        {
            if (r <= 0.001f) return CleanWall;
            if (tw.Stage == Stage.Failed && Mathf.Abs(d - tw.FailDepth) < 0.6f) return FailCol;

            bool aboveRoofBur = tw.P.OrigRadius[i] <= 0.001f;                // the drilled shaft through enamel/dentin
            Color wall = aboveRoofBur ? DentinInner : Color.Lerp(Pulp, CleanWall, tw.Cleaned[i]);
            bool shaped = tw.ShapedRadius[i] > tw.P.OrigRadius[i] + 0.02f && !aboveRoofBur;
            if (shaped) wall = Color.Lerp(wall, ShapedWall, 0.5f);

            float deb = shaped ? (tw.Debris / 100f) * (1f - 0.75f * tw.Cleaned[i]) : 0f;
            if (deb > 0f) wall = Color.Lerp(wall, DebrisCol, Mathf.Clamp01(deb * 0.9f));
            if (tw.Irrigating && d > tw.P.ChamberRoof && d <= tw.P.Apex) wall = Color.Lerp(wall, Irrigant, 0.5f);
            if (tw.FillLevel > 0f && d >= fillFront && d <= tw.P.TargetWl + 0.1f) wall = Gutta;
            return wall;
        }
    }
}
