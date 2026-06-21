import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";

import {
  loadDevEnvironment,
  parseJsonc,
  resolveDevEnvironment,
} from "./dev-config.mjs";

test("parseJsonc strips comments without breaking URLs", () => {
  const config = parseJsonc(`
    {
      // provider defaults
      "llm": {
        "endpoint": "https://openrouter.ai/api/v1"
      },
      /* local telemetry */
      "observability": {
        "otlpEndpoint": "http://localhost:4317"
      }
    }
  `);

  assert.equal(config.llm.endpoint, "https://openrouter.ai/api/v1");
  assert.equal(config.observability.otlpEndpoint, "http://localhost:4317");
});

test("resolveDevEnvironment derives backend proxy and dfhack working directory", () => {
  const repoRoot = path.join("C:", "repo");
  const result = resolveDevEnvironment({
    repoRoot,
    configPath: path.join(repoRoot, "fortress-souls.config.jsonc"),
    config: {
      backend: { port: 6123 },
      frontend: { port: 6124 },
      dwarfFortress: {
        adapterType: "DfHackProcess",
        jsonFile: {
          dwarfListPath: "samples/snapshots/fake-dwarves-list.v0.1.json",
          dwarfSnapshotPath: "samples/snapshots/fake-dwarf-4101.v0.1.json",
        },
        dfHack: {
          runPath: "tools/dfhack/hack/dfhack-run.exe",
          host: "127.0.0.1",
          port: 5001,
        },
      },
      llm: {
        providerType: "OpenAiCompatible",
        endpoint: "https://openrouter.ai/api/v1",
        model: "deepseek/deepseek-v3.2",
      },
      observability: {
        otlpEndpoint: "",
      },
    },
    dotenv: {
      FortressSouls__Llm__ApiKey: "secret-key",
      FortressSouls__Llm__Model: "should-not-override-config",
    },
  });

  assert.equal(
    result.environment.FORTRESS_SOULS_BACKEND_BASE_URL,
    "http://127.0.0.1:6123",
  );
  assert.equal(
    result.environment.FORTRESS_SOULS_API_PROXY_TARGET,
    "http://127.0.0.1:6123",
  );
  assert.equal(result.environment.FORTRESS_SOULS_FRONTEND_PORT, "6124");
  assert.equal(result.environment.OTEL_EXPORTER_OTLP_PROTOCOL, "grpc");
  assert.equal(result.environment.OTEL_SERVICE_NAME, "FortressSouls.Api");
  assert.equal(
    result.environment.FortressSouls__DfHack__RunPath,
    path.join(repoRoot, "tools", "dfhack", "hack", "dfhack-run.exe"),
  );
  assert.equal(
    result.environment.FortressSouls__DfHack__WorkingDirectory,
    path.join(repoRoot, "tools", "dfhack", "hack"),
  );
  assert.equal(result.environment.FortressSouls__Llm__ApiKey, "secret-key");
  assert.equal(
    result.environment.FortressSouls__Llm__Model,
    "deepseek/deepseek-v3.2",
  );
  assert.equal(result.summary.observability, "console");
});

test("loadDevEnvironment bootstraps local config from example and merges dotenv secrets", async () => {
  const repoRoot = await fs.mkdtemp(
    path.join(os.tmpdir(), "fortress-souls-config-"),
  );

  try {
    await fs.writeFile(
      path.join(repoRoot, "fortress-souls.config.example.jsonc"),
      `
      {
        "backend": { "port": 7001 },
        "frontend": { "port": 7002 },
        "dwarfFortress": {
          "adapterType": "JsonFile",
          "jsonFile": {
            "dwarfListPath": "samples/snapshots/fake-dwarves-list.v0.1.json",
            "dwarfSnapshotPath": "samples/snapshots/fake-dwarf-4101.v0.1.json"
          }
        }
      }
      `,
      "utf8",
    );
    await fs.writeFile(
      path.join(repoRoot, ".env"),
      "FortressSouls__Llm__ApiKey=test-key\n",
      "utf8",
    );

    const result = await loadDevEnvironment(repoRoot);

    assert.equal(
      result.summary.configPath,
      path.join(repoRoot, "fortress-souls.config.jsonc"),
    );
    assert.equal(
      result.environment.FORTRESS_SOULS_API_PROXY_TARGET,
      "http://127.0.0.1:7001",
    );
    assert.equal(result.environment.FortressSouls__Llm__ApiKey, "test-key");
    assert.equal(result.environment.OTEL_SERVICE_NAME, "FortressSouls.Api");
    assert.equal(
      result.environment.FortressSouls__DwarfFortress__JsonFile__DwarfListPath,
      path.join(
        repoRoot,
        "samples",
        "snapshots",
        "fake-dwarves-list.v0.1.json",
      ),
    );
    assert.equal(
      await fs.readFile(
        path.join(repoRoot, "fortress-souls.config.jsonc"),
        "utf8",
      ),
      await fs.readFile(
        path.join(repoRoot, "fortress-souls.config.example.jsonc"),
        "utf8",
      ),
    );
  } finally {
    await fs.rm(repoRoot, { recursive: true, force: true });
  }
});
