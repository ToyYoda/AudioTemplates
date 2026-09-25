# Audio Templates

Tray app that switches the Windows default microphone and output device with a hotkey.

- Up to 5 lines, each with a microphone and an output device ("(keep current)" leaves that side unchanged).
- `Ctrl+Alt+1…5` (or `Ctrl+Alt+Numpad 1…5`) activates a line from anywhere. The modifier can be changed in the
  "Hotkey" dropdown: `Ctrl+Alt`, `Ctrl+Shift`, `Alt+Shift` or `Ctrl+Alt+Shift`. Win-key combinations aren't offered
  because Windows reserves them for the taskbar. Global hotkeys take priority over app shortcuts
  (e.g. `Ctrl+Shift+1` in Excel).
- Sets the device as default for all roles (Default Device **and** Default Communication Device).
- Closing/minimizing hides to the tray; left-click the tray icon to open, right-click for quick switching and Exit.
- Settings: `%APPDATA%\AudioTemplates\config.txt`. "Start with Windows" adds an HKCU Run entry (starts minimized).

## Download

**[AudioTemplates.exe (latest release)](https://github.com/ToyYoda/AudioTemplates/releases/latest/download/AudioTemplates.exe)**:
a single file with no installer; it needs the .NET Framework 4.x that ships with Windows 10/11.
The exe isn't code-signed, so SmartScreen may warn on first start ("More info" → "Run anyway").

## Build

No SDK needed — run `build.cmd`; it uses the C# compiler bundled with Windows (.NET Framework 4.x) and produces `AudioTemplates.exe`.

Releases are built by GitHub Actions: pushing a tag like `v1.1.0` builds the exe and publishes it as a release.

## Note for German keyboard layouts

`Ctrl+Alt` is the same as `AltGr`, so while the app runs `AltGr+2` (²) and `AltGr+3` (³) trigger lines 2 and 3 instead of typing those characters. Choose `Ctrl+Shift` or `Alt+Shift` in the Hotkey dropdown to avoid this.
