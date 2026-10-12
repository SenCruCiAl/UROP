// EndoSession.cs
// The playable root-canal trainer. Owns the twin, the simulated AR tracker, the prompt engine, the 3D tooth,
// the instruments, the AR overlays, the camera and the HUD. Input uses the NEW Input System only.
//
// Controls (also shown in-game, F1):
//   Up/Down arrows or mouse wheel   move the instrument   (hold SHIFT = fine control)
//   W record working length   ENTER next stage   N next file   I irrigate (hold)   O condense gutta-percha (hold)
//   R toggle AR tracking error   M toggle patient motion   TAB novice/expert prompts
//   C camera preset   Right-mouse drag orbit   F5 new patient   F6 next tooth type   F9 save session   F1 help
//
// RESEARCH PROTOTYPE - NOT FOR CLINICAL USE. All clinical numbers are game tuning (see CLINICIAN-REVIEW tags).
using UnityEngine;
using UnityEngine.InputSystem;

namespace SurgiFlow.Endo
{
    public sealed class EndoSession : MonoBehaviour
    {
        [SerializeField] Material toothMaterial;
        [SerializeField] int fixedSeed = -1;            // >= 0 => reproducible patient
        [SerializeField] bool startExpert;

        public EndoTwin Twin { get; private set; }
        public PatientTooth Patient { get; private set; }
        public Tracker Tracker { get; } = new Tracker();
        public PromptEngine Prompts { get; } = new PromptEngine();

        Transform _toothRoot, _overlayRoot;
        ToothModel _model;
        Camera _cam;
        int _toothType;
        float _refreshTimer;
        bool _saved, _showHelp = true;
        string _savedPath;

        // camera rig
        float _yaw, _pitch = 6f, _dist = 6.4f, _tDist = 6.4f;
        Vector3 _pivot = new Vector3(0, -1.3f, 0), _tPivot = new Vector3(0, -1.3f, 0);
        int _camPreset;

        // instruments + overlays
        LineRenderer _bur, _file, _gutta, _anchorLine, _wlLine, _floorLine;
        Transform _anchorMarker;
        Material _lineMat;

        // ------------------------------------------------------------------ lifecycle
        void Awake()
        {
            _cam = Camera.main;
            if (_cam == null) { var go = new GameObject("Main Camera") { tag = "MainCamera" }; _cam = go.AddComponent<Camera>(); go.AddComponent<AudioListener>(); }
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.07f, 0.09f, 0.12f);
            _cam.fieldOfView = 32f;

            _toothRoot = new GameObject("ToothRoot").transform; _toothRoot.SetParent(transform, false);
            _overlayRoot = new GameObject("AROverlayRoot").transform; _overlayRoot.SetParent(transform, false);

            var shader = Shader.Find("SurgiFlow/VertexColorUnlit"); if (shader == null) shader = Shader.Find("Sprites/Default");
            _lineMat = new Material(shader);

            if (toothMaterial == null) toothMaterial = new Material(Shader.Find("SurgiFlow/VertexColorLit"));
            _model = _toothRoot.gameObject.AddComponent<ToothModel>();
            _model.Material = toothMaterial;

            _bur = MakeLine("Bur", _toothRoot); _file = MakeLine("File", _toothRoot); _gutta = MakeLine("GuttaPercha", _toothRoot);
            _anchorLine = MakeLine("AnchorLine", _overlayRoot); _wlLine = MakeLine("WlLine", _overlayRoot); _floorLine = MakeLine("FloorLine", _overlayRoot);
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            s.name = "AnchorMarker"; Destroy(s.GetComponent<Collider>());
            s.transform.SetParent(_overlayRoot, false); s.transform.localScale = Vector3.one * 0.12f;
            s.GetComponent<Renderer>().sharedMaterial = new Material(_lineMat);
            _anchorMarker = s.transform;

            Prompts.Expert = startExpert;
            StartCase(fixedSeed >= 0 ? fixedSeed : (int?)null);
        }

