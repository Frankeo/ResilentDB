# Engine.Storage

## Responsabilidad

Este proyecto coordina el archivo de base de datos: mantiene el catálogo, crea/abre índices primarios, valida su correspondencia con el esquema y controla el flush. No implementa el formato de página ni las políticas de caché; delega esas tareas en Pager, BufferPool y B+Tree.

## Catálogo

La página 1 contiene un registro con magic `RDC4`, longitud y JSON. El JSON guarda el `Schema` y el mapa de tabla a página de metadata de su índice. `Load()` comprueba el tamaño del payload, que haya un ID distinto por tabla y que cada tabla tenga exactamente una clave primaria `INTEGER` antes de abrir y validar los índices.

Se eligió un catálogo JSON de una página porque es fácil de inspeccionar durante el aprendizaje. Su tamaño limita el número de tablas e índices; no es un catálogo multipágina.

## Persistencia y límites

`Save()` fuerza primero las páginas dirty pendientes, escribe el catálogo actualizado y vuelve a vaciar el buffer. Esto publica el catálogo después de las páginas que aún están en caché, pero no controla el orden interno por tipo de página ni revierte escrituras tempranas por eviction. No hay WAL ni atomicidad ante una caída.

La clase es propietaria de un único `ClockBufferPool` compartido por los árboles abiertos. Al cerrarse, libera primero los árboles y luego el pool.

## Dependencias y build

Depende de Core, BufferPool y B+Tree. BufferPool aporta Pager transitivamente.

```sh
dotnet build Engine/Storage/Engine.Storage.csproj
```