Imports System.Windows.Media.Media3D

''' <summary>
''' Boolean mesh subtraction via a BSP tree (the classic algorithm popularized by Evan Wallace's
''' csg.js). Works on arbitrary closed, manifold triangle meshes - not just convex shapes - which
''' is what makes a real "cut tool" (like Tinkercad's hole) possible instead of just clipping
''' against planes.
''' </summary>
Public Module CSG

    Private Const Epsilon As Double = 0.00001
    Private Const CoplanarType As Integer = 0
    Private Const FrontType As Integer = 1
    Private Const BackType As Integer = 2
    Private Const SpanningType As Integer = 3

    Private Class CsgPlane
        Public Normal As Vector3D
        Public W As Double

        Public Shared Function FromPoints(a As Point3D, b As Point3D, c As Point3D) As CsgPlane
            Dim n = Vector3D.CrossProduct(New Vector3D(b.X - a.X, b.Y - a.Y, b.Z - a.Z), New Vector3D(c.X - a.X, c.Y - a.Y, c.Z - a.Z))
            n.Normalize()
            Return New CsgPlane With {.Normal = n, .W = Vector3D.DotProduct(n, New Vector3D(a.X, a.Y, a.Z))}
        End Function

        Public Function Clone() As CsgPlane
            Return New CsgPlane With {.Normal = Normal, .W = W}
        End Function

        Public Sub Flip()
            Normal = -Normal
            W = -W
        End Sub

        ''' <summary>Splits polygon against this plane, sorting the pieces into the four lists.</summary>
        Public Sub SplitPolygon(polygon As CsgPolygon, coplanarFront As List(Of CsgPolygon), coplanarBack As List(Of CsgPolygon), front As List(Of CsgPolygon), back As List(Of CsgPolygon))
            Dim polygonType As Integer = 0
            Dim types As New List(Of Integer)

            For Each v In polygon.Vertices
                Dim t = Vector3D.DotProduct(Normal, New Vector3D(v.X, v.Y, v.Z)) - W
                Dim vType = If(t < -Epsilon, BackType, If(t > Epsilon, FrontType, CoplanarType))
                polygonType = polygonType Or vType
                types.Add(vType)
            Next

            Select Case polygonType
                Case CoplanarType
                    If Vector3D.DotProduct(Normal, polygon.Plane.Normal) > 0 Then
                        coplanarFront.Add(polygon)
                    Else
                        coplanarBack.Add(polygon)
                    End If
                Case FrontType
                    front.Add(polygon)
                Case BackType
                    back.Add(polygon)
                Case SpanningType
                    Dim f As New List(Of Point3D)
                    Dim b As New List(Of Point3D)
                    For i = 0 To polygon.Vertices.Count - 1
                        Dim j = (i + 1) Mod polygon.Vertices.Count
                        Dim ti = types(i)
                        Dim tj = types(j)
                        Dim vi = polygon.Vertices(i)
                        Dim vj = polygon.Vertices(j)

                        If ti <> BackType Then f.Add(vi)
                        If ti <> FrontType Then b.Add(vi)

                        If (ti Or tj) = SpanningType Then
                            Dim denom = Vector3D.DotProduct(Normal, New Vector3D(vj.X - vi.X, vj.Y - vi.Y, vj.Z - vi.Z))
                            Dim t = (W - Vector3D.DotProduct(Normal, New Vector3D(vi.X, vi.Y, vi.Z))) / denom
                            Dim v As New Point3D(vi.X + (vj.X - vi.X) * t, vi.Y + (vj.Y - vi.Y) * t, vi.Z + (vj.Z - vi.Z) * t)
                            f.Add(v)
                            b.Add(v)
                        End If
                    Next
                    If f.Count >= 3 Then front.Add(New CsgPolygon(f))
                    If b.Count >= 3 Then back.Add(New CsgPolygon(b))
            End Select
        End Sub
    End Class

    Private Class CsgPolygon
        Public Vertices As List(Of Point3D)
        Public Plane As CsgPlane

        Public Sub New(vertices As List(Of Point3D))
            Me.Vertices = vertices
            Plane = CsgPlane.FromPoints(vertices(0), vertices(1), vertices(2))
        End Sub

        Public Sub FlipInPlace()
            Vertices.Reverse()
            Plane.Flip()
        End Sub
    End Class

    Private Class CsgNode
        Public Plane As CsgPlane
        Public Front As CsgNode
        Public Back As CsgNode
        Public Polygons As New List(Of CsgPolygon)

        Public Sub Build(polys As List(Of CsgPolygon))
            If polys.Count = 0 Then Return
            If Plane Is Nothing Then Plane = polys(0).Plane.Clone()

            Dim front As New List(Of CsgPolygon)
            Dim back As New List(Of CsgPolygon)
            For Each p In polys
                Plane.SplitPolygon(p, Polygons, Polygons, front, back)
            Next

            If front.Count > 0 Then
                If Me.Front Is Nothing Then Me.Front = New CsgNode()
                Me.Front.Build(front)
            End If
            If back.Count > 0 Then
                If Me.Back Is Nothing Then Me.Back = New CsgNode()
                Me.Back.Build(back)
            End If
        End Sub

        Public Function ClipPolygons(polys As List(Of CsgPolygon)) As List(Of CsgPolygon)
            If Plane Is Nothing Then Return New List(Of CsgPolygon)(polys)

            Dim front As New List(Of CsgPolygon)
            Dim back As New List(Of CsgPolygon)
            For Each p In polys
                Plane.SplitPolygon(p, front, back, front, back)
            Next

            If Me.Front IsNot Nothing Then front = Me.Front.ClipPolygons(front)
            If Me.Back IsNot Nothing Then
                back = Me.Back.ClipPolygons(back)
            Else
                back = New List(Of CsgPolygon)
            End If

            front.AddRange(back)
            Return front
        End Function

        Public Sub ClipTo(other As CsgNode)
            Polygons = other.ClipPolygons(Polygons)
            If Front IsNot Nothing Then Front.ClipTo(other)
            If Back IsNot Nothing Then Back.ClipTo(other)
        End Sub

        Public Sub Invert()
            For Each p In Polygons
                p.FlipInPlace()
            Next
            If Plane IsNot Nothing Then Plane.Flip()
            If Front IsNot Nothing Then Front.Invert()
            If Back IsNot Nothing Then Back.Invert()
            Dim tmp = Front
            Front = Back
            Back = tmp
        End Sub

        Public Function AllPolygons() As List(Of CsgPolygon)
            Dim result As New List(Of CsgPolygon)(Polygons)
            If Front IsNot Nothing Then result.AddRange(Front.AllPolygons())
            If Back IsNot Nothing Then result.AddRange(Back.AllPolygons())
            Return result
        End Function
    End Class

    ''' <summary>Subtracts cutterTriangles from targetTriangles. Each input triangle is 3 points;
    ''' both lists must be in the same coordinate frame. Result triangles are in that same frame.</summary>
    Public Function Subtract(targetTriangles As List(Of Point3D()), cutterTriangles As List(Of Point3D())) As List(Of Point3D())
        Dim a As New CsgNode()
        a.Build(ToPolygons(targetTriangles))
        Dim b As New CsgNode()
        b.Build(ToPolygons(cutterTriangles))

        a.Invert()
        a.ClipTo(b)
        b.ClipTo(a)
        b.Invert()
        b.ClipTo(a)
        b.Invert()
        a.Build(b.AllPolygons())
        a.Invert()

        Return Triangulate(a.AllPolygons())
    End Function

    Private Function ToPolygons(triangles As List(Of Point3D())) As List(Of CsgPolygon)
        Dim result As New List(Of CsgPolygon)
        For Each tri In triangles
            result.Add(New CsgPolygon(New List(Of Point3D) From {tri(0), tri(1), tri(2)}))
        Next
        Return result
    End Function

    Private Function Triangulate(polys As List(Of CsgPolygon)) As List(Of Point3D())
        Dim result As New List(Of Point3D())
        For Each p In polys
            If p.Vertices.Count < 3 Then Continue For
            For i = 1 To p.Vertices.Count - 2
                result.Add({p.Vertices(0), p.Vertices(i), p.Vertices(i + 1)})
            Next
        Next
        Return result
    End Function

End Module
