# Audio Mirror

[![CI](https://github.com/MrMarioo/AudioMirror/actions/workflows/ci.yml/badge.svg)](https://github.com/MrMarioo/AudioMirror/actions/workflows/ci.yml)
![.NET 8](https://img.shields.io/badge/.NET-8-512BD4)
![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6)
[![License: MIT](https://img.shields.io/badge/license-MIT-green)](LICENSE)

A small Windows tray app that plays your system sound on **several output devices at once**,
for example speakers + a monitor over HDMI, or headphones + a Bluetooth speaker.

Windows can only play sound on one device at a time. Audio Mirror captures whatever plays on your
**default** device (WASAPI loopback) and sends a copy to the extra devices you choose, resampling
on the fly when their formats differ.

## Features

- Lives in the system tray, no main window
- Mirror to any number of extra output devices
- **Handles device changes**: follows the Windows default device, survives unplugging, and picks a
  device back up automatically when it's plugged in again
- **Drift compensation**: stops extra devices from falling further and further behind over long
  sessions, even though each one runs on its own hardware clock
- Remembers your devices and on/off state between runs
- English and Polish UI, following the Windows display language

## Download

Each tagged version is published to [Releases](https://github.com/MrMarioo/AudioMirror/releases) in two variants:

| File | Size | Needs |
| --- | --- | --- |
| `AudioMirror-x.y.z-win-x64.exe` | ~65 MB | nothing |
| `AudioMirror-x.y.z-win-x64-net8-runtime.exe` | ~0.5 MB | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |

## Usage

1. Run `AudioMirror.exe`. A speaker icon appears in the tray.
2. Right-click the icon and tick the devices that should **also** play sound.
3. Click **▶ Start mirroring**.

The main sound always plays on the Windows default device. To change which device is the main one,
change the default device in Windows sound settings; Audio Mirror follows it automatically.

## How it works

```
                      ┌──────────────────────── per extra device ───────────────────────┐
 default device ──▶ WASAPI      ──▶  BufferedWaveProvider ──▶ (MediaFoundation  ──▶ WasapiOut ──▶ device
 (loopback)         loopback          + LatencyGuard            resampler, if the
                    capture                                     format differs)
                      └──────────────────────────────────────────────────────────────────┘
```

- **Capture.** One `WasapiLoopbackCapture` records what the default device plays. Its capture thread
  fans every packet out to one `OutputPipe` per extra device.
- **Clock drift.** The default device and each extra device run on separate crystals, so a buffer
  between them slowly fills up. Without correction the extra devices would fall seconds behind over a
  long session. `LatencyGuard` trims a pipe's buffer back to ~80 ms whenever it grows past 250 ms,
  always in whole audio frames so channels never swap.
- **Device changes.** `IMMNotificationClient` callbacks arrive on a COM thread, so they are only
  posted to the UI thread, debounced (Windows sends bursts), and compared with the current routing.
  The pipeline is rebuilt only when the routing actually changes, because each restart is audible.
- **Threading.** The pipe list is shared between the UI thread and the capture thread under a lock.
  The capture is disposed *outside* that lock, because disposing waits for the capture thread.

## Project structure

```
src/AudioMirror/
  Core/            application logic, no NAudio/WinForms dependencies
    MirrorController.cs   state machine: what the user wants vs. which devices exist
    MirrorRouting.cs      which device feeds which, and when two routings are "the same"
    I*.cs                 abstractions the controller depends on
  Audio/           NAudio / WASAPI implementations
    WasapiMirrorEngine.cs   capture + fan-out
    OutputPipe.cs           buffer → resampler → output for one device
    LatencyGuard.cs         drift compensation (pure maths)
    WasapiAudioDevices.cs   device list + change notifications
  Settings/        JSON settings in %AppData%\AudioMirror
  UI/              tray icon and menu
  Resources/       English and Polish strings
  Program.cs       composition root
tests/AudioMirror.Tests/   xUnit tests, no audio hardware needed
```

`MirrorController` depends only on interfaces (`IAudioDevices`, `IMirrorEngine`, `ISettingsStore`,
`IUserNotifier`). The tests drive it with in-memory fakes: unplugging devices, default-device
switches, engine faults, stale faults from a replaced pipeline, and restoring the previous session.

## Building

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
git clone https://github.com/MrMarioo/AudioMirror.git
cd AudioMirror
dotnet build -c Release
dotnet test -c Release
```

Single-file build that runs without .NET installed:

```bash
dotnet publish src/AudioMirror -c Release -r win-x64 -p:PublishSingleFile=true -o publish
```

Pushing a `v*` tag runs the [release workflow](.github/workflows/ci.yml), which publishes both
`.exe` variants to GitHub Releases.

## Troubleshooting

A log is written to `%LocalAppData%\AudioMirror\audiomirror.log`.

## Known limitations

- The extra devices lag slightly behind the main one (typically tens of milliseconds, depending on
  hardware). Bluetooth devices add noticeably more.
- Drift compensation only handles an extra device that plays *slower* than the main one: it keeps
  that device's lag between roughly 80 and 250 ms, and each trim may cause a very short click. An
  extra device that plays *faster* runs out of buffered audio now and then and plays a moment of
  silence instead. Proper fix: adaptive resampling (small continuous rate adjustment).

## Built with

- [NAudio](https://github.com/naudio/NAudio) (MIT)

## License

[MIT](LICENSE)
