# Submission kit

Deadline: **November 5, 2026, 11:00 JST** (November 4, 9:00 PM ET). Nothing in this file has been submitted.
The official form questions were not available to us; the answers below are organized by common topics so
they can be pasted into whichever fields the form uses.

## Project name

AgentWorld

## Short description (one sentence)

An open-source visual laboratory for building, testing, and evaluating collaborative AI agents powered by Meta Llama.

## Full description

AgentWorld is a Unity 6 pixel-art world where three AI agents work as a team: Ada (Coordinator), Grace
(Explorer), and Linus (Builder). All of them use Meta Llama 3.2 3B running locally through Ollama. Give the team
a mission in plain language, such as "Build a small village near the lake." Ada uses Llama to judge whether the
mission is feasible and to delegate the work. Grace scouts the map, Unity measures the free land at each place,
and Grace writes those measurements to shared memory. Grace and Linus then discuss the plan in Llama-generated
dialogue grounded in the measured numbers. Ada picks the building sites, and Linus builds houses and farms in
front of you.

Version 1.1 adds an in-game Skill Library (press K) to browse and safely test every skill, a custom
`count_people` skill, and SkillResult reporting: when an agent states a measured number, it is the number Unity
measured, never one Llama predicted.

Every action the model proposes is JSON that names a registered skill. Unity checks it against the skill
registry, the agent's configured permissions, and the live world before executing it, so the model never
runs code or invents coordinates. Missions end with an honest outcome (Succeeded, Partial, Failed, Cancelled,
or Unsupported), and built-in evaluation scenarios produce measured reports in text and JSON.

## Problem

AI agents are moving from chat windows into environments such as games, tools, and robots. There, three
problems become hard: understanding what an agent decided and why, preventing invalid or unsafe actions from
running, and measuring how reliably a team of agents completes a task. Most agent demos show a final answer
and hide the process.

## Solution

AgentWorld makes the process visible and checkable:
- skills are discoverable and testable in the Skill Library (K), with permissions and a dry-run validator;
- measured results come from Unity as a `SkillResult`, so a model-predicted number is never reported;
- the model's decisions, the agents' conversation, Unity's validation results, and each task's status appear
  live in a Mission Control panel and in speech bubbles;
- a skill registry turns every action into a validated, permissioned capability;
- an evaluation harness measures outcomes across repeatable scenarios.

## Technical architecture

- **Model and runtime:** Meta Llama 3.2 3B Instruct (open weights) served by Ollama on localhost. Unity calls
  `/api/chat` asynchronously with `UnityWebRequest`, using JSON mode for decisions, main-thread timeouts, and cancellation.
- **Skill registry:** eight skills (`move_to`, `inspect`, `report_findings`, `build_house`, `build_farm`, `speak`,
  `wait`, and the custom `count_people`), browsable in an in-game Skill Library (K) with a safe tester; each with an id, allowed roles, a parameter schema, a validator, an executor, and safety notes.
- **Agent configuration:** `agents.json` assigns titles, personalities, and skills to the existing agents and is
  validated at startup.
- **Mission pipeline:** Llama delegation → Unity-measured scouting → Llama dialogue → Llama site selection
  (validated against capacity) → construction with recovery → outcome.
- **Specialist routing:** Llama maps free-form requests to an intent; status and capability answers are built
  from live game state and the registry.
- **Evaluation:** scenarios (`village`, `farm`, `unsupported`) with measured metrics and JSON export.
- **Skill Library and SkillResult (v1.1):** in-game browser and safe tester for skills; measuring skills return a
  `SkillResult`, and speech that states a result is skipped before measurement or replaced by the measured value.
- **Quality:** EditMode 33/33 passed; PlayMode 42 passed, 0 failed, 6 opt-in live-Llama tests skipped (November 4,
  2026). One builder test timed out once at 30 s; its timeout is now 90 s, and it passed 5/5 in repeated runs.

## Meta technology used

Meta Llama 3.2 3B Instruct, run locally through Ollama. Llama makes every planning, delegation, site-selection,
and routing decision, and writes the agents' dialogue. No hosted inference, Meta Model API, or API keys are used.
The project does not use Muse Glimmer or Muse Spark.

## Innovation and differentiation

1. **Visible collaboration:** agents converse in the world, grounded in data Unity measured, and the dialogue
   feeds the coordinator's decision.
2. **Safe execution:** the model proposes and Unity disposes. Every action is a registered skill, checked for
   permission and validity before it runs, with retries and recovery when the model or the world disagree.
3. **Honest reporting and evaluation:** unsupported requests are refused with an explanation, and missions are
   measured rather than assumed to succeed.

## Developer impact

Developers can:
- reassign which agent may use which skill by editing one JSON file;
- add a new skill in C# by following a documented example that is also exercised by the test suite;
- run evaluation scenarios to compare behavior.

Because inference is local, experiments are free, private, and work offline.

## Verified results (November 4, 2026, Llama 3.2 3B on a laptop with an i7-13650HX, 16 GB RAM, RTX 3050)

- Evaluation suite: `village` passed (2/2 houses, 18.0 s), `farm` passed (2 houses and 1 farm, 28.8 s), and
  `unsupported` passed (castle request refused, 1.9 s).
- Specialist routing: 5 of 5 typical requests routed as expected.
- Windows build at 1920x1080: the village mission succeeded with no exceptions in the player log.
- v1.1.0 Windows build (demo video): `village` evaluation passed (2/2 houses, 0 invalid responses, 44.2 s); asked how
  many people were in the lab, Llama chose `count_people` and Unity measured 2.

## GitHub URL

https://github.com/tanakaseidy3103/AgentWorld

## Demo video description

Video: `AgentWorld-v1.1-Hackathon-Demo.mp4` (2:09, real v1.1.0 gameplay). Suggested text: "AgentWorld is an open-source visual lab where three AI agents powered by
Meta Llama 3.2 (running locally via Ollama) plan, talk, scout, and build together in Unity. Every model decision
is validated against the live world before it runs, and missions are evaluated with measured metrics."

## Installation and testing instructions

1. Install Ollama (https://ollama.com/download/windows) and run `ollama pull llama3.2`.
2. Either unzip `AgentWorld-Windows-x64-v1.1.0.zip` from the GitHub release and run `AgentWorld.exe`, or open the
   repository in Unity 6000.0.60f1. The world is generated on first open; then open `Assets/AgentWorld/Scenes/Main.unity` and press Play.
3. In game, press **M** and type `Build a small village near the lake`, then press Esc to watch Mission Control.
4. Optional: in a lab computer terminal, run `eval run village` for a measured report.
5. Press **K** to open the Skill Library; in a terminal, try `agent grace how many people are in the lab right now?`
5. Automated tests: Unity Test Runner (EditMode and PlayMode), or the batch commands in the README.

## Owner checklist (actions only the repository owner should take)

- [x] License: MIT (`LICENSE`)
- [x] v1.0.0 merged to `main` and released publicly
- [ ] Review and merge PR #2 (`feature/skill-library`, v1.1.0)
- [ ] Publish the GitHub release `v1.1.0` with `AgentWorld-Windows-x64-v1.1.0.zip` (notes in `docs/RELEASE_NOTES_v1.1.0.md`)
- [ ] Upload `AgentWorld-v1.1-Hackathon-Demo.mp4` (Unlisted or Public) and test the link in a private window
- [ ] Fill in and submit the form before 11:00 JST, November 5
