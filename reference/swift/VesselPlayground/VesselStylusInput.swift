//
//  VesselStylusInput.swift
//  Mar2025VascCath
//
//  Muse stylus input for the vessel playground. Uses the shared
//  GameControllerManager for tracking and its button toggles (primary =
//  catheter, secondary = wire), but reads the raw stylus pose and drives the
//  playground model instead of the main sim's AppModel distances.
//

import RealityKit
import SwiftUI
import simd

@available(visionOS 26.0, *)
@MainActor
final class VesselStylusInput {
    private let model: CathWirePlaygroundModel
    private let session: VesselPlaygroundSession
    private var lastPosition: SIMD3<Float>?
    private var lastTwist: Float?

    /// Device travel per metre of stylus travel along x.
    static let pushScale: Float = 0.5
    static let twistScale: Float = 1.0

    init(model: CathWirePlaygroundModel, session: VesselPlaygroundSession) {
        self.model = model
        self.session = session
    }

    /// Hooks the stylus up to an unrotated root at the scene origin, so the
    /// manager reports positions in world axes.
    func attach(to content: RealityViewContent, appModel: AppModel) async {
        let manager = GameControllerManager.shared
        let stylusRoot = Entity()
        content.add(stylusRoot)
        manager.setSceneRoot(stylusRoot)
        manager.drivesAppModel = false
        await manager.handleGameControllerSetup(hapticsModel: .init(), appModel: appModel)
        _ = content.subscribe(to: SceneEvents.Update.self) { [weak self] (_: SceneEvents.Update) in
            GameControllerManager.shared.updateStylusTransform()
            self?.update()
        }
    }

    /// Hands the stylus back to the main simulation.
    static func detach() {
        GameControllerManager.shared.drivesAppModel = true
    }

    private func update() {
        let manager = GameControllerManager.shared
        guard session.stylusInputEnabled, manager.isConnected else {
            lastPosition = nil; lastTwist = nil
            return
        }
        let position = manager.stylusPosition
        let rotation = simd_float3x3(manager.stylusRotation)
        let twist = atan2(rotation.columns.1.z, rotation.columns.1.y)
        defer { lastPosition = position; lastTwist = twist }
        guard let lastPosition, let lastTwist else { return }

        let push = simd_dot(position - lastPosition, VesselPlayground.insertionAxis) * Self.pushScale
        var roll = twist - lastTwist
        if roll > .pi { roll -= 2 * .pi }
        if roll < -.pi { roll += 2 * .pi }

        let active: [FlexDevice.Kind] = [manager.isCatheterControlActive ? .catheter : nil,
                                         manager.isWireControlActive ? .wire : nil].compactMap { $0 }
        for kind in active {
            if push != 0 { model.push(kind, by: push) }
            if roll != 0 { model.twist(kind, by: roll * Self.twistScale) }
        }
    }
}
