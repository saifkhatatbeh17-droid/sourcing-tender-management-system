<#
.SYNOPSIS
    Starts the local vLLM OpenAI-compatible server 100% on GPU for a 64 GB RAM + RTX 3070 (8 GB VRAM) machine.

.PARAMETER Profile
    Smart8B (default): Runs Qwen/Qwen3-8B-AWQ (Marlin 4-bit) with 10,240 context (~7.0 GB VRAM, maximum 8B accuracy).
    Fast16k:           Runs Qwen/Qwen3-4B-AWQ (Marlin 4-bit) with full 16,384 context (~5.1 GB VRAM, ideal when desktop/browser apps use >1.2 GB VRAM).

.PARAMETER ApiKey
    Local Bearer token required by the vLLM endpoint (defaults to 'tiryaq-local-vllm').
#>
param(
    [ValidateSet("Smart8B", "Fast16k")]
    [string]$Profile = "Smart8B",
    [string]$ApiKey = "tiryaq-local-vllm"
)

$ErrorActionPreference = "Stop"

Write-Host "Checking NVIDIA GPU availability..." -ForegroundColor Cyan
if (-not (Get-Command nvidia-smi -ErrorAction SilentlyContinue)) {
    throw "nvidia-smi not found. Install the latest NVIDIA Game Ready / Studio Driver with WSL2 CUDA support first."
}
nvidia-smi --query-gpu=name,memory.total,memory.free --format=csv,noheader

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker CLI not found. Install Docker Desktop with the WSL 2 backend enabled."
}

if ($Profile -eq "Fast16k") {
    $env:VLLM_MODEL = "Qwen/Qwen3-4B-AWQ"
    $env:VLLM_MAX_LEN = "16384"
    $env:VLLM_GPU_MEM = "0.86"
} else {
    $env:VLLM_MODEL = "Qwen/Qwen3-8B-AWQ"
    $env:VLLM_MAX_LEN = "10240"
    $env:VLLM_GPU_MEM = "0.88"
}
$env:VLLM_API_KEY = $ApiKey

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    Write-Host "Starting vLLM ($($env:VLLM_MODEL), thinking=OFF, 100% GPU) on http://localhost:8000/v1 ..." -ForegroundColor Green
    docker compose -f docker-compose.vllm.yml up -d

    Write-Host "Waiting for vLLM /v1/models readiness (first run downloads weights into ~/.cache/huggingface)..." -ForegroundColor Yellow
    $headers = @{ Authorization = "Bearer $ApiKey" }
    for ($i = 0; $i -lt 90; $i++) {
        try {
            $res = Invoke-RestMethod -Uri "http://localhost:8000/v1/models" -Headers $headers -TimeoutSec 3
            if ($res.data) {
                Write-Host "vLLM is LIVE on GPU! Active model: $($res.data[0].id)" -ForegroundColor Green
                return
            }
        } catch {
            Start-Sleep -Seconds 4
        }
    }
    Write-Warning "vLLM is still downloading or initializing. Follow progress with: docker logs -f tiryaq-vllm"
} finally {
    Pop-Location
}
