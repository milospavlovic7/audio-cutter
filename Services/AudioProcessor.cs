using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AudioCutterApp.Models;

namespace AudioCutterApp.Services
{
    public class AudioProcessor
    {
        private readonly string _ffmpegPath;
        private readonly string _ffprobePath;

        public AudioProcessor()
        {
            _ffmpegPath = ResolveBinaryPath("ffmpeg.exe");
            _ffprobePath = ResolveBinaryPath("ffprobe.exe");
        }

        public bool IsFFmpegAvailable => File.Exists(_ffmpegPath);
        public bool IsFFprobeAvailable => File.Exists(_ffprobePath);

        public string FFmpegPath => _ffmpegPath;
        public string FFprobePath => _ffprobePath;

        private static string ResolveBinaryPath(string binaryName)
        {
            // 1. Check in App binaries/ subdirectory
            var inBinaries = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "binaries", binaryName);
            if (File.Exists(inBinaries)) return inBinaries;

            // 2. Check in App root directory
            var inRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, binaryName);
            if (File.Exists(inRoot)) return inRoot;

            // 3. Check WinGet known install folder for Gyan FFmpeg
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var wingetSearch = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(wingetSearch))
            {
                var candidates = Directory.GetFiles(wingetSearch, binaryName, SearchOption.AllDirectories);
                if (candidates.Length > 0 && File.Exists(candidates[0]))
                {
                    return candidates[0];
                }
            }

            // 4. Return plain name to rely on PATH
            return binaryName;
        }

        public async Task<double> GetDurationAsync(string filePath, CancellationToken ct = default)
        {
            if (!File.Exists(filePath)) return 0;

            var psi = new ProcessStartInfo
            {
                FileName = _ffprobePath,
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var output = await outputTask;

            if (double.TryParse(output.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double duration))
            {
                return duration;
            }

            return 0;
        }

        public async Task<(double startSilenceEnd, double endSilenceStart)> DetectSilenceAsync(
            string filePath,
            double thresholdDb = -50.0,
            double minDurationSec = 0.1,
            double totalDuration = 0,
            CancellationToken ct = default)
        {
            if (totalDuration <= 0)
            {
                totalDuration = await GetDurationAsync(filePath, ct);
            }

            var thresholdStr = thresholdDb.ToString("0.0", CultureInfo.InvariantCulture);
            var minDurStr = minDurationSec.ToString("0.00", CultureInfo.InvariantCulture);

            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = $"-hide_banner -vn -i \"{filePath}\" -af silencedetect=noise={thresholdStr}dB:d={minDurStr} -f null -",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var errorOutput = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            // Parse lines like:
            // [silencedetect @ ...] silence_start: 0
            // [silencedetect @ ...] silence_end: 2.145 | silence_duration: 2.145
            var silenceIntervals = new List<(double start, double? end)>();

            var startRegex = new Regex(@"silence_start:\s*([0-9\.]+)", RegexOptions.Compiled);
            var endRegex = new Regex(@"silence_end:\s*([0-9\.]+)", RegexOptions.Compiled);

            double? currentStart = null;

            foreach (var line in errorOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var startMatch = startRegex.Match(line);
                if (startMatch.Success && double.TryParse(startMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double sStart))
                {
                    currentStart = sStart;
                }

                var endMatch = endRegex.Match(line);
                if (endMatch.Success && double.TryParse(endMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double sEnd))
                {
                    if (currentStart.HasValue)
                    {
                        silenceIntervals.Add((currentStart.Value, sEnd));
                        currentStart = null;
                    }
                    else
                    {
                        silenceIntervals.Add((0, sEnd));
                    }
                }
            }

            if (currentStart.HasValue)
            {
                // Silence went until the very end
                silenceIntervals.Add((currentStart.Value, null));
            }

            double startSilenceEnd = 0.0;
            double endSilenceStart = totalDuration;

            // Check silence at beginning
            if (silenceIntervals.Count > 0)
            {
                var first = silenceIntervals[0];
                if (first.start <= 0.15) // Started near the very beginning
                {
                    startSilenceEnd = first.end ?? totalDuration;
                }
            }

            // Check silence at end
            if (silenceIntervals.Count > 0)
            {
                var last = silenceIntervals[^1];
                if (!last.end.HasValue || last.end.Value >= totalDuration - 0.25)
                {
                    endSilenceStart = last.start;
                }
            }

            return (startSilenceEnd, endSilenceStart);
        }

        public async Task ProcessFileAsync(
            AudioFileItem item,
            string outputDirectory,
            ProcessingOptions options,
            Action<string>? onLog = null,
            CancellationToken ct = default)
        {
            if (!File.Exists(item.FullPath))
            {
                item.Status = "Greška: Fajl ne postoji";
                return;
            }

            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(outputDirectory, item.FileName);
            item.OutputPath = outputPath;

            double duration = item.DurationSec;
            if (duration <= 0)
            {
                duration = await GetDurationAsync(item.FullPath, ct);
                item.DurationSec = duration;
            }

            if (duration <= 0)
            {
                item.Status = "Greška: Ne mogu očitati trajanje";
                return;
            }

            double cutStart = 0.0;
            double cutEndTimestamp = duration;

            if (options.Mode == CutMode.SilenceDetection)
            {
                item.Status = "Detektujem tišinu...";
                onLog?.Invoke($"[{item.FileName}] Detektovanje tišine (prag: {options.SilenceThresholdDb}dB)...");

                var (startSilenceEnd, endSilenceStart) = await DetectSilenceAsync(
                    item.FullPath,
                    options.SilenceThresholdDb,
                    options.MinSilenceDurationSec,
                    duration,
                    ct);

                if (options.SilenceTrimStart && startSilenceEnd > 0)
                {
                    cutStart = startSilenceEnd;
                }

                if (options.SilenceTrimEnd && endSilenceStart < duration && endSilenceStart > cutStart)
                {
                    cutEndTimestamp = endSilenceStart;
                }

                double trimmedStart = cutStart;
                double trimmedEnd = duration - cutEndTimestamp;
                if (trimmedStart <= 0.001 && trimmedEnd <= 0.001)
                {
                    item.Details = "Nije detektovana tišina (sačuvano bez sečenja)";
                    onLog?.Invoke($"[{item.FileName}] Nije detektovana tišina koja prelazi prag od {options.SilenceThresholdDb}dB.");
                }
                else
                {
                    item.Details = $"Tišina isečena: Početak {trimmedStart:F3}s, Kraj {trimmedEnd:F3}s";
                    onLog?.Invoke($"[{item.FileName}] Isečak: Početak od {cutStart:F3}s do {cutEndTimestamp:F3}s (ukupno isečeno: P {trimmedStart:F3}s, K {trimmedEnd:F3}s)");
                }
            }
            else // CustomCut
            {
                cutStart = Math.Max(0, options.CustomCutStartSec);
                var cutFromEnd = Math.Max(0, options.CustomCutEndSec);
                cutEndTimestamp = Math.Max(cutStart, duration - cutFromEnd);

                item.Details = $"Custom rez: Početak {cutStart:F3}s, Kraj {cutFromEnd:F3}s";
                onLog?.Invoke($"[{item.FileName}] Custom rez: Početak {cutStart:F3}s, Kraj isečen {cutFromEnd:F3}s");
            }

            double targetDuration = cutEndTimestamp - cutStart;
            if (targetDuration <= 0.05)
            {
                item.Status = "Greška: Zadata dužina je prekratka";
                onLog?.Invoke($"[{item.FileName}] Greška: Rez bi ostavio manje od 0.05s zvuka.");
                return;
            }

            item.Status = "Sečenje u toku...";

            var startStr = cutStart.ToString("0.000", CultureInfo.InvariantCulture);
            var durStr = targetDuration.ToString("0.000", CultureInfo.InvariantCulture);

            // Determine codec settings
            string codecArgs;
            if (options.FastStreamCopy)
            {
                codecArgs = "-c copy";
            }
            else
            {
                var ext = Path.GetExtension(item.FullPath).ToLowerInvariant();
                codecArgs = ext switch
                {
                    ".mp3" => "-c:a libmp3lame -q:a 0",
                    ".wav" => "-c:a pcm_s16le",
                    ".flac" => "-c:a flac",
                    ".m4a" or ".aac" => "-c:a aac -b:a 320k",
                    ".ogg" => "-c:a libvorbis -q:a 8",
                    _ => "-c:a copy"
                };
            }

            // High precision seek: input -ss with -to or output -ss with -t
            // Placing -ss before -i with -accurate_seek or placing after -i
            // Placing -ss and -t after -i ensures sample-accurate cutting with zero glitches
            var arguments = $"-y -hide_banner -i \"{item.FullPath}\" -ss {startStr} -t {durStr} {codecArgs} -map_metadata 0 -id3v2_version 3 \"{outputPath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var errTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var errOutput = await errTask;

            if (process.ExitCode == 0 && File.Exists(outputPath))
            {
                item.Status = "Završeno";
                onLog?.Invoke($"[{item.FileName}] Uspešno sačuvano u: {outputPath}");
            }
            else
            {
                item.Status = "Greška";
                onLog?.Invoke($"[{item.FileName}] Greška pri sečenju: {errOutput}");
            }
        }
    }
}
