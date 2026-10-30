# Vessel Playground

**Purpose:** A standalone prototype where a flexible guidewire and catheter are
pushed and twisted through real 3D vessel meshes, steering into side branches by
rotating an angled tip. It does not touch the main training flow, the FSM, or scoring.

**Launch:** the "Vessel Playground" button on the scenario banner
(`VesselPlaygroundLaunchButton`) opens the control window, and the window opens the
`VesselPlayground` immersive space. Closing the window takes the space down and reopens
the main menu.

## Vessel models (`VesselModelCatalog`)

A segmented picker at the top of the control window (`VesselModelPicker`) switches models.
Switching loads that model's centerlines, puts both devices back at the access site, and
the immersive view rebuilds the scene.

| Picker | Meshes drawn | Devices run in | Access site (model) | Display | Device scale |
|---|---|---|---|---|---|
| Tube | `VesselTube.obj` | the tube | trunk's max-y end | 3× | 1 |
| AVM | `avm_vessel_simple.obj` | the AVM | feeder at the +x/−y corner | 4× | 0.35 |
| Artery | `artery.stl` (red) + `vein.stl` (blue) | the artery | low-x, low-z end of the paired trunks | 3.5× | 0.5 |
| Vein | `artery.stl` (red) + `vein.stl` (blue) | the vein | low-x, low-z end of the paired trunks | 3.5× | 0.5 |

- **Assets** are in `Resources/VesselPlayground/`, in mm. The full-detail AVM
  (`avm_vessel.obj`, 200k triangles, 13.5 MB) is kept in `assets/vessel-models/`, outside
  the app target. The app ships the 50k-triangle `avm_vessel_simple.obj`, which has the same
  shape.
- **Artery and vein** are two separate closed vessels with no shared lumen, so a route can't
  cross from one to the other. That's why each has its own picker entry. Both are always drawn.
- **Device scale** shrinks the wire and catheter's radius, tip size and external length,
  because the new models' vessels are only about 0.3–1.1 mm in lumen radius. The tip angles are
  unchanged.
- **Layout (`VesselLayout`):** every model **lies flat with its access site on the right**.
  The orientation is computed from the centerlines, not set per model:
  - The least-spread principal axis (PCA of the centerline points) points world up, so the
    branches spread out horizontally.
  - The direction from the model's center to the access site points world +x, so the entry
    is the rightmost point.
  - Resulting heights: tube 43 mm (its branches leave the plane a little); AVM, artery and
    vein 9–11 mm.
- **Adding a model:** drop the mesh into `Resources/VesselPlayground/`, run the script below,
  and add a `VesselModelSpec` to `VesselModelSpec.all`. It will lie flat with its entry on
  the right automatically.

## Centerlines (`scripts/extract_centerlines.swift`)

Each model has a centerline graph JSON: nodes (`end`/`junction`), plus segments with points
and radii resampled every 1 mm. `insertionNode` is the access site. The files can be edited
by hand.

```bash
swiftc -O scripts/extract_centerlines.swift -o /tmp/extract_centerlines
R=Mar2025VascCath/Resources/VesselPlayground
/tmp/extract_centerlines $R/VesselTube.obj $R/VesselCenterlines.json
/tmp/extract_centerlines --solid --voxel=0.25 --access-near=53.5,-1.5,-0.4 $R/avm_vessel_simple.obj $R/AVMCenterlines.json
/tmp/extract_centerlines --solid --voxel=0.25 --access-near=-46.9,2.3,5.2 $R/artery.stl $R/ArteryCenterlines.json
/tmp/extract_centerlines --solid --voxel=0.25 --access-near=-33.2,5.6,4.9 $R/vein.stl $R/VeinCenterlines.json
```

- **Inputs:** OBJ (CRLF line endings are fine) or binary STL. Several inputs are merged into
  one model.
- **Hollow vs `--solid`:**
  - Hollow (the default) is for pipe-wall meshes like the Tinkercad tube. The lumen is the
    empty space the wall encloses, found with 26-direction enclosure rays.
  - `--solid` is for segmentation-style meshes, where each closed surface *is* the blood pool.
    The lumen is simply the inside.
- **Pipeline:**
  1. Voxelize by z-ray parity per shell.
  2. Keep the largest lumen component.
  3. Run an exact distance transform to get the local radius.
  4. Grow a tree of 1/r²-weighted minimal paths.
  5. Trim the end caps, smooth, and resample.
  6. Clean up: drop dead-end stubs under 2 mm next to a junction, and merge pass-through
     (degree-2) nodes.
