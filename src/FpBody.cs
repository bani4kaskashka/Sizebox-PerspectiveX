using HarmonyLib;
using UnityEngine;

namespace SizeboxPerspectiveX
{
    // In first person Sizebox snaps the micro to face wherever it walks (Rigidbody.MoveRotation
    // to the move direction): pressing A or D turns the body 90 degrees in one step, and since
    // the eyes sit in front of the neck the view slides sideways. With PerspectiveX the body
    // faces where you look instead and walks in any direction. The game also slows you down
    // the further the move direction is from the way the body faces (meant for turning); that
    // penalty is taken back out, except a little for walking backwards.
    [HarmonyPatch(typeof(NewPlayerMicroController), "MovementManagement")]
    internal static class FpBodyPatch
    {
        private static readonly AccessTools.FieldRef<NewPlayerMicroController, Vector3> PrevVelocity =
            AccessTools.FieldRefAccess<NewPlayerMicroController, Vector3>("_previousVelocity");
        private static readonly AccessTools.FieldRef<NewPlayerMicroController, Quaternion> CamPlaneRot =
            AccessTools.FieldRefAccess<NewPlayerMicroController, Quaternion>("_cameraRotationOnPlane");
        private static readonly AccessTools.FieldRef<NewPlayerMicroController, Vector3> CamPlaneFwd =
            AccessTools.FieldRefAccess<NewPlayerMicroController, Vector3>("_cameraForwardOnPlane");
        private static readonly AccessTools.FieldRef<NewPlayerMicroController, PlayerMicroSettings> Settings =
            AccessTools.FieldRefAccess<NewPlayerMicroController, PlayerMicroSettings>("settings");

        private static bool Active(NewPlayerMicroController c) =>
            PerspectiveXPlugin.Enabled.Value && PerspectiveXPlugin.BodyFollowsView.Value && PovDriver.InFirstPerson
            && !c.IsAiming && !c.IsClimbing && !c.IsFlying;

        private static void Prefix(NewPlayerMicroController __instance, out Vector3 __state)
        {
            __state = PrevVelocity(__instance);
        }

        private static void Postfix(NewPlayerMicroController __instance, float horizontal, float vertical, bool isGrounded, Vector3 __state)
        {
            if (!Active(__instance))
                return;
            Humanoid micro = PovDriver.Entity;
            if (micro == null)
                return;
            Rigidbody rb = micro.Rigidbody;
            Vector3 face = CamPlaneFwd(__instance);
            if (face.sqrMagnitude < 1e-6f)
                return;

            // Undo the turning slowdown for the step just computed (grounded movement only)
            var input = new Vector3(horizontal, 0f, vertical);
            if (isGrounded && input.sqrMagnitude > 0.01f)
            {
                Vector3 dir = CamPlaneRot(__instance) * input.normalized;
                float angle = Vector3.Angle(dir, micro.transform.forward);
                float applied = (180f - angle) / 180f * 0.8f + 0.2f;
                float wanted = angle <= 100f ? 1f : Mathf.Lerp(1f, 0.75f, (angle - 100f) / 80f);
                if (applied < wanted - 0.001f)
                {
                    float k = Mathf.Clamp01(Settings(__instance).movementSmoothing * Time.fixedDeltaTime);
                    Vector3 v = rb.velocity;
                    Vector3 vh = new Vector3(v.x, 0f, v.z), ph = new Vector3(__state.x, 0f, __state.z);
                    // v = p + (T*applied - p) * k  =>  T*applied = p + (v - p) / k
                    Vector3 targetApplied = k > 0.999f ? vh : ph + (vh - ph) / Mathf.Max(k, 1e-4f);
                    Vector3 target = targetApplied * (wanted / applied);
                    Vector3 nh = Vector3.Lerp(ph, target, k);
                    var fixedV = new Vector3(nh.x, v.y, nh.z);
                    rb.velocity = fixedV;
                    PrevVelocity(__instance) = fixedV;
                }
            }
            rb.MoveRotation(Quaternion.LookRotation(face));
        }
    }

