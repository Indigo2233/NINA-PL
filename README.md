# NINA-PL (Planetary)

Planetary imaging and automation software for Windows, combining the sequencing power of [NINA](https://github.com/isbeorn/nina) with the high-speed capture capabilities of SharpCap and FireCapture.

## Features

- **NINA-style Advanced Sequencer** -- tree-based sequence builder with drag-and-drop, sequential/parallel/DSO target containers, conditions, and triggers
- **High-Speed Video Capture** -- ring-buffer capture engine supporting SER v3, AVI, and FITS output
- **Planetary Autofocus** -- contrast-based, Fourier energy, and Brenner gradient metrics with V-curve/parabola fitting
- **Planetary Guiding** -- disk centroid, limb, and surface feature tracking with PID-controlled corrections
- **Live Stacking** -- real-time frame alignment, quality evaluation, and wavelet sharpening
- **Native Camera Support** -- direct SDK integration for QHY, ZWO ASI, PlayerOne, Touptek/Ogmacam, Daheng (via shim), Hikvision (via shim)
- **ASCOM / Alpaca** -- telescope, focuser, filter wheel, rotator, flat panel, and switch control
- **Plugin System** -- MEF-based extensibility

## Prerequisites

| Requirement | Version |
|---|---|
| Windows | 10 / 11 (x64) |
| .NET SDK | **9.0** or later |
| Visual Studio | 2022 17.8+ (with **.NET desktop development** workload) *or* VS Code + C# Dev Kit |
| Git | any recent version |

> **Note:** The project targets `net9.0-windows` (WPF). It will not build on macOS/Linux.

## Getting the Source

```bash
git clone git@github.com:Indigo2233/NINA-PL.git
cd NINA-PL
```

## Build

### Command Line

```bash
dotnet restore NINA-PL.sln
dotnet build NINA-PL.sln -c Debug
```

The WPF executable will be at:

```
NINA.PL.WPF\bin\Debug\net9.0-windows\win-x64\NINA.PL.WPF.exe
```

### Visual Studio

1. Open `NINA-PL.sln`
2. Set **NINA.PL.WPF** as the startup project
3. Press **F5**

## Run

```bash
dotnet run --project NINA.PL.WPF
```

Or launch the compiled executable directly.

## Solution Structure

```
NINA-PL.sln
  NINA.PL.Core          Core interfaces, mediators, DTOs, logging, astronomy utilities
  NINA.PL.Equipment      ASCOM + native camera driver implementations
  NINA.PL.Capture        High-speed capture engine (ring buffer, SER/AVI/FITS writers)
  NINA.PL.Guider         Planetary guiding (disk, limb, surface tracking + PID)
  NINA.PL.AutoFocus      Contrast / Fourier / Brenner autofocus with curve fitting
  NINA.PL.LiveStack      Real-time stacking and quality evaluation
  NINA.PL.Image          Image processing helpers (OpenCV wrappers)
  NINA.PL.Sequencer      Sequence engine, containers, instructions, conditions, triggers
  NINA.PL.Profile        User profile / settings persistence
  NINA.PL.Plugin         MEF plugin host and interfaces
  NINA.PL.WPF            WPF application (MVVM, views, view-models)
  NINA.PL.Test           Unit tests (xUnit)
```

## Key NuGet Dependencies

| Package | Purpose |
|---|---|
| `CommunityToolkit.Mvvm` | MVVM source generators (`[ObservableProperty]`, `[RelayCommand]`) |
| `Microsoft.Extensions.DependencyInjection` | Service container / DI |
| `NLog.Extensions.Logging` | Structured logging |
| `OpenCvSharp4` | Image processing |

## Sequencer Architecture

The sequencer follows NINA's tree-based model:

- **Containers**: `SequenceContainer` (sequential), `ParallelContainer`, `DeepSkyObjectContainer`
- **Instructions**: 40+ instruction types covering camera, mount, focuser, guider, flat panel, power, and utility operations
- **Conditions**: Loop, time, altitude, sun/moon altitude, twilight, moon illumination, safety, meridian
- **Triggers**: Autofocus (after exposures / time / filter change), dither, meridian flip

Each instruction exposes NINA-aligned parameters (e.g., Cool Camera has `TargetTemperature` and `DurationMinutes`; Take Exposure has `Gain`, `Offset`, `Binning`, `ImageType`, `FilterName`, `TotalExposureCount`).

## Configuration

- Observer latitude/longitude: **Settings** tab
- Camera native SDK DLLs: place vendor DLLs in the application directory or system PATH
- ASCOM devices: install ASCOM Platform 6.x and device drivers

## Tests

```bash
dotnet test NINA-PL.sln
```

## License

This project is licensed under the Mozilla Public License 2.0, consistent with NINA.
