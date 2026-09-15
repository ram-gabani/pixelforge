using BenchmarkDotNet.Running;

namespace PixelForge.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<ImageProcessingBenchmarks>(args: args);
    }
}

