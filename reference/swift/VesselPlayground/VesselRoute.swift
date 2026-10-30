//
//  VesselRoute.swift
//  Mar2025VascCath
//
//  The path the devices currently occupy: an ordered chain of centerline
//  segments starting at the access site. Sampled into one polyline with
//  parallel-transport frames so a device's twist is measured against a frame
//  that does not spin as the vessel curves.
//

import simd

struct VesselRoute: Sendable {
    /// One segment of the chain, walked from→to when `forward`.
    struct Leg: Equatable, Sendable {
        let segment: Int
        let forward: Bool
    }

    /// Position + orientation at an arc length along the route.
    struct Frame: Sendable {
        let position: SIMD3<Float>
        let tangent: SIMD3<Float>
        /// Twist-zero direction, perpendicular to `tangent`.
        let normal: SIMD3<Float>
        /// Lumen radius (0 outside the vessel).
        let radius: Float

        var binormal: SIMD3<Float> { simd_cross(tangent, normal) }

        /// Unit direction perpendicular to the tangent, rotated `twist` from `normal`.
        func direction(twist: Float) -> SIMD3<Float> { normal * cos(twist) + binormal * sin(twist) }
    }

    let graph: VesselGraph
    private(set) var legs: [Leg]

    private(set) var points: [SIMD3<Float>] = []
    private(set) var radii: [Float] = []
    private(set) var tangents: [SIMD3<Float>] = []
    private(set) var normals: [SIMD3<Float>] = []
    /// Cumulative arc length at each sample.
    private(set) var arcLengths: [Float] = []
    /// Arc length where each leg starts.
    private(set) var legStarts: [Float] = []

    init(graph: VesselGraph) {
        self.graph = graph
        let first = graph.segments(at: graph.insertionNode)[0]
        legs = [Leg(segment: first.id, forward: first.from == graph.insertionNode)]
        resample()
    }

    var length: Float { arcLengths.last ?? 0 }

    /// Node at the far end of the last leg.
    var endNode: Int { endNode(of: legs[legs.count - 1]) }

    var endsAtJunction: Bool { graph.nodes[endNode]?.kind == .junction }

    // MARK: - Editing

    mutating func append(_ leg: Leg) {
        legs.append(leg)
        resample()
    }

    /// Drops the last leg; the first leg (from the access site) always stays.
    mutating func removeLastLeg() {
        guard legs.count > 1 else { return }
        legs.removeLast()
        resample()
    }

    /// Legs leaving the route's end junction, excluding the way it came in.
    func nextLegs() -> [Leg] {
        guard endsAtJunction else { return [] }
        let node = endNode
        let arrivedOn = legs[legs.count - 1].segment
        return graph.segments(at: node)
            .filter { $0.id != arrivedOn }
            .map { Leg(segment: $0.id, forward: $0.from == node) }
    }

    /// Unit direction a candidate leg heads in, sampled `lookAhead` metres
    /// past the junction so a curved branch reads by its overall heading.
    func heading(of leg: Leg, lookAhead: Float = 0.006) -> SIMD3<Float> {
        let pts = orderedPoints(of: leg)
        guard let start = pts.first else { return .zero }
        var travelled: Float = 0
        var target = pts[pts.count - 1]
        for (a, b) in zip(pts, pts.dropFirst()) {
            travelled += simd_distance(a, b)
            if travelled >= lookAhead { target = b; break }
        }
        return simd_normalize(target - start)
    }

    // MARK: - Sampling

    /// Frame at arc length `s`. Before the access site (s < 0) the route
    /// continues straight out of the vessel along the entry direction.
    func frame(at s: Float) -> Frame {
        guard points.count > 1 else {
            return Frame(position: points.first ?? .zero, tangent: [0, 1, 0], normal: [1, 0, 0], radius: 0)
        }
        if s <= 0 {
            return Frame(position: points[0] + tangents[0] * s, tangent: tangents[0],
                         normal: normals[0], radius: s < 0 ? 0 : radii[0])
        }
        let s = min(s, length)
        var lo = 0, hi = arcLengths.count - 1
        while hi - lo > 1 {
            let mid = (lo + hi) / 2
            if arcLengths[mid] <= s { lo = mid } else { hi = mid }
        }
        let span = arcLengths[hi] - arcLengths[lo]
        let f = span > 0 ? (s - arcLengths[lo]) / span : 0
        let tangent = simd_normalize(simd_mix(tangents[lo], tangents[hi], SIMD3(repeating: f)))
        var normal = simd_mix(normals[lo], normals[hi], SIMD3(repeating: f))
        normal = simd_normalize(normal - tangent * simd_dot(normal, tangent))
        return Frame(position: simd_mix(points[lo], points[hi], SIMD3(repeating: f)),
                     tangent: tangent, normal: normal,
                     radius: radii[lo] + (radii[hi] - radii[lo]) * f)
    }

    // MARK: - Private

    private func endNode(of leg: Leg) -> Int {
        let seg = graph.segments[leg.segment]!
        return leg.forward ? seg.to : seg.from
    }

    private func orderedPoints(of leg: Leg) -> [SIMD3<Float>] {
        guard let seg = graph.segments[leg.segment] else { return [] }
        return leg.forward ? seg.points : seg.points.reversed()
    }

    private mutating func resample() {
        points = []; radii = []
        var legStartIndices: [Int] = []
        for leg in legs {
            guard let seg = graph.segments[leg.segment] else { continue }
            var pts = seg.points, rad = seg.radii
            if !leg.forward { pts.reverse(); rad.reverse() }
            if !points.isEmpty { pts.removeFirst(); rad.removeFirst() }   // shared junction point
            legStartIndices.append(max(0, points.count - 1))
            points += pts; radii += rad
        }

        arcLengths = [0]
        for i in 1..<max(points.count, 1) { arcLengths.append(arcLengths[i - 1] + simd_distance(points[i - 1], points[i])) }
        legStarts = legStartIndices.map { arcLengths[$0] }

        let n = points.count
        tangents = (0..<n).map { i in
            let a = points[max(0, i - 1)], b = points[min(n - 1, i + 1)]
            let d = b - a
            return simd_length(d) > 0 ? simd_normalize(d) : [0, 1, 0]
        }
        // Parallel transport: carry the normal along, removing only the
        // component the tangent change introduces (fine at 1 mm spacing).
        normals = []
        guard let t0 = tangents.first else { return }
        let reference: SIMD3<Float> = abs(t0.z) < 0.9 ? [0, 0, 1] : [1, 0, 0]
        var normal = simd_normalize(reference - t0 * simd_dot(reference, t0))
        for t in tangents {
            normal = simd_normalize(normal - t * simd_dot(normal, t))
            normals.append(normal)
        }
    }
}
