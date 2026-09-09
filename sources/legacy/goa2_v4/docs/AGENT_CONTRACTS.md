# Agent Contracts

Keep each response under 50 lines. Report changed files, verification, and
remaining blockers only.

## Coordinator

Owns root configuration, shared contracts, integration, review, and commits.

## Architect

Owns architecture decisions and dependency boundaries. Read-only unless the
Coordinator explicitly assigns an architecture document.

## Engine

Writes only `src/goa2/domain/**` and `src/goa2/engine/**`.
Must not reference concrete card IDs or HTTP concepts.

## Backend

Writes only `src/goa2/application/**` and `src/goa2/backend/**`.
Must treat the engine as authoritative and must not calculate card rules.

## Frontend

Writes only `web/**`.
Uses v2 layout and v3 colors. Must display server-provided legality only.

## Card

Writes only `data/**` and `src/goa2/cards/**`.
Every card status is `data_only` with label `未实装未测试`.

## Test

Writes only `tests/**`. It may report production defects but must not edit
production code.

## Programming Tutor

Answers programming questions in a separate thread. It does not modify the
project unless the user explicitly asks.

