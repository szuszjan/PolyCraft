Imports System.Media
Imports System.Threading.Tasks
Imports System.Windows
Imports System.Windows.Media
Imports System.Windows.Media.Animation

Namespace PolyCraft

Partial Class SplashWindow

    Private Shared ReadOnly LogoLines() As String = {
        "█████ █████ █     █   █ █████ █████ █████ █████ █████",
        "█   █ █   █ █      █ █  █     █   █ █   █ █       █  ",
        "█████ █   █ █       █   █     █████ █████ █████   █  ",
        "█     █   █ █       █   █     █  █  █   █ █       █  ",
        "█     █████ █████   █   █████ █   █ █   █ █       █  "
    }

    Private chimePlayer As SoundPlayer

    Public Sub New()
        InitializeComponent()
        Dim text = String.Join(Environment.NewLine, LogoLines)
        LogoBack.Text = text
        LogoMid.Text = text
        LogoFront.Text = text
        AddHandler Me.Loaded, AddressOf SplashWindow_Loaded
    End Sub

    Private Sub PlayStartupChime()
        Try
            chimePlayer = New SoundPlayer(New IO.MemoryStream(SoundGen.BuildStartupChimeWav()))
            chimePlayer.Play()
        Catch
            ' No audio device or playback failure - non-critical, ignore.
        End Try
    End Sub

    Private Async Sub SplashWindow_Loaded(sender As Object, e As RoutedEventArgs)
        PlayStartupChime()

        Dim fadeIn As New DoubleAnimation(0, 1, New Duration(TimeSpan.FromMilliseconds(700)))
        Me.BeginAnimation(Window.OpacityProperty, fadeIn)

        Dim scaleIn As New DoubleAnimation(0.8, 1.0, New Duration(TimeSpan.FromMilliseconds(700)))
        scaleIn.EasingFunction = New BackEase With {.EasingMode = EasingMode.EaseOut, .Amplitude = 0.4}
        LogoScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn)
        LogoScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn)

        Await Task.Delay(700)

        Await Task.Delay(1100)

        Dim fadeOut As New DoubleAnimation(1, 0, New Duration(TimeSpan.FromMilliseconds(600)))
        Me.BeginAnimation(Window.OpacityProperty, fadeOut)

        Dim scaleOut As New DoubleAnimation(1.0, 1.1, New Duration(TimeSpan.FromMilliseconds(600)))
        scaleOut.EasingFunction = New QuadraticEase With {.EasingMode = EasingMode.EaseIn}
        LogoScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleOut)
        LogoScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleOut)

        Await Task.Delay(600)

        Me.Close()
    End Sub

End Class

End Namespace
