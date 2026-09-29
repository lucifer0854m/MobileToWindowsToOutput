# PhoneSpeaker

PhoneSpeaker uses Windows' built-in Bluetooth A2DP sink support through `Windows.Media.Audio.AudioPlaybackConnection`. It is a real Bluetooth audio receiver, not a device-discovery mockup. Windows 10 version 2004 (build 19041) or newer is required.

## Windows Bluetooth architecture and limits

Windows normally uses A2DP to send audio from the PC to Bluetooth speakers/headphones. For the reverse direction, Windows provides `AudioPlaybackConnection`: the app enables the receiver for a paired audio-capable device, then opens it. Windows' Bluetooth stack handles A2DP and renders the received stream to the system audio endpoints. The app does not access the encoded or decoded samples.

The API routes incoming audio through Windows' system playback path, so output selection is done in Windows Sound Settings. Windows does not expose this incoming remote stream to the app as a separate render stream/audio session. Consequently, this project cannot provide per-phone volume/mute, independent phone-vs-Windows mixing, or app-only routing to a non-default endpoint. The phone's own volume and Windows system volume remain available. Windows audio continues to work normally. The API is for media A2DP; it does not make phone call audio available as an app input.

The receiver selector reports paired devices that Windows exposes for audio playback connections. It is not a general radio scanner and this app does not pair devices directly. Pair the phone using Windows Bluetooth settings first. Windows and phone/driver behavior determine whether a given device advertises the needed audio profile. Some devices or OEM Bluetooth drivers may not expose it.

## Contents

- `PhoneSpeaker.sln` and `PhoneSpeaker.csproj`: Visual Studio solution and WinUI 3 project.
- `App.xaml`, `MainWindow.xaml`: WinUI resources and interface.
- `App.xaml.cs`, `MainWindow.xaml.cs`: app lifecycle, device watcher, A2DP connection, reconnection preference, and error handling.
- `app.manifest`: desktop app manifest and DPI awareness.
- `Properties/launchSettings.json`: Visual Studio/`dotnet run` launch profile.

## Features

- Discover paired phones that Windows exposes for Bluetooth audio playback.
- Connect and disconnect the Windows A2DP audio receiver from the app.
- Refresh the paired-device list and open Windows Bluetooth or Sound settings.
- Switch between light and dark themes.
- Optionally reconnect to the last phone when the app starts.
- Optionally launch at Windows sign-in, minimized to the taskbar. Enabling this also enables reconnect to the last phone.

Preferences are stored in `%LOCALAPPDATA%\PhoneSpeaker\settings.json`. The sign-in option is stored for the current Windows user in the standard Run registry key and can be turned off in the app.

## Requirements and permissions

- Windows 10 2004+; Windows 11 recommended.
- Visual Studio 2022 with the **WinUI application development** workload (called **Windows application development** in older VS versions), the Windows App SDK C# tools, and a Windows 10/11 SDK. Or use .NET 8 SDK plus the Visual Studio WinUI/MSIX build components; plain .NET SDK alone lacks the PRI build tasks.
- Bluetooth adapter and working Windows Bluetooth audio driver.
- `Microsoft.WindowsAppSDK` 1.7.250401001 is the project's NuGet dependency. WinUI 3 and WinRT API projections come from that package.
- This is an unpackaged desktop app. It does not declare UWP package capabilities or require elevation. Windows Bluetooth pairing/settings handle device consent and pairing.

## Build and run

1. In Visual Studio Installer, choose **Modify** for Visual Studio Community and install **WinUI application development**. Ensure the Windows App SDK C# tools and Windows 11 SDK are selected. This supplies the PRI/MSIX MSBuild tasks required by WinUI XAML projects. Restart Visual Studio afterward.
2. Open `PhoneSpeaker.sln` in Visual Studio 2022. Allow NuGet restore.
3. Select `x64` (or `ARM64`) and `Debug` or `Release`.
4. Press **F5**. Alternatively, from this folder run `dotnet restore` then `dotnet run --project PhoneSpeaker.csproj -r win-x64`.
5. For a command-line build, run `dotnet build PhoneSpeaker.csproj -c Release -r win-x64`.

