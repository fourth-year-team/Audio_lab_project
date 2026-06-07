using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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

        public Axis[] RatioXAxes { get; set; }
        public Axis[] RatioYAxes { get; set; }
        public Axis[] SpeedXAxes { get; set; }
        public Axis[] SpeedYAxes { get; set; }

        private byte[]? _lastCompressedData;
        private float[]? _lastCompressionInput;

        [ObservableProperty]
        private bool _hasCompressedData;

        [ObservableProperty]
        private bool _isDecompressed;

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

            var glassPurple = SKColor.Parse("#A855F7");
            var glassCyan = SKColor.Parse("#22D3EE");

            RatioXAxes = new Axis[]
            {
                new Axis
                {
                    LabelsPaint = new SolidColorPaint(SKColors.White.WithAlpha(80)),
                    SeparatorsPaint = new SolidColorPaint(SKColors.White.WithAlpha(12))
                }
            };

            RatioYAxes = new Axis[]
            {
                new Axis
                {
                    LabelsPaint = new SolidColorPaint(SKColors.White.WithAlpha(80)),
                    SeparatorsPaint = new SolidColorPaint(SKColors.White.WithAlpha(12))
                }
            };

            SpeedXAxes = new Axis[]
            {
                new Axis
                {
                    LabelsPaint = new SolidColorPaint(SKColors.White.WithAlpha(80)),
                    SeparatorsPaint = new SolidColorPaint(SKColors.White.WithAlpha(12))
                }
            };

            SpeedYAxes = new Axis[]
            {
                new Axis
                {
                    LabelsPaint = new SolidColorPaint(SKColors.White.WithAlpha(80)),
                    SeparatorsPaint = new SolidColorPaint(SKColors.White.WithAlpha(12))
                }
            };

            CompressionRatioSeries = new ISeries[]
            {
                new LineSeries<double>
                {
                    Values = _ratioValues,
                    Name = "Compression Ratio (%)",
                    Fill = new LinearGradientPaint(
                        new SKColor[] { glassPurple.WithAlpha(50), glassPurple.WithAlpha(5) },
                        new SKPoint(0, 0.5f),
                        new SKPoint(0, 1)),
                    Stroke = new SolidColorPaint(glassPurple, 2),
                    GeometrySize = 9,
                    GeometryStroke = new SolidColorPaint(glassPurple, 3),
                    GeometryFill = new SolidColorPaint(SKColor.Parse("#1A1A2E")),
                    LineSmoothness = 0.3
                }
            };

            ProcessingSpeedSeries = new ISeries[]
            {
                new LineSeries<double>
                {
                    Values = _speedValues,
                    Name = "Speed (Samples/ms)",
                    Fill = new LinearGradientPaint(
                        new SKColor[] { glassCyan.WithAlpha(50), glassCyan.WithAlpha(5) },
                        new SKPoint(0, 0.5f),
                        new SKPoint(0, 1)),
                    Stroke = new SolidColorPaint(glassCyan, 2),
                    GeometrySize = 9,
                    GeometryStroke = new SolidColorPaint(glassCyan, 3),
                    GeometryFill = new SolidColorPaint(SKColor.Parse("#1A1A2E")),
                    LineSmoothness = 0.3
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

                _lastCompressedData = await Task.Run(() => SelectedAlgorithm.Compress(samplesToCompress, progress, token), token);
                _lastCompressionInput = samplesToCompress;
                ProcessedSamples = null;

                stopwatch.Stop();

                LastResult = new CompressionResult
                {
                    AlgorithmName = SelectedAlgorithm.Name,
                    OriginalSize = OriginalSamples.Length * 4,
                    CompressedSize = _lastCompressedData.Length,
                    ProcessingTime = stopwatch.Elapsed,
                    Parameters = GetAlgorithmParameters(SelectedAlgorithm) + $", Rate={TargetSampleRate}, Bits={QuantizationBits}",
                    Snr = -1
                };

                _ratioValues.Add(LastResult.SavingsPercentage);
                HasCompressedData = true;
                StatusMessage = "Compressed. Press DECOMPRESS to restore audio.";
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
            _lastCompressedData = null;
            _lastCompressionInput = null;
            HasCompressedData = false;
            IsDecompressed = false;
            CompressionProgress = 0;
            QuantizationBits = 16;
            if (Metadata != null) TargetSampleRate = Metadata.SampleRate;
            _ratioValues.Clear();
            _speedValues.Clear();
            LastResult = new CompressionResult();
            StatusMessage = "Ready";
            SelectedAlgorithm = Algorithms.First();
            foreach (var algo in _algorithms)
            {
                if (algo is DeltaModulationAlgorithm dm) dm.StepSize = 0.01f;
                else if (algo is AdmAlgorithm adm) { adm.MinStep = 0.001f; adm.MaxStep = 0.1f; adm.Multiplier = 1.5f; }
                else if (algo is NonlinearQuantizationAlgorithm nl) nl.Mu = 255.0;
            }
        }

        [RelayCommand]
        public void Decompress()
        {
            if (_lastCompressedData == null) return;
            try
            {
                StatusMessage = "Decompressing...";
                float[] decompressed = SelectedAlgorithm.Decompress(_lastCompressedData);
                ProcessedSamples = decompressed;
                IsDecompressed = true;

                double snr = -1;
                if (_lastCompressionInput != null && decompressed.Length == _lastCompressionInput.Length)
                    snr = ComputeSnr(_lastCompressionInput, decompressed);

                LastResult = new CompressionResult
                {
                    AlgorithmName = LastResult.AlgorithmName,
                    OriginalSize = LastResult.OriginalSize,
                    CompressedSize = LastResult.CompressedSize,
                    ProcessingTime = LastResult.ProcessingTime,
                    Parameters = LastResult.Parameters,
                    Snr = snr
                };
                StatusMessage = "Decompression complete. Ready for playback.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Decompress error: {ex.Message}";
            }
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
            _audioService.SaveWav(filePath, ProcessedSamples, TargetSampleRate, Metadata.Channels);
            StatusMessage = $"Saved to {filePath}";
        }
    }
}
