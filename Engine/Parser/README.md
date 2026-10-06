# Engine.Parser

## Responsabilidad

Convierte texto SQL del subconjunto soportado en AST de `Engine.Core`. `Parser` ofrece `Parse` para una sentencia y `ParseStatements` para lotes separados por punto y coma.

## Estrategia

`Parser` detecta el tipo de comando y delega en un parser especializado (`CreateTableStatementParser`, `InsertStatementParser`, `SelectStatementParser`, `UpdateStatementParser` o `DeleteStatementParser`). La separación de sentencias recorre los caracteres y evita dividir dentro de comillas. Las clases base reúnen conversión de valores, separación de elementos entre comillas y lectura sencilla de `WHERE`.

Se usan expresiones regulares porque la gramática es pequeña y el proyecto es educativo; no equivalen a un parser SQL completo. No hay soporte general de comentarios, escapes de comillas, joins ni precedencia de expresiones.

## Dependencias y build

Solo depende de `Engine.Core`; las constantes gramaticales son locales al módulo.

```sh
dotnet build Engine/Parser/Engine.Parser.csproj
```