Imports System.IO
Imports System.Text
Imports System.Text.Json
Imports Microsoft.VisualBasic.Imaging.Drawing3D
Imports Microsoft.VisualBasic.Imaging.Landscape.Data
Imports Microsoft.VisualBasic.Imaging.Landscape.Gltf
Imports num = System.Numerics

Namespace Model

    ''' <summary>按连杆分组的 glTF 网格集合。</summary>
    Public Class GlbScene

        Public Property SourceFile As String

        ''' <summary>键是 URDF link 名（找不到对应 link 时用节点名）。</summary>
        Public Property Meshes As New Dictionary(Of String, LinkMesh)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>本解析器得到的三角形总数。</summary>
        Public Property TriangleCount As Integer

        ''' <summary>
        ''' 用 Landscape 的 <see cref="ModelLoader"/> 整体加载得到的三角形数。
        ''' 与 <see cref="TriangleCount"/> 一致说明两份解析没有分歧。
        ''' </summary>
        Public Property LandscapeTriangleCount As Integer

        Public Property NodeCount As Integer
        Public Property MeshCount As Integer

        Public Function GetMesh(linkName As String) As LinkMesh
            Dim mesh As LinkMesh = Nothing

            If String.IsNullOrEmpty(linkName) Then Return Nothing
            If Meshes.TryGetValue(linkName, mesh) Then Return mesh

            Return Nothing
        End Function

        Public Function MeshNames() As List(Of String)
            Return Meshes.Keys.ToList()
        End Function
    End Class

    ''' <summary>
    ''' 按连杆分组加载 GLB：Landscape 的 <c>ModelBuilder</c> 是 Friend 且会丢弃 glTF 的节点变换，
    ''' 无法把"哪个三角形属于哪根连杆"还原出来，所以这里复用 Landscape 的 <see cref="GltfRoot"/>
    ''' 公共类型自行解析 JSON chunk / BIN chunk，遍历节点树并按 URDF link 名分组。
    ''' </summary>
    ''' <remarks>
    ''' <para>
    ''' glTF 的坐标约定是 **Y-up**（Blender 导出），而 URDF 与本工程的物理世界都是 **Z-up**，
    ''' 因此每个顶点在最后都要乘一个绕 X 轴 +90° 的旋转：<c>(x, y, z) → (x, -z, y)</c>。
    ''' 已用 base_link 的包围盒（y ∈ [0.014, 0.132] ↔ URDF box 高度 0.13）与
    ''' J2_Shoulder 的包围盒（x ∈ [0, 0.215]）核对过。
    ''' </para>
    ''' <para>
    ''' 一个 URDF link 可能对应多个 glTF 节点（本模型的 Camera / Finger_L / Finger_R 都挂在
    ''' J6_Roll 下），所以顶点先变换到 glTF 世界系，再乘上"最近 link 节点世界矩阵的逆"，
    ''' 从而落到该 link 的局部系里。
    ''' </para>
    ''' </remarks>
    Public Module GlbLinkLoader

        Private Const GlbMagic As UInteger = &H46546C67UI
        Private Const ChunkJson As UInteger = &H4E4F534AUI
        Private Const ChunkBin As UInteger = &H004E4942UI

        Private Const DefaultColor As String = "#B8BDC7"

        ''' <summary>加载 GLB，并按 URDF 的 link 名分组。</summary>
        ''' <param name="file">glb 路径</param>
        ''' <param name="linkNames">URDF 中已知的 link 名，用于确定分组归属</param>
        ''' <param name="validateWithLandscape">是否用 Landscape 再整体加载一次做面数校验</param>
        Public Function Load(file As String, linkNames As IEnumerable(Of String),
                             Optional validateWithLandscape As Boolean = True) As GlbScene

            If String.IsNullOrEmpty(file) OrElse Not File.Exists(file) Then
                Throw New FileNotFoundException("找不到模型文件: " & file, file)
            End If

            Dim scene As New GlbScene With {.SourceFile = file}
            Dim container As GlbContainer = ReadContainer(file)

            If String.IsNullOrEmpty(container.Json) Then
                Throw New InvalidDataException("GLB 文件中没有 JSON chunk: " & file)
            End If

            Dim options As New JsonSerializerOptions With {
                .PropertyNameCaseInsensitive = True
            }
            Dim root As GltfRoot = JsonSerializer.Deserialize(Of GltfRoot)(container.Json, options)

            If root Is Nothing OrElse root.Nodes Is Nothing OrElse root.Nodes.Length = 0 Then
                Throw New InvalidDataException("GLB 中没有节点: " & file)
            End If

            Dim buffers As New List(Of Byte()) From {container.Binary}
            Dim known As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

            If linkNames IsNot Nothing Then
                For Each linkName As String In linkNames
                    known.Add(linkName)
                Next
            End If

            scene.NodeCount = root.Nodes.Length
            scene.MeshCount = If(root.Meshes Is Nothing, 0, root.Meshes.Length)

            Dim roots As Integer() = RootNodes(root)

            For Each nodeIndex As Integer In roots
                Call WalkNode(root, nodeIndex, num.Matrix4x4.Identity, Nothing, num.Matrix4x4.Identity,
                              known, buffers, scene)
            Next

            For Each mesh As LinkMesh In scene.Meshes.Values
                Call mesh.RecalculateBounds()
                scene.TriangleCount += mesh.TriangleCount
            Next

            If validateWithLandscape Then
                scene.LandscapeTriangleCount = LandscapeTriangleCount(file)
            End If

            Return scene
        End Function

        ''' <summary>
        ''' 用 Landscape 的整体加载器再读一次，返回它认为的三角形数。
        ''' 这一步保证"模型确实是通过 Landscape 加载的"，同时校验按连杆分组的解析没有丢面。
        ''' </summary>
        Public Function LandscapeTriangleCount(file As String) As Integer
            Try
                Dim model As SceneModel = ModelLoader.LoadModel(file)

                If model Is Nothing OrElse model.Surfaces Is Nothing Then Return 0

                Return model.Surfaces.Length
            Catch ex As Exception
                Console.WriteLine("[glb] Landscape 校验加载失败: " & ex.Message)
                Return -1
            End Try
        End Function

        ' /********************************************************************************/
        '  节点树
        ' /********************************************************************************/

        Private Function RootNodes(root As GltfRoot) As Integer()
            If root.DefaultScene.HasValue AndAlso root.Scenes IsNot Nothing Then
                Dim index As Integer = root.DefaultScene.Value

                If index >= 0 AndAlso index < root.Scenes.Length Then
                    Dim sc As Scene = root.Scenes(index)

                    If sc.Nodes IsNot Nothing AndAlso sc.Nodes.Length > 0 Then
                        Return sc.Nodes
                    End If
                End If
            End If

            If root.Scenes IsNot Nothing AndAlso root.Scenes.Length > 0 AndAlso root.Scenes(0).Nodes IsNot Nothing Then
                Return root.Scenes(0).Nodes
            End If

            Return {0}
        End Function

        Private Sub WalkNode(root As GltfRoot, nodeIndex As Integer, parentWorld As num.Matrix4x4,
                             currentLink As String, currentLinkInverse As num.Matrix4x4,
                             knownLinks As HashSet(Of String), buffers As List(Of Byte()), scene As GlbScene)

            If nodeIndex < 0 OrElse nodeIndex >= root.Nodes.Length Then Return

            Dim node As Node = root.Nodes(nodeIndex)
            Dim local As num.Matrix4x4 = LocalMatrix(node)
            Dim world As num.Matrix4x4 = num.Matrix4x4.Multiply(local, parentWorld)
            Dim linkName As String = currentLink
            Dim linkInverse As num.Matrix4x4 = currentLinkInverse

            ' 该节点本身是一个 URDF link：把它作为后续子节点的局部系原点
            If Not String.IsNullOrEmpty(node.Name) AndAlso knownLinks.Contains(node.Name) Then
                linkName = node.Name

                Dim inverted As num.Matrix4x4

                If num.Matrix4x4.Invert(world, inverted) Then
                    linkInverse = inverted
                Else
                    linkInverse = num.Matrix4x4.Identity
                End If
            End If

            If node.MeshIndex.HasValue Then
                Dim group As String = If(linkName, If(node.Name, "mesh" & node.MeshIndex.Value))

                Call BuildMesh(root, node, node.MeshIndex.Value,
                               num.Matrix4x4.Multiply(world, linkInverse),
                               group, buffers, scene)
            End If

            If node.Children IsNot Nothing Then
                For Each child As Integer In node.Children
                    Call WalkNode(root, child, world, linkName, linkInverse, knownLinks, buffers, scene)
                Next
            End If
        End Sub

        Private Function LocalMatrix(node As Node) As num.Matrix4x4
            If node.Matrix IsNot Nothing AndAlso node.Matrix.Length = 16 Then
                ' glTF 的 matrix 是列主序，System.Numerics 的构造函数参数是行主序
                Dim m As Single() = node.Matrix

                Return New num.Matrix4x4(
                    m(0), m(4), m(8), m(12),
                    m(1), m(5), m(9), m(13),
                    m(2), m(6), m(10), m(14),
                    m(3), m(7), m(11), m(15))
            End If

            Dim t As Single() = If(node.Translation, New Single() {0, 0, 0})
            Dim r As Single() = If(node.Rotation, New Single() {0, 0, 0, 1})
            Dim s As Single() = If(node.Scale, New Single() {1, 1, 1})

            Dim scale As num.Matrix4x4 = num.Matrix4x4.CreateScale(s(0), s(1), s(2))
            Dim rotation As num.Matrix4x4 = num.Matrix4x4.CreateFromQuaternion(
                New num.Quaternion(r(0), r(1), r(2), r(3)))
            Dim translation As num.Matrix4x4 = num.Matrix4x4.CreateTranslation(t(0), t(1), t(2))

            Return num.Matrix4x4.Multiply(num.Matrix4x4.Multiply(scale, rotation), translation)
        End Function

        ' /********************************************************************************/
        '  三角形提取
        ' /********************************************************************************/

        Private Sub BuildMesh(root As GltfRoot, node As Node, meshIndex As Integer,
                              nodeToLink As num.Matrix4x4, group As String,
                              buffers As List(Of Byte()), scene As GlbScene)

            If root.Meshes Is Nothing OrElse meshIndex < 0 OrElse meshIndex >= root.Meshes.Length Then Return

            Dim mesh As LinkMesh = Nothing

            If Not scene.Meshes.TryGetValue(group, mesh) Then
                mesh = New LinkMesh With {
                    .LinkName = group,
                    .NodePath = If(node.Name, group)
                }
                scene.Meshes.Add(group, mesh)
            End If

            ' 顶点先变换到 link 局部系，再做 Y-up → Z-up
            Dim transform As num.Matrix4x4 = num.Matrix4x4.Multiply(nodeToLink, YupToZup())

            For Each primitive As Primitive In root.Meshes(meshIndex).Primitives
                If primitive.Attributes Is Nothing Then Continue For

                Dim positionAccessor As Integer

                If Not primitive.Attributes.TryGetValue("POSITION", positionAccessor) Then Continue For

                Dim positions As num.Vector3() = ReadVec3(root, buffers, positionAccessor)

                If positions Is Nothing OrElse positions.Length = 0 Then Continue For

                Dim indices As Integer() = Nothing

                If primitive.IndicesAccessor.HasValue Then
                    indices = ReadIndices(root, buffers, primitive.IndicesAccessor.Value)
                End If

                Dim color As String = MaterialColor(root, primitive)
                Dim mode As Integer = If(primitive.Mode, 4)

                If mode <> 4 Then Continue For   ' 只处理 TRIANGLES

                Dim count As Integer = If(indices IsNot Nothing, indices.Length, positions.Length)

                For i As Integer = 0 To count - 3 Step 3
                    Dim i0 As Integer = If(indices IsNot Nothing, indices(i), i)
                    Dim i1 As Integer = If(indices IsNot Nothing, indices(i + 1), i + 1)
                    Dim i2 As Integer = If(indices IsNot Nothing, indices(i + 2), i + 2)

                    If i0 < 0 OrElse i0 >= positions.Length Then Continue For
                    If i1 < 0 OrElse i1 >= positions.Length Then Continue For
                    If i2 < 0 OrElse i2 >= positions.Length Then Continue For

                    Dim a As num.Vector3 = num.Vector3.Transform(positions(i0), transform)
                    Dim b As num.Vector3 = num.Vector3.Transform(positions(i1), transform)
                    Dim c As num.Vector3 = num.Vector3.Transform(positions(i2), transform)

                    mesh.Add(New Point3D(CSng(a.X), CSng(a.Y), CSng(a.Z)),
                             New Point3D(CSng(b.X), CSng(b.Y), CSng(b.Z)),
                             New Point3D(CSng(c.X), CSng(c.Y), CSng(c.Z)),
                             color)
                Next
            Next
        End Sub

        ''' <summary>glTF 的 Y-up → 本工程的 Z-up：<c>(x, y, z) → (x, -z, y)</c>。</summary>
        Private Function YupToZup() As num.Matrix4x4
            Return num.Matrix4x4.CreateRotationX(CSng(System.Math.PI / 2.0))
        End Function

        Private Function MaterialColor(root As GltfRoot, primitive As Primitive) As String
            If Not primitive.MaterialIndex.HasValue Then Return DefaultColor
            If root.Materials Is Nothing Then Return DefaultColor

            Dim index As Integer = primitive.MaterialIndex.Value

            If index < 0 OrElse index >= root.Materials.Length Then Return DefaultColor

            Dim material As GltfMaterial = root.Materials(index)

            If material Is Nothing OrElse material.PbrMetallicRoughness Is Nothing Then Return DefaultColor

            Dim factor As Single() = material.PbrMetallicRoughness.BaseColorFactor

            If factor Is Nothing OrElse factor.Length < 3 Then Return DefaultColor

            Return $"#{ToByte(factor(0)):X2}{ToByte(factor(1)):X2}{ToByte(factor(2)):X2}"
        End Function

        Private Function ToByte(value As Single) As Integer
            Dim v As Double = System.Math.Max(0.0, System.Math.Min(1.0, value)) * 255.0

            Return CInt(System.Math.Round(v))
        End Function

        ' /********************************************************************************/
        '  二进制读取
        ' /********************************************************************************/

        Private Class GlbContainer
            Public Json As String
            Public Binary As Byte()
        End Class

        Private Function ReadContainer(file As String) As GlbContainer
            Dim container As New GlbContainer()

            Using stream As FileStream = File.OpenRead(file)
                Using reader As New BinaryReader(stream, Encoding.UTF8, leaveOpen:=False)
                    If stream.Length < 12 Then
                        Throw New InvalidDataException("不是有效的 GLB 文件（长度不足）: " & file)
                    End If

                    Dim magic As UInteger = reader.ReadUInt32()
                    Dim version As UInteger = reader.ReadUInt32()
                    Dim totalLength As UInteger = reader.ReadUInt32()

                    If magic <> GlbMagic Then
                        Throw New InvalidDataException($"不是有效的 GLB 文件（magic=0x{magic:X8}）: " & file)
                    End If

                    If version <> 2UI Then
                        Throw New InvalidDataException("只支持 glTF 2.0 的 GLB，实际版本: " & version)
                    End If

                    Dim limit As Long = System.Math.Min(CLng(totalLength), stream.Length)

                    While stream.Position + 8 <= limit
                        Dim chunkLength As Integer = CInt(reader.ReadUInt32())
                        Dim chunkType As UInteger = reader.ReadUInt32()

                        If chunkLength <= 0 Then Continue While
                        If stream.Position + chunkLength > stream.Length Then Exit While

                        Dim data As Byte() = reader.ReadBytes(chunkLength)

                        Select Case chunkType
                            Case ChunkJson
                                container.Json = Encoding.UTF8.GetString(data).TrimEnd(ChrW(0), " "c)
                            Case ChunkBin
                                container.Binary = data
                        End Select
                    End While
                End Using
            End Using

            Return container
        End Function

        Private Function ReadVec3(root As GltfRoot, buffers As List(Of Byte()), accessorIndex As Integer) As num.Vector3()
            Dim accessor As Accessor = root.Accessors(accessorIndex)

            If accessor Is Nothing OrElse Not accessor.BufferViewIndex.HasValue Then Return Nothing

            Dim view As BufferView = root.BufferViews(accessor.BufferViewIndex.Value)

            If view Is Nothing Then Return Nothing
            If view.BufferIndex < 0 OrElse view.BufferIndex >= buffers.Count Then Return Nothing

            Dim data As Byte() = buffers(view.BufferIndex)

            If data Is Nothing Then Return Nothing

            Dim baseOffset As Integer = If(view.ByteOffset, 0) + If(accessor.ByteOffset, 0)
            Dim componentSize As Integer = accessor.ComponentSize
            Dim stride As Integer = If(view.ByteStride, componentSize * 3)
            Dim count As Integer = accessor.Count

            If count <= 0 Then Return Nothing

            Dim result(count - 1) As num.Vector3

            For i As Integer = 0 To count - 1
                Dim offset As Integer = baseOffset + i * stride

                If offset + 12 > data.Length Then Exit For

                result(i) = New num.Vector3(
                    BitConverter.ToSingle(data, offset),
                    BitConverter.ToSingle(data, offset + 4),
                    BitConverter.ToSingle(data, offset + 8))
            Next

            Return result
        End Function

        Private Function ReadIndices(root As GltfRoot, buffers As List(Of Byte()), accessorIndex As Integer) As Integer()
            Dim accessor As Accessor = root.Accessors(accessorIndex)

            If accessor Is Nothing OrElse Not accessor.BufferViewIndex.HasValue Then Return Nothing

            Dim view As BufferView = root.BufferViews(accessor.BufferViewIndex.Value)

            If view Is Nothing Then Return Nothing
            If view.BufferIndex < 0 OrElse view.BufferIndex >= buffers.Count Then Return Nothing

            Dim data As Byte() = buffers(view.BufferIndex)

            If data Is Nothing Then Return Nothing

            Dim baseOffset As Integer = If(view.ByteOffset, 0) + If(accessor.ByteOffset, 0)
            Dim componentSize As Integer = accessor.ComponentSize
            Dim stride As Integer = If(view.ByteStride, componentSize)
            Dim count As Integer = accessor.Count

            If count <= 0 Then Return Nothing

            Dim result(count - 1) As Integer

            For i As Integer = 0 To count - 1
                Dim offset As Integer = baseOffset + i * stride

                If offset + componentSize > data.Length Then Exit For

                Select Case accessor.ComponentType
                    Case 5121
                        result(i) = CInt(data(offset))
                    Case 5123
                        result(i) = CInt(BitConverter.ToUInt16(data, offset))
                    Case 5125
                        result(i) = CInt(BitConverter.ToUInt32(data, offset))
                    Case Else
                        Return Nothing
                End Select
            Next

            Return result
        End Function

    End Module

End Namespace
