Imports System.Windows

Namespace PolyCraft

Partial Class App

    Private Sub Application_Startup(sender As Object, e As StartupEventArgs)
        Dim splash As New SplashWindow()
        AddHandler splash.Closed, Sub(s, ev)
            Dim main As New MainWindow()
            Me.MainWindow = main
            Me.ShutdownMode = ShutdownMode.OnLastWindowClose
            main.Show()
        End Sub
        splash.Show()
    End Sub

End Class

End Namespace
