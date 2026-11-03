Imports System.Numerics
Imports MicroduckLab.Core

Namespace Render

    ''' <summary>
    ''' The orbit camera and the chase camera of the playground.
    ''' </summary>
    ''' <remarks>
    ''' The renderer of the drawing library projects with
    ''' ``factor = fov / (z + viewDistance)``, that is a pseudo perspective whose
    ''' field of view is expressed in pixels at the plane z = 0 of the camera. The
    ''' rig therefore keeps the target of the camera at the origin of the projected
    ''' space (the world is translated by minus the target) and converts a real
    ''' field of view in degrees into that pixel factor.
    '''
    ''' The camera itself is the ``Camera`` of the imaging library, so the angles
    ''' are degrees and the rotation is the one of the cpu painter.
    ''' </remarks>
    Public Class CameraRig

        ''' <summary>how far the eye may come to the target, metres</summary>
        Public Const MinDistance As Double = 0.45
        ''' <summary>how far the eye may go from the target, metres</summary>
        Public Const MaxDistance As Double = 9.0
        ''' <summary>the vertical field of view of the chase camera, degrees</summary>
        Public Const ChaseFieldOfView As Double = 42.0
        ''' <summary>the pitch of the chase camera above the duck, degrees</summary>
        Public Const ChasePitch As Double = 24.0
        ''' <summary>the eye keeps this far behind the duck, metres</summary>
        Public Const ChaseDistance As Double = 1.35

        ''' <summary>the point that the camera looks at</summary>
        Public Property Target As Vector3 = Vector3.Zero
        ''' <summary>the heading of the eye around the target, degrees</summary>
        Public Property Azimuth As Double = 135
        ''' <summary>the elevation of the eye above the target, degrees</summary>
        Public Property Elevation As Double = 24
        ''' <summary>the distance of the eye from the target, metres</summary>
        Public Property Distance As Double = ChaseDistance
        ''' <summary>the vertical field of view, degrees</summary>
        Public Property FieldOfView As Double = ChaseFieldOfView
        ''' <summary>the chase camera follows the duck</summary>
        Public Property Chase As Boolean = True
        ''' <summary>the radian heading of the duck, the chase camera follows it</summary>
        Public Property Heading As Double

        Private m_first As Boolean = True

        ''' <summary>
        ''' follow the duck of the snapshot
        ''' </summary>
        ''' <param name="position">the centre of the duck</param>
        ''' <param name="heading">the yaw of the duck, radians</param>
        ''' <param name="dt">the frame time, seconds</param>
        Public Sub Update(position As Vector3, heading As Double, dt As Double)
            If m_first Then
                Target = position
                Me.Heading = heading
                m_first = False
            End If

            ' the target follows the duck faster vertically than horizontally,
            ' so a hop does not swing the whole frame around
            Dim follow As Double = Ease(dt, 6.0)
            Dim current As Vector3 = Target

            Target = New Vector3(
                current.X + (position.X - current.X) * CSng(follow),
                current.Y + (position.Y - current.Y) * CSng(follow),
                current.Z + (position.Z - current.Z) * CSng(Ease(dt, 9.0)))

            If Chase Then
                ' the heading is an exponential itself so that the gait swing of
                ' the trunk does not shake the frame
                Me.Heading += Wrap(heading - Me.Heading) * Ease(dt, 2.2)

                ' the duck faces its local +X, so the eye sits opposite to that
                ' heading and looks along it
                Dim wanted As Double = Me.Heading * 180.0 / Math.PI + 180.0
                Dim turn As Double = WrapDegrees(wanted - Azimuth)

                Azimuth += turn * Ease(dt, 2.4)
                Elevation += (ChasePitch - Elevation) * Ease(dt, 3.0)
                Distance += (ChaseDistance - Distance) * Ease(dt, 2.0)
            End If
        End Sub

        ''' <summary>the eye of the camera, in world space</summary>
        Public Function Eye() As Vector3
            Dim azimuth As Double = Azimuth * Math.PI / 180.0
            Dim elevation As Double = Elevation * Math.PI / 180.0
            Dim horizontal As Double = Math.Cos(elevation) * Distance

            Return Target + New Vector3(
                CSng(horizontal * Math.Cos(azimuth)),
                CSng(horizontal * Math.Sin(azimuth)),
                CSng(Math.Sin(elevation) * Distance))
        End Function

        ''' <summary>
        ''' orbit the eye around the target, in degrees, and detach the chase
        ''' camera
        ''' </summary>
        Public Sub Orbit(deltaAzimuth As Double, deltaElevation As Double)
            Chase = False
            Azimuth += deltaAzimuth
            Elevation = Phys.Clamp(Elevation + deltaElevation, MinElevation, MaxElevation)
        End Sub

        ''' <summary>how low the eye may go, degrees (below the horizon)</summary>
        Public Const MinElevation As Double = -85.0
        ''' <summary>how high the eye may go, degrees (straight down on the floor)</summary>
        Public Const MaxElevation As Double = 89.0

        ''' <summary>
        ''' put the eye straight above the duck, the birds eye view of the arena
        ''' </summary>
        Public Sub LookDown()
            Chase = False
            Elevation = MaxElevation - 3.0
        End Sub

        ''' <summary>
        ''' back to a three quarter view at the height of the duck
        ''' </summary>
        Public Sub LookLevel()
            Chase = False
            Elevation = 12.0
        End Sub

        ''' <summary>zoom the eye towards or away from the target</summary>
        Public Sub Zoom(steps As Double)
            Distance = Phys.Clamp(Distance * Math.Pow(1.12, steps), MinDistance, MaxDistance)
        End Sub

        ''' <summary>snap the chase camera back on</summary>
        Public Sub AttachChase()
            Chase = True
        End Sub

        ''' <summary>
        ''' put the eye back into the presentation pose, the sliding of the
        ''' respawn of the browser build
        ''' </summary>
        Public Sub ResetView()
            m_first = True
            Chase = True
        End Sub

        ''' <summary>
        ''' the transform that moves the world into the view of the camera.
        ''' </summary>
        ''' <remarks>
        ''' The camera of the drawing library only knows a pitch (``AngleX``), and
        ''' it applies that pitch about the x axis after every other rotation, so a
        ''' yaw cannot be expressed with its angles: the world is turned about its
        ''' own z axis here instead, and the camera keeps the pure pitch that puts
        ''' the z axis of the model up on the screen.
        '''
        ''' The turn has to bring the eye direction (the azimuth) onto the +y axis
        ''' of the camera space, which the pitch then tips towards the viewer.
        ''' </remarks>
        Public Function ViewTranslation() As Matrix4x4
            Dim turn As Double = (90.0 - Azimuth) * Math.PI / 180.0

            Return Matrix4x4.CreateTranslation(-Target) *
                   Matrix4x4.CreateRotationZ(CSng(turn))
        End Function

        ''' <summary>
        ''' the pixel factor that the pseudo perspective of the library needs for
        ''' the requested vertical field of view and the given viewport height.
        ''' </summary>
        ''' <remarks>
        ''' The projection of the library is ``x_pixels = factor * x / (z + distance)``,
        ''' so the factor is the number of pixels that one metre at unit distance
        ''' covers. Dividing by the distance is the job of the projection: folding
        ''' the distance into the factor as well would keep the target the same
        ''' size at every zoom, which is the opposite of what a zoom does.
        ''' </remarks>
        Public Function PixelFactor(viewportHeight As Integer) As Double
            Dim half As Double = FieldOfView * Math.PI / 360.0

            Return (Math.Max(viewportHeight, 1) * 0.5) / Math.Tan(half)
        End Function

        Private Shared Function Ease(dt As Double, rate As Double) As Double
            Dim t As Double = 1.0 - Math.Exp(-rate * Math.Max(dt, 0))

            Return If(t < 0, 0, If(t > 1, 1, t))
        End Function

        Private Shared Function Wrap(radians As Double) As Double
            While radians > Math.PI
                radians -= 2 * Math.PI
            End While

            While radians < -Math.PI
                radians += 2 * Math.PI
            End While

            Return radians
        End Function

        Private Shared Function WrapDegrees(degrees As Double) As Double
            While degrees > 180
                degrees -= 360
            End While

            While degrees < -180
                degrees += 360
            End While

            Return degrees
        End Function
    End Class
End Namespace
