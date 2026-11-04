# Submission materials — AgentWorld

## Title and tagline

**AgentWorld: Llama Agents That Build Together**
*A living pixel world where three local Meta Llama agents plan, delegate, explore and build, safely and offline.*

## Short description (≈100 words)

AgentWorld is a Unity 2D world inhabited by three AI agents powered by Meta Llama 3.2 running locally
through Ollama. Give the team a mission in plain language, like "Build a small village near the lake".
Ada, the coordinator, uses Llama to decompose it. Grace explores the map and writes measured findings
to shared memory. Ada uses Llama again to choose building sites, and Linus constructs the houses in
front of you. Every model decision is structured JSON that Unity validates against the real world state
before executing it, with retries, recovery, cancellation and honest failure reporting. Everything runs
offline: no API keys, no cloud, no per-token cost.

## Problem and relevance

LLM agents are moving from chat into environments: games, robots, software tools. There, a
hallucinated location or an unsafe command has real consequences. Teams need patterns for letting a
model *decide* while a trusted system *acts*, for coordinating several agents through shared state,
and for showing users what the AI decided. AgentWorld demonstrates all three in a form anyone can watch:
the model proposes, the engine validates and executes, and a live panel shows which is which. Because it
runs on open weights locally, it costs nothing to run, keeps data private, and works offline, which suits
classrooms, indie game studios and privacy-sensitive settings.

## Technical architecture (for the form)

- **Model:** Meta Llama 3.2 3B Instruct (open weights) served by Ollama on localhost; Unity calls
  `/api/chat` asynchronously (UnityWebRequest + Awaitable) with JSON mode.
- **Agents:** each NPC has a `LlamaAgentBrain` (state machine, bounded memory, timeout, cancellation).
- **Mission pipeline:** Ada → Llama delegation JSON → Grace inspects; Unity computes free land and writes
  `SiteReport`s to shared memory → Ada → Llama site-selection JSON → Linus builds.
- **Safety:** whitelisted actions (`move_to`, `build`, `inspect`, `speak`, `wait`), only real location
  ids, capacity checks, max actions/houses/retries. Model output is never executed as code.
- **World:** collision-free placement (no overlap, roads and lab kept clear), animated construction,
  versioned JSON save (v3) that persists houses, memory and mission.
- **Quality:** 39 automated tests with a deterministic mock LLM, plus opt-in live-Llama tests.

## Features that actually work (verified)

See README → "What works (verified)". Do not claim anything beyond that list.

## 3-minute video script

Preparation (before recording):
1. `ollama list` shows `llama3.2`. Run `ollama run llama3.2 "hi"` once so the model is warm in memory.
2. Delete the old save so the world starts clean:
   `del "%USERPROFILE%\AppData\LocalLow\DefaultCompany\AgentWorld\world.json"`
3. Unity: **AgentWorld → Rebuild World**, open `Main.unity`, set the Game view to **1920x1080**, press Play.

| Time | Screen | Say |
|---|---|---|
| 0:00–0:20 | Title card, then the game running | "AI agents usually live in chat boxes. AgentWorld gives them a world, a body and teammates, all powered by Meta Llama 3.2 running locally." |
| 0:20–0:40 | Walk with WASD past the lake and houses; Mission Control on the right shows *Meta Llama (Ollama): conectado · llama3.2* | "Three agents live here: Ada coordinates, Grace explores, Linus builds. Everything runs offline through Ollama." |
| 0:40–0:55 | Press **M**, type `Build a small village with a farm near the lake`, Enter, then **Esc** | "I give the team one mission in plain language." |
| 0:55–1:15 | Panel: Ada *Thinking*, then the **Decisões do Llama** line with the explore list and message | "Ada asks Llama to break it down: which places Grace should scout, and how many houses." |
| 1:15–1:45 | Grace (magnifier icon) walking to each place, planting flags labelled "N livres"; *Memória compartilhada* fills with free-site counts | "Grace inspects each place. Unity measures the real free land and Grace writes it to shared memory. These are facts, not guesses." |
| 1:45–2:25 | Second Llama decision (houses + farm sites, reason); Linus (hammer icon) walks over; scaffold and dust, the house rises, then the farm; tasks get ✔ | "Ada reads the shared memory and Llama picks the sites. Unity checks every choice: the place must exist, must have been explored and must have room. Then Linus builds." |
| 2:25–2:40 | Press **M**, type `Build a giant stone castle on top of the lake`. Ada refuses with Llama's explanation; status *Unsupported* | "And when we ask for something the team can't do, Ada says so instead of pretending. Every model decision is validated before anything moves." |
| 2:40–2:50 | Press F5; stop and restart Play (or F9); the village is still there | "The world, the agents' memory and the mission persist." |
| 2:50–3:00 | Wide shot of the village | "Local open-weight Llama agents that collaborate safely. This is AgentWorld." |

Backup if the live run misbehaves: run `mission stop`, then
`agent builder build a house near the lake` (single-agent, verified 3/3), or re-record. Never edit the
video to fake a result.

## Demo recording checklist

- [ ] Ollama running, `llama3.2` warm, Wi-Fi not needed
- [ ] Old `world.json` deleted; Rebuild World done; Game view 1920x1080, not maximized-blurry
- [ ] Mission Control shows "conectado · llama3.2" before you start
- [ ] OBS / Xbox Game Bar (Win+Alt+R) records the editor at 1080p with microphone
- [ ] One full dry run done (missions take ~60 s; check the timing)
- [ ] Video ≤ 3:00, model name and "runs locally" mentioned, no unverified claims
- [ ] Upload to YouTube/Vimeo as **Public** or **Unlisted**; test the link in a private window

## Final submission checklist

- [ ] Repository URL: https://github.com/tanakaseidy3103/AgentWorld (owner must push the branches and make it **public**)
- [ ] Default branch or README points judges to the final code (merge `feature/final-sprint` into `main`, or say which branch)
- [ ] Demo video URL: ______________________ (public, opens without login)
- [ ] README has setup, model, architecture, Meta AI usage, limitations (done in repo)
- [ ] No secrets: verified with `git grep`; `.gitignore` excludes Library/, Builds/, logs
- [ ] Model disclosed: **Meta Llama 3.2 3B via Ollama, local**; no Muse Glimmer/Spark bonus claimed
- [ ] Form fields filled from this file (title, tagline, description, problem, architecture)
- [ ] Submitted before **Nov 5 2026, 11:00 JST (Nov 4, 9 PM ET)**. Aim for a buffer of 2+ hours.
