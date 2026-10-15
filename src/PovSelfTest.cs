using System.Collections;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace SizeboxPerspectiveX
{
    // Developer-only ([Dev] SelfTest): loads the first stage, possesses a character, switches to
    // first person, looks down at the body and saves screenshots with PerspectiveX off and on.
    internal class PovSelfTest : MonoBehaviour
    {
        private IEnumerator Start()
        {
            string outDir = Path.Combine(Paths.BepInExRootPath, "selftest");
            Directory.CreateDirectory(outDir);
            bool gts = PerspectiveXPlugin.Test.Value == PerspectiveXPlugin.DevTest.Giantess;

            SceneLoader loader = null;
            while ((loader = FindObjectOfType<SceneLoader>()) == null)
                yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(3f);
            var stage = loader.GetSceneList().First(s => s.Scene == "Island_SBE");
            AsyncOperation op = loader.LoadScene(stage);
            while (op != null && !op.isDone)
                yield return null;
            while (GameController.Instance == null || GameController.LocalClient == null || ObjectManager.Instance == null)
                yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(4f);

            AssetManager am = AssetManager.Instance;
            string name = gts
                ? am.GetGtsAssetNames().First()
                : am.GetFemaleMicroAssetNames().First(n => n.ToLowerInvariant().Contains("unitychan"));
            Transform cam = Camera.main.transform;
            Vector3 pos = cam.position + cam.forward.Flatten().normalized * (gts ? 60f : 4f);
            if (Physics.Raycast(pos + Vector3.up * 200f, Vector3.down, out RaycastHit hit, 1000f, Layers.walkableMask))
                pos = hit.point;
            EntityBase e = null;
            if (gts)
                GameController.LocalClient.SpawnGiantess(am.GetAssetDescriptionByName(name), pos, Quaternion.LookRotation(cam.forward.Flatten()), 0.01f, x => e = x);
            else
                GameController.LocalClient.SpawnMicro(am.GetAssetDescriptionByName(name), pos, Quaternion.LookRotation(cam.forward.Flatten()), 1f, x => e = x);
            while (e == null)
                yield return null;
            yield return new WaitForSeconds(1f);
            if (e is Humanoid hm && hm.ai != null)
                hm.ai.DisableAI();
            bool played = GameController.LocalClient.Player.PlayAs(e as IPlayable);
            yield return new WaitForSeconds(2f);

            // Sizebox only runs its camera controllers in play mode.
            GameController.playerInputEnabled = true;
            BaseCameraController ctrl = GameController.LocalClient.Player.Camera.ActiveCameraController;
            PerspectiveXPlugin.Log.LogInfo($"[SelfTest] playing as {name}: {played}, controller {ctrl?.GetType().Name}");
            var press = AccessTools.Method(ctrl.GetType(), "OnCameraPress");
            var ctxType = press.GetParameters()[0].ParameterType;
            press.Invoke(ctrl, new[] { System.Activator.CreateInstance(ctxType) });
            ctrl.orbit.xRotation = -55f; // look down at the body (negative pitch looks down in first person)
            yield return new WaitForSeconds(2f);
            PerspectiveXPlugin.Log.LogInfo($"[SelfTest] first person: {ctrl.FirstPersonMode}");

            foreach (Canvas c in FindObjectsOfType<Canvas>())
                c.enabled = false;

            string tag = gts ? "gts" : "micro";
            PerspectiveXPlugin.Enabled.Value = false;
            yield return new WaitForSeconds(1.5f);
            yield return Shot(outDir, $"pov_{tag}_vanilla_down");
            PerspectiveXPlugin.Enabled.Value = true;
            yield return new WaitForSeconds(1.5f);
            yield return Shot(outDir, $"pov_{tag}_px_down");
            ctrl.orbit.xRotation = 0f;
            yield return new WaitForSeconds(1.5f);
            yield return Shot(outDir, $"pov_{tag}_px_ahead");
            Humanoid h = (Humanoid)e;
            Transform l = h.Animator.GetBoneTransform(HumanBodyBones.LeftEye), r = h.Animator.GetBoneTransform(HumanBodyBones.RightEye);
            PerspectiveXPlugin.Log.LogInfo($"[SelfTest] camera y {Camera.main.transform.position.y - e.transform.position.y:0.000}, " +
                                           $"head bone y {h.Animator.GetBoneTransform(HumanBodyBones.Head).position.y - e.transform.position.y:0.000}, " +
                                           $"eyes {(l != null && r != null ? "mapped" : "not mapped")}, height {h.MeshHeight:0.000}");
            // Walk straight ahead for 4 s in first person and watch the view direction.
            var mover = FindObjectOfType<NewPlayerMicroController>();
            if (mover != null && !gts)
            {
                foreach (bool enabledPx in new[] { false, true })
                {
                    PerspectiveXPlugin.Enabled.Value = enabledPx;
                    yield return new WaitForSeconds(1f);
                    float startYaw = Camera.main.transform.eulerAngles.y, prevYaw = startYaw, maxDev = 0f, maxStep = 0f, maxPitchStep = 0f, prevPitch = Camera.main.transform.eulerAngles.x;
                    Vector3 p0 = e.transform.position;
                    float t = 0f;
                    int frames = 0;
                    while (t < 4f && frames < 600)
                    {
                        Traverse.Create(mover).Field("verticalInput").SetValue(1f);
                        yield return null;
                        t += Time.deltaTime;
                        frames++;
                        if (enabledPx && frames % 10 == 0)
                            PerspectiveXPlugin.Log.LogInfo($"[SelfTest]   f{frames} dt {Time.deltaTime:0.000} cam {Camera.main.transform.position - e.transform.position} rot {Camera.main.transform.eulerAngles} body yaw {e.transform.eulerAngles.y:0.0} fp {ctrl.FirstPersonMode}");
                        float yaw = Camera.main.transform.eulerAngles.y, pitch = Camera.main.transform.eulerAngles.x;
                        maxDev = Mathf.Max(maxDev, Mathf.Abs(Mathf.DeltaAngle(startYaw, yaw)));
                        maxStep = Mathf.Max(maxStep, Mathf.Abs(Mathf.DeltaAngle(prevYaw, yaw)));
                        maxPitchStep = Mathf.Max(maxPitchStep, Mathf.Abs(Mathf.DeltaAngle(prevPitch, pitch)));
                        prevYaw = yaw;
                        prevPitch = pitch;
                    }
                    Traverse.Create(mover).Field("verticalInput").SetValue(0f);
                    Vector3 d = e.transform.position - p0;
                    PerspectiveXPlugin.Log.LogInfo($"[SelfTest] walk 4 s, PerspectiveX {(enabledPx ? "ON " : "OFF")}: view yaw drift {maxDev:0.0} deg, worst frame yaw jump {maxStep:0.0} deg, pitch jump {maxPitchStep:0.0} deg, moved {new Vector3(d.x, 0, d.z).magnitude:0.0} u");
                    yield return Shot(outDir, $"pov_{tag}_walk_{(enabledPx ? "px" : "vanilla")}");
                    yield return new WaitForSeconds(1f);
                }
            }
            PerspectiveXPlugin.Log.LogInfo("[SelfTest] screenshot done");
        }

        private static IEnumerator Shot(string dir, string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
            yield return new WaitForSeconds(0.5f);
        }
    }
}
