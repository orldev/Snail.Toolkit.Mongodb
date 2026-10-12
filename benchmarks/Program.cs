using BenchmarkDotNet.Running;

namespace Snail.Toolkit.Mongodb.Benchmarks;

/// <summary>Runs the benchmarks named on the command line, or offers all of them.</summary>
public static class Program
{
    /// <summary>Hands the command line to BenchmarkDotNet.</summary>
    /// <param name="args">BenchmarkDotNet's arguments, such as --filter and --job.</param>
    public static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
