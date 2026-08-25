Imports System.IO
Imports System.Text

''' <summary>Synthesizes a short startup chime as an in-memory WAV, so the app needs no
''' external audio asset.</summary>
Public Module SoundGen

    Public Function BuildStartupChimeWav() As Byte()
        Const sampleRate As Integer = 44100
        Dim notes As (Freq As Double, Duration As Double)() = {
            (523.25, 0.16),  ' C5
            (659.25, 0.16),  ' E5
            (783.99, 0.3)    ' G5 (final note, held a bit longer)
        }

        Dim samples As New List(Of Short)()
        For Each note In notes
            Dim n = CInt(sampleRate * note.Duration)
            Dim attack = CInt(sampleRate * 0.01)
            Dim release = CInt(sampleRate * 0.08)
            For i = 0 To n - 1
                Dim t = i / CDbl(sampleRate)
                Dim env As Double = 1.0
                If i < attack Then
                    env = i / CDbl(attack)
                ElseIf i > n - release Then
                    env = Math.Max(0, (n - i) / CDbl(release))
                End If
                Dim value = Math.Sin(2 * Math.PI * note.Freq * t) * env * 0.5
                samples.Add(CShort(value * Short.MaxValue))
            Next
        Next

        Return WrapPcmAsWav(samples, sampleRate)
    End Function

    Private Function WrapPcmAsWav(samples As List(Of Short), sampleRate As Integer) As Byte()
        Dim ms As New MemoryStream()
        Dim bw As New BinaryWriter(ms, Encoding.UTF8, leaveOpen:=True)

        Dim channels As Short = 1
        Dim bitsPerSample As Short = 16
        Dim byteRate = sampleRate * channels * (bitsPerSample \ 8)
        Dim blockAlign As Short = CShort(channels * (bitsPerSample \ 8))
        Dim dataSize = samples.Count * (bitsPerSample \ 8)

        bw.Write(Encoding.ASCII.GetBytes("RIFF"))
        bw.Write(CUInt(36 + dataSize))
        bw.Write(Encoding.ASCII.GetBytes("WAVE"))

        bw.Write(Encoding.ASCII.GetBytes("fmt "))
        bw.Write(CUInt(16))
        bw.Write(CShort(1)) ' PCM
        bw.Write(channels)
        bw.Write(CUInt(sampleRate))
        bw.Write(CUInt(byteRate))
        bw.Write(blockAlign)
        bw.Write(bitsPerSample)

        bw.Write(Encoding.ASCII.GetBytes("data"))
        bw.Write(CUInt(dataSize))
        For Each s In samples
            bw.Write(s)
        Next

        bw.Flush()
        Return ms.ToArray()
    End Function

End Module
