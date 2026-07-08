# Winshots Installer

Extract the release archive, then run:

```powershell
.\install.ps1
```

The default install location is:

```text
%LOCALAPPDATA%\Programs\Winshots
```

The installer copies the Windows app, the MCP server, and the Codex plugin files. If the Codex CLI is available, it refreshes `winshots@winshots-local` so new Codex threads can load the installed MCP server.

Captures and visual sessions are not stored in the install directory. They stay under:

```text
%USERPROFILE%\Documents\Winshots\captures
%USERPROFILE%\Documents\Winshots\sessions
```

To install without touching Codex plugin state:

```powershell
.\install.ps1 -SkipCodexPlugin
```

To uninstall:

```powershell
.\uninstall.ps1
```
