Imports System.Drawing
Imports System.IO
Imports System.Numerics
Imports System.Text.Json

Namespace Assets

    ''' <summary>
    ''' One mesh of one rigid body, in the local space of that body.
    ''' </summary>
    Public Class KinematicGeom
        Public Property Mesh As String
        Public Property Type As String = "mesh"
        Public Property Position As Vector3 = Vector3.Zero
        Public Property Rotation As Quaternion = Quaternion.Identity
        Public Property Color As Color = Color.Silver
    End Class

    ''' <summary>
    ''' The hinge of a body: its axis, its local position and its travel.
    ''' </summary>
    Public Class KinematicJoint
        Public Property Name As String
        Public Property Type As String = "hinge"
        Public Property Axis As Vector3 = Vector3.UnitZ
        Public Property Position As Vector3 = Vector3.Zero
        Public Property Limits As Double() = {-Math.PI, Math.PI}
        Public Property ActuatorIndex As Integer = -1
    End Class

    ''' <summary>
    ''' One rigid body of the robot: its placement inside the parent frame, the
    ''' meshes that it carries and the joint that connects it to the parent.
    ''' </summary>
    Public Class KinematicBody
        Public Property Name As String
        Public Property Parent As String
        Public Property Position As Vector3 = Vector3.Zero
        Public Property Rotation As Quaternion = Quaternion.Identity
        Public Property Geoms As New List(Of KinematicGeom)
        Public Property Joint As KinematicJoint

        ''' <summary>the index of the body inside <see cref="KinematicsDocument.Bodies"/></summary>
        Public Property Index As Integer = -1
        ''' <summary>the index of the parent body, -1 for the trunk</summary>
        Public Property ParentIndex As Integer = -1
    End Class

    ''' <summary>
    ''' The ``kinematics.json`` document: the body tree, the meshes of every body
    ''' and the hinges between them, exported from the MJCF of the robot.
    ''' </summary>
    ''' <remarks>
    ''' This is the only source of the render rig of the duck. The document
    ''' carries no mass or inertia (those live in the MJCF), but it does carry
    ''' the local placement of every mesh and its color, which is what the
    ''' three.js build used to assemble the skeleton.
    '''
    ''' The document uses the MJCF frame (z up) and the MJCF quaternion order
    ''' (w, x, y, z); both are converted here, at the boundary, so that the rest
    ''' of the application never sees them.
    ''' </remarks>
    Public Class KinematicsDocument

        Public ReadOnly Property Bodies As New List(Of KinematicBody)()

        ''' <summary>the body of the trunk (the one without a parent)</summary>
        Public Property Root As KinematicBody

        Public Shared Function Load(path As String) As KinematicsDocument
            Using stream As Stream = File.OpenRead(path)
                Using document As JsonDocument = JsonDocument.Parse(stream)
                    Return Parse(document)
                End Using
            End Using
        End Function

        Private Shared Function Parse(document As JsonDocument) As KinematicsDocument
            Dim result As New KinematicsDocument()
            Dim root As JsonElement = document.RootElement
            Dim list As JsonElement
            Dim body As JsonElement

            If Not root.TryGetProperty("bodies", list) Then
                Throw New InvalidDataException("the kinematics document has no 'bodies' array")
            End If

            For Each body In list.EnumerateArray()
                Dim item As New KinematicBody With {
                    .Index = result.Bodies.Count,
                    .Name = ReadString(body, "name"),
                    .Parent = ReadString(body, "parent"),
                    .Position = ReadVector3(body, "pos"),
                    .Rotation = ReadQuaternion(body, "quat")
                }
                Dim geoms As JsonElement

                If body.TryGetProperty("geoms", geoms) Then
                    For Each geom As JsonElement In geoms.EnumerateArray()
                        Call item.Geoms.Add(ParseGeom(geom))
                    Next
                End If

                Dim joint As JsonElement

                If body.TryGetProperty("joint", joint) AndAlso joint.ValueKind = JsonValueKind.Object Then
                    item.Joint = ParseJoint(joint)
                End If

                Call result.Bodies.Add(item)
            Next

            For Each item As KinematicBody In result.Bodies
                If String.IsNullOrEmpty(item.Parent) Then
                    result.Root = item
                Else
                    item.ParentIndex = result.Bodies.FindIndex(
                        Function(x) String.Equals(x.Name, item.Parent, StringComparison.Ordinal))
                End If
            Next

            Return result
        End Function

        Private Shared Function ParseGeom(geom As JsonElement) As KinematicGeom
            Dim item As New KinematicGeom With {
                .Mesh = ReadString(geom, "mesh"),
                .Type = ReadString(geom, "type", "mesh"),
                .Position = ReadVector3(geom, "pos"),
                .Rotation = ReadQuaternion(geom, "quat")
            }
            Dim palette As JsonElement

            If geom.TryGetProperty("color", palette) AndAlso palette.ValueKind = JsonValueKind.Array AndAlso palette.GetArrayLength() >= 3 Then
                Dim alpha As Double = If(palette.GetArrayLength() >= 4, ReadDouble(palette, 3, 1), 1)

                item.Color = Color.FromArgb(
                    CInt(Math.Round(Math.Max(0, Math.Min(1, alpha)) * 255)),
                    ToByte(ReadDouble(palette, 0, 1)),
                    ToByte(ReadDouble(palette, 1, 1)),
                    ToByte(ReadDouble(palette, 2, 1)))
            End If

            Return item
        End Function

        Private Shared Function ParseJoint(joint As JsonElement) As KinematicJoint
            Dim item As New KinematicJoint With {
                .Name = ReadString(joint, "name"),
                .Type = ReadString(joint, "type", "hinge"),
                .Axis = ReadVector3(joint, "axis", Vector3.UnitZ),
                .Position = ReadVector3(joint, "pos")
            }
            Dim limits As JsonElement

            If joint.TryGetProperty("range", limits) AndAlso limits.ValueKind = JsonValueKind.Array AndAlso limits.GetArrayLength() >= 2 Then
                item.Limits = {ReadDouble(limits, 0, -Math.PI), ReadDouble(limits, 1, Math.PI)}
            End If

            Dim index As JsonElement

            If joint.TryGetProperty("actuator_index", index) AndAlso index.ValueKind = JsonValueKind.Number Then
                item.ActuatorIndex = index.GetInt32()
            End If

            Return item
        End Function

        Private Shared Function ToByte(value As Double) As Byte
            Return CByte(Math.Max(0, Math.Min(255, Math.Round(value * 255))))
        End Function

        Private Shared Function ReadString(element As JsonElement, name As String, Optional fallback As String = Nothing) As String
            Dim value As JsonElement

            If element.TryGetProperty(name, value) AndAlso value.ValueKind = JsonValueKind.String Then
                Return value.GetString()
            End If

            Return fallback
        End Function

        Private Shared Function ReadDouble(array As JsonElement, index As Integer, fallback As Double) As Double
            Dim value As JsonElement

            If index < array.GetArrayLength() Then
                value = array(index)

                If value.ValueKind = JsonValueKind.Number Then
                    Return value.GetDouble()
                End If
            End If

            Return fallback
        End Function

        Private Shared Function ReadVector3(element As JsonElement, name As String,
                                             Optional fallback As Vector3 = Nothing) As Vector3
            Dim value As JsonElement

            If element.TryGetProperty(name, value) AndAlso value.ValueKind = JsonValueKind.Array AndAlso value.GetArrayLength() >= 3 Then
                Return New Vector3(
                    CSng(ReadDouble(value, 0, 0)),
                    CSng(ReadDouble(value, 1, 0)),
                    CSng(ReadDouble(value, 2, 0)))
            End If

            Return fallback
        End Function

        ''' <summary>
        ''' read an MJCF quaternion (w, x, y, z) into the managed order
        ''' </summary>
        Private Shared Function ReadQuaternion(element As JsonElement, name As String) As Quaternion
            Dim value As JsonElement

            If element.TryGetProperty(name, value) AndAlso value.ValueKind = JsonValueKind.Array AndAlso value.GetArrayLength() >= 4 Then
                Return New Quaternion(
                    CSng(ReadDouble(value, 1, 0)),
                    CSng(ReadDouble(value, 2, 0)),
                    CSng(ReadDouble(value, 3, 0)),
                    CSng(ReadDouble(value, 0, 1)))
            End If

            Return Quaternion.Identity
        End Function
    End Class
End Namespace
