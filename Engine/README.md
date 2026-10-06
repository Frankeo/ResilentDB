# Engine

## Responsabilidad

Este proyecto es la fachada del motor. `DbEngine` es el punto de entrada de la aplicación: crea o abre Storage, carga el esquema, valida los índices y coordina la ejecución de sentencias y su ciclo de vida.

## Cómo funciona

`Execute(string)` divide el texto en sentencias mediante `Engine.Parser`, y envía cada AST al `ExecutionDispatcher` de `Engine.Executors`. El dispatcher trabaja con `IExecutionContext`, que implementa `DbEngine`; así los ejecutores no dependen de esta fachada concreta. Los resultados son `CommandResult` o `QueryResult`, no objetos sin tipo.

`Engine.csproj` solo compila `DbEngine.cs` y referencia Core, Parser, Executors y Storage. Los archivos de esos módulos se excluyen de la compilación local para evitar duplicar tipos.

## Ejecutar

```sh
dotnet build Engine/Engine.csproj
```

Las bibliotecas de cada módulo pueden compilarse y estudiarse por separado desde sus respectivos `.csproj`. La fachada es útil para aplicaciones sencillas; los consumidores avanzados pueden referenciar directamente los módulos que necesiten.