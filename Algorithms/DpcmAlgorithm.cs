using System;

namespace AudioLabProject.Algorithms
{
    public class DpcmAlgorithm : ICompressionAlgorithm
    {
        public string Name => "DPCM";

        public byte[] Compress(float[] samples, IProgress<double>? progress = null, System.Threading.CancellationToken ct = default)
        {
            if (samples == null || samples.Length == 0) return Array.Empty<byte>();

            byte[] result = new byte[samples.Length * 2];
            short lastValue = 0;

            for (int i = 0; i < samples.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                short currentValue = (short)(samples[i] * short.MaxValue);
                int rawDiff = currentValue - lastValue;
                if (rawDiff > short.MaxValue) rawDiff = short.MaxValue;
                if (rawDiff < short.MinValue) rawDiff = short.MinValue;
                short difference = (short)rawDiff;
                byte[] bytes = BitConverter.GetBytes(difference);
                result[i * 2] = bytes[0];
                result[i * 2 + 1] = bytes[1];
                lastValue = currentValue;
                if (i % 1000 == 0)
                    progress?.Report((double)i / samples.Length * 100.0);
            }

            progress?.Report(100.0);
            return result;
        }

        public float[] Decompress(byte[] compressedData)
        {
            int sampleCount = compressedData.Length / 2;
            float[] result = new float[sampleCount];
            short lastValue = 0;

            for (int i = 0; i < sampleCount; i++)
            {
                short difference = BitConverter.ToInt16(compressedData, i * 2);
                short currentValue = (short)(lastValue + difference);
                result[i] = (float)currentValue / short.MaxValue;
                lastValue = currentValue;
            }

            return result;
        }
    }
}
