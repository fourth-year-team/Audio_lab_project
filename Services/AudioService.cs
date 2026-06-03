using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;
using AudioLabProject.Models;

namespace AudioLabProject.Services
{
    public interface IAudioService : IDisposable
    {
        Task<float[]> LoadSamplesAsync(string filePath);
        AudioMetadata GetMetadata(string filePath);
        void Play(float[] samples, int sampleRate, int channels);
        void Stop();
        void SaveWav(string filePath, float[] samples, int sampleRate, int channels);
        float[] Resample(float[] samples, int originalRate, int targetRate, int channels);
        float[] ReduceBitDepth(float[] samples, int bits);
    }

    public class AudioService : IAudioService
    {
        private IWavePlayer? _wavePlayer;

        public async Task<float[]> LoadSamplesAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                using var reader = new AudioFileReader(filePath);
                var samples = new float[reader.Length / (reader.WaveFormat.BitsPerSample / 8)];
                int read = reader.Read(samples, 0, samples.Length);
                return samples.Take(read).ToArray();
            });
        }

        public AudioMetadata GetMetadata(string filePath)
        {
            using var reader = new AudioFileReader(filePath);
            var info = new FileInfo(filePath);
            return new AudioMetadata
            {
                FileName = Path.GetFileName(filePath),
                Duration = reader.TotalTime,
                SampleRate = reader.WaveFormat.SampleRate,
                Channels = reader.WaveFormat.Channels,
                BitRate = reader.WaveFormat.AverageBytesPerSecond * 8,
                Encoding = reader.WaveFormat.Encoding.ToString(),
                FileSize = info.Length
            };
        }

        public void Play(float[] samples, int sampleRate, int channels)
        {
            Stop();
            var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
            var ms = new MemoryStream();
            var writer = new WaveFileWriter(new IgnoreDisposeStream(ms), waveFormat);
            writer.WriteSamples(samples, 0, samples.Length);
            writer.Flush();
            ms.Position = 0;
            var reader = new WaveFileReader(ms);
            _wavePlayer = new WaveOutEvent();
            _wavePlayer.Init(reader);
            _wavePlayer.Play();
        }

        public void Stop()
        {
            if (_wavePlayer != null)
            {
                _wavePlayer.Stop();
                _wavePlayer.Dispose();
                _wavePlayer = null;
            }
        }

        public void SaveWav(string filePath, float[] samples, int sampleRate, int channels)
        {
            var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
            using var writer = new WaveFileWriter(filePath, waveFormat);
            writer.WriteSamples(samples, 0, samples.Length);
        }

        public float[] Resample(float[] samples, int originalRate, int targetRate, int channels)
        {
            if (originalRate == targetRate) return samples;
            var outSamples = new List<float>();
            var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(originalRate, channels);
            var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(targetRate, channels);

            using (var ms = new MemoryStream())
            {
                using (var writer = new WaveFileWriter(new IgnoreDisposeStream(ms), waveFormat))
                {
                    writer.WriteSamples(samples, 0, samples.Length);
                    writer.Flush();
                }
                ms.Position = 0;
                using (var reader = new WaveFileReader(ms))
                {
                    using (var resampler = new MediaFoundationResampler(reader, targetFormat))
                    {
                        resampler.ResamplerQuality = 60;
                        var buffer = new float[samples.Length];
                        int read;
                        while ((read = resampler.ToSampleProvider().Read(buffer, 0, buffer.Length)) > 0)
                            outSamples.AddRange(buffer.Take(read));
                    }
                }
            }
            return outSamples.ToArray();
        }

        public float[] ReduceBitDepth(float[] samples, int bits)
        {
            if (bits >= 32) return samples;
            double levels = Math.Pow(2, bits);
            float[] result = new float[samples.Length];

            for (int i = 0; i < samples.Length; i++)
            {
                double normalized = (samples[i] + 1.0) / 2.0;
                double quantized = Math.Round(normalized * (levels - 1)) / (levels - 1);
                result[i] = (float)(quantized * 2.0 - 1.0);
            }

            return result;
        }

        public void Dispose() => Stop();
    }

    public class IgnoreDisposeStream : Stream
    {
        private readonly Stream _innerStream;
        public IgnoreDisposeStream(Stream innerStream) => _innerStream = innerStream;
        public override bool CanRead => _innerStream.CanRead;
        public override bool CanSeek => _innerStream.CanSeek;
        public override bool CanWrite => _innerStream.CanWrite;
        public override long Length => _innerStream.Length;
        public override long Position { get => _innerStream.Position; set => _innerStream.Position = value; }
        public override void Flush() => _innerStream.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _innerStream.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);
        public override void SetLength(long value) => _innerStream.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _innerStream.Write(buffer, offset, count);
        protected override void Dispose(bool disposing) { }
    }
}
