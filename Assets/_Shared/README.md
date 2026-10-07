# AR Murals — shared architecture

Read this before you touch the project. It takes two minutes and it is the whole
reason four people can build five murals at once without blocking each other.

## The rule

You work ONLY inside `Assets/Murals/<your slot>/`. Nobody edits anything in anyone
else's folder, and nobody edits `Assets/_Shared/` except the group leader.

```
Assets/
  _Shared/            Interface, manager, UI shell, editor tools   (lead only)
  Murals/
    M1_Emergence/     A — Chibueze
    M2_Reconstruct/   B
    M3_Expansion/     C
    M4_Story/         D
    M5_LivingPaint/   B
  ReferenceImages/    The one shared image library                 (lead only, at merge)
```

## Never share a scene

Unity scene files cannot be merged. Each person builds in their OWN scene inside
their own folder, and packages the result as ONE prefab with ONE root object named
`Mural_<slot>_Root`. At the merge, five prefabs are dragged into one scene. Five
drags, no conflicts.

## The contract

Your mural root carries one script deriving from `MuralExperienceBase`, which gives
you the three methods the shared manager calls and nothing else:

```csharp
void OnTrackingFound();   // begin or resume
void OnTrackingLost();    // hide content within 0.5s, keep state
void OnReset();           // back to untouched
```

`ARMuralManager` (on the XR Origin) watches the tracked image library, matches each
detected mural to its owner by slot name, parents that owner's root to the physical
mural, and calls those three methods. It knows nothing about what your mural does.

## Build with no camera and no image target

Tick **Debug Keys** on your experience component and press Play:

| Key | Does |
| --- | --- |
| F | simulate tracking found |
| L | simulate tracking lost |
| R | reset to untouched |

This is what lets you build and polish your entire mural today, before anyone has
photographed anything.

## Named slots that must not be renamed

Audio children on your mural root: `SFX_Detect`, `SFX_Lost`, `SFX_Confirm`,
`Ambient_Bed` (plus `VO_Narration` on M4). Put any placeholder clip in them now;
D's final audio pack replaces the clips, not the structure.

UI panels on `UI_Shell`: `StartPanel`, `ScanPanel`, `ARControls`, `InfoPanel`,
`ExitPanel`. Reference them through `UIShellController`, never by dragging panels
into serialised fields — C's polished prefab has to be able to replace this one.

## Tracking-lost behaviour (this is marked)

Agreed for all five murals: on loss, digital content hides within half a second
rather than freezing in mid-air, the scanning prompt returns, and state is kept so
re-acquiring the mural resumes instead of restarting. `MuralExperienceBase` and
`ARMuralManager` already do this. Do not reimplement it per mural.

## Image targets

While building, test with your own single-image library in your own folder. The five
images go into `Assets/ReferenceImages/` as ONE library only at the merge. Each
reference image is named for its slot (`M1`, `M2`, …) and must carry the mural's real
physical width in metres, or your content will be the wrong size on the wall.

## Editor menu

- **AR Murals → 1. Configure Project** — Force Text serialisation, visible meta files,
  app id, ARM64 + IL2CPP, OpenGLES3 only. Run once, on any machine.
- **AR Murals → 2. Build M1 Emergence Scene** — M1 only. It is the group leader's own
  scene builder; it is not a template for anyone else's mural.

## Starting your mural (M2-M5)

Build it yourself. The scaffolding above is the contract, not a generator.

1. Pull the repo, open the project, run **AR Murals → 1. Configure Project**.
2. Make a new scene in `Assets/Murals/<your folder>/Scenes/`. Copy the AR Session and
   XR Origin out of the AR template's SampleScene so you start from Unity's own rig,
   and delete the template's demo UI and object spawner.
3. Add `ARTrackedImageManager` to the XR Origin, and a reference image library of your
   own in your own folder, with your mural photograph named for your slot and its real
   physical width in metres.
4. Add `ARMuralManager` to the XR Origin and drag your mural root into its list.
5. Build your mural root as ONE GameObject named `Mural_<slot>_Root`, with everything
   under a child called `Content`, the four named audio sources, and one script that
   derives from `MuralExperienceBase`.
6. Save that root as a prefab in your own `Prefabs/` folder. That prefab is what gets
   dragged into the merged project at the end.
7. Build your UI from the five panel names listed above so C's polished `UI_Shell`
   can replace yours without rewiring anything.

M1 is in the repo and you can read it for reference, but how your mural transforms,
what emerges, how it animates and how the viewer interacts with it is yours to design
and build. Copying M1's structure is fine; copying its behaviour defeats the point of
using five different transformation approaches.

**Keep one beat in common** — dim or mask the painted original before anything digital
appears. That is what makes the content read as having come out of the wall rather than
sitting on top of it, and mural-digital integration is 8 of the 40 marks.
