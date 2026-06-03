using System;
using System.Collections;

namespace AudioLabProject.Algorithms
{
    public class AdmAlgorithm : ICompressionAlgorithm
    {
        public string Name => "Adaptive Delta Modulation";
        public float MinStep { get; set; } = 0.001f;
        public float MaxStep { get; set; } = 0.1f;
        public float Multiplier { get; set; } = 1.5f;
        public int OriginalSampleCount { get; private set; }

        public byte[] Compress(float[] samples, IProgress<double>? progress = null, System.Threading.CancellationToken ct = default)
        {
            if (samples == null || samples.Length == 0) return Array.Empty<byte>();

            OriginalSampleCount = samples.Length;
            BitArray bits = new BitArray(samples.Length);
            float currentEstimate = 0;
            float currentStep = MinStep;
            bool lastBit = false;

            for (int i = 0; i < samples.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                bool currentBit = samples[i] > currentEstimate;
                bits.Set(i, currentBit);
                if (currentBit == lastBit)
                    currentStep = Math.Min(currentStep * Multiplier, MaxStep);
                else
                    currentStep = Math.Max(currentStep / Multiplier, MinStep);
                if (currentBit)
                    currentEstimate += currentStep;
                else
                    currentEstimate -= currentStep;
                lastBit = currentBit;
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
            float currentStep = MinStep;
            bool lastBit = false;

            for (int i = 0; i < bitCount; i++)
            {
                bool currentBit = bits.Get(i);
                if (currentBit == lastBit)
                    currentStep = Math.Min(currentStep * Multiplier, MaxStep);
                else
                    currentStep = Math.Max(currentStep / Multiplier, MinStep);
                if (currentBit)
                    currentEstimate += currentStep;
                else
                    currentEstimate -= currentStep;
                result[i] = Math.Clamp(currentEstimate, -1.0f, 1.0f);
                lastBit = currentBit;
            }

            return result;
        }
    }
}
