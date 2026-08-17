# Compare the EF model (SatiContextModelSnapshot.cs) against the schema a database
# actually has. Any column the model expects but the database lacks is drift: it works on
# a hand-patched database and fails on a fresh one.
#
# IMPORTANT: point this at a database built from scratch by running the migration chain,
# not at SatiProduction or SatiDemo. Those carry columns applied outside the chain over
# time, which is exactly what hides the gap this script exists to find. Notes.Minutes and
# Notes.StartTime sat in that blind spot on every working database while no migration
# created them.
param(
    [string]$SnapshotPath = (Join-Path $PSScriptRoot '..\Migrations\SatiContextModelSnapshot.cs'),
    [string]$ConnectionString = 'Server=(localdb)\MSSQLLocalDB;Database=SatiSchemaCheck;Trusted_Connection=True;TrustServerCertificate=True'
)

$snapshot = $SnapshotPath
$cs = $ConnectionString

$text = Get-Content $snapshot -Raw

# Split into per-entity blocks: modelBuilder.Entity("Type", b => { ... });
$expected = @{}   # table -> [string[]] columns
# Slice from each modelBuilder.Entity( to the next one; brace matching is brittle here
# because the snapshot nests lambdas at varying indentation.
$starts = [regex]::Matches($text, 'modelBuilder\.Entity\("([^"]+)"')
for ($i = 0; $i -lt $starts.Count; $i++) {
    $from = $starts[$i].Index
    $to = if ($i + 1 -lt $starts.Count) { $starts[$i + 1].Index } else { $text.Length }
    $body = $text.Substring($from, $to - $from)
    $tbl = [regex]::Match($body, 'b\.ToTable\("([^"]+)"')
    if (-not $tbl.Success) { continue }   # relationship-only pass, no table
    $table = $tbl.Groups[1].Value
    if (-not $expected.ContainsKey($table)) { $expected[$table] = New-Object System.Collections.Generic.List[string] }
    foreach ($p in [regex]::Matches($body, 'b\.Property<[^>]+>\("([^"]+)"\)')) {
        $null = $expected[$table].Add($p.Groups[1].Value)
    }
}

# Actual schema.
$actual = @{}
$conn = New-Object System.Data.SqlClient.SqlConnection $cs
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS"
$r = $cmd.ExecuteReader()
while ($r.Read()) {
    $t = $r.GetString(0)
    if (-not $actual.ContainsKey($t)) { $actual[$t] = New-Object System.Collections.Generic.List[string] }
    $null = $actual[$t].Add($r.GetString(1))
}
$r.Close()
$conn.Close()

$missing = @()
foreach ($table in ($expected.Keys | Sort-Object)) {
    if (-not $actual.ContainsKey($table)) {
        $missing += [pscustomobject]@{ Table = $table; Column = '(entire table)'; }
        continue
    }
    foreach ($col in $expected[$table]) {
        if ($actual[$table] -notcontains $col) {
            $missing += [pscustomobject]@{ Table = $table; Column = $col }
        }
    }
}

"Entities with tables: $($expected.Count)"
"Columns expected:     $(($expected.Values | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum)"
"MISSING from database: $($missing.Count)"
if ($missing) { $missing | Format-Table -AutoSize }
