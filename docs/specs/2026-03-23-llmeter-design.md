# LLMeter – Design Spec

## Overview

LLMeter is a self-hosted ASP.NET Core service that aggregates LLM token usage and costs across three providers (Anthropic, OpenRouter, Mistral via LiteLLM) and exposes the data via a REST API. Primary consumer is a StreamDeck integration, but any HTTP client can use it.

## Requirements

- Track tokens in/out, cost in USD, and remaining credits per provider
- Aggregate by day, week, month, year
- Expose simple REST API (no auth, internal network only)
- StreamDeck-friendly compact summary endpoint
- Self-hosted on Proxmox CT (cirrus-pve), deployed via `deploy-service.sh`
- Data sovereignty: Mistral traffic stays local via LiteLLM

## Tech Stack

- **Runtime:** .NET 8 / ASP.NET Core Minimal API
- **Database:** SQLite (via EF Core)
- **Container:** Docker, deployed via `deploy-service.sh --name llmeter --node cirrus-pve`
- **Reverse proxy:** Traefik at `llmeter.home.freaxnx01.ch`

## Architecture

```
┌─────────────────────────────────────────────┐
│              LLMeter (Proxmox CT)           │
│                                             │
│  ┌──────────────┐    ┌───────────────────┐  │
│  │  Background   │    │   REST API        │  │
│  │  Sync Worker  │    │   (Minimal API)   │  │
│  │               │    │                   │  │
│  │  Every 15min: │    │  GET /api/usage   │  │
│  │  - Anthropic  │    │  GET /api/balance │  │
│  │  - OpenRouter │    │  GET /api/summary │  │
│  │  - LiteLLM    │    │                   │  │
│  └──────┬───────┘    └────────┬──────────┘  │
│         │                     │              │
│         └──────┐   ┌─────────┘              │
│                ▼   ▼                        │
│          ┌──────────────┐                   │
│          │   SQLite DB   │                  │
│          └──────────────┘                   │
└─────────────────────────────────────────────┘
         ▲                          │
         │ polls APIs               │ serves data
         ▼                          ▼
   Anthropic Admin API        StreamDeck / Browser
   OpenRouter Credits API     or any HTTP client
   LiteLLM /spend endpoint
```

## Data Model

### UsageRecords

| Column | Type | Description |
|--------|------|-------------|
| Id | int PK | Auto-increment |
| Provider | string | `anthropic`, `openrouter`, `mistral` |
| Model | string | e.g. `claude-opus-4-6`, `mistral-large` |
| InputTokens | long | Tokens in |
| OutputTokens | long | Tokens out |
| CostUsd | decimal | Cost in USD |
| RecordedAt | datetime | Timestamp from provider |
| SyncedAt | datetime | When we fetched it |

Deduplication: unique on `RecordedAt` + `Provider` + `Model`. This is sufficient because usage is aggregated per-model per time bucket — there is only one API key per provider.

### BalanceSnapshots

| Column | Type | Description |
|--------|------|-------------|
| Id | int PK | Auto-increment |
| Provider | string | `anthropic`, `openrouter`, `mistral` |
| TotalCredits | decimal | Total credits loaded |
| TotalUsed | decimal | Total spent |
| Remaining | decimal | Credits left |
| SnapshotAt | datetime | When captured |

### SyncStatus

| Column | Type | Description |
|--------|------|-------------|
| Provider | string PK | `anthropic`, `openrouter`, `mistral` |
| LastSyncedAt | datetime | Last successful sync time |
| LastError | string? | Last error message, null if OK |

### Configuration (appsettings.json)

```json
{
  "SyncIntervalMinutes": 15,
  "Providers": {
    "Anthropic": {
      "AdminApiKey": "${ANTHROPIC_ADMIN_API_KEY}",
      "TotalCredits": 100.00
    },
    "OpenRouter": {
      "ManagementApiKey": "${OPENROUTER_MANAGEMENT_API_KEY}"
    },
    "Mistral": {
      "LiteLlmBaseUrl": "http://litellm.local:4000",
      "Budget": 20.00
    }
  }
}
```

API keys injected via environment variables in docker-compose.

## API Endpoints

### GET /api/usage?provider={p}&period={day|week|month|year}

