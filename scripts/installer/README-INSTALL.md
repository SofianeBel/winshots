# Winshots Setup and Portable Package

For normal installation, run the setup executable from the release:

```text
winshots-1.0.1-win-x64-setup.exe
```

The setup installs Winshots under the local user profile, creates Start Menu shortcuts, and registers an Apps & Features uninstaller.

Extract the release archive, then run the connected UI directly:

```powershell
.\Winshots Review UI.cmd
```

This starts the C# host, Electron review UI, global shortcuts, overlay/session controls, and local MCP files together.

The portable package also keeps a script fallback for local installs:

```powershell
.\install.ps1
```

The default install location is:

```text
%LOCALAPPDATA%\Programs\Winshots
```

The local install copies the Windows app, the MCP server, and the Codex plugin files. It does not touch Codex plugin cache by default.

Captures and visual sessions are not stored in the install directory. They stay under:

```text
%USERPROFILE%\Documents\Winshots\captures
%USERPROFILE%\Documents\Winshots\sessions
```

To refresh the Codex plugin after closing Codex:

```powershell
.\install.ps1 -InstallCodexPlugin
```

To uninstall:

```powershell
.\uninstall.ps1
```
