#!/usr/bin/env node

import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const localConfigFileName = "fortress-souls.config.jsonc";
const exampleConfigFileName = "fortress-souls.config.example.jsonc";
const allowedDotEnvKeys = new Set([
  "FortressSouls__Llm__ApiKey",
  "OTEL_EXPORTER_OTLP_HEADERS",
]);

const defaultConfig = {
  backend: {
    port: 5230,
  },
  frontend: {
    port: 5173,
  },
  observability: {
    otlpEndpoint: "http://localhost:4317",
  },
  dwarfFortress: {
    adapterType: "Fake",
    jsonFile: {
      dwarfListPath: "samples/snapshots/fake-dwarves-list.v0.1.json",
      dwarfSnapshotPath: "samples/snapshots/fake-dwarf-4101.v0.1.json",
    },
    dfHack: {
      runPath:
        "C:/Program Files (x86)/Steam/steamapps/common/DFHack/hack/dfhack-run.exe",
      workingDirectory: "",
      host: "127.0.0.1",
      port: 5000,
    },
  },
  llm: {
    providerType: "Fake",
    endpoint: "https://openrouter.ai/api/v1",
    model: "deepseek/deepseek-v3.2",
    maxOutputTokens: 500,
    temperature: 0.85,
    timeoutSeconds: 45,
  },
};

export function stripJsonComments(input) {
  let result = "";
  let inString = false;
  let stringQuote = "";
  let escaping = false;
  let inLineComment = false;
  let inBlockComment = false;

  for (let index = 0; index < input.length; index++) {
    const current = input[index];
    const next = input[index + 1];

    if (inLineComment) {
      if (current === "\n") {
        inLineComment = false;
        result += current;
      }

      continue;
    }

    if (inBlockComment) {
      if (current === "*" && next === "/") {
        inBlockComment = false;
        index++;
      }

      continue;
    }

    if (inString) {
      result += current;

      if (escaping) {
        escaping = false;
        continue;
      }

      if (current === "\\") {
        escaping = true;
        continue;
      }

      if (current === stringQuote) {
        inString = false;
        stringQuote = "";
      }

      continue;
    }

    if (current === '"' || current === "'") {
      inString = true;
      stringQuote = current;
      result += current;
      continue;
    }

    if (current === "/" && next === "/") {
      inLineComment = true;
      index++;
      continue;
    }

    if (current === "/" && next === "*") {
      inBlockComment = true;
      index++;
      continue;
    }

    result += current;
  }

  return result;
}

export function parseJsonc(input) {
  const stripped = stripJsonComments(input);
  return JSON.parse(stripped);
}

export function parseDotEnv(input) {
  const values = {};
  const lines = input.split(/\r?\n/);

  for (const line of lines) {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith("#")) {
      continue;
    }

    const exportPrefix = trimmed.startsWith("export ")
      ? trimmed.slice(7)
      : trimmed;
    const separatorIndex = exportPrefix.indexOf("=");
    if (separatorIndex < 0) {
      continue;
    }

    const key = exportPrefix.slice(0, separatorIndex).trim();
    let value = exportPrefix.slice(separatorIndex + 1);

    if (
      (value.startsWith('"') && value.endsWith('"')) ||
      (value.startsWith("'") && value.endsWith("'"))
    ) {
      value = value.slice(1, -1);
    }

    values[key] = value;
  }

  return values;
}

function mergeInto(baseValue, overrideValue) {
  if (overrideValue === undefined) {
    return structuredClone(baseValue);
  }

  if (baseValue === null || overrideValue === null) {
    return overrideValue;
  }

  if (Array.isArray(baseValue) || Array.isArray(overrideValue)) {
    return structuredClone(overrideValue);
  }

  if (typeof baseValue !== "object" || typeof overrideValue !== "object") {
    return structuredClone(overrideValue);
  }

  const merged = { ...structuredClone(baseValue) };
  for (const [key, value] of Object.entries(overrideValue)) {
    merged[key] =
      key in merged ? mergeInto(merged[key], value) : structuredClone(value);
  }

  return merged;
}

function toPositivePort(value, fieldName) {
  const parsed = Number.parseInt(String(value), 10);
  if (Number.isNaN(parsed) || parsed <= 0 || parsed > 65535) {
    throw new Error(`${fieldName} must be a valid TCP port.`);
  }

  return parsed;
}

function toResolvedPath(repoRoot, configuredPath) {
  if (!configuredPath || !String(configuredPath).trim()) {
    return "";
  }

  return path.isAbsolute(configuredPath)
    ? configuredPath
    : path.resolve(repoRoot, configuredPath);
}

function toStringValue(value) {
  return value === undefined || value === null ? "" : String(value);
}

