//
//  extract_centerlines.swift
//  One-time tool: vessel mesh (OBJ or binary STL, mm) → lumen centerline
//  graph JSON.
//
//  Build & run (optimized — the interpreter is too slow for the voxel passes):
//    swiftc -O scripts/extract_centerlines.swift -o /tmp/extract_centerlines
//    /tmp/extract_centerlines [--solid] [--voxel=0.5] [--access-near=x,y,z] <in.obj|in.stl>... <out.json>
//  (The older form `<in> <out.json> [voxel_mm]` still works.)
//
//  Two kinds of mesh:
//    - default: a hollow pipe (closed wall shells, e.g. Tinkercad's tube.obj);
//      the lumen is the empty space the wall encloses.
//    - --solid: each closed shell IS the blood-pool surface (segmentation
//      exports); the lumen is simply the inside. Several input files are
//      merged into one model (e.g. artery.stl + vein.stl).
//
//  Pipeline (VMTK-style minimal paths, no skeleton thinning):
//    1. Parse + weld the mesh(es), split into closed shells.
//    2. Voxelize by z-ray parity per shell, OR'd together (survives overlaps
//       and the non-manifold seams Tinkercad unions leave).
//    3. Lumen. Hollow: empty voxels near the wall that are enclosed by it —
//       rays in most of 26 directions hit wall within `maxRayMM` (the pipe's
//       open ends let only the near-axial rays escape). Solid: the voxelized
//       interior. Keep the largest component.
//    4. Exact Euclidean distance transform of the lumen → local radius.
//    5. Grow a tree of center-hugging paths: repeatedly take the voxel
//       geodesically farthest from the tree; if it is far enough to be a real
//       vessel end, connect it with a 1/r²-weighted Dijkstra path. The voxel
//       where the path meets the tree becomes a junction.
//    6. Trim end caps, smooth, resample every 1 mm, write JSON.
//
//  The JSON is hand-editable (e.g. `insertionNode`, a misplaced junction).
//

import Foundation
import simd

// MARK: - Args

let args = CommandLine.arguments
let flags = args.dropFirst().filter { $0.hasPrefix("--") }
var positional = args.dropFirst().filter { !$0.hasPrefix("--") }
let solid = flags.contains("--solid")
var voxel = flags.first { $0.hasPrefix("--voxel=") }.flatMap { Double($0.dropFirst(8)) } ?? 0.5
if positional.count == 3, let v = Double(positional[2]) { voxel = v; positional.removeLast() }
guard positional.count >= 2, positional.last!.hasSuffix(".json") else {
    print("usage: extract_centerlines [--solid] [--voxel=0.5] [--access-near=x,y,z] <in.obj|in.stl>... <out.json>")
    exit(1)
}
let inputs = Array(positional.dropLast())
let outputPath = positional.last!

// MARK: - Mesh input (OBJ text, binary STL)

var rawVerts: [SIMD3<Double>] = []
var tris: [SIMD3<Int>] = []
for path in inputs {
    let base = rawVerts.count
    if path.lowercased().hasSuffix(".stl") {
        let data = try Data(contentsOf: URL(fileURLWithPath: path))
        let count = Int(data.subdata(in: 80..<84).withUnsafeBytes { $0.loadUnaligned(as: UInt32.self) })
        data.withUnsafeBytes { b in
            for t in 0..<count {
                let o = 84 + t * 50 + 12          // skip the facet normal
                for k in 0..<3 {
                    let q = o + k * 12
                    rawVerts.append(SIMD3(Double(b.loadUnaligned(fromByteOffset: q, as: Float.self)),
                                          Double(b.loadUnaligned(fromByteOffset: q + 4, as: Float.self)),
                                          Double(b.loadUnaligned(fromByteOffset: q + 8, as: Float.self))))
                }
                let i = base + t * 3
                tris.append(SIMD3(i, i + 1, i + 2))
            }
        }
        continue
    }
    let text = try String(contentsOfFile: path, encoding: .utf8)
    for line in text.split(whereSeparator: \.isNewline) {
        let p = line.split(whereSeparator: { $0 == " " || $0 == "\t" })
        guard let head = p.first else { continue }
        if head == "v", p.count >= 4,
           let x = Double(p[1]), let y = Double(p[2]), let z = Double(p[3]) {
            rawVerts.append(SIMD3(x, y, z))
        } else if head == "f", p.count >= 4 {
            let idx = p[1...].compactMap { Int($0.split(separator: "/")[0]) }.map { base + $0 - 1 }
            guard idx.count >= 3 else { continue }
            for i in 1..<(idx.count - 1) { tris.append(SIMD3(idx[0], idx[i], idx[i + 1])) }
        }
    }
}