Returns aggregated tokens in/out and cost per provider/model for the given period. Periods use calendar boundaries: `day` = today, `week` = current ISO week (Monday–Sunday), `month` = current calendar month, `year` = current calendar year. All times in UTC.

```json
{
  "period": "day",
  "from": "2026-03-23T00:00:00Z",
  "to": "2026-03-24T00:00:00Z",
  "providers": [
    {
      "provider": "anthropic",
      "inputTokens": 125000,
      "outputTokens": 48000,
      "costUsd": 3.42,
      "models": [
        { "model": "claude-opus-4-6", "inputTokens": 100000, "outputTokens": 40000, "costUsd": 2.80 },
        { "model": "claude-sonnet-4-6", "inputTokens": 25000, "outputTokens": 8000, "costUsd": 0.62 }
      ]
    }
  ],
  "totals": { "inputTokens": 250000, "outputTokens": 95000, "costUsd": 7.15 }
}
```

`provider` parameter is optional — omit to get all providers.

### GET /api/balance

Returns current credit balance per provider.

```json
{
  "providers": [
    { "provider": "anthropic", "totalCredits": 100.00, "used": 45.30, "remaining": 54.70 },
    { "provider": "openrouter", "totalCredits": 50.00, "used": 12.80, "remaining": 37.20 },
    { "provider": "mistral", "totalCredits": 20.00, "used": 5.10, "remaining": 14.90 }
  ],
  "lastSyncedAt": {
    "anthropic": "2026-03-23T14:30:00Z",
    "openrouter": "2026-03-23T14:30:00Z",
    "mistral": "2026-03-23T14:15:00Z"
  }
}
```

### GET /api/summary

StreamDeck-friendly compact endpoint — all key numbers in one call. `lastSyncedAt` is the oldest (most stale) provider sync time.

```json
{
  "today": { "costUsd": 2.15, "inputTokens": 80000, "outputTokens": 32000 },
  "thisMonth": { "costUsd": 45.30, "inputTokens": 1800000, "outputTokens": 720000 },
  "balanceTotal": 91.90,
  "lastSyncedAt": "2026-03-23T14:15:00Z"
}
```

## Sync Worker

A `BackgroundService` running every 15 minutes (configurable).

### Anthropic

1. `GET /v1/organizations/cost_report` (Admin API key) — fetches from last sync time to now (or last 24h on first run)
2. `GET /v1/organizations/usage_report/messages` for token counts
3. Upsert into `UsageRecords`
4. Balance: subtract cumulative cost from configured `TotalCredits` → store in `BalanceSnapshots`. Note: `TotalCredits` must be manually updated in config after each top-up (Anthropic has no balance API)

### OpenRouter

1. `GET /api/v1/credits` (management key) → store in `BalanceSnapshots`
2. `GET /api/v1/activity` for per-model usage and cost breakdown → upsert into `UsageRecords`. Note: OpenRouter's `/api/v1/credits` already returns `total_credits` and `total_usage`, so no config value needed for OpenRouter balance

### Mistral (via LiteLLM)

1. Call LiteLLM's `/spend/logs` endpoint (returns per-request spend with model info)
2. Filter for Mistral model usage → upsert into `UsageRecords`
3. Balance: subtract cumulative cost from configured `Budget` → store in `BalanceSnapshots`

### First Run Behavior

- Anthropic: fetch last 24h of data
- OpenRouter: fetch current credits + recent activity
- LiteLLM: fetch last 24h of `/spend/logs`

### Error Handling

- If a provider API fails, log and skip — don't block other providers
- Retry once after 30s on transient failures (HTTP 5xx, timeout)
- Track `lastSyncedAt` per provider in `SyncStatus` table so the API can show staleness

## Deployment

- **Dockerfile:** .NET 8 publish → `mcr.microsoft.com/dotnet/aspnet:8.0` runtime
- **docker-compose.yml:** single service, port 8080, SQLite volume mount
- **Deploy:** `deploy-service.sh --name llmeter --node cirrus-pve`
- **DNS:** `llmeter.home.freaxnx01.ch` via Traefik
- **No auth** — internal network only
- **Health:** `GET /healthz` endpoint for Traefik health checks
