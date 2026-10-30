//
//  VesselPlaygroundControlsView.swift
//  Mar2025VascCath
//
//  Control window for the vessel playground. Opening it opens the immersive
//  space; closing it takes the space down and brings the main menu back.
//

import SwiftUI

struct VesselPlaygroundControlsView: View {
    @Environment(\.openImmersiveSpace) private var openImmersiveSpace
    @Environment(\.dismissImmersiveSpace) private var dismissImmersiveSpace
    @Environment(\.openWindow) private var openWindow

    private let session = VesselPlayground.session

    var body: some View {
        Group {
            if let model = VesselPlayground.model {
                content(model)
            } else {
                ContentUnavailableView("Vessel data missing",
                                       systemImage: "exclamationmark.triangle",
                                       description: Text("VesselCenterlines.json could not be loaded."))
            }
        }
        .padding(24)
        .task { await openSpace() }
        .onDisappear {
            if session.spaceState == .open {
                Task { await dismissImmersiveSpace() }
            }
            openWindow(id: SimulationWindows.main)
        }
    }

    private func content(_ model: CathWirePlaygroundModel) -> some View {
        VStack(alignment: .leading, spacing: 24) {
            header
            VesselModelPicker(model: model)
            DeviceControlPanel(model: model)
            TipShapePicker(model: model)
            VesselRouteStatusView(model: model)
            VesselPlaygroundOptionsView(model: model, session: session)
        }
    }

    private var header: some View {
        HStack {
            Text("Vessel Playground")
                .font(.title)
                .bold()
            Spacer()
            Button {
                Task {
                    if session.spaceState == .open { await dismissImmersiveSpace() } else { await openSpace() }
                }
            } label: {
                Label(session.spaceState == .open ? "Hide Vessel" : "Show Vessel",
                      systemImage: session.spaceState == .open ? "eye.slash" : "eye")
            }
            .disabled(session.spaceState == .inTransition)
        }
    }

    private func openSpace() async {
        guard session.spaceState == .closed, VesselPlayground.model != nil else { return }
        session.spaceState = .inTransition
        if await openImmersiveSpace(id: VesselPlayground.immersiveSpaceID) != .opened {
            session.spaceState = .closed
        }
    }
}
