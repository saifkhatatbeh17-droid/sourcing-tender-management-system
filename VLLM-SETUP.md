# vLLM & AI Setup Guide

Tiryaq Pharma’s AI capabilities—role-scoped workspace chat (`AiChatClient`), AI Intelligence Hub (`AiIntelligenceService`), tender bid comparison, top-three committee narratives, manager referral summaries, and Top Committee importance assessments (`BidRecommendationService`)—use a native **vLLM OpenAI-compatible `/v1/chat/completions` client** (`Services/VllmSettings.cs`).

---

## 1. Why vLLM?

**vLLM** is a high-throughput, GPU-native open-source LLM inference and serving engine originally developed at UC Berkeley:

| Capability | vLLM (`/v1/chat/completions`) |
|---|---|
| **GPU KV-Cache Memory** | **PagedAttention** packs KV-cache in non-contiguous GPU pages (<4% waste), keeping 100% of execution in VRAM |
| **4-Bit Ampere Kernels** | **Marlin (`awq_marlin`) Tensor-Core kernels** purpose-built for NVIDIA Ampere GPUs (RTX 30-series) |
| **Repeated System Prompts** | **Automatic Prefix Caching (`--enable-prefix-caching`)** caches Tiryaq’s evaluation and workspace chat system prompts in VRAM |
| **Structured JSON Schemas** | **XGrammar Guided Decoding** (`response_format: json_schema`) compiles schemas to zero-overhead token masks |
| **API Standard** | **Standard OpenAI `/v1/chat/completions` + Bearer API Key**—identical across local GPU vLLM and OpenAI-compatible cloud providers |

---

## 2. Recommended Model & Hardware Tuning (64 GB RAM + RTX 3070 8 GB VRAM)

For a machine with **64 GB System RAM** and an **NVIDIA GeForce RTX 3070 (8 GB GDDR6, Ampere SM 8.6)**:

- **Primary Model (`Smart8B` default):** **`Qwen/Qwen3-8B-AWQ`**
  - **Why this model:** Qwen3-8B delivers strong instruction adherence, numerical faithfulness, and strict JSON schema compliance, while 4-bit AWQ (`awq_marlin`) compresses weights to `~5.5 GB` so **100% of the model stays in the RTX 3070’s 8 GB VRAM** (zero CPU layer offload).
  - **Why thinking is turned OFF (`enable_thinking: false`):** By default, Qwen3 generates hundreds of hidden `<think>...</think>` reasoning tokens before answering. Disabling thinking both on the vLLM server (`--default-chat-template-kwargs '{"enable_thinking":false}'`) and per C# request (`chat_template_kwargs.enable_thinking = false`) makes JSON generation start on the very first token (**~65–85 tokens/sec**).
  - **Important Ampere SM 8.6 Note:** `--kv-cache-dtype fp8` requires Ada Lovelace (`SM 8.9+`, RTX 40-series) and is **intentionally omitted** so vLLM runs cleanly on the RTX 3070 using native `half` (`FP16`) PagedAttention + `--enforce-eager` (which saves `~0.6 GB` of CUDA graph memory).
- **High-Headroom Alternative (`Fast16k` profile):** **`Qwen/Qwen3-4B-AWQ`**
  - Uses only `~2.5 GB` for weights (`~5.1 GB` total with a full `16,384`-token KV cache), ideal if Windows 11 desktop apps, multiple monitors, or heavy browser tabs are already consuming more than `1.2 GB` of the RTX 3070’s 8 GB VRAM.

---

## 3. Step-by-Step Setup After Cloning From GitHub

Clone the repository and restore .NET dependencies as usual:

```powershell
git clone https://github.com/saifkhatatbeh17-droid/sourcing-tender-management-system.git
cd sourcing-tender-management-system
dotnet tool restore
dotnet restore
dotnet ef database update
```

Then choose **Option A (Local RTX 3070 GPU via vLLM)** or **Option B (Cloud API Key — No Local GPU Needed)**.

---

### Option A: Run vLLM 100% on Local GPU (64 GB RAM + RTX 3070 8 GB)

> **Why Docker / WSL 2?** vLLM runs natively on Linux CUDA kernels. On Windows 11, running the official `vllm/vllm-openai` container through **WSL 2 + Docker Desktop** gives direct, near-bare-metal RTX 3070 GPU passthrough while your **64 GB System RAM** caches model weights in memory for fast restarts.

