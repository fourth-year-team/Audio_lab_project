using System;

namespace AudioLabProject.Algorithms
{
    public interface ICompressionAlgorithm
    {
        string Name { get; }
        byte[] Compress(float[] samples, IProgress<double>? progress = null, System.Threading.CancellationToken ct = default);
        float[] Decompress(byte[] compressedData);
    }
}
