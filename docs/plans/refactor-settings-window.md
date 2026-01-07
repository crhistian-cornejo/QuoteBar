# Plan: Refactorizar SettingsWindow.cs

**Estado**: ✅ Completado  
**Prioridad**: Media  
**Archivo original**: `NativeBar.WinUI/SettingsWindow.cs` (2611 líneas → 454 líneas)

## Problema

El archivo `SettingsWindow.cs` tiene más de 2200 líneas de código, lo cual dificulta:
- Mantenimiento y depuración
- Navegación del código
- Reutilización de componentes
- Testing de componentes individuales

## Solución Propuesta

Dividir el archivo en una estructura modular dentro de una carpeta `Settings/`:

```
NativeBar.WinUI/
├── Settings/
│   ├── SettingsWindow.cs              (~200 líneas)
│   │   - Ventana principal
│   │   - Navegación entre páginas
│   │   - Configuración de titlebar y backdrop
│   │
│   ├── Pages/
│   │   ├── GeneralSettingsPage.cs     (~150 líneas)
│   │   │   - Start at login
│   │   │   - Refresh interval
│   │   │   - Hover delay
│   │   │   - Keyboard shortcuts
│   │   │
│   │   ├── ProvidersSettingsPage.cs   (~400 líneas)
│   │   │   - Provider toggles
│   │   │   - Provider cards con auto-detección
│   │   │   - Connect/Disconnect dialogs
│   │   │   - z.ai configuración especial
│   │   │
│   │   ├── AppearanceSettingsPage.cs  (~200 líneas)
│   │   │   - Theme selection
│   │   │   - Accent color
│   │   │   - Compact mode
│   │   │   - Tray badge settings
│   │   │
│   │   ├── NotificationsSettingsPage.cs (~100 líneas)
│   │   │   - Usage alerts
│   │   │   - Thresholds (warning/critical)
│   │   │   - Sound settings
│   │   │
│   │   └── AboutSettingsPage.cs       (~150 líneas)
│   │       - App info
│   │       - Version
│   │       - Links
│   │       - Check for updates
│   │
│   ├── Controls/
│   │   ├── SettingCard.cs             (~50 líneas)
│   │   │   - Control reutilizable para cards de configuración
│   │   │   - Props: Title, Description, Control
│   │   │
│   │   ├── ProviderCard.cs            (~150 líneas)
│   │   │   - Card de proveedor con estado
│   │   │   - Icono, nombre, status
│   │   │   - Botón Configure/Connect
│   │   │   - Auto-detección async
│   │   │
│   │   └── TrayBadgeSelector.cs       (~100 líneas)
│   │       - Panel de selección de providers para tray
│   │       - Límite de 3 providers
│   │
│   └── Helpers/
│       ├── ProviderIconHelper.cs      (~80 líneas)
│       │   - GetProviderSvgFileName()
│       │   - CreateProviderCardIcon()
│       │   - ParseColor()
│       │
│       └── ProviderStatusDetector.cs  (~120 líneas)
│           - GetProviderStatus()
│           - GetProviderStatusFast()
│           - GetProviderStatusWithCLI()
│           - CanDetectCLI()
│           - DisconnectProvider()
```

## Beneficios

1. **Mantenibilidad**: Cada archivo tiene una sola responsabilidad
2. **Legibilidad**: Archivos de 50-400 líneas son más fáciles de entender
3. **Testabilidad**: Se pueden testear componentes individuales
4. **Reutilización**: `SettingCard`, `ProviderCard` se pueden usar en otros lugares
5. **Colaboración**: Múltiples desarrolladores pueden trabajar en diferentes páginas

## Pasos de Implementación

### Fase 1: Crear estructura base
- [ ] Crear carpeta `Settings/` y subcarpetas
- [ ] Crear interfaz `ISettingsPage` si es necesario
- [ ] Mover `SettingsWindow.cs` a `Settings/`

### Fase 2: Extraer Helpers
- [ ] Crear `ProviderIconHelper.cs` con métodos de iconos
- [ ] Crear `ProviderStatusDetector.cs` con métodos de detección
- [ ] Actualizar referencias en `SettingsWindow.cs`

### Fase 3: Extraer Controls
- [ ] Crear `SettingCard.cs` como UserControl o clase
- [ ] Crear `ProviderCard.cs`
- [ ] Crear `TrayBadgeSelector.cs`

### Fase 4: Extraer Pages
- [ ] Crear `GeneralSettingsPage.cs`
- [ ] Crear `ProvidersSettingsPage.cs`
- [ ] Crear `AppearanceSettingsPage.cs`
- [ ] Crear `NotificationsSettingsPage.cs`
- [ ] Crear `AboutSettingsPage.cs`

