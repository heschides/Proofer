/*
================================================================================
  Report: duplicate compliance form rows  (READ ONLY -- no writes, no DDL)
================================================================================

  WHAT HAPPENED
  -------------
  Before 57af6fa (2026-07-24), PersonService.GetAllPeopleAsync ran
  EnsureCurrentCycleForms + SaveChangesAsync unconditionally on every caseload
  load, each call on its own DbContext from the factory. Startup issued those
  loads concurrently -- the stall that same commit fixed by serializing them.

  Each concurrent context read Forms, found the current- and next-cycle rows
  missing, and inserted a full set. There is no unique constraint on
  (PersonId, Type, DueDate) to stop the second and third writer. Three
  concurrent loaders produced three copies.

  57af6fa stopped the bleeding: EnableEnsureCycleFormsOnLoad is false and the
  loads are serialized. The rows it already wrote were never cleaned up.

  WHY IT SURFACES NOW
  -------------------
  Duplicates are invisible until one ages past its due date.
  GetCurrentCycleForm returns a single row -- OrderByDescending(DueDate)
  .FirstOrDefault() over a tie -- so the checkbox, the matrix and the task board
  all read one copy. BillingComplianceGate iterates every row in Person.Forms,
  so it sees the copies nobody can reach, and Distinct collapses their identical
  messages into one bullet. Completing the form writes the reachable copy and
  changes nothing the gate reads.

  This will recur every quarter, per client, as each duplicated review comes due.

  WHY THIS SCRIPT ONLY REPORTS
  ----------------------------
  The copies have DIVERGED. Different copies of the same form carry different
  completion dates because edits landed on whichever copy happened to surface.
  Collapsing on lowest Id would silently discard real attestations, and
  CompletedDate is date-keyed into BillingComplianceGate.IsBillingWindowBlocked
  -- which completion date survives decides whether past service dates were
  billable. That is a billing decision, not a mechanical one, so nothing here
  deletes or updates anything.

  Result 3 is the one to read: it separates the groups that can be collapsed
  mechanically from the ones that need a human to choose.

  HOW TO RUN
  ----------
      sqlcmd -S "(localdb)\MSSQLLocalDB" -d SatiProduction -E ^
             -i scripts\Report-DuplicateComplianceForms.sql -W -s "|"

  The -W -s "|" makes the output narrow enough to read and paste. Output
  contains no names, narratives, birth dates or identifiers -- only PersonId,
  form type, and the date/flag columns.
================================================================================
*/

SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

--------------------------------------------------------------------------------
-- 0. Connection sanity. If FormRows is near zero you are on the wrong instance.
--------------------------------------------------------------------------------
SELECT
    N'0-connection'                     AS Result,
    DB_NAME()                           AS DatabaseName,
    @@SERVERNAME                        AS ServerName,
    (SELECT COUNT(*) FROM dbo.People)   AS PeopleRows,
    (SELECT COUNT(*) FROM dbo.Forms)    AS FormRows;

--------------------------------------------------------------------------------
-- 1. Scale of the problem.
--------------------------------------------------------------------------------
WITH Groups AS (
    SELECT PersonId, Type, DueDate, COUNT(*) AS Copies
    FROM dbo.Forms
    GROUP BY PersonId, Type, DueDate
)
SELECT
    N'1-scale'                                              AS Result,
    (SELECT COUNT(*) FROM dbo.Forms)                        AS TotalFormRows,
    (SELECT COUNT(*) FROM Groups)                           AS DistinctForms,
    (SELECT COUNT(*) FROM Groups WHERE Copies > 1)          AS DuplicatedForms,
    (SELECT ISNULL(SUM(Copies - 1), 0) FROM Groups WHERE Copies > 1)
                                                            AS SurplusRows,
    (SELECT COUNT(DISTINCT PersonId) FROM Groups WHERE Copies > 1)
                                                            AS PeopleAffected,
    (SELECT MAX(Copies) FROM Groups)                        AS MaxCopiesOfOneForm;

