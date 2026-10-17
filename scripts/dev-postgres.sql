-- ...and the tests' user, which creates and drops a database per test (tests/Spellbound.Web.Tests/TestDatabase.cs).
CREATE ROLE postgres WITH LOGIN SUPERUSER PASSWORD 'postgres';
