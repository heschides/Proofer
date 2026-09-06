# Sati desktop installers

The repository builds two separate per-user Windows installers:

- `Build-DemoInstaller.ps1` packages the cloud/API-backed Demo client as
  `SatiDemoSetup-x.y.z.exe`. It contains only the public HTTPS API mapping.
- `Build-LocalInstaller.ps1` packages the Local Production/LocalDB client as
  `SatiLocalSetup-x.y.z.exe`. It embeds a Microsoft-signed `SqlLocalDB.msi`, contains the
  workstation database mapping, and requires Windows integrated security; the builder rejects SQL
  credentials and rejects an unsigned or non-Microsoft LocalDB prerequisite.

They install side by side under `%LOCALAPPDATA%\Programs\SatiLogica\Sati Demo` and
`%LOCALAPPDATA%\Programs\SatiLogica\Sati`, with separate shortcuts and uninstall entries.

Build both from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\installer\Build-DemoInstaller.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\installer\Build-LocalInstaller.ps1 `
    -LocalDbMsiPath C:\path\to\SqlLocalDB.msi
```

Validate the LocalDB install payload without touching the normal installation:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
    .\scripts\Test-LocalInstaller.ps1 `
    -InstallerPath .\artifacts\SatiLocalInstaller\SatiLocalSetup-1.3.0.exe
```

On a clean workstation, the combined installer requests elevation only when LocalDB is absent. Sati
then creates an empty `SatiProduction` database, applies controlled migrations, writes the Production
identity marker, and opens guarded first-administrator setup. An existing database is never adopted
or relabeled by this path.

## API-backed Demo installer

`Build-DemoInstaller.ps1` publishes a self-contained Windows x64 Demo client and wraps it in a per-user installer.

The Demo build:

- launches directly into the Azure Demo environment;
- connects only to the public HTTPS API configured in `appsettings.Public.json`;
- does not package `appsettings.json`, a SQL connection string, or an Azure credential;
- installs under `%LOCALAPPDATA%\Programs\SatiLogica\Sati Demo`;
- creates Start Menu and Desktop shortcuts and a Windows uninstall entry;
- includes the .NET runtime, so the receiving computer does not need a separate .NET installation.
- exposes the installed version and release notes from the Settings window.

Build from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\installer\Build-DemoInstaller.ps1
```

The installer and its SHA-256 checksum are written to `artifacts\SatiDemoInstaller`.

Run the isolated installation and launch acceptance test from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
    .\scripts\Test-DemoInstaller.ps1 `
    -InstallerPath .\artifacts\SatiDemoInstaller\SatiDemoSetup-1.3.0.exe `
    -LaunchIterations 5
```

The acceptance test requires each packaged launch to remain responsive, accept a normal window-close
request, exit with code zero, and leave no isolated installation behind.

The generated installer is not code-signed. Windows may display an Unknown publisher or SmartScreen warning until the executable is signed with a trusted code-signing certificate.
