using System.IO;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using QuoteBar.Core.Services;
using Microsoft.UI.Text;

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

    // UI elements for download progress
    private StackPanel? _mainStack;
    private StackPanel? _progressPanel;
    private ProgressBar? _progressBar;
    private TextBlock? _statusText;
    private TextBlock? _percentText;

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
        var grid = new Grid { MaxHeight = 500, MinWidth = 400 };

        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Auto
        };

        _mainStack = new StackPanel { Spacing = 16 };

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
            FontWeight = FontWeights.SemiBold,
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
        _mainStack.Children.Add(iconPanel);

        // Progress panel (initially hidden)
        _progressPanel = new StackPanel
        {
            Spacing = 8,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 8, 0, 8)
        };

        var progressHeader = new Grid();
        _statusText = new TextBlock
        {
            Text = "Preparing download...",
            FontSize = 13,
            Foreground = new SolidColorBrush(_theme.TextColor)
        };
        _percentText = new TextBlock
        {
            Text = "0%",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = new SolidColorBrush(_theme.AccentColor)
        };
        progressHeader.Children.Add(_statusText);
        progressHeader.Children.Add(_percentText);
        _progressPanel.Children.Add(progressHeader);

        _progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 8,
            CornerRadius = new CornerRadius(4)
        };
        _progressPanel.Children.Add(_progressBar);

        _mainStack.Children.Add(_progressPanel);

        var releaseNotesLabel = new TextBlock
        {
            Text = "What's New:",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = new SolidColorBrush(_theme.TextColor)
        };
        _mainStack.Children.Add(releaseNotesLabel);

        // Rich text block with proper markdown parsing
        var releaseNotes = new RichTextBlock
        {
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(_theme.TextColor),
            LineHeight = 24
        };

        if (_release != null && !string.IsNullOrEmpty(_release.Body))
        {
            ParseMarkdownToRichText(releaseNotes, _release.Body);
        }

        _mainStack.Children.Add(releaseNotes);

        scroll.Content = _mainStack;
        Grid.SetRow(scroll, 0);
        grid.Children.Add(scroll);

        Content = grid;
    }

    /// <summary>
    /// Parse markdown and render to RichTextBlock with proper formatting
    /// </summary>
    private void ParseMarkdownToRichText(RichTextBlock richText, string markdown)
    {
        var lines = markdown.Split('\n');
        Paragraph? currentParagraph = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // Skip empty lines
            if (string.IsNullOrWhiteSpace(line))
            {
                if (currentParagraph != null)
                {
                    richText.Blocks.Add(currentParagraph);
                    currentParagraph = null;
                }
                continue;
            }

            // Skip horizontal rules
            if (line == "---" || line == "***" || line == "___")
            {
                continue;
            }

            // Skip installation instructions (we'll show our own)
            if (line.Contains("Download `QuoteBar-") || line.Contains("Installation"))
            {
                continue;
            }

            // Parse headers (## Header)
            var headerMatch = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
            if (headerMatch.Success)
            {
                if (currentParagraph != null)
                {
                    richText.Blocks.Add(currentParagraph);
                }
                currentParagraph = new Paragraph { Margin = new Thickness(0, 8, 0, 4) };
                var headerRun = new Run
                {
                    Text = headerMatch.Groups[2].Value,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 15
                };
                currentParagraph.Inlines.Add(headerRun);
                richText.Blocks.Add(currentParagraph);
                currentParagraph = null;
                continue;
            }

            // Parse list items (- item or * item)
            var listMatch = Regex.Match(line, @"^[-*]\s+(.+)$");
            if (listMatch.Success)
            {
                currentParagraph = new Paragraph { Margin = new Thickness(8, 2, 0, 2) };
                currentParagraph.Inlines.Add(new Run { Text = "• " });
                ParseInlineMarkdown(currentParagraph, listMatch.Groups[1].Value);
                richText.Blocks.Add(currentParagraph);
                currentParagraph = null;
                continue;
            }

            // Regular text
            if (currentParagraph == null)
            {
                currentParagraph = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
            }
            ParseInlineMarkdown(currentParagraph, line);
            currentParagraph.Inlines.Add(new Run { Text = " " });
        }

        if (currentParagraph != null)
        {
            richText.Blocks.Add(currentParagraph);
        }
    }

    /// <summary>
    /// Parse inline markdown (bold, code, etc) and add to paragraph
    /// </summary>
    private void ParseInlineMarkdown(Paragraph paragraph, string text)
    {
        // Pattern to match **bold**, `code`, or regular text
        var pattern = @"(\*\*(.+?)\*\*)|(`(.+?)`)|([^*`]+)";
        var matches = Regex.Matches(text, pattern);

        foreach (Match match in matches)
        {
            if (match.Groups[2].Success) // **bold**
            {
                paragraph.Inlines.Add(new Run
                {
                    Text = match.Groups[2].Value,
                    FontWeight = FontWeights.SemiBold
                });
            }
            else if (match.Groups[4].Success) // `code`
            {
                paragraph.Inlines.Add(new Run
                {
                    Text = match.Groups[4].Value,
                    FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                    Foreground = new SolidColorBrush(_theme.AccentColor)
                });
            }
            else if (match.Groups[5].Success) // regular text
            {
                var textValue = match.Groups[5].Value;
                if (!string.IsNullOrEmpty(textValue))
                {
                    paragraph.Inlines.Add(new Run { Text = textValue });
                }
            }
        }
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
        IsPrimaryButtonEnabled = false;
        IsSecondaryButtonEnabled = false;
        CloseButtonText = ""; // Hide close button during download

        // Show progress UI
        if (_progressPanel != null)
        {
            _progressPanel.Visibility = Visibility.Visible;
        }
        UpdateProgressUI(UpdatePhase.Downloading, 0, "Connecting...");
        PrimaryButtonText = "Downloading...";

        try
        {
            var progress = new Progress<UpdateProgress>(p =>
            {
                // Update on UI thread
                DispatcherQueue?.TryEnqueue(() =>
                {
                    UpdateProgressUI(p.Phase, p.Percent, p.Status);
                    
                    // Update button text based on phase
                    PrimaryButtonText = p.Phase switch
                    {
                        UpdatePhase.Downloading => "Downloading...",
                        UpdatePhase.Extracting => "Extracting...",
                        UpdatePhase.Ready => "Install & Restart",
                        _ => PrimaryButtonText
                    };
                });
            });

            var updatePath = await _updateService.DownloadUpdateAsync(_release, progress);

            if (!string.IsNullOrEmpty(updatePath))
            {
                _updatePath = updatePath;
                UpdateProgressUI(UpdatePhase.Ready, 100, "✓ Ready to install");
                PrimaryButtonText = "Install & Restart";
                IsPrimaryButtonEnabled = true;
                CloseButtonText = "Later";
            }
            else
            {
                await ShowErrorAsync("Download failed. Please try again later.");
                ResetDownloadUI();
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("UpdateDialog", "Download failed", ex);
            await ShowErrorAsync($"Download failed: {ex.Message}");
            ResetDownloadUI();
        }
        finally
        {
            _isDownloading = false;
        }
    }

    private void UpdateProgressUI(UpdatePhase phase, double percent, string status)
    {
        if (_progressBar != null)
        {
            _progressBar.Value = percent;
            
            // Use indeterminate for extracting since it's quick
            _progressBar.IsIndeterminate = phase == UpdatePhase.Installing;
        }
        if (_percentText != null)
        {
            _percentText.Text = phase == UpdatePhase.Installing ? "" : $"{percent:F0}%";
        }
        if (_statusText != null)
        {
            _statusText.Text = status;
            
            // Green for ready/complete
            if (phase == UpdatePhase.Ready || phase == UpdatePhase.Complete)
            {
                _statusText.Foreground = new SolidColorBrush(_theme.AccentColor);
            }
        }
    }

    private void ResetDownloadUI()
    {
        if (_progressPanel != null)
        {
            _progressPanel.Visibility = Visibility.Collapsed;
        }
        PrimaryButtonText = "Download & Install";
        IsPrimaryButtonEnabled = true;
        IsSecondaryButtonEnabled = true;
    }

    private async Task InstallUpdateAsync()
    {
        if (string.IsNullOrEmpty(_updatePath)) return;

        try
        {
            // Show installing state
            _isDownloading = true;
            IsPrimaryButtonEnabled = false;
            CloseButtonText = "";
            PrimaryButtonText = "Installing...";
            
            if (_progressPanel != null)
            {
                _progressPanel.Visibility = Visibility.Visible;
            }
            UpdateProgressUI(UpdatePhase.Installing, 0, "Preparing installation...");

            var currentPath = AppContext.BaseDirectory;

            if (_updateService.PrepareUpdater(_updatePath, currentPath))
            {
                UpdateProgressUI(UpdatePhase.Installing, 50, "Starting updater...");
                
                // Small delay for visual feedback
                await Task.Delay(300);
                
                // Launch updater and close app
                var scriptPath = Path.Combine(Path.GetTempPath(), "QuoteBar-Updater.ps1");

                try
                {
                    UpdateProgressUI(UpdatePhase.Installing, 100, "Closing app to install...");
                    await Task.Delay(500);
                    
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
                    ResetDownloadUI();
                }
            }
            else
            {
                await ShowErrorAsync("Failed to prepare updater. Please download manually.");
                ResetDownloadUI();
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("UpdateDialog", "Install failed", ex);
            await ShowErrorAsync($"Install failed: {ex.Message}");
            ResetDownloadUI();
        }
        finally
        {
            _isDownloading = false;
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
