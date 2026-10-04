CREATE TABLE users (
    id INTEGER PRIMARY KEY,
    name TEXT,
    age INTEGER
);


INSERT INTO users VALUES (1, 'Hayden Brooks', 25);
INSERT INTO users VALUES (2, 'Emerson Cole', 30);
INSERT INTO users VALUES (3, 'Finley Hart', 35);
INSERT INTO users VALUES (4, 'Rowan Miles', 40);
INSERT INTO users VALUES (5, 'Skyler Ford', 45);
INSERT INTO users VALUES (6, 'Dakota Wells', 50);
INSERT INTO users VALUES (7, 'Reese Palmer', 55);

UPDATE users
SET name = 'Carlos', age = 25
WHERE id = 3;

DELETE FROM users
WHERE id = 3;


