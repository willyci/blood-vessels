//
//  VesselPlayground.swift
//  Mar2025VascCath
//
//  Shared state for the vessel playground: a flexible guidewire + catheter
//  pushed and twisted through a 3D vessel (Resources/VesselPlayground,
//  listed in VesselModelCatalog). The controls window and the immersive
//  space are separate scenes, so they share these lazily created instances
//  (same pattern as ESP32Playground).
//

import Foundation
import Observation
import simd

@MainActor
enum VesselPlayground {
    /// nil when the default model's centerline JSON is missing or invalid.
    static let model: CathWirePlaygroundModel? = {
        do {
            let spec = VesselModelSpec.tube
            return CathWirePlaygroundModel(graph: try VesselGraph.loadBundled(named: spec.centerlines), spec: spec)
        } catch {
            print("[VesselPlayground] Centerlines failed to load: \(error)")
            return nil
        }
    }()

    static let session = VesselPlaygroundSession()

    /// Scene ids registered in Mar2025VascCathApp.
    static let windowID = "VesselPlaygroundWindow"
    static let immersiveSpaceID = "VesselPlayground"

    /// Loads another vessel model into the shared playground model. Returns
    /// false (and keeps the current model) if its centerlines fail to load.
    @discardableResult
    static func select(_ id: VesselModelSpec.ID) -> Bool {
        guard let model, model.spec.id != id else { return true }
        let spec = VesselModelSpec.spec(id)
        do {
            model.load(spec, graph: try VesselGraph.loadBundled(named: spec.centerlines))
            return true
        } catch {
            print("[VesselPlayground] \(spec.title) centerlines failed to load: \(error)")
            return false
        }
    }

    /// World direction the devices advance in at the access site: the chord
    /// over the route's first 2 cm (steadier than the very first tangent on
    /// segmented meshes), turned by the model's room orientation — leftward,
    /// since the access site is on the right. Hand and stylus pushes are
    /// measured along it.
    static var insertionAxis: SIMD3<Float> {
        guard let model else { return [-1, 0, 0] }
        let route = model.route
        let chord = route.frame(at: min(0.02, route.length)).position - route.frame(at: 0).position
        guard simd_length(chord) > 1e-6 else { return [-1, 0, 0] }
        return simd_normalize(model.orientation.act(chord))
    }
}

/// Scene + input switches for the playground (not simulation state).
@MainActor
@Observable
final class VesselPlaygroundSession {
    /// Guards against double-opening the immersive space.
    var spaceState: AppModel.ImmersiveSpaceState = .closed
    /// Pinch-drag / wrist-roll drives the devices (left = catheter, right = wire).
    var handInputEnabled = true
    /// Muse stylus drives the devices (visionOS 26+).
    var stylusInputEnabled = false
}
