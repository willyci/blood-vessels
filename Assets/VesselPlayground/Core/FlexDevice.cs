// A flexible endovascular device (guidewire or catheter) that follows the vessel route.
// Its shape is the route itself plus a pre-shaped tip (TipShape) whose bend direction
// rotates with `Twist` — that tip is what steers into branches. Port of FlexDevice.swift.

using UnityEngine;

namespace BloodVessels
{
    public enum DeviceKind { Wire, Catheter }

    public static class DeviceKinds
    {
        public static readonly DeviceKind[] All = { DeviceKind.Wire, DeviceKind.Catheter };
        public static string Label(this DeviceKind kind) => kind == DeviceKind.Wire ? "Wire" : "Catheter";
    }

    public struct FlexDevice
    {
        public DeviceKind Kind;
        /// Tip distance past the access site along the route (m).
        public float Insertion;
        /// Rotation about the device axis (rad), relative to the route's parallel-transport
        /// frame. Unbounded so continuous twisting stays smooth.
        public float Twist;
        public float Radius;
        public TipShape Tip;
        /// Tip size relative to the stock shape (the model's device scale).
        public float TipScale;
        /// Length drawn outside the vessel, behind the access site.
        public float ExternalLength;

        /// 0.035" (0.9 mm) angled guidewire.
        public static FlexDevice Guidewire => new()
        {
            Kind = DeviceKind.Wire, Radius = 0.00045f, Tip = TipShape.Angled, TipScale = 1, ExternalLength = 0.09f,
        };

        /// 5 Fr (1.7 mm) Berenstein catheter.
        public static FlexDevice Catheter => new()
        {
            Kind = DeviceKind.Catheter, Radius = 0.00085f, Tip = TipShape.Bern, TipScale = 1, ExternalLength = 0.06f,
        };

        public static FlexDevice Stock(DeviceKind kind) => kind == DeviceKind.Wire ? Guidewire : Catheter;

        /// Same device with radius, tip size and external length scaled, for vessels thinner
        /// than the stock device (tip angles unchanged).
        public FlexDevice Scaled(float factor)
        {
            var d = this;
            d.Radius *= factor;
            d.TipScale *= factor;
            d.ExternalLength *= factor;
            return d;
        }

        /// Twist wrapped to 0..<360 for display.
        public int TwistDegrees
        {
            get
            {
                int deg = Mathf.RoundToInt(Twist * Mathf.Rad2Deg) % 360;
                return deg < 0 ? deg + 360 : deg;
            }
        }
    }
}
