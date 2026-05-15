# Digitālais Virinātājs (DigitalaisVirinatajs)

A small Windows desktop utility (WPF) that helps keep a Windows session awake by periodically nudging the cursor and briefly restoring/minimizing selected applications. Intended for scenarios where you need to prevent idle sleep or display timeout without changing system power settings.

Repository: `DigitalaisVirinatajs`  
Active UI file: `MainWindow.xaml.cs`  
Targets: .NET 10

## Key features
- Select running apps from a taskbar-aware list and keep them active.
- Periodic cursor "nudge" to prevent display sleep (`GetCursorPos` / `SetCursorPos`).
- Periodically restore and minimize chosen app windows to keep them active.
- Uses `SetThreadExecutionState` to request the system stay awake while running.
- System tray icon with quick exit option.

## How it works (implementation notes)
- App enumerates processes with visible taskbar windows and presents them as `AppEntry` items.
- When started:
  - Calls `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED)`.
  - Runs a background nudge loop to move the cursor by 1px occasionally (only if the cursor is otherwise idle).
  - Runs a per-app loop that restores then minimizes each app window on a schedule (uses `ShowWindow`, `SetForegroundWindow`).
- Includes a blocked list for known shell/system windows that should not appear.
- UI updates are driven by a `DispatcherTimer`.

## Usage
1. Build and open the solution in Visual Studio (2022/2026) or run:
   - dotnet build
2. Run the app.
3. Click `Refresh` to populate the list of visible apps.
4. Select one or more apps to keep awake.
5. Click `Start` — the app will begin nudging and cycling the selected windows.
6. Click `Stop` to end the behavior. Closing the window or using the tray icon exit will stop the service.

## UI & Tray
- Main window provides `Start`, `Stop`, `Refresh`, and a list of detected apps.
- A tray icon (labeled "Digitālais virinātājs") is available with an Exit command.

## Platform & limitations
- Windows-only; relies on native Win32 and DWM calls (`user32.dll`, `dwmapi.dll`, `kernel32.dll`).
- May not work for some modern / UWP / cloaked windows. The app checks DWM cloak state and window styles to detect taskbar-visible windows.
- Requires permission to control the foreground window and move the cursor — use responsibly.

## Security & privacy
- No network access or remote telemetry by default.
- The app programmatically moves the cursor and manipulates window state — do not run if that could interfere with critical input or automation.

## Troubleshooting
- If some apps do not appear, they may be cloaked, tool windows, or intentionally hidden by the OS.
- If the system still sleeps, ensure the app is running (tray icon visible) and not blocked by security policies.
- Check `MainWindow.xaml.cs` for the current implementation and blocked-process list.

## Contributing
- Open a PR with changes or file issues in the repository.
- Keep changes compatible with .NET 10 and avoid introducing telemetry or external dependencies without explicit intent.

## Attribution & license
- No license file is included in the repository. Add a `LICENSE` if you want to specify terms.
