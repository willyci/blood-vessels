//
//  VesselPlaygroundTests.swift
//  Mar2025VascCathTests
//
//  Synthetic Y junction (mm): access end (0,0,0) → junction (0,20,0) →
//  trunk end (0,40,0), plus a side branch from the junction to (10,30,0).
//  The route's twist-zero direction at the access site is +z, so twist 90°
//  bends the tip toward +x — into the side branch.
//
//  Tight fork: the same access leg, then two legs both ~8° off straight
//  ahead, to (3,40,0) and (-3,40,0).
//

import XCTest
import simd
@testable import Mar2025VascCath

@MainActor
final class VesselPlaygroundTests: XCTestCase {
    var sut: CathWirePlaygroundModel!

    override func setUp() {
        super.setUp()
        sut = CathWirePlaygroundModel(graph: try! VesselGraph(json: Self.yJunctionJSON))
    }

    override func tearDown() {
        sut = nil
        super.tearDown()
    }

    // MARK: - Graph / route

    func test_graphLoad_millimetres_convertsToMetres() {
        XCTAssertEqual(sut.graph.nodes[1]!.position.y, 0.020, accuracy: 1e-6)
    }

    func test_route_initial_startsAtAccessSite() {
        XCTAssertEqual(simd_distance(sut.route.frame(at: 0).position, .zero), 0, accuracy: 1e-6)
    }

    func test_routeFrame_beforeAccessSite_extendsStraightOut() {
        XCTAssertEqual(sut.route.frame(at: -0.01).position.y, -0.01, accuracy: 1e-5)
    }

    // MARK: - Branch selection

    func test_push_tipTwistedTowardBranch_entersSideBranch() {
        sut.twist(.wire, by: .pi / 2)
        advance(.wire, mm: 30)
        XCTAssertEqual(sut.routeDescription, "Side branch 1")
    }

    func test_push_tipTwistedAwayFromBranch_staysOnTrunk() {
        advance(.wire, mm: 30)
        XCTAssertEqual(sut.routeDescription, "Main trunk")
    }

    func test_push_straightTipTwistedTowardBranch_staysOnTrunk() {
        sut.setTip(.straight, for: .wire)
        sut.twist(.wire, by: .pi / 2)
        advance(.wire, mm: 30)
        XCTAssertEqual(sut.routeDescription, "Main trunk")
    }

    func test_push_tipAtJunction_reportsJunction() {
        advance(.wire, mm: 20)
        XCTAssertTrue(sut.tipAtJunction)
    }

    func test_catheter_advancedOverWire_followsWireRoute() {
        sut.twist(.wire, by: .pi / 2)
        advance(.wire, mm: 30)
        advance(.catheter, mm: 25)
        XCTAssertEqual(sut.routeDescription, "Side branch 1")
    }

    func test_pullBack_behindJunction_uncommitsBranch() {
        sut.twist(.wire, by: .pi / 2)
        advance(.wire, mm: 30)
        advance(.wire, mm: -15)
        XCTAssertEqual(sut.route.legs.count, 1)
    }

    func test_push_pastDeadEnd_clampsShortOfEnd() {
        advance(.wire, mm: 60)
        XCTAssertEqual(sut.wire.insertion, sut.route.length - CathWirePlaygroundModel.endMargin, accuracy: 1e-5)
    }

    func test_push_pastDeadEnd_reportsDeadEnd() {
        advance(.wire, mm: 60)
        XCTAssertTrue(sut.tipAtDeadEnd)
    }

    func test_push_negativeFromStart_staysAtZero() {
        sut.push(.catheter, by: -0.01)
        XCTAssertEqual(sut.catheter.insertion, 0)
    }

    func test_reset_afterBranching_returnsToTrunkStart() {
        sut.twist(.wire, by: .pi / 2)
        advance(.wire, mm: 30)
        sut.reset()
        XCTAssertEqual(sut.route.legs.count, 1)
    }

    func test_push_tightFork_bendTowardPlusX_takesPlusXLeg() {
        let fork = CathWirePlaygroundModel(graph: try! VesselGraph(json: Self.tightForkJSON))
        fork.twist(.wire, by: .pi / 2)
        for _ in 0..<30 { fork.push(.wire, by: 0.001) }
        XCTAssertEqual(fork.route.legs.last?.segment, 1)
    }

