# ResilentDB.Diagnostics

Un recorrido ejecutable para ver cómo una sentencia SQL pasa por el parser, el dispatcher, los executors, Storage y el B+ tree. Cada sentencia y su resultado aparecen separados para poder seguir la traza en orden.

## Ejecutar el recorrido

Desde la raíz del repositorio:

```sh
dotnet run --project Diagnostics/Diagnostics.csproj -- /tmp/diagnostics-walkthrough.mdb Diagnostics/Walkthrough.sql
```

Usa un archivo de base de datos nuevo, ya que el ejemplo crea la tabla `people`. Si repites la ejecución, cambia la ruta o elimina el archivo de la base que hayas creado.

El script [Walkthrough.sql](Walkthrough.sql) ejecuta nueve pasos: crear la tabla, insertar tres filas, buscar una clave primaria, actualizar una columna, volver a leerla, borrar una fila y confirmar que ya no existe. El programa imprime `=== STEP n/9 ===` antes de cada operación y presenta su resultado inmediatamente después de los eventos.

## Cómo leer la traza

Cada línea contiene un contador global, un componente, un evento y detalles. La indentación marca datos secundarios de una operación, como los campos de una fila:

```text
000021 [Executor] InsertStarted: table=people, suppliedValues=3
000022   [Executor] FieldAdded: table=people, field=id, value=10
000023   [Executor] FieldAdded: table=people, field=name, value="Ana"
000024   [Executor] FieldAdded: table=people, field=city, value="Lima"
000025 [Executor] PrimaryKeyResolved: table=people, key=10
000026 [Executor] DuplicateCheck: key=10
000027 [BPlusTree] LookupStarted: key=10, rootPage=3
000028 [BPlusTree] LeafSelected: key=10, page=3, depth=0
000029 [BPlusTree] LeafPageRead: page=3, keys=[], next=-1, payloadKinds=[]
000030 [BPlusTree] LookupCompleted: key=10, found=false, leafPage=3, insertionSlot=0
000031 [Executor] IndexInsert: key=10, fields=3
000032 [BPlusTree] InsertStarted: key=10, payloadBytes=47
000033 [BPlusTree] LeafInsertPosition: page=3, key=10, index=0, existingKeys=[]
000034 [BPlusTree] LeafPageWritten: page=3, entries=1, encodedBytes=69
000035 [BPlusTree] MetadataWritten: page=2, root=3, count=1, maxKeys=32, freeHead=-1
000036 [Storage] SaveStarted: tables=1
000037 [Storage] SaveCompleted: pages=4
000038 [Engine] StatementCompleted: InsertStatement: CommandResult ...
RESULT 1 fila insertada
```

Los números de página, bytes y contadores pueden variar entre ejecuciones. En esta inserción se ven dos recorridos del índice: uno para comprobar que la clave no exista y otro para encontrar dónde insertar. Con solo tres registros, la raíz también es la hoja, por eso aparece `LeafSelected` pero no `InternalNodeVisited`.

## Métodos por sentencia

### Crear tabla

Para `CREATE TABLE people (...)`, sigue estos eventos: `CreateTableStarted`, un `ColumnDefined` por columna, `PrimaryIndexCreationStarted`, `PrimaryIndexCreated`, `SaveStarted` y `CreateTableCompleted`.

```text
DbEngine.Execute(Statement)
	-> ExecutionDispatcher.Execute
		-> CreateTableExecutor.Execute
			-> DbEngine.CreatePrimaryIndex
				-> Storage.CreatePrimaryIndex
					-> BPlusTree.CreateOnBufferPool
						-> BPlusTree.Initialize
							-> WriteLeaf
							-> WriteMetadata
			-> DbEngine.Save
				-> Storage.Save
					-> WriteCatalog
```

El índice empieza vacío, pero ya tiene una metadata page y una hoja raíz. `ColumnDefined` describe el esquema; las columnas no se convierten en entradas separadas del B+ tree.

Código: [CreateTableExecutor](../Engine/Executors/CreateTableExecutor.cs), [DbEngine](../Engine/DbEngine.cs), [Storage](../Engine/Storage/Storage.cs), [BPlusTree](../Engine/Storage/BPlusTree/BPlusTree.cs), [PageIO](../Engine/Storage/BPlusTree/PageIO.cs).

### Insertar fila

`INSERT INTO people VALUES (10, 'Ana', 'Lima')` convierte los valores en un `Row` y usa `id` como clave. Mira `FieldAdded`, `PrimaryKeyResolved`, `DuplicateCheck`, `LeafInsertPosition`, `LeafPageWritten` y `MetadataWritten`.

```text
InsertExecutor.Execute
	-> IPrimaryKeyIndex.ContainsKey
		-> BPlusTree.TryGetPayload
			-> FindLeaf -> ReadLeaf -> LowerBound
	-> IPrimaryKeyIndex.InsertRecord
		-> BPlusTree.InsertRecord -> InsertPayload
			-> FindLeaf -> ReadLeaf -> LowerBound
			-> EnsureOverflowEntries
			-> WriteLeaf -> WriteMetadata
	-> IExecutionContext.Save -> Storage.Save
```

`Row` completo se serializa como JSON UTF-8 en el payload de la entrada cuya clave es `id`. Si una hoja se llena, la ruta añade `SplitLeaf`, `FindLeafSplitIndex` y eventos de promoción del separador.

Código: [InsertExecutor](../Engine/Executors/InsertExecutor.cs), [ExecutorBase](../Engine/Executors/ExecutorBase.cs), [Traversal](../Engine/Storage/BPlusTree/Traversal.cs), [PageIO](../Engine/Storage/BPlusTree/PageIO.cs), [Rebalancing](../Engine/Storage/BPlusTree/Rebalancing.cs).

