Imports System.IO

Namespace Assets

    ''' <summary>
    ''' Resolves the files of the sim out of the ``Assets`` folder that travels
    ''' next to the executable.
    ''' </summary>
    ''' <remarks>
    ''' The layout is the historical one of the web build (``robot/mjlab``,
    ''' ``policies``, ``assets``), so a file of the browser version can be
    ''' compared with the desktop one by its name alone.
    ''' </remarks>
    Public Module AssetPath

        Private ReadOnly m_root As String = ResolveRoot()

        ''' <summary>
        ''' the absolute path of the ``Assets`` folder
        ''' </summary>
        Public ReadOnly Property Root As String
            Get
                Return m_root
            End Get
        End Property

        Private Function ResolveRoot() As String
            Dim candidates As New List(Of String)()

            Call candidates.Add(Path.Combine(AppContext.BaseDirectory, "Assets"))

            ' a run from the ide starts the exe out of bin/<platform>/<config>,
            ' so the project folder is searched upwards as well
            Dim dir As New DirectoryInfo(AppContext.BaseDirectory)

            For i As Integer = 0 To 5
                If dir Is Nothing Then
                    Exit For
                End If

                Call candidates.Add(Path.Combine(dir.FullName, "Assets"))
                dir = dir.Parent
            Next

            For Each candidate As String In candidates
                If Directory.Exists(Path.Combine(candidate, "robot")) Then
                    Return Path.GetFullPath(candidate)
                End If
            Next

            ' the diagnostics of a missing asset folder are much easier to read
            ' when the expected path is part of the message
            Return Path.GetFullPath(candidates(0))
        End Function

        ''' <summary>
        ''' the ``robot/mjlab`` folder: the kinematics documents, the MJCF files
        ''' and the STL meshes of the two locomotion variants
        ''' </summary>
        Public ReadOnly Property RobotDirectory As String
            Get
                Return Path.Combine(m_root, "robot", "mjlab")
            End Get
        End Property

        ''' <summary>
        ''' the folder of the STL meshes
        ''' </summary>
        Public ReadOnly Property MeshDirectory As String
            Get
                Return Path.Combine(RobotDirectory, "meshes")
            End Get
        End Property

        ''' <summary>
        ''' the ``policies`` folder of the trained onnx policies
        ''' </summary>
        Public ReadOnly Property PolicyDirectory As String
            Get
                Return Path.Combine(m_root, "policies")
            End Get
        End Property

        ''' <summary>
        ''' the ``assets`` folder of the browser build: sound effects, voices,
        ''' stickers and the ui images
        ''' </summary>
        Public ReadOnly Property MediaDirectory As String
            Get
                Return Path.Combine(m_root, "assets")
            End Get
        End Property

        Public Function RobotFile(name As String) As String
            Return Path.Combine(RobotDirectory, name)
        End Function

        Public Function MeshFile(name As String) As String
            Return Path.Combine(MeshDirectory, name)
        End Function

        Public Function PolicyFile(name As String) As String
            Return Path.Combine(PolicyDirectory, name)
        End Function

        Public Function MediaFile(ParamArray parts As String()) As String
            Dim folder As String = MediaDirectory

            For Each part As String In parts
                folder = Path.Combine(folder, part)
            Next

            Return folder
        End Function

        ''' <summary>
        ''' a short relative path for the diagnostics of the boot console
        ''' </summary>
        Public Function Display(path As String) As String
            If String.IsNullOrEmpty(path) Then
                Return "(none)"
            End If

            If path.StartsWith(m_root, StringComparison.OrdinalIgnoreCase) Then
                Return "Assets\" & path.Substring(m_root.Length).TrimStart("\"c)
            End If

            Return path
        End Function
    End Module
End Namespace
