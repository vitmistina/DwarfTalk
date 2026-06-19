# Provider Configuration Runbook

Status: Draft  
Applies to: v0.1 real provider mode  
Provider: OpenRouter through OpenAI-compatible HTTP API

## Default provider

v0.1 uses `FakeChatProvider` by default.

The first real provider target is:

```text
ProviderType = OpenAiCompatible
Endpoint = https://openrouter.ai/api/v1
Model = deepseek/deepseek-v3.2
```

## Environment variables

PowerShell:

```powershell
$env:FortressSouls__Llm__ProviderType = "OpenAiCompatible"
$env:FortressSouls__Llm__Endpoint = "https://openrouter.ai/api/v1"
$env:FortressSouls__Llm__Model = "deepseek/deepseek-v3.2"
$env:FortressSouls__Llm__ApiKey = ""
$env:FortressSouls__Llm__MaxOutputTokens = "500"
$env:FortressSouls__Llm__Temperature = "0.85"
$env:FortressSouls__Llm__TimeoutSeconds = "45"
```

For fake mode:

```powershell
$env:FortressSouls__Llm__ProviderType = "Fake"
```

## Manual OpenRouter smoke test

Use this before implementing or debugging the provider adapter.

```powershell
$env:OPENROUTER_API_KEY = ""

$body = @{
  model = "deepseek/deepseek-v3.2"
  messages = @(
    @{
      role = "system"
      content = "You portray a Dwarf Fortress dwarf. Stay concise and in character."
    },
    @{
      role = "user"
      content = "You are Urist, a tired miner. Why are you unhappy?"
    }
  )
  temperature = 0.85
  max_tokens = 300
} | ConvertTo-Json -Depth 10

Invoke-RestMethod `
  -Uri "https://openrouter.ai/api/v1/chat/completions" `
  -Method Post `
  -Headers @{
    "Authorization" = "Bearer $env:OPENROUTER_API_KEY"
    "Content-Type" = "application/json"
    "HTTP-Referer" = "http://localhost:5173"
    "X-Title" = "DwarfTalk"
  } `
  -Body $body
```

## Expected result

The response should contain one assistant message with concise in-character dwarf prose.

The exact phrasing is not important. This test only proves:

- the API key works,
- the endpoint works,
- the model slug works,
- the request shape is accepted,
- the model returns plain chat text.

## Safety rules

Never commit:

- real API keys,
- Authorization headers,
- full provider request logs,
- full prompt logs by default,
- full LLM responses in telemetry by default.

Never expose secrets through:

- health endpoint,
- provider status endpoint,
- frontend diagnostics,
- exception messages,
- structured logs,
- trace tags.

## Recommended v0.1 defaults

```text
MaxOutputTokens = 500
Temperature = 0.85
TimeoutSeconds = 45
```

For normal dwarf chat, avoid excessive output limits. A dwarf does not need 4,000 tokens to complain about hauling rocks. That way lies invoice-shaped slapstick.

## Troubleshooting

### 401 Unauthorized

Likely causes:

- missing API key,
- wrong environment variable,
- copied key with whitespace,
- OpenRouter account not funded,
- key revoked.

First checks:

```powershell
echo $env:OPENROUTER_API_KEY
echo $env:FortressSouls__Llm__ApiKey
```

Do not paste real keys into logs, issues, screenshots, or prompts.

### 404 Model not found

Likely causes:

- model slug changed,
- model temporarily unavailable,
- wrong endpoint,
- provider routing issue.

First adjustment:

```text
Check the model slug in OpenRouter.
```

### Timeout

Likely causes:

- provider latency,
- overloaded model,
- too high `MaxOutputTokens`,
- network issue.

First adjustment:

```text
Lower MaxOutputTokens to 300.
```

### Response is too verbose

Lower:

```text
MaxOutputTokens
Temperature
```

Also strengthen the prompt rule:

```text
Keep responses concise unless the player asks for detail.
```

### Response is too generic

Check whether the prompt includes:

- selected dwarf identity,
- profession,
- current job,
- personality traits,
- needs,
- recent conversation.

Generic model output is often not a provider problem. It is usually a thin prompt with a decorative beard.
