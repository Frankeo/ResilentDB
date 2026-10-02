using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Engine.BufferPool;
using Engine;

const int dataPageCount = 16384;
const int readOperations = 20000;
const int writeOperations = 2000;
const int cacheCapacity = 256;
const int repetitions = 5;

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

    Console.WriteLine(JsonSerializer.Serialize(result));
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
    var modes = Enum.GetValues<CacheMode>();
    var results = modes.ToDictionary(mode => mode, _ => new List<BenchmarkResult>());

    Console.WriteLine("Buffer pool memory benchmark");
    Console.WriteLine($"Data pages: {dataPageCount} ({dataPageCount * Constants.DefaultPageSize / 1024 / 1024} MiB)");
    Console.WriteLine($"Reads: {readOperations:N0}; writes: {writeOperations:N0}; cache capacity: {cacheCapacity} pages");
    Console.WriteLine($"Independent process runs per mode: {repetitions}; output uses medians");
    Console.WriteLine();

    for (var repetition = 0; repetition < repetitions; repetition++)
    {
        for (var modeOffset = 0; modeOffset < modes.Length; modeOffset++)
        {
            var mode = modes[(modeOffset + repetition) % modes.Length];
            var databasePath = Path.Combine(temporaryDirectory, $"{mode}.mdb");
            File.Copy(seedPath, databasePath, overwrite: true);
            results[mode].Add(await RunWorkerAsync(
                mode,
                databasePath,
                dataPageCount,
                readOperations,
                writeOperations,
                cacheCapacity));
        }
    }

    var checksums = results.Values.SelectMany(runResults => runResults)
        .Select(result => result.Checksum)
        .Distinct()
        .ToArray();
    if (checksums.Length != 1)
        throw new InvalidOperationException("Los modos no produjeron el mismo checksum");

    foreach (var mode in modes)
    {
        PrintMedianResult(mode, results[mode]);
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

    using var process = Process.GetCurrentProcess();
    process.Refresh();
    var managedBeforeInitialization = GC.GetTotalMemory(forceFullCollection: true);
    var workingSetBeforeInitialization = process.WorkingSet64;
    var initializationTimer = Stopwatch.StartNew();
    using IBufferPool bufferPool = mode switch
    {
        CacheMode.Direct => new DirectBufferPool(filePath),
        CacheMode.FullCache => new FullCacheBufferPool(filePath),
        CacheMode.LruCache => new LruBufferPool(filePath, cacheCapacity),
        CacheMode.Clock => new ClockBufferPool(filePath, cacheCapacity),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
    initializationTimer.Stop();

    process.Refresh();
    var managedAfterInitialization = GC.GetTotalMemory(forceFullCollection: false);
    var workingSetAfterInitialization = process.WorkingSet64;
    var managedPeak = managedAfterInitialization;
    var workingSetPeak = workingSetAfterInitialization;
    var workloadTimer = Stopwatch.StartNew();
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
    workloadTimer.Stop();

    var cachedPages = bufferPool switch
    {
        FullCacheBufferPool fullCache => fullCache.CachedPageCount,
        LruBufferPool lruCache => lruCache.CachedPageCount,
        ClockBufferPool clock => clock.CachedPageCount,
        _ => 0
    };

    return new BenchmarkResult(
        initializationTimer.Elapsed.TotalMilliseconds,
        workloadTimer.Elapsed.TotalMilliseconds,
        managedBeforeInitialization,
        managedAfterInitialization,
        managedPeak,
        workingSetBeforeInitialization,
        workingSetAfterInitialization,
        workingSetPeak,
        cachedPages,
        checksum);
}

static async Task<BenchmarkResult> RunWorkerAsync(
    CacheMode mode,
    string databasePath,
    int dataPageCount,
    int readOperations,
    int writeOperations,
    int cacheCapacity)
{
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

    return JsonSerializer.Deserialize<BenchmarkResult>(output)
        ?? throw new InvalidOperationException($"Benchmark {mode} no devolvió resultados");
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

static void PrintMedianResult(CacheMode mode, List<BenchmarkResult> results)
{
    var initializationMs = Median(results.Select(result => result.InitializationMilliseconds));
    var workloadMs = Median(results.Select(result => result.WorkloadMilliseconds));
    var managedInitializationDelta = Median(results.Select(result =>
        (double)(result.ManagedAfterInitialization - result.ManagedBeforeInitialization)));
    var workingSetInitializationDelta = Median(results.Select(result =>
        (double)(result.WorkingSetAfterInitialization - result.WorkingSetBeforeInitialization)));
    var workingSetPeak = Median(results.Select(result => (double)result.WorkingSetPeak));
    var cachedPages = Median(results.Select(result => (double)result.CachedPages));

    Console.WriteLine($"{mode,-10} init median {initializationMs,8:N1} ms | " +
        $"workload median {workloadMs,8:N1} ms | " +
        $"init heap delta {ToMiB(managedInitializationDelta),7:N2} MiB | " +
        $"init RSS delta {ToMiB(workingSetInitializationDelta),7:N2} MiB | " +
        $"peak RSS median {ToMiB(workingSetPeak),7:N2} MiB | " +
        $"cached pages {cachedPages,6:N0} | checksum {results[0].Checksum}");
}
static double ToMiB(double bytes) => bytes / (1024d * 1024d);

static double Median(IEnumerable<double> values)
{
    var ordered = values.Order().ToArray();
    var middle = ordered.Length / 2;
    return ordered.Length % 2 == 0
        ? (ordered[middle - 1] + ordered[middle]) / 2
        : ordered[middle];
}

internal enum CacheMode
{
    Direct,
    FullCache,
    LruCache,
    Clock
}

internal sealed record BenchmarkResult(
    double InitializationMilliseconds,
    double WorkloadMilliseconds,
    long ManagedBeforeInitialization,
    long ManagedAfterInitialization,
    long ManagedPeak,
    long WorkingSetBeforeInitialization,
    long WorkingSetAfterInitialization,
    long WorkingSetPeak,
    int CachedPages,
    long Checksum);
