//
//  DeviceShape.swift
//  Mar2025VascCath
//
//  Turns a device's insertion/twist into a centerline for rendering: the
//  route from just outside the access site up to where the tip starts,
//  then the tip's shape (TipShape) laid along the route in its
//  twist-rotated bend plane, so the tip visibly points where it will steer.
//  The tip's farthest-forward point sits at the insertion depth, and the
//  whole shape is shrunk to fit the narrowest lumen it spans.
//

import simd

enum DeviceShape {
    struct Sample: Equatable {
        let position: SIMD3<Float>
        /// Ring orientation reference — rotates with twist so a stripe on the
        /// device shows it turning.
        let twistNormal: SIMD3<Float>
    }

    /// Smallest the tip is shrunk to, so a lumen narrower than the device
    /// can't collapse it to a point.
    private static let minimumFit: Float = 0.05

    static func samples(for device: FlexDevice, on route: VesselRoute, step: Float = 0.001) -> [Sample] {
        let end = device.insertion
        let tip = device.tip.curve(scale: device.tipScale, step: min(step, 0.0005 * device.tipScale))
        let forward = tip.map(\.x).max() ?? 0
        let back = tip.map(\.x).min() ?? 0
        let reach = tip.map { abs($0.y) }.max() ?? 0

        // Inside the vessel the shape is limited by the lumen wall.
        var fit: Float = 1
        if reach > 0, let room = lumenRoom(for: device, on: route, from: end - (forward - back), to: end, step: step) {
            fit = max(minimumFit, min(1, room / reach))
        }
        let tipStart = end - forward * fit

        var samples = shaft(for: device, on: route, to: tipStart, step: step)
        for point in tip.dropFirst() {
            let frame = route.frame(at: tipStart + point.x * fit)
            let bend = frame.direction(twist: device.twist)
            samples.append(Sample(position: frame.position + bend * point.y * fit, twistNormal: bend))
        }
        return samples
    }

    /// The route from `externalLength` outside the access site to `end`.
    private static func shaft(for device: FlexDevice, on route: VesselRoute, to end: Float, step: Float) -> [Sample] {
        let start = -device.externalLength
        let count = max(2, Int(((end - start) / step).rounded(.up)) + 1)
        return (0..<count).map { i in
            let frame = route.frame(at: start + (end - start) * Float(i) / Float(count - 1))
            return Sample(position: frame.position, twistNormal: frame.direction(twist: device.twist))
        }
    }

    /// Narrowest clearance between the device and the lumen wall over
    /// `from...to`; nil when that stretch is entirely outside the vessel.
    private static func lumenRoom(for device: FlexDevice, on route: VesselRoute,
                                  from: Float, to: Float, step: Float) -> Float? {
        var room: Float?
        for s in stride(from: from, through: to, by: step) {
            let radius = route.frame(at: s).radius
            guard radius > 0 else { continue }
            room = min(room ?? .infinity, max(0, radius - device.radius))
        }
        return room
    }
}
