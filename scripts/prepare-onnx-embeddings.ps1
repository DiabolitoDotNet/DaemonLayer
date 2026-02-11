[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$Model = "sentence-transformers/all-MiniLM-L6-v2",

    [Parameter(Mandatory = $false)]
    [string]$OutputDir = "models/sentence-transformers",

    [Parameter(Mandatory = $false)]
    [string]$VenvDir = "artifacts/tools/onnx-embeddings-venv",

    [Parameter(Mandatory = $false)]
    [switch]$Force,

    [Parameter(Mandatory = $false)]
    [switch]$RecreateVenv
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Command([string]$Name) {
    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -eq $cmd) {
        throw "Required command not found in PATH: $Name"
    }
}

function Ensure-Directory([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path | Out-Null
    }
}

function Remove-DirectoryIfPresent([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

Assert-Command "python"

$repoRoot = (Resolve-Path -LiteralPath ".").Path
$outputPath = Join-Path $repoRoot $OutputDir
$venvPath = Join-Path $repoRoot $VenvDir

Ensure-Directory (Split-Path -Parent $outputPath)
Ensure-Directory (Split-Path -Parent $venvPath)
Ensure-Directory $outputPath

if ($Force.IsPresent) {
    Remove-DirectoryIfPresent $outputPath
    Ensure-Directory $outputPath
}

if ($RecreateVenv.IsPresent) {
    Remove-DirectoryIfPresent $venvPath
}

if (-not (Test-Path -LiteralPath $venvPath)) {
    Write-Host "Creating venv at $venvPath" -ForegroundColor Cyan
    # Avoid relying on global pip state; venv will bootstrap pip via ensurepip.
    python -m venv $venvPath
}

$pythonExe = Join-Path $venvPath "Scripts/python.exe"
if (-not (Test-Path -LiteralPath $pythonExe)) {
    throw "Python venv executable not found: $pythonExe (try re-running with -RecreateVenv)"
}

Write-Host "Installing Python dependencies (optimum export)" -ForegroundColor Cyan
& $pythonExe -m pip install --upgrade pip

# NOTE:
# Optimum's CLI surface changes across versions. The most reliable approach is to use
# the Python API: ORTModelForFeatureExtraction.from_pretrained(..., export=True).
# This requires the onnxruntime integration extra.
& $pythonExe -m pip install "optimum[onnxruntime]" transformers onnxruntime

Write-Host "Exporting ONNX model '$Model' to '$outputPath' (Python API)" -ForegroundColor Cyan

$toolsDir = Join-Path $repoRoot "artifacts/tools"
Ensure-Directory $toolsDir

$exportPy = Join-Path $toolsDir "export_sentence_transformer_onnx.py"

$py = @'
import argparse
import os

from transformers import AutoTokenizer
from optimum.onnxruntime import ORTModelForFeatureExtraction


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", required=True)
    ap.add_argument("--output", required=True)
    args = ap.parse_args()

    os.makedirs(args.output, exist_ok=True)

    # Export ONNX model
    model = ORTModelForFeatureExtraction.from_pretrained(args.model, export=True)
    model.save_pretrained(args.output)

    # Save tokenizer (fast tokenizers will produce tokenizer.json)
    tokenizer = AutoTokenizer.from_pretrained(args.model, use_fast=True)
    tokenizer.save_pretrained(args.output)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
'@

Set-Content -LiteralPath $exportPy -Value $py -Encoding UTF8

& $pythonExe $exportPy --model $Model --output $outputPath

$modelFile = Join-Path $outputPath "model.onnx"
$tokenizerFile = Join-Path $outputPath "tokenizer.json"

if (-not (Test-Path -LiteralPath $modelFile)) {
    throw "Export completed but model file not found: $modelFile"
}

if (-not (Test-Path -LiteralPath $tokenizerFile)) {
    Write-Warning "Tokenizer file not found at $tokenizerFile. Some exports write tokenizer files under different names; see models/README.md for expected structure."
}

Write-Host "Done." -ForegroundColor Green
Write-Host "Expected files:" -ForegroundColor DarkGray
Write-Host " - $modelFile" -ForegroundColor DarkGray
Write-Host " - $tokenizerFile" -ForegroundColor DarkGray
