// When no XR device is running (Play mode on a Mac, where Quest Link doesn't exist), the
// camera rig gets no head pose and would sit at floor level under the table. This lifts the
// rig to standing eye height, tilts it toward the table, and gives the view a visible
// background instead of the transparent passthrough clear. On the headset it does nothing.

using System.Collections;
using UnityEngine;
using UnityEngine.XR;

public sealed class VesselEditorViewFallback : MonoBehaviour
{
    public float EyeHeight = 1.6f;
    [Tooltip("Downward tilt toward the table (degrees).")]
    public float Pitch = 40f;
    public Color Background = new(0.33f, 0.36f, 0.4f);

    IEnumerator Start()
    {
        yield return null;   // give the XR loader a frame to start a device
        if (XRSettings.isDeviceActive) yield break;

        transform.position += Vector3.up * EyeHeight;
        transform.rotation = Quaternion.Euler(Pitch, 0, 0) * transform.rotation;
        foreach (var eye in GetComponentsInChildren<Camera>()) eye.backgroundColor = Background;
        Debug.Log("[VesselPlayground] No XR device: showing a fixed standing view (keyboard controls: W/S, A/D, Tab, T, M, C, R).");
    }
}