// Weld by rounded position so shells connect across duplicated vertices.
var weldKey: [SIMD3<Int>: Int] = [:]
var weld = [Int](repeating: 0, count: rawVerts.count)
for (i, v) in rawVerts.enumerated() {
    let k = SIMD3(Int((v.x * 1000).rounded()), Int((v.y * 1000).rounded()), Int((v.z * 1000).rounded()))
    if let e = weldKey[k] { weld[i] = e } else { weldKey[k] = weldKey.count; weld[i] = weldKey.count - 1 }
}
var parent = Array(0..<weldKey.count)
func root(_ x: Int) -> Int {
    var x = x
    while parent[x] != x { parent[x] = parent[parent[x]]; x = parent[x] }
    return x
}
for t in tris {
    let a = root(weld[t.x]), b = root(weld[t.y]), c = root(weld[t.z])
    parent[b] = a; parent[root(c)] = root(a)
}
var shellOf: [Int: Int] = [:]
let triShell: [Int] = tris.map { t in
    let r = root(weld[t.x])
    if let s = shellOf[r] { return s }
    shellOf[r] = shellOf.count
    return shellOf.count - 1
}
let shellCount = shellOf.count
print("parsed \(rawVerts.count) verts, \(tris.count) tris, \(shellCount) shells")

// MARK: - Voxel grid

var lo = rawVerts[0], hi = rawVerts[0]
for v in rawVerts { lo = pointwiseMin(lo, v); hi = pointwiseMax(hi, v) }
lo -= SIMD3(repeating: 2 * voxel); hi += SIMD3(repeating: 2 * voxel)
let nx = Int(((hi.x - lo.x) / voxel).rounded(.up))
let ny = Int(((hi.y - lo.y) / voxel).rounded(.up))
let nz = Int(((hi.z - lo.z) / voxel).rounded(.up))
let nxy = nx * ny
@inline(__always) func vid(_ x: Int, _ y: Int, _ z: Int) -> Int { x + nx * (y + ny * z) }
@inline(__always) func center(_ i: Int) -> SIMD3<Double> {
    let x = i % nx, y = (i / nx) % ny, z = i / nxy
    return lo + SIMD3(Double(x) + 0.5, Double(y) + 0.5, Double(z) + 0.5) * voxel
}
print("grid \(nx)×\(ny)×\(nz) @ \(voxel) mm")

// Ray parity along +z for every (x, y) column, per shell. A tiny jitter keeps
// the rays off shared triangle edges.
let jx = 0.000_173 * voxel, jy = 0.000_291 * voxel
var hits = [[Double]](repeating: [], count: nxy * shellCount)
for (ti, t) in tris.enumerated() {
    let a = rawVerts[t.x], b = rawVerts[t.y], c = rawVerts[t.z]
    let d = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y)
    if abs(d) < 1e-12 { continue }   // parallel to the rays
    let minX = min(a.x, b.x, c.x), maxX = max(a.x, b.x, c.x)
    let minY = min(a.y, b.y, c.y), maxY = max(a.y, b.y, c.y)
    let ix0 = max(0, Int(((minX - lo.x) / voxel - 0.5).rounded(.up)))
    let ix1 = min(nx - 1, Int(((maxX - lo.x) / voxel - 0.5).rounded(.down)))
    let iy0 = max(0, Int(((minY - lo.y) / voxel - 0.5).rounded(.up)))
    let iy1 = min(ny - 1, Int(((maxY - lo.y) / voxel - 0.5).rounded(.down)))
    if ix0 > ix1 || iy0 > iy1 { continue }
    for iy in iy0...iy1 {
        let py = lo.y + (Double(iy) + 0.5) * voxel + jy
        for ix in ix0...ix1 {
            let px = lo.x + (Double(ix) + 0.5) * voxel + jx
            let u = ((b.x - px) * (c.y - py) - (c.x - px) * (b.y - py)) / d
            let v = ((c.x - px) * (a.y - py) - (a.x - px) * (c.y - py)) / d
            let w = 1 - u - v
            if u < 0 || v < 0 || w < 0 { continue }
            hits[(ix + nx * iy) * shellCount + triShell[ti]].append(u * a.z + v * b.z + w * c.z)
        }
    }
}
var wall = [Bool](repeating: false, count: nxy * nz)
var oddColumns = 0
for col in 0..<nxy {
    for s in 0..<shellCount {
        var h = hits[col * shellCount + s]
        if h.isEmpty { continue }
        h.sort()
        if h.count % 2 == 1 { oddColumns += 1 }
        var k = 0
        while k + 1 < h.count {
            let z0 = Int(((h[k] - lo.z) / voxel - 0.5).rounded(.up))
            let z1 = Int(((h[k + 1] - lo.z) / voxel - 0.5).rounded(.down))
            if z0 <= z1 { for z in max(0, z0)...min(nz - 1, z1) { wall[col + nxy * z] = true } }
            k += 2
        }
    }
}
print("wall voxels \(wall.reduce(0) { $0 + ($1 ? 1 : 0) }) (\(oddColumns) odd-parity columns ignored)")

