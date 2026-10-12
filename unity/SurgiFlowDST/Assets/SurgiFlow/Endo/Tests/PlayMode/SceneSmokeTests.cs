// Loads the real scene in play mode, drives a full procedure through the public API and renders screenshots of the
// 3D view (HUD excluded: IMGUI is not part of a camera render). Set SF_SHOTS to a folder to keep the PNGs.
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SurgiFlow.Endo.Tests
{
    public class SceneSmokeTests
    {
        static void Shot(string name)
        {
            string dir = System.Environment.GetEnvironmentVariable("SF_SHOTS");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            var cam = Camera.main;
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            cam.targetTexture = null; RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        }

        static IEnumerator DriveTo(EndoTwin t, float depth, float step = 0.4f, bool irrigate = false)
        {
            int guard = 0;
            while (Mathf.Abs(depth - t.Depth) > 1e-3f && !t.IsOver && guard++ < 2000)
            {
                t.Move(Mathf.Clamp(depth - t.Depth, -step, step), true);
                if (guard % 5 == 0) yield return null;
            }
        }

        [UnityTest]
        public IEnumerator FullProcedureRendersWithoutErrors()
        {
            yield return SceneManager.LoadSceneAsync("EndoSim", LoadSceneMode.Single);
            yield return null;
            var s = Object.FindFirstObjectByType<EndoSession>();
            Assert.NotNull(s, "EndoSession missing from scene");
            s.StartCase(11);
            var t = s.Twin;
            yield return null; yield return null;
            Shot("0_start");

            yield return DriveTo(t, t.P.ChamberRoof - 1.5f); yield return null; Shot("1_access_drilling");
            yield return DriveTo(t, t.P.ChamberRoof + 0.3f); yield return null; Shot("2_chamber_open");
            t.NextStage();
            yield return DriveTo(t, t.P.TargetWl); t.RecordWl(); yield return null; Shot("3_working_length");
            t.NextStage();
            yield return DriveTo(t, t.Wl); yield return null; Shot("4_shaping_file15");
            Object.FindFirstObjectByType<EndoSession>().Prompts.Expert = true;
            Assert.IsFalse(t.IsOver, t.FailReason);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