        LineRenderer MakeLine(string name, Transform parent)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = new Material(_lineMat); lr.useWorldSpace = false; lr.alignment = LineAlignment.View;
            lr.numCapVertices = 6; lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.enabled = false;
            return lr;
        }

        static void SetColor(Renderer r, Color c)
        {
            var m = r.sharedMaterial;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        public void StartCase(int? seed = null)
        {
            var spec = ToothSpec.Library[_toothType % ToothSpec.Library.Length];
            Patient = new PatientTooth(spec, seed);
            Twin = new EndoTwin(Patient);
            _model.Build(Patient);
            _model.Refresh(Twin);
            _saved = false; _savedPath = null;
            Tracker.Degraded = false; Tracker.PatientMotion = false;
            SetCameraPreset(0);
        }

        // ------------------------------------------------------------------ main loop
        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);        // clamp: alt-tab / breakpoints must not jump the sim
            bool moving = false, irrigating = false, filling = false;
            ReadInput(dt, ref moving, ref irrigating, ref filling);

            Tracker.Update(dt);
            Twin.Tick(dt, irrigating, filling, moving);
            Prompts.Update(dt, Twin, Tracker.RegistrationErrorMm);

            if (Twin.IsOver && !_saved)
            {
                _saved = true;
                try { _savedPath = Twin.Save(); } catch (System.Exception e) { Debug.LogWarning("Session save failed: " + e.Message); }
            }

            _refreshTimer -= dt;
            if (_refreshTimer <= 0f) { _refreshTimer = 1f / 30f; _model.Refresh(Twin); }

            _toothRoot.localPosition = new Vector3(Tracker.TruePoseMm.x, Tracker.TruePoseMm.y, 0) * Units.MmToUnity;
            _overlayRoot.localPosition = new Vector3(Tracker.EstPoseMm.x, Tracker.EstPoseMm.y, 0) * Units.MmToUnity;
            UpdateInstruments();
            UpdateOverlays();
        }

        void LateUpdate() => UpdateCamera();

        // ------------------------------------------------------------------ input (new Input System only)
        void ReadInput(float dt, ref bool moving, ref bool irrigating, ref bool filling)
        {
            var kb = Keyboard.current; var ms = Mouse.current;
            if (kb == null) return;

            if (kb.f5Key.wasPressedThisFrame) { StartCase(fixedSeed >= 0 ? fixedSeed : (int?)null); return; }
            if (kb.f6Key.wasPressedThisFrame) { _toothType++; StartCase(); return; }
            if (kb.f1Key.wasPressedThisFrame) _showHelp = !_showHelp;
            if (kb.f9Key.wasPressedThisFrame) { try { _savedPath = Twin.Save(); } catch (System.Exception e) { Debug.LogWarning(e.Message); } }
            if (kb.cKey.wasPressedThisFrame) SetCameraPreset((_camPreset + 1) % 4);
            if (kb.tabKey.wasPressedThisFrame) Prompts.Expert = !Prompts.Expert;
            if (kb.rKey.wasPressedThisFrame) Tracker.Degraded = !Tracker.Degraded;
            if (kb.mKey.wasPressedThisFrame) Tracker.PatientMotion = !Tracker.PatientMotion;

            if (ms != null && ms.rightButton.isPressed)
            {
                Vector2 d = ms.delta.ReadValue();
                _yaw = Mathf.Clamp(_yaw + d.x * 0.25f, -70f, 70f);
                _pitch = Mathf.Clamp(_pitch - d.y * 0.25f, -25f, 50f);
            }

            if (Twin.IsOver) return;

            bool fine = kb.shiftKey.isPressed;
            float delta = 0f;
            if (kb.downArrowKey.isPressed) delta += (fine ? 1.0f : 3.5f) * dt;
            if (kb.upArrowKey.isPressed) delta -= (fine ? 1.0f : 3.5f) * dt;
            if (ms != null)
            {
                float wheel = ms.scroll.ReadValue().y;
                if (wheel != 0f) delta -= Mathf.Sign(wheel) * (fine ? 0.15f : 0.5f);
            }
            if (delta != 0f) { moving = true; Twin.Move(delta, fine); }

            if (kb.wKey.wasPressedThisFrame) Twin.RecordWl();
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Twin.NextStage();
            if (kb.nKey.wasPressedThisFrame) Twin.NextFile();
            irrigating = kb.iKey.isPressed;
            filling = kb.oKey.isPressed;
        }

        // ------------------------------------------------------------------ camera
        void SetCameraPreset(int i)
        {
            _camPreset = i;
            _yaw = 0f; _pitch = 6f;
            switch (i)
            {
                case 0: _tPivot = new Vector3(0, -1.3f, 0); _tDist = 6.4f; break;
                case 1: _tPivot = Units.ToothToLocal(0, Patient.CrownLength * 0.7f); _tDist = 3.4f; break;
                case 2: _tPivot = Units.ToothToLocal(0, Patient.Apex - 2f); _tDist = 3.0f; break;
                default: _tDist = 2.8f; break;                         // 3 = follow tip (pivot updated every frame)
            }
        }

        void UpdateCamera()
        {
            if (_camPreset == 3 && Twin != null) _tPivot = Units.ToothToLocal(0, Mathf.Max(Twin.Depth, 2f));
            float k = 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime);
            _pivot = Vector3.Lerp(_pivot, _tPivot, k); _dist = Mathf.Lerp(_dist, _tDist, k);
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 pos = _pivot + rot * new Vector3(0, 0, _dist);
            _cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(_pivot - pos, Vector3.up));
        }

        // ------------------------------------------------------------------ instruments
        static readonly Color[] IsoColors =       // ISO 3630-1 handle colour code; CLINICIAN-REVIEW: confirm
        {
            new Color(0.55f, 0.3f, 0.75f),   // 10 purple
            new Color(0.95f, 0.95f, 0.95f),  // 15 white
            new Color(1.0f, 0.88f, 0.2f),    // 20 yellow
            new Color(0.9f, 0.2f, 0.2f),     // 25 red
            new Color(0.2f, 0.4f, 0.95f),    // 30 blue
            new Color(0.2f, 0.7f, 0.3f),     // 35 green
            new Color(0.12f, 0.12f, 0.12f),  // 40 black
        };

        static Color IsoColor(int iso) => IsoColors[Mathf.Clamp(iso / 5 - 2, 0, IsoColors.Length - 1)];

        void SetWidth(LineRenderer lr, float mm) { lr.startWidth = lr.endWidth = mm * Units.MmToUnity; }

        void UpdateInstruments()
        {
            var t = Twin;
            _bur.enabled = _file.enabled = _gutta.enabled = false;
            if (t.IsOver && t.Stage == Stage.Complete) return;
            const float handleTop = -9f;

            if (t.Instrument == InstrumentKind.Bur)
            {
                _bur.enabled = true; _bur.positionCount = 2;
                _bur.SetPosition(0, Units.ToothToLocal(0, handleTop, 0.2f)); _bur.SetPosition(1, Units.ToothToLocal(0, t.Depth, 0.2f));
                SetWidth(_bur, EndoTwin.BurRadiusMm * 2f); _bur.startColor = _bur.endColor = new Color(0.72f, 0.74f, 0.8f);
            }
            else
            {
                var lr = t.Instrument == InstrumentKind.KFile ? _file : _gutta;
                float tip = t.Instrument == InstrumentKind.KFile ? t.Depth
                          : t.P.TargetWl - t.FillLevel * (t.P.TargetWl - t.P.ChamberFloor) - 0.5f;
                int n = Mathf.Max(0, Mathf.FloorToInt(tip)) + 3;
                lr.enabled = true; lr.positionCount = n + 1;
                lr.SetPosition(0, Units.ToothToLocal(0, handleTop, 0.2f));
                for (int i = 1; i < n; i++) { float d = Mathf.Min(i - 1, tip); lr.SetPosition(i, Units.ToothToLocal(t.P.CenterlineX(d), d, 0.2f)); }
                lr.SetPosition(n, Units.ToothToLocal(t.P.CenterlineX(tip), tip, 0.2f));
                if (t.Instrument == InstrumentKind.KFile)
                { SetWidth(lr, Mathf.Max(0.35f, t.FileSize / 100f * 1.6f)); lr.startColor = lr.endColor = Color.Lerp(IsoColor(t.FileSize), new Color(0.8f, 0.82f, 0.88f), 0.35f); }
                else { SetWidth(lr, 0.55f); lr.startColor = lr.endColor = new Color(0.98f, 0.62f, 0.7f); }
            }
        }

        // ------------------------------------------------------------------ AR overlays (registered through the ESTIMATED pose)
        static Color LevelColor(int level) => level >= 2 ? new Color(1f, 0.25f, 0.2f) : level == 1 ? new Color(1f, 0.75f, 0.15f) : new Color(0.3f, 0.9f, 1f);

        void UpdateOverlays()
        {
            float conf = Tracker.RegistrationErrorMm > Tracker.ConfidenceGateMm ? 0.3f : 1f;     // dim when mis-registered
            var cur = Prompts.Current;
            bool showAnchor = cur != null && cur.Anchor.HasValue && !Twin.IsOver;
            _anchorMarker.gameObject.SetActive(showAnchor); _anchorLine.enabled = showAnchor;
            if (showAnchor)
            {
                Vector2 a = cur.Anchor.Value; Color c = LevelColor(cur.Level) * conf; c.a = 1f;
                _anchorMarker.localPosition = Units.ToothToLocal(a.x, a.y, 0.3f);
                SetColor(_anchorMarker.GetComponent<Renderer>(), c);
                HorizontalLine(_anchorLine, a.y, 4.5f, c, 0.25f);
            }

            bool floor = Twin.Stage == Stage.Access && !Prompts.Expert;
            _floorLine.enabled = floor;
            if (floor) HorizontalLine(_floorLine, Patient.ChamberFloor, 3.2f, new Color(1f, 0.2f, 0.2f) * conf, 0.2f);

            bool wl = Twin.RecordedWl.HasValue && (Twin.Stage == Stage.WorkingLength || Twin.Stage == Stage.Shaping);
            _wlLine.enabled = wl;
            if (wl) HorizontalLine(_wlLine, Twin.RecordedWl.Value, 3.2f, new Color(0.2f, 1f, 0.45f) * conf, 0.2f);
        }

        void HorizontalLine(LineRenderer lr, float depth, float halfWidthMm, Color c, float widthMm)
        {
            c.a = 1f;
            lr.positionCount = 2;
            lr.SetPosition(0, Units.ToothToLocal(-halfWidthMm, depth, 0.28f)); lr.SetPosition(1, Units.ToothToLocal(halfWidthMm, depth, 0.28f));
            SetWidth(lr, widthMm); lr.startColor = lr.endColor = c;
        }

        // ------------------------------------------------------------------ HUD (IMGUI; no input read here)
        GUIStyle _label, _title, _small, _banner, _box;
        Texture2D _white;

        void InitStyles()
        {
            if (_label != null) return;
            _white = Texture2D.whiteTexture;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 20, richText = true, normal = { textColor = Color.white } };
            _title = new GUIStyle(_label) { fontSize = 26, fontStyle = FontStyle.Bold };
            _small = new GUIStyle(_label) { fontSize = 17 };
            _banner = new GUIStyle(_label) { fontSize = 26, alignment = TextAnchor.MiddleCenter, wordWrap = true, fontStyle = FontStyle.Bold };
            _box = new GUIStyle(GUI.skin.box);
        }

        void Fill(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, _white); GUI.color = o; }

        void Bar(float x, float y, float w, string label, float value01, Color c)
        {
            GUI.Label(new Rect(x, y - 4, 170, 28), label, _small);
            Fill(new Rect(x + 170, y + 2, w, 16), new Color(0, 0, 0, 0.55f));
            Fill(new Rect(x + 170, y + 2, w * Mathf.Clamp01(value01), 16), c);
        }

        void OnGUI()
        {
            if (Twin == null) return;
            InitStyles();
            float s = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            float W = Screen.width / s;
            var t = Twin; var p = Patient;

            // --- left panel
            Fill(new Rect(14, 14, 440, 520), new Color(0.04f, 0.06f, 0.09f, 0.78f));
            GUI.Label(new Rect(26, 20, 420, 34), "SurgiFlow Endo Trainer", _title);
            GUI.Label(new Rect(26, 54, 420, 24), "<color=#ffb347>Research prototype - unreviewed clinical rules - not for patient use</color>", _small);
            GUI.Label(new Rect(26, 82, 420, 28), $"Case: {p.Spec.Label}   seed {p.Seed}", _small);
            GUI.Label(new Rect(26, 108, 420, 28), $"Stage: <b>{StageText.Name(t.Stage)}</b>   Time {t.Time:F0}s", _label);
            string inst = t.Instrument == InstrumentKind.KFile ? $"ISO #{t.FileSize} K-file" : StageText.Name(t.Instrument);
            GUI.Label(new Rect(26, 138, 420, 28), $"Instrument: {inst}", _label);
            GUI.Label(new Rect(26, 168, 420, 28), $"Tip depth: {t.Depth:F2} mm", _label);
            float? eal = t.ApexLocator();
            GUI.Label(new Rect(26, 198, 420, 28), eal.HasValue ? $"Apex locator: {eal.Value:F1} mm to apex" : "Apex locator: --", _label);
            Bar(26, 236, 230, "File stress", t.Stress / 100f, Color.Lerp(new Color(0.3f, 0.8f, 0.4f), new Color(1f, 0.25f, 0.2f), t.Stress / 100f));
            Bar(26, 262, 230, "Debris", t.Debris / 100f, new Color(0.65f, 0.45f, 0.2f));
            Bar(26, 288, 230, "Cleanliness", t.Cleanliness(), new Color(0.3f, 0.75f, 0.95f));
            Bar(26, 314, 230, "Obturation", t.FillLevel, new Color(0.96f, 0.58f, 0.66f));
            GUI.Label(new Rect(26, 346, 420, 28), $"Score: <b>{t.Score()}</b> / 100    Errors: {t.Errors}", _label);
            float re = Tracker.RegistrationErrorMm;
            string reg = re > Tracker.ConfidenceGateMm ? $"<color=#ff5a4a>AR reg. error {re:F2} mm - UNRELIABLE</color>" : $"<color=#6be28a>AR reg. error {re:F2} mm</color>";
            GUI.Label(new Rect(26, 380, 420, 28), reg, _label);
            GUI.Label(new Rect(26, 410, 420, 28), $"Tracking: {(Tracker.Degraded ? "degraded (R)" : "good")}   Motion: {(Tracker.PatientMotion ? "on (M)" : "off")}", _small);
            GUI.Label(new Rect(26, 436, 420, 28), $"Prompts: {(Prompts.NoviceWording(t) ? "novice" : "expert")}{(Prompts.Expert && Prompts.NoviceWording(t) ? " (switched back after errors)" : "")}  [TAB]", _small);
            GUI.Label(new Rect(26, 462, 420, 28), $"Camera: preset {_camPreset + 1}/4 [C]   orbit: right-mouse drag", _small);
            GUI.Label(new Rect(26, 488, 420, 28), "[F1] controls   [F5] new patient   [F6] next tooth", _small);

            // --- event log (right)
            float lx = W - 560;
            Fill(new Rect(lx, 14, 546, 300), new Color(0.04f, 0.06f, 0.09f, 0.7f));
            GUI.Label(new Rect(lx + 12, 18, 520, 28), "Event log", _label);
            int shown = 0;
            for (int i = t.Events.Count - 1; i >= 0 && shown < 9; i--, shown++)
            {
                var e = t.Events[i];
                string col = e.level >= 3 ? "#ff5a4a" : e.level == 2 ? "#ff9a4a" : e.level == 1 ? "#ffe066" : "#d6e2f0";
                GUI.Label(new Rect(lx + 12, 50 + shown * 28, 530, 28), $"<color={col}>[{e.t:F0}s] {e.text}</color>", _small);
            }

            // --- prompt banner (bottom centre)
            if (Prompts.Current != null)
            {
                var pr = Prompts.Current;
                Color bg = pr.Level >= 2 ? new Color(0.55f, 0.08f, 0.06f, 0.88f) : pr.Level == 1 ? new Color(0.5f, 0.38f, 0.04f, 0.88f) : new Color(0.04f, 0.2f, 0.3f, 0.88f);
                var r = new Rect(W / 2 - 520, 1080 - 130, 1040, 96);
                Fill(r, bg);
                GUI.Label(new Rect(r.x + 14, r.y, r.width - 28, r.height), Prompts.Text(pr, t), _banner);
            }

            if (_showHelp) DrawHelp(W);
            if (t.IsOver) DrawDebrief(W);
            GUI.matrix = Matrix4x4.identity;
        }

        void DrawHelp(float W)
        {
            var r = new Rect(14, 546, 440, 330);
            Fill(r, new Color(0.04f, 0.06f, 0.09f, 0.78f));
            GUI.Label(new Rect(26, 552, 420, 320),
                "<b>Controls</b>\n" +
                "Up/Down or wheel  move instrument\n" +
                "SHIFT  fine control (use near the apex / curves)\n" +
                "W  record working length\n" +
                "ENTER  next stage     N  next file\n" +
                "I  irrigate (hold)    O  condense gutta-percha (hold)\n" +
                "R  toggle AR tracking error\n" +
                "M  toggle patient motion\n" +
                "TAB  novice / expert prompts\n" +
                "C  camera preset    F9  save session\n\n" +
                "Goal: open the chamber, find the working length, shape with\nsequential files + irrigation, then fill. Avoid perforation,\nfile separation and over-instrumentation.", _small);
        }

        void DrawDebrief(float W)
        {
            var t = Twin;
            var r = new Rect(W / 2 - 360, 150, 720, 560);
            Fill(r, new Color(0.03f, 0.05f, 0.08f, 0.94f));
            bool ok = t.Stage == Stage.Complete;
            GUI.Label(new Rect(r.x + 20, r.y + 12, 680, 40), ok ? "<color=#6be28a>Procedure complete</color>" : "<color=#ff5a4a>Procedure failed</color>", _title);
            string body = ok ? $"Score <b>{t.Score()}</b>/100\n" : $"{t.FailReason}\n";
            body += $"Working length: recorded {(t.RecordedWl.HasValue ? t.RecordedWl.Value.ToString("F2") : "--")} mm, target {Patient.TargetWl:F2} mm";
            if (t.RecordedWl.HasValue) body += $" (error {t.WlError:+0.00;-0.00} mm)";
            body += $"\nCleanliness {t.Cleanliness() * 100f:F0}%   Peak file stress {t.PeakStress:F0}%   Files: {string.Join(", ", t.FilesDone)}\n";
            body += $"Time {t.Time:F0}s   Errors {t.Errors}\n\n<b>Penalties</b>\n";
            if (t.Penalties.Count == 0) body += "none\n";
            foreach (var pen in t.Penalties) body += $"-{pen.points}  {pen.reason}\n";
            body += "\n<b>Learning points</b>\n" + Feedback();
            GUI.Label(new Rect(r.x + 20, r.y + 60, 680, 460), body, _small);
            GUI.Label(new Rect(r.x + 20, r.y + r.height - 34, 680, 28), $"F5 new patient   {(_savedPath != null ? "saved: " + System.IO.Path.GetFileName(_savedPath) : "")}", _small);
        }

        string Feedback()
        {
            var t = Twin; var sb = new System.Text.StringBuilder();
            if (t.Stage == Stage.Failed && t.FailReason != null && t.FailReason.Contains("floor"))
                sb.AppendLine("- Stop drilling once you feel/see the chamber; deeper cuts risk perforating the floor.");
            if (t.Stage == Stage.Failed && t.FailReason != null && t.FailReason.Contains("separated"))
                sb.AppendLine("- Short strokes, fine control, irrigate often: stress rises faster in curves and with debris.");
            if (t.RecordedWl.HasValue && Mathf.Abs(t.WlError) > 0.5f)
                sb.AppendLine("- Re-check the apex locator reading: aim for the apical constriction, not the apex itself.");
            foreach (var pen in t.Penalties)
            {
                if (pen.reason.StartsWith("No irrigation")) { sb.AppendLine("- Irrigate between every file to remove debris."); break; }
            }
            if (t.Cleanliness() < 0.85f && t.Stage != Stage.Failed) sb.AppendLine("- Cleanliness was low: work each file to full length and irrigate.");
            if (sb.Length == 0) sb.AppendLine("- Clean run. Try expert prompts (TAB), tracking error (R) or patient motion (M).");
            sb.AppendLine("\n<color=#ffb347>Simulation values are game tuning, not clinical guidance.</color>");
            return sb.ToString();
        }
    }
}
