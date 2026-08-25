Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports System.Windows
Imports System.Windows.Input
Imports System.Windows.Media
Imports System.Windows.Media.Imaging
Imports System.Windows.Media.Media3D

Namespace PolyCraft

Partial Class MainWindow

    Private sceneObjects As New List(Of SceneObject)()
    Private selectedObject As SceneObject = Nothing
    Private ReadOnly modelLookup As New Dictionary(Of GeometryModel3D, SceneObject)()
    Private ReadOnly gizmoLookup As New Dictionary(Of GeometryModel3D, Char)()
    Private ReadOnly meshCache As New Dictionary(Of PrimitiveKind, MeshGeometry3D)()
    Private placeCounter As Integer = 0
    Private ReadOnly rng As New Random()

    ' --- Transform gizmo state ---
    Private gizmoMode As GizmoMode = GizmoMode.Move
    Private isDraggingGizmo As Boolean = False
    Private dragAxis As Char = "X"c
    Private dragStartMouse As Point
    Private dragStartPos As Point3D
    Private dragStartRot As Point3D
    Private dragStartScale As Point3D
    Private dragCenterScreen As Point
    Private dragStartAngleRad As Double
    Private dragAxisDirWorld As Vector3D

    Private ReadOnly palette() As String = {
        "#E74C3C", "#E67E22", "#F1C40F", "#2ECC71", "#1ABC9C", "#3498DB",
        "#9B59B6", "#EC7063", "#5DADE2", "#58D68D", "#F4D03F", "#AF7AC5"
    }

    ' --- Orbit camera state ---
    Private cameraTarget As New Point3D(0, 0.5, 0)
    Private cameraYaw As Double = 35
    Private cameraPitch As Double = 25
    Private cameraDistance As Double = 6
    Private isOrbiting As Boolean = False
    Private isPanning As Boolean = False
    Private lastMousePos As Point

    ' --- Projection mode ---
    Private isOrthographic As Boolean = False
    Private ReadOnly orthoCamera As New OrthographicCamera()

    ' --- Snap to object ---
    Private snapEnabled As Boolean = False

    ' --- View cube ---
    Private ReadOnly viewCubeLookup As New Dictionary(Of GeometryModel3D, (Yaw As Double, Pitch As Double))()

    Public Sub New()
        InitializeComponent()
        BuildPalettePanel()
        BuildViewCube()
        UpdateCamera()
        UpdateOrthoButtonVisual()
        UpdateSnapButtonVisual()
        RefreshObjectList()
        UpdateInspector()
        UpdateGizmoModeButtons()
    End Sub

#Region "Mesh cache / scene rebuild"

    Private Function GetCachedMesh(kind As PrimitiveKind) As MeshGeometry3D
        If Not meshCache.ContainsKey(kind) Then
            meshCache(kind) = MeshBuilder.Build(kind)
        End If
        Return meshCache(kind)
    End Function

    Private Function ParseColorOrGray(hex As String) As Color
        Try
            Return CType(ColorConverter.ConvertFromString(hex), Color)
        Catch
            Return Colors.Gray
        End Try
    End Function

    Private Sub RebuildScene()
        Dim group As New Model3DGroup()
        modelLookup.Clear()
        gizmoLookup.Clear()

        AddOriginGizmo(group)

        For Each obj In sceneObjects
            AddObjectModels(group, obj)
        Next

        For Each obj In GetSelectedObjects()
            AddSelectionBox(group, obj)
        Next

        If selectedObject IsNot Nothing AndAlso gizmoMode <> GizmoMode.None Then
            AddTransformGizmo(group, selectedObject)
        End If

        SceneVisual.Content = group
    End Sub

    Private Sub AddObjectModels(group As Model3DGroup, obj As SceneObject)
        For Each piece In TransformHelper.GetPieces(obj)
            Dim mesh = If(piece.Kind = PrimitiveKind.Custom, MeshBuilder.BuildCustom(piece.CustomTriangles), GetCachedMesh(piece.Kind))

            ' Cut tools render as a translucent red overlay (Tinkercad "hole" style) regardless
            ' of their own color, so it's obvious they're a cutter and not a solid.
            Dim mat As Material = If(obj.IsCutTool,
                New DiffuseMaterial(New SolidColorBrush(Color.FromArgb(130, 220, 60, 60))),
                New DiffuseMaterial(New SolidColorBrush(ParseColorOrGray(piece.ColorHex))))

            Dim geoModel As New GeometryModel3D(mesh, mat)
            geoModel.BackMaterial = mat
            geoModel.Transform = New MatrixTransform3D(piece.Matrix)

            group.Children.Add(geoModel)
            modelLookup(geoModel) = obj
        Next
    End Sub

    Private Sub AddOriginGizmo(group As Model3DGroup)
        Dim cubeMesh = GetCachedMesh(PrimitiveKind.Cube)
        AddFixedBox(group, cubeMesh, New Point3D(0.5, 0, 0), New Vector3D(1, 0.04, 0.04), Colors.IndianRed)
        AddFixedBox(group, cubeMesh, New Point3D(0, 0.5, 0), New Vector3D(0.04, 1, 0.04), Colors.MediumSeaGreen)
        AddFixedBox(group, cubeMesh, New Point3D(0, 0, 0.5), New Vector3D(0.04, 0.04, 1), Colors.CornflowerBlue)
    End Sub

    Private Sub AddFixedBox(group As Model3DGroup, mesh As MeshGeometry3D, center As Point3D, scale As Vector3D, color As Color)
        Dim mat As New DiffuseMaterial(New SolidColorBrush(color))
        Dim gm As New GeometryModel3D(mesh, mat)
        gm.BackMaterial = mat
        Dim tg As New Transform3DGroup()
        tg.Children.Add(New ScaleTransform3D(scale.X, scale.Y, scale.Z))
        tg.Children.Add(New TranslateTransform3D(center.X, center.Y, center.Z))
        gm.Transform = tg
        group.Children.Add(gm)
    End Sub

#End Region

