//
//  VesselPlaygroundOptionsView.swift
//  Mar2025VascCath
//
//  Input toggles, centerline debug overlay, and reset.
//

import SwiftUI

struct VesselPlaygroundOptionsView: View {
    @Bindable var model: CathWirePlaygroundModel
    @Bindable var session: VesselPlaygroundSession

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Toggle(isOn: $session.handInputEnabled) {
                Label("Hand tracking (pinch: left = catheter, right = wire)", systemImage: "hand.pinch")
            }
            if #available(visionOS 26.0, *) {
                Toggle(isOn: $session.stylusInputEnabled) {
                    Label("Stylus (primary = catheter, secondary = wire)", systemImage: "pencil.tip")
                }
            }
            Toggle(isOn: $model.showCenterlines) {
                Label("Show centerlines", systemImage: "point.3.connected.trianglepath.dotted")
            }
            Button(role: .destructive) {
                model.reset()
            } label: {
                Label("Reset devices", systemImage: "arrow.counterclockwise")
            }
        }
        .toggleStyle(.switch)
        .tint(.green)
    }
}
