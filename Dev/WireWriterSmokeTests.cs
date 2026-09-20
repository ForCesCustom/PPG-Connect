using System;
using System.Diagnostics;
using PPGTogether.BepInEx;

internal static class WireWriterSmokeTests
{
    private static void Main()
    {
        float[] values = { 0f, -0f, 1f, -1f, float.Epsilon, float.MaxValue, float.MinValue, float.PositiveInfinity, float.NegativeInfinity, float.NaN, 123.456f };
        int checks = 0;
        Random random = new Random(4919);
        for (int i = 0; i < 10011; i++)
        {
            float value = i < values.Length ? values[i] : (float)((random.NextDouble() - .5) * 1e20);
            Writer writer = new Writer(4); writer.Float(value);
            byte[] actual = writer.ToArray(), expected = BitConverter.GetBytes(value);
            if (!BitConverter.IsLittleEndian) Array.Reverse(expected);
            for (int j = 0; j < 4; j++) if (actual[j] != expected[j]) throw new Exception("IEEE byte parity at " + i);
            checks++;
        }
        Benchmark(false); Benchmark(true); // Warm both paths before measurement.
        Console.WriteLine("Writer IEEE-byte checks passed: " + checks);
        Benchmark(false); Benchmark(true);
    }

    private static void Benchmark(bool optimized)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        int collections = GC.CollectionCount(0);
        Stopwatch timer = Stopwatch.StartNew();
        int total = 0;
        for (int packet = 0; packet < 20000; packet++)
        {
            Writer writer = new Writer(256);
            for (int i = 0; i < 64; i++)
                if (optimized) writer.Float(i * .125f); else writer.Raw(BitConverter.GetBytes(i * .125f));
            total += writer.ToArray().Length;
        }
        timer.Stop();
        if (total != 5120000) throw new Exception("Benchmark output length changed");
        Console.WriteLine((optimized ? "Stack-bit writer" : "Old byte-array writer") + ": " + timer.ElapsedMilliseconds + "ms, Gen0 collections=" + (GC.CollectionCount(0) - collections) + ", bytes=" + total);
    }
}
