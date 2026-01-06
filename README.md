# QuoteBar (NativeBar for Windows)

A native Windows application for monitoring AI provider usage quotas. Track your usage across Claude, Codex, Cursor, Gemini, Copilot, Droid, z.ai and more.

## Features

- System tray integration with usage indicator
- Real-time usage monitoring for multiple AI providers
- Secure API token storage using Windows Credential Manager
- Dark/Light theme support (follows system theme)
- Native Windows 11 design with Mica backdrop
- Multi-provider support with tabbed interface

## Supported Providers

- **Claude** (Anthropic) - CLI integration
- **Codex** (OpenAI) - CLI integration
- **Cursor** - API integration
- **Gemini** (Google) - API integration
- **Copilot** (GitHub) - API integration
- **Droid** - API integration
- **z.ai** - API token authentication

## Requirements

- Windows 10 (19041) or later
- .NET 9.0 SDK
- Visual Studio 2022 or VS Code with C# extension

## Development

### Quick Start

```powershell
# Build and run in release mode
.\dev.cmd run

# Build only
.\dev.cmd build

# Clean build outputs
.\dev.cmd clean

# Publish for distribution
.\dev.cmd publish
```

### Using PowerShell directly

```powershell
# Build and run
.\dev.ps1 run

# Watch mode (hot reload)
.\dev.ps1 watch

# Clean
.\dev.ps1 clean

# Build release
.\dev.ps1 release

# Publish self-contained
.\dev.ps1 publish
```

### Manual Build

```bash
cd NativeBar.WinUI
dotnet build -c Release
dotnet run
```

## Project Structure

```
QuoBarWin/
├── NativeBar.sln           # Solution file
├── dev.ps1                 # Development script (PowerShell)
├── dev.cmd                 # Development script (Batch wrapper)
└── NativeBar.WinUI/        # Main WinUI 3 application
    ├── Core/
    │   ├── Models/         # Data models
    │   ├── Providers/      # Provider implementations
    │   └── Services/       # Core services
    ├── TrayPopup/          # Tray popup window
    ├── ViewModels/         # MVVM view models
    └── Assets/             # Icons and resources
```

## Security

API tokens are stored securely using Windows Credential Manager, not in plain text files. The application never logs or exposes sensitive credentials.

## License

MIT License
