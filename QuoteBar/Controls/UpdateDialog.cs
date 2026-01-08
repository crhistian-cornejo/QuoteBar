using System.IO;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using QuoteBar.Core.Services;

namespace QuoteBar.Controls;

/// <summary>
/// Dialog for showing available updates and downloading them
/// </summary>
public sealed partial class UpdateDialog : ContentDialog
{
    private readonly ThemeService _theme = ThemeService.Instance;
    private readonly UpdateService _updateService = UpdateService.Instance;

    private GitHubRelease? _release;
    private string? _updatePath;
    private bool _isDownloading;

    public UpdateDialog(XamlRoot xamlRoot)
    {
        XamlRoot = xamlRoot;
        Title = "Update Available";
        PrimaryButtonText = "Download & Install";
        CloseButtonText = "Later";
        DefaultButton = ContentDialogButton.Primary;

        BuildUI();
    }

    private void BuildUI()
    {
        var grid = new Grid { MaxHeight = 500 };

        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Auto
        };

        var stack = new StackPanel { Spacing = 16 };

        var iconPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var icon = new FontIcon
        {
            Glyph = "\uE896", // Download icon
            FontSize = 32,
            Foreground = new SolidColorBrush(_theme.AccentColor)
        };

        var titlePanel = new StackPanel { Spacing = 4 };
        titlePanel.Children.Add(new TextBlock
        {
            Text = "New Version Available",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(_theme.TextColor)
        });

        var versionText = new TextBlock
        {
            Text = $"Version {_release?.Version ?? "Unknown"}",
            FontSize = 14,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor)
        };

        titlePanel.Children.Add(versionText);
        iconPanel.Children.Add(icon);
        iconPanel.Children.Add(titlePanel);
        stack.Children.Add(iconPanel);

        var releaseNotesLabel = new TextBlock
        {
            Text = "What's New:",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = new SolidColorBrush(_theme.TextColor)
        };
        stack.Children.Add(releaseNotesLabel);

        var releaseNotes = new RichTextBlock
        {
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(_theme.TextColor)
        };

        if (_release != null && !string.IsNullOrEmpty(_release.Body))
        {
            var paragraph = new Paragraph();
            var run = new Run
            {
                Text = FormatReleaseNotes(_release.Body)
            };
            paragraph.Inlines.Add(run);
            releaseNotes.Blocks.Add(paragraph);
        }

        stack.Children.Add(releaseNotes);

        scroll.Content = stack;
        Grid.SetRow(scroll, 0);
        grid.Children.Add(scroll);

        Content = grid;
    }

    private static string FormatReleaseNotes(string notes)
    {
        // Simple formatting for markdown-like notes
        var formatted = notes;

        // Remove markdown headers
        formatted = Regex.Replace(formatted, @"^#{1,6}\s+", "", RegexOptions.Multiline);

        // Convert list items to bullets
        formatted = Regex.Replace(formatted, @"^\s*[-*]\s+", "• ", RegexOptions.Multiline);

        return formatted.Trim();
    }

    public void SetRelease(GitHubRelease release)
    {
        _release = release;
        BuildUI();
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_isDownloading) return;

        if (!string.IsNullOrEmpty(_updatePath))
        {
            // Install update
            args.Cancel = true;
            await InstallUpdateAsync();
            return;
        }

        // Start download
        args.Cancel = true;
        await DownloadUpdateAsync();
    }

    private async Task DownloadUpdateAsync()
    {
        if (_release == null) return;

        _isDownloading = true;
        PrimaryButtonText = "Downloading...";
        IsPrimaryButtonEnabled = false;

        try
        {
            var progress = new Progress<double>(p =>
            {
                PrimaryButtonText = $"Downloading... {p:F0}%";
            });

            var updatePath = await _updateService.DownloadUpdateAsync(_release, progress);

            if (!string.IsNullOrEmpty(updatePath))
            {
                _updatePath = updatePath;
                PrimaryButtonText = "Install & Restart";
                IsPrimaryButtonEnabled = true;
            }
            else
            {
                await ShowErrorAsync("Download failed. Please try again later.");
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("UpdateDialog", "Download failed", ex);
            await ShowErrorAsync($"Download failed: {ex.Message}");
        }
        finally
        {
            _isDownloading = false;
        }
    }

    private async Task InstallUpdateAsync()
    {
        if (string.IsNullOrEmpty(_updatePath)) return;

        try
        {
            var currentPath = AppContext.BaseDirectory;

            if (_updateService.PrepareUpdater(_updatePath, currentPath))
            {
                // Show confirmation
                var confirmDialog = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = "Install Update?",
                    Content = "QuoteBar will close to install the update. Any unsaved changes will be lost.\n\nDo you want to continue?",
                    PrimaryButtonText = "Install",
                    CloseButtonText = "Cancel"
                };

                var result = await confirmDialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    // Launch updater and close app
                    var scriptPath = Path.Combine(Path.GetTempPath(), "QuoteBar-Updater.ps1");

                    try
                    {
                        ProcessStartInfo startInfo = new()
                        {
                            FileName = "powershell.exe",
                            Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\"",
                            UseShellExecute = true,
                            WindowStyle = ProcessWindowStyle.Normal
                        };

                        Process.Start(startInfo);
                        Application.Current.Exit();
                    }
                    catch (Exception ex)
                    {
                        await ShowErrorAsync($"Failed to start updater: {ex.Message}");
                    }
                }
            }
            else
            {
                await ShowErrorAsync("Failed to prepare updater. Please download manually.");
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("UpdateDialog", "Install failed", ex);
            await ShowErrorAsync($"Install failed: {ex.Message}");
        }
    }

    private async Task ShowErrorAsync(string message)
    {
        var errorDialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Error",
            Content = message,
            CloseButtonText = "OK"
        };

        await errorDialog.ShowAsync();
    }
}
