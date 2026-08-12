# Sati Demo installer

`Build-DemoInstaller.ps1` publishes a self-contained Windows x64 Demo client and wraps it in a per-user installer.

The Demo build:

- launches directly into the Azure Demo environment;
- connects only to the public HTTPS API configured in `appsettings.Public.json`;
- does not package `appsettings.json`, a SQL connection string, or an Azure credential;
- installs under `%LOCALAPPDATA%\Programs\Satilogica\Sati Demo`;
- creates Start Menu and Desktop shortcuts and a Windows uninstall entry;
- includes the .NET runtime, so the receiving computer does not need a separate .NET installation.

Build from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\installer\Build-DemoInstaller.ps1
```

The installer and its SHA-256 checksum are written to `artifacts\SatiDemoInstaller`.

The generated installer is not code-signed. Windows may display an Unknown publisher or SmartScreen warning until the executable is signed with a trusted code-signing certificate.
