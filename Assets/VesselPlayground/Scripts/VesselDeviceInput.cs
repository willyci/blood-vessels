// Drives the playground devices from Quest hands, Quest controllers, and (in the Editor) the
// keyboard. Left hand / left controller = catheter, right = wire, as in the visionOS app.
//
// Hands or controllers: while pinching (hands) or holding the index trigger (controllers),
//   moving along the push direction pushes / pulls and rolling the wrist about it twists.
//   Release to ratchet for long advances. Port of VesselHandInput.swift.
// Thumbsticks: up/down pushes, left/right twists (fine control).
// Controller buttons: A = next wire tip, B = next catheter tip, X = next model,
//   Y = show centerlines, thumbstick click = reset.
// Keyboard (Editor / Meta XR Simulator): Tab = switch device, W/S = push, A/D = twist
//   (Shift = faster), T = next tip, M = next model, C = centerlines, R = reset.
//
// Also publishes fingertip / controller-tip positions for VesselPokeButton.

using System.Collections.Generic;
using BloodVessels;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DefaultExecutionOrder(-50)]
public sealed class VesselDeviceInput : MonoBehaviour
{
    public VesselPlaygroundController Playground;

    [Header("Tracked anchors (OVRCameraRig)")]
    public Transform LeftHandAnchor;
    public Transform RightHandAnchor;
    public OVRHand LeftHand;
    public OVRHand RightHand;
    public OVRSkeleton LeftSkeleton;
    public OVRSkeleton RightSkeleton;

    [Header("Tuning")]
    [Tooltip("Device travel per metre of hand travel.")]
    public float PushScale = 0.5f;
    public float TwistScale = 1f;
    [Tooltip("Thumbstick / keyboard push speed (m/s at full deflection).")]
    public float StickPushSpeed = 0.02f;
    [Tooltip("Thumbstick / keyboard twist speed (deg/s at full deflection).")]
    public float StickTwistSpeed = 120f;
    const float StickDeadZone = 0.2f;

    /// World positions that can press a VesselPokeButton this frame (index fingertips,
    /// controller tips).
    public static readonly List<Vector3> Pointers = new();

    struct Grip
    {
        public Vector3 Position;
        public Quaternion Rotation;
    }

    readonly Dictionary<DeviceKind, Grip?> grips = new() { [DeviceKind.Wire] = null, [DeviceKind.Catheter] = null };

    CathWirePlaygroundModel Model => Playground != null ? Playground.Model : null;

    void Update()
    {
        Pointers.Clear();
        if (Model == null) return;
        TrackSide(DeviceKind.Catheter, LeftHandAnchor, LeftHand, LeftSkeleton, OVRInput.Controller.LTouch);
        TrackSide(DeviceKind.Wire, RightHandAnchor, RightHand, RightSkeleton, OVRInput.Controller.RTouch);
        ControllerButtons();
        Keyboard();
    }

    // MARK: - Hands / controllers

    void TrackSide(DeviceKind kind, Transform anchor, OVRHand hand, OVRSkeleton skeleton, OVRInput.Controller controller)
    {
        if (anchor == null) return;
        bool handTracked = hand != null && hand.IsTracked;
        bool controllerConnected = OVRInput.IsControllerConnected(controller);

        if (handTracked && FingerTip(skeleton) is Vector3 tip) Pointers.Add(tip);
        else if (controllerConnected) Pointers.Add(anchor.position + anchor.forward * 0.04f);

        bool gripping = (handTracked && hand.GetFingerIsPinching(OVRHand.HandFinger.Index))
                        || (controllerConnected && OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, controller));
        if (!gripping)
        {
            grips[kind] = null;
        }
        else
        {
            var grip = new Grip { Position = anchor.position, Rotation = anchor.rotation };
            if (grips[kind] is Grip last)
            {
                var axis = Playground.InsertionAxis;
                float push = Vector3.Dot(grip.Position - last.Position, axis) * PushScale;
                float roll = TwistAngle(grip.Rotation * Quaternion.Inverse(last.Rotation), axis);
                if (push != 0) Model.Push(kind, push);
                if (roll != 0) Model.Twist(kind, roll * TwistScale);
            }
            grips[kind] = grip;
        }

        if (controllerConnected)
        {
            var stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, controller);
            Drive(kind, Mathf.Abs(stick.y) > StickDeadZone ? stick.y : 0, Mathf.Abs(stick.x) > StickDeadZone ? stick.x : 0);
        }
    }

    static Vector3? FingerTip(OVRSkeleton skeleton)
    {
        if (skeleton == null || !skeleton.IsInitialized || !skeleton.IsDataValid) return null;
        foreach (var bone in skeleton.Bones)
            if (bone.Id == OVRSkeleton.BoneId.XRHand_IndexTip || bone.Id == OVRSkeleton.BoneId.Hand_IndexTip)
                return bone.Transform.position;
        return null;
    }

    /// Signed rotation of `q` about `axis` — the "twist" half of the swing–twist split.
    public static float TwistAngle(Quaternion q, Vector3 axis)
    {
        float proj = Vector3.Dot(new Vector3(q.x, q.y, q.z), axis);
        if (Mathf.Abs(proj) < 1e-9f && Mathf.Abs(q.w) < 1e-9f) return 0;
        float angle = 2 * Mathf.Atan2(proj, q.w);
        if (angle > Mathf.PI) angle -= 2 * Mathf.PI;
        if (angle < -Mathf.PI) angle += 2 * Mathf.PI;
        return angle;
    }

    /// Continuous push / twist from a stick or keys, -1…1 each.
    void Drive(DeviceKind kind, float push, float twist)
    {
        float dt = Time.deltaTime;
        if (push != 0) Model.Push(kind, push * StickPushSpeed * dt);
        if (twist != 0) Model.Twist(kind, twist * StickTwistSpeed * Mathf.Deg2Rad * dt);
    }

    void ControllerButtons()
    {
        if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch)) Playground.CycleWireTip();
        if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch)) Playground.CycleCatheterTip();
        if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch)) Playground.NextModel();
        if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.LTouch)) Playground.ToggleCenterlines();
        if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.LTouch)
            || OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.RTouch))
            Playground.ResetDevices();
    }

    // MARK: - Keyboard

    void Keyboard()
    {
#if ENABLE_INPUT_SYSTEM
        var keys = UnityEngine.InputSystem.Keyboard.current;
        if (keys == null) return;
        if (keys.tabKey.wasPressedThisFrame)
            Model.ActiveDevice = Model.ActiveDevice == DeviceKind.Wire ? DeviceKind.Catheter : DeviceKind.Wire;
        if (keys.tKey.wasPressedThisFrame) Playground.CycleTip(Model.ActiveDevice);
        if (keys.mKey.wasPressedThisFrame) Playground.NextModel();
        if (keys.cKey.wasPressedThisFrame) Playground.ToggleCenterlines();
        if (keys.rKey.wasPressedThisFrame) Playground.ResetDevices();

        float fast = keys.shiftKey.isPressed ? 5 : 1;
        float push = (keys.wKey.isPressed ? 1 : 0) - (keys.sKey.isPressed ? 1 : 0);
        float twist = (keys.dKey.isPressed ? 1 : 0) - (keys.aKey.isPressed ? 1 : 0);
        if (push != 0 || twist != 0) Drive(Model.ActiveDevice, push * fast, twist * fast);
#endif
    }
}
