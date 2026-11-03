# ADR-0009: Database rules live in the model

- **Status:** Accepted
- **Date:** 2026-11-02
- **Author:** Ethan

## Context

Rules the database can hold, it holds (DATA-11). A rule can reach SQLite two ways: through the EF Core model, in an entity's configuration, or as raw SQL in a migration. The difference shows when a table is rebuilt.

SQLite can't change most columns, or any CHECK, in place, so EF Core rebuilds the table: it copies the rows into an `ef_temp_` table, drops the old one, renames the copy and makes the table's indexes again. It writes all of that from the model as it stood when the migration was added, so it brings back only what the model knows. Anything else the table had goes with the old table, with no error and no warning.

Build Plan 3.2 brought this up. Only one business day may be open at a time, and `ClosedAt` is NULL while a night is open. The obvious rule, a unique index on `ClosedAt` filtered to the NULLs, refuses nothing, because SQLite treats NULLs as distinct in a unique index. What's needed is an index over a value every open row shares. EF Core's `HasIndex` takes only properties, not expressions, so raw SQL is the easy way to write one.

## Decision

Every rule the database holds is in the EF Core model: indexes through `HasIndex`, CHECKs through `HasCheckConstraint`, foreign keys through the relationships. When a rule needs a value no column holds, a generated column through `HasComputedColumnSql` gives the index one. Raw SQL in a migration never holds a rule.

The one open night is the first case:

- **A generated column:** `"IsOpen" AS ("ClosedAt" IS NULL)`. It's virtual, so it takes no space in the table, and it's a shadow property, so `BusinessDay` has only `Id`, `OpenedAt` and `ClosedAt`. SQLite works it out, so nothing can set it wrong.
- **A partial unique index:** `IX_BusinessDays_IsOpen` is unique on `IsOpen`, `WHERE "IsOpen"`. Every open row has the same value, so a second one is refused, whether it's an insert or an update that reopens a closed night. Closed rows aren't in the index.
- **An ordinary refusal:** SQLite reports a unique index's error, so `SqliteErrors.IsUniqueIndexRefusal` reads it as it reads every other unique index's, and `BusinessDays` handles a lost race the way `MemberRoster` does.

## Alternatives

- **A filtered unique index on `ClosedAt`.** It can't work: a scratch table with one took two open rows.
- **A raw expression index in the migration,** `CREATE UNIQUE INDEX ... ON "BusinessDays" ((1)) WHERE "ClosedAt" IS NULL`. It's the smallest answer: no extra column, and it refuses a second open row with the same error. But the model doesn't know it. On a scratch copy, a later migration that rebuilt `BusinessDays` dropped it without a word, and recreated the generated column's index that sat beside it. A test of the rule would catch that, but only once the rebuild was written, and only for a rule that has a test.
- **A `bool` column that code keeps in step with `ClosedAt`.** Its index would be in the model, so it would survive a rebuild. But the two columns could disagree: a write that sets one and forgets the other, such as a repair by hand in the SQLite shell, would leave the index guarding the wrong thing.
- **A trigger that refuses a second open row.** It's raw SQL too, so a rebuild would drop it, and it reports a trigger's error, not a unique index's.

## Consequences

- **A rebuild keeps every rule.** On that scratch copy, the rebuild also left the generated column out of its copy, kept the row, and still refused a second open night.
- **The model test covers the rules.** `Model_ComparedWithMigrations_HasNoPendingChanges` fails when a rule in the model changes without a migration. It can't see raw SQL.
- **A column nothing reads.** `BusinessDays` shows a fourth column in the SQLite shell, which this ADR explains.
- **Raw SQL is still allowed for what isn't a rule,** such as moving data. No migration has any today.
- **A rule the model can't express** gets its own ADR before it goes into a migration's raw SQL.
- **Reviews check it (DATA-5):** reading a migration's script includes asking whether any rule in it is missing from the model.
