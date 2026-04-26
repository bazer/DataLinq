using BenchmarkDotNet.Running;

namespace DataLinq.Benchmark;

public static class Program
{
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
