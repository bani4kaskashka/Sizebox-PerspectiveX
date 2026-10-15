using UnityEngine;

namespace SizeboxPerspectiveX
{
    // Runs after Sizebox's camera controllers and IK (LateUpdate, very late) and overrides the
    // first-person camera pose.
    [DefaultExecutionOrder(31000)]
    internal class PovDriver : MonoBehaviour
    {
        // Head collapse state, shared with HeadRestorer which undoes it early next frame.
        internal static Transform CollapsedHead;
        internal static Vector3 CollapsedHeadScale;
        internal static Vector3 CollapsedHeadLocalPos;
        // Camera we moved this frame; put back on Sizebox's rig before the game's own Update
        // reads it (first-person movement steers by the camera's direction).
        internal static Transform Posed;

        private Humanoid _entity;
        private bool _active;
        private Vector3 _smoothPos;
        private bool _smoothInit;
        private Quaternion _swayBaseline;
        private bool _swayInit;
        private Camera _cam;
        private int _playerLayerBit;

        private void Start()
        {
            _playerLayerBit = 1 << LayerMask.NameToLayer("Player");
        }

        private void LateUpdate()
        {
            Humanoid entity = null;
            BaseCameraController ctrl = null;
            if (PerspectiveXPlugin.Enabled.Value && GameController.LocalClient != null && GameController.LocalClient.Player != null)
            {
                Player player = GameController.LocalClient.Player;
                ctrl = player.Camera?.ActiveCameraController;
                entity = player.Entity as Humanoid;
                if (ctrl == null || !ctrl.enabled || !ctrl.FirstPersonMode || entity == null || entity.Animator == null)
                    entity = null;
            }

            if (entity != _entity || (entity == null && _active))
                Exit();
            if (entity == null)
                return;

            _entity = entity;
            _cam = Camera.main;
            if (_cam == null)
                return;
            _active = true;
            Place(entity);
        }

        private void Place(Humanoid h)
        {
            Animator anim = h.Animator;
            Transform head = anim.GetBoneTransform(HumanBodyBones.Head);
            if (head == null)
                return;

            // Everything below is in "human centimetres": this character's height / 1.7 m.
            float cm = h.MeshHeight / 170f;

            // Sizebox's camera rig already turned the view from the mouse; keep its direction,
            // drop any roll so the horizon stays level. Read it from the rig, never from the
            // camera itself: the camera still carries our own previous frame's adjustment.
            Transform rig = _cam.transform.parent;
            Quaternion gameRot = rig != null ? rig.rotation : _cam.transform.rotation;
            Vector3 fwd = gameRot * Vector3.forward;
            Quaternion look = Mathf.Abs(fwd.y) > 0.999f ? gameRot : Quaternion.LookRotation(fwd, Vector3.up);

            float sway = PerspectiveXPlugin.HeadSway.Value;
            if (sway > 0f)
            {
                Vector3 headFwd = head.forward;
                if (Mathf.Abs(headFwd.y) < 0.999f)
                {
                    // Head rotation without roll, split into a slow baseline and the fast
                    // animation part; only the fast part sways the view.
                    Quaternion headNoRoll = Quaternion.LookRotation(headFwd, Vector3.up);
                    if (!_swayInit)
                    {
                        _swayBaseline = headNoRoll;
                        _swayInit = true;
                    }
                    _swayBaseline = Quaternion.Slerp(_swayBaseline, headNoRoll, 1f - Mathf.Exp(-2.5f * Time.deltaTime));
                    Quaternion animDelta = headNoRoll * Quaternion.Inverse(_swayBaseline);
                    look = Quaternion.Slerp(Quaternion.identity, animDelta, sway) * look;
                }
            }

            Vector3 eyes = EyeMidpoint(anim, head, h.transform, cm);
            Vector3 flatFwd = Vector3.ProjectOnPlane(look * Vector3.forward, Vector3.up);
            if (flatFwd.sqrMagnitude < 1e-6f)
                flatFwd = h.transform.forward;
            flatFwd.Normalize();
            Vector3 target = eyes + flatFwd * (PerspectiveXPlugin.ForwardOffset.Value * cm) + Vector3.up * (PerspectiveXPlugin.UpOffset.Value * cm);

            float smoothing = PerspectiveXPlugin.PositionSmoothing.Value;
            if (!_smoothInit || smoothing <= 0f || (target - _smoothPos).sqrMagnitude > (100f * cm) * (100f * cm))
            {
                _smoothPos = target; // first frame, or teleported/resized: snap
                _smoothInit = true;
            }
            else
            {
                float followSpeed = Mathf.Lerp(70f, 4f, smoothing);
                _smoothPos = Vector3.Lerp(_smoothPos, target, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));
            }

