# Architecture

## Overview

QuoteBar is a native Windows 11 application built with WinUI 3. It follows MVVM pattern and uses provider registry for extensibility.

## Core Components

### App Lifecycle

```
App (OnLaunched)
  ↓
HiddenWindow (COM handle)
  ↓
NotifyIconHelper (system tray)
  ↓
Services (initialized)
  ├─ UsageStore (data)
  ├─ UpdateService (updates)
  ├─ ProviderStatusService (polling)
  └─ NotificationService (toasts)
```

### Data Flow

```
ProviderRegistry
  ↓ (fetch strategies)
UsageFetcher
  ↓ (parse API/CLI)
UsageSnapshot
  ↓ (record)
UsageStore
  ↓ (notify)
ViewModels → UI
```

## Services

### UsageStore

Manages provider usage data and refresh intervals.

```csharp
// Get current snapshot
var snapshot = store.GetCurrentSnapshot();

// Refresh all providers
await store.RefreshAllAsync();

// Event-driven updates
store.AllProvidersRefreshed += () => UpdateTrayBadge();
```

### SettingsService

Persists app configuration via JSON.

```csharp
// Auto-save on any change
settings.Settings.AutoRefreshEnabled = true;
settings.Save(); // Triggers SettingsChanged event

// Subscribe to changes
settings.SettingsChanged += OnSettingsChanged;
```

### UpdateService

Handles GitHub Releases checking and update installation.

```csharp
// Start periodic checks (24h)
UpdateService.Instance.StartPeriodicChecks();

// Event when new version available
UpdateService.Instance.UpdateAvailable += (release) => ShowUpdateDialog();

// Download and install
var path = await UpdateService.Instance.DownloadUpdateAsync(release, progress);
UpdateService.Instance.PrepareUpdater(path, currentAppPath);
```

### ProviderStatusService

Polls statuspage.io for provider incidents.

```csharp
// Start polling (5 min intervals)
ProviderStatusService.Instance.StartPolling(300);

// Get current status
var status = ProviderStatusService.Instance.GetStatus("claude");
```

## Provider System

### Provider Descriptor

Each provider implements `IProviderDescriptor`:

```csharp
public interface IProviderDescriptor
{
    string Id { get; }
    string DisplayName { get; }
    string IconGlyph { get; }
    string PrimaryColor { get; }

    // Usage windows
    string PrimaryLabel { get; }
    string SecondaryLabel { get; }

    // Fetch strategies
    IReadOnlyList<IProviderFetchStrategy> FetchStrategies { get; }
}
```

### Fetch Strategies

Providers support multiple fetch methods (fallback chain):

```csharp
public interface IProviderFetchStrategy
{
    string StrategyName { get; }
    int Priority { get; }

    Task<bool> CanExecuteAsync();     // Can this strategy run?
    Task<UsageSnapshot> FetchAsync(); // Fetch data
}
```

**Example: Claude provider strategies**
1. OAuth API (highest priority)
2. Browser cookies
3. CLI (`claude usage`)

Strategy execution:
```
UsageFetcher.FetchAsync()
  ↓
For each strategy in priority order:
  ↓
  CanExecuteAsync()?
    ├─ No → Skip
    └─ Yes → FetchAsync()
           ├─ Success → Return
           └─ Error → Try next
```

## UI Architecture

### MVVM Pattern

Views bind to ViewModels via `CommunityToolkit.Mvvm`:

```csharp
// ViewModel
public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private UsageSnapshot? _currentSnapshot;

    [RelayCommand]
    private async Task RefreshAsync() { ... }
}

// View binding
<TextBlock Text="{x:Bind ViewModel.CurrentSnapshot.Primary.UsedPercent}" />
<Button Command="{x:Bind ViewModel.RefreshCommand}" />
```

### Settings Pages

Each settings page implements `ISettingsPage`:

```csharp
public interface ISettingsPage
{
    FrameworkElement Content { get; }
    void OnThemeChanged();
}
```

Pages loaded lazily via `SettingsWindow`:

```csharp
private ProvidersSettingsPage? _providersPage;

ISettingsPage page = pageName switch
{
    "Providers" => _providersPage ??= new ProvidersSettingsPage(),
    "General" => _generalPage ??= new GeneralSettingsPage(),
    // ...
};
```

### Tray Popup

`TrayPopupWindow` positioned near tray icon:

```
NotifyIcon click
  ↓
PopupState.ShowRequested
  ↓
DispatcherQueue.TryEnqueue(() => OnShowPopup())
  ↓
TrayPopupWindow.ShowPopup()
```

## Data Models

### UsageSnapshot

Complete usage data for a provider:

