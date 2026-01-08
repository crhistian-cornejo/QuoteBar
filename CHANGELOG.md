# Changelog

All notable changes to QuoteBar will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] - 2026-01-08

### Added
- **SharedHttpClient**: Centralized HTTP client service to prevent socket exhaustion
- **Change API Key/Cookie options**: New menu items for z.ai and MiniMax providers
- **Buffered logging**: DebugLogger now uses buffered writes for better performance

### Changed
- **Status polling interval**: Reduced from 5 to 15 minutes to lower resource usage
- **Settings debouncing**: Added 500ms delay to prevent excessive disk writes
- **Disconnect behavior**: Now only disables tracking instead of deleting external CLI credentials (Claude, Codex, Gemini)

### Fixed
- **Connect dialogs**: MiniMax and z.ai dialogs now display correctly with proper XamlRoot handling
- **Icon handle leak**: Fixed memory leak in LoadLogoIcon
- **Event subscription cleanup**: Proper unsubscription on app exit to prevent memory leaks
- **UsageStore disposal**: Implemented IDisposable pattern correctly with timer cleanup
- **Service shutdown order**: Services now disposed in correct reverse order on exit

## [0.1.0] - 2026-01-07

### Added
- Initial release
- System tray integration with usage indicator
- Real-time usage monitoring for multiple AI providers:
  - Claude (Anthropic) - CLI integration
  - Codex (OpenAI) - CLI integration
  - Cursor - API integration
  - Gemini (Google) - OAuth integration
  - Copilot (GitHub) - OAuth integration
  - Droid - API integration
  - z.ai - API token authentication
  - MiniMax - Cookie authentication
  - Augment - Cookie authentication
  - Antigravity - Local probe
- Secure API token storage using Windows Credential Manager
- Dark/Light theme support (follows system theme)
- Native Windows 11 design with Mica backdrop
- Cost tracking dashboard
- Provider order customization
- Hotkey support for quick popup access
- Auto-start with Windows option

[0.2.0]: https://github.com/crhistian-cornejo/QuoteBar/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/crhistian-cornejo/QuoteBar/releases/tag/v0.1.0
