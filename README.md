# AgentWorld — A Living Pixel World Run by Local Llama Agents

**Three AI agents that plan, delegate, explore and build together, powered by Meta Llama 3.2 running 100% locally.**

AgentWorld is a 2D pixel-art simulation built in Unity 6. You give a mission in plain language,
for example *"Build a small village near the lake"*, and a team of agents carries it out:

1. **Ada** (coordinator) asks **Meta Llama 3.2** to break the mission into tasks: which places to scout and how many houses to build.
2. **Grace** (explorer) walks to each place. Unity measures the real free building land there, and Grace writes a structured report to **shared memory**.
3. **Ada** asks Llama again, this time with Grace's reports, and picks the building sites.
4. **Linus** (builder) walks to each site and builds the houses. If a site fills up, Unity recovers by trying another reported location.
5. The village stays in the world and is still there after you save and load.

The model **makes the decisions**. Unity **validates every decision against the live world** and then
**executes it deterministically**. Nothing the model writes is ever run as code.

> Built for the Meta Global AI Developer Hackathon. Unity **6000.0.60f1** · Built-in RP · Windows.

---

## Why it matters

Today's LLM agents mostly live in chat boxes. Once you give them a body, a world and teammates, the
hard problems show up: trusting model output, coordinating several agents, recovering from failure,
and showing people *why* an agent did something. AgentWorld is a small, inspectable testbed for exactly
that. It's useful for:
- teaching how agents work,
- prototyping game NPCs with real autonomy,
- exploring safe "LLM proposes, engine disposes" architectures.

And it runs fully offline on a normal laptop, with no API key and nothing billed per token.

## How we use Meta's open-weight AI

| | |
|---|---|
| Model | **Meta Llama 3.2 3B Instruct** (`llama3.2`, Q4_K_M, ~2 GB), open weights |
| Runtime | **Ollama**, local HTTP API at `http://localhost:11434` (`/api/chat` with `format: "json"`) |
| Not used | Meta Model API, cloud inference, paid APIs |
| Llama decides | mission decomposition (where to scout, how many houses), site selection from shared memory, single-agent action plans, NPC dialogue in character |
| Unity decides | whether a location exists, how much free land it has, collision-free placement, pathing, retries/recovery, success/failure |

Special prizes: this project uses **Llama 3.2**. It does **not** use Muse Glimmer or Muse Spark and makes
no claim to those bonus categories.

## Architecture

```
 Player ──► In-game terminal / NPC chat (IMGUI)          Mission Control panel (Tab)
                    │                                              ▲ live state
                    ▼                                              │
            MissionControl ───────────────────────────────────────┘
   1 Ada:   Llama ─► JSON {explore[], houses, message} ─► validate (real location ids)
   2 Grace: Perform(move_to, inspect) ─► Unity measures free sites ─► SiteReport ─► shared memory
   3 Ada:   Llama(shared memory) ─► JSON {build_at[], reason} ─► validate (explored? capacity?)
   4 Linus: Perform(move_to, build) ─► StructureBuilder (free site, no overlap, off roads)
            └─ site full? Unity recovery to another reported site (max 2 retries)
                    │
     LlamaAgentBrain (per NPC): state machine, memory, timeout, cancellation
                    │
     OllamaClient (UnityWebRequest, async) ─► Ollama ─► Meta Llama 3.2 (local)
                    │
     WorldSaveManager ─► world.json v3 (houses, agent memory, mission, shared memory)
```

Every model answer goes through the same path: parse JSON → whitelist → check against world state →
retry once with the validator's error → fail honestly. Agent states:
`Idle → Thinking → Planning → Moving → Acting → Completed | Failed | Cancelled`.

More detail: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Setup (Windows)

1. **Install Ollama:** <https://ollama.com/download/windows> (it runs in the tray on `localhost:11434`).
2. **Pull the model:**
   ```
   ollama pull llama3.2
   ollama list          # llama3.2:latest should be listed
   ```
