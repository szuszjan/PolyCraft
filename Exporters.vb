Imports System.Globalization
Imports System.IO
Imports System.Windows.Media.Media3D

''' <summary>Exports the scene (world-space, per-object meshes already transformed) to OBJ and binary STL.</summary>
Public Module Exporters

    Public Sub ExportObj(objects As List(Of SceneObject), path As String)
        Using writer As New StreamWriter(path, False)
            writer.WriteLine("# Low Poly Modeler export")
            Dim vertexOffset As Integer = 1 ' OBJ indices are 1-based

            For Each obj In objects
                Dim safeName = If(String.IsNullOrWhiteSpace(obj.Name), obj.Kind.ToString(), obj.Name.Replace(" "c, "_"c))
                writer.WriteLine($"o {safeName}")

                For Each piece In TransformHelper.GetPieces(obj)
                    Dim mesh = MeshBuilder.Resolve(piece.Kind, piece.CustomTriangles)
                    Dim m = piece.Matrix

                    For Each p In mesh.Positions
                        Dim wp = m.Transform(p)
                        writer.WriteLine(String.Format(CultureInfo.InvariantCulture, "v {0:0.######} {1:0.######} {2:0.######}", wp.X, wp.Y, wp.Z))
                    Next

                    Dim tris = mesh.TriangleIndices
                    Dim i As Integer = 0
                    While i < tris.Count
                        Dim a = tris(i) + vertexOffset
                        Dim b = tris(i + 1) + vertexOffset
                        Dim c = tris(i + 2) + vertexOffset
                        writer.WriteLine($"f {a} {b} {c}")
                        i += 3
                    End While

                    vertexOffset += mesh.Positions.Count
                Next
            Next
        End Using
    End Sub

    Public Sub ExportStl(objects As List(Of SceneObject), path As String)
        Using stream As New FileStream(path, FileMode.Create, FileAccess.Write)
            Using bw As New BinaryWriter(stream)
                Dim header(79) As Byte
                Dim headerText = System.Text.Encoding.ASCII.GetBytes("Low Poly Modeler binary STL export")
                Array.Copy(headerText, header, Math.Min(headerText.Length, header.Length))
                bw.Write(header)

                ' Gather all world-space triangles first so we can write an accurate count.
                Dim triangles As New List(Of Point3D())()
                For Each obj In objects
                    For Each piece In TransformHelper.GetPieces(obj)
                        Dim mesh = MeshBuilder.Resolve(piece.Kind, piece.CustomTriangles)
                        Dim m = piece.Matrix
                        Dim worldPts(mesh.Positions.Count - 1) As Point3D
                        For i = 0 To mesh.Positions.Count - 1
                            worldPts(i) = m.Transform(mesh.Positions(i))
                        Next

                        Dim tris = mesh.TriangleIndices
                        Dim k As Integer = 0
                        While k < tris.Count
                            triangles.Add({worldPts(tris(k)), worldPts(tris(k + 1)), worldPts(tris(k + 2))})
                            k += 3
                        End While
                    Next
                Next

                bw.Write(CUInt(triangles.Count))

                For Each tri In triangles
                    Dim u As New Vector3D(tri(1).X - tri(0).X, tri(1).Y - tri(0).Y, tri(1).Z - tri(0).Z)
                    Dim v As New Vector3D(tri(2).X - tri(0).X, tri(2).Y - tri(0).Y, tri(2).Z - tri(0).Z)
                    Dim n = Vector3D.CrossProduct(u, v)
                    If n.Length > 0 Then n.Normalize()

                    bw.Write(CSng(n.X)) : bw.Write(CSng(n.Y)) : bw.Write(CSng(n.Z))
                    For Each p In tri
                        bw.Write(CSng(p.X)) : bw.Write(CSng(p.Y)) : bw.Write(CSng(p.Z))
                    Next
                    bw.Write(CUShort(0))
                Next
            End Using
        End Using
    End Sub

End Module