// MARK: - Exact EDT (Felzenszwalb–Huttenlocher, separable)

let inf = 1e20
func edt1D(_ f: [Double]) -> [Double] {
    let n = f.count
    var d = [Double](repeating: 0, count: n), v = [Int](repeating: 0, count: n)
    var z = [Double](repeating: 0, count: n + 1)
    var k = 0; z[0] = -inf; z[1] = inf
    for q in 1..<n {
        var s: Double
        repeat {
            let p = v[k]
            s = ((f[q] + Double(q * q)) - (f[p] + Double(p * p))) / Double(2 * q - 2 * p)
            if s <= z[k] { k -= 1 } else { break }
        } while k >= 0
        k += 1; v[k] = q; z[k] = s; z[k + 1] = inf
    }
    k = 0
    for q in 0..<n {
        while z[k + 1] < Double(q) { k += 1 }
        let p = v[k]; d[q] = Double((q - p) * (q - p)) + f[p]
    }
    return d
}
/// Squared distance (voxels²) from every voxel to the nearest `false` voxel.
func edt3D(_ mask: [Bool]) -> [Double] {
    var dist = mask.map { $0 ? inf : 0 }
    for z in 0..<nz { for y in 0..<ny {
        let base = vid(0, y, z)
        let out = edt1D(Array(dist[base..<(base + nx)]))
        for x in 0..<nx { dist[base + x] = out[x] }
    } }
    for z in 0..<nz { for x in 0..<nx {
        let out = edt1D((0..<ny).map { dist[vid(x, $0, z)] })
        for y in 0..<ny { dist[vid(x, y, z)] = out[y] }
    } }
    for y in 0..<ny { for x in 0..<nx {
        let out = edt1D((0..<nz).map { dist[vid(x, y, $0)] })
        for z in 0..<nz { dist[vid(x, y, z)] = out[z] }
    } }
    return dist
}

// MARK: - Lumen: empty space enclosed by the wall

let maxRayMM = 15.0
let maxSteps = Int(maxRayMM / voxel)
let minHits = 21                       // of 26 directions
let wallDist = solid ? [] : edt3D(wall.map { !$0 })  // distance to the wall
var dirs: [SIMD3<Int>] = []
for dz in -1...1 { for dy in -1...1 { for dx in -1...1 where dx != 0 || dy != 0 || dz != 0 {
    dirs.append(SIMD3(dx, dy, dz))
} } }
// Solid meshes: what was voxelized as "wall" is already the lumen.
var lumen = solid ? wall : [Bool](repeating: false, count: wall.count)
if !solid { for z in 1..<(nz - 1) { for y in 1..<(ny - 1) { for x in 1..<(nx - 1) {
    let i = vid(x, y, z)
    guard !wall[i], wallDist[i].squareRoot() * voxel <= 6 else { continue }
    var hitsN = 0, misses = 0
    for d in dirs {
        var p = SIMD3(x, y, z), hit = false
        for _ in 0..<maxSteps {
            p &+= d
            if p.x < 0 || p.y < 0 || p.z < 0 || p.x >= nx || p.y >= ny || p.z >= nz { break }
            if wall[vid(p.x, p.y, p.z)] { hit = true; break }
        }
        if hit { hitsN += 1 } else { misses += 1; if misses > 26 - minHits { break } }
    }
    if hitsN >= minHits { lumen[i] = true }
} } } }
// Largest 26-connected component only.
var comp = [Int32](repeating: -1, count: lumen.count)
var bestComp: Int32 = -1, bestSize = 0, compCount: Int32 = 0
for s in 0..<lumen.count where lumen[s] && comp[s] < 0 {
    var stack = [s]; comp[s] = compCount; var size = 0
    while let i = stack.popLast() {
        size += 1
        let x = i % nx, y = (i / nx) % ny, z = i / nxy
        for d in dirs {
            let a = x + d.x, b = y + d.y, c = z + d.z
            guard a >= 0, b >= 0, c >= 0, a < nx, b < ny, c < nz else { continue }
            let j = vid(a, b, c)
            if lumen[j] && comp[j] < 0 { comp[j] = compCount; stack.append(j) }
        }
    }
    if size > bestSize { bestSize = size; bestComp = compCount }
    compCount += 1
}
let inside = comp.map { $0 == bestComp && $0 >= 0 }
print("lumen voxels \(bestSize) (largest of \(compCount) components)")
let dist = edt3D(inside)

