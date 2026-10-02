# Buffer Pool Benchmark

Run the comparison with:

```sh
dotnet run --project ResilentDB.Benchmarks/ResilentDB.Benchmarks.csproj
```

The benchmark creates a temporary database with 16,384 data pages (64 MiB), runs
20,000 pseudo-random page reads and 2,000 page updates, then compares:

- `Direct`: each page access goes through `Pager` without an application cache.
- `FullCache`: all data pages are loaded into memory and dirty pages are flushed.
- `LruCache`: pages are cached with an LRU limit of 256 pages.
- `Clock`: pages use the second-chance Clock policy with a 256-page limit.

Each mode runs in its own process. The output reports elapsed time, managed
heap and working-set start/peak measurements, cache size, and a checksum that
should match across modes. The benchmark measures page access, not SQL-row
operations: the current `DbEngine` loads its complete table snapshot in memory,
so comparing SQL workloads would not isolate page-cache behavior.