- **Access site:** `--access-near=x,y,z` (mm) picks the end nearest that point. Without it, the
  highest (max y) end is used.

## Model (`VesselPlayground/`, no RealityKit)

| Type | Role |
|---|---|
| `VesselGraph` | Loads a centerline JSON and converts mm to m. |
| `VesselRoute` | The chain of legs from the access site, sampled with parallel-transport frames. `frame(at:)` extends straight out of the vessel for s < 0. |
| `FlexDevice` | Insertion, twist, radius, tip shape and tip scale. Wire: 0.9 mm, angled tip by default. Catheter: 5 Fr, Bern tip by default. `scaled(by:)` sizes them per model. |
| `TipShape` | The tip catalog (below): each shape is a chain of constant-curvature bends, plus the capture angle it steers with. |
| `VesselModelSpec` | Catalog entry: meshes, centerlines, display and device scale, picker note. |
| `VesselLayout` | `flatOrientation(for:)`: lay flat, access on the right (see above). |
| `CathWirePlaygroundModel` | Push/twist rules (below). `load(_:graph:)` switches models and recomputes `orientation`. |

**Push/twist rules (`CathWirePlaygroundModel`):**

- **Shared route:** both devices use one route. The device that is further in leads.
- **Branch choice:** when the leading tip passes the junction at the end of the route,
  `chooseNextLeg` scores each leg leaving the junction. The score is the dot product of the
  leg's sideways heading (6 mm look-ahead) with the tip's bend direction, which is the frame
  normal rotated by `twist`.
  - **One near-straight leg** (within about 20° ahead): that leg is the default. A side
    branch is taken only within 55° (`branchCaptureAngle`).
  - **Several near-straight legs** (a tight fork, common in the AVM): the bend picks among all
    of them, so no leg is unreachable.
  - A leg that doubles back is never treated as straight ahead.
- **Pulling back:** if the leading tip retracts to or behind a leg's start, that leg is
  un-committed, so the branch can be chosen again.
- **Limits:** insertion is at least 0, and tips stop 1 mm short of a dead end.
- **Status:** `routeDescription` reports "Main trunk" or "Side branch N", numbered from the
  access site along the longest path. The model also exposes `tipAtJunction` and
  `tipAtDeadEnd`.

## Tip shapes (`TipShape`)

The **Wire tip** and **Catheter tip** pickers in the control window (`TipShapePicker`) switch
shapes in place. Insertion, twist and the committed route are kept, and so is the choice through
Reset and model switches. Sizes are approximate and smaller than real catheter curves, so they
read inside the playground's vessels.

| Device | Shape | Bends (shaft → tip, stock size) | Capture angle |
|---|---|---|---|
| Wire | Straight | none | can't steer: always takes the straightest leg |
| Wire | J-tip | 180° half-circle, 3 mm radius | 30° (meant to avoid small side branches) |
| Wire | Angled (default) | 45° over 2 mm, then 6 mm straight | 55° |
| Catheter | Bern (default) | 30° over 4 mm, then 8 mm straight | 55° |
| Catheter | Cobra | 60° primary curve over 8 mm, 6 mm straight, −45° "head" over 3 mm, 2 mm straight | 55° |
| Catheter | Mikaelsson | 180° loop (4 mm radius), 8 mm limb running back alongside the shaft, 45° hook, 2 mm | 55° |

- **Steering:** every shaped tip steers by its bend plane, which turns with twist. The shape
  changes only how it looks and its capture angle. The leading device's tip makes the choice.
- **Drawing (`DeviceShape`):** the shaft follows the route up to where the tip starts. The tip's
  2D curve is then laid along the route, with x along the route and y along the twist-rotated
  bend direction. The tip's farthest-forward point sits at the insertion depth, so a J or a
  Mikaelsson leads with its curve. The whole shape is shrunk uniformly (down to 5%) to fit the
  narrowest lumen it spans, which is why the larger catheter curves look small in the thin
  models. Outside the vessel it is drawn full size.
- **Not modeled:** a wire inside the catheter doesn't straighten or reshape the catheter's tip.

## Rendering

- **Scene placement:** `VesselPlaygroundImmersiveView` places the root at table height,
  `[0, 0.9, -0.7]`, so the user looks down onto the flat model. The root uses the model's
  flat orientation, is scaled by the spec's display scale, and the vessel's bounds are
  centered on it.