--------------------------------------------------------------------------------
-- 2. Which duplicated forms are already blocking billing, or will block next.
--    A group blocks when ANY copy is overdue and incomplete, exactly as
--    BillingComplianceGate.IsIncompleteAndOverdue decides it.
--------------------------------------------------------------------------------
WITH Groups AS (
    SELECT
        PersonId,
        Type,
        DueDate,
        COUNT(*)                                                    AS Copies,
        SUM(CASE WHEN CompletedDate IS NOT NULL THEN 1 ELSE 0 END)  AS CopiesWithDate,
        SUM(CASE WHEN CAST(DueDate AS date) < CAST(GETDATE() AS date)
                  AND (CompletedDate IS NULL
                       OR CAST(CompletedDate AS date) > CAST(GETDATE() AS date))
                 THEN 1 ELSE 0 END)                                 AS CopiesBlocking
    FROM dbo.Forms
    GROUP BY PersonId, Type, DueDate
)
SELECT
    N'2-blocking-now'   AS Result,
    PersonId,
    Type,
    DueDate,
    Copies,
    CopiesWithDate,
    CopiesBlocking,
    CASE WHEN CopiesWithDate > 0
         THEN N'FALSE BLOCK -- work was attested on another copy'
         ELSE N'genuinely outstanding on every copy'
    END                 AS Reading
FROM Groups
WHERE Copies > 1
  AND CopiesBlocking > 0
ORDER BY PersonId, DueDate, Type;

--------------------------------------------------------------------------------
-- 3. THE MERGE PLAN. Every duplicated form, classified by whether its copies
--    agree. "AGREE" groups can be collapsed mechanically -- any survivor
--    carries identical state. "CONFLICT" groups need someone to choose, because
--    the copies hold different completion dates and the choice changes which
--    past service dates were billable.
--------------------------------------------------------------------------------
WITH Groups AS (
    SELECT
        PersonId,
        Type,
        DueDate,
        COUNT(*)                            AS Copies,
        COUNT(DISTINCT CONVERT(char(8), CompletedDate, 112)) AS DistinctCompletedDates,
        SUM(CASE WHEN CompletedDate IS NOT NULL THEN 1 ELSE 0 END) AS CopiesWithDate,
        COUNT(DISTINCT CAST(IsCompliant AS int))               AS DistinctFlags,
        MIN(CompletedDate)                  AS EarliestCompletedDate,
        MAX(CompletedDate)                  AS LatestCompletedDate,
        MIN(OpenedDate)                     AS EarliestOpenedDate
    FROM dbo.Forms
    GROUP BY PersonId, Type, DueDate
)
SELECT
    N'3-merge-plan'     AS Result,
    CASE
        WHEN DistinctCompletedDates > 1
          OR (CopiesWithDate > 0 AND CopiesWithDate < Copies)
            THEN N'CONFLICT -- copies hold different completion dates'
        WHEN DistinctFlags > 1
            THEN N'CONFLICT -- copies disagree on IsCompliant'
        ELSE N'AGREE -- safe to collapse'
    END                 AS Classification,
    PersonId,
    Type,
    DueDate,
    Copies,
    CopiesWithDate,
    EarliestCompletedDate,
    LatestCompletedDate,
    EarliestOpenedDate
FROM Groups
WHERE Copies > 1
ORDER BY
    CASE
        WHEN DistinctCompletedDates > 1
          OR (CopiesWithDate > 0 AND CopiesWithDate < Copies) THEN 0
        WHEN DistinctFlags > 1 THEN 1
        ELSE 2
    END,
    PersonId, DueDate, Type;

--------------------------------------------------------------------------------
-- 4. Row-level detail for CONFLICT groups only -- the ones needing a decision.
--------------------------------------------------------------------------------
WITH Conflicted AS (
    SELECT PersonId, Type, DueDate
    FROM dbo.Forms
    GROUP BY PersonId, Type, DueDate
    HAVING COUNT(*) > 1
       AND (COUNT(DISTINCT CONVERT(char(8), CompletedDate, 112)) > 1
            OR (SUM(CASE WHEN CompletedDate IS NOT NULL THEN 1 ELSE 0 END) > 0
                AND SUM(CASE WHEN CompletedDate IS NOT NULL THEN 1 ELSE 0 END) < COUNT(*))
            OR COUNT(DISTINCT CAST(IsCompliant AS int)) > 1)
)
SELECT
    N'4-conflict-rows'  AS Result,
    f.PersonId,
    f.Type,
    f.DueDate,
    f.Id                AS FormId,
    f.IsCompliant,
    f.CompletedDate,
    f.OpenedDate
FROM dbo.Forms AS f
INNER JOIN Conflicted AS c
        ON c.PersonId = f.PersonId
       AND c.Type     = f.Type
       AND c.DueDate  = f.DueDate
ORDER BY f.PersonId, f.DueDate, f.Type, f.Id;
