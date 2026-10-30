//
//  VesselPlaygroundLaunchButton.swift
//  Mar2025VascCath
//
//  Scenario-banner button that opens the vessel playground window (which
//  opens its immersive space).
//

import SwiftUI

struct VesselPlaygroundLaunchButton: View {
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        Button {
            openWindow(id: VesselPlayground.windowID)
        } label: {
            HStack(spacing: 8) {
                Image(systemName: "point.topleft.down.to.point.bottomright.curvepath")
                    .font(.body)
                Text("Vessel Playground")
                    .font(.headline)
                    .fontWeight(.semibold)
            }
            .foregroundStyle(.white)
            .padding(.horizontal, 28)
            .padding(.vertical, 12)
            .background(
                LinearGradient(colors: [.red, .orange], startPoint: .leading, endPoint: .trailing)
            )
            .clipShape(Capsule())
            .shadow(color: .black.opacity(0.3), radius: 8, y: 4)
        }
        .buttonStyle(.plain)
    }
}