#Region "Selection box"

    ''' <summary>Local-space (pre-object-transform) center/size of the object's actual geometry:
    ''' the unit cube for a simple primitive, or the union of part bounds for a compound. No
    ''' padding - this is the real surface, used for snapping so objects end up flush, not gapped.</summary>
    Private Function ComputeTrueLocalBounds(obj As SceneObject) As (center As Point3D, size As Vector3D)
        If obj.Parts Is Nothing OrElse obj.Parts.Count = 0 Then
            Return (New Point3D(0, 0, 0), New Vector3D(1, 1, 1))
        End If

        Dim minX = Double.MaxValue, minY = Double.MaxValue, minZ = Double.MaxValue
        Dim maxX = Double.MinValue, maxY = Double.MinValue, maxZ = Double.MinValue
        For Each part In obj.Parts
            Dim hx = 0.5 * part.ScaleX, hy = 0.5 * part.ScaleY, hz = 0.5 * part.ScaleZ
            minX = Math.Min(minX, part.PosX - hx) : maxX = Math.Max(maxX, part.PosX + hx)
            minY = Math.Min(minY, part.PosY - hy) : maxY = Math.Max(maxY, part.PosY + hy)
            minZ = Math.Min(minZ, part.PosZ - hz) : maxZ = Math.Max(maxZ, part.PosZ + hz)
        Next

        Dim center As New Point3D((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2)
        Dim size As New Vector3D(
            Math.Max(0.05, maxX - minX),
            Math.Max(0.05, maxY - minY),
            Math.Max(0.05, maxZ - minZ))
        Return (center, size)
    End Function

    ''' <summary>Local-space center/size of the box that should wrap the object for the visual
    ''' selection outline - the true bounds with a little padding so the box doesn't hug the mesh.</summary>
    Private Function ComputeSelectionBoxLocal(obj As SceneObject) As (center As Point3D, size As Vector3D)
        Dim trueBounds = ComputeTrueLocalBounds(obj)
        Return (trueBounds.center, trueBounds.size * 1.08)
    End Function

    Private Sub AddSelectionBox(group As Model3DGroup, obj As SceneObject)
        Dim bounds = ComputeSelectionBoxLocal(obj)
        Dim objTransform = TransformHelper.BuildTransform(obj)
        Dim boxMesh = GetCachedMesh(PrimitiveKind.Cube)
        Dim mat As Material = New EmissiveMaterial(New SolidColorBrush(Colors.Gold))
        Const t As Double = 0.035

        Dim hx = bounds.size.X / 2, hy = bounds.size.Y / 2, hz = bounds.size.Z / 2
        For Each sy In {-1, 1}
            For Each sz In {-1, 1}
                AddSelectionEdge(group, boxMesh, mat, objTransform,
                    New Point3D(bounds.center.X, bounds.center.Y + sy * hy, bounds.center.Z + sz * hz),
                    New Vector3D(bounds.size.X, t, t))
            Next
        Next
        For Each sx In {-1, 1}
            For Each sz In {-1, 1}
                AddSelectionEdge(group, boxMesh, mat, objTransform,
                    New Point3D(bounds.center.X + sx * hx, bounds.center.Y, bounds.center.Z + sz * hz),
                    New Vector3D(t, bounds.size.Y, t))
            Next
        Next
        For Each sx In {-1, 1}
            For Each sy In {-1, 1}
                AddSelectionEdge(group, boxMesh, mat, objTransform,
                    New Point3D(bounds.center.X + sx * hx, bounds.center.Y + sy * hy, bounds.center.Z),
                    New Vector3D(t, t, bounds.size.Z))
            Next
        Next
    End Sub

    Private Sub AddSelectionEdge(group As Model3DGroup, mesh As MeshGeometry3D, mat As Material, objTransform As Transform3DGroup, centerLocal As Point3D, scaleLocal As Vector3D)
        Dim edgeLocal As New Transform3DGroup()
        edgeLocal.Children.Add(New ScaleTransform3D(scaleLocal.X, scaleLocal.Y, scaleLocal.Z))
        edgeLocal.Children.Add(New TranslateTransform3D(centerLocal.X, centerLocal.Y, centerLocal.Z))

        Dim combined = edgeLocal.Value * objTransform.Value
        Dim gm As New GeometryModel3D(mesh, mat)
        gm.Transform = New MatrixTransform3D(combined)
        group.Children.Add(gm)
    End Sub

#End Region

#Region "Transform gizmo"

    Private Sub AddTransformGizmo(group As Model3DGroup, obj As SceneObject)
        Dim pos As New Point3D(obj.PosX, obj.PosY, obj.PosZ)

        ' Size the gizmo to the object's actual on-screen footprint so handles always extend
        ' clearly beyond its surface, instead of a fixed size that gets swallowed by big objects.
        Dim bounds = ComputeSelectionBoxLocal(obj)
        Dim worldSize As New Vector3D(bounds.size.X * obj.ScaleX, bounds.size.Y * obj.ScaleY, bounds.size.Z * obj.ScaleZ)
        Dim maxExtent = Math.Max(worldSize.X, Math.Max(worldSize.Y, worldSize.Z))
        Dim shaftLength = Math.Max(0.9, maxExtent * 0.75)
        Dim sizeRatio = shaftLength / 0.9
        Dim thickness = 0.035 * Math.Sqrt(sizeRatio)
        Dim tipScale = Math.Sqrt(sizeRatio)
        Dim ringRadius = shaftLength * 0.85

        ' The gizmo tracks the object's current orientation, so handles always point along its
        ' actual local axes - otherwise a rotated object's arrows/rings would visually lie.
        Dim rot = TransformHelper.BuildRotationMatrix(obj)
        Dim axisX = rot.Transform(New Vector3D(1, 0, 0))
        Dim axisY = rot.Transform(New Vector3D(0, 1, 0))
        Dim axisZ = rot.Transform(New Vector3D(0, 0, 1))

        Select Case gizmoMode
            Case GizmoMode.Move
                AddMoveAxis(group, pos, "X"c, axisX, Colors.IndianRed, shaftLength, thickness, tipScale)
                AddMoveAxis(group, pos, "Y"c, axisY, Colors.MediumSeaGreen, shaftLength, thickness, tipScale)
                AddMoveAxis(group, pos, "Z"c, axisZ, Colors.CornflowerBlue, shaftLength, thickness, tipScale)
            Case GizmoMode.Scale
                AddScaleAxis(group, pos, "X"c, axisX, Colors.IndianRed, shaftLength, thickness, tipScale)
                AddScaleAxis(group, pos, "Y"c, axisY, Colors.MediumSeaGreen, shaftLength, thickness, tipScale)
                AddScaleAxis(group, pos, "Z"c, axisZ, Colors.CornflowerBlue, shaftLength, thickness, tipScale)
            Case GizmoMode.Rotate
                AddRotateAxis(group, pos, "X"c, axisX, Colors.IndianRed, ringRadius)
                AddRotateAxis(group, pos, "Y"c, axisY, Colors.MediumSeaGreen, ringRadius)
                AddRotateAxis(group, pos, "Z"c, axisZ, Colors.CornflowerBlue, ringRadius)
        End Select
    End Sub

    ''' <summary>Rotates a direction so fromDir points along toDir instead. Used to orient gizmo
    ''' pieces (which are modeled along a fixed default axis) to point along an arbitrary
    ''' direction - a world axis, or a rotated object's current local axis.</summary>
    Private Function BuildAlignRotation(fromDir As Vector3D, toDir As Vector3D) As Transform3D
        fromDir.Normalize()
        toDir.Normalize()
        Dim dot = Vector3D.DotProduct(fromDir, toDir)
        If dot > 0.9999 Then Return New Transform3DGroup() ' identity
        If dot < -0.9999 Then
            Dim perp = Vector3D.CrossProduct(fromDir, New Vector3D(1, 0, 0))
            If perp.Length < 0.001 Then perp = Vector3D.CrossProduct(fromDir, New Vector3D(0, 1, 0))
            perp.Normalize()
            Return New RotateTransform3D(New AxisAngleRotation3D(perp, 180))
        End If

        Dim axis = Vector3D.CrossProduct(fromDir, toDir)
        axis.Normalize()
        Dim angleDeg = Math.Acos(Math.Max(-1, Math.Min(1, dot))) * 180 / Math.PI
        Return New RotateTransform3D(New AxisAngleRotation3D(axis, angleDeg))
    End Function

    ''' <summary>Two unit vectors perpendicular to axis and to each other, used to build a ring or
    ''' any other geometry that lies in the plane normal to axis.</summary>
    Private Function GetPerpendicularBasis(axis As Vector3D) As (u As Vector3D, v As Vector3D)
        axis.Normalize()
        Dim reference As New Vector3D(0, 1, 0)
        If Math.Abs(Vector3D.DotProduct(axis, reference)) > 0.99 Then reference = New Vector3D(1, 0, 0)
        Dim u = Vector3D.CrossProduct(reference, axis)
        u.Normalize()
        Dim v = Vector3D.CrossProduct(axis, u)
        v.Normalize()
        Return (u, v)
    End Function

    Private Sub AddGizmoShaft(group As Model3DGroup, pos As Point3D, axis As Char, axisDir As Vector3D, mat As Material, shaftLength As Double, thickness As Double)
        Dim center = pos + axisDir * (shaftLength / 2)
        Dim t As New Transform3DGroup()
        t.Children.Add(New ScaleTransform3D(shaftLength, thickness, thickness)) ' long axis = local +X
        t.Children.Add(BuildAlignRotation(New Vector3D(1, 0, 0), axisDir))
        t.Children.Add(New TranslateTransform3D(center.X, center.Y, center.Z))

        Dim gm As New GeometryModel3D(GetCachedMesh(PrimitiveKind.Cube), mat)
        gm.BackMaterial = mat
        gm.Transform = t
        group.Children.Add(gm)
        gizmoLookup(gm) = axis
    End Sub

    Private Sub AddGizmoTip(group As Model3DGroup, pos As Point3D, axis As Char, axisDir As Vector3D, mat As Material, useCone As Boolean, shaftLength As Double, tipScale As Double)
        Dim mesh = GetCachedMesh(If(useCone, PrimitiveKind.Cone, PrimitiveKind.Cube))
        Dim t As New Transform3DGroup()
        If useCone Then
            t.Children.Add(New ScaleTransform3D(0.14 * tipScale, 0.22 * tipScale, 0.14 * tipScale))
        Else
            t.Children.Add(New ScaleTransform3D(0.16 * tipScale, 0.16 * tipScale, 0.16 * tipScale))
        End If

        ' The cone/cube tip is modeled pointing along +Y by default; rotate it to point along axisDir.
        t.Children.Add(BuildAlignRotation(New Vector3D(0, 1, 0), axisDir))

        Dim tip = pos + axisDir * (shaftLength + 0.05 * tipScale)
        t.Children.Add(New TranslateTransform3D(tip.X, tip.Y, tip.Z))

        Dim gm As New GeometryModel3D(mesh, mat)
        gm.BackMaterial = mat
        gm.Transform = t
        group.Children.Add(gm)
        gizmoLookup(gm) = axis
    End Sub

    Private Sub AddMoveAxis(group As Model3DGroup, pos As Point3D, axis As Char, axisDir As Vector3D, color As Color, shaftLength As Double, thickness As Double, tipScale As Double)
        Dim mat As Material = New DiffuseMaterial(New SolidColorBrush(color))
        AddGizmoShaft(group, pos, axis, axisDir, mat, shaftLength, thickness)
        AddGizmoTip(group, pos, axis, axisDir, mat, useCone:=True, shaftLength:=shaftLength, tipScale:=tipScale)
    End Sub

    Private Sub AddScaleAxis(group As Model3DGroup, pos As Point3D, axis As Char, axisDir As Vector3D, color As Color, shaftLength As Double, thickness As Double, tipScale As Double)
        Dim mat As Material = New DiffuseMaterial(New SolidColorBrush(color))
        AddGizmoShaft(group, pos, axis, axisDir, mat, shaftLength, thickness)
        AddGizmoTip(group, pos, axis, axisDir, mat, useCone:=False, shaftLength:=shaftLength, tipScale:=tipScale)
    End Sub

    ''' <summary>Draws a hollow ring (not a solid disk) made of small tangent-aligned segments, in
    ''' the plane perpendicular to axisDir - the object's current local axis, so the ring you grab
    ''' visually matches the axis it actually rotates around.</summary>
    Private Sub AddRotateAxis(group As Model3DGroup, pos As Point3D, axis As Char, axisDir As Vector3D, color As Color, radius As Double)
        Const segments As Integer = 32
        Dim thickness = Math.Max(0.025, radius * 0.045)
        Dim mat As Material = New DiffuseMaterial(New SolidColorBrush(color))
        Dim boxMesh = GetCachedMesh(PrimitiveKind.Cube)
        Dim basis = GetPerpendicularBasis(axisDir)

        For i = 0 To segments - 1
            Dim theta = 2 * Math.PI * i / segments
            Dim thetaNext = 2 * Math.PI * (i + 1) / segments
            Dim midTheta = (theta + thetaNext) / 2
            Dim segLen = radius * (thetaNext - theta) * 1.15 ' slight overlap so segments visually connect

            Dim localPos = basis.u * (radius * Math.Cos(midTheta)) + basis.v * (radius * Math.Sin(midTheta))
            Dim tangent = basis.u * (-Math.Sin(midTheta)) + basis.v * Math.Cos(midTheta)

            Dim segPos = pos + localPos
            Dim t As New Transform3DGroup()
            t.Children.Add(New ScaleTransform3D(segLen, thickness, thickness))
            t.Children.Add(BuildAlignRotation(New Vector3D(1, 0, 0), tangent))
            t.Children.Add(New TranslateTransform3D(segPos.X, segPos.Y, segPos.Z))

            Dim gm As New GeometryModel3D(boxMesh, mat)
            gm.BackMaterial = mat
            gm.Transform = t
            group.Children.Add(gm)
            gizmoLookup(gm) = axis
        Next
    End Sub

    Private Sub UpdateGizmoModeButtons()
        Dim active As New SolidColorBrush(Color.FromRgb(&H8A, &H6A, &H2A))
        Dim inactive As New SolidColorBrush(Color.FromRgb(&H30, &H30, &H36))
        MoveModeButton.Background = If(gizmoMode = GizmoMode.Move, active, inactive)
        RotateModeButton.Background = If(gizmoMode = GizmoMode.Rotate, active, inactive)
        ScaleModeButton.Background = If(gizmoMode = GizmoMode.Scale, active, inactive)
    End Sub

    Private Sub MoveMode_Click(sender As Object, e As RoutedEventArgs)
        gizmoMode = GizmoMode.Move
        UpdateGizmoModeButtons()
        RebuildScene()
    End Sub

    Private Sub RotateMode_Click(sender As Object, e As RoutedEventArgs)
        gizmoMode = GizmoMode.Rotate
        UpdateGizmoModeButtons()
        RebuildScene()
    End Sub

    Private Sub ScaleMode_Click(sender As Object, e As RoutedEventArgs)
        gizmoMode = GizmoMode.Scale
        UpdateGizmoModeButtons()
        RebuildScene()
    End Sub

#End Region

#Region "Object list / selection"

    Private Function GetSelectedObjects() As List(Of SceneObject)
        Return ObjectListBox.SelectedItems.Cast(Of SceneObject)().ToList()
    End Function

    ''' <summary>Rebinds the object list. By default preserves the current primary selection;
    ''' pass selectItems to select a specific (possibly multi-item) set afterward instead.</summary>
    Private Sub RefreshObjectList(Optional selectItems As IEnumerable(Of SceneObject) = Nothing)
        Dim toSelect As New List(Of SceneObject)
        If selectItems IsNot Nothing Then
            toSelect.AddRange(selectItems.Where(Function(o) sceneObjects.Contains(o)))
        ElseIf selectedObject IsNot Nothing AndAlso sceneObjects.Contains(selectedObject) Then
            toSelect.Add(selectedObject)
        End If

        ObjectListBox.ItemsSource = Nothing
        ObjectListBox.DisplayMemberPath = "Name"
        ObjectListBox.ItemsSource = sceneObjects
        For Each item In toSelect
            ObjectListBox.SelectedItems.Add(item)
        Next

        selectedObject = toSelect.FirstOrDefault()
        UpdateInspector()
        RebuildScene()
    End Sub

    Private Sub SelectObject(obj As SceneObject)
        selectedObject = obj
        ObjectListBox.SelectedItem = obj
        UpdateInspector()
        RebuildScene()
    End Sub

    Private Sub ObjectListBox_SelectionChanged(sender As Object, e As Controls.SelectionChangedEventArgs)
        Dim obj = TryCast(ObjectListBox.SelectedItem, SceneObject)
        If ReferenceEquals(obj, selectedObject) Then Return
        selectedObject = obj
        UpdateInspector()
        RebuildScene()
    End Sub

#End Region

#Region "Add / duplicate / delete"

    Private Sub AddNewObject(kind As PrimitiveKind)
        Dim obj As New SceneObject()
        obj.Kind = kind
        Dim countOfKind = sceneObjects.Where(Function(o) o.Kind = kind).Count() + 1
        obj.Name = kind.ToString() & " " & countOfKind
        obj.PosX = (placeCounter Mod 5) * 1.3 - 2.6
        obj.PosZ = (placeCounter \ 5) * 1.3
        obj.PosY = 0.5
        obj.ColorHex = palette(rng.Next(palette.Length))
        placeCounter += 1

        sceneObjects.Add(obj)
        RefreshObjectList()
        SelectObject(obj)
    End Sub

    Private Sub AddCube_Click(sender As Object, e As RoutedEventArgs)
        AddNewObject(PrimitiveKind.Cube)
    End Sub

    Private Sub AddPyramid_Click(sender As Object, e As RoutedEventArgs)
        AddNewObject(PrimitiveKind.Pyramid)
    End Sub

    Private Sub AddSphere_Click(sender As Object, e As RoutedEventArgs)
        AddNewObject(PrimitiveKind.Sphere)
    End Sub

    Private Sub AddCylinder_Click(sender As Object, e As RoutedEventArgs)
        AddNewObject(PrimitiveKind.Cylinder)
    End Sub

    Private Sub AddCone_Click(sender As Object, e As RoutedEventArgs)
        AddNewObject(PrimitiveKind.Cone)
    End Sub

    Private Sub AddPlane_Click(sender As Object, e As RoutedEventArgs)
        AddNewObject(PrimitiveKind.Plane)
    End Sub

    Private Sub Duplicate_Click(sender As Object, e As RoutedEventArgs)
        If selectedObject Is Nothing Then Return
        Dim copy = TransformHelper.CloneSceneObject(selectedObject)
        copy.Name = selectedObject.Name & " copy"
        copy.PosX += 0.4
        copy.PosZ += 0.4
        sceneObjects.Add(copy)
        RefreshObjectList()
        SelectObject(copy)
    End Sub

    Private Sub Delete_Click(sender As Object, e As RoutedEventArgs)
        If selectedObject Is Nothing Then Return
        sceneObjects.Remove(selectedObject)
        selectedObject = Nothing
        RefreshObjectList()
        UpdateInspector()
    End Sub

    Private Sub Join_Click(sender As Object, e As RoutedEventArgs)
        Dim selected = GetSelectedObjects()
        If selected.Count < 2 Then
            MessageBox.Show("Select two or more objects in the list to join (Ctrl/Shift-click).", "Join", MessageBoxButton.OK, MessageBoxImage.Information)
            Return
        End If

        Dim cutters = selected.Where(Function(o) o.IsCutTool).ToList()
        If cutters.Count > 0 Then
            Dim solids = selected.Where(Function(o) Not o.IsCutTool).ToList()
            If solids.Count <> 1 Then
                MessageBox.Show("To cut, select exactly one solid object plus one or more cut tools.", "Cut", MessageBoxButton.OK, MessageBoxImage.Information)
                Return
            End If
            PerformCut(solids(0), cutters)
            Return
        End If

        Dim pivotX = selected.Average(Function(o) o.PosX)
        Dim pivotY = selected.Average(Function(o) o.PosY)
        Dim pivotZ = selected.Average(Function(o) o.PosZ)

        Dim compound As New SceneObject()
        compound.Name = "Joined (" & selected.Count & ")"
        compound.PosX = pivotX
        compound.PosY = pivotY
        compound.PosZ = pivotZ
        compound.ColorHex = selected(0).ColorHex
        compound.Parts = New List(Of SceneObject)

        For Each src In selected
            If src.Parts IsNot Nothing AndAlso src.Parts.Count > 0 Then
                For Each innerPart In src.Parts
                    Dim worldPart = TransformHelper.BakePartToWorld(innerPart, src)
                    compound.Parts.Add(RebasePart(worldPart, pivotX, pivotY, pivotZ))
                Next
            Else
                compound.Parts.Add(RebasePart(src, pivotX, pivotY, pivotZ))
            End If
        Next

        For Each src In selected
            sceneObjects.Remove(src)
        Next
        sceneObjects.Add(compound)
        RefreshObjectList()
        SelectObject(compound)
    End Sub

    ''' <summary>Subtracts each cutter's mesh from the solid's mesh (Tinkercad "hole" style),
    ''' replacing both with a single baked Custom-mesh object at the solid's original transform.</summary>
    Private Sub PerformCut(solid As SceneObject, cutters As List(Of SceneObject))
        If solid.Parts IsNot Nothing AndAlso solid.Parts.Count > 0 Then
            MessageBox.Show("The solid is a joined object - separate it first before cutting.", "Cut", MessageBoxButton.OK, MessageBoxImage.Information)
            Return
        End If
        If cutters.Any(Function(c) c.Parts IsNot Nothing AndAlso c.Parts.Count > 0) Then
            MessageBox.Show("A cut tool is a joined object - separate it first before cutting.", "Cut", MessageBoxButton.OK, MessageBoxImage.Information)
            Return
        End If

        Try
            Dim solidMatrix = TransformHelper.BuildTransform(solid).Value
            Dim solidInverse = solidMatrix
            solidInverse.Invert()

            Dim solidMesh = MeshBuilder.Resolve(solid.Kind, solid.CustomTriangles)
            Dim targetTris = MeshToTriangleList(solidMesh, Matrix3D.Identity)

            For Each cutter In cutters
                Dim cutterMesh = MeshBuilder.Resolve(cutter.Kind, cutter.CustomTriangles)
                Dim cutterMatrix = TransformHelper.BuildTransform(cutter).Value
                Dim cutterToSolidLocal = cutterMatrix * solidInverse
                Dim cutterTris = MeshToTriangleList(cutterMesh, cutterToSolidLocal)
                targetTris = CSG.Subtract(targetTris, cutterTris)
            Next

            If targetTris.Count = 0 Then
                MessageBox.Show("The cut tool doesn't overlap the solid (or removes it entirely) - nothing to keep.", "Cut", MessageBoxButton.OK, MessageBoxImage.Information)
                Return
            End If

            Dim result As New SceneObject()
            result.Kind = PrimitiveKind.Custom
            result.Name = solid.Name & " (cut)"
            result.PosX = solid.PosX : result.PosY = solid.PosY : result.PosZ = solid.PosZ
            result.RotX = solid.RotX : result.RotY = solid.RotY : result.RotZ = solid.RotZ
            result.ScaleX = solid.ScaleX : result.ScaleY = solid.ScaleY : result.ScaleZ = solid.ScaleZ
            result.ColorHex = solid.ColorHex
            result.CustomTriangles = TrianglesToFlatList(targetTris)

            sceneObjects.Remove(solid)
            For Each c In cutters
                sceneObjects.Remove(c)
            Next
            sceneObjects.Add(result)
            RefreshObjectList()
            SelectObject(result)
        Catch ex As Exception
            MessageBox.Show("Cut failed:" & Environment.NewLine & ex.Message, "Cut", MessageBoxButton.OK, MessageBoxImage.Error)
        End Try
    End Sub

    Private Function MeshToTriangleList(mesh As MeshGeometry3D, transform As Matrix3D) As List(Of Point3D())
        Dim result As New List(Of Point3D())
        Dim tris = mesh.TriangleIndices
        Dim i As Integer = 0
        While i <= tris.Count - 3
            Dim p0 = transform.Transform(mesh.Positions(tris(i)))
            Dim p1 = transform.Transform(mesh.Positions(tris(i + 1)))
            Dim p2 = transform.Transform(mesh.Positions(tris(i + 2)))
            result.Add({p0, p1, p2})
            i += 3
        End While
        Return result
    End Function

    Private Function TrianglesToFlatList(triangles As List(Of Point3D())) As List(Of Double)
        Dim result As New List(Of Double)
        For Each tri In triangles
            For Each p In tri
                result.Add(p.X)
                result.Add(p.Y)
                result.Add(p.Z)
            Next
        Next
        Return result
    End Function

    Private Sub UpdateCutToolButtonVisual()
        Dim active As New SolidColorBrush(Color.FromRgb(&H99, &H40, &H40))
        Dim inactive As New SolidColorBrush(Color.FromRgb(&H30, &H30, &H36))
        CutToolButton.Background = If(selectedObject IsNot Nothing AndAlso selectedObject.IsCutTool, active, inactive)
    End Sub

    Private Sub CutTool_Click(sender As Object, e As RoutedEventArgs)
        If selectedObject Is Nothing Then Return
        selectedObject.IsCutTool = Not selectedObject.IsCutTool
        UpdateCutToolButtonVisual()
        RebuildScene()
    End Sub

    Private Function RebasePart(src As SceneObject, pivotX As Double, pivotY As Double, pivotZ As Double) As SceneObject
        Return New SceneObject() With {
            .Kind = src.Kind,
            .Name = src.Name,
            .PosX = src.PosX - pivotX,
            .PosY = src.PosY - pivotY,
            .PosZ = src.PosZ - pivotZ,
            .RotX = src.RotX,
            .RotY = src.RotY,
            .RotZ = src.RotZ,
            .ScaleX = src.ScaleX,
            .ScaleY = src.ScaleY,
            .ScaleZ = src.ScaleZ,
            .ColorHex = src.ColorHex
        }
    End Function

    Private Sub Separate_Click(sender As Object, e As RoutedEventArgs)
        If selectedObject Is Nothing OrElse selectedObject.Parts Is Nothing OrElse selectedObject.Parts.Count = 0 Then
            MessageBox.Show("Select a joined object to separate.", "Separate", MessageBoxButton.OK, MessageBoxImage.Information)
            Return
        End If

        Dim compound = selectedObject
        Dim expanded = compound.Parts.Select(Function(p) TransformHelper.BakePartToWorld(p, compound)).ToList()

        sceneObjects.Remove(compound)
        sceneObjects.AddRange(expanded)
        RefreshObjectList(expanded)
    End Sub

#End Region

#Region "Inspector panel"

    Private Sub BuildPalettePanel()
        For Each hexValue In palette
            Dim c = CType(ColorConverter.ConvertFromString(hexValue), Color)
            Dim swatch As New Controls.Border()
            swatch.Background = New SolidColorBrush(c)
            swatch.Width = 20
            swatch.Height = 20
            swatch.BorderBrush = Brushes.Black
            swatch.BorderThickness = New Thickness(1)

            Dim btn As New Controls.Button()
            btn.Content = swatch
            btn.Padding = New Thickness(0)
            btn.Width = 24
            btn.Height = 24
            btn.Margin = New Thickness(2)
            btn.Tag = hexValue
            AddHandler btn.Click, AddressOf PaletteButton_Click
            PalettePanel.Children.Add(btn)
        Next
    End Sub

    Private Sub PaletteButton_Click(sender As Object, e As RoutedEventArgs)
        If selectedObject Is Nothing Then Return
        selectedObject.ColorHex = CStr(DirectCast(sender, Controls.Button).Tag)
        UpdateInspector()
        RebuildScene()
    End Sub

    Private Sub UpdateInspector()
        If selectedObject Is Nothing Then
            InspectorPanel.IsEnabled = False
            PosXBox.Text = "" : PosYBox.Text = "" : PosZBox.Text = ""
            RotXBox.Text = "" : RotYBox.Text = "" : RotZBox.Text = ""
            ScaleXBox.Text = "" : ScaleYBox.Text = "" : ScaleZBox.Text = ""
            ColorHexBox.Text = ""
            NameBox.Text = ""
            UpdateCutToolButtonVisual()
            Return
        End If

        InspectorPanel.IsEnabled = True
        Dim inv = CultureInfo.InvariantCulture
        PosXBox.Text = selectedObject.PosX.ToString("0.###", inv)
        PosYBox.Text = selectedObject.PosY.ToString("0.###", inv)
        PosZBox.Text = selectedObject.PosZ.ToString("0.###", inv)
        RotXBox.Text = selectedObject.RotX.ToString("0.###", inv)
        RotYBox.Text = selectedObject.RotY.ToString("0.###", inv)
        RotZBox.Text = selectedObject.RotZ.ToString("0.###", inv)
        ScaleXBox.Text = selectedObject.ScaleX.ToString("0.###", inv)
        ScaleYBox.Text = selectedObject.ScaleY.ToString("0.###", inv)
        ScaleZBox.Text = selectedObject.ScaleZ.ToString("0.###", inv)
        ColorHexBox.Text = selectedObject.ColorHex
        NameBox.Text = selectedObject.Name
        UpdateCutToolButtonVisual()
    End Sub

    Private Function ParseOr(box As Controls.TextBox, fallback As Double, inv As CultureInfo) As Double
        Dim v As Double
        If Double.TryParse(box.Text, NumberStyles.Float, inv, v) Then Return v
        Return fallback
    End Function

    Private Sub ApplyTransformFromUI()
        If selectedObject Is Nothing Then Return
        Dim inv = CultureInfo.InvariantCulture
        selectedObject.PosX = ParseOr(PosXBox, selectedObject.PosX, inv)
        selectedObject.PosY = ParseOr(PosYBox, selectedObject.PosY, inv)
        selectedObject.PosZ = ParseOr(PosZBox, selectedObject.PosZ, inv)
        selectedObject.RotX = ParseOr(RotXBox, selectedObject.RotX, inv)
        selectedObject.RotY = ParseOr(RotYBox, selectedObject.RotY, inv)
        selectedObject.RotZ = ParseOr(RotZBox, selectedObject.RotZ, inv)
        selectedObject.ScaleX = Math.Max(0.01, ParseOr(ScaleXBox, selectedObject.ScaleX, inv))
        selectedObject.ScaleY = Math.Max(0.01, ParseOr(ScaleYBox, selectedObject.ScaleY, inv))
        selectedObject.ScaleZ = Math.Max(0.01, ParseOr(ScaleZBox, selectedObject.ScaleZ, inv))
        UpdateInspector()
        RebuildScene()
    End Sub

    Private Sub TransformBox_LostFocus(sender As Object, e As RoutedEventArgs)
        ApplyTransformFromUI()
    End Sub

    Private Sub TransformBox_KeyDown(sender As Object, e As KeyEventArgs)
        If e.Key = Key.Enter Then
            ApplyTransformFromUI()
            e.Handled = True
        End If
    End Sub

    Private Sub ApplyColorFromUI()
        If selectedObject Is Nothing Then Return
        Dim text = ColorHexBox.Text.Trim()
        If Not text.StartsWith("#") Then text = "#" & text
        Try
            Dim validated As Color = CType(ColorConverter.ConvertFromString(text), Color)
            selectedObject.ColorHex = text
        Catch
            ' invalid hex, ignore and revert display below
        End Try
        UpdateInspector()
        RebuildScene()
    End Sub

    Private Sub ColorHexBox_LostFocus(sender As Object, e As RoutedEventArgs)
        ApplyColorFromUI()
    End Sub

    Private Sub ColorHexBox_KeyDown(sender As Object, e As KeyEventArgs)
        If e.Key = Key.Enter Then
            ApplyColorFromUI()
            e.Handled = True
        End If
    End Sub

    Private Sub ApplyNameFromUI()
        If selectedObject Is Nothing Then Return
        Dim newName = NameBox.Text.Trim()
        If newName = "" Then newName = selectedObject.Kind.ToString()
        selectedObject.Name = newName
        RefreshObjectList()
    End Sub

    Private Sub NameBox_LostFocus(sender As Object, e As RoutedEventArgs)
        ApplyNameFromUI()
    End Sub

    Private Sub NameBox_KeyDown(sender As Object, e As KeyEventArgs)
        If e.Key = Key.Enter Then
            ApplyNameFromUI()
            e.Handled = True
        End If
    End Sub

#End Region

#Region "Camera"

    Private Sub UpdateCamera()
        Dim yawRad = cameraYaw * Math.PI / 180
        Dim pitchRad = cameraPitch * Math.PI / 180
        Dim x = cameraDistance * Math.Cos(pitchRad) * Math.Sin(yawRad)
        Dim y = cameraDistance * Math.Sin(pitchRad)
        Dim z = cameraDistance * Math.Cos(pitchRad) * Math.Cos(yawRad)

        Dim pos As New Point3D(cameraTarget.X + x, cameraTarget.Y + y, cameraTarget.Z + z)
        Dim look As New Vector3D(cameraTarget.X - pos.X, cameraTarget.Y - pos.Y, cameraTarget.Z - pos.Z)
        Dim up As New Vector3D(0, 1, 0)

        MainCamera.Position = pos
        MainCamera.LookDirection = look
        MainCamera.UpDirection = up

        orthoCamera.Position = pos
        orthoCamera.LookDirection = look
        orthoCamera.UpDirection = up
        ' Roughly matches what the perspective camera frames at this same distance, so
        ' toggling projection mode doesn't visibly jump the view.
        orthoCamera.Width = cameraDistance * 0.83

        Viewport3D1.Camera = If(isOrthographic, CType(orthoCamera, Camera), CType(MainCamera, Camera))

        UpdateViewCubeCamera()
    End Sub

    Private Sub Viewport_MouseDown(sender As Object, e As MouseButtonEventArgs)
        lastMousePos = e.GetPosition(Viewport3D1)

        Select Case e.ChangedButton
            Case MouseButton.Right
                isOrbiting = True
                DirectCast(sender, UIElement).CaptureMouse()
            Case MouseButton.Middle
                isPanning = True
                DirectCast(sender, UIElement).CaptureMouse()
            Case MouseButton.Left
                PerformHitTest(lastMousePos)
                If isDraggingGizmo Then
                    DirectCast(sender, UIElement).CaptureMouse()
                End If
        End Select
    End Sub

    Private Sub Viewport_MouseUp(sender As Object, e As MouseButtonEventArgs)
        isOrbiting = False
        isPanning = False
        isDraggingGizmo = False
        DirectCast(sender, UIElement).ReleaseMouseCapture()
    End Sub

    Private Sub Viewport_MouseLeave(sender As Object, e As MouseEventArgs)
    End Sub

    Private Function GetCameraRightUp() As (right As Vector3D, up As Vector3D)
        Dim look = MainCamera.LookDirection
        look.Normalize()
        Dim right = Vector3D.CrossProduct(look, MainCamera.UpDirection)
        right.Normalize()
        Dim camUp = Vector3D.CrossProduct(right, look)
        camUp.Normalize()
        Return (right, camUp)
    End Function

    Private Sub Viewport_MouseMove(sender As Object, e As MouseEventArgs)
        Dim pos = e.GetPosition(Viewport3D1)
        Dim dx = pos.X - lastMousePos.X
        Dim dy = pos.Y - lastMousePos.Y

        If isOrbiting Then
            cameraYaw -= dx * 0.4
            cameraPitch += dy * 0.4
            cameraPitch = Math.Max(-89, Math.Min(89, cameraPitch))
            UpdateCamera()
        ElseIf isPanning Then
            Dim basis = GetCameraRightUp()
            Dim panScale = cameraDistance * 0.0015
            cameraTarget = New Point3D(
                cameraTarget.X - basis.right.X * dx * panScale + basis.up.X * dy * panScale,
                cameraTarget.Y - basis.right.Y * dx * panScale + basis.up.Y * dy * panScale,
                cameraTarget.Z - basis.right.Z * dx * panScale + basis.up.Z * dy * panScale)
            UpdateCamera()
        ElseIf isDraggingGizmo Then
            If gizmoMode = GizmoMode.Rotate Then
                ApplyRotateDrag(pos)
            Else
                Dim totalDx = pos.X - dragStartMouse.X
                Dim totalDy = pos.Y - dragStartMouse.Y
                ApplyGizmoDrag(ComputeAxisPixelDelta(dragAxisDirWorld, totalDx, totalDy))
            End If
        End If

        lastMousePos = pos
    End Sub

    Private Sub Viewport_MouseWheel(sender As Object, e As MouseWheelEventArgs)
        Dim factor = 1.0 - e.Delta / 1200.0
        cameraDistance = Math.Max(1.0, Math.Min(60.0, cameraDistance * factor))
        UpdateCamera()
    End Sub

    ''' <summary>Projects a world axis direction onto the screen and returns how many of the given
    ''' screen-space mouse-delta pixels fall "along" that axis's on-screen direction.</summary>
    Private Function ComputeAxisPixelDelta(axisDirWorld As Vector3D, dx As Double, dy As Double) As Double
        Dim basis = GetCameraRightUp()

        Dim screenX = Vector3D.DotProduct(axisDirWorld, basis.right)
        Dim screenY = -Vector3D.DotProduct(axisDirWorld, basis.up)
        Dim len = Math.Sqrt(screenX * screenX + screenY * screenY)
        If len < 0.0001 Then Return 0

        screenX /= len
        screenY /= len
        Return dx * screenX + dy * screenY
    End Function

    Private Sub ApplyGizmoDrag(pixelsAlongAxis As Double)
        If selectedObject Is Nothing Then Return

        Select Case gizmoMode
            Case GizmoMode.Move
                Dim worldDelta = pixelsAlongAxis * cameraDistance * 0.0015
                Dim newPos = dragStartPos + dragAxisDirWorld * worldDelta
                If snapEnabled Then newPos = ApplySnapping(selectedObject, newPos)
                selectedObject.PosX = newPos.X
                selectedObject.PosY = newPos.Y
                selectedObject.PosZ = newPos.Z
            Case GizmoMode.Scale
                ' Scale is applied before rotation in the object's transform, so ScaleX/Y/Z are
                ' already in the object's own local frame regardless of its current orientation -
                ' only the drag-direction sensing needs the rotated axis, not which field changes.
                Dim factor = Math.Max(0.02, 1 + pixelsAlongAxis * 0.006)
                Select Case dragAxis
                    Case "X"c : selectedObject.ScaleX = Math.Max(0.02, dragStartScale.X * factor)
                    Case "Y"c : selectedObject.ScaleY = Math.Max(0.02, dragStartScale.Y * factor)
                    Case "Z"c : selectedObject.ScaleZ = Math.Max(0.02, dragStartScale.Z * factor)
                End Select
        End Select

        UpdateInspector()
        RebuildScene()
    End Sub

    ''' <summary>Projects a world-space point to 2D pixel coordinates within Viewport3D1.</summary>
    Private Function ProjectToScreen(worldPoint As Point3D) As Point?
        Try
            Dim gt = TryCast(SceneVisual.TransformToAncestor(Viewport3D1), GeneralTransform3DTo2D)
            If gt Is Nothing Then Return Nothing
            Return gt.Transform(worldPoint)
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>Rotates by the angle the mouse has swept around the object's on-screen position
    ''' since the drag started - dragging in a circle around the object, like a trackball ring,
    ''' rather than the linear axis-projection trick used for Move/Scale. The rotation is composed
    ''' properly around the object's actual local axis at drag-start (dragAxisDirWorld) on top of
    ''' its starting orientation, then decomposed back to Euler angles - so it turns exactly around
    ''' the ring you grabbed instead of getting tangled up with the object's other rotations.</summary>
    Private Sub ApplyRotateDrag(currentMouse As Point)
        If selectedObject Is Nothing Then Return

        Dim dx = currentMouse.X - dragCenterScreen.X
        Dim dy = currentMouse.Y - dragCenterScreen.Y
        If dx = 0 AndAlso dy = 0 Then Return

        Dim currentAngle = Math.Atan2(dy, dx)
        ' Screen Y grows downward, which mirrors the angle relative to WPF's right-hand-rule
        ' AxisAngleRotation3D - negate so dragging clockwise around the ring (as seen on screen)
        ' turns the object clockwise instead of the reverse.
        Dim deltaDeg = -(currentAngle - dragStartAngleRad) * 180 / Math.PI

        Dim startMatrix = TransformHelper.BuildRotationMatrix(dragStartRot.X, dragStartRot.Y, dragStartRot.Z)
        Dim incrementMatrix = New RotateTransform3D(New AxisAngleRotation3D(dragAxisDirWorld, deltaDeg)).Value
        Dim combined = startMatrix * incrementMatrix
        Dim newEuler = TransformHelper.DecomposeRotationDegrees(combined)

        selectedObject.RotX = newEuler.RotX
        selectedObject.RotY = newEuler.RotY
        selectedObject.RotZ = newEuler.RotZ

        UpdateInspector()
        RebuildScene()
    End Sub

    Private Sub StartGizmoDrag(axis As Char)
        isDraggingGizmo = True
        dragAxis = axis
        dragStartMouse = lastMousePos
        dragStartPos = New Point3D(selectedObject.PosX, selectedObject.PosY, selectedObject.PosZ)
        dragStartRot = New Point3D(selectedObject.RotX, selectedObject.RotY, selectedObject.RotZ)
        dragStartScale = New Point3D(selectedObject.ScaleX, selectedObject.ScaleY, selectedObject.ScaleZ)

        ' The axis direction is fixed for the whole drag (from the object's orientation at the
        ' moment you grabbed the handle) so a single drag gesture rotates/moves consistently.
        Dim rot = TransformHelper.BuildRotationMatrix(selectedObject)
        Dim localUnit As New Vector3D(If(axis = "X"c, 1, 0), If(axis = "Y"c, 1, 0), If(axis = "Z"c, 1, 0))
        dragAxisDirWorld = rot.Transform(localUnit)
        dragAxisDirWorld.Normalize()

        If gizmoMode = GizmoMode.Rotate Then
            Dim objPos As New Point3D(selectedObject.PosX, selectedObject.PosY, selectedObject.PosZ)
            Dim screenPos = ProjectToScreen(objPos)
            dragCenterScreen = If(screenPos.HasValue, screenPos.Value, dragStartMouse)
            Dim dx0 = dragStartMouse.X - dragCenterScreen.X
            Dim dy0 = dragStartMouse.Y - dragCenterScreen.Y
            dragStartAngleRad = Math.Atan2(dy0, dx0)
        End If
    End Sub

    Private Sub PerformHitTest(pt As Point)
        Dim result = VisualTreeHelper.HitTest(Viewport3D1, pt)
        Dim rayHit = TryCast(result, RayMeshGeometry3DHitTestResult)

        If rayHit IsNot Nothing Then
            Dim geo = TryCast(rayHit.ModelHit, GeometryModel3D)
            If geo IsNot Nothing Then
                If selectedObject IsNot Nothing AndAlso gizmoLookup.ContainsKey(geo) Then
                    StartGizmoDrag(gizmoLookup(geo))
                    Return
                End If
                If modelLookup.ContainsKey(geo) Then
                    SelectObject(modelLookup(geo))
                    Return
                End If
            End If
        End If

        SelectObject(Nothing)
    End Sub

    Private Sub UpdateOrthoButtonVisual()
        Dim active As New SolidColorBrush(Color.FromRgb(&H8A, &H6A, &H2A))
        Dim inactive As New SolidColorBrush(Color.FromRgb(&H30, &H30, &H36))
        OrthoButton.Background = If(isOrthographic, active, inactive)
    End Sub

    Private Sub OrthoButton_Click(sender As Object, e As RoutedEventArgs)
        isOrthographic = Not isOrthographic
        UpdateOrthoButtonVisual()
        UpdateCamera()
    End Sub

    Private Sub UpdateSnapButtonVisual()
        Dim active As New SolidColorBrush(Color.FromRgb(&H8A, &H6A, &H2A))
        Dim inactive As New SolidColorBrush(Color.FromRgb(&H30, &H30, &H36))
        SnapButton.Background = If(snapEnabled, active, inactive)
    End Sub

    Private Sub SnapButton_Click(sender As Object, e As RoutedEventArgs)
        snapEnabled = Not snapEnabled
        UpdateSnapButtonVisual()
    End Sub

    ''' <summary>World-space half-extents of an object's bounding box (ignores rotation's effect
    ''' on the axis-aligned extents, same approximation already used for gizmo sizing).</summary>
    Private Function GetWorldHalfExtents(obj As SceneObject) As Vector3D
        Dim b = ComputeTrueLocalBounds(obj)
        Return New Vector3D(b.size.X * obj.ScaleX / 2, b.size.Y * obj.ScaleY / 2, b.size.Z * obj.ScaleZ / 2)
    End Function

    ''' <summary>Snaps one axis coordinate to the nearest edge/center alignment with any other
    ''' object's corresponding edge/center, within threshold; otherwise leaves it unchanged.</summary>
    Private Function SnapOneAxis(draggedCenter As Double, draggedHalf As Double, otherCenters As List(Of Double), otherHalves As List(Of Double), threshold As Double) As Double
        Dim best = threshold
        Dim result = draggedCenter
        Dim draggedEdges = New Double() {draggedCenter - draggedHalf, draggedCenter, draggedCenter + draggedHalf}

        For i = 0 To otherCenters.Count - 1
            Dim otherEdges = New Double() {otherCenters(i) - otherHalves(i), otherCenters(i), otherCenters(i) + otherHalves(i)}
            For Each de In draggedEdges
                For Each oe In otherEdges
                    Dim diff = oe - de
                    If Math.Abs(diff) < best Then
                        best = Math.Abs(diff)
                        result = draggedCenter + diff
                    End If
                Next
            Next
        Next

        Return result
    End Function

    ''' <summary>Snaps a candidate Move position so the dragged object's edges/center align with
    ''' any nearby object's edges/center, independently per axis - like Tinkercad's object snap.</summary>
    Private Function ApplySnapping(obj As SceneObject, candidatePos As Point3D) As Point3D
        Const threshold As Double = 0.18
        Dim others = sceneObjects.Where(Function(o) Not ReferenceEquals(o, obj)).ToList()
        If others.Count = 0 Then Return candidatePos

        Dim half = GetWorldHalfExtents(obj)
        Dim otherHalves = others.Select(Function(o) GetWorldHalfExtents(o)).ToList()

        Dim newX = SnapOneAxis(candidatePos.X, half.X, others.Select(Function(o) o.PosX).ToList(), otherHalves.Select(Function(v) v.X).ToList(), threshold)
        Dim newY = SnapOneAxis(candidatePos.Y, half.Y, others.Select(Function(o) o.PosY).ToList(), otherHalves.Select(Function(v) v.Y).ToList(), threshold)
        Dim newZ = SnapOneAxis(candidatePos.Z, half.Z, others.Select(Function(o) o.PosZ).ToList(), otherHalves.Select(Function(v) v.Z).ToList(), threshold)
        Return New Point3D(newX, newY, newZ)
    End Function

#End Region

#Region "View cube"

    Private Sub UpdateViewCubeCamera()
        Const cubeDistance As Double = 3.2
        Dim yawRad = cameraYaw * Math.PI / 180
        Dim pitchRad = cameraPitch * Math.PI / 180
        Dim x = cubeDistance * Math.Cos(pitchRad) * Math.Sin(yawRad)
        Dim y = cubeDistance * Math.Sin(pitchRad)
        Dim z = cubeDistance * Math.Cos(pitchRad) * Math.Cos(yawRad)

        ViewCubeCamera.Position = New Point3D(x, y, z)
        ViewCubeCamera.LookDirection = New Vector3D(-x, -y, -z)
        ViewCubeCamera.UpDirection = New Vector3D(0, 1, 0)
    End Sub

    Private Sub BuildViewCube()
        Dim group As New Model3DGroup()
        group.Children.Add(New AmbientLight(Colors.White))

        ' Each face maps to the camera yaw/pitch that looks straight at it.
        AddViewCubeFace(group, "TOP",
            New Point3D(-0.5, 0.5, 0.5), New Point3D(0.5, 0.5, 0.5), New Point3D(0.5, 0.5, -0.5), New Point3D(-0.5, 0.5, -0.5),
            0, 89)
        AddViewCubeFace(group, "BOTTOM",
            New Point3D(-0.5, -0.5, -0.5), New Point3D(0.5, -0.5, -0.5), New Point3D(0.5, -0.5, 0.5), New Point3D(-0.5, -0.5, 0.5),
            0, -89)
        AddViewCubeFace(group, "FRONT",
            New Point3D(-0.5, -0.5, 0.5), New Point3D(0.5, -0.5, 0.5), New Point3D(0.5, 0.5, 0.5), New Point3D(-0.5, 0.5, 0.5),
            0, 0)
        AddViewCubeFace(group, "BACK",
            New Point3D(0.5, -0.5, -0.5), New Point3D(-0.5, -0.5, -0.5), New Point3D(-0.5, 0.5, -0.5), New Point3D(0.5, 0.5, -0.5),
            180, 0)
        AddViewCubeFace(group, "RIGHT",
            New Point3D(0.5, -0.5, 0.5), New Point3D(0.5, -0.5, -0.5), New Point3D(0.5, 0.5, -0.5), New Point3D(0.5, 0.5, 0.5),
            90, 0)
        AddViewCubeFace(group, "LEFT",
            New Point3D(-0.5, -0.5, -0.5), New Point3D(-0.5, -0.5, 0.5), New Point3D(-0.5, 0.5, 0.5), New Point3D(-0.5, 0.5, -0.5),
            -90, 0)

        ViewCubeVisual.Content = group
    End Sub

    Private Sub AddViewCubeFace(group As Model3DGroup, label As String, p0 As Point3D, p1 As Point3D, p2 As Point3D, p3 As Point3D, yaw As Double, pitch As Double)
        Dim mesh As New MeshGeometry3D()
        mesh.Positions.Add(p0) : mesh.Positions.Add(p1) : mesh.Positions.Add(p2) : mesh.Positions.Add(p3)
        mesh.TextureCoordinates.Add(New Point(0, 1))
        mesh.TextureCoordinates.Add(New Point(1, 1))
        mesh.TextureCoordinates.Add(New Point(1, 0))
        mesh.TextureCoordinates.Add(New Point(0, 0))
        mesh.TriangleIndices.Add(0) : mesh.TriangleIndices.Add(1) : mesh.TriangleIndices.Add(2)
        mesh.TriangleIndices.Add(0) : mesh.TriangleIndices.Add(2) : mesh.TriangleIndices.Add(3)

        Dim mat As New DiffuseMaterial(BuildFaceBrush(label))
        Dim gm As New GeometryModel3D(mesh, mat)
        group.Children.Add(gm)
        viewCubeLookup(gm) = (yaw, pitch)
    End Sub

    Private Function BuildFaceBrush(label As String) As VisualBrush
        Dim tb As New Controls.TextBlock()
        tb.Text = label
        tb.Foreground = Brushes.White
        tb.FontSize = 13
        tb.FontWeight = FontWeights.Bold
        tb.HorizontalAlignment = HorizontalAlignment.Center
        tb.VerticalAlignment = VerticalAlignment.Center

        Dim border As New Controls.Border()
        border.Width = 64
        border.Height = 64
        border.Background = New SolidColorBrush(Color.FromRgb(&H45, &H45, &H45))
        border.BorderBrush = New SolidColorBrush(Color.FromRgb(&H70, &H70, &H70))
        border.BorderThickness = New Thickness(1)
        border.Child = tb

        border.Measure(New Size(64, 64))
        border.Arrange(New Rect(0, 0, 64, 64))

        Return New VisualBrush(border)
    End Function

    Private Sub ViewCube_MouseDown(sender As Object, e As MouseButtonEventArgs)
        Dim pt = e.GetPosition(ViewCubeViewport)
        Dim result = VisualTreeHelper.HitTest(ViewCubeViewport, pt)
        Dim rayHit = TryCast(result, RayMeshGeometry3DHitTestResult)

        If rayHit IsNot Nothing Then
            Dim geo = TryCast(rayHit.ModelHit, GeometryModel3D)
            If geo IsNot Nothing AndAlso viewCubeLookup.ContainsKey(geo) Then
                Dim face = viewCubeLookup(geo)
                cameraYaw = face.Yaw
                cameraPitch = face.Pitch
                isOrthographic = True
                UpdateOrthoButtonVisual()
                UpdateCamera()
            End If
        End If

        e.Handled = True
    End Sub

#End Region

#Region "File menu"

    Private Sub NewScene_Click(sender As Object, e As RoutedEventArgs)
        If sceneObjects.Count > 0 Then
            Dim result = MessageBox.Show("Clear the current scene?", "New Scene", MessageBoxButton.YesNo, MessageBoxImage.Question)
            If result <> MessageBoxResult.Yes Then Return
        End If
        sceneObjects.Clear()
        selectedObject = Nothing
        placeCounter = 0
        RefreshObjectList()
        UpdateInspector()
    End Sub

    Private Sub OpenProject_Click(sender As Object, e As RoutedEventArgs)
        Dim dlg As New Microsoft.Win32.OpenFileDialog()
        dlg.Filter = "Low Poly Modeler Project (*.lpm.json)|*.lpm.json|JSON files (*.json)|*.json|All files (*.*)|*.*"
        If dlg.ShowDialog() = True Then
            Try
                Dim json = File.ReadAllText(dlg.FileName)
                Dim loaded = JsonSerializer.Deserialize(Of List(Of SceneObject))(json)
                sceneObjects = If(loaded, New List(Of SceneObject)())
                selectedObject = Nothing
                placeCounter = sceneObjects.Count
                RefreshObjectList()
                UpdateInspector()
            Catch ex As Exception
                MessageBox.Show("Could not load project:" & Environment.NewLine & ex.Message, "Load Failed", MessageBoxButton.OK, MessageBoxImage.Error)
            End Try
        End If
    End Sub

    Private Sub SaveProject_Click(sender As Object, e As RoutedEventArgs)
        Dim dlg As New Microsoft.Win32.SaveFileDialog()
        dlg.Filter = "Low Poly Modeler Project (*.lpm.json)|*.lpm.json|All files (*.*)|*.*"
        dlg.FileName = "scene.lpm.json"
        If dlg.ShowDialog() = True Then
            Try
                Dim options As New JsonSerializerOptions With {.WriteIndented = True}
                File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(sceneObjects, options))
            Catch ex As Exception
                MessageBox.Show("Could not save project:" & Environment.NewLine & ex.Message, "Save Failed", MessageBoxButton.OK, MessageBoxImage.Error)
            End Try
        End If
    End Sub

    Private Sub ExportObj_Click(sender As Object, e As RoutedEventArgs)
        If sceneObjects.Count = 0 Then
            MessageBox.Show("Nothing to export yet.", "Export OBJ", MessageBoxButton.OK, MessageBoxImage.Information)
            Return
        End If
        Dim dlg As New Microsoft.Win32.SaveFileDialog()
        dlg.Filter = "Wavefront OBJ (*.obj)|*.obj"
        dlg.FileName = "scene.obj"
        If dlg.ShowDialog() = True Then
            Try
                Exporters.ExportObj(sceneObjects, dlg.FileName)
            Catch ex As Exception
                MessageBox.Show("Export failed:" & Environment.NewLine & ex.Message, "Export OBJ", MessageBoxButton.OK, MessageBoxImage.Error)
            End Try
        End If
    End Sub

    Private Sub ExportStl_Click(sender As Object, e As RoutedEventArgs)
        If sceneObjects.Count = 0 Then
            MessageBox.Show("Nothing to export yet.", "Export STL", MessageBoxButton.OK, MessageBoxImage.Information)
            Return
        End If
        Dim dlg As New Microsoft.Win32.SaveFileDialog()
        dlg.Filter = "STL files (*.stl)|*.stl"
        dlg.FileName = "scene.stl"
        If dlg.ShowDialog() = True Then
            Try
                Exporters.ExportStl(sceneObjects, dlg.FileName)
            Catch ex As Exception
                MessageBox.Show("Export failed:" & Environment.NewLine & ex.Message, "Export STL", MessageBoxButton.OK, MessageBoxImage.Error)
            End Try
        End If
    End Sub

    ''' <summary>Captures the current view as a PNG, showing only the model - no origin axis
    ''' indicator, selection outline, or transform gizmo.</summary>
    Private Sub Render_Click(sender As Object, e As RoutedEventArgs)
        If sceneObjects.Count = 0 Then
            MessageBox.Show("Nothing to render yet.", "Render", MessageBoxButton.OK, MessageBoxImage.Information)
            Return
        End If

        Dim dlg As New Microsoft.Win32.SaveFileDialog()
        dlg.Filter = "PNG Image (*.png)|*.png"
        dlg.FileName = "render.png"
        If dlg.ShowDialog() <> True Then Return

        Dim previousContent = SceneVisual.Content
        Try
            Dim cleanGroup As New Model3DGroup()
            For Each obj In sceneObjects
                AddObjectModels(cleanGroup, obj)
            Next
            SceneVisual.Content = cleanGroup

            Dim w = Math.Max(1, CInt(ViewportBorder.ActualWidth))
            Dim h = Math.Max(1, CInt(ViewportBorder.ActualHeight))
            Const scale As Integer = 2
            Dim rtb As New RenderTargetBitmap(w * scale, h * scale, 96.0 * scale, 96.0 * scale, PixelFormats.Pbgra32)
            rtb.Render(ViewportBorder)

            Dim encoder As New PngBitmapEncoder()
            encoder.Frames.Add(BitmapFrame.Create(rtb))
            Using fs As New FileStream(dlg.FileName, FileMode.Create)
                encoder.Save(fs)
            End Using
        Catch ex As Exception
            MessageBox.Show("Render failed:" & Environment.NewLine & ex.Message, "Render", MessageBoxButton.OK, MessageBoxImage.Error)
        Finally
            SceneVisual.Content = previousContent
        End Try
    End Sub

    Private Sub Exit_Click(sender As Object, e As RoutedEventArgs)
        Me.Close()
    End Sub

#End Region

End Class

End Namespace
