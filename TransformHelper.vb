Imports System.Linq
Imports System.Windows.Media.Media3D

Public Module TransformHelper

    ''' <summary>Builds the local-to-world transform for a scene object: scale, then rotate XYZ, then translate.</summary>
    Public Function BuildTransform(obj As SceneObject) As Transform3DGroup
        Dim g As New Transform3DGroup()
        g.Children.Add(New ScaleTransform3D(obj.ScaleX, obj.ScaleY, obj.ScaleZ))
        g.Children.Add(New RotateTransform3D(New AxisAngleRotation3D(New Vector3D(1, 0, 0), obj.RotX)))
        g.Children.Add(New RotateTransform3D(New AxisAngleRotation3D(New Vector3D(0, 1, 0), obj.RotY)))
        g.Children.Add(New RotateTransform3D(New AxisAngleRotation3D(New Vector3D(0, 0, 1), obj.RotZ)))
        g.Children.Add(New TranslateTransform3D(obj.PosX, obj.PosY, obj.PosZ))
        Return g
    End Function

    ''' <summary>Flattens a scene object into its renderable/exportable pieces: for a simple
    ''' primitive, a single piece using its own transform; for a joined compound, one piece per
    ''' part with the part's transform composed under the compound's own transform.</summary>
    Public Function GetPieces(obj As SceneObject) As List(Of (Kind As PrimitiveKind, Matrix As Matrix3D, ColorHex As String, CustomTriangles As List(Of Double)))
        Dim result As New List(Of (PrimitiveKind, Matrix3D, String, List(Of Double)))
        If obj.Parts IsNot Nothing AndAlso obj.Parts.Count > 0 Then
            Dim compoundMatrix = BuildTransform(obj).Value
            For Each part In obj.Parts
                Dim partMatrix = BuildTransform(part).Value
                result.Add((part.Kind, partMatrix * compoundMatrix, part.ColorHex, part.CustomTriangles))
            Next
        Else
            result.Add((obj.Kind, BuildTransform(obj).Value, obj.ColorHex, obj.CustomTriangles))
        End If
        Return result
    End Function

    ''' <summary>Bakes a part's transform (relative to parent) into an equivalent top-level scene
    ''' object with absolute values. Position is exact; rotation/scale are additive/multiplicative
    ''' approximations that are exact whenever the parent's own rotation is identity.</summary>
    Public Function BakePartToWorld(part As SceneObject, parent As SceneObject) As SceneObject
        Dim combined = BuildTransform(part).Value * BuildTransform(parent).Value
        Dim worldOrigin = combined.Transform(New Point3D(0, 0, 0))
        Return New SceneObject() With {
            .Kind = part.Kind,
            .Name = part.Name,
            .PosX = worldOrigin.X,
            .PosY = worldOrigin.Y,
            .PosZ = worldOrigin.Z,
            .RotX = parent.RotX + part.RotX,
            .RotY = parent.RotY + part.RotY,
            .RotZ = parent.RotZ + part.RotZ,
            .ScaleX = parent.ScaleX * part.ScaleX,
            .ScaleY = parent.ScaleY * part.ScaleY,
            .ScaleZ = parent.ScaleZ * part.ScaleZ,
            .ColorHex = part.ColorHex
        }
    End Function

    ''' <summary>The rotation-only part of an object's transform (world-axis X, then Y, then Z),
    ''' matching the order used in BuildTransform. Used to find the object's current local axes.</summary>
    Public Function BuildRotationMatrix(rotXDeg As Double, rotYDeg As Double, rotZDeg As Double) As Matrix3D
        Dim g As New Transform3DGroup()
        g.Children.Add(New RotateTransform3D(New AxisAngleRotation3D(New Vector3D(1, 0, 0), rotXDeg)))
        g.Children.Add(New RotateTransform3D(New AxisAngleRotation3D(New Vector3D(0, 1, 0), rotYDeg)))
        g.Children.Add(New RotateTransform3D(New AxisAngleRotation3D(New Vector3D(0, 0, 1), rotZDeg)))
        Return g.Value
    End Function

    Public Function BuildRotationMatrix(obj As SceneObject) As Matrix3D
        Return BuildRotationMatrix(obj.RotX, obj.RotY, obj.RotZ)
    End Function

    ''' <summary>Extracts (RotX, RotY, RotZ) degrees from a rotation matrix, inverse to
    ''' BuildRotationMatrix's X-then-Y-then-Z (world axis) composition order.</summary>
    Public Function DecomposeRotationDegrees(m As Matrix3D) As (RotX As Double, RotY As Double, RotZ As Double)
        Dim m13 = Math.Max(-1.0, Math.Min(1.0, m.M13))
        Dim yRad = Math.Asin(-m13)
        Dim xRad As Double
        Dim zRad As Double

        If Math.Abs(m13) > 0.9999 Then
            ' Gimbal lock (pitch at +/-90 deg): X and Z become coupled: pin Z and fold everything
            ' into X so the object's orientation still comes out right, just not the same split.
            zRad = 0
            xRad = Math.Atan2(-m.M32, m.M22)
        Else
            xRad = Math.Atan2(m.M23, m.M33)
            zRad = Math.Atan2(m.M12, m.M11)
        End If

        Return (xRad * 180 / Math.PI, yRad * 180 / Math.PI, zRad * 180 / Math.PI)
    End Function

    ''' <summary>Deep-clones a scene object (new Id, independent Parts list) at the same transform.</summary>
    Public Function CloneSceneObject(src As SceneObject) As SceneObject
        Dim copy As New SceneObject() With {
            .Kind = src.Kind,
            .Name = src.Name,
            .PosX = src.PosX, .PosY = src.PosY, .PosZ = src.PosZ,
            .RotX = src.RotX, .RotY = src.RotY, .RotZ = src.RotZ,
            .ScaleX = src.ScaleX, .ScaleY = src.ScaleY, .ScaleZ = src.ScaleZ,
            .ColorHex = src.ColorHex,
            .IsCutTool = src.IsCutTool
        }
        If src.Parts IsNot Nothing AndAlso src.Parts.Count > 0 Then
            copy.Parts = src.Parts.Select(Function(p) CloneSceneObject(p)).ToList()
        End If
        If src.CustomTriangles IsNot Nothing Then
            copy.CustomTriangles = New List(Of Double)(src.CustomTriangles)
        End If
        Return copy
    End Function

End Module
