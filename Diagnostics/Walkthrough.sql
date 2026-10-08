CREATE TABLE people (id INTEGER PRIMARY KEY, name TEXT, city TEXT);
INSERT INTO people VALUES (10, 'Ana', 'Lima');
INSERT INTO people VALUES (20, 'Luis', 'Quito');
INSERT INTO people VALUES (30, 'Eva', 'Bogota');
SELECT * FROM people WHERE id = 20;
UPDATE people SET city = 'Cusco' WHERE id = 20;
SELECT name, city FROM people WHERE id = 20;
DELETE FROM people WHERE id = 10;
SELECT * FROM people WHERE id = 10;