3. **Open the project:** Unity Hub → *Add project from disk* → this folder → editor **6000.0.60f1**.
4. Menu **AgentWorld → Rebuild World** (generates sprites, prefabs and `Assets/AgentWorld/Scenes/Main.unity`).
5. Open `Main.unity` and press **Play**.

Optional environment variables: `AGENTWORLD_MODEL` (default `llama3.2`), `AGENTWORLD_OLLAMA_URL`
(default `http://localhost:11434`), `AGENTWORLD_TIMEOUT` (seconds per HTTP request, default 90).
Windows build: menu **AgentWorld → Build Windows** writes `Builds/Windows/AgentWorld.exe`.

## Controls and commands

| Key | Action |
|---|---|
| WASD | walk |
| E | at a lab computer: terminal · next to an agent: chat with it (Llama) |
| Tab | show/hide Mission Control |
| F5 / F9 | save / load |
| Enter / Esc | send / close |

Terminal (computers inside the lab building at the top of the map):

```
mission build a small village near the lake    # multi-agent mission
mission status | mission stop
agent builder build a house near the lake       # single agent, Llama writes the whole action plan
agent explorer inspect the lake | agent assistant explain the lab
agent list | agent status | agent stop <id>
world status | world structures | ai status
help, echo, whoami, agents, time, ide, clear
```

In-game text is in Portuguese (the team's language). Missions and chat work in English or Portuguese,
and Llama answers in the language of the request.

## What works (verified)

- Multi-agent mission with real Llama decisions. 6 live runs on Llama 3.2 3B: 5 *Succeeded*, 1 *Partial*
  (Ada chose fewer sites than she planned, and that was reported honestly). Around 60 s per mission.
- Single-agent Llama plans (`agent builder ...`): 3/3 live runs built a house.
- Validation rejects unknown actions, invented locations, unexplored or over-capacity sites, and builds by
  non-builders. A rejection triggers one retry that includes the error.
- Construction: free-site search, no overlaps, roads and lab kept clear, nobody gets walled in, and a visible build animation.
- Persistence (save v3): houses, agent memory, mission state and shared memory. v1/v2 saves still load.
- Failure handling: Ollama offline, timeouts, cancellation (`mission stop`, `agent stop`), malformed JSON.
- Mission Control panel and terminal scale with resolution, so text is readable at 1080p and above.
- Windows standalone build succeeds.

## Tests

```
Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults TestResults-EditMode.xml
Unity.exe -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults TestResults-PlayMode.xml
```

19 EditMode + 20 PlayMode tests use a **deterministic mock LLM**. They cover plan parsing and validation,
unsafe actions, state transitions, build placement, mission delegation, invalid locations, unexplored
sites, site-full recovery, cancellation, timeouts, Ollama offline, save/load and terminal commands.
Live-model tests are opt-in (they need Ollama running):

```
Unity.exe -batchmode -projectPath . -runTests -testPlatform PlayMode -testFilter AgentWorld.Tests.MissionPlayModeTests.LiveLlama_VillageMission
Unity.exe -batchmode -projectPath . -runTests -testPlatform PlayMode -testFilter AgentWorld.Tests.AiPlayModeTests.LiveLlama_BuilderBuildsHouse
```

## Built during the hackathon

All code in this repository was written during the hackathon (see the git history, Nov 3–4 2026):
the pixel world and procedural art (MVP 1), local Llama agents with validated JSON plans (MVP 2), and the
multi-agent mission, shared memory, recovery, Mission Control UI and Windows build (final sprint).
All art is generated procedurally by our own `WorldBuilder.cs`; there are no third-party assets.

## Known limitations

- One structure type (house); at most 3 houses per mission and 12 agent-built structures overall.
- No real pathfinding: straight lines, a sidestep when stuck, and a route through the lab door.
- A 3B model sometimes picks fewer sites than planned (Partial) or needs a validator retry.
- Missions run their steps one after another and are not resumed after loading a save (they show as *Interrupted*).
- The first request after starting Ollama is slower while the model loads.
