# Port Mar2025VascCath to Unity for Meta Quest 3 — size estimate & phased plan

## Context
The user is asking how big a job it is to move this visionOS app (SwiftUI + RealityKit + ARKit)
to Unity on Meta Quest 3, and which model and effort level to use. Nothing carries over directly:
the code is Swift and the content is USD, while Unity needs C# and FBX/glTF. This is a rewrite
that keeps the design, not a conversion.

## What exists today (measured)
| Area | Swift lines | Unity equivalent | Difficulty |
|---|---|---|---|
| Views (SwiftUI windows, HUD, cath-lab rig, monitors) | 8,900 | Unity UI (uGUI or UI Toolkit) on world-space canvases | **High**: the largest chunk, and all of it is rewritten |
| Model (FSM, scoring, safe zones, progression) | 3,900 | Plain C# classes; the logic ports almost line for line | Medium (mechanical) |
| ESP32 playground (BLE trackers) | 1,950 | Android BLE plugin (a Quest 3 runs Android) | Medium–high |
| Debug tooling | 1,900 | Rebuild only the parts you need | Low; optional |
| Vessel playground | 1,900 | C# plus a procedural `Mesh`; the math ports directly | Medium |
| Managers (stylus, targets, video) | 1,440 | Logitech MX Ink SDK (supported on Quest 3), `VideoPlayer` | Medium |
| Voice | 500 | Meta Voice SDK (Wit.ai), or Android speech | Medium |
| Tests | 2,000 | Unity Test Framework (NUnit) | Low |
| **Total** | **~21k** (141 files) | | |

Assets:
- 28 usdz and 20 usda scenes (training room, catheter, guidewire, hub, coil, buttons). These need
  converting through Blender to FBX/glTF. Materials are redone in URP, and USD timeline animations
  (e.g. `catheter-NoEase-1124`) are re-imported as Animator clips.
- 12 mp4, 30 mp3 and about 405 png files (X-ray frame sequences): these import as they are.
- The vessel OBJ/STL meshes and centerline JSON import as they are.

## Platform gaps (the risky part)
- **Image tracking (ARKit):** Quest 3 has no built-in image tracking. Options:
  - Meta's Passthrough Camera API with your own marker detection (e.g. OpenCV)
  - Spatial anchors placed once by the user
  - Drop it
  Decide this first.
- **Hand tracking:** good on Quest 3 (Meta XR SDK / XR Hands). The pinch, push and twist logic
  ports over, but the thresholds need retuning.
- **Gaze plus pinch UI:** Quest uses controller or hand rays and poke. The UI interaction model
  changes, though it isn't harder.
- **Passthrough "mixed" immersion:** supported (Meta passthrough with MR Utility Kit).
- **Rendering budget:** the Quest 3 is a mobile GPU. The translucent vessels, the room and video
  need optimizing (draw calls, transparency overdraw).

## Size estimate
- One experienced Unity/Quest developer working without AI help: about 3–5 months for feature
  parity.
- With Claude doing most of the porting: code translation becomes fast (days per module). The
  bottleneck moves to the things only you can do:
  - asset conversion
  - on-headset testing and tuning
  - Unity scene and prefab wiring
  
  Realistically that's about 6–10 weeks of part-time effort. A "vessel playground only" proof of
  concept is about 1–2 weeks.

## Recommended phases
1. **Proof of concept: the vessel playground.** It is self-contained, pure math, with simple
   assets. This proves the Unity, Quest, hands and passthrough pipeline, and shows the real
   performance.
2. **Asset pipeline:** convert the training room and devices from USD to glTF/FBX, then set up
   URP materials and prefabs.
3. **Core sim:** port the FSM, scoring and progression to C#, along with their tests (this logic
   barely changes).
4. **Interaction:** hand tracking, the MX Ink stylus, grab targets and the boom monitors.
5. **UI:** HUD, guidance, debrief and patient info as world-space canvases.
6. **Extras:**
   - voice commands
   - BLE trackers
   - tracking the physical table: image tracking or a replacement (see Platform gaps)

## Which model and effort level
- **Opus 5.5 at medium effort** is enough for most of the port: module-by-module translation of
  Swift to C#, UI rebuilds, and tests.
- **Use high effort (or plan mode)** for:
  - the initial architecture: how the `@Observable` state and ECS map onto MonoBehaviours or
    ScriptableObjects
  - the FSM port, which must match `specs/fsm-state-chart.md` exactly
  - the replacement for image tracking
  - hard on-device bugs
- The bigger lever is breaking the work into phases, with one session per module and specs
  checked in, rather than a higher effort level.
- Unity skills already available here (`unity:new-unity-project`, `unity:unity-cli`,
  `unity:ui-uitk`/`ui-ugui`) can create the project and drive the editor.
- Claude cannot test on the headset. Every phase needs you to try it on the Quest 3.

## Verification (per phase)
- EditMode tests (NUnit) for the ported model and FSM logic, mirroring the current XCTest cases.
- Run in the editor with the Meta XR Simulator, then on a Quest 3 build. Track frame time against
  the 72/90 Hz budget with the OVR Metrics Tool.
