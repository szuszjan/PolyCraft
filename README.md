```
█████ █████ █     █   █ █████ █████ █████ █████ █████
█   █ █   █ █      █ █  █     █   █ █   █ █       █
█████ █   █ █       █   █     █████ █████ █████   █
█     █   █ █       █   █     █  █  █   █ █       █
█     █████ █████   █   █████ █   █ █   █ █       █
```

A low-poly 3D modeling app for Windows, built in VB.NET / WPF. Assemble scenes from
primitives, transform them with a real object-oriented gizmo, cut holes with boolean
operations, and export to OBJ or STL.

## Features

- **Primitives** — Cube, Pyramid, Sphere, Cylinder, Cone, Plane, each generated as a
  flat-shaded low-poly mesh.
- **Transform gizmo** — Move, Rotate, and Scale handles that track the selected object's
  actual orientation (not just world axes), so dragging a handle always does what it
  visually indicates, even after the object has already been rotated.
- **Rotate rings** — drag-to-rotate handles that turn by the angle your mouse sweeps
  around the object, like a trackball, rather than a linear axis hack.
- **Selection box** — a wireframe outline on the selected object(s); supports
  multi-select (Ctrl/Shift-click in the object list).
- **Join / Separate** — combine multiple primitives into one compound object, or split
  a joined object back apart.
- **Cut tool** — mark any shape as a cut tool (it renders translucent red), then Join it
  with a solid to subtract it via a real BSP-tree boolean operation — a Tinkercad-style
  "hole" tool, not a visual trick.
- **Snap to object** — optional snapping that aligns the moved object's edges/center
  with nearby objects', independently per axis.
- **View cube** — a Fusion-360-style navigation cube in the corner of the viewport;
  click a face to snap the camera to that orthographic view.
- **Orbit camera** — right-drag to orbit, middle-drag to pan, scroll to zoom, with a
  perspective/orthographic toggle.
- **Color** — a palette of quick-pick swatches plus a hex code field per object.
- **Save / load** — scenes save to a JSON project file and reload exactly as left.
- **Export** — Wavefront OBJ and binary STL, ready for slicing or import elsewhere.
- **Render** — File > Render saves a clean PNG of the current view (2x supersampled),
  with the origin axis indicator, selection outline, and gizmo hidden.

## Requirements

- Windows
- [.NET 9 SDK](https://dotnet.microsoft.com/download)

## Running it

```powershell
dotnet run
```

or open `PolyCraft.vbproj` in Visual Studio and press F5.

## Controls

| Input | Action |
|---|---|
| Left-click | Select object / drag gizmo handle |
| Right-drag | Orbit camera |
| Middle-drag | Pan camera |
| Scroll | Zoom |
| Ctrl/Shift-click (object list) | Multi-select |

## Project layout

| File | Purpose |
|---|---|
| `MainWindow.xaml(.vb)` | Main window, viewport, and all scene/tool interaction |
| `SceneObject.vb` | The scene object model (primitives, compounds, cut tools) |
| `MeshBuilder.vb` | Procedural low-poly mesh generation per primitive kind |
| `TransformHelper.vb` | Transform composition, Euler decomposition, compound flattening |
| `CSG.vb` | BSP-tree boolean mesh subtraction (the cut tool) |
| `Exporters.vb` | OBJ and STL export |
| `SplashWindow.xaml(.vb)` | Startup splash screen (fade/scale animation + chime) |
| `SoundGen.vb` | Synthesizes the startup chime as an in-memory WAV, no audio assets needed |

## License

No license has been chosen yet for this project.
