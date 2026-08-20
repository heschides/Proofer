<#
.SYNOPSIS
    A scratchpad shared between the Windows logins on this machine.

.DESCRIPTION
    Paste on one login, open it on the other. The text lives in one file under
    C:\Users\Public\Documents, which every interactively logged-on account can read
    and write.

    This is not a web page, and the reason is worth knowing rather than discovering.
    A browser cannot do this job:

      * Its storage — localStorage, IndexedDB — belongs to the browser profile, and a
        browser profile belongs to one Windows user. Two logins would each see their
        own empty page.
      * The File System Access API, which could write to a shared folder, is not
        exposed to pages opened from file://. It needs a real http(s) origin, which
        means running a web server just to hold a text box.

    So the page would look right and share nothing. This does the same job with a
    text file, which also means Notepad opens it if this script ever will not.

    DO NOT PASTE CLIENT INFORMATION HERE. C:\Users\Public is readable by every
    account on the machine and carries none of the protections the application does.
    This is for commands, snippets, and notes to yourself.

.EXAMPLE
    ./scripts/Shared-Scratchpad.ps1
#>
param(
    [string]$Path = 'C:\Users\Public\Documents\SatiShared\scratchpad.txt'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework

$directory = Split-Path -Parent $Path
if (-not (Test-Path -LiteralPath $directory)) {
    [void](New-Item -ItemType Directory -Path $directory -Force)
}
if (-not (Test-Path -LiteralPath $Path)) {
    [System.IO.File]::WriteAllText($Path, '', (New-Object System.Text.UTF8Encoding($false)))
}

$xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Shared Scratchpad" Height="560" Width="820"
        WindowStartupLocation="CenterScreen" Background="#F6F7F9">
  <Grid Margin="10">
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto" />
      <RowDefinition Height="*" />
      <RowDefinition Height="Auto" />
    </Grid.RowDefinitions>

    <TextBlock Grid.Row="0" Margin="2,0,2,8" TextWrapping="Wrap" Foreground="#5C6975">
      Shared by every login on this computer. Do not paste client information here.
    </TextBlock>

    <TextBox x:Name="Body" Grid.Row="1"
             AcceptsReturn="True" AcceptsTab="True"
             TextWrapping="NoWrap"
             VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto"
             FontFamily="Cascadia Mono, Consolas" FontSize="13"
             Padding="8" Background="White" BorderBrush="#D3DADF"
             AutomationProperties.Name="Shared scratchpad text" />

    <DockPanel Grid.Row="2" Margin="0,10,0,0" LastChildFill="False">
      <TextBlock x:Name="Status" DockPanel.Dock="Left" VerticalAlignment="Center"
                 Foreground="#5C6975" TextTrimming="CharacterEllipsis" />
      <Button x:Name="Save" DockPanel.Dock="Right" Content="Save" Width="90" Height="28"
              Margin="8,0,0,0" IsDefault="True"
              AutomationProperties.Name="Save the scratchpad" />
      <Button x:Name="Reload" DockPanel.Dock="Right" Content="Reload" Width="90" Height="28"
              AutomationProperties.Name="Reload from disk, discarding unsaved changes" />
    </DockPanel>
  </Grid>
</Window>
'@

$window = [Windows.Markup.XamlReader]::Load((New-Object System.Xml.XmlNodeReader ([xml]$xaml)))
$body = $window.FindName('Body')
$status = $window.FindName('Status')
$saveButton = $window.FindName('Save')
$reloadButton = $window.FindName('Reload')

# The timestamp the loaded text came from. Windows Fast User Switching means both
# logins can be live at once, so "last write wins" could silently discard the other
# session's paste. Comparing against this on save turns that into a question.
$script:loadedStamp = $null

function Read-Scratchpad {
    $body.Text = [System.IO.File]::ReadAllText($Path)
    $script:loadedStamp = (Get-Item -LiteralPath $Path).LastWriteTimeUtc
    $local = (Get-Item -LiteralPath $Path).LastWriteTime
    $status.Text = "Loaded. Last saved $($local.ToString('MMM d, h:mm tt'))."
}

function Write-Scratchpad {
    $currentStamp = (Get-Item -LiteralPath $Path).LastWriteTimeUtc
    if ($currentStamp -ne $script:loadedStamp) {
        $answer = [System.Windows.MessageBox]::Show(
            "The other login saved this scratchpad after you opened it. Saving now replaces what they wrote.`n`nSave anyway?",
            'Changed since you opened it',
            [System.Windows.MessageBoxButton]::YesNo,
            [System.Windows.MessageBoxImage]::Warning)
        if ($answer -ne [System.Windows.MessageBoxResult]::Yes) {
            $status.Text = 'Not saved. Press Reload to see their version first.'
            return
        }
    }

    # No BOM, so Notepad and anything else reading this file see plain text.
    [System.IO.File]::WriteAllText($Path, $body.Text, (New-Object System.Text.UTF8Encoding($false)))
    $script:loadedStamp = (Get-Item -LiteralPath $Path).LastWriteTimeUtc
    $status.Text = "Saved $((Get-Date).ToString('h:mm tt')) as $env:USERNAME."
}

$saveButton.Add_Click({ Write-Scratchpad })
$reloadButton.Add_Click({ Read-Scratchpad })

# Ctrl+S saves without reaching for the mouse, which is the whole point of a
# scratchpad you are pasting into repeatedly.
$window.Add_PreviewKeyDown({
    if ($_.Key -eq 'S' -and ([System.Windows.Input.Keyboard]::Modifiers -band [System.Windows.Input.ModifierKeys]::Control)) {
        Write-Scratchpad
        $_.Handled = $true
    }
})

Read-Scratchpad
$body.Focus()
[void]$window.ShowDialog()
