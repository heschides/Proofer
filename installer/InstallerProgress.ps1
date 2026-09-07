function Start-SatiInstallerProgress {
    param(
        [Parameter(Mandatory)]
        [string]$Title,

        [Parameter(Mandatory)]
        [string]$Heading,

        [Parameter(Mandatory)]
        [string]$Detail
    )

    $stateMap = [hashtable]::Synchronized(@{
        Heading = $Heading
        Detail = $Detail
        Close = $false
    })
    $runspace = $null
    $pipeline = $null

    try {
        $runspace = [System.Management.Automation.Runspaces.RunspaceFactory]::CreateRunspace()
        $runspace.ApartmentState = [System.Threading.ApartmentState]::STA
        $runspace.ThreadOptions = [System.Management.Automation.Runspaces.PSThreadOptions]::ReuseThread
        $runspace.Open()

        $pipeline = [PowerShell]::Create()
        $pipeline.Runspace = $runspace
        $pipeline.AddScript({
            param($SharedState, $WindowTitle)

            Add-Type -AssemblyName PresentationFramework, PresentationCore

            $window = [System.Windows.Window]::new()
            $window.Title = $WindowTitle
            $window.Width = 520
            $window.Height = 250
            $window.WindowStartupLocation = [System.Windows.WindowStartupLocation]::CenterScreen
            $window.WindowStyle = [System.Windows.WindowStyle]::None
            $window.ResizeMode = [System.Windows.ResizeMode]::NoResize
            $window.AllowsTransparency = $true
            $window.Background = [System.Windows.Media.Brushes]::Transparent
            $window.ShowInTaskbar = $true
            $window.Topmost = $true
            [System.Windows.Automation.AutomationProperties]::SetName($window, $WindowTitle)

            $border = [System.Windows.Controls.Border]::new()
            $border.CornerRadius = [System.Windows.CornerRadius]::new(18)
            $border.BorderThickness = [System.Windows.Thickness]::new(1)
            $border.BorderBrush = [System.Windows.Media.BrushConverter]::new().ConvertFrom('#D7C7D4')
            $border.Background = [System.Windows.Media.BrushConverter]::new().ConvertFrom('#FFF9F5')
            $border.Padding = [System.Windows.Thickness]::new(34, 26, 34, 26)
            $shadow = [System.Windows.Media.Effects.DropShadowEffect]::new()
            $shadow.BlurRadius = 24
            $shadow.ShadowDepth = 4
            $shadow.Opacity = 0.24
            $border.Effect = $shadow

            $layout = [System.Windows.Controls.Grid]::new()
            $layout.RowDefinitions.Add([System.Windows.Controls.RowDefinition]::new())
            $layout.RowDefinitions.Add([System.Windows.Controls.RowDefinition]::new())
            $layout.RowDefinitions.Add([System.Windows.Controls.RowDefinition]::new())
            $layout.RowDefinitions.Add([System.Windows.Controls.RowDefinition]::new())
            $layout.RowDefinitions[0].Height = [System.Windows.GridLength]::Auto
            $layout.RowDefinitions[1].Height = [System.Windows.GridLength]::Auto
            $layout.RowDefinitions[2].Height = [System.Windows.GridLength]::Auto
            $layout.RowDefinitions[3].Height = [System.Windows.GridLength]::Auto

            $brand = [System.Windows.Controls.TextBlock]::new()
            $brand.Text = 'Sati'
            $brand.FontFamily = [System.Windows.Media.FontFamily]::new('Segoe UI Semibold')
            $brand.FontSize = 30
            $brand.Foreground = [System.Windows.Media.BrushConverter]::new().ConvertFrom('#A11B73')
            $brand.Margin = [System.Windows.Thickness]::new(0, 0, 0, 12)
            [System.Windows.Controls.Grid]::SetRow($brand, 0)

            $headingText = [System.Windows.Controls.TextBlock]::new()
            $headingText.Text = [string]$SharedState.Heading
            $headingText.FontFamily = [System.Windows.Media.FontFamily]::new('Segoe UI Semibold')
            $headingText.FontSize = 18
            $headingText.Foreground = [System.Windows.Media.BrushConverter]::new().ConvertFrom('#312A30')
            $headingText.Margin = [System.Windows.Thickness]::new(0, 0, 0, 6)
            [System.Windows.Automation.AutomationProperties]::SetName($headingText, 'Installation status')
            [System.Windows.Controls.Grid]::SetRow($headingText, 1)

            $detailText = [System.Windows.Controls.TextBlock]::new()
            $detailText.Text = [string]$SharedState.Detail
            $detailText.FontFamily = [System.Windows.Media.FontFamily]::new('Segoe UI')
            $detailText.FontSize = 13
            $detailText.Foreground = [System.Windows.Media.BrushConverter]::new().ConvertFrom('#665B64')
            $detailText.Margin = [System.Windows.Thickness]::new(0, 0, 0, 22)
            [System.Windows.Controls.Grid]::SetRow($detailText, 2)

            $progress = [System.Windows.Controls.ProgressBar]::new()
            $progress.Height = 8
            $progress.IsIndeterminate = $true
            $progress.Foreground = [System.Windows.Media.BrushConverter]::new().ConvertFrom('#A11B73')
            $progress.Background = [System.Windows.Media.BrushConverter]::new().ConvertFrom('#EADCE6')
            [System.Windows.Automation.AutomationProperties]::SetName($progress, 'Sati installation in progress')
            [System.Windows.Controls.Grid]::SetRow($progress, 3)

            $layout.Children.Add($brand) | Out-Null
            $layout.Children.Add($headingText) | Out-Null
            $layout.Children.Add($detailText) | Out-Null
            $layout.Children.Add($progress) | Out-Null
            $border.Child = $layout
            $window.Content = $border

            $timer = [System.Windows.Threading.DispatcherTimer]::new()
            $timer.Interval = [TimeSpan]::FromMilliseconds(100)
            $timer.Add_Tick({
                $headingText.Text = [string]$SharedState.Heading
                $detailText.Text = [string]$SharedState.Detail
                if ([bool]$SharedState.Close) {
                    $timer.Stop()
                    $window.Close()
                }
            })
            $window.Add_ContentRendered({ $timer.Start() })
            $window.Add_Closing({
                param($sender, $eventArgs)
                if (-not [bool]$SharedState.Close) {
                    $eventArgs.Cancel = $true
                }
            })
            [void]$window.ShowDialog()
        }).AddArgument($stateMap).AddArgument($Title) | Out-Null

        $asyncResult = $pipeline.BeginInvoke()
        return [pscustomobject]@{
            StateMap = $stateMap
            Pipeline = $pipeline
            Runspace = $runspace
            AsyncResult = $asyncResult
        }
    }
    catch {
        if ($null -ne $pipeline) { $pipeline.Dispose() }
        if ($null -ne $runspace) { $runspace.Dispose() }
        return $null
    }
}

function Update-SatiInstallerProgress {
    param(
        $ProgressHandle,
        [string]$Heading,
        [string]$Detail
    )

    if ($null -eq $ProgressHandle) { return }
    if (-not [string]::IsNullOrWhiteSpace($Heading)) {
        $ProgressHandle.StateMap.Heading = $Heading
    }
    if (-not [string]::IsNullOrWhiteSpace($Detail)) {
        $ProgressHandle.StateMap.Detail = $Detail
    }
}

function Stop-SatiInstallerProgress {
    param($ProgressHandle)

    if ($null -eq $ProgressHandle) { return }
    try {
        $ProgressHandle.StateMap.Close = $true
        if ($ProgressHandle.AsyncResult.AsyncWaitHandle.WaitOne(5000)) {
            $ProgressHandle.Pipeline.EndInvoke($ProgressHandle.AsyncResult)
        }
        else {
            $ProgressHandle.Pipeline.Stop()
        }
    }
    catch { }
    finally {
        $ProgressHandle.Pipeline.Dispose()
        $ProgressHandle.Runspace.Dispose()
    }
}
