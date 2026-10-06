# Benchmark de buffer pool

## Objetivo

Compara el coste de memoria y el tiempo de distintas políticas de acceso a páginas, aislando el `IBufferPool` del parser, los ejecutores y el B+Tree. No es un benchmark de consultas SQL ni una medición de rendimiento de producción.

## Ejecutar

```sh
dotnet run --project Benchmarks/Benchmarks.csproj
```

El programa crea una base temporal de 16 384 páginas de datos (64 MiB con páginas de 4096 bytes), realiza 20 000 lecturas pseudoaleatorias y 2 000 escrituras de página, y compara:

- `Direct`: no conserva un frame reutilizable después de liberar el pin; accede mediante Pager.
- `FullCache`: carga todas las páginas en memoria y escribe las dirty al hacer flush.
- `LruCache`: mantiene un máximo de 256 páginas con reemplazo LRU.
- `Clock`: mantiene 256 páginas con la política Clock de segunda oportunidad.

Cada modo corre en un proceso separado. Se reportan duración, heap administrado, working set al inicio y pico, tamaño de cache y checksum; el checksum debe coincidir entre modos. Inicialización y carga se miden por separado. Cada modo se ejecuta cinco veces en orden rotado y se muestran medianas para reducir variación y sesgo por orden.

## Interpretación

`Direct` no limpia ni evita la cache de archivos del sistema operativo; los resultados incluyen el comportamiento normal del filesystem del host. La carga sintética opera directamente sobre páginas para comparar estrategias de cache. Una prueba SQL incluiría además parsing, ejecución, serialización, árbol y catálogo, así que no permitiría atribuir el coste únicamente al buffer pool.