/// Lumen radius at a voxel, in voxels.
@inline(__always) func radiusVox(_ i: Int) -> Double { dist[i].squareRoot() }

// MARK: - Dijkstra over inside voxels (26-neighbourhood)

var offsets: [(Int, Double)] = []
for dz in -1...1 { for dy in -1...1 { for dx in -1...1 where dx != 0 || dy != 0 || dz != 0 {
    offsets.append((dx + nx * (dy + ny * dz), Double(dx * dx + dy * dy + dz * dz).squareRoot()))
} } }

struct Heap {
    var items: [(Double, Int)] = []
    mutating func push(_ e: (Double, Int)) {
        items.append(e); var i = items.count - 1
        while i > 0 { let p = (i - 1) / 2; if items[p].0 <= items[i].0 { break }; items.swapAt(p, i); i = p }
    }
    mutating func pop() -> (Double, Int)? {
        guard !items.isEmpty else { return nil }
        let top = items[0]; let last = items.removeLast()
        if !items.isEmpty {
            items[0] = last; var i = 0
            while true {
                let l = 2 * i + 1, r = l + 1; var m = i
                if l < items.count && items[l].0 < items[m].0 { m = l }
                if r < items.count && items[r].0 < items[m].0 { m = r }
                if m == i { break }; items.swapAt(m, i); i = m
            }
        }
        return top
    }
}

/// `centered`: cost len / r² pulls paths onto the lumen axis; otherwise plain length.
func dijkstra(from sources: [Int], centered: Bool) -> (cost: [Double], prev: [Int]) {
    var cost = [Double](repeating: inf, count: inside.count)
    var prev = [Int](repeating: -1, count: inside.count)
    var heap = Heap()
    for s in sources { cost[s] = 0; heap.push((0, s)) }
    while let (c, i) = heap.pop() {
        if c > cost[i] { continue }
        for (o, len) in offsets {
            let j = i + o
            guard j >= 0, j < inside.count, inside[j] else { continue }
            let step = centered ? len / max(dist[j], 0.25) : len
            if c + step < cost[j] { cost[j] = c + step; prev[j] = i; heap.push((c + step, j)) }
        }
    }
    return (cost, prev)
}
func farthest(_ cost: [Double]) -> (Int, Double) {
    var best = -1, bestCost = -1.0
    for i in 0..<cost.count where inside[i] && cost[i] < inf && cost[i] > bestCost { best = i; bestCost = cost[i] }
    return (best, bestCost)
}

// MARK: - Tree of minimal paths

var deepest = 0
for i in 0..<inside.count where inside[i] && dist[i] > dist[deepest] { deepest = i }
let maxRadiusMM = radiusVox(deepest) * voxel
let minBranchMM = max(8, 2.5 * maxRadiusMM)
print(String(format: "max lumen radius %.2f mm → min branch length %.1f mm", maxRadiusMM, minBranchMM))

let firstEnd = farthest(dijkstra(from: [deepest], centered: false).cost).0
var polylines: [[Int]] = []          // voxel ids
var treeVoxels: [Int] = [firstEnd]
var junctions = Set<Int>()

