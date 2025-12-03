param(
    [switch]$Run,      # Run the solution after building (starts BaGet service)
    [switch]$Publish   # Clean, restore, build (Release), and publish the solution
)

# Script configuration - script is in root, solution is in root
# Usage:
#   .\build.ps1              - Clean, restore, and build (Debug)
#   .\build.ps1 -Run         - Clean, restore, build (Debug), and run BaGet service
#   .\build.ps1 -Publish     - Clean, restore, build (Release), and publish to Publish folder
$SolutionPath = Join-Path $PSScriptRoot "BaGet.sln"
$ProjectPath = Join-Path $PSScriptRoot "src\BaGet\BaGet.csproj"
$PublishPath = Join-Path $PSScriptRoot "Publish"

# Determine build configuration
$Configuration = if ($Publish) { "Release" } else { "Debug" }

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "BaGet Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Function to check if a process is running
function Test-ProcessRunning {
    param([string]$ProcessName)
    $processes = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
    return $null -ne $processes -and $processes.Count -gt 0
}

# Function to close processes
function Stop-ProjectProcesses {
    Write-Host "Checking for running processes..." -ForegroundColor Yellow
    
    $processesToCheck = @(
        "dotnet"
    )
    
    $foundProcesses = $false
    
    foreach ($processName in $processesToCheck) {
        if (Test-ProcessRunning $processName) {
            $foundProcesses = $true
            try {
                Get-Process -Name $processName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
                Start-Sleep -Seconds 2
            }
            catch {
                Write-Host "  Warning: Could not close all $processName processes: $_" -ForegroundColor Red
            }
        }
    }
    
    # Check for processes using files in the solution directory
    Write-Host "Checking for processes locking solution files..." -ForegroundColor Yellow
    try {
        $solutionDir = Split-Path $SolutionPath -Parent
        $lockedFiles = Get-ChildItem -Path $solutionDir -Recurse -File -ErrorAction SilentlyContinue | 
            Where-Object {
                try {
                    [System.IO.File]::OpenWrite($_.FullName).Close()
                    $false
                }
                catch {
                    $true
                }
            }
        
        if ($lockedFiles) {
            Write-Host "  Warning: Some files may be locked by other processes." -ForegroundColor Yellow
        }
    }
    catch {
        # Ignore errors in file locking check
    }
    
    if (-not $foundProcesses) {
        Write-Host "No running processes found." -ForegroundColor Green
    }
    
    Write-Host ""
}

# Step 1: Close running processes
Stop-ProjectProcesses

