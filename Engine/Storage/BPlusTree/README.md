# Engine.Storage.BPlusTree

## Responsabilidad

Implementa `BPlusTree`, un índice persistente que también sirve como almacenamiento de filas para la clave primaria. Implementa `IPrimaryKeyIndex` de Core y usa `IBufferPool` para todo acceso a páginas. No mantiene el árbol entero en memoria: conserva root/count/free-list metadata y carga nodos cuando los necesita.

## Por qué un B+Tree

Un árbol binario puede necesitar muchas lecturas aleatorias para llegar a una clave. Un B+Tree agrupa muchas claves por página, por lo que su fanout alto reduce la altura y el número de I/O. Los nodos internos contienen solo separadores y referencias a hijos; los registros viven en hojas. Todas las hojas están al mismo nivel y enlazadas, lo que hace eficiente tanto la búsqueda puntual como el recorrido de rangos.

El parámetro `maxKeys` limita entradas por nodo. La capacidad real también depende de los bytes libres de la página: payloads inline hacen que algunas hojas deban dividirse antes de alcanzar ese máximo. En tests se usa `maxKeys: 3` para hacer visibles los splits y merges con pocos datos.

## Invariante de separadores

Cada `Keys[i]` de un nodo interno es exactamente la clave mínima del subárbol `Children[i + 1]`. Por ello, las claves quedan particionadas así:

```text
child 0: keys < separator 0
child 1: separator 0 <= keys < separator 1
...
child n: separator n-1 <= keys
```
            [30]
           /    \\
      [10, 20] -> [30, 40]
## Ejemplo de inserción y split

`30`, el mínimo de la hoja derecha, se copia al padre como separador. Si luego se insertan `25` y `27`, ambas claves van a la hoja izquierda; esa hoja vuelve a dividirse y el separador nuevo se inserta ordenadamente:

```text
          [25 | 30]
         /     |     \\
    [10, 20] -> [25, 27] -> [30, 40]
```

`30`, el mínimo de la hoja derecha, se copia al padre como separador. Si luego se insertan `25` y `27`, la hoja derecha vuelve a dividirse y el separador nuevo se inserta ordenadamente:

```text
								 [25 | 30]
							 /     |     \\
				[10, 20] -> [25, 27] -> [30, 40]
```

Si el padre también excede `maxKeys`, se divide y se promociona su clave central. El proceso sube por el path de búsqueda; si ya no queda padre, se crea una nueva raíz. Cada split mantiene los enlaces entre hojas.

## Búsqueda y range scan

Para buscar `27` en el ejemplo, el árbol compara con `[25 | 30]`, elige el segundo hijo y busca la clave en `[25, 27]`. La búsqueda en cada nodo usa búsqueda binaria. A nivel de páginas el coste esperado es `O(log_F N)`, donde `F` es el fanout y `N` el número de claves; el número exacto de lecturas depende de la altura y de los aciertos en el buffer pool.

Un scan `[26, 35]` encuentra primero la hoja que podría contener `26`, salta `25`, devuelve `27`, sigue `NextPageId` y devuelve `30`; al encontrar `40` termina. En accesos a páginas cuesta aproximadamente `O(log_F N + P)`, donde `P` es el número de hojas visitadas; el trabajo para producir `K` resultados es `O(log_F N + K)`.

Ejemplo de API:

```csharp
using var tree = new BPlusTree(
		"index.db", maxKeys: 3, bufferPoolCapacity: 2);

tree.Insert(10, 100);
tree.Insert(20, 200);
tree.Insert(30, 300);
tree.Insert(40, 400); // provoca un split con maxKeys = 3

var range = tree.Scan(20, 35);
tree.Validate();
```

Para filas, `InsertRecord` serializa un `Row` y `TryGetRecord` lo reconstruye.

## Eliminación, redistribución y merge

Después de borrar, una hoja no raíz debe conservar al menos `MinimumLeafKeys`. El árbol intenta primero tomar prestada la última entrada del hermano izquierdo o la primera del derecho, actualizando el separador afectado. Si ningún hermano puede prestar, fusiona hojas vecinas y repara `NextPageId`.

