using System;

namespace AudioLabProject.Algorithms
{
    public class NonlinearQuantizationAlgorithm : ICompressionAlgorithm
    {
        public string Name => "Nonlinear Quantization (Mu-law)";
        public double Mu { get; set; } = 255.0;

        public byte[] Compress(float[] samples, IProgress<double>? progress = null, System.Threading.CancellationToken ct = default)
        {
            if (samples == null || samples.Length == 0) return Array.Empty<byte>();

            byte[] result = new byte[samples.Length];

            for (int i = 0; i < samples.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                double x = samples[i];
                double compressed = Math.Sign(x) * Math.Log(1 + Mu * Math.Abs(x)) / Math.Log(1 + Mu);
                double mapped = (compressed + 1.0) / 2.0 * 255.0;
                result[i] = (byte)Math.Clamp(Math.Round(mapped), 0, 255);
                if (i % 1000 == 0)
                    progress?.Report((double)i / samples.Length * 100.0);
            }

            progress?.Report(100.0);
            return result;
        }

        public float[] Decompress(byte[] compressedData)
        {
            float[] result = new float[compressedData.Length];
            double logMu = Math.Log(1 + Mu);

            for (int i = 0; i < compressedData.Length; i++)
            {
                double y = (compressedData[i] / 255.0 * 2.0) - 1.0;
                double decompressed = Math.Sign(y) * (1.0 / Mu) * (Math.Pow(1.0 + Mu, Math.Abs(y)) - 1.0);
                result[i] = (float)decompressed;
            }

            return result;
        }
    }
}