# Step 2: Clean the solution
Write-Host "Cleaning solution..." -ForegroundColor Cyan
try {
    # Suppress verbose output from dotnet clean
    $originalTerminalLogger = $env:MSBUILDTERMINALLOGGER
    $env:MSBUILDTERMINALLOGGER = "off"
    
    $processInfo = New-Object System.Diagnostics.ProcessStartInfo
    $processInfo.FileName = "dotnet"
    $processInfo.Arguments = "clean `"$SolutionPath`" --verbosity quiet /nologo /noconsolelogger"
    $processInfo.UseShellExecute = $false
    $processInfo.RedirectStandardOutput = $true
    $processInfo.RedirectStandardError = $true
    $processInfo.CreateNoWindow = $true
    $processInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
    
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $processInfo
    $null = $process.Start()
    
    # Read and discard all output
    $null = $process.StandardOutput.ReadToEnd()
    $null = $process.StandardError.ReadToEnd()
    $null = $process.WaitForExit()
    
    # Restore environment
    if ($originalTerminalLogger) {
        $env:MSBUILDTERMINALLOGGER = $originalTerminalLogger
    } else {
        Remove-Item Env:\MSBUILDTERMINALLOGGER -ErrorAction SilentlyContinue
    }
    
    if ($process.ExitCode -eq 0) {
        Write-Host "  Solution cleaned successfully." -ForegroundColor Green
    }
    else {
        Write-Host "  Warning: Clean completed with exit code $($process.ExitCode)" -ForegroundColor Yellow
    }
}
catch {
    Write-Host "  Error cleaning solution: $_" -ForegroundColor Red
}
Write-Host ""

# Step 3: Remove bin and obj directories
Write-Host "Removing bin and obj directories..." -ForegroundColor Cyan
$solutionDir = Split-Path $SolutionPath -Parent
$removedCount = 0

try {
    # Remove bin directories
    $binDirs = Get-ChildItem -Path $solutionDir -Directory -Recurse -Filter "bin" -ErrorAction SilentlyContinue
    foreach ($dir in $binDirs) {
        try {
            Remove-Item -Path $dir.FullName -Recurse -Force -ErrorAction Stop
            $removedCount++
        }
        catch {
            Write-Host "  Warning: Could not remove $($dir.FullName): $_" -ForegroundColor Yellow
        }
    }
    
    # Remove obj directories
    $objDirs = Get-ChildItem -Path $solutionDir -Directory -Recurse -Filter "obj" -ErrorAction SilentlyContinue
    foreach ($dir in $objDirs) {
        try {
            Remove-Item -Path $dir.FullName -Recurse -Force -ErrorAction Stop
            $removedCount++
        }
        catch {
            Write-Host "  Warning: Could not remove $($dir.FullName): $_" -ForegroundColor Yellow
        }
    }
    
    if (Test-Path "$($solutionDir)\packages") {
        Remove-Item -Path "$($solutionDir)\packages" -Recurse -Force -ErrorAction Stop
        $removedCount++
    }

    if (Test-Path $PublishPath) {
        Remove-Item -Path $PublishPath -Recurse -Force -ErrorAction Stop
        $removedCount++
    }

    if ($removedCount -gt 0) {
        Write-Host "  Removed $removedCount directories." -ForegroundColor Green
    }
    else {
        Write-Host "  No bin/obj directories found to remove." -ForegroundColor Green
    }
}
catch {
    Write-Host "  Error removing directories: $_" -ForegroundColor Red
}
Write-Host ""

# Step 4: Restore NuGet packages
Write-Host "Restoring NuGet packages..." -ForegroundColor Cyan
try {
    $restoreResult = & dotnet restore $SolutionPath --verbosity quiet
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  NuGet packages restored successfully." -ForegroundColor Green
    }
    else {
        Write-Host "  Error: NuGet restore failed with exit code $LASTEXITCODE" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}
catch {
    Write-Host "  Error restoring packages: $_" -ForegroundColor Red
    exit 1
}
Write-Host ""

# Step 5: Build the solution
Write-Host "Building solution..." -ForegroundColor Cyan
try {
    $buildResult = & dotnet build $SolutionPath --configuration $Configuration --no-restore --verbosity quiet
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  Solution built successfully!" -ForegroundColor Green
    }
    else {
        Write-Host "  Error: Build failed with exit code $LASTEXITCODE" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}
catch {
    Write-Host "  Error building solution: $_" -ForegroundColor Red
    exit 1
}
Write-Host ""

# Step 6: Publish the solution (if requested)
if ($Publish) {
    Write-Host "Publishing solution..." -ForegroundColor Cyan
    
    # Remove existing publish directory if it exists
    if (Test-Path $PublishPath) {
        Write-Host "  Removing existing publish directory..." -ForegroundColor Yellow
        try {
            Remove-Item -Path $PublishPath -Recurse -Force -ErrorAction Stop
            Write-Host "  Existing publish directory removed." -ForegroundColor Green
        }
        catch {
            Write-Host "  Warning: Could not remove existing publish directory: $_" -ForegroundColor Yellow
        }
    }
    
    try {
        Write-Host "  Publishing to: $PublishPath" -ForegroundColor Yellow
        
        # Ensure the publish directory exists
        $null = New-Item -ItemType Directory -Path $PublishPath -Force -ErrorAction SilentlyContinue
        
        # Publish using dotnet publish
        Write-Host "  Publishing BaGet project..." -ForegroundColor Yellow
        $publishResult = & dotnet publish $ProjectPath --configuration $Configuration --output $PublishPath --no-build
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  Solution published successfully!" -ForegroundColor Green
            Write-Host "  Published files are located at: $PublishPath" -ForegroundColor Green
        }
        else {
            Write-Host "  Error: Publish failed with exit code $LASTEXITCODE" -ForegroundColor Red
            exit $LASTEXITCODE
        }
    }
    catch {
        Write-Host "  Error publishing solution: $_" -ForegroundColor Red
        Write-Host "  Stack trace: $($_.ScriptStackTrace)" -ForegroundColor Red
        exit 1
    }
    Write-Host ""
}

# Run the solution (if requested)
if ($Run) {
    Write-Host "Running solution..." -ForegroundColor Cyan
    
    Write-Host "  Starting BaGet service..." -ForegroundColor Yellow
    Write-Host "  Navigate to http://localhost:50561/v3/index.json in your browser" -ForegroundColor Cyan
    Write-Host "  Press Ctrl+C to stop the service" -ForegroundColor Gray
    Write-Host ""
    
    try {
        $projectDir = Split-Path $ProjectPath -Parent
        Set-Location -Path $projectDir
        & dotnet run --urls "http://localhost:50561" --verbosity quiet
    }
    catch {
        Write-Host "  Error running solution: $_" -ForegroundColor Red
        exit 1
    }
    finally {
        Set-Location -Path $PSScriptRoot
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Build script completed!" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