export function resolveDevEnvironment({
  repoRoot,
  config = {},
  dotenv = {},
  configPath = "",
}) {
  const merged = mergeInto(defaultConfig, config);
  const backendPort = toPositivePort(merged.backend.port, "backend.port");
  const frontendPort = toPositivePort(merged.frontend.port, "frontend.port");
  const backendBaseUrl = `http://127.0.0.1:${backendPort}`;
  const jsonFileListPath = toResolvedPath(
    repoRoot,
    merged.dwarfFortress.jsonFile.dwarfListPath,
  );
  const jsonFileSnapshotPath = toResolvedPath(
    repoRoot,
    merged.dwarfFortress.jsonFile.dwarfSnapshotPath,
  );
  const dfHackRunPath = toResolvedPath(
    repoRoot,
    merged.dwarfFortress.dfHack.runPath,
  );
  const configuredWorkingDirectory = toResolvedPath(
    repoRoot,
    merged.dwarfFortress.dfHack.workingDirectory,
  );
  const dfHackWorkingDirectory =
    configuredWorkingDirectory ||
    (dfHackRunPath ? path.dirname(dfHackRunPath) : "");

  const environment = {
    ASPNETCORE_ENVIRONMENT: "Development",
    FORTRESS_SOULS_CONFIG_SOURCE: configPath ? path.basename(configPath) : "",
    FORTRESS_SOULS_CONFIG_PATH: configPath,
    FORTRESS_SOULS_BACKEND_PORT: String(backendPort),
    FORTRESS_SOULS_BACKEND_BASE_URL: backendBaseUrl,
    FORTRESS_SOULS_FRONTEND_PORT: String(frontendPort),
    FORTRESS_SOULS_API_PROXY_TARGET: backendBaseUrl,
    FortressSouls__DwarfFortress__AdapterType: toStringValue(
      merged.dwarfFortress.adapterType,
    ),
    FortressSouls__DwarfFortress__JsonFile__DwarfListPath: jsonFileListPath,
    FortressSouls__DwarfFortress__JsonFile__DwarfSnapshotPath:
      jsonFileSnapshotPath,
    FortressSouls__DfHack__RunPath: dfHackRunPath,
    FortressSouls__DfHack__WorkingDirectory: dfHackWorkingDirectory,
    FortressSouls__DfHack__Host: toStringValue(
      merged.dwarfFortress.dfHack.host,
    ),
    FortressSouls__DfHack__Port: toStringValue(
      merged.dwarfFortress.dfHack.port,
    ),
    FortressSouls__Llm__ProviderType: toStringValue(merged.llm.providerType),
    FortressSouls__Llm__Endpoint: toStringValue(merged.llm.endpoint),
    FortressSouls__Llm__Model: toStringValue(merged.llm.model),
    FortressSouls__Llm__MaxOutputTokens: toStringValue(
      merged.llm.maxOutputTokens,
    ),
    FortressSouls__Llm__Temperature: toStringValue(merged.llm.temperature),
    FortressSouls__Llm__TimeoutSeconds: toStringValue(
      merged.llm.timeoutSeconds,
    ),
    OTEL_EXPORTER_OTLP_ENDPOINT: toStringValue(
      merged.observability.otlpEndpoint,
    ),
    OTEL_EXPORTER_OTLP_PROTOCOL: "grpc",
    OTEL_SERVICE_NAME: "FortressSouls.Api",
  };

  for (const [key, value] of Object.entries(dotenv)) {
    if (allowedDotEnvKeys.has(key)) {
      environment[key] = value;
    }
  }

  return {
    environment,
    summary: {
      configPath,
      adapterType: environment.FortressSouls__DwarfFortress__AdapterType,
      providerType: environment.FortressSouls__Llm__ProviderType,
      backendBaseUrl: environment.FORTRESS_SOULS_BACKEND_BASE_URL,
      frontendBaseUrl: `http://127.0.0.1:${frontendPort}`,
      observability: environment.OTEL_EXPORTER_OTLP_ENDPOINT
        ? "otlp"
        : "console",
    },
  };
}

export async function loadDevEnvironment(repoRoot) {
  const localConfigPath = path.join(repoRoot, localConfigFileName);
  const exampleConfigPath = path.join(repoRoot, exampleConfigFileName);
  const dotenvPath = path.join(repoRoot, ".env");

  let configText;

  try {
    configText = await fs.readFile(localConfigPath, "utf8");
  } catch (error) {
    if (
      error &&
      typeof error === "object" &&
      "code" in error &&
      error.code === "ENOENT"
    ) {
      configText = await fs.readFile(exampleConfigPath, "utf8");
      await fs.writeFile(localConfigPath, configText, "utf8");
    } else {
      throw error;
    }
  }

  let dotenv = {};
  try {
    dotenv = parseDotEnv(await fs.readFile(dotenvPath, "utf8"));
  } catch (error) {
    if (
      !(
        error &&
        typeof error === "object" &&
        "code" in error &&
        error.code === "ENOENT"
      )
    ) {
      throw error;
    }
  }

  return resolveDevEnvironment({
    repoRoot,
    config: parseJsonc(configText),
    dotenv,
    configPath: localConfigPath,
  });
}

function formatEnvironment(result) {
  return Object.entries(result.environment)
    .map(([key, value]) => `${key}=${value}`)
    .join("\n");
}

function formatSummary(result) {
  return [
    `config: ${result.summary.configPath}`,
    `adapter: ${result.summary.adapterType}`,
    `provider: ${result.summary.providerType}`,
    `backend: ${result.summary.backendBaseUrl}`,
    `frontend: ${result.summary.frontendBaseUrl}`,
    `observability: ${result.summary.observability}`,
  ].join("\n");
}

async function main() {
  const command = process.argv[2] ?? "env";
  const repoRoot = process.argv[3]
    ? path.resolve(process.argv[3])
    : process.cwd();
  const result = await loadDevEnvironment(repoRoot);

  if (command === "env") {
    process.stdout.write(`${formatEnvironment(result)}\n`);
    return;
  }

  if (command === "summary") {
    process.stdout.write(`${formatSummary(result)}\n`);
    return;
  }

  throw new Error(`Unknown command '${command}'. Expected 'env' or 'summary'.`);
}

const currentFilePath = fileURLToPath(import.meta.url);
if (process.argv[1] && path.resolve(process.argv[1]) === currentFilePath) {
  main().catch((error) => {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  });
}
