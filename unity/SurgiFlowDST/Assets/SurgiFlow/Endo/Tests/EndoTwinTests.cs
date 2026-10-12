// EditMode tests for the pure-C# twin, tracker and prompt engine. Run: Window > General > Test Runner,
// or headless: Unity.exe -batchmode -projectPath <path> -runTests -testPlatform EditMode -testResults results.xml
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace SurgiFlow.Endo.Tests
{
    public class EndoTwinTests
    {
        static EndoTwin NewTwin(int seed = 42) => new EndoTwin(new PatientTooth(ToothSpec.Library[0], seed));

        static void DriveTo(EndoTwin t, float depth, float step = 0.25f, bool irrigate = false)
        {
            int guard = 0;
            while (Mathf.Abs(depth - t.Depth) > 1e-3f && !t.IsOver && guard++ < 5000)
            {
                float d = Mathf.Clamp(depth - t.Depth, -step, step);
                t.Move(d, true);
                t.Tick(0.05f, irrigate, false, true);
                if (guard % 8 == 0) t.Tick(1f, irrigate, false, false);     // rest: stress decays, irrigation clears debris
            }
        }

        [Test] public void SameSeedGivesSamePatient()
        {
            var a = new PatientTooth(ToothSpec.Library[0], 7); var b = new PatientTooth(ToothSpec.Library[0], 7);
            Assert.AreEqual(a.CanalLength, b.CanalLength); Assert.AreEqual(a.CurvatureDeg, b.CurvatureDeg);
            Assert.AreNotEqual(a.CanalLength, new PatientTooth(ToothSpec.Library[0], 8).CanalLength);
        }

        [Test] public void PatientLandmarksAreOrdered()
        {
            for (int seed = 0; seed < 50; seed++)
            {
                var p = new PatientTooth(ToothSpec.Library[seed % ToothSpec.Library.Length], seed);
                Assert.Less(p.ChamberRoof, p.ChamberFloor); Assert.Less(p.ChamberFloor, p.TargetWl); Assert.Less(p.TargetWl, p.Apex);
            }
        }

        [Test] public void BurPastChamberFloorPerforates()
        {
            var t = NewTwin();
            DriveTo(t, t.P.ChamberFloor + 1f);
            Assert.AreEqual(Stage.Failed, t.Stage);
            StringAssert.Contains("perforated", t.FailReason);
        }

        [Test] public void CannotLeaveAccessBeforeChamberReached()
        {
            var t = NewTwin();
            DriveTo(t, t.P.ChamberRoof - 1f); t.NextStage();
            Assert.AreEqual(Stage.Access, t.Stage);
        }

        [Test] public void ApexLocatorReadsZeroAtApex()
        {
            var t = NewTwin(); DriveTo(t, t.P.ChamberRoof + 0.3f); t.NextStage();
            DriveTo(t, t.P.Apex);
            Assert.AreEqual(0f, t.ApexLocator().Value, 0.3f);      // noise sigma 0.05 mm
        }

        [Test] public void PerfectRunCompletesWithHighScore()
        {
            var t = NewTwin();
            DriveTo(t, t.P.ChamberRoof + 0.3f); t.NextStage();
            Assert.AreEqual(Stage.WorkingLength, t.Stage);
            DriveTo(t, t.P.TargetWl); t.RecordWl(); t.NextStage();
            Assert.AreEqual(Stage.Shaping, t.Stage);
            int guard = 0;
            while (t.Stage == Stage.Shaping && guard++ < 20)
            {
                DriveTo(t, t.Wl, 0.25f, true);
                t.Tick(2f, true, false, false);                    // final irrigation
                t.NextFile();
                if (t.Stage == Stage.Shaping) DriveTo(t, 0f, 2f);
            }
            Assert.AreEqual(Stage.Obturation, t.Stage, t.FailReason);
            for (int i = 0; i < 20; i++) t.Tick(0.5f, false, true, false);
            t.NextStage();
            Assert.AreEqual(Stage.Complete, t.Stage);
            Assert.GreaterOrEqual(t.Score(), 90);
        }

        [Test] public void FileFailsWhenDrivenHardWithoutRest()
        {
            var t = NewTwin(); DriveTo(t, t.P.ChamberRoof + 0.3f); t.NextStage();
            DriveTo(t, t.P.TargetWl); t.RecordWl(); t.NextStage();
            for (int i = 0; i < 400 && !t.IsOver; i++) { t.Move(0.5f, false); t.Move(-0.4f, false); t.Tick(0.016f, false, false, true); }
            Assert.AreEqual(Stage.Failed, t.Stage);
            StringAssert.Contains("separated", t.FailReason);
        }

        [Test] public void NotIrrigatingBetweenFilesIsPenalised()
        {
            var t = NewTwin(); DriveTo(t, t.P.ChamberRoof + 0.3f); t.NextStage();
            DriveTo(t, t.P.TargetWl); t.RecordWl(); t.NextStage();
            DriveTo(t, t.Wl, 0.25f, true); t.Tick(2f, true, false, false); t.NextFile();   // irrigated: no penalty
            DriveTo(t, t.Wl, 0.25f, false); t.NextFile();                                    // not irrigated since previous file
            Assert.IsTrue(t.Penalties.Exists(p => p.reason.StartsWith("No irrigation")));
        }

        [Test] public void DegradedTrackerExceedsConfidenceGate()
        {
            var tr = new Tracker { Degraded = true, PatientMotion = true };
            for (int i = 0; i < 120; i++) tr.Update(0.02f);
            Assert.Greater(tr.RegistrationErrorMm, Tracker.ConfidenceGateMm);
            tr.Degraded = false; tr.Update(0.02f);
            Assert.AreEqual(0f, tr.RegistrationErrorMm, 1e-4f);
        }

        [Test] public void PromptEngineWarnsOnRegistrationError()
        {
            var t = NewTwin(); var pe = new PromptEngine();
            pe.Update(0.016f, t, 1.2f);
            StringAssert.Contains("registration", pe.Current.Novice);
            Assert.AreEqual(2, pe.Current.Level);
        }

        [Test] public void ExpertSwitchesBackToNoviceAfterThreeErrors()
        {
            var t = NewTwin(); var pe = new PromptEngine { Expert = true };
            Assert.IsFalse(pe.NoviceWording(t));
            for (int i = 0; i < 3; i++) t.Log(2, "x");
            Assert.IsTrue(pe.NoviceWording(t));
        }

        [Test] public void SaveWritesCsvAndJson()
        {
            var t = NewTwin(); t.Tick(0.5f, false, false, false);
            string dir = Path.Combine(Path.GetTempPath(), "surgiflow_test_" + System.Guid.NewGuid().ToString("N"));
            string json = t.Save(dir);
            Assert.IsTrue(File.Exists(json)); Assert.AreEqual(2, Directory.GetFiles(dir).Length);
            StringAssert.Contains("Maxillary central incisor", File.ReadAllText(json));
            Directory.Delete(dir, true);
        }
    }
}
