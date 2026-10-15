using System.Collections;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace SizeboxPerspectiveX
{
    // Developer-only ([Dev] SelfTest = Motion): first person on a micro, then stand, walk,
    // strafe both ways, crouch (crawl) and walk, draw the size gun and walk/look around.
    // Logs per phase how much the camera jumps from frame to frame relative to the body
    // (in human cm) and how far the body yaw is from the view yaw, and screenshots each phase.
    internal class PovMotionTest : MonoBehaviour
    {
        private string _out;
        private Humanoid _h;
        private NewPlayerMicroController _mover;
        private BaseCameraController _ctrl;

        private IEnumerator Start()
        {
            _out = Path.Combine(Paths.BepInExRootPath, "selftest");
            Directory.CreateDirectory(_out);
            SceneLoader loader = null;
            while ((loader = FindObjectOfType<SceneLoader>()) == null)
                yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(3f);
            AsyncOperation op = loader.LoadScene(loader.GetSceneList().First(s => s.Scene == "Island_SBE"));
            while (op != null && !op.isDone)
                yield return null;
            while (GameController.Instance == null || GameController.LocalClient == null || ObjectManager.Instance == null)
                yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(4f);

            AssetManager am = AssetManager.Instance;
            string name = am.GetFemaleMicroAssetNames().First(n => n.ToLowerInvariant().Contains("unitychan"));
            Transform cam = Camera.main.transform;
            Vector3 pos = cam.position + cam.forward.Flatten().normalized * 4f;
            if (Physics.Raycast(pos + Vector3.up * 200f, Vector3.down, out RaycastHit hit, 1000f, Layers.walkableMask))
                pos = hit.point;
            EntityBase e = null;
            GameController.LocalClient.SpawnMicro(am.GetAssetDescriptionByName(name), pos, Quaternion.LookRotation(cam.forward.Flatten()), 1f, x => e = x);
            while (e == null)
                yield return null;
            yield return new WaitForSeconds(1f);
            _h = (Humanoid)e;
            if (_h.ai != null)
                _h.ai.DisableAI();
            GameController.LocalClient.Player.PlayAs(e as IPlayable);
            yield return new WaitForSeconds(2f);
            GameController.playerInputEnabled = true;
            _ctrl = GameController.LocalClient.Player.Camera.ActiveCameraController;
            var press = AccessTools.Method(_ctrl.GetType(), "OnCameraPress");
            press.Invoke(_ctrl, new[] { System.Activator.CreateInstance(press.GetParameters()[0].ParameterType) });
            _ctrl.orbit.xRotation = 0f;
            _mover = FindObjectOfType<NewPlayerMicroController>();
            foreach (Canvas c in FindObjectsOfType<Canvas>())
                c.enabled = false;
            yield return new WaitForSeconds(1.5f);
            PerspectiveXPlugin.Log.LogInfo($"[SelfTest] motion: first person {_ctrl.FirstPersonMode}, PerspectiveX {PerspectiveXPlugin.Enabled.Value}");

            yield return Phase("stand", 0f, 0f, 1.5f);
            yield return Phase("forward", 0f, 1f, 2.5f);
            yield return Phase("strafe_right", 1f, 0f, 2.5f);
            yield return Phase("strafe_left", -1f, 0f, 2.5f);
            yield return Phase("back", 0f, -1f, 2f);
            yield return Phase("diagonal", 1f, 1f, 2f);
            yield return Phase("look_turn", 0f, 1f, 2.5f, 60f);

            Invoke("OnCrouchPerformed");
            yield return new WaitForSeconds(1f);
            yield return Phase("crouch_stand", 0f, 0f, 2f);
            yield return Phase("crouch_forward", 0f, 1f, 2.5f);
            yield return Phase("crouch_strafe", 1f, 0f, 2f);
            yield return Phase("crouch_look", 0f, 1f, 2.5f, 60f);
            Invoke("OnCrouchPerformed");
            yield return new WaitForSeconds(1f);

            Invoke("SetupAim");
            yield return new WaitForSeconds(1.5f);
            yield return Phase("gun_stand", 0f, 0f, 1.5f);
            yield return Phase("gun_look", 0f, 0f, 2.5f, 60f);
            yield return Phase("gun_forward", 0f, 1f, 2.5f);
            yield return Phase("gun_strafe", 1f, 0f, 2f);
            var gun = _mover.GetRaygun();
            gun.StartFiring();
            yield return Phase("gun_fire", 0f, 0f, 1.2f);
            gun.StopFiring();
            PerspectiveXPlugin.Log.LogInfo($"[SelfTest] motion: gun target {FpGun.Target}, gun fwd {gun.transform.forward}, view fwd {PovDriver.ViewRotation * Vector3.forward}");
            _ctrl.orbit.xRotation = -35f;
            yield return Phase("gun_down", 0f, 0f, 1.5f);
            _ctrl.orbit.xRotation = 0f;
            Invoke("DisableAim");
            yield return new WaitForSeconds(1f);
            PerspectiveXPlugin.Log.LogInfo("[SelfTest] screenshot done");
        }

        private void Invoke(string method)
        {
            var m = AccessTools.Method(typeof(NewPlayerMicroController), method);
            var p = m.GetParameters();
            m.Invoke(_mover, p.Length == 0 ? null : new[] { System.Activator.CreateInstance(p[0].ParameterType) });
            PerspectiveXPlugin.Log.LogInfo($"[SelfTest] motion: {method}");
        }

        // Drive input for a while; log the worst camera jump (relative to the body) per frame
        private IEnumerator Phase(string label, float h, float v, float seconds, float turnDegPerSec = 0f)
        {
            float cm = _h.MeshHeight / 170f;
            Transform cam = Camera.main.transform;
            Vector3 prevRel = cam.position - _h.transform.position;
            float prevYaw = cam.eulerAngles.y;
            float worstJump = 0f, sumJump = 0f, worstYaw = 0f, worstBodyOff = 0f, sumBodyOff = 0f;
            int frames = 0;
            float t = 0f;
            Vector3 p0 = _h.transform.position;
            bool shot = false;
            while (t < seconds)
            {
                Traverse.Create(_mover).Field("horizontalInput").SetValue(h);
                Traverse.Create(_mover).Field("verticalInput").SetValue(v);
                if (turnDegPerSec != 0f)
                    _ctrl.orbit.yRotation += turnDegPerSec * Time.deltaTime;
                yield return null;
                t += Time.deltaTime;
                frames++;
                cam = Camera.main.transform;
                Vector3 rel = cam.position - _h.transform.position;
                float jump = (rel - prevRel).magnitude / cm;
                worstJump = Mathf.Max(worstJump, jump);
                sumJump += jump;
                float yaw = cam.eulerAngles.y;
                worstYaw = Mathf.Max(worstYaw, Mathf.Abs(Mathf.DeltaAngle(prevYaw, yaw)) - Mathf.Abs(turnDegPerSec) * Time.deltaTime);
                float off = Mathf.Abs(Mathf.DeltaAngle(yaw, _h.transform.eulerAngles.y));
                worstBodyOff = Mathf.Max(worstBodyOff, off);
                sumBodyOff += off;
                prevRel = rel;
                prevYaw = yaw;
                if (!shot && t > seconds * 0.6f)
                {
                    shot = true;
                    ScreenCapture.CaptureScreenshot(Path.Combine(_out, $"motion_{label}.png"));
                }
            }
            Traverse.Create(_mover).Field("horizontalInput").SetValue(0f);
            Traverse.Create(_mover).Field("verticalInput").SetValue(0f);
            float moved = (_h.transform.position - p0).Flatten().magnitude / (_h.MeshHeight / 1.7f);
            PerspectiveXPlugin.Log.LogInfo($"[SelfTest] motion {label,-15} cam jump/frame worst {worstJump,5:0.0} cm avg {sumJump / frames,4:0.0} cm | yaw jump {worstYaw,5:0.0} deg | body vs view yaw avg {sumBodyOff / frames,5:0.0} worst {worstBodyOff,5:0.0} deg | moved {moved:0.0} m");
        }
    }
}