    func test_push_tightFork_bendTowardMinusX_takesMinusXLeg() {
        let fork = CathWirePlaygroundModel(graph: try! VesselGraph(json: Self.tightForkJSON))
        fork.twist(.wire, by: -.pi / 2)
        for _ in 0..<30 { fork.push(.wire, by: 0.001) }
        XCTAssertEqual(fork.route.legs.last?.segment, 2)
    }

    // MARK: - Vessel models

    func test_scaled_halfScale_halvesRadius() {
        XCTAssertEqual(FlexDevice.guidewire.scaled(by: 0.5).radius, FlexDevice.guidewire.radius / 2, accuracy: 1e-9)
    }

    func test_scaled_halfScale_keepsTipShape() {
        XCTAssertEqual(FlexDevice.catheter.scaled(by: 0.5).tip, FlexDevice.catheter.tip)
    }

    func test_scaled_halfScale_halvesTipScale() {
        XCTAssertEqual(FlexDevice.catheter.scaled(by: 0.5).tipScale, 0.5, accuracy: 1e-9)
    }

    // MARK: - Tip shapes

    func test_tipOptions_wire_areStraightJAndAngled() {
        XCTAssertEqual(TipShape.options(for: .wire), [.straight, .jTip, .angled])
    }

    func test_tipOptions_catheter_areBernCobraAndMikaelsson() {
        XCTAssertEqual(TipShape.options(for: .catheter), [.bern, .cobra, .mikaelsson])
    }

    func test_curve_jTip_endsBackBesideShaft() {
        let end = TipShape.jTip.curve(scale: 1, step: 0.0002).last!
        XCTAssertLessThanOrEqual(simd_distance(end, [0, 0.006]), 0.0001)   // 3 mm radius half-circle
    }

    func test_curve_straight_isSinglePoint() {
        XCTAssertEqual(TipShape.straight.curve(scale: 1, step: 0.001).count, 1)
    }

    func test_setTip_afterPush_keepsInsertion() {
        advance(.catheter, mm: 10)
        sut.setTip(.cobra, for: .catheter)
        XCTAssertEqual(sut.catheter.insertion, 0.010, accuracy: 1e-5)
    }

    func test_reset_afterSetTip_keepsTip() {
        sut.setTip(.jTip, for: .wire)
        sut.reset()
        XCTAssertEqual(sut.wire.tip, .jTip)
    }

    func test_load_afterSetTip_keepsTip() {
        sut.setTip(.mikaelsson, for: .catheter)
        sut.load(.avm, graph: sut.graph)
        XCTAssertEqual(sut.catheter.tip, .mikaelsson)
    }

    func test_load_avmSpec_scalesDevices() {
        sut.load(.avm, graph: sut.graph)
        XCTAssertEqual(sut.catheter.radius, FlexDevice.catheter.radius * VesselModelSpec.avm.deviceScale, accuracy: 1e-9)
    }

    func test_load_afterBranching_resetsRoute() {
        sut.twist(.wire, by: .pi / 2)
        advance(.wire, mm: 30)
        sut.load(.artery, graph: sut.graph)
        XCTAssertEqual(sut.route.legs.count, 1)
    }

    func test_catalog_everySpec_hasUniqueCenterlines() {
        XCTAssertEqual(Set(VesselModelSpec.all.map(\.centerlines)).count, VesselModelSpec.all.count)
    }

    func test_flatOrientation_yJunction_planeTurnsHorizontal() {
        let q = VesselLayout.flatOrientation(for: sut.graph)
        let heights = sut.graph.segments.values.flatMap(\.points).map { q.act($0).y }
        XCTAssertEqual(heights.max()! - heights.min()!, 0, accuracy: 1e-5)
    }

    func test_flatOrientation_yJunction_accessOnRight() {
        let q = VesselLayout.flatOrientation(for: sut.graph)
        let xs = sut.graph.segments.values.flatMap(\.points).map { q.act($0).x }
        XCTAssertEqual(q.act(sut.graph.nodes[0]!.position).x, xs.max()!, accuracy: 1e-5)
    }

    func test_stlTriangles_oneFacet_returnsThreeCorners() throws {
        var data = Data(count: 80)
        withUnsafeBytes(of: UInt32(1).littleEndian) { data.append(contentsOf: $0) }
        let floats: [Float] = [0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0]   // normal + 3 corners
        floats.forEach { f in withUnsafeBytes(of: f) { data.append(contentsOf: $0) } }
        data.append(contentsOf: [0, 0])
        XCTAssertEqual(try VesselMeshLoader.stlTriangles(data), [[0, 0, 0], [1, 0, 0], [0, 1, 0]])
    }

