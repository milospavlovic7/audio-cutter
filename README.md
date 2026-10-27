# 🎧 Audio Cutter

A high-performance, native Windows batch audio trimming and cutting desktop utility built with .NET 10 WPF and FFmpeg. Quickly slice, split, and normalize multiple audio files simultaneously with precision and zero unnecessary re-encoding loss.

### 🌐 Web Showcase: [https://milospavlovic7.github.io/audio-cutter/](https://milospavlovic7.github.io/audio-cutter/)

---

## ✨ Key Features

- **Batch Multi-File Processing**: Queue dozens or hundreds of audio tracks and process them simultaneously with asynchronous worker pools.
- **Broad Format Support**: Works out of the box with `.mp3`, `.wav`, `.flac`, `.m4a`, `.aac`, `.ogg`, `.wma`, `.aiff`, and `.opus`.
- **Lossless Stream Copy**: Option to cut on keyframe boundaries without transcoding, preserving original master sound quality at lightning speeds.
- **Custom Time Boundaries**: Configure start and end timestamps (`HH:mm:ss.fff` or seconds) per file or apply global trim presets.
- **Detailed Diagnostic Logging**: Real-time progress bar, thread telemetry, and error reporting.
- **Cancellation & Pause**: Safe cancellation token support to gracefully halt background processing at any time.

---

## 🎮 How to Use

1. **Select Source Folder**: Click **Browse** to choose a directory containing audio files.
2. **Configure Trimming**:
   - Set **Start Time** and **End Time** (or segment length).
   - Select destination output directory.
3. **Execute**: Click **Pokreni Sečenje** (Start Cutting) to process the batch.
4. **Inspect**: Watch real-time logs and open the destination folder directly once completed.

---

## 🚀 Quick Start

### Option 1: Run Pre-compiled Binary
Double-click `Pokreni Audio Cutter.bat` or grab the latest standalone build from GitHub Releases.
*(Note: Requires `ffmpeg.exe` and `ffprobe.exe` placed inside the `binaries/` folder or installed on system PATH).*

### Option 2: Build from Source (.NET 10 / 8)
```bash
# Clone the repository
git clone https://github.com/milospavlovic7/audio-cutter.git
cd audio-cutter

# Build project
dotnet build AudioCutterApp.csproj
```
Run via Visual Studio 2022+ or `dotnet run --project AudioCutterApp.csproj`.

---

## 🛠️ Tech Stack

- **Platform**: Windows Desktop (.NET 10 / WPF)
- **Language**: C#
- **Engine**: FFmpeg / FFprobe process orchestration
- **UI Framework**: Modern XAML with custom controls and dark theme styling

---

## 📜 License

MIT License. Free for educational and personal use.
