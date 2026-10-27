using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AudioCutterApp.Models
{
    public enum CutMode
    {
        SilenceDetection,
        CustomCut
    }

    public class ProcessingOptions
    {
        public CutMode Mode { get; set; } = CutMode.SilenceDetection;

        // Silence options
        public bool SilenceTrimStart { get; set; } = true;
        public bool SilenceTrimEnd { get; set; } = true;
        public double SilenceThresholdDb { get; set; } = -50.0;
        public double MinSilenceDurationSec { get; set; } = 0.1;

        // Custom cut options (in seconds, millisecond precision)
        public double CustomCutStartSec { get; set; } = 0.0;
        public double CustomCutEndSec { get; set; } = 0.0;

        // Fast stream copy vs high-precision re-encode
        public bool FastStreamCopy { get; set; } = false;
    }

    public class AudioFileItem : INotifyPropertyChanged
    {
        private string _status = "Čeka";
        private string _details = "";
        private double _durationSec = 0;
        private string _durationFormatted = "--:--";

        public string FullPath { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }

        public double DurationSec
        {
            get => _durationSec;
            set
            {
                _durationSec = value;
                DurationFormatted = FormatTime(value);
                OnPropertyChanged();
            }
        }

        public string DurationFormatted
        {
            get => _durationFormatted;
            set
            {
                _durationFormatted = value;
                OnPropertyChanged();
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged();
            }
        }

        public string Details
        {
            get => _details;
            set
            {
                _details = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static string FormatTime(double seconds)
        {
            if (seconds <= 0) return "00:00.000";
            var ts = TimeSpan.FromSeconds(seconds);
            return string.Format("{0:D2}:{1:D2}.{2:D3}", (int)ts.TotalMinutes, ts.Seconds, ts.Milliseconds);
        }
    }
}
