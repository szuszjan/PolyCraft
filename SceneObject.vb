Public Enum PrimitiveKind
    Cube
    Pyramid
    Sphere
    Cylinder
    Cone
    Plane
    ''' <summary>A one-off baked mesh (e.g. the result of a boolean cut), stored in CustomTriangles
    ''' instead of being generated procedurally.</summary>
    Custom
End Enum

''' <summary>A single placed primitive: type + transform + color. Fully round-trips to/from JSON.</summary>
Public Class SceneObject
    Public Property Id As Guid = Guid.NewGuid()
    Public Property Name As String = ""
    Public Property Kind As PrimitiveKind = PrimitiveKind.Cube

    Public Property PosX As Double = 0
    Public Property PosY As Double = 0
    Public Property PosZ As Double = 0

    Public Property RotX As Double = 0
    Public Property RotY As Double = 0
    Public Property RotZ As Double = 0

    Public Property ScaleX As Double = 1
    Public Property ScaleY As Double = 1
    Public Property ScaleZ As Double = 1

    Public Property ColorHex As String = "#3388CC"

    ''' <summary>When non-empty, this object is a joined compound: each part's Pos/Rot/Scale is
    ''' relative to this object's own transform, and Kind is unused for rendering.</summary>
    Public Property Parts As List(Of SceneObject) = Nothing

    ''' <summary>When Kind = Custom, the object's mesh in local space: flattened triangles, 9
    ''' doubles each (3 vertices x XYZ), in vertex order.</summary>
    Public Property CustomTriangles As List(Of Double) = Nothing

    ''' <summary>Marks this object as a cut tool (Tinkercad "hole" style): rendered as a
    ''' translucent cutter, and subtracted from a solid instead of merged when joined with one.</summary>
    Public Property IsCutTool As Boolean = False
End Class

Public Enum GizmoMode
    None
    Move
    Rotate
    Scale
End Enum
