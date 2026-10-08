# ResilentDB.Diagnostics

Herramienta de consola para seguir una sentencia SQL a través de Parser, Engine, Executors, Storage y B+ tree. La traza es opcional y se activa usando este proyecto; el CLI habitual mantiene su salida normal.

## Uso

```sh
dotnet run --project Diagnostics/Diagnostics.csproj -- /tmp/demo.mdb assets/file.sql
```

Argumentos: primero el archivo de base de datos y luego el script SQL. Usa una ruta nueva si el script contiene `CREATE TABLE`, porque el motor conserva el esquema entre ejecuciones.

Cada evento muestra secuencia, componente, operación y detalle. Los eventos de Executor enseñan el plan elegido, las claves primarias y los campos modificados. Los de BPlusTree detallan selección de hijos, páginas/hojas leídas y escritas, splits, redistribuciones, merges, overflow y metadata.

La secuencia típica de una consulta por clave primaria se ve así:

```text
[Parser] StatementsParsed
[Engine] StatementStarted
[Executor] AccessPlan: primary-key point lookup
[BPlusTree] LookupStarted
[BPlusTree] InternalNodeVisited
[BPlusTree] LeafSelected
[BPlusTree] LeafPageRead
[BPlusTree] LookupCompleted
[Executor] RowsRead
[Engine] StatementCompleted
```

Los detalles de filas y valores se imprimen intencionalmente para facilitar el aprendizaje. No uses esta herramienta para registrar datos sensibles.