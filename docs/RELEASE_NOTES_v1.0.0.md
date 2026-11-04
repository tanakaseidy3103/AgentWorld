# AgentWorld v1.0.0 — Meta AI Developer Hackathon

An open-source visual laboratory for building, testing, and evaluating collaborative AI agents powered by Meta Llama.

## What's included

- Three collaborating agents: Ada (Coordinator), Grace (Explorer), and Linus (Builder), powered by Meta Llama 3.2 3B running locally through Ollama.
- Multi-agent missions in plain language. Ada delegates, Grace measures free land and reports it to shared memory, the agents discuss the plan, Ada chooses the sites, and Linus builds houses and farms.
- A skill registry with seven validated skills, and agent permissions configured in `agents.json`.
- Specialist routing for free-form requests ("What can Linus build?", "Find a safe place to build").
- Honest outcomes: Succeeded, Partial, Failed, Cancelled, or Unsupported.
- Evaluation scenarios (`eval run village | farm | unsupported`) with measured text and JSON reports.
- Mission Control panel showing Llama decisions, the agent conversation, Unity validation, tasks, and shared memory.

## Requirements

- Windows 10 or 11 (64-bit)
- Ollama installed and running locally, with the model pulled: `ollama pull llama3.2` (about 2 GB)
- Tested on an Intel i7-13650HX laptop with 16 GB RAM and an RTX 3050 6 GB. Other hardware hasn't been measured.

## Setup

1. Download `AgentWorld-Windows-x64.zip` and unzip the whole folder (not just the .exe).
2. Make sure Ollama is running (`ollama list` shows `llama3.2`).
3. Run `AgentWorld.exe`. Press **M**, type `Build a small village near the lake`, press Enter, then Esc.
4. To change agent skills, edit `AgentWorld_Data/StreamingAssets/agents.json`.

## Known limitations

- Only houses and farms can be built; there are no roads, bridges, or other structures.
- `agents.json` configures the three existing agents; new characters and new skills require C# code.
- One model is used for all agents. Llama 3.2 3B may pick sites other than the one named in the mission.
- Movement uses straight lines with sidestepping, not full pathfinding.
- A license hasn't been selected yet.

## Verification

- 27/27 EditMode and 37/37 PlayMode tests pass; opt-in live-Llama tests are documented in the README.
- The village mission succeeded in this Windows build at 1920x1080 with real Llama 3.2.
