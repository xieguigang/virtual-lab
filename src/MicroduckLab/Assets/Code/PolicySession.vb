Imports System.Diagnostics
Imports System.IO
Imports Microsoft.VisualBasic.MachineLearning.DataStorage.ONNX

Namespace Assets

    ''' <summary>
    ''' One trained policy: the onnx graph, its evaluator and the names of its
    ''' input and output tensors.
    ''' </summary>
    ''' <remarks>
    ''' The browser build ran onnxruntime-web. The desktop build evaluates the
    ''' graph with the built in evaluator of MLDataStorage, which is a pure
    ''' managed, double precision implementation of the onnx operators. Every
    ''' policy of this robot is a small mlp (``Gemm``/``Elu`` plus the folded
    ''' normalisation), so the evaluator covers it completely - the operator list
    ''' is checked at load time instead of failing in the middle of the gait.
    ''' </remarks>
    Public Class PolicySession

        Private Shared ReadOnly Supported As New HashSet(Of String)(StringComparer.Ordinal) From {
            "Identity", "Dropout", "Constant", "Add", "Sub", "Mul", "Div", "Pow", "MatMul", "Gemm",
            "Relu", "LeakyRelu", "Elu", "Selu", "Sigmoid", "HardSigmoid", "Tanh", "Softplus", "Softsign",
            "Exp", "Log", "Sqrt", "Neg", "Abs", "Floor", "Ceil", "Softmax", "Cast", "Conv", "ConvTranspose",
            "MaxPool", "AveragePool", "GlobalAveragePool", "LRN", "Flatten", "Reshape", "Transpose",
            "Concat", "Squeeze", "Unsqueeze", "Shape", "Gather", "ReduceSum", "ReduceMean"
        }

        Public ReadOnly Property Name As String
        Public ReadOnly Property Path As String
        Public ReadOnly Property InputName As String
        Public ReadOnly Property OutputName As String
        Public ReadOnly Property InputSize As Integer
        Public ReadOnly Property OutputSize As Integer
        ''' <summary>the wall clock time of the last inference, milliseconds</summary>
        Public Property LastInferenceMs As Double

        Private ReadOnly m_model As OnnxModel
        Private ReadOnly m_evaluator As OnnxGraphEvaluator
        Private ReadOnly m_input As New Dictionary(Of String, OnnxTensor)()
        Private m_buffer As Double()

        Public Sub New(name As String, path As String, inputSize As Integer, outputSize As Integer)
            Me.Name = name
            Me.Path = path
            Me.InputSize = inputSize
            Me.OutputSize = outputSize
            m_buffer = New Double(inputSize - 1) {}

            If Not File.Exists(path) Then
                Throw New FileNotFoundException($"the policy '{name}' is missing from {Path}", path)
            End If

            m_model = OnnxModel.Load(path)

            If m_model.Inputs Is Nothing OrElse m_model.Inputs.Length = 0 Then
                Throw New InvalidDataException($"the policy '{name}' declares no input")
            End If

            If m_model.Outputs Is Nothing OrElse m_model.Outputs.Length = 0 Then
                Throw New InvalidDataException($"the policy '{name}' declares no output")
            End If

            InputName = m_model.Inputs(0).Name
            OutputName = m_model.Outputs(0).Name
            m_evaluator = New OnnxGraphEvaluator(m_model)
        End Sub

        ''' <summary>
        ''' check that every operator of the graph is available and that the graph
        ''' answers a finite action
        ''' </summary>
        ''' <exception cref="InvalidOperationException">the graph can not be evaluated here</exception>
        Public Sub Validate()
            Dim operators As String() = m_model.GetOpTypes()
            Dim missing As New List(Of String)()

            For Each op As String In operators
                If Not Supported.Contains(op) Then
                    Call missing.Add(op)
                End If
            Next

            If missing.Count > 0 Then
                Throw New InvalidOperationException(
                    $"the policy '{Name}' needs the operator(s) {String.Join(", ", missing.Distinct())}, which the managed evaluator does not implement")
            End If

            Dim action As Double() = Run(New Double(InputSize - 1) {})

            If action.Length <> OutputSize Then
                Throw New InvalidDataException($"the policy '{Name}' answered {action.Length} values, expected {OutputSize}")
            End If

            For Each value As Double In action
                If Double.IsNaN(value) OrElse Double.IsInfinity(value) Then
                    Throw New InvalidDataException($"the policy '{Name}' is not finite on a zero observation")
                End If
            Next
        End Sub

        ''' <summary>
        ''' one forward pass of the policy
        ''' </summary>
        ''' <param name="observation">the observation, <see cref="InputSize"/> values</param>
        ''' <returns>the raw action, <see cref="OutputSize"/> values</returns>
        ''' <remarks>
        ''' The raw action of the network is returned: the caller scales it and
        ''' adds the standing pose, exactly like the browser build does. The raw
        ''' action is also what the next observation carries as ``last_action``.
        ''' </remarks>
        Public Function Run(observation As Double()) As Double()
            Dim count As Integer = Math.Min(observation.Length, InputSize)

            Array.Copy(observation, m_buffer, count)

            If count < InputSize Then
                Array.Clear(m_buffer, count, InputSize - count)
            End If

            m_input(InputName) = New OnnxTensor(New Integer() {1, InputSize}, m_buffer)

            Dim clock As Stopwatch = Stopwatch.StartNew()
            Dim outputs As Dictionary(Of String, OnnxTensor) = m_evaluator.Run(m_input)
            Dim tensor As OnnxTensor = Nothing

            clock.Stop()
            LastInferenceMs = clock.Elapsed.TotalMilliseconds

            If Not outputs.TryGetValue(OutputName, tensor) Then
                If outputs.Count = 0 Then
                    Throw New InvalidDataException($"the policy '{Name}' produced no output")
                End If

                tensor = outputs.Values.First()
            End If

            Return tensor.Data
        End Function

        ''' <summary>
        ''' a one line summary of the graph for the boot console
        ''' </summary>
        Public ReadOnly Property Summary As String
            Get
                Return $"{InputName}[{InputSize}] -> {OutputName}[{OutputSize}], {m_model.GetOpTypes().Distinct().Count()} operator kinds"
            End Get
        End Property
    End Class
End Namespace