#### Step 1 — Install GPU & Container Prerequisites
1. **NVIDIA Driver:** Install the latest NVIDIA Game Ready or Studio Driver for your RTX 3070 (includes WSL 2 CUDA support automatically). Verify in PowerShell:
   ```powershell
   nvidia-smi
   ```
2. **WSL 2:** Open PowerShell as Administrator and ensure WSL 2 is installed:
   ```powershell
   wsl --install
   ```
   *(Reboot Windows if installing WSL 2 for the first time.)*
3. **Docker Desktop:** Install [Docker Desktop for Windows](https://www.docker.com/products/docker-desktop/) and ensure **Settings → General → Use the WSL 2 based engine** is checked.

#### Step 2 — Start the vLLM GPU Server
From the project root, run the included launcher script (first launch downloads `Qwen/Qwen3-8B-AWQ` once into `~/.cache/huggingface`):

```powershell
.\scripts\start-vllm-3070.ps1
```

- If Windows desktop/browser apps are holding >1.2 GB of your 8 GB VRAM and you need full `16,384`-token context headroom, launch the `Fast16k` profile instead:
  ```powershell
  .\scripts\start-vllm-3070.ps1 -Profile Fast16k
  ```
- Confirm vLLM is running 100% in GPU VRAM:
  ```powershell
  nvidia-smi
  docker logs --tail 30 tiryaq-vllm
  ```

#### Step 3 — Configure `appsettings.Local.json` for Local vLLM
Create or update `appsettings.Local.json` in the project root (ignored by Git):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=SourcingTenderManagementCodeFirst;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "Vllm": {
    "BaseUrl": "http://localhost:8000/v1",
    "Model": "Qwen/Qwen3-8B-AWQ",
    "ApiKey": "tiryaq-local-vllm",
    "EnableThinking": false
  }
}
```
*(If you started `-Profile Fast16k`, set `"Model": "Qwen/Qwen3-4B-AWQ"`.)*

#### Step 4 — Run Tiryaq
```powershell
dotnet run --launch-profile https
```
Open `https://localhost:7130`.

---

### Option B: Run With an API Key (Zero Local Download — Ideal for Slower PCs)

Because `Services/VllmSettings.cs` speaks the standard OpenAI `/v1/chat/completions` protocol with Bearer authentication, slower laptops or machines without an NVIDIA GPU do **not** need to download Docker or model weights.

Create or edit `appsettings.Local.json` (already listed in `.gitignore` so your API key is never committed) using any OpenAI-compatible provider:

#### Example 1 — Groq (Ultra-fast LPU inference, supports Qwen3 with thinking off)
```json
{
  "Vllm": {
    "BaseUrl": "https://api.groq.com/openai/v1",
    "Model": "qwen/qwen3.8-27b",
    "ApiKey": "gsk_YOUR_GROQ_API_KEY",
    "EnableThinking": false
  }
}
```

#### Example 2 — OpenRouter (Access Qwen3-235B / Qwen3-32B / Llama 3.3 with one key)
```json
{
  "Vllm": {
    "BaseUrl": "https://openrouter.ai/api/v1",
    "Model": "qwen/qwen3-32b",
    "ApiKey": "sk-or-v1-YOUR_OPENROUTER_KEY",
    "EnableThinking": false
  }
}
```

#### Example 3 — Together AI / DeepInfra (Hosted vLLM-class endpoints)
```json
{
  "Vllm": {
    "BaseUrl": "https://api.together.xyz/v1",
    "Model": "Qwen/Qwen3-235B-A22B-Instruct-2507-tput",
    "ApiKey": "YOUR_TOGETHER_API_KEY",
    "EnableThinking": false
  }
}
```

#### Example 4 — Google Gemini (OpenAI-Compatible Endpoint)
```json
{
  "Vllm": {
    "BaseUrl": "https://generativelanguage.googleapis.com/v1beta/openai",
    "Model": "gemini-2.5-flash",
    "ApiKey": "AIzaSy_YOUR_GEMINI_API_KEY",
    "EnableThinking": false
  }
}
```

> **Environment Variable Alternative:** Instead of storing the key in `appsettings.Local.json`, you can set `VLLM_API_KEY`, `VLLM_BASE_URL`, and `VLLM_MODEL` in your shell or OS environment variables.

---

## 4. Stopping or Updating the Local vLLM Container

```powershell
# Stop vLLM and free all 8 GB VRAM immediately
docker compose -f docker-compose.vllm.yml down

# Pull the latest vLLM engine image
docker compose -f docker-compose.vllm.yml pull
```
