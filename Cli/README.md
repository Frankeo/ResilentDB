# CLI

## Responsabilidad

La aplicación de consola es la interfaz interactiva del motor. Crea `basededatos.mdb` en el directorio de trabajo, entrega SQL a `DbEngine` y presenta `CommandResult` y `QueryResult`.

## Uso

```sh
dotnet run --project Cli/Cli.csproj
```

Escribe sentencias SQL en el prompt `mdb>`. `.read <archivo.sql>` ejecuta un archivo con varias sentencias; `.exit` termina. Los errores se muestran en consola sin finalizar la sesión.

El CLI no interpreta SQL ni accede al archivo directamente: esa separación mantiene la consola como capa de presentación y hace que el mismo motor se pueda usar desde tests u otras aplicaciones.