### Fase 5: Refactorizar SettingsWindow
- [ ] Simplificar `SettingsWindow.cs` para solo navegación
- [ ] Usar las nuevas páginas y controles
- [ ] Verificar que todo funciona correctamente

### Fase 6: Cleanup
- [ ] Eliminar código duplicado
- [ ] Añadir documentación XML donde falte
- [ ] Verificar que el build pasa sin warnings

## Notas Técnicas

- Mantener compatibilidad con el sistema de temas (`ThemeService`)
- Las páginas deben suscribirse a `ThemeChanged` para actualizar colores
- Considerar usar `UserControl` para páginas o simplemente `FrameworkElement`
- El `SettingsService` debe seguir siendo singleton compartido

---

## Mejora UI: Sidebar Colapsable (Estilo Copilot)

### Referencia Visual

La UI de Microsoft Copilot usa un patrón de navegación moderno con sidebar colapsable:

| Estado | Descripción |
|--------|-------------|
| **Expandido** | Sidebar visible con iconos + texto, panel de navegación a la izquierda |
| **Colapsado** | Sidebar oculto, solo iconos en header (logo + hamburger + home) |

### Implementación con NavigationView

Reemplazar el `SplitView` manual actual por el control nativo `NavigationView` de WinUI 3:

```xml
<NavigationView
    x:Name="NavView"
    PaneDisplayMode="Auto"
    OpenPaneLength="280"
    CompactPaneLength="48"
    IsPaneToggleButtonVisible="True"
    IsBackButtonVisible="Collapsed"
    IsSettingsVisible="False">
    
    <NavigationView.MenuItems>
        <NavigationViewItem Icon="Setting" Content="General" Tag="General"/>
        <NavigationViewItem Icon="Contact" Content="Providers" Tag="Providers"/>
        <NavigationViewItem Icon="View" Content="Appearance" Tag="Appearance"/>
        <NavigationViewItem Icon="Message" Content="Notifications" Tag="Notifications"/>
    </NavigationView.MenuItems>
    
    <NavigationView.FooterMenuItems>
        <NavigationViewItem Icon="Help" Content="About" Tag="About"/>
    </NavigationView.FooterMenuItems>
    
    <Frame x:Name="ContentFrame"/>
</NavigationView>
```

### Modos de PaneDisplayMode

| Modo | Comportamiento |
|------|----------------|
| `Left` | Siempre expandido |
| `LeftCompact` | Solo iconos, expandir al hacer hover |
| `LeftMinimal` | Completamente oculto, overlay al hacer clic en hamburger |
| `Auto` | **Recomendado** - Cambia automáticamente según ancho de ventana |

### Breakpoints Automáticos (PaneDisplayMode="Auto")

```
Ancho ventana >= 1008px  →  Left (expandido)
Ancho ventana >= 641px   →  LeftCompact (iconos)
Ancho ventana < 641px    →  LeftMinimal (oculto)
```

### Configuración del Header (Estilo Copilot)

```csharp
// Custom header con logo de la app
NavView.PaneHeader = new StackPanel
{
    Orientation = Orientation.Horizontal,
    Children = 
    {
        new Image { Source = new BitmapImage(logoUri), Width = 24, Height = 24 },
        new TextBlock { Text = "QuoteBar", FontWeight = FontWeights.SemiBold, Margin = new Thickness(8,0,0,0) }
    }
};
```

### Beneficios

1. **Responsivo**: Se adapta automáticamente al tamaño de ventana
2. **Nativo**: Usa el control estándar de WinUI 3, soporte completo de accesibilidad
3. **Consistente**: Mismo patrón que Windows 11 Settings, Microsoft Store, Copilot
4. **Animaciones**: Transiciones fluidas incluidas por defecto
5. **Keyboard**: Navegación con teclado funciona out-of-the-box

### Pasos de Implementación

- [ ] Crear `SettingsWindow.xaml` con `NavigationView`
- [ ] Convertir páginas a `Page` o `UserControl` navegables
- [ ] Implementar `NavigationView.SelectionChanged` para navegación
- [ ] Configurar `PaneHeader` con logo de QuoteBar
- [ ] Usar `ContentFrame.Navigate()` para cambiar páginas
- [ ] Agregar iconos (`FontIcon` o `SymbolIcon`) a cada `NavigationViewItem`
- [ ] Probar comportamiento en diferentes tamaños de ventana

---

## Riesgos

- **Bajo**: Cambios son internos, no afectan API pública
- **Medio**: Requiere testing manual de todas las páginas después de refactorizar

## Estimación

- **Tiempo**: 4-6 horas (incluyendo NavigationView)
- **Complejidad**: Media-Alta