```csharp
public record UsageSnapshot
{
    string ProviderId { get; init; }
    RateWindow? Primary { get; init; }      // e.g., Session usage
    RateWindow? Secondary { get; init; }    // e.g., Weekly quota
    RateWindow? Tertiary { get; init; }
    ProviderCost? Cost { get; init; }        // Monthly spend
    ProviderIdentity? Identity { get; init; }
    DateTime FetchedAt { get; init; }
    string? ErrorMessage { get; init; }
}
```

### RateWindow

Time-based usage window with reset info:

```csharp
public record RateWindow
{
    double UsedPercent { get; init; }
    double? Used { get; init; }
    double? Limit { get; init; }
    int? WindowMinutes { get; init; }
    DateTime? ResetsAt { get; init; }
    string? Unit { get; init; }
}
```

## Extensibility

### Adding a Provider

1. Create folder: `Core/Providers/NewProvider/`
2. Implement descriptor:
```csharp
public class NewProviderDescriptor : ProviderDescriptor
{
    public override string Id => "newprovider";
    public override string DisplayName => "New Provider";
    // ...
    protected override void InitializeStrategies()
    {
        AddStrategy(new NewProviderOAuthStrategy());
        AddStrategy(new NewProviderCLIStrategy());
    }
}
```
3. Register in `ProviderRegistry.RegisterDefaultProviders()`
4. Add to `ProvidersSettingsPage.cs` visibility toggles
5. Add icon in `Assets/icons/newprovider.svg`

### Dynamic Settings

Providers can expose custom settings via `IProviderWithSettings`:

```csharp
public class NewProviderSettings : ProviderSettingsBase
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
            },
            new ProviderSettingDefinition
            {
                Key = "auto_retry",
                DisplayName = "Auto Retry on Failure",
                Type = ProviderSettingType.Toggle,
                DefaultValue = "true"
            }
        };
    }

    public override Task ApplySettingAsync(string key, string? value)
    {
        // Apply setting to provider
    }

    public override Task<string?> GetSettingValueAsync(string key)
    {
        // Return current value
    }
}
```

## Threading

### DispatcherQueue

All UI updates must go through DispatcherQueue:

```csharp
private DispatcherQueue? _dispatcherQueue;

// In service
_dispatcherQueue?.TryEnqueue(() =>
{
    // Update UI from background thread
    viewModel.CurrentSnapshot = snapshot;
});
```

### Async/Await

Provider fetching is fully async:

```csharp
public async Task<UsageSnapshot> FetchAsync(CancellationToken token)
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    var response = await client.GetAsync(url, token);
    var json = await response.Content.ReadAsStringAsync(token);
    // ...
}
```

## Error Handling

### Logging

All components use `DebugLogger`:

```csharp
DebugLogger.Log("Provider", "Fetching data");
DebugLogger.LogError("Provider", "Fetch failed", ex);
```

Logs written to:
```
%LocalAppData%\QuoteBar\logs\
```

### Graceful Degradation

- If provider API fails, show error but don't crash
- If status polling fails, retry next interval
- If auto-update fails, manual check still available
- Unhandled exceptions logged and app keeps running

## Security

### Credential Storage

API tokens use Windows Credential Manager (DPAPI):

```csharp
// Store
CredentialManager.WriteCredential("provider", token);

// Read
var token = CredentialManager.ReadCredential("provider");

// Delete
CredentialManager.DeleteCredential("provider");
```

### Settings Storage

Non-sensitive settings in JSON (plain text):
```
%LocalAppData%\QuoteBar\settings.json
```

Usage history separately:
```
%LocalAppData%\QuoteBar\usage_history.json
```

## Performance

### Lazy Loading

Settings pages loaded on-demand to reduce startup time:

```csharp
private GeneralSettingsPage? _generalPage;

ISettingsPage page = pageName switch
{
    "General" => _generalPage ??= new GeneralSettingsPage(), // Only once
    // ...
};
```

### Icon Caching

SVG icons converted to PNG once at startup:

```csharp
IconGenerator.EnsureIconsExist(); // Check cache, convert if missing
```

### Efficient Refresh

- Single `Timer` per service (not per provider)
- Event-driven updates (avoid polling UI)
- Batched operations (e.g., `RefreshAllAsync`)

## Build System

### dev.ps1

PowerScript script for common tasks:

```powershell
.\dev.ps1 run      # Build + run
.\dev.ps1 build    # Release build
.\dev.ps1 watch    # Hot reload
.\dev.ps1 publish   # Self-contained exe
.\dev.ps1 clean    # Clean outputs
```

### Hot Reload

Watch mode triggers recompile on file change:

```powershell
dotnet watch --project NativeBar.WinUI
```

Changes reflected in running app without restart.