    // MARK: - Geometry

    func test_tubeGeometry_threeRingsFourSides_vertexCount() {
        let tube = makeTube()
        XCTAssertEqual(tube.positions.count, 3 * 5 + 1)   // rings with seam + cap center
    }

    func test_tubeGeometry_threeRingsFourSides_indexCount() {
        let tube = makeTube()
        XCTAssertEqual(tube.indices.count, 2 * 4 * 6 + 4 * 3)   // body quads + cap fan
    }

    func test_deviceShape_everyTip_staysInsideLumen() {
        advance(.wire, mm: 15)
        for shape in TipShape.allCases {
            sut.setTip(shape, for: .wire)
            let farthest = DeviceShape.samples(for: sut.wire, on: sut.route).suffix(80).map { sample in
                let s = sut.route.frame(at: sut.wire.insertion)
                return simd_length(simd_cross(sample.position - s.position, s.tangent))
            }.max()!
            XCTAssertLessThanOrEqual(farthest, 0.003 - sut.wire.radius + 1e-5)
        }
    }

    func test_deviceShape_bentTip_staysInsideLumen() {
        sut.twist(.wire, by: .pi / 2)
        advance(.wire, mm: 10)
        let tip = DeviceShape.samples(for: sut.wire, on: sut.route).last!.position
        let axis = sut.route.frame(at: sut.wire.insertion).position
        XCTAssertLessThanOrEqual(simd_distance(tip, axis), 0.003)
    }

    // MARK: - Helpers

    private func advance(_ kind: FlexDevice.Kind, mm: Int) {
        let step: Float = mm < 0 ? -0.001 : 0.001
        for _ in 0..<abs(mm) { sut.push(kind, by: step) }
    }

    private func makeTube() -> TubeGeometry {
        TubeGeometry(centers: [[0, 0, 0], [0, 1, 0], [0, 2, 0]],
                     twistNormals: Array(repeating: [1, 0, 0], count: 3), radius: 0.1, sides: 4)
    }

    private static func line(_ a: SIMD3<Double>, _ b: SIMD3<Double>) -> String {
        let n = Int(simd_distance(a, b).rounded())
        return (0...n).map { i -> String in
            let p = a + (b - a) * Double(i) / Double(n)
            return "[\(p.x),\(p.y),\(p.z)]"
        }.joined(separator: ",")
    }

    private static func radii(_ count: Int) -> String { Array(repeating: "3", count: count).joined(separator: ",") }

    private static let tightForkJSON: Data = {
        let json = """
        {"units":"mm","insertionNode":0,
         "nodes":[{"id":0,"kind":"end","pos":[0,0,0]},{"id":1,"kind":"junction","pos":[0,20,0]},
                  {"id":2,"kind":"end","pos":[3,40,0]},{"id":3,"kind":"end","pos":[-3,40,0]}],
         "segments":[
           {"id":0,"from":0,"to":1,"points":[\(line([0, 0, 0], [0, 20, 0]))],"radii":[\(radii(21))]},
           {"id":1,"from":1,"to":2,"points":[\(line([0, 20, 0], [3, 40, 0]))],"radii":[\(radii(21))]},
           {"id":2,"from":1,"to":3,"points":[\(line([0, 20, 0], [-3, 40, 0]))],"radii":[\(radii(21))]}]}
        """
        return Data(json.utf8)
    }()

    private static let yJunctionJSON: Data = {
        let json = """
        {"units":"mm","insertionNode":0,
         "nodes":[{"id":0,"kind":"end","pos":[0,0,0]},{"id":1,"kind":"junction","pos":[0,20,0]},
                  {"id":2,"kind":"end","pos":[0,40,0]},{"id":3,"kind":"end","pos":[10,30,0]}],
         "segments":[
           {"id":0,"from":0,"to":1,"points":[\(line([0, 0, 0], [0, 20, 0]))],"radii":[\(radii(21))]},
           {"id":1,"from":1,"to":2,"points":[\(line([0, 20, 0], [0, 40, 0]))],"radii":[\(radii(21))]},
           {"id":2,"from":1,"to":3,"points":[\(line([0, 20, 0], [10, 30, 0]))],"radii":[\(radii(15))]}]}
        """
        return Data(json.utf8)
    }()
}
