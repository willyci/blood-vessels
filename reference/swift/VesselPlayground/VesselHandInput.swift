//
//  VesselHandInput.swift
//  Mar2025VascCath
//
//  ARKit hand tracking for the vessel playground. While a hand pinches,
//  its movement along the device axis pushes/pulls and its wrist roll about
//  that axis twists: left hand = catheter, right hand = wire (as in the main
//  sim). Releasing the pinch lets the user ratchet for long advances.
//

import ARKit
import simd

@MainActor
struct VesselHandInput {
    let model: CathWirePlaygroundModel

    /// Thumb–index gap that counts as a pinch (m).
    static let pinchThreshold: Float = 0.025
    /// Device travel per metre of hand travel.
    static let pushScale: Float = 0.5
    static let twistScale: Float = 1.0
    static var insertionAxis: SIMD3<Float> { VesselPlayground.insertionAxis }

    private struct Grip {
        var pinch: SIMD3<Float>
        var wrist: simd_quatf
    }

    func run() async {
        guard HandTrackingProvider.isSupported else { return }
        let session = ARKitSession()
        let provider = HandTrackingProvider()
        do { try await session.run([provider]) } catch { return }
        defer { session.stop() }

        var grips: [Bool: Grip] = [:]   // keyed by isLeft
        for await update in provider.anchorUpdates {
            if Task.isCancelled { break }
            let anchor = update.anchor
            let isLeft = anchor.chirality == .left
            guard anchor.isTracked, let grip = Self.grip(of: anchor) else {
                grips[isLeft] = nil
                continue
            }
            if let last = grips[isLeft] {
                let kind: FlexDevice.Kind = isLeft ? .catheter : .wire
                let push = simd_dot(grip.pinch - last.pinch, Self.insertionAxis) * Self.pushScale
                let roll = Self.twistAngle(of: grip.wrist * last.wrist.inverse, around: Self.insertionAxis)
                if push != 0 { model.push(kind, by: push) }
                if roll != 0 { model.twist(kind, by: roll * Self.twistScale) }
            }
            grips[isLeft] = grip
        }
    }

    /// Pinch point + wrist orientation in world space, or nil when not pinching.
    private static func grip(of anchor: HandAnchor) -> Grip? {
        guard let skeleton = anchor.handSkeleton else { return nil }
        let origin = anchor.originFromAnchorTransform
        func world(_ joint: HandSkeleton.JointName) -> simd_float4x4 {
            origin * skeleton.joint(joint).anchorFromJointTransform
        }
        let thumb = world(.thumbTip).columns.3, index = world(.indexFingerTip).columns.3
        let thumbTip = SIMD3(thumb.x, thumb.y, thumb.z), indexTip = SIMD3(index.x, index.y, index.z)
        guard simd_distance(thumbTip, indexTip) < pinchThreshold else { return nil }
        return Grip(pinch: (thumbTip + indexTip) / 2, wrist: simd_quatf(world(.wrist)))
    }

    /// Signed rotation of `q` about `axis` — the "twist" half of swing–twist.
    static func twistAngle(of q: simd_quatf, around axis: SIMD3<Float>) -> Float {
        let proj = simd_dot(q.imag, axis)
        guard abs(proj) > 1e-9 || abs(q.real) > 1e-9 else { return 0 }
        var angle = 2 * atan2(proj, q.real)
        if angle > .pi { angle -= 2 * .pi }
        if angle < -.pi { angle += 2 * .pi }
        return angle
    }
}
