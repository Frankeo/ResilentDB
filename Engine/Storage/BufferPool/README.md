# Engine.Storage.BufferPool

## Responsabilidad

El buffer pool mantiene páginas en memoria para reducir accesos a Pager y define fijación, dirty pages, eviction y flush. Todos los backends implementan `IBufferPool`.

## Contrato de acceso

- `ReadPage()` devuelve una copia y libera internamente el pin.
- `WritePage()` actualiza la página y la marca dirty.
- `FetchPage()` devuelve el frame mutable y debe cerrarse con `UnpinPage()`.
- `FetchPageHandle()` ofrece un `PageHandle : IDisposable`; `MarkDirty()` registra cambios y `Dispose()` libera el pin.

Una página fijada no puede expulsarse. La dirty page se escribe con Pager al ser expulsada o al hacer `Flush()`. El pool rechaza `Dispose()` si todavía hay pins activos, para que una fuga sea visible.

## Políticas disponibles

`ClockBufferPool` usa segunda oportunidad: recorre frames circularmente, da otra oportunidad a páginas referenciadas y elige la primera no fijada y sin referencia. Limita el trabajo de búsqueda a dos vueltas. Es el backend por defecto por su estado pequeño y política sencilla.

`LruBufferPool` mantiene una lista enlazada desde más reciente a menos reciente y expulsa el último frame elegible. `FullCacheBufferPool` conserva todas las páginas en memoria y sirve como comparación, no como política escalable. `DirectBufferPool` no conserva un cache reutilizable tras el último unpin y escribe una página dirty al liberarla.

## Dependencias y build

Depende únicamente de Pager; no conoce SQL, filas ni el B+Tree.

```sh
dotnet build Engine/Storage/BufferPool/Engine.Storage.BufferPool.csproj
```