# Plan: Refactorizar SettingsWindow.cs

**Estado**: Pendiente  
**Prioridad**: Media  
**Archivo actual**: `NativeBar.WinUI/SettingsWindow.cs` (2207 líneas)

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

## Riesgos

- **Bajo**: Cambios son internos, no afectan API pública
- **Medio**: Requiere testing manual de todas las páginas después de refactorizar

## Estimación

- **Tiempo**: 2-4 horas
- **Complejidad**: Media
