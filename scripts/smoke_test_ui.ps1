param(
    [string]$ExePath = "src/Subviora.Desktop/bin/Release/net10.0-windows10.0.19041.0/Subviora.exe"
)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$fullExePath = [System.IO.Path]::GetFullPath($ExePath)
if (-not (Test-Path $fullExePath)) {
    Write-Error "Executable not found at: $fullExePath"
    exit 1
}

# Ensure no existing instance is running
Get-Process Subviora -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Write-Host "Launching Subviora from: $fullExePath"
$proc = Start-Process -FilePath $fullExePath -PassThru
if ($null -eq $proc) {
    Write-Error "Failed to start process."
    exit 1
}

try {
    # Wait for the main window
    $timeoutSeconds = 12
    $elapsed = 0
    $window = $null

    while ($elapsed -lt $timeoutSeconds) {
        if ($proc.HasExited) {
            Write-Error "Subviora process terminated unexpectedly with exit code: $($proc.ExitCode)"
            exit 2
        }

        $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "Subviora")
        $window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
        if ($null -ne $window) {
            break
        }
        Start-Sleep -Milliseconds 250
        $elapsed += 0.25
    }

    if ($null -eq $window) {
        Write-Error "Could not find 'Subviora' window within timeout."
        exit 3
    }

    Write-Host "Subviora window detected successfully (Process ID: $($proc.Id))"
    Start-Sleep -Milliseconds 500

    # Pages to test
    $pages = @("Oyun Profilleri", "Yakalama & OCR", "Ayarlar", "Kontrol Paneli")
    $iterations = 3

    for ($i = 1; $i -le $iterations; $i++) {
        Write-Host "--- Starting Navigation Loop $i of $iterations ---"
        foreach ($pageName in $pages) {
            if ($proc.HasExited) {
                Write-Error "Process crashed while navigating to '$pageName'! ExitCode: $($proc.ExitCode)"
                exit 4
            }

            Write-Host "Navigating to: '$pageName'..."
            $itemCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $pageName)
            $navItem = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $itemCond)

            if ($null -eq $navItem) {
                Write-Error "Could not find navigation element: '$pageName'"
                exit 5
            }

            $selectPattern = $null
            if ($navItem.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$selectPattern)) {
                $selectPattern.Select()
            } else {
                # Fallback to InvokePattern if available
                $invokePattern = $null
                if ($navItem.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$invokePattern)) {
                    $invokePattern.Invoke()
                } else {
                    Write-Error "Navigation element '$pageName' does not support SelectionItemPattern or InvokePattern."
                    exit 6
                }
            }

            Start-Sleep -Milliseconds 600

            if ($proc.HasExited) {
                Write-Error "CRASH DETECTED after navigating to '$pageName'! Exit code: $($proc.ExitCode)"
                exit 7
            }
            Write-Host "Successfully rendered '$pageName' without crash."
        }
    }

    Write-Host "All navigation cycles completed successfully with 0 crashes!" -ForegroundColor Green

    # Close window gracefully
    $closePattern = $null
    if ($window.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern, [ref]$closePattern)) {
        $closePattern.Close()
        Write-Host "Window close requested via WindowPattern."
    } else {
        $proc.CloseMainWindow()
    }

    $proc.WaitForExit(5000)
    if (-not $proc.HasExited) {
        $proc.Kill()
    }

    Write-Host "Subviora shut down cleanly with exit code: $($proc.ExitCode)"
}
finally {
    if (-not $proc.HasExited) {
        $proc.Kill()
    }
}