    // The size gun in first person: Sizebox parents the gun to the camera as a floating view
    // model, apart from your hand. With your own body on screen that looks broken, so with
    // PerspectiveX the gun stays in your right hand and the arm aims (the game's own aim IK,
    // as in third person) at what the crosshair is on.
    [HarmonyPatch(typeof(PlayerMicroCameraController), "PositionRaygun")]
    internal static class FpGunPositionPatch
    {
        private static bool Prefix(PlayerMicroCameraController __instance)
        {
            if (!FpGun.Active(__instance))
                return true;
            var mover = Traverse.Create(__instance).Field("movementController").GetValue<NewPlayerMicroController>();
            if (mover != null && mover.IsAiming)
                mover.ResetRaygunPosition();
            return false;
        }
    }

    // While aiming in first person: body faces the view, the arm holds the gun a little low
    // and to the right (aimed at a point offset from the crosshair, so it doesn't cover the
    // middle of the screen), and FpGun.Target is where the crosshair really is.
    [HarmonyPatch(typeof(NewPlayerMicroController), "UpdateAiming")]
    internal static class FpGunAimPatch
    {
        private static readonly AccessTools.FieldRef<NewPlayerMicroController, PlayerIK> Ik =
            AccessTools.FieldRefAccess<NewPlayerMicroController, PlayerIK>("playerIk");
        private static readonly AccessTools.FieldRef<NewPlayerMicroController, Vector3> CamPlaneFwd =
            AccessTools.FieldRefAccess<NewPlayerMicroController, Vector3>("_cameraForwardOnPlane");

        private static bool Prefix(NewPlayerMicroController __instance)
        {
            if (!__instance.IsAiming || !GameController.playerInputEnabled || !FpGun.Active(null) || __instance.IsFlying)
                return true;
            Humanoid me = PovDriver.Entity;
            if (me == null || !PovDriver.HasView)
                return true;
            Vector3 face = CamPlaneFwd(__instance);
            if (face.sqrMagnitude > 1e-6f)
                me.Rigidbody.MoveRotation(Quaternion.LookRotation(face));

            Vector3 eye = PovDriver.ViewPosition;
            Quaternion look = PovDriver.ViewRotation;
            float scale = me.Scale;
            Vector3 fwd = look * Vector3.forward;
            FpGun.Target = Physics.Raycast(eye, fwd, out RaycastHit hit, 100f * scale, __instance.aimingMask)
                ? hit.point
                : eye + fwd * (100f * scale);
            FpGun.HasTarget = true;

            Quaternion hip = look * Quaternion.Euler(PerspectiveXPlugin.GunDown.Value, PerspectiveXPlugin.GunRight.Value, 0f);
            __instance.aimPoint.position = eye + hip * Vector3.forward * (3f * scale);
            var ik = Ik(__instance)?.GetAimIk();
            if (ik != null)
                ik.solver.target = __instance.aimPoint;
            return false;
        }
    }

    // Shots leave the gun along its forward axis; for the moment of firing, point the gun at
    // the crosshair target, then put it back where the arm holds it.
    [HarmonyPatch]
    internal static class FpGunFirePatch
    {
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PlayerRaygun), "FireProjectile");
            yield return AccessTools.Method(typeof(PlayerRaygun), "FireContinuousLaser");
        }

        private static void Prefix(PlayerRaygun __instance, out Quaternion? __state)
        {
            __state = null;
            if (!FpGun.HasTarget || !FpGun.Active(null))
                return;
            Transform t = __instance.transform;
            Vector3 dir = FpGun.Target - t.position;
            if (dir.sqrMagnitude < 1e-8f)
                return;
            __state = t.rotation;
            t.rotation = Quaternion.LookRotation(dir, t.up);
        }

        private static void Postfix(PlayerRaygun __instance, Quaternion? __state)
        {
            if (__state.HasValue)
                __instance.transform.rotation = __state.Value;
        }
    }

    internal static class FpGun
    {
        internal static Vector3 Target;
        internal static bool HasTarget;

        internal static bool Active(BaseCameraController ctrl) =>
            PerspectiveXPlugin.Enabled.Value && PerspectiveXPlugin.GunInHand.Value
            && (ctrl != null ? ctrl.FirstPersonMode : PovDriver.InFirstPerson);
    }
}
