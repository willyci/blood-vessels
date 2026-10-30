// A physical button on the table edge: touch it with an index fingertip (hands) or the
// controller tip to press it. Fires once per touch. The pointers come from
// VesselDeviceInput.Pointers.

using UnityEngine;
using UnityEngine.Events;

public sealed class VesselPokeButton : MonoBehaviour
{
    public UnityEvent OnPress = new();
    [Tooltip("The cap that dips and lights up while pressed.")]
    public Renderer Cap;
    public Color IdleColor = new(0.2f, 0.25f, 0.3f);
    public Color PressedColor = new(0.1f, 0.6f, 0.95f);

    [Tooltip("Half-size of the touch zone around the button (local units).")]
    public Vector3 TouchHalfExtents = new(0.5f, 1.2f, 0.5f);

    bool touching;
    MaterialPropertyBlock block;

    void Start() => SetLit(false);

    void Update()
    {
        bool now = false;
        foreach (var p in VesselDeviceInput.Pointers)
        {
            var local = transform.InverseTransformPoint(p);
            if (Mathf.Abs(local.x) <= TouchHalfExtents.x && Mathf.Abs(local.y) <= TouchHalfExtents.y
                && Mathf.Abs(local.z) <= TouchHalfExtents.z)
            {
                now = true;
                break;
            }
        }
        if (now && !touching) OnPress.Invoke();
        if (now != touching) SetLit(now);
        touching = now;
    }

    void SetLit(bool lit)
    {
        if (Cap == null) return;
        block ??= new MaterialPropertyBlock();
        block.SetColor("_BaseColor", lit ? PressedColor : IdleColor);
        Cap.SetPropertyBlock(block);
    }
}
