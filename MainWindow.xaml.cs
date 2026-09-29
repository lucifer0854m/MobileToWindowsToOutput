using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.Win32;
using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;

namespace PhoneSpeaker;

public sealed partial class MainWindow : Window
{
    private const string StartupRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "PhoneSpeaker";
    private const int SwMinimize = 6;

    private readonly ObservableCollection<DeviceInformation> _phones = [];
    private readonly string _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhoneSpeaker", "settings.json");
    private DeviceWatcher? _watcher;
    private AudioPlaybackConnection? _activeConnection;
    private Settings _settings = new();
    private bool _loadingSettings;
    private readonly bool _startMinimized;

    public MainWindow(bool startMinimized = false)
    {
        _startMinimized = startMinimized;
        InitializeComponent();
        PhonePicker.ItemsSource = _phones;
        Root.RequestedTheme = ElementTheme.Default;
        Activated += OnActivated;
        Closed += OnClosed;
        LoadSettings();
        StartWatcher();
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnActivated;
        if (_startMinimized)
        {
            await Task.Delay(300);
            ShowWindow(WinRT.Interop.WindowNative.GetWindowHandle(this), SwMinimize);
        }

        if (_settings.AutoReconnect && !string.IsNullOrWhiteSpace(_settings.LastDeviceId))
        {
            // DeviceWatcher can deliver Added asynchronously; retry once its initial enumeration completes.
            await Task.Delay(1800);
            var remembered = _phones.FirstOrDefault(d => d.Id == _settings.LastDeviceId);
            if (remembered is not null)
            {
                PhonePicker.SelectedItem = remembered;
                await ConnectAsync(remembered);
            }
        }
    }

    private void StartWatcher()
    {
        try
        {
            _watcher = DeviceInformation.CreateWatcher(AudioPlaybackConnection.GetDeviceSelector());
            _watcher.Added += Watcher_Added;
            _watcher.Updated += Watcher_Updated;
            _watcher.Removed += Watcher_Removed;
            _watcher.EnumerationCompleted += Watcher_EnumerationCompleted;
            _watcher.Start();
        }
        catch (Exception ex)
        {
            ShowMessage("Bluetooth audio discovery is unavailable. Pair a phone in Settings and check that the Bluetooth adapter and driver are enabled. " + ex.Message, InfoBarSeverity.Error);
        }
    }