for _ in 0..<24 {
    let (far, lenVox) = farthest(dijkstra(from: treeVoxels, centered: false).cost)
    guard far >= 0, lenVox * voxel >= minBranchMM else { break }
    let prev = dijkstra(from: treeVoxels, centered: true).prev
    var path: [Int] = [far]
    while prev[path.last!] >= 0 { path.append(prev[path.last!]) }
    path.reverse()                     // attach point → new end
    let attach = path[0]
    if let pi = polylines.firstIndex(where: { $0.contains(attach) }) {
        let k = polylines[pi].firstIndex(of: attach)!
        if k > 0 && k < polylines[pi].count - 1 {
            let p = polylines[pi]
            polylines[pi] = Array(p[...k])
            polylines.append(Array(p[k...]))
        }
        junctions.insert(attach)
    }
    polylines.append(path)
    treeVoxels.append(contentsOf: path)
    print(String(format: "  branch to end at (%.1f, %.1f, %.1f), %.1f mm from tree",
                 center(far).x, center(far).y, center(far).z, lenVox * voxel))
}

// MARK: - Graph, cap trimming, smoothing, resampling

var nodeVoxels: [Int] = []
func nodeID(_ v: Int) -> Int {
    if let i = nodeVoxels.firstIndex(of: v) { return i }
    nodeVoxels.append(v); return nodeVoxels.count - 1
}
var degree: [Int: Int] = [:]
for p in polylines { degree[p.first!, default: 0] += 1; degree[p.last!, default: 0] += 1 }

struct OutSegment: Codable { var id: Int; var from: Int; var to: Int; var points: [[Double]]; var radii: [Double] }
struct OutNode: Codable { var id: Int; var kind: String; var pos: [Double] }
struct OutFile: Codable {
    var units = "mm"
    var source: String
    var voxelMM: Double
    var insertionNode: Int
    var nodes: [OutNode]
    var segments: [OutSegment]
}

func round3(_ v: Double) -> Double { (v * 1000).rounded() / 1000 }

var segments: [OutSegment] = []
var endPositions: [Int: SIMD3<Double>] = [:]
for (si, poly) in polylines.enumerated() {
    var pts = poly.map(center)
    var rad = poly.map { radiusVox($0) * voxel }
    // Trim the capped end: the farthest-voxel search lands on the cap rim, so
    // drop points closer to the end than ~the local radius.
    if degree[poly.last!] == 1 {
        var acc = 0.0, cut = pts.count - 1
        while cut > 1 {
            acc += simd_distance(pts[cut], pts[cut - 1])
            if acc >= rad[cut - 1] { break }
            cut -= 1
        }
        pts = Array(pts[...cut]); rad = Array(rad[...cut])
    }
    if degree[poly.first!] == 1 {
        var acc = 0.0, cut = 0
        while cut < pts.count - 2 {
            acc += simd_distance(pts[cut], pts[cut + 1])
            if acc >= rad[cut + 1] { break }
            cut += 1
        }
        pts = Array(pts[cut...]); rad = Array(rad[cut...])
    }
    // Laplacian smoothing with fixed endpoints (keeps junctions shared).
    for _ in 0..<12 where pts.count > 2 {
        var next = pts
        for i in 1..<(pts.count - 1) { next[i] = (pts[i - 1] + 2 * pts[i] + pts[i + 1]) / 4 }
        pts = next
    }
    // Resample every 1 mm.
    var outP: [SIMD3<Double>] = [pts[0]], outR: [Double] = [rad[0]]
    var carry = 0.0
    for i in 1..<pts.count {
        let a = pts[i - 1], b = pts[i], len = simd_distance(a, b)
        var t = 1.0 - carry
        while t <= len {
            let f = t / len
            outP.append(a + (b - a) * f); outR.append(rad[i - 1] + (rad[i] - rad[i - 1]) * f)
            t += 1.0
        }
        carry = len - (t - 1.0)
    }
    if simd_distance(outP.last!, pts.last!) > 0.2 { outP.append(pts.last!); outR.append(rad.last!) }

    let from = nodeID(poly.first!), to = nodeID(poly.last!)
    if degree[poly.first!] == 1 { endPositions[from] = outP.first! }
    if degree[poly.last!] == 1 { endPositions[to] = outP.last! }
    segments.append(OutSegment(id: si, from: from, to: to,
                               points: outP.map { [round3($0.x), round3($0.y), round3($0.z)] },
                               radii: outR.map(round3)))
}

var nodes: [OutNode] = nodeVoxels.enumerated().map { i, v in
    let p = endPositions[i] ?? center(v)
    return OutNode(id: i, kind: degree[v] == 1 ? "end" : "junction", pos: [round3(p.x), round3(p.y), round3(p.z)])
}

// MARK: - Clean-up: drop stub leaves, merge pass-through nodes

