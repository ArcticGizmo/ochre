# 0009: The bridge contract is declared once in C# and the TypeScript types are generated

- Status: accepted
- Date: 2026-11-25

## Context

The UI talks to the host only through the typed bridge (ADR 0002, security.md threat 5). Each message has a C# request and response record (validated on the host) and a matching TypeScript type. In the M0 spike the TS types were hand-written; a new message needed edits in three places and they drifted once within a day. M1 adds a dozen task messages, and every later milestone adds more.

Options considered:

| Option | Verdict |
|---|---|
| Hand-written TS mirrors | Cheap now, drifts silently; drift shows up as runtime "invalid payload" errors |
| JSON Schema in the middle (C# → schema → TS) | Two generators and a schema dialect to learn, for the same result |
| A small in-repo generator that reflects over a C# contract list and writes one `.ts` file | Chosen |

## Decision

`src/Ochre.Host/Bridge/BridgeContract.cs` lists every request (`type`, request record, response record) and every event (`name`, payload record). `tools/Ochre.BridgeGen` reflects over that list and writes `ui/src/generated/bridge-types.ts`: one interface per record (camelCase, nullability from C# annotations), string unions for enums, and `Requests` / `Events` maps that the UI's `request()` and `on()` are typed against. The generated file is committed. Run `dotnet run --project tools/Ochre.BridgeGen` after changing a bridge record; `--check` exits non-zero when the file is stale.

At startup the host compares the handlers it registered with the contract and logs any mismatch, so a handler without a contract entry (or the reverse) is caught on the first run.

## Consequences

- Adding a message is: record(s), a contract line, a handler, regenerate. The UI then fails to type-check until it uses the new shape correctly.
- The generator understands only what the bridge needs (strings, numbers, booleans, timestamps as strings, enums, nullable, lists, nested records); anything else fails generation loudly rather than producing `any`.
- No runtime schema validation in the UI; the host remains the validator.
