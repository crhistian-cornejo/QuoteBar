# QuoteBar for Windows

![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)
![Windows](https://img.shields.io/badge/Windows-10%2B-informational?logo=windows)
![.NET](https://img.shields.io/badge/.NET-9.0-purple.svg)
![Platform](https://img.shields.io/badge/Platform-WinUI%203-lightgrey.svg)

WinUI 3 application that monitors AI provider usage from the system tray. Track quotas, spending, and status across Claude, Codex, Cursor, Gemini, Copilot, Droid, z.ai, Antigravity, Augment, and MiniMax.

![PopOver](../docs/images/PopOver.png)

## Overview

QuoteBar runs in your system tray and shows real-time usage metrics. View daily/weekly limits, track costs, and monitor service status without interrupting your workflow.

## Features

- System tray integration with live usage indicators
- Multi-provider support (10+ AI services)
- Secure credential storage (Windows Credential Manager)
- Auto-update via GitHub Releases
- Cost tracking with 30‑day history
- Provider ordering and custom settings
- Native Windows 11 design (Mica, WinUI 3)
- Dark/light theme (system-aware)
- Service status monitoring (incident alerts)
- Manual and auto‑refresh modes
- Keyboard shortcuts

## Supported Providers

| Provider | Auth | Limits | Cost |
|----------|--------|---------|-------|
| [Claude](../docs/claude.md) | OAuth/CLI | Session + Weekly | ✅ |
| [Codex](../docs/codex.md) | OAuth | Usage + Billing | - |
| [Cursor](../docs/cursor.md) | API | Billing cycle | ✅ |
| [Gemini](../docs/gemini.md) | OAuth | Quota limits | - |
| [Copilot](../docs/copilot.md) | OAuth | Plan usage | - |
| [Droid](../docs/droid.md) | API | Billing cycle | - |
| [z.ai](../docs/zai.md) | API Token | Quota limits | - |
| [Antigravity](../docs/antigravity.md) | Local probe | Session + Weekly | - |
| [Augment](../docs/augment.md) | Cookies | Session + Credits | - |
| [MiniMax](../docs/minimax.md) | Cookies | Quota limits | - |

## Settings

Configure QuoteBar from the system tray menu:

- **Providers** – Toggle visibility, configure authentication, reorder providers
- **Cost Tracking** – View 30‑day spending history with daily breakdowns

![CostTracking](../docs/images/CostTracking.png)

- **Provider Order** – Arrange providers in your preferred order
- **General** – Refresh intervals, startup, keyboard shortcuts
- **Appearance** – Theme, compact mode, accent colors
- **Notifications** – Usage alerts, sounds, threshold settings

![Providers](../docs/images/Providers.png)

## Install

### Requirements

- Windows 10 (19041) or later
- Windows 11 recommended for best experience

### Quick Install

Download the latest release from [GitHub Releases](https://github.com/crhistian-cornejo/QuoteBar/releases) and run `QuoteBar.exe`.

### Development Build

```powershell
cd QuoteBar
.\dev.ps1 run
```

## Development

### Build

```powershell
# Release build
.\dev.ps1 build

# Run in release mode
.\dev.ps1 run

# Watch mode (hot reload)
.\dev.ps1 watch

# Clean
.\dev.ps1 clean

# Publish for distribution
.\dev.ps1 publish
```

### Project Details

**Technology Stack**
- UI: WinUI 3 (Windows App SDK 1.6)
- Language: C# (.NET 9.0)
- Architecture: MVVM with CommunityToolkit.Mvvm
- XAML: Compiled XAML with hot reload

**Key Patterns**
- Provider registry with fallback strategies
- MVVM with observable properties
- Dependency injection via IServiceProvider
- Async/await throughout
- Settings persistence via JSON
- Secure credentials via Windows Credential Manager

### Adding a New Provider

1. Create provider folder in `Core/Providers/YourProvider/`
2. Implement `IProviderDescriptor` with fetch strategies
3. Register in `ProviderRegistry.RegisterDefaultProviders()`
4. Add to visibility toggles in `ProvidersSettingsPage.cs`
5. Add icon in `Assets/icons/yourprovider.svg`

For dynamic settings, implement `IProviderWithSettings`:

```csharp
public class MyProviderSettings : ProviderSettingsBase
{
    public override List<ProviderSettingDefinition> GetSettingDefinitions()
    {
        return new()
        {
            new ProviderSettingDefinition
            {
                Key = "timeout",
                DisplayName = "Request Timeout",
                Type = ProviderSettingType.NumberBox,
                DefaultValue = "30",
                MinValue = 5,
                MaxValue = 120
            }
        };
    }

    public override Task ApplySettingAsync(string key, string? value)
    {
        // Apply to provider
    }

    public override Task<string?> GetSettingValueAsync(string key)
    {
        // Return current value
    }
}
```

### Releasing

1. Bump version in `QuoteBar.csproj`
2. Build: `.\dev.ps1 release`
3. Create GitHub release with tag
4. Upload `QuoteBar-*.zip` from `bin/Release/`
5. Auto‑updater will notify users

## Storage

### Settings

```
%LocalAppData%\QuoteBar\settings.json
```

### Usage History

```
%LocalAppData%\QuoteBar\usage_history.json
```

### Credentials

API tokens stored in Windows Credential Manager (DPAPI encrypted).

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Win + Shift + Q` | Toggle popup |
| `1`‑`9` | Switch provider |
| `R` | Refresh data |
| `D` | Open dashboard |
| `S` | Open settings |
| `P` | Pin popup |
| `Esc` | Close popup |
| `?` | Show help |

## Privacy & Security

- API tokens stored in Windows Credential Manager (encrypted)
- Browser cookies reused when enabled (no password storage)
- No data sent to external servers except provider APIs
- Local storage only (settings + usage history)
- Open source: [GitHub](https://github.com/crhistian-cornejo/QuoteBar)

## Documentation

- [Provider setup guides](../docs/)
- [Architecture overview](../docs/architecture.md)
- [Settings reference](../docs/settings.md)
- [Release process](../docs/releasing.md)

## Credits

Inspired by [CodexBar](https://github.com/steipete/CodexBar) for macOS.

## License

MIT

---

![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)
![Windows](https://img.shields.io/badge/Windows-10%2B-informational?logo=windows)
![.NET](https://img.shields.io/badge/.NET-9.0-purple.svg)
![Platform](https://img.shields.io/badge/Platform-WinUI%203-lightgrey.svg)

![GitHub release](https://img.shields.io/github/v/release/crhistian-cornejo/QuoteBar?display_name=release)
![GitHub stars](https://img.shields.io/github/stars/crhistian-cornejo/QuoteBar?style=social)
![GitHub forks](https://img.shields.io/github/forks/crhistian-cornejo/QuoteBar?style=social)
![GitHub issues](https://img.shields.io/github/issues/crhistian-cornejo/QuoteBar)
