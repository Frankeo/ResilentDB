# Engine.Core

## Responsabilidad

Core define los contratos estables que comparten los módulos, sin depender de Parser, Executors ni Storage. Incluye `Schema`, `TableDef`, `ColumnDef`, `Row`, las clases AST de cada sentencia y los resultados tipados `CommandResult`/`QueryResult`.

## Contratos

`IExecutionContext` ofrece a los ejecutores el esquema, persistencia y acceso a índices sin acoplarlos a `DbEngine`. `IPrimaryKeyIndex` expresa las operaciones que necesita la ejecución SQL sin exponer el formato de páginas ni la implementación B+Tree.

Esta separación permite compilar Parser, Executors y Storage como bibliotecas independientes y mantiene un grafo de dependencias acíclico. Core no implementa almacenamiento ni reglas SQL.

## Compilar

```sh
dotnet build Engine/Core/Engine.Core.csproj
```