# NativeBar - AI Usage Monitor for Windows

NativeBar es una aplicación nativa de Windows (WinUI 3) que monitorea el uso de APIs de múltiples proveedores de IA desde la barra de tareas, similar a CodexBar pero para Windows.

## Características

- **System Tray Icon**: Icono en la barra de tareas con menú contextual
- **Popover/Flyout**: Ventana flotante con información detallada de uso
- **Múltiples Providers**: Soporte para Claude, Codex, GitHub Copilot, y Antigravity
- **Progress Bars**: Visualización de uso con barras de progreso y porcentajes
- **Fetch Strategies**: Sistema de fallback automático (OAuth → Web → CLI)
- **Auto-refresh**: Actualización automática cada 5 minutos
- **Indicadores de Costo**: Tracking de costos en USD

## Arquitectura

### Core Components

#### Provider System
- **IProviderDescriptor**: Define metadata y capacidades de cada provider
- **IProviderFetchStrategy**: Estrategias de obtención de datos con prioridades
- **ProviderRegistry**: Registro central de todos los providers
- **UsageFetcher**: Ejecuta estrategias con fallback automático

#### Data Models
- **UsageSnapshot**: Snapshot completo del uso de un provider
- **RateWindow**: Ventana de tiempo con porcentaje usado y reset info
- **ProviderIdentity**: Información de cuenta (email, plan)
- **ProviderCost**: Tracking de costos

#### State Management
- **UsageStore**: Store central observable con MVVM
- Auto-refresh cada 5 minutos
- Soporte para múltiples providers simultáneos

### UI Architecture

#### System Tray
- Implementado con `H.NotifyIcon.WinUI`
- Click izquierdo: Abre ventana principal
- Click derecho: Menú contextual (Refresh, Exit)

#### Main Window
- WinUI 3 con XAML
- MVVM pattern con CommunityToolkit.Mvvm
- Data binding con converters
- Progress bars customizadas

### Providers Implementados

#### 1. Claude
- **OAuth Strategy**: API oficial de Anthropic
- **Web Strategy**: Scraping del dashboard
- **Métricas**: 5-hour window, 24-hour window

#### 2. Codex/OpenAI
- **RPC Strategy**: JSON-RPC con daemon local
- **CLI Strategy**: Parsing de `codex status`
- **Métricas**: 5-hour window, weekly limit, credits

#### 3. GitHub Copilot
- **OAuth Strategy**: GitHub API
- **Métricas**: Monthly usage, completions

#### 4. Antigravity
- **OAuth Strategy**: API REST
- **Métricas**: Daily usage, weekly usage

## Estructura del Proyecto

```
NativeBar.WinUI/
├── Core/
│   ├── Models/
│   │   └── UsageSnapshot.cs          # Data models
│   ├── Providers/
│   │   ├── IProviderDescriptor.cs    # Provider interfaces
│   │   ├── ProviderRegistry.cs       # Registry & fetcher
│   │   ├── Claude/
│   │   │   └── ClaudeProvider.cs
│   │   ├── Codex/
│   │   │   └── CodexProvider.cs
│   │   ├── GitHub/
│   │   │   └── GitHubProvider.cs
│   │   └── Antigravity/
│   │       └── AntigravityProvider.cs
│   └── Services/
│       └── UsageStore.cs             # State management
├── ViewModels/
│   └── MainViewModel.cs              # Main VM
├── Controls/
│   └── UsageProgressBar.cs           # Custom progress bar
├── Converters/
│   └── ValueConverters.cs            # XAML converters
├── App.xaml                          # Application entry
├── App.xaml.cs
├── MainWindow.xaml                   # Main UI
└── MainWindow.xaml.cs
```

## Requisitos

- Windows 10 version 1809 (build 17763) o superior
- .NET 9.0
- Windows App SDK 1.6

## Instalación

1. Clonar el repositorio
2. Restaurar paquetes NuGet:
   ```bash
   dotnet restore
   ```

3. Compilar el proyecto:
   ```bash
   dotnet build
   ```

4. Ejecutar:
   ```bash
   dotnet run
   ```

## Desarrollo

### Agregar un Nuevo Provider

1. Crear una clase que herede de `ProviderDescriptor`:

```csharp
public class MyProviderDescriptor : ProviderDescriptor
{
    public override string Id => "myprovider";
    public override string DisplayName => "My Provider";
    public override string IconGlyph => "\uE943";
    public override string PrimaryColor => "#FF0000";
    public override string SecondaryColor => "#FF6666";
    public override string PrimaryLabel => "Daily usage";
    public override string SecondaryLabel => "Monthly usage";
    
    protected override void InitializeStrategies()
    {
        AddStrategy(new MyOAuthStrategy());
    }
}
```

2. Implementar estrategias de fetch:

```csharp
public class MyOAuthStrategy : IProviderFetchStrategy
{
    public string StrategyName => "OAuth";
    public int Priority => 1;
    
    public async Task<bool> CanExecuteAsync()
    {
        // Verificar si hay token disponible
        return true;
    }
    
    public async Task<UsageSnapshot> FetchAsync(CancellationToken ct)
    {
        // Obtener datos del API
        return new UsageSnapshot { /* ... */ };
    }
}
```

3. Registrar en `ProviderRegistry.RegisterDefaultProviders()`:

```csharp
Register(new MyProviderDescriptor());
```

## Pendientes / TODOs

- [ ] Implementar Windows Credential Manager para tokens
- [ ] Parsear respuestas JSON de APIs reales
- [ ] Web scraping con browser automation
- [ ] CLI parsing para cada provider
- [ ] Animaciones de iconos (blink, wiggle, tilt)
- [ ] Renderizado custom de iconos en system tray
- [ ] Charts para historial de uso
- [ ] Settings window para configuración
- [ ] Multi-account support
- [ ] Notificaciones cuando se alcancen límites
- [ ] Dark/Light theme support
- [ ] Exportar datos de uso

## Similitudes con CodexBar

Esta implementación replica la arquitectura de CodexBar:

1. **Provider System**: Mismo patrón descriptor + strategy
2. **Fetch Fallback**: OAuth → Web → CLI chain
3. **Data Models**: UsageSnapshot, RateWindow, etc.
4. **UI Layout**: Header, progress bars, cost tracking
5. **Auto-refresh**: Timer de 5 minutos
6. **Multi-provider**: Switch entre providers

## Diferencias con CodexBar

1. **Platform**: WinUI 3 en lugar de SwiftUI + AppKit
2. **System Tray**: H.NotifyIcon en lugar de NSStatusItem
3. **State Management**: CommunityToolkit.Mvvm en lugar de @Observable
4. **Icon Rendering**: Pendiente (CodexBar usa Core Graphics)
5. **Web Scraping**: Pendiente (CodexBar usa WKWebView)

## Licencia

MIT

## Créditos

Inspirado en [CodexBar](https://github.com/example/CodexBar) de macOS.
