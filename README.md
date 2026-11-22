<p align="center">
  <img src="docs/icon.png" width="96" alt="Crest icon"/>
</p>

<h1 align="center">Crest</h1>
<p align="center"><b>The Dynamic Island / MacBook notch, for Windows.</b><br/>
Native, open source, no account, no telemetry.</p>

<p align="center">
  <a href="https://github.com/rishii-hub/crest/releases/latest"><b>Download for Windows 10/11</b></a> ·
  <a href="#features">Features</a> ·
  <a href="#build-from-source">Build</a>
</p>

<p align="center"><img src="docs/screenshots/tab-home.png" width="760" alt="Crest expanded"/></p>

---

Crest puts a small black notch at the top of your screen. It stays out of the way, shows live activities the way an iPhone or MacBook does (music, timers, volume, charging, AirPods-style Bluetooth pop-ups, camera/mic indicators), and opens into a control centre when you hover it.

It is a native WPF app on .NET 8. No Electron, no browser engine in the background.

## Features

### Live activities (collapsed notch)
| | |
|---|---|
| **Now playing** | Album art + an equalizer driven by the real audio level, tinted with the album's colour. Works with anything that talks to Windows media controls: Spotify, YouTube in Chrome/Edge/Firefox, Apple Music, VLC, Media Player. |
| **Timers** | Running Pomodoro / countdown / stopwatch shown live in the notch. |
| **Volume & brightness HUD** | macOS-style bar in the notch whenever volume or laptop brightness changes. |
| **Charging / low battery** | Plug-in animation with battery level; warnings at 20 %, 10 %, 5 %. |
| **Bluetooth** | "Connected / Disconnected" pop-up for earbuds, headphones and other paired devices. |
| **Privacy indicators** | Orange dot while an app uses the microphone, green for the camera (hover in the expanded view to see which app). |
| **Caps Lock** | On/off indicator. |

### Expanded notch (hover or click)
| Tab | What it does |
|---|---|
| **Home** | Media player with seek bar, clock, weather, quick toggles (Keep awake, Mute, Mic off, Snip, Lock), volume & brightness sliders. |
| **Shelf** | Drag files onto the notch to park them, then drag them out into any app. Text and links work too. "Drag all" moves everything at once. |
| **Quick Launch** | Pin apps, shortcuts, folders and links; click to open, drag to reorder. |
| **Clipboard** | History of text, images and files with search and pinning. Skips content password managers mark as private. |
| **Notes** | Autosaving scratchpad plus a to-do list. |
| **Timers** | Pomodoro (focus / break / long break / cycles / auto-start / daily count), countdown with presets, stopwatch with laps, water tracker with reminders. |
| **System** | CPU, memory, network speed, disks, battery and uptime with live graphs. |
| **Weather** | Current conditions, 12-hour strip and 6-day forecast from [Open-Meteo](https://open-meteo.com) (free, no API key). |
| **Clock** | Month calendar and world clocks for any Windows time zone. |
| **Settings** | Notch or floating Island style, three sizes, "thin line" idle mode, accent colours, hover delay, hide in full screen, hide from screen sharing, display choice, start with Windows, per-activity toggles. |

### Behaviour details
- **Stays out of your way.** It hides automatically for full-screen games, videos and presentations. The thin-line idle mode keeps browser tabs underneath clickable.
- **Doesn't steal focus.** Clicking buttons in the notch leaves your current app focused. Only text fields take keyboard focus, and focus goes back to your app when the notch closes.
- **Spring animations** tuned to match the iPhone Dynamic Island.
- **Per-monitor DPI aware**: sharp on 100 %–200 % scaling and mixed-DPI setups. It can follow the display your cursor is on.
- **Hide from screen capture** (optional): the notch won't appear in recordings, screenshots or Teams/Zoom/Meet shares.
- **Tray icon** with open, settings, hide, autostart and quit.
- **Shortcuts:** `Ctrl+Alt+N` open/close · `Ctrl+Alt+V` clipboard · `Ctrl+Alt+P` start/pause Pomodoro · `Esc` close.

## Install
1. Download **`Crest-win-x64.exe`** from [Releases](https://github.com/rishii-hub/crest/releases/latest). It's one self-contained file with no installer and no .NET needed.
   (`Crest-win-x64-small.exe` is the same app, but it needs the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).)
2. Run it. Windows SmartScreen may warn because the exe isn't code-signed. Click **More info → Run anyway**.
3. Optional: turn on *Settings → Start with Windows*.

Settings and data are stored in `%AppData%\Crest`. To uninstall, quit from the tray, delete the exe, and delete that folder.

## Privacy
- Nothing leaves your PC except weather requests to Open-Meteo, and only after you set a location.
- Clipboard history stays local. Images are kept in memory only, unless you pin them.
- No analytics, no accounts, no auto-updater phoning home.

## Known limitations
- Windows doesn't let apps reserve screen space for a notch, so a maximized window's top-centre pixels sit under it. Use **Thin line** idle mode or **Island** style if that bothers you.
- Windows notifications and calendar events can't be mirrored. Those APIs require a packaged (MSIX) app, which is on the roadmap.
- Brightness controls work on built-in laptop panels, not external monitors.
- Bluetooth headphone battery levels aren't exposed by Windows for most devices.

## Build from source
Requires the .NET 8 SDK on Windows 10 2004+ / Windows 11.

```powershell
git clone https://github.com/rishii-hub/crest
cd crest
dotnet run --project src/Crest
# single-file release build:
dotnet publish src/Crest -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o out
```

`Crest.exe --tour <folder>` walks through every tab and live activity and saves screenshots. CI runs it on every push as a visual smoke test.

### Project layout
```
src/Crest/
  NotchWindow.xaml(.cs)   window, shape, animations, live activities, hover/drag, full-screen detection
  Controls/               NotchShape, SpringEase, RingProgress, Sparkline, BatteryGlyph, Stepper
  Services/               Media (SMTC), Audio (Core Audio), Brightness (WMI), Power, Privacy,
                          Bluetooth, Clipboard, Timers, Weather, stores
  Views/                  one file per tab
  Core/                   Win32 interop, storage, shell thumbnails, helpers
```

## Contributing
Issues and PRs are welcome. Keep it native and light: no new background processes or polling faster than it needs to be. Run `--tour` before opening a PR and attach the screenshots of anything you changed.

## Credits
Inspired by Apple's Dynamic Island, macOS notch utilities like NotchNook and Boring Notch, and the Windows notch apps that came before. Crest is an independent project and isn't affiliated with Apple or Microsoft.

## License
[MIT](LICENSE)
