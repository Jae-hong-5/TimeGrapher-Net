using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

using TimeGrapher.App.Services;

namespace TimeGrapher.App.Views;

internal sealed class MainWindowDialogService : ITimeGrapherDialogService
{
    private readonly Window _owner;

    public MainWindowDialogService(Window owner)
    {
        _owner = owner;
    }

    public async Task<RecordSessionChoice> AskRecordSessionAsync()
    {
        var dialog = new Window
        {
            Title = "Record Session",
            Width = 360,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        // Dismissing the dialog (title-bar X / Alt+F4) bypasses the button
        // handlers, so the fallback must abort the start — matching the Qt
        // original's QMessageBox escape behavior and this dialog's own Esc
        // (the Cancel button is IsCancel).
        var result = RecordSessionChoice.Cancel;

        var yes = new Button { Content = "Yes", Width = 80, IsDefault = false };
        var no = new Button { Content = "No", Width = 80, IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        yes.Click += (_, _) => { result = RecordSessionChoice.Yes; dialog.Close(); };
        no.Click += (_, _) => { result = RecordSessionChoice.No; dialog.Close(); };
        cancel.Click += (_, _) => { result = RecordSessionChoice.Cancel; dialog.Close(); };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        buttons.Children.Add(yes);
        buttons.Children.Add(no);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Do you want to record this session ?", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(buttons);
        dialog.Content = panel;

        await dialog.ShowDialog(_owner);
        return result;
    }

    public async Task<string?> PickOpenWavAsync(string currentDirectory)
    {
        IStorageProvider sp = _owner.StorageProvider;
        IStorageFolder? startFolder = null;
        try
        {
            startFolder = await sp.TryGetFolderFromPathAsync(currentDirectory);
        }
        catch
        {
        }

        IReadOnlyList<IStorageFile> files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Document",
            AllowMultiple = false,
            SuggestedStartLocation = startFolder,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("WAV Files") { Patterns = new[] { "*.wav" } },
            },
        });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public async Task<string?> PickOpenMeasurementLogAsync()
    {
        IStorageProvider sp = _owner.StorageProvider;
        IReadOnlyList<IStorageFile> files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Measurement Log",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Measurement Logs") { Patterns = new[] { "*.csv", "*.log", "*.txt" } },
            },
        });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public async Task<string?> PickSaveWavAsync()
    {
        IStorageProvider sp = _owner.StorageProvider;
        IStorageFile? file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Output File",
            DefaultExtension = "wav",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Wav Files") { Patterns = new[] { "*.wav" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*" } },
            },
        });

        return file?.TryGetLocalPath();
    }

    public async Task<AiAnalysisDialogResult?> AskAiAnalysisAsync(AiAnalysisDialogRequest request)
    {
        var dialog = new Window
        {
            Title = "AI Analysis",
            Width = 480,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        AiAnalysisDialogResult? result = null;
        string[] backendLabels = BuildBackendLabels(request.BackendOptions);
        var backendCombo = new ComboBox
        {
            ItemsSource = backendLabels,
            SelectedIndex = SelectedBackendIndex(request.BackendOptions, request.SelectedBackendBaseUrl),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var username = new TextBox
        {
            Text = request.SavedCredentials?.Username ?? string.Empty,
            Watermark = "User ID",
            MinWidth = 120,
        };
        var password = new TextBox
        {
            Text = request.SavedCredentials?.Password ?? string.Empty,
            PasswordChar = '●',
            Watermark = "User PW",
            MinWidth = 120,
        };
        var remember = new CheckBox
        {
            Content = "Remember login with OS credential store",
            IsChecked = request.CredentialPersistenceAvailable && request.SavedCredentials != null,
            IsEnabled = request.CredentialPersistenceAvailable,
        };
        var consent = new CheckBox
        {
            Content = "I consent to upload the selected TimeGrapher measurement log to the selected TimeGrapher service server for AI analysis.",
        };
        var errorText = new TextBlock
        {
            Foreground = Brushes.IndianRed,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };

        if (!request.CredentialPersistenceAvailable)
        {
            remember.Content = "Remember login unavailable: OS credential store probe failed";
        }

        var ok = new Button { Content = "Analyze", Width = 88, IsDefault = true, IsEnabled = consent.IsChecked == true };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        consent.IsCheckedChanged += (_, _) => ok.IsEnabled = consent.IsChecked == true;
        ok.Click += (_, _) =>
        {
            string selectedBackend = request.BackendOptions[Math.Max(0, backendCombo.SelectedIndex)].BaseUrl;
            string usernameText = username.Text?.Trim() ?? string.Empty;
            string passwordText = password.Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(usernameText) || string.IsNullOrWhiteSpace(passwordText))
            {
                errorText.Text = "Enter the provided User ID and User PW.";
                errorText.IsVisible = true;
                return;
            }

            if (consent.IsChecked != true)
            {
                errorText.Text = "Consent is required before uploading the measurement log.";
                errorText.IsVisible = true;
                return;
            }

            result = new AiAnalysisDialogResult(
                selectedBackend,
                usernameText,
                passwordText,
                remember.IsChecked == true && request.CredentialPersistenceAvailable,
                ConsentGranted: true);
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var backendLabel = new TextBlock { Text = "Server", VerticalAlignment = VerticalAlignment.Center };
        var userIdLabel = new TextBlock { Text = "User ID", VerticalAlignment = VerticalAlignment.Center };
        var userPwLabel = new TextBlock { Text = "User PW", VerticalAlignment = VerticalAlignment.Center };
        var formGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,8,*,16,Auto,8,*"),
            RowDefinitions = new RowDefinitions("Auto,10,Auto"),
        };
        Grid.SetRow(backendLabel, 0);
        Grid.SetColumn(backendLabel, 0);
        Grid.SetRow(backendCombo, 0);
        Grid.SetColumn(backendCombo, 2);
        Grid.SetColumnSpan(backendCombo, 5);
        Grid.SetRow(userIdLabel, 2);
        Grid.SetColumn(userIdLabel, 0);
        Grid.SetRow(username, 2);
        Grid.SetColumn(username, 2);
        Grid.SetRow(userPwLabel, 2);
        Grid.SetColumn(userPwLabel, 4);
        Grid.SetRow(password, 2);
        Grid.SetColumn(password, 6);
        formGrid.Children.Add(backendLabel);
        formGrid.Children.Add(backendCombo);
        formGrid.Children.Add(userIdLabel);
        formGrid.Children.Add(username);
        formGrid.Children.Add(userPwLabel);
        formGrid.Children.Add(password);
        var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = "Send the selected TimeGrapher measurement log to the selected TimeGrapher service server for a Korean AI analysis.",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(formGrid);
        panel.Children.Add(remember);
        panel.Children.Add(consent);
        panel.Children.Add(errorText);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        await dialog.ShowDialog(_owner);
        return result;
    }

    public async Task<IAiAnalysisDisplaySession> ShowAiAnalysisProgressAsync(AiAnalysisProgressDisplay display)
    {
        var dialog = new Window
        {
            Title = "AI Analysis",
            Width = 960,
            Height = 640,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var details = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 0, 0, 10),
        };
        var divider = new Border
        {
            Height = 1,
            Margin = new Avalonia.Thickness(0, 0, 0, 10),
        };
        divider.Bind(Border.BackgroundProperty, divider.GetResourceObservable("ChromeBorderBrush"));
        var contentHost = new ContentControl();
        var close = new Button
        {
            Content = "Close",
            Width = 80,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        close.Click += (_, _) => dialog.Close();

        var panel = new DockPanel { Margin = new Avalonia.Thickness(16) };
        DockPanel.SetDock(details, Dock.Top);
        DockPanel.SetDock(divider, Dock.Top);
        DockPanel.SetDock(close, Dock.Bottom);
        panel.Children.Add(details);
        panel.Children.Add(divider);
        panel.Children.Add(close);
        panel.Children.Add(contentHost);
        dialog.Content = panel;

        var session = new AiAnalysisDisplaySession(display.BackendBaseUrl, details, contentHost, close);
        // The progress window is non-modal: closing it (title-bar X / Close button)
        // must cancel the in-flight request whose result it would otherwise display.
        dialog.Closed += (_, _) => session.RaiseClosed();
        await session.ShowStatusAsync(display.StatusText);
        dialog.Show(_owner);
        return session;
    }

    private sealed class AiAnalysisDisplaySession : IAiAnalysisDisplaySession
    {
        private readonly string _backendBaseUrl;
        private readonly TextBlock _details;
        private readonly ContentControl _contentHost;
        private readonly Button _close;
        private Action? _onClosed;
        private bool _closed;

        public AiAnalysisDisplaySession(
            string backendBaseUrl,
            TextBlock details,
            ContentControl contentHost,
            Button close)
        {
            _backendBaseUrl = backendBaseUrl;
            _details = details;
            _contentHost = contentHost;
            _close = close;
        }

        public void OnClosed(Action callback)
        {
            // If the window already closed before the controller registered (it awaits
            // ShowStatusAsync first), fire immediately so the cancellation is not lost.
            if (_closed)
            {
                callback();
                return;
            }

            _onClosed += callback;
        }

        public void RaiseClosed()
        {
            _closed = true;
            _onClosed?.Invoke();
        }

        public Task ShowStatusAsync(string statusText)
        {
            _details.Text = $"Server: {_backendBaseUrl}\nStatus: Waiting for AI analysis";
            _contentHost.Content = BuildStatusContent(statusText);
            _close.IsDefault = false;
            _close.IsEnabled = false;
            return Task.CompletedTask;
        }

        public Task ShowResultAsync(AiAnalysisDisplay display)
        {
            _details.Text = $"Server: {display.BackendBaseUrl}\nModel: {display.Model}\nRequest ID: {display.RequestId}";
            _contentHost.Content = BuildExplanationContent(display.Explanation);
            _close.IsDefault = true;
            _close.IsEnabled = true;
            return Task.CompletedTask;
        }

        public Task ShowFailureAsync(AiAnalysisFailureDisplay failure)
        {
            _details.Text = FailureDetails(_backendBaseUrl, failure.RequestId);
            _contentHost.Content = BuildFailureContent(failure.Message);
            _close.IsDefault = true;
            _close.IsEnabled = true;
            return Task.CompletedTask;
        }

        private static string FailureDetails(string backendBaseUrl, string? requestId)
        {
            string requestIdText = requestId == null
                ? string.Empty
                : $"\nRequest ID: {requestId}";
            return $"Server: {backendBaseUrl}\nStatus: Request failed{requestIdText}";
        }

        private static Control BuildStatusContent(string statusText)
        {
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new ProgressBar
            {
                IsIndeterminate = true,
                Height = 6,
            });
            panel.Children.Add(new TextBlock
            {
                Text = statusText,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeight.Normal,
            });
            return panel;
        }

        private static Control BuildExplanationContent(string explanation)
        {
            Control explanationContent = MarkdownDisplayRenderer.Render(explanation);
            return new ScrollViewer
            {
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = explanationContent,
            };
        }

        private static Control BuildFailureContent(string message) => new TextBlock
        {
            Text = message,
            Foreground = Brushes.IndianRed,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeight.Normal,
        };
    }
    public async Task ShowErrorAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => dialog.Close();
        var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(ok);
        dialog.Content = panel;
        await dialog.ShowDialog(_owner);
    }

    private static string[] BuildBackendLabels(IReadOnlyList<AiBackendOption> options)
    {
        var labels = new string[options.Count];
        for (int i = 0; i < options.Count; i++)
        {
            labels[i] = options[i].DisplayName;
        }

        return labels;
    }

    private static int SelectedBackendIndex(IReadOnlyList<AiBackendOption> options, string selectedBackendBaseUrl)
    {
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i].BaseUrl == selectedBackendBaseUrl)
            {
                return i;
            }
        }

        return 0;
    }
}