### Buscar fila

`SELECT * FROM people WHERE id = 20` toma el plan de búsqueda puntual porque el filtro usa la clave primaria. Sigue `AccessPlan`, `LookupStarted`, `LeafSelected`, `LeafPageRead` y `LookupCompleted`; después aparecen `RowsRead` y `ProjectionApplied`.

```text
SelectExecutor.Execute
	-> ExecutorBase.ValidateWhereClause
	-> ExecutorBase.FindMatchingRows
		-> IPrimaryKeyIndex.TryGetRecord
			-> BPlusTree.TryGetRecord -> TryGetPayload
				-> FindLeaf
					-> ReadInternal -> UpperBound   (si hay nodos internos)
				-> ReadLeaf -> LowerBound
				-> DeserializeRow
	-> proyectar las columnas solicitadas
```

En el ejemplo de tres filas solo se lee la raíz hoja. `FindLeaf` compara con separadores y elige un hijo únicamente cuando el árbol ya tiene nodos internos.

Código: [SelectExecutor](../Engine/Executors/SelectExecutor.cs), [ExecutorBase](../Engine/Executors/ExecutorBase.cs), [Traversal](../Engine/Storage/BPlusTree/Traversal.cs), [PageIO](../Engine/Storage/BPlusTree/PageIO.cs).

### Actualizar columna

`UPDATE people SET city = 'Cusco' WHERE id = 20` primero busca la fila por clave primaria, registra `FieldUpdated` con el valor anterior y el nuevo, y reemplaza el payload serializado mediante `UpdateRecord`.

```text
UpdateExecutor.Execute
	-> ExecutorBase.FindMatchingRows
		-> TryGetRecord -> TryGetPayload -> FindLeaf -> ReadLeaf
	-> modificar row.Values["city"]
	-> IPrimaryKeyIndex.UpdateRecord
		-> BPlusTree.UpdateRecord
			-> FindLeaf -> ReadLeaf -> LowerBound
			-> liberar overflow anterior, si existe
			-> WriteLeaf -> WriteMetadata
	-> Save -> Storage.Save
```

Por eso un `UPDATE` normal hace una búsqueda para evaluar el `WHERE` y vuelve a encontrar la hoja para escribir el registro. Cambiar la clave primaria tiene otra ruta: el executor borra la clave anterior e inserta la nueva.

Código: [UpdateExecutor](../Engine/Executors/UpdateExecutor.cs), [BPlusTree](../Engine/Storage/BPlusTree/BPlusTree.cs), [PageIO](../Engine/Storage/BPlusTree/PageIO.cs).

### Borrar fila

`DELETE FROM people WHERE id = 10` primero obtiene la fila que coincide, emite `RowDelete` con sus campos y después llama a `Delete(key)`. En el ejemplo, la raíz sigue siendo hoja y solo se reescribe esa hoja.

```text
DeleteExecutor.Execute
	-> ExecutorBase.FindMatchingRows
		-> TryGetRecord -> TryGetPayload -> FindLeaf -> ReadLeaf
	-> IPrimaryKeyIndex.Delete
		-> BPlusTree.Delete
			-> FindLeaf -> ReadLeaf -> LowerBound
			-> quitar entrada y liberar overflow, si existe
			-> RebalanceLeaf (si una hoja no raíz queda bajo el mínimo)
			-> WriteLeaf -> WriteMetadata
	-> Save -> Storage.Save
```

Con un árbol más grande, busca eventos `LeafRedistribution`, `LeafMerge`, `TreePageFreed` e `InternalNodeSplit` durante inserciones/deleciones. El B+ tree predeterminado permite hasta 32 claves por nodo; al superar la capacidad, se divide una hoja y el separador sube al padre. Después, una búsqueda puede mostrar `InternalNodeVisited` con los separadores y el hijo elegido.

Código: [DeleteExecutor](../Engine/Executors/DeleteExecutor.cs), [BPlusTree](../Engine/Storage/BPlusTree/BPlusTree.cs), [Rebalancing](../Engine/Storage/BPlusTree/Rebalancing.cs), [Allocator](../Engine/Storage/BPlusTree/Allocator.cs).

## Eventos y métodos

El texto del evento es una etiqueta de diagnóstico, no necesariamente el nombre literal del método. Usa esta tabla para saltar desde la traza al código que decide la operación:

| Evento | Método o decisión |
| --- | --- |
| `StatementStarted` | `DbEngine.Execute(Statement)` |
| `CreateTableStarted`, `ColumnDefined` | `CreateTableExecutor.Execute` |
| `AccessPlan` | `ExecutorBase.FindMatchingRows` |
| `LookupStarted`, `LeafSelected`, `InternalNodeVisited` | `BPlusTree.TryGetPayload` y `FindLeaf` |
| `LeafPageRead`, `LeafPageWritten` | `ReadLeaf` y `WriteLeaf` |
| `LeafInsertPosition` | `InsertPayload` después de `LowerBound` |
| `LeafSplit`, `LeafRedistribution`, `LeafMerge` | métodos de `Rebalancing.cs` |
| `MetadataWritten` | `WriteMetadata` |
| `SaveStarted`, `SaveCompleted` | `Storage.Save` |
| `StatementCompleted` | `DbEngine.Execute(Statement)` |

Las páginas físicas entran por `IBufferPool`; Storage comparte el pool entre índices y `Save()` lo vacía antes de escribir el catálogo. Para los detalles del formato persistente, consulta [la guía del B+ tree](../Engine/Storage/BPlusTree/README.md).

Los detalles de filas y valores se imprimen intencionalmente para facilitar el aprendizaje. No uses esta herramienta para registrar datos sensibles.