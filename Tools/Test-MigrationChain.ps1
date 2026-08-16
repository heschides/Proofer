# Replay the EF migration chain symbolically and flag operations that would fail on a
# database built from scratch: adding a column that already exists (SQL 2705), or
# dropping/altering one that does not (SQL 4924/1911).
param(
    [string]$MigrationsPath = (Join-Path $PSScriptRoot '..\Migrations')
)

$dir = $MigrationsPath
$files = Get-ChildItem $dir -Filter *.cs |
    Where-Object { $_.Name -notmatch 'Designer|ModelSnapshot' } |
    Sort-Object Name

$exists = @{}      # "Table.Column" -> $true
$tables = @{}      # "Table" -> $true
$problems = @()

function Note($file, $kind, $target, $detail) {
    $script:problems += [pscustomobject]@{ Migration = $file; Problem = $kind; Target = $target; Detail = $detail }
}

foreach ($f in $files) {
    $text = Get-Content $f.FullName -Raw
    $upIndex = $text.IndexOf('protected override void Up')
    $downIndex = $text.IndexOf('protected override void Down')
    if ($upIndex -lt 0) { continue }
    $up = if ($downIndex -gt $upIndex) { $text.Substring($upIndex, $downIndex - $upIndex) } else { $text.Substring($upIndex) }

    # Strip comments so commented-out operations are not counted as real ones.
    $up = [regex]::Replace($up, '//[^\r\n]*', '')

    # Walk operations in source order so add-then-drop within one migration is handled.
    $ops = [regex]::Matches($up, '(?s)(CreateTable|DropTable|AddColumn<[^>]+>|DropColumn|AlterColumn<[^>]+>|RenameColumn)\s*\((.*?)\);')
    foreach ($op in $ops) {
        $kind = $op.Groups[1].Value
        $body = $op.Groups[2].Value

        if ($kind -eq 'CreateTable') {
            $tblMatch = [regex]::Match($body, 'name:\s*"([^"]+)"')
            if (-not $tblMatch.Success) { continue }
            $tbl = $tblMatch.Groups[1].Value
            if ($tables.ContainsKey($tbl)) { Note $f.Name 'CreateTable on existing table' $tbl '' }
            $tables[$tbl] = $true
            foreach ($colM in [regex]::Matches($body, '(\w+)\s*=\s*table\.Column<')) {
                $exists["$tbl.$($colM.Groups[1].Value)"] = $true
            }
            continue
        }

        $tblMatch = [regex]::Match($body, 'table:\s*"([^"]+)"')
        $nameMatch = [regex]::Match($body, 'name:\s*"([^"]+)"')

        if ($kind -eq 'DropTable') {
            if ($nameMatch.Success) {
                $tbl = $nameMatch.Groups[1].Value
                $tables.Remove($tbl)
                foreach ($k in @($exists.Keys)) { if ($k -like "$tbl.*") { $exists.Remove($k) } }
            }
            continue
        }

        if (-not ($tblMatch.Success -and $nameMatch.Success)) { continue }
        $key = "$($tblMatch.Groups[1].Value).$($nameMatch.Groups[1].Value)"

        switch -Wildcard ($kind) {
            'AddColumn*' {
                if ($exists.ContainsKey($key)) { Note $f.Name 'AddColumn on existing column (SQL 2705)' $key '' }
                $exists[$key] = $true
            }
            'DropColumn' {
                if (-not $exists.ContainsKey($key)) { Note $f.Name 'DropColumn on missing column (SQL 4924)' $key '' }
                $exists.Remove($key)
            }
            'AlterColumn*' {
                if (-not $exists.ContainsKey($key)) { Note $f.Name 'AlterColumn on missing column' $key '' }
            }
            'RenameColumn' {
                $newMatch = [regex]::Match($body, 'newName:\s*"([^"]+)"')
                if ($newMatch.Success) {
                    $exists.Remove($key)
                    $exists["$($tblMatch.Groups[1].Value).$($newMatch.Groups[1].Value)"] = $true
                }
            }
        }
    }
}

"Migrations replayed: $($files.Count)"
"Tables tracked:      $($tables.Count)"
"Columns tracked:     $($exists.Count)"
"Problems found:      $($problems.Count)"
if ($problems) { $problems | Format-List }
