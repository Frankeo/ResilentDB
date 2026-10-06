# UnitTests

## Propósito

Suite xUnit para aprender y comprobar invariantes por capa, desde texto SQL hasta páginas persistidas. Cada test deriva de `EngineTestBase`, que crea un archivo temporal aislado y limpia el archivo principal al terminar; los tests de índices también limpian sus archivos auxiliares.

## Organización

- `ParserTests`: reconocimiento de AST, clave primaria y valores con comas entre comillas.
- `EngineExecutionTests`: CRUD, filtros, resultados tipados, errores y ciclo de vida de `DbEngine`.
- `BPlusTreeTests`: splits, scans, persistencia, overflow, rebalanceo, free list y comparación aleatoria con `SortedDictionary`.
- `BufferPoolTests`: Pager, eviction LRU/Clock, FullCache, dirty pages, pins, PageHandle y acceso concurrente.
- `StorageTests`: persistencia de payloads que ocupan varias páginas y superan la capacidad del buffer pool.
- `PrimaryIndexIntegrationTests`: CRUD de clave primaria atravesando parser, ejecutores, Storage y B+Tree.

La semilla fija del test aleatorio permite reproducir fallos. Las pruebas comparan comportamiento observable con una estructura de referencia, en vez de verificar solo detalles internos.

## Ejecutar

```sh
dotnet test UnitTests/UnitTests.csproj
```

El proyecto usa xUnit 2 y versiones actuales del SDK de test para mantener una cadena de dependencias sin el Newtonsoft.Json vulnerable que traía el tooling anterior.