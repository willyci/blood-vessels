//
//  VesselRouteStatusView.swift
//  Mar2025VascCath
//
//  Live readouts: each device's insertion and twist, where the route goes,
//  and what the leading tip is doing (at a junction, at a dead end).
//

import SwiftUI

struct VesselRouteStatusView: View {
    let model: CathWirePlaygroundModel

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 24) {
                ForEach(FlexDevice.Kind.allCases) { kind in
                    deviceReadout(model.device(kind), isLeader: model.leader == kind)
                }
            }
            HStack(spacing: 8) {
                Image(systemName: "point.topleft.down.to.point.bottomright.curvepath")
                Text(model.routeDescription)
                    .font(.headline)
                Text("· \(model.junctionsPassed) junction\(model.junctionsPassed == 1 ? "" : "s") passed")
                    .foregroundStyle(.secondary)
            }
            tipStatus
        }
    }

    private func deviceReadout(_ device: FlexDevice, isLeader: Bool) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 6) {
                Text(device.kind.label).font(.headline)
                if isLeader {
                    Text("leading")
                        .font(.caption.weight(.semibold))
                        .padding(.horizontal, 8)
                        .padding(.vertical, 3)
                        .background(Color.orange.opacity(0.2))
                        .foregroundStyle(.orange)
                        .clipShape(Capsule())
                }
            }
            Text(String(format: "%.1f cm · %d°", device.insertion * 100, device.twistDegrees))
                .font(.system(.title2, design: .rounded))
                .monospacedDigit()
                .fontWeight(.semibold)
        }
    }

    @ViewBuilder
    private var tipStatus: some View {
        if model.tipAtJunction {
            Label("At a junction — twist to aim the tip, then push", systemImage: "arrow.triangle.branch")
                .foregroundStyle(.yellow)
        } else if model.tipAtDeadEnd {
            Label("End of vessel — pull back to choose another branch", systemImage: "stop.circle")
                .foregroundStyle(.orange)
        } else {
            Label("Advancing", systemImage: "arrow.forward.circle")
                .foregroundStyle(.secondary)
        }
    }
}
