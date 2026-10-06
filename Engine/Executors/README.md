# Engine.Executors

## Responsabilidad

Implementa la semántica de `CREATE TABLE`, `INSERT`, `SELECT`, `UPDATE` y `DELETE`. Cada executor declara qué AST acepta y produce un `CommandResult` o `QueryResult`.

## Organización

`ExecutionDispatcher` recorre el conjunto pequeño de ejecutores y entrega el primero que reconoce la sentencia. `ExecutorBase` comparte validación de `WHERE`, comparación de valores y búsqueda de filas. Cuando el filtro apunta a la clave primaria, se usa búsqueda puntual o range scan del índice; con otras columnas, se escanean filas y se filtran en memoria.

Los ejecutores reciben `IExecutionContext` y usan `IPrimaryKeyIndex` desde Core. Esto evita una referencia a la fachada `Engine` y, por tanto, una dependencia circular.

## Dependencias y build

Solo depende de Core; no conoce Pager, buffer pools ni el formato del B+Tree.

```sh
dotnet build Engine/Executors/Engine.Executors.csproj
```