            _cam.transform.SetPositionAndRotation(_smoothPos, look);
            Posed = _cam.transform;
            _cam.nearClipPlane = PerspectiveXPlugin.NearClip.Value * cm;
            if (PerspectiveXPlugin.Fov.Value > 1f)
                _cam.fieldOfView = PerspectiveXPlugin.Fov.Value;
            if (PerspectiveXPlugin.ShowBody.Value)
                _cam.cullingMask |= _playerLayerBit;

            // Collapse the head bone for this frame's rendering, after its position was read.
            // HeadRestorer puts it back before next frame's animation, so the animation, IK and
            // look-at keep working on a normal head and nothing is saved into the character.
            if (PerspectiveXPlugin.HideHead.Value)
            {
                CollapsedHead = head;
                CollapsedHeadScale = head.localScale;
                CollapsedHeadLocalPos = head.localPosition;
                // Collapse into the neck, not at the head bone: vertices that are only partly
                // weighted to the head would otherwise stretch into a cone up to just under the
                // eyes. Folded into the neck they disappear below the view.
                Transform neck = anim.GetBoneTransform(HumanBodyBones.Neck);
                if (neck != null)
                    head.position = neck.position;
                head.localScale = head.localScale * 0.0001f;
            }
        }

        private static Vector3 EyeMidpoint(Animator anim, Transform head, Transform body, float cm)
        {
            Transform l = anim.GetBoneTransform(HumanBodyBones.LeftEye);
            Transform r = anim.GetBoneTransform(HumanBodyBones.RightEye);
            if (l != null && r != null)
                return (l.position + r.position) * 0.5f;
            // No eye bones mapped: eyes sit about 7 cm above and 8 cm in front of the head bone.
            // Use the body's up/forward, not the head bone's axes: converted MMD rigs orient
            // their bones arbitrarily.
            return head.position + body.up * (7f * cm) + body.forward * (8f * cm);
        }

        private void Exit()
        {
            HeadRestorer.Restore();
            if (_active && _cam != null)
            {
                // Sizebox's giantess camera never resets the camera's local pose itself.
                _cam.transform.localPosition = Vector3.zero;
                _cam.transform.localRotation = Quaternion.identity;
            }
            _active = false;
            _entity = null;
            _smoothInit = false;
            _swayInit = false;
        }

        private void OnDisable() => Exit();
    }

    [DefaultExecutionOrder(-31000)]
    internal class HeadRestorer : MonoBehaviour
    {
        // FixedUpdate runs before Update in a frame, so restore there too: physics and head
        // colliders never see the collapsed head.
        private void FixedUpdate() => Restore();

        private void Update() => Restore();

        internal static void Restore()
        {
            if (PovDriver.Posed != null)
            {
                PovDriver.Posed.localPosition = Vector3.zero;
                PovDriver.Posed.localRotation = Quaternion.identity;
                PovDriver.Posed = null;
            }
            if (PovDriver.CollapsedHead != null)
            {
                PovDriver.CollapsedHead.localScale = PovDriver.CollapsedHeadScale;
                PovDriver.CollapsedHead.localPosition = PovDriver.CollapsedHeadLocalPos;
            }
            PovDriver.CollapsedHead = null;
        }
    }
}
