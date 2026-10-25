# JSON text parse API contract

POST /parse accepts a UTF-8 JSON text body (text/plain or application/json).
The server decodes UTF-8 to an owned string, deserializes to a typed object,
then serializes the same object to an application/json response, status 200.
No database, external service, client cache, response cache, compression, TLS,
or artificial delay is used. This measures HTTP + decoding + parsing + object
allocation + serialization, NOT a parser-only microbenchmark or language ranking.

Fields: id (int32), name (string), active (bool), score (finite double), text
(string), tags (array of strings), items (array of objects with int32 id,
finite double price, int32 quantity), attributes (string-to-string object).
All fields are required and non-null. Unknown fields are rejected. Field names
are case-sensitive. Property order and numeric lexical spelling are not significant.
Input object property names must be unique; duplicate-field inputs are outside
this benchmark contract. Neither implementation performs general JSON Schema validation.

Malformed JSON, invalid UTF-8, missing fields, wrong types, null references,
and unknown fields: 400. Body > 1 MiB: 413. Error body format is not compared.
GET /health: 200 text 'ok'.

Use only locally generated nonsensitive data. Both servers bind host loopback
ports (18090, 18091, 18092). The Docker network is local; no workspace publication.
