# Audio Mirror

A small Windows tray app that plays your system sound on **several output devices at once**.
For example: speakers + a monitor over HDMI, or headphones + a Bluetooth speaker.

Audio Mirror captures whatever plays on your **default** device (WASAPI loopback) and sends a copy
to the extra devices you choose. If a device uses a different sample rate or format, the audio is
resampled for it automatically.

> The UI is in Polish.

## Features

- Runs in the system tray, with no main window
- Pick any number of extra output devices from the tray menu
- The device list refreshes every time you open the menu, so newly plugged-in devices show up
- Changes apply right away, even while mirroring is on
- Automatic resampling between devices with different formats

## Requirements

- Windows 10 / 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (not needed for the self-contained build)

## Usage

1. Run `AudioMirror.exe`. A speaker icon appears in the tray.
2. Right-click the icon and tick the devices that should **also** play sound.
3. Click **▶ Włącz mirror** (turn on mirroring).
4. To stop, click **■ Zatrzymaj** (stop). **Zamknij** (close) exits the app.

The main sound always plays on the Windows default device. To change which device is the main one,
change the default device in Windows sound settings.

## Building

```bash
git clone https://github.com/<your-username>/audio-mirror.git
cd audio-mirror
dotnet build -c Release
```

Single-file, self-contained build (no .NET install needed on the target PC):

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

## Known limitations

- There can be a small delay (latency) between the main device and the extra devices. It depends on the hardware and is usually tens of milliseconds.
- Bluetooth devices usually add noticeably more delay.

## Built with

- [NAudio](https://github.com/naudio/NAudio) (MIT)

## License

[MIT](LICENSE)
