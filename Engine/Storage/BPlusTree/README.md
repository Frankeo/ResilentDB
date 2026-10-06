# Engine.Storage.BPlusTree

## Responsabilidad

Implementa `PagedPrimaryKeyBPlusTree`, un índice persistente que también sirve como almacenamiento de filas para la clave primaria. Implementa el contrato `IPrimaryKeyIndex` de Core y usa `IBufferPool` para todo acceso a páginas.

## Estructura y algoritmos

Los nodos internos almacenan separadores y referencias a hijos; las hojas contienen claves, payloads y el ID de la hoja siguiente. La búsqueda desciende por separadores con búsqueda binaria; el scan de rango encuentra la hoja inicial y avanza por la cadena de hojas, evitando volver a subir al padre por cada resultado.

Al exceder capacidad, una hoja se divide buscando una separación válida por ocupación y bytes disponibles; se inserta el separador en el padre y los splits pueden propagarse hasta crear una raíz. Al borrar, se intenta redistribuir desde un hermano; si no alcanza, se fusionan nodos y se reduce la raíz cuando queda un único hijo.

Las filas se serializan como JSON UTF-8 antes de guardarse como payload. Es fácil de inspeccionar y evoluciona con los campos del modelo, aunque ocupa más espacio y requiere más CPU que un formato binario específico. Los payloads que no caben eficientemente en una hoja se guardan en overflow pages enlazadas. Las páginas de árbol y overflow liberadas se enlazan en una free list para reutilización. Los tipos de página distinguen hojas, nodos internos, overflow y páginas libres. El formato interno usa magic `RDBI`, versión 4 y números little-endian.

## Validación y límites

`Validate()` comprueba profundidad uniforme, ocupación, orden, separadores exactos, enlaces de hojas, ownership de overflow y disyunción de la free list. Las escrituras no tienen WAL; el contexto de mutación puede revertir cambios de memoria ante una excepción, pero no es recuperación frente a caída del proceso.

El parámetro `maxKeys` permite reducir el fanout en tests y forzar splits/merges con pocos registros. Para producción faltarían recuperación transaccional, formatos de páginas endurecidos y evaluación de concurrencia.

## Dependencias y build

Depende de Core y BufferPool. Storage accede a las fábricas internas mediante `InternalsVisibleTo`.

```sh
dotnet build Engine/Storage/BPlusTree/Engine.Storage.BPlusTree.csproj
```