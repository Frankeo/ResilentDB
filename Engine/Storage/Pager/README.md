# Engine.Storage.Pager

## Responsabilidad

Pager es la capa de I/O físico. Traduce IDs de página a offsets de archivo, lee y escribe páginas de tamaño fijo y asigna páginas secuencialmente al final.

## Formato del archivo

Por defecto una página mide 4096 bytes. La página 0 empieza con una cabecera de 16 bytes, formada por cuatro bytes de magic, versión, tamaño de página y cantidad de páginas (`Int32`). `PageCount` incluye la cabecera. La página 0 no se expone como página de datos; IDs de datos comienzan en 1 y `Storage` reserva el ID 1 para el catálogo.

`ReadHeader()` valida magic, versión, tamaño y longitud mínima del archivo. El encabezado se cachea en memoria y cada llamada pública devuelve una copia para impedir modificaciones externas. `ReadPage()` exige una lectura completa; `WritePage()` solo acepta páginas existentes y rellena con ceros el resto de una escritura corta. `AllocatePage()` extiende el archivo y persiste el nuevo PageCount.

Se usa acceso secuencial por offset en vez de un formato variable porque simplifica el mapeo, la validación y los cálculos de almacenamiento. `FileShare.None` evita abrir el mismo archivo desde dos `Pager` a la vez. El acceso directo a un `Pager` debe serializarse; los buffer pools lo hacen en sus operaciones.

## Limitaciones

El formato tiene versión, pero no journal/WAL ni recuperación de asignaciones incompletas. `Flush(true)` solicita sincronización al sistema operativo, no convierte varias escrituras en una transacción.

## Compilar

```sh
dotnet build Engine/Storage/Pager/Engine.Storage.Pager.csproj
```