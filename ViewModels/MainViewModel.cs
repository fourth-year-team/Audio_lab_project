using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AudioLabProject.Models;
using AudioLabProject.Services;
using AudioLabProject.Algorithms;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace AudioLabProject.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IAudioService _audioService;
        private readonly List<ICompressionAlgorithm> _algorithms;

        [ObservableProperty]
        private AudioMetadata _metadata = new();

        [ObservableProperty]
        private float[]? _originalSamples;

        [ObservableProperty]
        private float[]? _processedSamples;

        [ObservableProperty]
        private double _compressionProgress;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private ICompressionAlgorithm _selectedAlgorithm;

        [ObservableProperty]
        private CompressionResult _lastResult = new();

        [ObservableProperty]
        private bool _isDraggingOver;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsFileLoaded))]
        private bool _isFileSelected;

        [ObservableProperty]
        private bool _isProcessing;

        [ObservableProperty]
        private int _targetSampleRate;

        [ObservableProperty]
        private int _quantizationBits = 16;

        public List<int> AvailableBitDepths { get; } = new() { 4, 8, 16, 24, 32 };

        private System.Threading.CancellationTokenSource? _compressionCts;

        public bool IsFileLoaded => IsFileSelected;

        public ObservableCollection<ICompressionAlgorithm> Algorithms { get; }

        public ISeries[] CompressionRatioSeries { get; set; }
        public ISeries[] ProcessingSpeedSeries { get; set; }

        private readonly ObservableCollection<double> _ratioValues = new();
        private readonly ObservableCollection<double> _speedValues = new();
        private long _lastProgressTime;
        private int _lastReportedBucket = -1;

        public MainViewModel()
        {
            _audioService = new AudioService();
            _algorithms = new List<ICompressionAlgorithm>
            {
                new DpcmAlgorithm(),
                new DeltaModulationAlgorithm(),
                new AdmAlgorithm(),
                new NonlinearQuantizationAlgorithm()
            };

            Algorithms = new ObservableCollection<ICompressionAlgorithm>(_algorithms);
            SelectedAlgorithm = Algorithms.First();

            // Initialize Charts
            CompressionRatioSeries = new ISeries[]
            {
                new LineSeries<double>
                {
                    Values = _ratioValues,
                    Name = "Compression Ratio (%)",
                    Fill = null
                }
            };

            ProcessingSpeedSeries = new ISeries[]
            {
                new LineSeries<double>
                {
                    Values = _speedValues,
                    Name = "Speed (Samples/ms)",
                    Fill = null
                }
            };
        }

        [RelayCommand]
        public async Task LoadFileAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            try
            {
                StatusMessage = "Loading file...";
                Metadata = _audioService.GetMetadata(filePath);
                OriginalSamples = await _audioService.LoadSamplesAsync(filePath);
                TargetSampleRate = Metadata.SampleRate;

                // Clear previous compression data
                _ratioValues.Clear();
                _speedValues.Clear();
                LastResult = new CompressionResult();
                CompressionProgress = 0;

                IsFileSelected = true;
                StatusMessage = $"Loaded: {Metadata.FileName}";
            }
            catch (Exception ex)
            {
                IsFileSelected = false;
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task BrowseFileAsync()
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Audio Files|*.wav;*.mp3;*.flac;*.wma;*.aac;*.m4a|All Files|*.*",
                Title = "Select Audio File",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                await LoadFileAsync(openFileDialog.FileName);
            }
        }

        [RelayCommand]
        public async Task ProcessCompressionAsync()
        {
            if (OriginalSamples == null || SelectedAlgorithm == null || IsProcessing) return;

            bool cancelled = false;
            int initialSpeedCount = _speedValues.Count;
            try
            {
                IsProcessing = true;
                _compressionCts = new System.Threading.CancellationTokenSource();
                var token = _compressionCts.Token;

                StatusMessage = $"Compressing with {SelectedAlgorithm.Name}...";
                CompressionProgress = 0;
                _lastReportedBucket = -1;
                _lastProgressTime = 0;

                var stopwatch = Stopwatch.StartNew();
                
                // Pre-processing
                float[] samplesToCompress = OriginalSamples;
                if (TargetSampleRate != Metadata.SampleRate)
                {
                    StatusMessage = "Resampling...";
                    samplesToCompress = await Task.Run(() => _audioService.Resample(OriginalSamples, Metadata.SampleRate, TargetSampleRate, Metadata.Channels), token);
                }
                
                if (QuantizationBits < 32)
                {
                    StatusMessage = $"Quantizing to {QuantizationBits} bits...";
                    samplesToCompress = await Task.Run(() => _audioService.ReduceBitDepth(samplesToCompress, QuantizationBits), token);
                }

                var progress = new Progress<double>(p => {
                    CompressionProgress = p;
                    int bucket = (int)(p / 10);
                    if (bucket > _lastReportedBucket)
                    {
                        _lastReportedBucket = bucket;
                        long currentMs = stopwatch.ElapsedMilliseconds;
                        long deltaMs = currentMs - _lastProgressTime;
                        double chunkSamples = samplesToCompress.Length * 0.1;
                        _speedValues.Add(deltaMs > 0 ? chunkSamples / deltaMs : 0);
                        _lastProgressTime = currentMs;
                    }
                });

                byte[] compressed = await Task.Run(() => SelectedAlgorithm.Compress(samplesToCompress, progress, token), token);
                ProcessedSamples = await Task.Run(() => SelectedAlgorithm.Decompress(compressed), token);
                
                stopwatch.Stop();

                // Compute SNR
                double snr = ComputeSnr(samplesToCompress, ProcessedSamples);

                LastResult = new CompressionResult
                {
                    AlgorithmName = SelectedAlgorithm.Name,
                    OriginalSize = OriginalSamples.Length * 4,
                    CompressedSize = compressed.Length,
                    ProcessingTime = stopwatch.Elapsed,
                    Parameters = GetAlgorithmParameters(SelectedAlgorithm) + $", Rate={TargetSampleRate}, Bits={QuantizationBits}",
                    Snr = snr
                };

                _ratioValues.Add(LastResult.SavingsPercentage);
                StatusMessage = "Compression complete.";
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                StatusMessage = "Compression cancelled.";
                CompressionProgress = 0;
            }
            catch (Exception ex)
            {
                cancelled = true;
                StatusMessage = $"Compression Error: {ex.Message}";
            }
            finally
            {
                IsProcessing = false;
                if (cancelled)
                {
                    // Remove speed values added during cancelled run
                    while (_speedValues.Count > initialSpeedCount)
                        _speedValues.RemoveAt(_speedValues.Count - 1);
                }
                _compressionCts?.Dispose();
                _compressionCts = null;
            }
        }

        private double ComputeSnr(float[] original, float[] reconstructed)
        {
            if (original == null || reconstructed == null || original.Length == 0)
                return -1;

            int len = Math.Min(original.Length, reconstructed.Length);
            double signalPower = 0;
            double noisePower = 0;

            for (int i = 0; i < len; i++)
            {
                signalPower += original[i] * original[i];
                double diff = original[i] - reconstructed[i];
                noisePower += diff * diff;
            }

            if (noisePower < 1e-12 || signalPower < 1e-12)
                return -1;

            return 10.0 * Math.Log10(signalPower / noisePower);
        }

        private string GetAlgorithmParameters(ICompressionAlgorithm algorithm)
        {
            if (algorithm is DeltaModulationAlgorithm dm) return $"StepSize={dm.StepSize}";
            if (algorithm is AdmAlgorithm adm) return $"Min={adm.MinStep}, Max={adm.MaxStep}, Mult={adm.Multiplier}";
            if (algorithm is NonlinearQuantizationAlgorithm nl) return $"Mu={nl.Mu}";
            return "Default";
        }

        [RelayCommand]
        public void CancelCompression()
        {
            _compressionCts?.Cancel();
        }

        [RelayCommand]
        public void Reset()
        {
            CancelCompression();
            ProcessedSamples = null;
            CompressionProgress = 0;
            QuantizationBits = 16;
            if (Metadata != null) TargetSampleRate = Metadata.SampleRate;
            _ratioValues.Clear();
            _speedValues.Clear();
            LastResult = new CompressionResult();
            StatusMessage = "Ready";
        }

        [RelayCommand]
        public void PlayOriginal()
        {
            if (OriginalSamples != null) _audioService.Play(OriginalSamples, Metadata.SampleRate, Metadata.Channels);
        }

        [RelayCommand]
        public void PlayProcessed()
        {
            if (ProcessedSamples != null) _audioService.Play(ProcessedSamples, Metadata.SampleRate, Metadata.Channels);
        }

        [RelayCommand]
        public void StopPlayback() => _audioService.Stop();

        [RelayCommand]
        public void SaveProcessed(string filePath)
        {
            if (ProcessedSamples == null) return;
            _audioService.SaveWav(filePath, ProcessedSamples, Metadata.SampleRate, Metadata.Channels);
            StatusMessage = $"Saved to {filePath}";
        }
    }
}
