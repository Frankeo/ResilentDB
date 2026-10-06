# ResilentDB

## Persistencia

`Storage.Save()` y la creación de índices vacían primero las páginas dirty pendientes del B+Tree, escriben después el catálogo y vuelven a vaciar el buffer pool. `ClockBufferPool.Flush()` también fuerza el vaciado del archivo a disco.

El buffer pool no ordena las páginas del B+Tree por tipo dentro del primer vaciado, y una expulsión puede escribir una página antes de `Save()`. Este orden publica el catálogo después de las páginas pendientes, pero no ofrece atomicidad frente a un fallo del proceso o del sistema; no hay WAL.