The project targets .NET 8 and Windows SDK 10.0.19041.0. Install a matching Windows 10 SDK or newer through Visual Studio if MSBuild reports the target SDK is missing.

## Connect a phone

1. Turn on Bluetooth on the phone and PC.
2. In Windows, open **Settings → Bluetooth & devices → Add device → Bluetooth** and pair the phone. Accept the pairing prompt on both devices.
3. Open PhoneSpeaker, choose the phone in the list, and select **Connect**.
4. Start music/video on the phone. If Android asks where to play audio, choose the paired PC. iOS may show the PC among AirPlay/Bluetooth audio destinations after pairing; choose the PC as its Bluetooth audio output.
5. Select **Disconnect** to stop the receiver.

PhoneSpeaker can remember the last audio-capable paired phone. Enable **Reconnect to the last phone when PhoneSpeaker starts** to reconnect automatically when Windows enumerates that phone.

## Connect Bluetooth buds/speakers and choose the output

1. Pair/connect the buds or speaker to Windows through **Settings → Bluetooth & devices**.
2. In Windows, open **Settings → System → Sound → Output** and choose the PC speakers, wired device, HDMI/DisplayPort device, Bluetooth speaker, or buds.
3. PhoneSpeaker's received audio follows the current Windows system playback route. Change the Windows output there whenever needed. No special PhoneSpeaker setup is required for Bluetooth output devices.

## Troubleshooting

- **Bluetooth disabled / no phones listed:** enable Bluetooth in Windows Settings, check the radio in Device Manager, then refresh PhoneSpeaker's list.
- **Phone is not discoverable / missing from list:** PhoneSpeaker lists paired audio-capable devices, not arbitrary nearby devices. Pair the phone in Windows Settings, keep it awake/unlocked, and refresh.
- **Connect fails:** disconnect the phone from other audio hosts, verify it remains paired and connected, retry, then remove and pair it again in Windows Settings.
- **Audio profile unavailable:** the device/driver must expose a Bluetooth audio playback connection (A2DP source from phone to Windows sink). Generic Bluetooth discovery alone cannot supply audio. Update the PC's Bluetooth driver and Windows; some drivers/device combinations do not expose this profile.
- **Connected but silent:** start media playback on the phone, choose the PC as the phone's Bluetooth output, verify Windows Sound output and volume, and confirm PhoneSpeaker is still connected.
- **Bluetooth speaker/buds do not appear as output:** pair/connect them to Windows first, then select them in Windows **Settings → System → Sound → Output**.
- **Device disconnected:** move the phone closer, verify Bluetooth is on, and press Connect again. Turn on auto reconnect if desired.
- **Phone calls:** this receiver is for A2DP media playback. It does not provide call audio routing or HFP call handling.

## Sources

- [Microsoft: Enable audio playback from remote Bluetooth-connected devices](https://learn.microsoft.com/en-us/windows/apps/develop/media-playback/enable-remote-audio-playback)
- [Microsoft: Bluetooth Classic Audio](https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/bluetooth-classic-audio)
- [Microsoft: Windows 11 Bluetooth profile support](https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/general-bluetooth-support-in-windows)

## Install PhoneSpeaker on Windows

The Release build is packaged as an x64 MSIX app. Double-click `Install PhoneSpeaker.cmd` and follow the prompts. Windows will ask you to trust the app's local development signing certificate before installing. This certificate is only for this locally built package; a public distribution release should be signed with a trusted code-signing certificate or distributed through the Microsoft Store.

After installation, launch **PhoneSpeaker** from the Start menu. To remove it, open **Settings → Apps → Installed apps**, find PhoneSpeaker, and select **Uninstall**.

To rebuild the install package from Visual Studio's MSBuild tools, build the `PhoneSpeaker.csproj` Release configuration for x64 with `GenerateAppxPackageOnBuild=true` and `AppxPackageSigningEnabled=false`, then sign the generated MSIX with a certificate whose subject matches the Publisher in `Package.appxmanifest`.
