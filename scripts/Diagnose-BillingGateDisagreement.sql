/*
================================================================================
  Diagnose: checkbox says complete, billing gate says incomplete
================================================================================

  WHY THIS EXISTS
  ---------------
  The compliance checkbox, the caseload matrix, the task board and the overdue
  events all read Form.IsCompliant. BillingComplianceGate reads ONLY
  Form.CompletedDate -- Person.EvaluateComplianceGate projects the snapshot as
  (Type, DueDate, CompletedDate) and never passes IsCompliant at all.

  Form.MarkComplete/Reset keep the two in step, and PersonSaveRules checks the
  invariant, but ONLY for new forms at person creation (Id == 0). Nothing checks
  it on update, and there is no CHECK constraint. So a row can hold a state where
  the checkbox reads "done" and the gate reads "incomplete", and it survives a
  restart because the row itself is the disagreement.

  This script finds those rows. It is READ ONLY -- no INSERT, UPDATE, DELETE or
  DDL anywhere in it.

  It returns NO names, narratives, dates of birth or identifiers -- only
  PersonId, form type, and the three date/flag columns the gate depends on. Safe
  to paste the output back into a work log or a ticket.

  HOW TO RUN
  ----------
  On the login that owns the real database, from the repo root:

      sqlcmd -S "(localdb)\MSSQLLocalDB" -d SatiProduction -E ^
             -i scripts\Diagnose-BillingGateDisagreement.sql

  Or open it in SSMS against SatiProduction and press F5.

  Confirm result set 0 first: if Forms is near zero you are attached to the wrong
  instance and everything below is meaningless.

  READING THE OUTPUT
  ------------------
  Result 1 non-empty  -> shape A or B. One row, IsCompliant=1 with a NULL or
                         future CompletedDate. The checkbox and the gate are
                         reading the same row and disagreeing about it.
  Result 2 non-empty  -> shape C. Duplicate form rows for one person/type/due
                         date: GetCurrentCycleForm takes OrderByDescending(DueDate)
                         .FirstOrDefault() and sees one, while the gate iterates
                         all of Person.Forms and sees both.
  Result 3            -> exactly what the gate is complaining about, and result 4
                         is the full form list it iterated to get there.

  If results 1 and 2 are both empty but result 3 has a row, then the running app
  is not reading this database -- check whether the session is Demo, which routes
  through the API to SatiDemo and would never touch local storage.
================================================================================
*/

SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

DECLARE @Today date = CAST(GETDATE() AS date);

/* The document types in BillingComplianceGate.DefaultRequirements. An agency
   that has narrowed BillingComplianceRequirements in Settings gates on fewer
   than these; widen or trim this list to match if that is the case. */
DECLARE @Gated TABLE (Type nvarchar(64) PRIMARY KEY);
INSERT INTO @Gated (Type) VALUES
    (N'Q1R'), (N'Q2R'), (N'Q3R'), (N'Q4R'),
    (N'PCP'), (N'ComprehensiveAssessment'),
    (N'Reclassification'), (N'SafetyPlan');

--------------------------------------------------------------------------------
-- 0. Sanity: which database is this, and does it actually hold data?
--------------------------------------------------------------------------------
SELECT
    N'0-connection'                     AS Result,
    DB_NAME()                           AS DatabaseName,
    @@SERVERNAME                        AS ServerName,
    @Today                              AS TodayUtcLocal,
    (SELECT COUNT(*) FROM dbo.People)   AS PeopleRows,
    (SELECT COUNT(*) FROM dbo.Forms)    AS FormRows;

--------------------------------------------------------------------------------
-- 1. Shapes A and B: rows where IsCompliant and CompletedDate disagree.
--    A = CompletedDate NULL. B = CompletedDate in the future, which
--    BillingComplianceGate.IsIncompleteAndOverdue also treats as incomplete
--    (completedDate.Value.Date > asOfDate.Date).
--------------------------------------------------------------------------------
SELECT
    N'1-divergent-row'  AS Result,
    CASE
        WHEN f.CompletedDate IS NULL THEN N'A: compliant, no completion date'
        ELSE N'B: compliant, completion date in the future'
    END                 AS Shape,
    f.Id                AS FormId,
    f.PersonId,
    f.Type,
    f.DueDate,
    f.IsCompliant,
    f.CompletedDate
FROM dbo.Forms AS f
WHERE f.IsCompliant = 1
  AND (f.CompletedDate IS NULL OR CAST(f.CompletedDate AS date) > @Today)
ORDER BY f.PersonId, f.DueDate, f.Type;

--------------------------------------------------------------------------------
-- 2. Shape C: more than one form row for the same person, type and due date.
--------------------------------------------------------------------------------
SELECT
    N'2-duplicate-rows' AS Result,
    f.PersonId,
    f.Type,
    f.DueDate,
    COUNT(*)            AS RowCountForKey
FROM dbo.Forms AS f
GROUP BY f.PersonId, f.Type, f.DueDate
HAVING COUNT(*) > 1
ORDER BY f.PersonId, f.DueDate, f.Type;

--------------------------------------------------------------------------------
-- 3. Every row the gate is currently blocking on, and whether the checkbox for
--    that same row would render checked. A row with CheckboxWouldShow = N'checked'
--    IS the reported bug: one record, two readers, opposite answers.
--
--    This mirrors BillingComplianceGate.IsIncompleteAndOverdue:
--        dueDate.Date < asOfDate.Date
--        AND (completedDate IS NULL OR completedDate.Date > asOfDate.Date)
--------------------------------------------------------------------------------
SELECT
    N'3-gate-blocking'  AS Result,
    f.Id                AS FormId,
    f.PersonId,
    f.Type,
    f.DueDate,
    f.IsCompliant,
    f.CompletedDate,
    CASE WHEN f.IsCompliant = 1 THEN N'checked -- DISAGREEMENT'
         ELSE N'unchecked -- consistent'
    END                 AS CheckboxWouldShow
FROM dbo.Forms AS f
WHERE f.Type IN (SELECT Type FROM @Gated)
  AND CAST(f.DueDate AS date) < @Today
  AND (f.CompletedDate IS NULL OR CAST(f.CompletedDate AS date) > @Today)
ORDER BY f.PersonId, f.DueDate, f.Type;

--------------------------------------------------------------------------------
-- 4. Full form list for every person appearing in result 3 -- the exact
--    collection Person.EvaluateComplianceGate iterates, plus the EffectiveDate
--    the cycle window is derived from. If result 3 flagged a Q1R the screen
--    shows as checked, the answer is in this person's rows.
--------------------------------------------------------------------------------
SELECT
    N'4-person-forms'   AS Result,
    f.PersonId,
    p.EffectiveDate,
    f.Id                AS FormId,
    f.Type,
    f.DueDate,
    f.IsCompliant,
    f.CompletedDate,
    f.OpenedDate
FROM dbo.Forms AS f
INNER JOIN dbo.People AS p
        ON p.Id = f.PersonId
WHERE f.PersonId IN (
        SELECT b.PersonId
        FROM dbo.Forms AS b
        WHERE b.Type IN (SELECT Type FROM @Gated)
          AND CAST(b.DueDate AS date) < @Today
          AND (b.CompletedDate IS NULL OR CAST(b.CompletedDate AS date) > @Today)
      )
ORDER BY f.PersonId, f.DueDate, f.Type;
