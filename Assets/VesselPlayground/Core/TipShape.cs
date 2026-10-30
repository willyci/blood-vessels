// Pre-shaped tips the devices can be switched between. Each shape is a chain of
// constant-curvature bends in the tip's bend plane, walked from the end of the straight
// shaft to the tip. The plane turns with the device's twist, so every shaped tip steers
// the same way; the shape decides how it looks and how readily it catches a side branch.
//
// Dimensions are approximate stock (device scale 1) sizes, kept smaller than real catheter
// curves so they read inside the playground's vessels. Port of TipShape.swift.

using System.Collections.Generic;
using UnityEngine;

namespace BloodVessels
{
    public enum TipShape
    {
        // Guidewire tips.
        Straight, JTip, Angled,
        // Catheter tips.
        Bern, Cobra, Mikaelsson,
    }

    public static class TipShapes
    {
        /// One piece of the tip: `Length` (m) turning by `Angle` (rad) toward the bend
        /// direction (negative turns back). Angle 0 is a straight run.
        public readonly struct Bend
        {
            public readonly float Length, Angle;
            public Bend(float length, float angle) { Length = length; Angle = angle; }
        }

        const float Mm = 0.001f, Deg = Mathf.PI / 180;

        public static string Label(this TipShape shape) => shape switch
        {
            TipShape.Straight => "Straight",
            TipShape.JTip => "J-tip",
            TipShape.Angled => "Angled",
            TipShape.Bern => "Bern",
            TipShape.Cobra => "Cobra",
            _ => "Mikaelsson",
        };

        public static TipShape[] OptionsFor(DeviceKind kind) => kind == DeviceKind.Wire
            ? new[] { TipShape.Straight, TipShape.JTip, TipShape.Angled }
            : new[] { TipShape.Bern, TipShape.Cobra, TipShape.Mikaelsson };

        /// The next option for the same device, wrapping around.
        public static TipShape Next(this TipShape shape, DeviceKind kind)
        {
            var options = OptionsFor(kind);
            int i = System.Array.IndexOf(options, shape);
            return options[(i + 1) % options.Length];
        }

        public static Bend[] Bends(this TipShape shape) => shape switch
        {
            TipShape.Straight => new Bend[0],
            // 3 mm radius half-circle.
            TipShape.JTip => new[] { new Bend(Mathf.PI * 3 * Mm, 180 * Deg) },
            TipShape.Angled => new[] { new Bend(2 * Mm, 45 * Deg), new Bend(6 * Mm, 0) },
            // Berenstein: one short, gentle angle.
            TipShape.Bern => new[] { new Bend(4 * Mm, 30 * Deg), new Bend(8 * Mm, 0) },
            // Broad primary curve, then the "head" turns back toward forward.
            TipShape.Cobra => new[]
            {
                new Bend(8 * Mm, 60 * Deg), new Bend(6 * Mm, 0),
                new Bend(3 * Mm, -45 * Deg), new Bend(2 * Mm, 0),
            },
            // Reverse curve: a hairpin loop, a limb running back alongside the shaft,
            // and a tip hooked further round.
            _ => new[]
            {
                new Bend(Mathf.PI * 4 * Mm, 180 * Deg), new Bend(8 * Mm, 0),
                new Bend(3 * Mm, 45 * Deg), new Bend(2 * Mm, 0),
            },
        };

        /// A side branch is entered only when the bend points within this angle of it.
        /// Null: the tip can't steer and always takes the straightest leg. The J-tip is
        /// meant to avoid small side branches, so its window is narrow.
        public static float? CaptureAngle(this TipShape shape) => shape switch
        {
            TipShape.Straight => null,
            TipShape.JTip => 30 * Deg,
            _ => 55 * Deg,
        };

        /// The tip as a 2D polyline in its bend plane: x along the shaft (forward), y toward
        /// the bend. Starts at the shaft end (0, 0); a straight tip is that point alone.
        public static List<Vector2> Curve(this TipShape shape, float scale, float step)
        {
            var points = new List<Vector2> { Vector2.zero };
            float heading = 0;
            foreach (var bend in shape.Bends())
            {
                float length = bend.Length * scale;
                int pieces = Mathf.Max(bend.Angle == 0 ? 1 : 4, Mathf.CeilToInt(length / step));
                float piece = length / pieces, turn = bend.Angle / pieces;
                for (int i = 0; i < pieces; i++)
                {
                    heading += turn / 2;
                    points.Add(points[points.Count - 1] + piece * new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)));
                    heading += turn / 2;
                }
            }
            return points;
        }
    }
}