    private void Watcher_Added(DeviceWatcher sender, DeviceInformation device) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_phones.All(d => d.Id != device.Id)) _phones.Add(device);
        if (_phones.Count == 1) PhonePicker.SelectedIndex = 0;
        if (_phones.Count == 0) StatusDetail.Text = " · No paired audio-capable phones found";
    });

    private void Watcher_Updated(DeviceWatcher sender, DeviceInformationUpdate update) => DispatcherQueue.TryEnqueue(() =>
    {
        var index = -1;
        for (var i = 0; i < _phones.Count; i++) if (_phones[i].Id == update.Id) { index = i; break; }
        if (index >= 0) _phones[index].Update(update);
    });

    private void Watcher_Removed(DeviceWatcher sender, DeviceInformationUpdate update) => DispatcherQueue.TryEnqueue(() =>
    {
        var device = _phones.FirstOrDefault(d => d.Id == update.Id);
        if (device is not null) _phones.Remove(device);
        if (_activeConnection?.DeviceId == update.Id)
        {
            _activeConnection.Dispose();
            _activeConnection = null;
            SetDisconnected("Phone left Bluetooth range or disconnected.");
            ShowMessage("The phone disconnected. Move it closer, check Bluetooth, then reconnect.", InfoBarSeverity.Warning);
        }
    });

    private void Watcher_EnumerationCompleted(DeviceWatcher sender, object args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_phones.Count == 0)
        {
            StatusDetail.Text = " · No paired audio-capable phones found";
            ShowMessage("Pair your phone first in Windows Settings → Bluetooth & devices. This list includes paired devices Windows exposes for remote audio playback.", InfoBarSeverity.Informational);
        }
    });

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (PhonePicker.SelectedItem is not DeviceInformation phone)
        {
            ShowMessage("Select a paired phone. If it is not listed, pair it in Windows Bluetooth settings and refresh.", InfoBarSeverity.Warning);
            return;
        }
        await ConnectAsync(phone);
    }

    private async Task ConnectAsync(DeviceInformation phone)
    {
        ConnectButton.IsEnabled = false;
        ConnectionStatus.Text = "Connecting";
        StatusDetail.Text = " · Enabling Windows Bluetooth audio receiver";
        StatusDot.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 240, 174, 52));
        try
        {
            if (_activeConnection is not null)
            {
                _activeConnection.StateChanged -= Connection_StateChanged;
                _activeConnection.Dispose();
                _activeConnection = null;
            }

            var connection = AudioPlaybackConnection.TryCreateFromId(phone.Id);
            if (connection is null)
            {
                SetDisconnected("Windows did not expose an A2DP audio receiver for this device.");
                ShowMessage("This paired device does not expose an audio streaming profile. Check its Bluetooth audio support and Windows Bluetooth driver.", InfoBarSeverity.Error);
                return;
            }

            _activeConnection = connection;
            connection.StateChanged += Connection_StateChanged;
            await connection.StartAsync();
            var result = await connection.OpenAsync();
            if (result.Status != AudioPlaybackConnectionOpenResultStatus.Success)
            {
                connection.Dispose();
                _activeConnection = null;
                SetDisconnected("Windows could not open the Bluetooth audio connection.");
                ShowMessage($"Phone connection failed ({result.Status}). Confirm the phone is paired and connected, Bluetooth is on, and the phone is not streaming to another output.", InfoBarSeverity.Error);
                return;
            }

            _settings.LastDeviceId = phone.Id;
            _settings.LastDeviceName = phone.Name;
            SaveSettings();
            SetConnected(phone.Name);
        }
        catch (Exception ex)
        {
            SetDisconnected("Connection failed.");
            var hint = ex.HResult == unchecked((int)0x8007048F)
                ? "Bluetooth appears to be disabled. Turn it on in Windows Settings."
                : "Windows could not connect. Re-pair the phone and verify that its Bluetooth audio profile is available.";
            ShowMessage(hint + " " + ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }

    private void Connection_StateChanged(AudioPlaybackConnection sender, object args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (sender.State == AudioPlaybackConnectionState.Opened)
            SetConnected(PhonePicker.SelectedItem is DeviceInformation d ? d.Name : "Phone");
        else if (sender.State == AudioPlaybackConnectionState.Closed)
            SetDisconnected("Bluetooth audio connection closed. Reconnect when the phone is available.");
    });

    private void SetConnected(string phoneName)
    {
        ConnectionStatus.Text = "Connected";
        StatusDot.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 46, 160, 99));
        StatusDetail.Text = " · " + phoneName;
        AudioStatus.Text = "Connected · Play audio on your phone to begin";
        DisconnectButton.IsEnabled = true;
        ConnectButton.IsEnabled = true;
    }

    private void SetDisconnected(string detail)
    {
        ConnectionStatus.Text = "Disconnected";
        StatusDot.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 137, 147, 164));
        StatusDetail.Text = " · " + detail;
        AudioStatus.Text = "Waiting for phone audio";
        DisconnectButton.IsEnabled = false;
        ConnectButton.IsEnabled = true;
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeConnection is not null)
        {
            _activeConnection.StateChanged -= Connection_StateChanged;
            _activeConnection.Dispose();
            _activeConnection = null;
        }
        SetDisconnected("Disconnected by you.");
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_watcher?.Status == DeviceWatcherStatus.Started)
        {
            var oldWatcher = _watcher;
            oldWatcher.Stop();
            for (var i = 0; i < 20 && oldWatcher.Status != DeviceWatcherStatus.Stopped; i++) await Task.Delay(100);
        }
        _phones.Clear();
        StartWatcher();
    }

    private void BluetoothSettings_Click(object sender, RoutedEventArgs e) => OpenSettings("ms-settings:bluetooth");
    private void SoundSettings_Click(object sender, RoutedEventArgs e) => OpenSettings("ms-settings:sound");

    private static void OpenSettings(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch { }
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        Root.RequestedTheme = Root.RequestedTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        ThemeButton.Content = Root.RequestedTheme == ElementTheme.Dark ? "☀  Theme" : "☾  Theme";
    }

    private void AutoReconnect_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        _settings.AutoReconnect = AutoReconnectToggle.IsChecked == true;
        SaveSettings();
    }

    private void LaunchAtSignIn_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;

        var enabled = LaunchAtSignInToggle.IsChecked == true;
        try
        {
            using var runKey = Registry.CurrentUser.CreateSubKey(StartupRunKey, writable: true);
            if (enabled)
            {
                var executable = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executable)) throw new InvalidOperationException("Could not locate PhoneSpeaker.exe.");
                runKey.SetValue(StartupValueName, $"\"{executable}\" --background", RegistryValueKind.String);
            }
            else
            {
                runKey.DeleteValue(StartupValueName, throwOnMissingValue: false);
            }
            _settings.LaunchAtSignIn = enabled;
            if (enabled)
            {
                _settings.AutoReconnect = true;
                AutoReconnectToggle.IsChecked = true;
            }
            SaveSettings();
        }
        catch (Exception ex)
        {
            _loadingSettings = true;
            LaunchAtSignInToggle.IsChecked = !enabled;
            _loadingSettings = false;
            ShowMessage("Could not update the Windows sign-in startup setting: " + ex.Message, InfoBarSeverity.Error);
        }
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath)) _settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(_settingsPath)) ?? new();
        }
        catch { _settings = new(); }
        _loadingSettings = true;
        AutoReconnectToggle.IsChecked = _settings.AutoReconnect;
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(StartupRunKey);
            LaunchAtSignInToggle.IsChecked = runKey?.GetValue(StartupValueName) is string;
        }
        catch { LaunchAtSignInToggle.IsChecked = false; }
        _loadingSettings = false;
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { ShowMessage("Could not save preferences: " + ex.Message, InfoBarSeverity.Warning); }
    }

    private void ShowMessage(string message, InfoBarSeverity severity)
    {
        MessageBar.Message = message;
        MessageBar.Severity = severity;
        MessageBar.IsOpen = true;
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (_watcher?.Status == DeviceWatcherStatus.Started) _watcher.Stop();
        if (_activeConnection is not null)
        {
            _activeConnection.StateChanged -= Connection_StateChanged;
            _activeConnection.Dispose();
        }
    }

    private sealed class Settings
    {
        public string? LastDeviceId { get; set; }
        public string? LastDeviceName { get; set; }
        public bool AutoReconnect { get; set; }
        public bool LaunchAtSignIn { get; set; }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
