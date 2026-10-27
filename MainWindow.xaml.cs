using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AudioCutterApp.Models;
using AudioCutterApp.Services;

namespace AudioCutterApp
{
    public partial class MainWindow : Window
    {
        private readonly AudioProcessor _processor;
        private readonly ObservableCollection<AudioFileItem> _files = new();
        private CancellationTokenSource? _cts;
        private string? _selectedSourceFolder;
        private string? _outputFolder;

        private static readonly string[] SupportedExtensions = 
        { 
            ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".wma", ".aiff", ".opus" 
        };

        public MainWindow()
        {
            InitializeComponent();
            _processor = new AudioProcessor();
            GridFiles.ItemsSource = _files;

            Log("Audio Cutter pokrenut.");
            if (_processor.IsFFmpegAvailable)
            {
                Log($"FFmpeg spreman: {_processor.FFmpegPath}");
            }
            else
            {
                Log("Upozorenje: FFmpeg nije pronađen automatski. Proverite instalaciju.");
            }
        }

        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                TxtLogs.AppendText($"[{timestamp}] {message}\n");
                TxtLogs.ScrollToEnd();
            });
        }

        private void RbMode_Changed(object sender, RoutedEventArgs e)
        {
            if (PanelSilence == null || PanelCustom == null) return;

            if (RbSilenceMode.IsChecked == true)
            {
                PanelSilence.Visibility = Visibility.Visible;
                PanelCustom.Visibility = Visibility.Collapsed;
            }
            else
            {
                PanelSilence.Visibility = Visibility.Collapsed;
                PanelCustom.Visibility = Visibility.Visible;
            }
        }

        private void SliderSilenceDb_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtSilenceDbVal != null)
            {
                TxtSilenceDbVal.Text = $"{e.NewValue:0} dB";
            }
        }

        private void BtnSelectFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Izaberite folder sa audio fajlovima"
            };

            if (dialog.ShowDialog() == true)
            {
                SetSelectedFolder(dialog.FolderName);
            }
        }

        private void TxtSourceFolder_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                TryApplyManualFolder();
            }
        }

        private void TxtSourceFolder_LostFocus(object sender, RoutedEventArgs e)
        {
            TryApplyManualFolder();
        }

        private void TryApplyManualFolder()
        {
            var path = TxtSourceFolder.Text?.Trim();
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path) && path != _selectedSourceFolder)
            {
                SetSelectedFolder(path);
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_selectedSourceFolder))
            {
                SetSelectedFolder(_selectedSourceFolder);
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var dropped = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (dropped != null && dropped.Length > 0)
                {
                    var target = dropped[0];
                    if (Directory.Exists(target))
                    {
                        SetSelectedFolder(target);
                    }
                    else if (File.Exists(target))
                    {
                        var parentDir = Path.GetDirectoryName(target);
                        if (!string.IsNullOrEmpty(parentDir))
                        {
                            SetSelectedFolder(parentDir);
                        }
                    }
                }
            }
        }

        private void SetSelectedFolder(string folderPath)
        {
            try
            {
                _selectedSourceFolder = folderPath;
                TxtSourceFolder.Text = folderPath;
                TxtSourceFolder.Foreground = System.Windows.Media.Brushes.White;

                // Determine parent / origin root and construct "Folder CUT"
                var parent = Directory.GetParent(folderPath)?.FullName;
                var folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

                if (string.IsNullOrEmpty(parent))
                {
                    _outputFolder = Path.Combine(folderPath, folderName + " CUT");
                }
                else
                {
                    _outputFolder = Path.Combine(parent, folderName + " CUT");
                }

                TxtOutputFolderPreview.Text = _outputFolder;
                BtnOpenFolder.IsEnabled = false;

                ScanFolderFiles(folderPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Greška pri otvaranju foldera: {ex.Message}", "Greška", MessageBoxButton.OK, MessageBoxImage.Error);
                Log($"Greška: {ex.Message}");
            }
        }

        private async void ScanFolderFiles(string folderPath)
        {
            _files.Clear();
            ProgressBarWork.Value = 0;
            TxtPercent.Text = "0%";
            TxtCurrentAction.Text = "Skeniranje fajlova...";

            try
            {
                var dirInfo = new DirectoryInfo(folderPath);
                var foundFiles = dirInfo.GetFiles("*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => SupportedExtensions.Contains(f.Extension.ToLowerInvariant()))
                    .OrderBy(f => f.Name)
                    .ToList();

                TxtFileCount.Text = $"{foundFiles.Count} pesama pronađeno";

                if (foundFiles.Count == 0)
                {
                    TxtCurrentAction.Text = "Nema pronađenih audio fajlova u selektovanom folderu.";
                    Log($"U folderu '{folderPath}' nije pronađen nijedan podržani audio fajl ({string.Join(", ", SupportedExtensions)}).");
                    return;
                }

                foreach (var file in foundFiles)
                {
                    _files.Add(new AudioFileItem
                    {
                        FullPath = file.FullName,
                        FileName = file.Name,
                        Extension = file.Extension,
                        FileSizeBytes = file.Length,
                        Status = "Čeka",
                        Details = ""
                    });
                }

                TxtCurrentAction.Text = $"Pronađeno {foundFiles.Count} fajlova. Učitavanje dužina...";
                Log($"Pronađeno {foundFiles.Count} audio fajlova u folderu: {folderPath}");

                // Asynchronously load durations in background
                _ = Task.Run(async () =>
                {
                    foreach (var item in _files.ToList())
                    {
                        try
                        {
                            var duration = await _processor.GetDurationAsync(item.FullPath);
                            Dispatcher.Invoke(() => item.DurationSec = duration);
                        }
                        catch
                        {
                            // Ignore preview duration errors
                        }
                    }
                    Dispatcher.Invoke(() =>
                    {
                        TxtCurrentAction.Text = "Spremno za obradu";
                    });
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Greška pri skeniranju foldera: {ex.Message}", "Greška", MessageBoxButton.OK, MessageBoxImage.Error);
                Log($"Greška pri skeniranju: {ex.Message}");
            }
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedSourceFolder) || _files.Count == 0 || string.IsNullOrEmpty(_outputFolder))
            {
                MessageBox.Show("Molimo izaberite folder sa audio fajlovima pre pokretanja.", "Obaveštenje", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Build options
            var options = new ProcessingOptions();
            if (RbSilenceMode.IsChecked == true)
            {
                options.Mode = CutMode.SilenceDetection;
                options.SilenceTrimStart = ChkSilenceStart.IsChecked == true;
                options.SilenceTrimEnd = ChkSilenceEnd.IsChecked == true;

                if (!options.SilenceTrimStart && !options.SilenceTrimEnd)
                {
                    MessageBox.Show("Morate označiti barem uklanjanje tišine sa početka ili sa kraja.", "Upozorenje", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                options.SilenceThresholdDb = SliderSilenceDb.Value;

                if (double.TryParse(TxtMinSilenceDuration.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double minDur) && minDur > 0)
                {
                    options.MinSilenceDurationSec = minDur;
                }
            }
            else
            {
                options.Mode = CutMode.CustomCut;

                if (!double.TryParse(TxtCutStartSec.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double cutStart) || cutStart < 0)
                {
                    MessageBox.Show("Neispravan unos za sečenje sa početka (mora biti broj >= 0, npr. 5.000).", "Greška u unosu", MessageBoxButton.OK, MessageBoxImage.Warning);
                    TxtCutStartSec.Focus();
                    return;
                }

                if (!double.TryParse(TxtCutEndSec.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double cutEnd) || cutEnd < 0)
                {
                    MessageBox.Show("Neispravan unos za sečenje sa kraja (mora biti broj >= 0, npr. 3.500 ili 0.000).", "Greška u unosu", MessageBoxButton.OK, MessageBoxImage.Warning);
                    TxtCutEndSec.Focus();
                    return;
                }

                if (cutStart == 0 && cutEnd == 0)
                {
                    MessageBox.Show("I početak i kraj su podešeni na 0.000s, što znači da fajlovi ne bi bili skraćeni. Unesite željeni rez.", "Obaveštenje", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                options.CustomCutStartSec = cutStart;
                options.CustomCutEndSec = cutEnd;
            }

            options.FastStreamCopy = ChkFastStreamCopy.IsChecked == true;

            // UI state
            _cts = new CancellationTokenSource();
            BtnStart.IsEnabled = false;
            BtnCancel.IsEnabled = true;
            BtnSelectFolder.IsEnabled = false;
            BtnOpenFolder.IsEnabled = false;

            Log($"=== Započeta obrada {_files.Count} fajlova ===");
            Log($"Odredišni CUT folder: {_outputFolder}");
            if (options.Mode == CutMode.SilenceDetection)
            {
                Log($"Režim: Automatsko uklanjanje tišine (Početak: {options.SilenceTrimStart}, Kraj: {options.SilenceTrimEnd}, Prag: {options.SilenceThresholdDb}dB)");
            }
            else
            {
                Log($"Režim: Custom rez (Početak: {options.CustomCutStartSec:F3}s, Kraj: {options.CustomCutEndSec:F3}s)");
            }

            int processedCount = 0;
            int total = _files.Count;

            try
            {
                for (int i = 0; i < total; i++)
                {
                    if (_cts.Token.IsCancellationRequested)
                    {
                        Log("Obrada je prekinuta od strane korisnika.");
                        TxtCurrentAction.Text = "Obrada otkazana.";
                        break;
                    }

                    var item = _files[i];
                    TxtCurrentAction.Text = $"Obrađujem ({i + 1}/{total}): {item.FileName}";

                    await _processor.ProcessFileAsync(
                        item,
                        _outputFolder,
                        options,
                        onLog: Log,
                        ct: _cts.Token
                    );

                    processedCount++;
                    double percent = (double)processedCount / total * 100;
                    ProgressBarWork.Value = percent;
                    TxtPercent.Text = $"{percent:0}%";
                }

                if (!_cts.Token.IsCancellationRequested)
                {
                    TxtCurrentAction.Text = $"Završeno! Uspešno obrađeno {processedCount} od {total} pesama.";
                    Log($"=== Obrada kompletirana! Svi fajlovi su sačuvani u: {_outputFolder} ===");
                    BtnOpenFolder.IsEnabled = true;

                    MessageBox.Show(
                        $"Uspešno je obrađeno {processedCount} audio fajlova!\n\nFolder sa isečenim pesmama:\n{_outputFolder}",
                        "Obrada završena",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (OperationCanceledException)
            {
                Log("Obrada otkazana.");
                TxtCurrentAction.Text = "Obrada otkazana.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Neočekivana greška tokom obrade: {ex.Message}", "Greška", MessageBoxButton.OK, MessageBoxImage.Error);
                Log($"Greška: {ex}");
            }
            finally
            {
                BtnStart.IsEnabled = true;
                BtnCancel.IsEnabled = false;
                BtnSelectFolder.IsEnabled = true;
                if (Directory.Exists(_outputFolder))
                {
                    BtnOpenFolder.IsEnabled = true;
                }
                _cts = null;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                BtnCancel.IsEnabled = false;
                Log("Zatražen prekid obrade...");
            }
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_outputFolder) && Directory.Exists(_outputFolder))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _outputFolder,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ne mogu otvoriti folder: {ex.Message}", "Greška", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}