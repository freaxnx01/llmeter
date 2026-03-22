# LLM Cost & Usage Tracking – Research Summary

## Context

Exploring options to get a unified overview of token usage and costs across multiple LLM providers:

- **Mistral** – accessed via local **LiteLLM** (self-hosted on Proxmox, for data privacy / DSH)
- **Anthropic** – direct API access
- **OpenRouter** – used for additional model routing

Key constraint: **Data must not leave Switzerland/local infra** (no routing everything through US services).

---

## Provider API Support for Usage/Cost Queries

### Anthropic — Full API support

- Endpoint: `GET /v1/organizations/usage_report/messages`
- Endpoint: `GET /v1/organizations/cost_report`
- Requires: **Admin API Key** (`sk-ant-admin...`) – separate from standard API key
- Returns: Token counts + costs grouped by model, workspace, API key
- Granularity: per minute, per day, custom date ranges
- Docs: https://platform.claude.com/docs/en/build-with-claude/usage-cost-api

```bash
curl "https://api.anthropic.com/v1/organizations/cost_report?\
starting_at=2025-01-01T00:00:00Z&\
ending_at=2025-01-31T00:00:00Z&\
group_by[]=workspace_id" \
--header "anthropic-version: 2023-06-01" \
--header "x-api-key: $ANTHROPIC_ADMIN_API_KEY"
```

### OpenRouter — Activity API + per-request usage

- Dashboard: `openrouter.ai/activity` (per model, per API key)
- Per-request: add `"usage": {"include": true}` to request body
- Returns cost + token counts in response
- Docs: https://openrouter.ai/docs

### Mistral — Per-response only

- No dedicated historical cost/usage endpoint
- `usage` object is returned in **every chat completion response** (input_tokens, output_tokens)
- Historical data only via **La Plateforme** web UI (console.mistral.ai)
- For aggregation: must accumulate from LiteLLM logs or response parsing

---

## Evaluated Solutions

### Option A: OpenRouter as unified gateway (ruled out)
Route everything through OpenRouter → one dashboard. **Rejected** because Mistral is intentionally routed via local LiteLLM for data privacy.

### Option B: Langfuse self-hosted (observability platform)
- Open source, MIT license, Docker Compose deployable on Proxmox
- Supports OpenRouter natively (Broadcast feature = zero code changes)
- Supports Anthropic + OpenAI out of the box (cost calculation built-in)
- Mistral via OpenAI-compatible adapter
- **Requires instrumentation** – traffic must flow through Langfuse SDK or proxy
- Best for: full observability (traces, latency, debug), not just cost overview
- Self-hosted = free, no limits

### Option C: LiteLLM built-in dashboard (already available)
- LiteLLM is already running locally
- Has built-in UI with cost/token tracking per model + provider
- Can add Anthropic + OpenRouter keys alongside Mistral
- All traffic routed through local LiteLLM → full data sovereignty
- **Recommended if** all tools can point to local LiteLLM endpoint

### Option D: Custom dashboard (DIY)
Poll the three APIs and aggregate:

```
Anthropic Admin API  →  /v1/organizations/cost_report
OpenRouter API       →  /v1/activity or per-request usage
Mistral              →  accumulate from LiteLLM logs
```

Build a small self-hosted dashboard (e.g. simple .NET/Blazor or static HTML) on Proxmox.

---

## Recommended Architecture

```
OpenClaw / Claude Code / other tools
              ↓
         LiteLLM (local, Proxmox)
         ↙        ↓        ↘
    Mistral   Anthropic   OpenRouter
```

- All traffic through local LiteLLM → unified cost tracking in LiteLLM UI
- LiteLLM holds all three API keys
- No data leaves local infra for Mistral calls
- Single dashboard, no extra tooling needed

---

## Open Questions / Next Steps

- [ ] Add Anthropic + OpenRouter API keys to local LiteLLM config
- [ ] Verify LiteLLM UI shows cost breakdown per provider
- [ ] Decide: LiteLLM dashboard sufficient, or worth adding Langfuse for full observability?
- [ ] Consider: small custom Blazor dashboard polling all three APIs directly
