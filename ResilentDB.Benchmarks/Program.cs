using System.Diagnostics;
using System.Reflection;
using Engine.BufferPool;
using Engine;

const int dataPageCount = 16384;
const int readOperations = 20000;
const int writeOperations = 2000;
const int cacheCapacity = 256;

if (args.Length > 0 && args[0] == "--worker")
{
    var mode = Enum.Parse<CacheMode>(args[1]);
    var result = RunWorkload(
        mode,
        args[2],
        int.Parse(args[3]),
        int.Parse(args[4]),
        int.Parse(args[5]),
        int.Parse(args[6]));

    PrintResult(mode, result);
    return;
}

var temporaryDirectory = Path.Combine(
    Path.GetTempPath(),
    $"ResilentDB.PageBenchmark.{Guid.NewGuid():N}");
Directory.CreateDirectory(temporaryDirectory);

try
{
    var seedPath = Path.Combine(temporaryDirectory, "seed.mdb");
    CreateSeedDatabase(seedPath, dataPageCount);

    Console.WriteLine("Buffer pool memory benchmark");
    Console.WriteLine($"Data pages: {dataPageCount} ({dataPageCount * Constants.DefaultPageSize / 1024 / 1024} MiB)");
    Console.WriteLine($"Reads: {readOperations:N0}; writes: {writeOperations:N0}; cache capacity: {cacheCapacity} pages");
    Console.WriteLine();

    foreach (var mode in Enum.GetValues<CacheMode>())
    {
        var databasePath = Path.Combine(temporaryDirectory, $"{mode}.mdb");
        File.Copy(seedPath, databasePath);
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
        startInfo.ArgumentList.Add("--worker");
        startInfo.ArgumentList.Add(mode.ToString());
        startInfo.ArgumentList.Add(databasePath);
        startInfo.ArgumentList.Add(dataPageCount.ToString());
        startInfo.ArgumentList.Add(readOperations.ToString());
        startInfo.ArgumentList.Add(writeOperations.ToString());
        startInfo.ArgumentList.Add(cacheCapacity.ToString());

        using var worker = Process.Start(startInfo)
            ?? throw new InvalidOperationException("No se pudo iniciar el proceso del benchmark");
        var outputTask = worker.StandardOutput.ReadToEndAsync();
        var errorTask = worker.StandardError.ReadToEndAsync();
        await worker.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;

        if (worker.ExitCode != 0)
            throw new InvalidOperationException($"Benchmark {mode} falló: {error}");

        Console.Write(output);
    }
}
finally
{
    Directory.Delete(temporaryDirectory, recursive: true);
}

static void CreateSeedDatabase(string filePath, int dataPageCount)
{
    using var pager = new Pager(filePath);
    for (var index = 0; index < dataPageCount; index++)
    {
        var pageId = pager.AllocatePage();
        var page = new byte[pager.PageSize];
        Array.Fill(page, (byte)(pageId % 251));
        BitConverter.TryWriteBytes(page, pageId);
        pager.WritePage(pageId, page);
    }
}

static BenchmarkResult RunWorkload(
    CacheMode mode,
    string filePath,
    int dataPageCount,
    int readOperations,
    int writeOperations,
    int cacheCapacity)
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    using IBufferPool bufferPool = mode switch
    {
        CacheMode.Direct => new DirectBufferPool(filePath),
        CacheMode.FullCache => new FullCacheBufferPool(filePath),
        CacheMode.LruCache => new LruBufferPool(filePath, cacheCapacity),
        CacheMode.Clock => new ClockBufferPool(filePath, cacheCapacity),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    using var process = Process.GetCurrentProcess();
    process.Refresh();
    var managedStart = GC.GetTotalMemory(forceFullCollection: true);
    var workingSetStart = process.WorkingSet64;
    var managedPeak = managedStart;
    var workingSetPeak = workingSetStart;
    var stopwatch = Stopwatch.StartNew();
    var random = new Random(42);
    long checksum = 0;

    for (var operation = 0; operation < readOperations; operation++)
    {
        var pageId = ChoosePage(random, dataPageCount);
        var page = bufferPool.ReadPage(pageId);
        checksum = unchecked(checksum * 31 + page[random.Next(page.Length)]);
        if (operation % 128 == 0)
            SampleMemory(process, ref managedPeak, ref workingSetPeak);
    }

    for (var operation = 0; operation < writeOperations; operation++)
    {
        var pageId = ChoosePage(random, dataPageCount);
        var page = bufferPool.ReadPage(pageId);
        page[sizeof(int)] = (byte)(page[sizeof(int)] + 1);
        bufferPool.WritePage(pageId, page);
        if (operation % 64 == 0)
            SampleMemory(process, ref managedPeak, ref workingSetPeak);
    }

    bufferPool.Flush();
    SampleMemory(process, ref managedPeak, ref workingSetPeak);
    stopwatch.Stop();

    var cachedPages = bufferPool switch
    {
        FullCacheBufferPool fullCache => fullCache.CachedPageCount,
        LruBufferPool lruCache => lruCache.CachedPageCount,
        ClockBufferPool clock => clock.CachedPageCount,
        _ => 0
    };

    return new BenchmarkResult(
        stopwatch.Elapsed,
        managedStart,
        managedPeak,
        workingSetStart,
        workingSetPeak,
        cachedPages,
        checksum);
}

static int ChoosePage(Random random, int dataPageCount) =>
    random.Next(100) < 80
        ? random.Next(1, Math.Min(dataPageCount, 256) + 1)
        : random.Next(1, dataPageCount + 1);

static void SampleMemory(Process process, ref long managedPeak, ref long workingSetPeak)
{
    managedPeak = Math.Max(managedPeak, GC.GetTotalMemory(forceFullCollection: false));
    process.Refresh();
    workingSetPeak = Math.Max(workingSetPeak, process.WorkingSet64);
}

static double ToMiB(long bytes) => bytes / (1024d * 1024d);

static void PrintResult(CacheMode mode, BenchmarkResult result)
{
    Console.WriteLine($"{mode,-10} elapsed {result.Elapsed.TotalMilliseconds,9:N0} ms | " +
        $"managed start {ToMiB(result.ManagedStart),7:N2} MiB | " +
        $"managed peak {ToMiB(result.ManagedPeak),7:N2} MiB | " +
        $"working set start {ToMiB(result.WorkingSetStart),7:N2} MiB | " +
        $"working set peak {ToMiB(result.WorkingSetPeak),7:N2} MiB | " +
        $"cached pages {result.CachedPages,5:N0} | checksum {result.Checksum}");
}

internal enum CacheMode
{
    Direct,
    FullCache,
    LruCache,
    Clock
}

internal sealed record BenchmarkResult(
    TimeSpan Elapsed,
    long ManagedStart,
    long ManagedPeak,
    long WorkingSetStart,
    long WorkingSetPeak,
    int CachedPages,
    long Checksum);
