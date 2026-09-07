Option Explicit

If WScript.Arguments.Count <> 1 Then
    WScript.Quit 64
End If

Dim scriptName
scriptName = WScript.Arguments(0)
If InStr(scriptName, "\") > 0 Or InStr(scriptName, "/") > 0 Or InStr(scriptName, "..") > 0 Then
    WScript.Quit 64
End If

Dim shell, fileSystem, scriptPath, powershellPath, command, exitCode
Set shell = CreateObject("WScript.Shell")
Set fileSystem = CreateObject("Scripting.FileSystemObject")
scriptPath = fileSystem.BuildPath(fileSystem.GetParentFolderName(WScript.ScriptFullName), scriptName)
If Not fileSystem.FileExists(scriptPath) Then
    WScript.Quit 2
End If

powershellPath = shell.ExpandEnvironmentStrings("%SystemRoot%") & "\System32\WindowsPowerShell\v1.0\powershell.exe"
command = Chr(34) & powershellPath & Chr(34) & _
    " -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File " & _
    Chr(34) & scriptPath & Chr(34)
exitCode = shell.Run(command, 0, True)
WScript.Quit exitCode
