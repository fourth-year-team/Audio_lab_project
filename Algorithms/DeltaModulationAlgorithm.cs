using System;
using System.Collections;

namespace AudioLabProject.Algorithms
{
    public class DeltaModulationAlgorithm : ICompressionAlgorithm
    {
        public string Name => "Delta Modulation";
        public float StepSize { get; set; } = 0.01f;
        public int OriginalSampleCount { get; private set; }

        public byte[] Compress(float[] samples, IProgress<double>? progress = null, System.Threading.CancellationToken ct = default)
        {
            if (samples == null || samples.Length == 0) return Array.Empty<byte>();

            OriginalSampleCount = samples.Length;
            BitArray bits = new BitArray(samples.Length);
            float currentEstimate = 0;

            for (int i = 0; i < samples.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (samples[i] > currentEstimate)
                {
                    bits.Set(i, true);
                    currentEstimate += StepSize;
                }
                else
                {
                    bits.Set(i, false);
                    currentEstimate -= StepSize;
                }
                if (i % 1000 == 0)
                    progress?.Report((double)i / samples.Length * 100.0);
            }

            byte[] result = new byte[(bits.Length + 7) / 8];
            bits.CopyTo(result, 0);
            progress?.Report(100.0);
            return result;
        }

        public float[] Decompress(byte[] compressedData)
        {
            int bitCount = OriginalSampleCount > 0 ? OriginalSampleCount : compressedData.Length * 8;
            BitArray bits = new BitArray(compressedData);
            float[] result = new float[bitCount];
            float currentEstimate = 0;

            for (int i = 0; i < bitCount; i++)
            {
                if (bits.Get(i))
                    currentEstimate += StepSize;
                else
                    currentEstimate -= StepSize;
                result[i] = Math.Clamp(currentEstimate, -1.0f, 1.0f);
            }

            return result;
        }
    }
}
