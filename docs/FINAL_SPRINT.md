# Final sprint — audit and TODO

Deadline: **Nov 4 2026, 9 PM ET = Nov 5, 11:00 JST**. Branch: `feature/final-sprint`.
Fallback: `feature/mvp2-llama-agents` (single-agent build demo, last commit `48aeae1`) is untouched.

## Audit (start of sprint, Nov 4 ~16:00 JST)

| Area | State found |
|---|---|
| Single-agent Llama plans (`agent builder ...`) | working, verified live 3/3 |
| NPC chat via E | working (mock-tested; live in editor) |
| Save/load v2 | working, tested |
| Multi-agent collaboration | **missing** |
| AI status UI | **missing** (only per-NPC labels + `ai status` command) |
| Build sites | could land on roads (seen in screenshots) |
| Windows build | never attempted |
| Scene in repo | generated assets are git-ignored; users must run Rebuild World |
| Secrets | none found (`git grep` for keys/tokens/emails/paths) |

## TODO (priority order) and status

- [x] P0 Multi-agent mission: Ada (Llama) delegates → Grace explores → shared memory → Ada (Llama) selects → Linus builds
- [x] P0 Validation against world state, 1 retry, recovery when a site fills, timeouts, cancellation, honest Partial/Failed
- [x] P0 Mission Control panel (Ollama/model, mission, agents, tasks, shared memory, Llama decisions vs Unity actions)
- [x] P0 Tests for delegation, invalid location, unexplored site, recovery, cancel, offline, persistence, terminal
- [x] P0 Live Llama verification of the mission (6 runs: 5 Succeeded, 1 Partial)
- [x] P1 Build sites kept off roads; save v3 persists mission + shared memory
- [x] P1 Windows standalone build (`AgentWorld → Build Windows`, 81 MB, succeeded in batchmode)
- [x] P1 README for judges, submission materials (`docs/SUBMISSION.md`)
- [ ] **Manual**: watch one full mission in the editor's Game view at 1920x1080 before recording
- [ ] **Manual**: save → quit → restart the game → confirm the village loads (only tested in-process)
- [ ] **Manual**: launch `Builds/Windows/AgentWorld.exe` once with Ollama running
- [ ] **Manual**: record the video, upload, make the repo public, submit
- [ ] P2 (skipped on purpose) more structure types, parallel agents, real pathfinding

## What was verified, and how

| Claim | Evidence |
|---|---|
| EditMode 19/19, PlayMode 20/20 pass | Unity 6000.0.60f1 batchmode, on a copy of the project (the editor had the project open) |
| Live mission works | `LiveLlama_VillageMission` + screenshot runs with real Ollama + llama3.2. Final code: 6 runs, 5 Succeeded, 1 Partial. An earlier prompt version failed 1/3 (model returned `<lake>`); fixed by id normalization |
| Panel readable, houses off roads | Screenshots from a live mission at 3840x2160 (the editor ignored the 1080p resize, so 1080p was not seen directly) |
| Windows build | `WorldBuilder.BuildWindows` in batchmode: "Windows build Succeeded" |
| Not verified | restart persistence across processes, the .exe actually running, 1080p on-screen look |

## Demo polish (branch `feature/demo-polish`, Nov 4 evening)

Fallback: `feature/final-sprint` (commit `e99cac5`).

- [x] Procedural visuals: 3-frame lake, shadows, pine trees, flowers/tufts/rocks, scaffold + dust during construction
- [x] Task icons over agents (think/decide, scout, hammer, done/fail); Grace's survey flags with free-site labels
- [x] Mission bar (M) using natural language; terminal docked bottom-left beside Mission Control; all commands kept
- [x] Honest refusal: Ada returns `feasible: no` + explanation → mission *Unsupported* (live 2/2 for the castle request)
- [x] New structure: farm (trigger collider = walkable, blocks other construction, saved/loaded) + tests
- [x] Tests: EditMode 19/19, PlayMode 25/25 (copy of the project, editor was open); Windows build succeeded again
- [x] Live: village mission 3/3 Succeeded (houses only), farm mission 1/1 Succeeded (1 house + 1 farm)
- [ ] Manual: in the open editor, run **AgentWorld → Rebuild World** so the new art and farm prefab exist (old scene has no farm prefab; farm builds fail there with "estrutura indisponível")
