// Turns a device's insertion/twist into a centerline for rendering: the route from just
// outside the access site up to where the tip starts, then the tip's shape (TipShape) laid
// along the route in its twist-rotated bend plane, so the tip visibly points where it will
// steer. The tip's farthest-forward point sits at the insertion depth, and the whole shape
// is shrunk to fit the narrowest lumen it spans. Port of DeviceShape.swift.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BloodVessels
{
    public static class DeviceShape
    {
        public readonly struct Sample
        {
            public readonly Vector3 Position;
            /// Ring orientation reference — rotates with twist so a stripe on the device
            /// shows it turning.
            public readonly Vector3 TwistNormal;
            public Sample(Vector3 position, Vector3 twistNormal) { Position = position; TwistNormal = twistNormal; }
        }

        /// Smallest the tip is shrunk to, so a lumen narrower than the device can't
        /// collapse it to a point.
        const float MinimumFit = 0.05f;

        public static List<Sample> Samples(FlexDevice device, VesselRoute route, float step = 0.001f)
        {
            float end = device.Insertion;
            var tip = device.Tip.Curve(device.TipScale, Mathf.Min(step, 0.0005f * device.TipScale));
            float forward = tip.Max(p => p.x);
            float back = tip.Min(p => p.x);
            float reach = tip.Max(p => Mathf.Abs(p.y));

            // Inside the vessel the shape is limited by the lumen wall.
            float fit = 1;
            if (reach > 0 && LumenRoom(device, route, end - (forward - back), end, step) is float room)
                fit = Mathf.Max(MinimumFit, Mathf.Min(1, room / reach));
            float tipStart = end - forward * fit;

            var samples = Shaft(device, route, tipStart, step);
            foreach (var point in tip.Skip(1))
            {
                var frame = route.FrameAt(tipStart + point.x * fit);
                var bend = frame.Direction(device.Twist);
                samples.Add(new Sample(frame.Position + bend * (point.y * fit), bend));
            }
            return samples;
        }

        /// The route from `ExternalLength` outside the access site to `end`.
        static List<Sample> Shaft(FlexDevice device, VesselRoute route, float end, float step)
        {
            float start = -device.ExternalLength;
            int count = Mathf.Max(2, Mathf.CeilToInt((end - start) / step) + 1);
            var samples = new List<Sample>(count + 64);
            for (int i = 0; i < count; i++)
            {
                var frame = route.FrameAt(start + (end - start) * i / (count - 1));
                samples.Add(new Sample(frame.Position, frame.Direction(device.Twist)));
            }
            return samples;
        }

        /// Narrowest clearance between the device and the lumen wall over `from...to`; null
        /// when that stretch is entirely outside the vessel.
        static float? LumenRoom(FlexDevice device, VesselRoute route, float from, float to, float step)
        {
            float? room = null;
            for (float s = from; s <= to + 1e-6f; s += step)
            {
                float radius = route.FrameAt(s).Radius;
                if (radius <= 0) continue;
                room = Mathf.Min(room ?? float.PositiveInfinity, Mathf.Max(0, radius - device.Radius));
            }
            return room;
        }
    }
}
