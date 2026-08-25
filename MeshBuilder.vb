Imports System.Windows.Media.Media3D

''' <summary>
''' Procedurally builds flat-shaded, low-poly unit meshes (roughly -0.5..0.5 in each axis)
''' for each primitive kind. Flat shading is achieved by duplicating vertices per triangle
''' and assigning each copy the triangle's face normal, since MeshGeometry3D only supports
''' one normal per vertex index.
''' </summary>
Public Module MeshBuilder

    Private Sub AddTriangle(mesh As MeshGeometry3D, p0 As Point3D, p1 As Point3D, p2 As Point3D)
        Dim u As New Vector3D(p1.X - p0.X, p1.Y - p0.Y, p1.Z - p0.Z)
        Dim v As New Vector3D(p2.X - p0.X, p2.Y - p0.Y, p2.Z - p0.Z)
        Dim n As Vector3D = Vector3D.CrossProduct(u, v)
        If n.Length > 0 Then n.Normalize()

        Dim baseIndex As Integer = mesh.Positions.Count
        mesh.Positions.Add(p0)
        mesh.Positions.Add(p1)
        mesh.Positions.Add(p2)
        mesh.Normals.Add(n)
        mesh.Normals.Add(n)
        mesh.Normals.Add(n)
        mesh.TriangleIndices.Add(baseIndex)
        mesh.TriangleIndices.Add(baseIndex + 1)
        mesh.TriangleIndices.Add(baseIndex + 2)
    End Sub

    Private Sub AddQuad(mesh As MeshGeometry3D, p0 As Point3D, p1 As Point3D, p2 As Point3D, p3 As Point3D)
        AddTriangle(mesh, p0, p1, p2)
        AddTriangle(mesh, p0, p2, p3)
    End Sub

    Public Function Build(kind As PrimitiveKind) As MeshGeometry3D
        Select Case kind
            Case PrimitiveKind.Cube
                Return BuildCube()
            Case PrimitiveKind.Pyramid
                Return BuildPyramid()
            Case PrimitiveKind.Sphere
                Return BuildSphere()
            Case PrimitiveKind.Cylinder
                Return BuildCylinder()
            Case PrimitiveKind.Cone
                Return BuildCone()
            Case PrimitiveKind.Plane
                Return BuildPlane()
            Case PrimitiveKind.Custom
                Return New MeshGeometry3D() ' resolved via Resolve(), which needs the triangle data
            Case Else
                Return BuildCube()
        End Select
    End Function

    ''' <summary>Builds a mesh from flattened triangle data (9 doubles per triangle), e.g. the
    ''' result of a boolean cut. Recomputes flat face normals same as the procedural builders.</summary>
    Public Function BuildCustom(triangles As List(Of Double)) As MeshGeometry3D
        Dim mesh As New MeshGeometry3D()
        If triangles Is Nothing Then Return mesh

        Dim i As Integer = 0
        While i <= triangles.Count - 9
            Dim p0 As New Point3D(triangles(i), triangles(i + 1), triangles(i + 2))
            Dim p1 As New Point3D(triangles(i + 3), triangles(i + 4), triangles(i + 5))
            Dim p2 As New Point3D(triangles(i + 6), triangles(i + 7), triangles(i + 8))
            AddTriangle(mesh, p0, p1, p2)
            i += 9
        End While

        Return mesh
    End Function

    ''' <summary>Resolves a piece's mesh: procedural for ordinary kinds, baked triangles for Custom.</summary>
    Public Function Resolve(kind As PrimitiveKind, customTriangles As List(Of Double)) As MeshGeometry3D
        If kind = PrimitiveKind.Custom Then Return BuildCustom(customTriangles)
        Return Build(kind)
    End Function

    Private Function BuildCube() As MeshGeometry3D
        Dim mesh As New MeshGeometry3D()
        Const h As Double = 0.5

        Dim v000 As New Point3D(-h, -h, -h)
        Dim v100 As New Point3D(h, -h, -h)
        Dim v110 As New Point3D(h, h, -h)
        Dim v010 As New Point3D(-h, h, -h)
        Dim v001 As New Point3D(-h, -h, h)
        Dim v101 As New Point3D(h, -h, h)
        Dim v111 As New Point3D(h, h, h)
        Dim v011 As New Point3D(-h, h, h)

        AddQuad(mesh, v001, v101, v111, v011) ' front  (+z)
        AddQuad(mesh, v100, v000, v010, v110) ' back   (-z)
        AddQuad(mesh, v101, v100, v110, v111) ' right  (+x)
        AddQuad(mesh, v000, v001, v011, v010) ' left   (-x)
        AddQuad(mesh, v011, v111, v110, v010) ' top    (+y)
        AddQuad(mesh, v000, v100, v101, v001) ' bottom (-y)

        Return mesh
    End Function

    Private Function BuildPyramid() As MeshGeometry3D
        Dim mesh As New MeshGeometry3D()
        Const h As Double = 0.5

        Dim b0 As New Point3D(-h, -h, -h)
        Dim b1 As New Point3D(h, -h, -h)
        Dim b2 As New Point3D(h, -h, h)
        Dim b3 As New Point3D(-h, -h, h)
        Dim apex As New Point3D(0, h, 0)

        AddQuad(mesh, b0, b1, b2, b3) ' base, facing down

        AddTriangle(mesh, b1, b0, apex) ' -z side
        AddTriangle(mesh, b2, b1, apex) ' +x side
        AddTriangle(mesh, b3, b2, apex) ' +z side
        AddTriangle(mesh, b0, b3, apex) ' -x side

        Return mesh
    End Function

    Private Function BuildSphere() As MeshGeometry3D
        ' Low-poly UV sphere.
        Dim mesh As New MeshGeometry3D()
        Const radius As Double = 0.5
        Const lonSegments As Integer = 8
        Const latSegments As Integer = 6

        Dim pts(latSegments, lonSegments) As Point3D
        For lat = 0 To latSegments
            Dim theta As Double = Math.PI * lat / latSegments ' 0..pi (top to bottom)
            Dim y As Double = radius * Math.Cos(theta)
            Dim r As Double = radius * Math.Sin(theta)
            For lon = 0 To lonSegments
                Dim phi As Double = 2 * Math.PI * lon / lonSegments
                Dim x As Double = r * Math.Sin(phi)
                Dim z As Double = r * Math.Cos(phi)
                pts(lat, lon) = New Point3D(x, y, z)
            Next
        Next

        For lat = 0 To latSegments - 1
            For lon = 0 To lonSegments - 1
                Dim p00 = pts(lat, lon)
                Dim p01 = pts(lat, lon + 1)
                Dim p10 = pts(lat + 1, lon)
                Dim p11 = pts(lat + 1, lon + 1)

                If lat = 0 Then
                    AddTriangle(mesh, p00, p11, p10)
                ElseIf lat = latSegments - 1 Then
                    AddTriangle(mesh, p00, p01, p11)
                Else
                    AddQuad(mesh, p00, p01, p11, p10)
                End If
            Next
        Next

        Return mesh
    End Function

    Private Function BuildCylinder() As MeshGeometry3D
        Dim mesh As New MeshGeometry3D()
        Const radius As Double = 0.5
        Const halfHeight As Double = 0.5
        Const segments As Integer = 10

        Dim top(segments - 1) As Point3D
        Dim bottom(segments - 1) As Point3D
        For i = 0 To segments - 1
            Dim ang As Double = 2 * Math.PI * i / segments
            Dim x As Double = radius * Math.Cos(ang)
            Dim z As Double = radius * Math.Sin(ang)
            top(i) = New Point3D(x, halfHeight, z)
            bottom(i) = New Point3D(x, -halfHeight, z)
        Next

        Dim topCenter As New Point3D(0, halfHeight, 0)
        Dim bottomCenter As New Point3D(0, -halfHeight, 0)

        For i = 0 To segments - 1
            Dim j As Integer = (i + 1) Mod segments
            ' side wall
            AddQuad(mesh, bottom(i), bottom(j), top(j), top(i))
            ' caps
            AddTriangle(mesh, topCenter, top(i), top(j))
            AddTriangle(mesh, bottomCenter, bottom(j), bottom(i))
        Next

        Return mesh
    End Function

    Private Function BuildCone() As MeshGeometry3D
        Dim mesh As New MeshGeometry3D()
        Const radius As Double = 0.5
        Const halfHeight As Double = 0.5
        Const segments As Integer = 10

        Dim apex As New Point3D(0, halfHeight, 0)
        Dim baseCenter As New Point3D(0, -halfHeight, 0)

        Dim baseRing(segments - 1) As Point3D
        For i = 0 To segments - 1
            Dim ang As Double = 2 * Math.PI * i / segments
            baseRing(i) = New Point3D(radius * Math.Cos(ang), -halfHeight, radius * Math.Sin(ang))
        Next

        For i = 0 To segments - 1
            Dim j As Integer = (i + 1) Mod segments
            AddTriangle(mesh, baseRing(i), baseRing(j), apex)
            AddTriangle(mesh, baseCenter, baseRing(j), baseRing(i))
        Next

        Return mesh
    End Function

    Private Function BuildPlane() As MeshGeometry3D
        Dim mesh As New MeshGeometry3D()
        Const h As Double = 0.5
        Dim p0 As New Point3D(-h, 0, -h)
        Dim p1 As New Point3D(h, 0, -h)
        Dim p2 As New Point3D(h, 0, h)
        Dim p3 As New Point3D(-h, 0, h)
        AddQuad(mesh, p0, p3, p2, p1) ' normal faces +y
        Return mesh
    End Function

End Module