Eliminar un hijo también quita una clave del padre. Si un nodo interno queda por debajo de su mínimo, el algoritmo intenta redistribuir hijos con un hermano; en caso contrario fusiona el nodo con el separador del padre. La operación puede propagarse hacia arriba. Si la raíz interna queda sin claves, se reemplaza por su único hijo y se libera la página de la raíz anterior.

```text
Antes:  parent [20]
		  /      \\
	[10, 15] -> [20, 25]

Tras borrar 20, la hoja derecha queda [25]. La izquierda está en su mínimo,
así que no puede prestar; se fusionan en [10, 15, 25] y la raíz se reduce.
```

El ejemplo ilustra la idea, no una secuencia fija de páginas: el hermano elegido depende de la posición del hijo y de si alguno puede prestar.

## Formato de página

Los números enteros se codifican little-endian. Cada árbol tiene una metadata page con magic `RDBI`, versión 4, root page ID, cantidad de claves, `maxKeys` y cabeza de la free list. En uso independiente el metadata ID por defecto es 1; `Storage` asigna un ID distinto a cada índice dentro del archivo compartido.

Las páginas de árbol empiezan con un byte de tipo y un contador de entradas; el encabezado ocupa 9 bytes. Una hoja añade su `NextPageId`. Cada entrada de hoja contiene:

```text
key: 8 bytes | kind: 1 byte | payload length: 4 bytes | value
```

Con `kind = 0`, `value` son los bytes inline del payload. Con `kind = 1`, `value` es el ID de la primera overflow page. Los nodos internos almacenan su primer hijo y luego pares `(separator, child)`; así `Children.Count == Keys.Count + 1`.

Las filas se serializan como JSON UTF-8 antes de guardarse. Es legible y evoluciona con el modelo, a cambio de más espacio y CPU que un formato binario específico.

## Overflow pages y reutilización

Un payload grande se divide en bloques de `PageSize - NodeHeaderSize` bytes. Cada overflow page guarda su tipo, ID de la siguiente página y cantidad de bytes usados. Al leer, el árbol sigue la cadena hasta reconstruir la longitud declarada y rechaza IDs inválidos, ciclos, tipos inesperados o longitudes inconsistentes.

Al borrar o reemplazar un payload, sus páginas overflow se liberan. Las páginas de árbol que dejan de usarse durante merges también se liberan. Ambas se encadenan con `FreePageType`; `AllocateTreePage()` reutiliza primero la cabeza de la free list y solo extiende el archivo cuando no hay páginas libres. Esto reduce crecimiento tras patrones de insertar/borrar repetidos.

## Qué valida `Validate()`

La validación recorre el árbol y construye conjuntos de páginas alcanzables. Comprueba:

- que todas las hojas estén a la misma profundidad y respeten ocupación;
- que las claves estén estrictamente ordenadas y cada separador sea el mínimo exacto del hijo derecho;
- que cada hoja alcanzable aparezca en el orden indicado por `NextPageId` y no haya ciclos;
- que cada cadena overflow tenga longitud/tipo correctos y no comparta páginas;
- que las páginas del árbol, overflow y free list sean disjuntas, y que la free list no contenga ciclos.

Por eso las pruebas llaman a `Validate()` después de secuencias aleatorias y de borrados que fuerzan rebalanceos.

## Atomicidad y límites

Las operaciones serializan acceso por instancia con un lock. `MutationContext` conserva páginas originales y asignaciones nuevas para revertir una excepción durante la operación en memoria. No hay WAL: esa reversión no protege contra apagado o caída entre escrituras, y el flush de múltiples páginas no es una transacción.

El formato actual está orientado a aprendizaje y tests, no a compatibilidad de largo plazo ni recuperación de desastres. Para producción faltarían WAL/recovery, límites de recursos frente a archivos hostiles, estrategia de upgrade de formato y una evaluación más extensa de concurrencia.

## Dependencias y build

Depende de Core y BufferPool. Storage accede a las fábricas internas mediante `InternalsVisibleTo`.

```sh
dotnet build Engine/Storage/BPlusTree/Engine.Storage.BPlusTree.csproj
dotnet test UnitTests/UnitTests.csproj --filter FullyQualifiedName~BPlusTreeTests
```