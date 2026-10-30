//
//  TipShape.swift
//  Mar2025VascCath
//
//  Pre-shaped tips the playground's devices can be switched between. Each
//  shape is a chain of constant-curvature bends in the tip's bend plane,
//  walked from the end of the straight shaft to the tip. The plane turns
//  with the device's twist, so every shaped tip steers the same way; the
//  shape decides how it looks and how readily it catches a side branch.
//
//  Dimensions are approximate stock (device scale 1) sizes, kept smaller
//  than real catheter curves so they read inside the playground's vessels.
//

import simd

enum TipShape: String, CaseIterable, Identifiable, Sendable {
    // Guidewire tips.
    case straight, jTip, angled
    // Catheter tips.
    case bern, cobra, mikaelsson

    var id: String { rawValue }

    var label: String {
        switch self {
        case .straight: "Straight"
        case .jTip: "J-tip"
        case .angled: "Angled"
        case .bern: "Bern"
        case .cobra: "Cobra"
        case .mikaelsson: "Mikaelsson"
        }
    }

    static func options(for kind: FlexDevice.Kind) -> [TipShape] {
        kind == .wire ? [.straight, .jTip, .angled] : [.bern, .cobra, .mikaelsson]
    }

    /// One piece of the tip: `length` (m) turning by `angle` (rad) toward
    /// the bend direction (negative turns back). Angle 0 is a straight run.
    struct Bend: Sendable {
        let length: Float
        let angle: Float
    }

    var bends: [Bend] {
        let mm: Float = 0.001, deg: Float = .pi / 180
        switch self {
        case .straight:
            return []
        case .jTip:
            // 3 mm radius half-circle.
            return [Bend(length: .pi * 3 * mm, angle: 180 * deg)]
        case .angled:
            return [Bend(length: 2 * mm, angle: 45 * deg), Bend(length: 6 * mm, angle: 0)]
        case .bern:
            // Berenstein: one short, gentle angle.
            return [Bend(length: 4 * mm, angle: 30 * deg), Bend(length: 8 * mm, angle: 0)]
        case .cobra:
            // Broad primary curve, then the "head" turns back toward forward.
            return [Bend(length: 8 * mm, angle: 60 * deg), Bend(length: 6 * mm, angle: 0),
                    Bend(length: 3 * mm, angle: -45 * deg), Bend(length: 2 * mm, angle: 0)]
        case .mikaelsson:
            // Reverse curve: a hairpin loop, a limb running back alongside
            // the shaft, and a tip hooked further round.
            return [Bend(length: .pi * 4 * mm, angle: 180 * deg), Bend(length: 8 * mm, angle: 0),
                    Bend(length: 3 * mm, angle: 45 * deg), Bend(length: 2 * mm, angle: 0)]
        }
    }

    /// A side branch is entered only when the bend points within this angle
    /// of it. Nil: the tip can't steer and always takes the straightest leg.
    /// The J-tip is meant to avoid small side branches, so its window is narrow.
    var captureAngle: Float? {
        switch self {
        case .straight: nil
        case .jTip: 30 * .pi / 180
        default: 55 * .pi / 180
        }
    }

    /// The tip as a 2D polyline in its bend plane: x along the shaft
    /// (forward), y toward the bend. Starts at the shaft end (0, 0); a
    /// straight tip is that point alone.
    func curve(scale: Float, step: Float) -> [SIMD2<Float>] {
        var points: [SIMD2<Float>] = [.zero]
        var heading: Float = 0
        for bend in bends {
            let length = bend.length * scale
            let pieces = max(bend.angle == 0 ? 1 : 4, Int((length / step).rounded(.up)))
            let piece = length / Float(pieces), turn = bend.angle / Float(pieces)
            for _ in 0..<pieces {
                heading += turn / 2
                points.append(points[points.count - 1] + piece * SIMD2(cos(heading), sin(heading)))
                heading += turn / 2
            }
        }
        return points
    }
}