- **Table (`VesselTable`):** a 1.2 × 0.7 m table with a white matte top (so the
  translucent vessels show their full color and the devices stand out) on brushed-steel legs to the floor. It is a separate
  entity, so it stays level while the model above it is rotated. On every model switch it is
  re-fitted so its top sits 5 mm under the vessel's lowest point.
- **Model switches:** the scaled child is named after the spec id. When the picker changes the
  spec, the `RealityView` update rebuilds the vessel, devices and overlay.
- **Vessels:** `VesselMeshLoader` builds them with flat normals and translucent materials, red
  for arteries and blue for veins.
- **Devices:** `DeviceShape` samples the route from the external length outside the access
  site, then lays on the tip shape (see Tip shapes). `TubeGeometry` sweeps the tube and adds a
  tip cap. Devices are drawn `VesselSceneFactory.deviceDrawScale` (2.5×) thicker than their
  physical radius so they read at room scale. The lumen fit uses the physical radius, so the
  drawn tube can overlap the vessel wall in the thin models.
- **Device colors:** the wire is near-black and the catheter bright green (65% opaque, so the
  wire shows inside it); both contrast with the red/blue vessels and the white table.
- **Twist cue:** a stripe on each device rotates with twist: yellow on the wire, white on the
  catheter.
- **Debug overlay:** "Show centerlines" draws the graph. Junctions are yellow, ends are green,
  and the access site is blue.

## Input

- **On-screen (`DeviceControlPanel`):** choose Wire or Catheter. Push is ±1 or ±5 mm per press
  and twist is ±3° or ±15°; every button repeats while held.
- **Push direction (`VesselPlayground.insertionAxis`):** the world direction of the route's
  first 2 cm. Because the entry is on the right, it is always leftward, angled away from the
  user by however much the vessel turns:
  - Tube: straight left.
  - Artery: left and slightly away.
  - AVM and vein: left and away, about 45°.
- **Hands (`VesselHandInput`):** the left pinch drives the catheter and the right pinch drives
  the wire.
  - A pinch-drag along the push direction pushes, scaled by `pushScale` 0.5.
  - Wrist roll about that axis twists, using the swing–twist decomposition.
  - Release the pinch to ratchet.
- **Stylus (`VesselStylusInput`, visionOS 26):** primary/secondary button toggles select the
  catheter or wire.
  - Stylus motion along the push direction pushes and stylus roll twists.
  - While the playground is open it sets `GameControllerManager.drivesAppModel = false`, so the
    stylus doesn't move the main sim's catheter/wire distances. The flag is restored when the
    space closes.

## Tests

`Mar2025VascCathTests/VesselPlaygroundTests.swift` covers:
- **Synthetic Y junction:**
  - mm→m loading, and sampling outside the access site
  - branch choice by twist
  - the catheter following the wire's route
  - un-committing a branch on pull-back
  - dead-end clamping and reset
- **Tight fork:** the bend picks between two near-straight legs.
- **Models:**
  - device scaling, and model switching (devices rescaled, route reset)
  - flat layout: the plane turns horizontal and the access site is the rightmost point
  - unique centerline files per catalog entry
  - binary STL parsing
- **Tip shapes:** the picker options for each device, J-tip and straight curves, a straight
  wire not steering, and switching tips keeping insertion and surviving Reset and model loads.
- **Geometry:** tube vertex and index counts, and every tip shape staying inside the lumen.

The project has no test target yet, so these tests have only been run through a macOS harness
that stands in for XCTest. An exhaustive twist search over every fork (run the same way) reached
every dead end in all four models: Tube 7/7, AVM 20/20, Artery 16/16, Vein 20/20.

## Tuning knobs

| Setting | Where |
|---|---|
| Display scale, device scale, access note | `VesselModelSpec` (catalog) |
| Lay-flat / entry-right rule | `VesselLayout` |
| `branchCaptureAngle`, `endMargin` | `CathWirePlaygroundModel` |
| Tip shapes, capture angles | `TipShape` |
| Default tips, device sizes | `FlexDevice` |
| Device draw thickness | `VesselSceneFactory.deviceDrawScale` |
| `pushScale` / `twistScale` | `VesselHandInput`, `VesselStylusInput` |
| Root placement (table height) | `VesselPlaygroundImmersiveView` |
| Table size, colors, clearance | `VesselTable` |