func segmentLength(_ s: OutSegment) -> Double {
    zip(s.points, s.points.dropFirst()).reduce(0.0) { acc, pr in
        acc + simd_distance(SIMD3(pr.0[0], pr.0[1], pr.0[2]), SIMD3(pr.1[0], pr.1[1], pr.1[2]))
    }
}
func nodeDegree(_ n: Int) -> Int { segments.filter { $0.from == n || $0.to == n }.count }

// Cap trimming can leave dead-end stubs a fraction of a millimetre long next
// to a junction; a device would "choose" one and stop at once.
let minLeafMM = 2.0
let stubs = Set(segments.filter { s in
    let leafEnd = nodeDegree(s.from) == 1 || nodeDegree(s.to) == 1
    return leafEnd && segmentLength(s) < minLeafMM && max(nodeDegree(s.from), nodeDegree(s.to)) >= 3
}.map(\.id))
segments.removeAll { stubs.contains($0.id) }
// A junction left with two segments is just a bend: join them into one.
while let mid = nodes.map(\.id).first(where: { nodeDegree($0) == 2 }) {
    let pair = segments.filter { $0.from == mid || $0.to == mid }
    func oriented(_ s: OutSegment, endingAt n: Int) -> OutSegment {
        s.to == n ? s : OutSegment(id: s.id, from: s.to, to: s.from,
                                   points: s.points.reversed(), radii: s.radii.reversed())
    }
    let a = oriented(pair[0], endingAt: mid)
    var b = oriented(pair[1], endingAt: mid)
    b = OutSegment(id: b.id, from: b.to, to: b.from, points: b.points.reversed(), radii: b.radii.reversed())
    let joined = OutSegment(id: a.id, from: a.from, to: b.to,
                            points: a.points + b.points.dropFirst(), radii: a.radii + b.radii.dropFirst())
    let (aID, bID) = (a.id, b.id)
    segments.removeAll { $0.id == aID || $0.id == bID }
    segments.append(joined)
}
// Renumber nodes and segments densely.
let usedNodes = nodes.filter { nodeDegree($0.id) > 0 }
let renumber = Dictionary(uniqueKeysWithValues: usedNodes.enumerated().map { ($1.id, $0) })
nodes = usedNodes.map { n in
    OutNode(id: renumber[n.id]!, kind: nodeDegree(n.id) == 1 ? "end" : "junction", pos: n.pos)
}
segments = segments.sorted { $0.id < $1.id }.enumerated().map { i, s in
    OutSegment(id: i, from: renumber[s.from]!, to: renumber[s.to]!, points: s.points, radii: s.radii)
}

// Insertion (access site): the end nearest `--access-near=x,y,z` (mm), else
// the highest (max y) end. Can also be edited by hand in the JSON.
let accessNear = flags.first { $0.hasPrefix("--access-near=") }.map {
    $0.dropFirst(14).split(separator: ",").compactMap { Double($0) }
}
let insertion = nodes.filter { $0.kind == "end" }.min { a, b in
    guard let p = accessNear, p.count == 3 else { return a.pos[1] > b.pos[1] }
    func d(_ n: OutNode) -> Double { zip(n.pos, p).reduce(0) { $0 + ($1.0 - $1.1) * ($1.0 - $1.1) } }
    return d(a) < d(b)
}!.id

let out = OutFile(source: inputs.map { ($0 as NSString).lastPathComponent }.joined(separator: " + "), voxelMM: voxel,
                  insertionNode: insertion, nodes: nodes, segments: segments)
let enc = JSONEncoder(); enc.outputFormatting = [.prettyPrinted, .sortedKeys]
try enc.encode(out).write(to: URL(fileURLWithPath: outputPath))

print("\nnodes:")
for n in nodes { print("  \(n.id) \(n.kind) (\(n.pos.map { String(format: "%.1f", $0) }.joined(separator: ", ")))") }
print("segments:")
for s in segments {
    let len = zip(s.points, s.points.dropFirst()).reduce(0.0) { acc, pr in
        acc + simd_distance(SIMD3(pr.0[0], pr.0[1], pr.0[2]), SIMD3(pr.1[0], pr.1[1], pr.1[2]))
    }
    print(String(format: "  %d: %d → %d, %.1f mm, radius %.2f–%.2f mm", s.id, s.from, s.to, len,
                 s.radii.min()!, s.radii.max()!))
}
print("insertionNode = \(insertion)")
