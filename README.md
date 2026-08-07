# RhythKit

The best external toolkit for Rhythia with the main focus on quality of life features!

RhythKit is a modern WPF (Windows Presentation Foundation) desktop application written in **C# and XAML**, providing a clean, animated UI for creating and managing Rhythia colorsets.

> **Note:** This is the C# rewrite of the original AutoHotkey (`main.ahk`) program, now rebuilt as a proper cross-compilable .NET desktop app with a much nicer interface.

## Features

- 🎨 **Colorset Maker** — Create custom note colorsets for Rhythia
  - Set the number of colors/swatches you want (1–50)
  - Each swatch is independently colorable via an interactive **hue wheel** (click or drag)
  - Live hex preview for the selected color
  - Save the colorset as a `.txt` file with **one hex color per row** (e.g. `#111111`)
  - Pick a custom colorset name and save path
  - Browse to a folder with the native folder picker
  - Recent colorsets history panel — click to load a previously saved colorset

- ⚙️ **Settings** — Configure the default colorsets folder, persisted to `RhythKit_Settings.ini`
## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) (or newer)
- Windows 10/11 (WPF)

## Build & Run

```bash
# Build
dotnet build RhythKit.sln

# Run (on Windows)
dotnet run --project src/RhythKit/RhythKit.csproj
```

The app creates a `colorsets` folder next to the executable by default and saves colorset `.txt` files there.

## Colorset File Format

Each colorset is saved as a plain text file with one hex color per row:

```
#FF4CC3
#4CC3FF
#A44CFF
```

No headers, no delimiters — just one `#RRGGBB` value per line.

## Logo / Icon

The app icon and in-app logo use the same `logo.png` design (blue rounded square with flowing curves and dots), customizable rebranding to **RhythKit** ("Rhyth" in white + "Kit" in accent blue).

## Project Structure

```
src/RhythKit/
├── App.xaml / App.xaml.cs        # Application entry point
├── MainWindow.xaml(.cs)          # Main window, title bar, sidebar
├── Controls/
│   ├── ColorsetMakerPage.xaml    # Main colorset UI
│   ├── SettingsPage.xaml         # Settings UI
│   ├── HueWheel.cs               # Interactive hue wheel control
│   └── RhythKitLogo.xaml         # Custom-drawn logo
├── Models/ColorItem.cs           # Color swatch model
├── Services/
│   ├── ColorsetService.cs        # Save/load colorsets
│   ├── SettingsService.cs        # Persist settings
│   └── ColorMath.cs              # HSV/HSL conversions
├── Themes/Theme.xaml             # Dark theme + styles
└── ViewModels/                   # MVVM view models
```

## License

Licensed under the MIT License. See [LICENSE](LICENSE).
