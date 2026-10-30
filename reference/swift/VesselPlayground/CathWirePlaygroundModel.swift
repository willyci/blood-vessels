//
//  CathWirePlaygroundModel.swift
//  Mar2025VascCath
//
//  Push/twist logic for the vessel playground. Both devices share one route.
//  Whichever tip is furthest in leads: when it reaches a junction it picks
//  the branch its bent tip points into, and the other device follows that
//  committed route. Pulling the leading tip back behind a junction
//  un-commits the branch so it can be chosen again.
//

import Foundation
import Observation
import simd

@MainActor
@Observable
final class CathWirePlaygroundModel {
    /// Vessel model the graph came from (picker selection).
    private(set) var spec: VesselModelSpec
    private(set) var graph: VesselGraph
    /// Model frame → world: laid flat, access site on the right.
    private(set) var orientation: simd_quatf
    private(set) var route: VesselRoute
    private(set) var wire = FlexDevice.guidewire
    private(set) var catheter = FlexDevice.catheter

    /// Device the on-screen controls (and a single-hand input) drive.
    var activeDevice: FlexDevice.Kind = .wire
    var showCenterlines = false

    /// Segments of the main trunk (access site → farthest end), for naming.
    private var trunkSegments: Set<Int>

    /// Branch is entered only when the tip points within this angle of it;
    /// otherwise the tip carries straight on along the trunk. Default for
    /// shaped tips; each TipShape sets its own.
    nonisolated static let branchCaptureAngle: Float = 55 * .pi / 180
    /// Tips stop this far short of a dead end.
    static let endMargin: Float = 0.001
    /// "At junction" readout tolerance.
    static let junctionTolerance: Float = 0.003

    init(graph: VesselGraph, spec: VesselModelSpec = .tube) {
        self.spec = spec
        self.graph = graph
        orientation = VesselLayout.flatOrientation(for: graph)
        route = VesselRoute(graph: graph)
        trunkSegments = Self.longestPath(in: graph)
        reset()
    }

    /// Switches to another vessel model: new graph, devices back at the
    /// access site, sized for that model's vessels.
    func load(_ spec: VesselModelSpec, graph: VesselGraph) {
        self.spec = spec
        self.graph = graph
        orientation = VesselLayout.flatOrientation(for: graph)
        trunkSegments = Self.longestPath(in: graph)
        reset()
    }

    // MARK: - Queries

    func device(_ kind: FlexDevice.Kind) -> FlexDevice { kind == .wire ? wire : catheter }

    var leader: FlexDevice.Kind { wire.insertion >= catheter.insertion ? .wire : .catheter }

    var leadingInsertion: Float { max(wire.insertion, catheter.insertion) }

    var tipAtJunction: Bool {
        route.endsAtJunction && route.length - leadingInsertion < Self.junctionTolerance
    }

    var tipAtDeadEnd: Bool {
        !route.endsAtJunction && route.length - leadingInsertion <= Self.endMargin + 0.0005
    }

    /// Junctions passed by the route so far, counted from the access site.
    var junctionsPassed: Int { route.legs.count - 1 }

    var routeDescription: String {
        if let branchLeg = route.legs.firstIndex(where: { !trunkSegments.contains($0.segment) }) {
            return "Side branch \(branchLeg)"
        }
        return "Main trunk"
    }

    /// World-free direction the device's bent tip points at its current position.
    func tipBendDirection(_ kind: FlexDevice.Kind) -> SIMD3<Float> {
        let d = device(kind)
        return route.frame(at: d.insertion).direction(twist: d.twist)
    }

    // MARK: - Commands

    func push(_ kind: FlexDevice.Kind, by delta: Float) {
        var d = device(kind)
        var target = max(0, d.insertion + delta)

        // Tip passing the end of the route at a junction → choose a branch.
        while target > route.length, route.endsAtJunction {
            let bend = route.frame(at: route.length).direction(twist: d.twist)
            guard let next = chooseNextLeg(bendDirection: bend, captureAngle: d.tip.captureAngle) else { break }
            route.append(next)
        }
        target = min(target, route.endsAtJunction ? route.length : route.length - Self.endMargin)
        d.insertion = target
        set(d)
        pruneRoute()
    }

    func twist(_ kind: FlexDevice.Kind, by delta: Float) {
        var d = device(kind)
        d.twist += delta
        set(d)
    }

    /// Swaps the device's tip shape in place; insertion, twist and the
    /// committed route are kept.
    func setTip(_ shape: TipShape, for kind: FlexDevice.Kind) {
        var d = device(kind)
        d.tip = shape
        set(d)
    }

    /// Devices back at the access site; the chosen tip shapes are kept.
    func reset() {
        route = VesselRoute(graph: graph)
        var newWire = FlexDevice.guidewire.scaled(by: spec.deviceScale)
        newWire.tip = wire.tip
        var newCatheter = FlexDevice.catheter.scaled(by: spec.deviceScale)
        newCatheter.tip = catheter.tip
        wire = newWire
        catheter = newCatheter
    }

    // MARK: - Branch selection

    /// Scores each leg leaving the junction by how well the tip's bend lines
    /// up with the leg's sideways heading. A single near-straight leg is the
    /// default when no side branch is within the capture angle; when several
    /// legs run near-straight (a tight fork), the bend picks among all of them.
    /// A tip that can't steer (nil capture angle) takes the straightest leg.
    func chooseNextLeg(bendDirection: SIMD3<Float>,
                       captureAngle: Float? = CathWirePlaygroundModel.branchCaptureAngle) -> VesselRoute.Leg? {
        let tangent = route.frame(at: route.length).tangent
        let candidates = route.nextLegs().map { leg in
            let heading = route.heading(of: leg)
            let lateral = heading - tangent * simd_dot(heading, tangent)
            let sideways = simd_length(lateral)
            // Bend alignment; a leg with no sideways component (straight on,
            // or doubling straight back) scores neutral.
            let score = sideways > 1e-3 ? simd_dot(lateral / sideways, bendDirection) : 0
            return (leg: leg, alignment: simd_dot(heading, tangent), sideways: sideways, score: score)
        }
        guard let captureAngle else { return candidates.max { $0.alignment < $1.alignment }?.leg }
        // Within ~20° of straight ahead (not a hairpin back).
        let straight = candidates.filter { $0.sideways < 0.35 && $0.alignment > 0 }
        guard straight.count == 1 else { return candidates.max { $0.score < $1.score }?.leg }

        let side = candidates.filter { $0.leg != straight[0].leg }.max { $0.score < $1.score }
        if let side, side.score >= cos(captureAngle) { return side.leg }
        return straight[0].leg
    }

    // MARK: - Private

    private func set(_ d: FlexDevice) {
        if d.kind == .wire { wire = d } else { catheter = d }
    }

    /// Drop legs the leading tip has pulled back out of.
    private func pruneRoute() {
        while route.legs.count > 1, let start = route.legStarts.last, leadingInsertion <= start + 0.0001 {
            route.removeLastLeg()
        }
    }

    /// Segment ids on the longest path from the access site (the main trunk).
    private static func longestPath(in graph: VesselGraph) -> Set<Int> {
        func walk(_ node: Int, from segment: Int?) -> (length: Float, segments: [Int]) {
            var best: (Float, [Int]) = (0, [])
            for seg in graph.segments(at: node) where seg.id != segment {
                let sub = walk(seg.otherEnd(from: node), from: seg.id)
                if sub.length + seg.length > best.0 { best = (sub.length + seg.length, [seg.id] + sub.segments) }
            }
            return best
        }
        return Set(walk(graph.insertionNode, from: nil).segments)
    }
}
