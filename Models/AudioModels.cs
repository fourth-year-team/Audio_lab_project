using System;

namespace AudioLabProject.Models
{
    public class AudioMetadata
    {
        public string FileName { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public string DurationDisplay => Duration.TotalHours >= 1
            ? Duration.ToString(@"hh\:mm\:ss")
            : Duration.ToString(@"mm\:ss");
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public int BitRate { get; set; }
        public string Encoding { get; set; } = string.Empty;
        public long FileSize { get; set; }
    }

    public class CompressionResult
    {
        public string AlgorithmName { get; set; } = string.Empty;
        public long OriginalSize { get; set; }
        public long CompressedSize { get; set; }
        public double SavingsPercentage => (1.0 - (double)CompressedSize / OriginalSize) * 100.0;
        public TimeSpan ProcessingTime { get; set; }
        public string ProcessingTimeDisplay => ProcessingTime.TotalHours >= 1
            ? ProcessingTime.ToString(@"hh\:mm\:ss\.fff")
            : ProcessingTime.ToString(@"mm\:ss\.fff");
        public string Parameters { get; set; } = string.Empty;
        public double Snr { get; set; }
        public string SnrDisplay => Snr >= 0 ? $"{Snr:F2} dB" : "N/A";
    }
}
