# blood-vessels
blood vessels, guidewire and catheter , playground on Quest

A Unity 3D port of the visionOS **Vessel Playground** (from Mar2025VascCath) for Meta Quest 3.
A flexible guidewire and catheter are pushed and twisted through 3D vessel models, steering
into branches by rotating a pre-shaped tip.

## Unity project

The repo root is the Unity project: **Unity 6000.3.9f1**, URP, Android target for Meta Quest 3.

- **Scene:** `Assets/Scenes/VesselPlayground.unity`. It has:
  - the Meta camera rig with passthrough (you see the real room) and tracked hands
  - a 1.2 × 0.7 m table, 0.9 m high and 0.7 m in front of you
  - the vessel models lying flat on the table. **Tube** is shown; **AVM** and **Artery + Vein**
    are inactive under `Vessels` (toggle them in the Hierarchy).
- **Packages:** Unity OpenXR, Meta XR Core SDK, Interaction SDK and MR Utility Kit (207.0.1).
- **XR setup:** OpenXR loader on Android, with the Meta Quest and Meta XR features and the Touch /
  Touch Plus controller profiles. Hand tracking is set to controllers plus hands, and passthrough is
  on. Player settings: IL2CPP, ARM64, Vulkan, linear color, min API 32.
- **Vessel assets:** `Assets/Vessels/Models` (mm meshes, imported at 0.001 scale by
  `VesselModelPostprocessor`; the STLs were converted to OBJ), `Assets/Vessels/Centerlines`
  (route graphs as TextAssets) and `Assets/Vessels/Materials`.
- **Rebuild tools** (menu **Tools > Blood Vessels**):
  - **Configure for Quest 3** re-applies the project settings.
  - **Rebuild Vessel Playground Scene** regenerates the scene from
    `Assets/Editor/ProjectBootstrap/VesselSceneBuilder.cs`.
  - For Meta's own checks, use **Meta > Tools > Project Setup Tool**.

**Run on the headset:**
1. Turn on Developer Mode for the Quest 3 in the Meta Horizon app.
2. Connect it by USB.
3. In Unity, go to **File > Build Profiles > Android**, then **Build And Run**.

## What's here

| Folder | Contents |
|---|---|
| `docs/quest3-port-plan.md` | Size estimate, platform gaps and phased plan for the Quest 3 port. This project is phase 1 (the vessel-playground proof of concept). |
| `docs/vessel-playground-spec.md` | How the original playground behaves: models, centerlines, push/twist and branch rules, tip shapes, rendering, input and tuning knobs. |
| `source-assets/vessels/` | Vessel meshes (mm units) and their centerline graphs (JSON), one per model: Tube, AVM, Artery, Vein. |
| `source-assets/vessels/full-detail/` | The 200k-triangle AVM. The app uses `avm_vessel_simple.obj`, which has the same shape. |
| `reference/swift/` | The original Swift source and tests, for porting to C#. The model files (`VesselGraph`, `VesselRoute`, `FlexDevice`, `TipShape`, `CathWirePlaygroundModel`, `DeviceShape`, `VesselLayout`) are plain math with no Apple APIs. |
| `reference/scripts/extract_centerlines.swift` | The offline tool that made the centerline JSON from a mesh. Run it on a Mac when you add a new model. |

## Notes for the Unity side
- **Units:** meshes and centerlines are in millimetres. Scale by 0.001 to get Unity metres, then
  apply each model's display scale (see the spec's catalog table).
- **Handedness:** RealityKit is right-handed and Unity is left-handed. Flip one axis
  (e.g. z → −z) when importing positions and centerline points, and keep it consistent.
- **STL import:** Unity doesn't import `.stl` natively. Convert `artery.stl` and `vein.stl` to
  OBJ or FBX in Blender, or port the small STL parser in `VesselMeshLoader.swift`.
- **Centerline JSON:** nodes (`end`/`junction`) plus segments with points and radii every 1 mm.
  `insertionNode` is the access site. It loads directly with `JsonUtility` or Newtonsoft.
