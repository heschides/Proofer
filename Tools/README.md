# Tools

Maintenance scripts. All are plain Windows PowerShell 5.1 and take no dependencies
beyond what ships with Windows, so they run on a machine that has nothing installed but
the app itself. Paths default to this repository; run them from anywhere.

---

## Test-MigrationChain.ps1

Replays all migrations symbolically, in order, tracking every table and column as it
goes, and reports operations that would fail against an empty database — adding a column
that already exists (SQL 2705), dropping or altering one that does not (SQL 4924),
creating a table twice.

```powershell
.\Tools\Test-MigrationChain.ps1
```

Run before any release. A working database only ever receives *new* migrations, so a
migration that repeats an earlier one is recorded as applied and never re-runs locally —
the breakage is invisible until someone builds from zero. That is what broke setup on a
second machine in August 2026, twice.

## Test-SchemaDrift.ps1

Compares every column in `SatiContextModelSnapshot.cs` against the schema actually
present in the database, and lists what the model expects but the database lacks.

```powershell
.\Tools\Test-SchemaDrift.ps1
```

Point it at a **freshly migrated** database, not your daily one — the daily database has
accumulated hand-applied fixes that hide exactly the gaps this looks for.

`Add-Migration` cannot substitute for this. It diffs the model against the snapshot, so a
column present in both, but never created by any migration file, looks like no change at
all. `Notes.Minutes` and `Notes.StartTime` sat in that blind spot until a fresh database
produced a `Notes` table the model could not query.

## New-SatiUser.ps1

Creates or repairs an account with any role, including Admin, writing directly to the
database with the same PBKDF2-SHA256 hashing the app uses.

```powershell
.\Tools\New-SatiUser.ps1 -Username josh -Role Admin
```

**Recovery only.** The normal way to get the first administrator is to launch Sati against
a database that has none: startup opens the Create Administrator window and will not
continue past it. Reach for this script when that door is closed — the only admin is
locked out, or an account needs a role the UI will not grant. Given an existing username
it updates that account in place, so it also serves as a password reset.
