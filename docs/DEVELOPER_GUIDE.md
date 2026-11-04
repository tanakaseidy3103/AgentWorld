# AgentWorld Developer Guide

This guide is for developers who want to configure the agents, add a skill, run missions and test the results.
Everything described here exists in the code; the limitations are listed at the end.

## 1. Architecture in one page

| Piece | File | Job |
|---|---|---|
| `ILlmClient`, `OllamaClient`, `LlmConfig` | `Scripts/Runtime/AI/LlmClient.cs` | Provider interface + local Ollama implementation (async `UnityWebRequest`) |
| `SkillRegistry`, `Skill`, `SkillContext` | `Scripts/Runtime/AI/SkillRegistry.cs` | Every capability: id, roles, parameters, `Validate`, `Execute`, safety notes |
| `AgentConfigs`, `agents.json` | `Scripts/Runtime/AI/AgentConfigs.cs`, `Assets/StreamingAssets/agents.json` | Which agent has which skills, its title and personality; validated at startup |
| `PlanValidator` | `Scripts/Runtime/AI/AgentPlan.cs` | Model JSON → registered skill? allowed for this agent? parameters valid against the world? |
| `LlamaAgentBrain` | `Scripts/Runtime/AI/LlamaAgentBrain.cs` | Per-agent state machine, memory, prompts, timeout, cancellation; executes skills |
| `MissionControl` | `Scripts/Runtime/AI/MissionControl.cs` | Multi-agent mission: delegation → scouting → dialogue → site selection → construction |
| `ExpertRouter` | `Scripts/Runtime/AI/ExpertRouter.cs` | Routes free-form requests to a specialist using Llama + the skill registry |
| `Evaluation` | `Scripts/Runtime/AI/Evaluation.cs` | Scenarios, measured metrics, text and JSON reports |
| `StructureBuilder`, `Landmark` | `Scripts/Runtime/AI/` | Free-site search, construction, persistence; named places the model may reference |
| `MissionPanel`, `TerminalUI` | `Scripts/Runtime/` | Mission Control dashboard, terminal, M mission bar |
| `WorldBuilder` | `Scripts/Editor/WorldBuilder.cs` | Generates all art, prefabs and the scene (runs automatically on a fresh clone) |

Rule of the house: **the model proposes, Unity disposes.** The LLM returns JSON that names skills and
location ids. It never supplies coordinates and is never executed as code.

## 2. Setup

1. Unity **6000.0.60f1** (Unity Hub). Open the folder; the world is generated on first open
   (or **AgentWorld → Rebuild World**).
2. Install Ollama (<https://ollama.com>), then `ollama pull llama3.2`. Check with `ollama list`.
3. Optional environment variables: `AGENTWORLD_MODEL`, `AGENTWORLD_OLLAMA_URL`, `AGENTWORLD_TIMEOUT`.
4. Play `Main.unity`. Mission Control (top right) shows whether Ollama is connected and which model is used.

## 3. Configuring agents (`Assets/StreamingAssets/agents.json`)

```json
{
  "version": 1,
  "agents": [
    { "id": "grace", "role": "explorer", "title": "Explorer",
      "personality": "curious and observant; loves describing what is around",
      "skills": ["move_to", "inspect", "report_findings", "speak", "wait"] }
  ]
}
```

| Field | Meaning |
|---|---|
| `id` | Must match an existing NPC: `ada`, `grace`, `linus` |
| `role` | `assistant` (coordinator), `explorer` or `builder`. The mission pipeline uses roles |
| `title` | Shown on the name plate ("Grace — Explorer") |
| `personality` | Added to the agent's prompts |
| `skills` | Skill ids from the registry. This is the agent's whole action space |

Startup validation (`AgentConfigs.Parse`): unknown skills, skills not allowed for the role, unknown roles
and duplicate ids are **reported and dropped**. Errors appear in the Unity console, in Mission Control and
in the `skills` terminal command. Invalid JSON falls back to built-in defaults.

The effect is real: the validator rejects plans that use skills the agent doesn't have, the prompt lists
only the agent's skills, missions refuse to start if Linus lacks `build_house`, Ada plans no farms if Linus
lacks `build_farm`, and "What can Linus build?" is answered from this list.

In a Windows build the file sits at `AgentWorld_Data/StreamingAssets/agents.json` and can be edited without
rebuilding.

## 4. Skills

Type `skills` in a lab terminal to list them. Built-in skills, all with real executors:

| Id | Roles | Parameters | What Unity does |
|---|---|---|---|
| `move_to` | all | `target` | Walks to the landmark's stand point (through the lab door if needed) |
| `inspect` | all | `target` | Walks there, describes nearby landmarks and people |
| `report_findings` | explorer | `target` | Measures free building sites there (`StructureBuilder.CountSites`) and reports the number |
| `build_house` | builder | `location` | Finds a free site next to the location, walks there, builds (scaffold, dust), persists |
| `build_farm` | builder | `location` | Same, with a walkable fenced farm |
| `speak` | all | `message` (≤200 chars) | Speech bubble |
| `wait` | all | `seconds` (0.5–10) | Pause |

The model writes actions in the original format: `{"type":"build","structure":"house","location":"lake"}`
maps to `build_house`, and `{"type":"report_findings","target":"lake"}` maps to `report_findings`.

### Complete example: how `report_findings` is registered (slightly simplified from `SkillRegistry.cs`)

```csharp
SkillRegistry.Register(new Skill
{
    Id = "report_findings", Name = "Report findings", Roles = new[] { "explorer" },
    Description = "Measure how many free building sites a location has right now and report it.",
    Parameters = new[] { "target: location id" },
    Safety = "Read-only; the number is computed by Unity, never by the model.",
    Example = "{\"type\":\"report_findings\",\"target\":\"<location id>\"}",
    // 1. Validation: normalize the id and require a real landmark.
    Validate = (a, locs) => locs.Contains(a.target = PlanValidator.Loc(a.target)) ? null : $"invalid location '{a.target}'",
    // 2. Execution on the main thread, with real world data.
    Execute = (c, ct) =>
    {
        var free = StructureBuilder.Instance.CountSites(Landmark.Find(c.Action.target).transform.position);
        var line = $"{c.Action.target}: {free} free building sites";
        c.Brain.Remember($"Measured {c.Action.target}: {free} free building sites");
        c.Say(line);
        c.Npc.Say(line);
        return Task.FromResult(line);
    },
});
```

Try it: `agent explorer how much free land is there in the east field?`. Grace's plan *can* use
`report_findings`, because it is in her skill list and in her prompt, and the number always comes from Unity.
Whether Llama 3.2 picks that skill for a given wording wasn't measured. The execution path is covered with a
mock plan by the PlayMode test `Grace_ReportFindingsSkill_MeasuresRealLand`.

### Complete, tested example: a new C# skill for an existing agent

This code is copied from `Assets/AgentWorld/Tests/PlayMode/CustomSkillExampleTests.cs`, and that test runs it.
It adds a `count_people` skill and gives it to Grace.

```csharp
SkillRegistry.Register(new Skill
{
    Id = "count_people",
    Name = "Count people",
    Description = "Count how many characters are within 5 units of a location.",
    Roles = new[] { "explorer", "assistant" },
    Parameters = new[] { "target: location id" },
    Safety = "Read-only.",
    Example = "{\"type\":\"count_people\",\"target\":\"<location id>\"}",
    Validate = (a, locations) =>
    {
        a.target = PlanValidator.Loc(a.target);
        return locations.Contains(a.target) ? null : $"invalid location '{a.target}'";
    },
    Execute = (c, ct) =>
    {
        Vector2 p = Landmark.Find(c.Action.target).transform.position;
        var n = Object.FindObjectsByType<NpcAgent>(FindObjectsSortMode.None).Count(x => Vector2.Distance(p, x.transform.position) < 5f);
        var line = $"{n} people near {c.Action.target}";
        c.Say(line);
        return Task.FromResult(line);
    },
});
```

Then add `"count_people"` to Grace's `skills` in `agents.json`:

```json
{ "id": "grace", "role": "explorer", "title": "Explorer",
  "skills": ["move_to", "inspect", "report_findings", "count_people", "speak", "wait"] }
```

What the test checks:
1. With the skill registered, that config validates without errors.
2. A plan `{"actions":[{"type":"count_people","target":"lab"}]}` runs on Grace and prints "N people near lab".
3. The same plan is rejected for Linus ("does not have skill 'count_people'").
4. Listing an unregistered skill in JSON (`"teleport"`) is rejected. **JSON alone cannot create a skill**; the C# registration is required.

Where to call `Register` in your project: at startup, before the first plan. For example, add it to
`SkillRegistry.RegisterBuiltIns()`, or call it from a `[RuntimeInitializeOnLoadMethod]` in your own script.
Run the example: `Unity.exe -batchmode -projectPath . -runTests -testPlatform PlayMode -testFilter AgentWorld.Tests.CustomSkillExampleTests`.

### Creating a new skill (steps)

1. Call `SkillRegistry.Register(new Skill { ... })`, either in `RegisterBuiltIns` or from your own code
   before the first plan runs. `Validate` and `Execute` are mandatory; ids must be unique.
2. **Validate:** check every parameter against the world (`locations` holds the valid landmark ids) and clamp
   numbers. Return an error string to reject the action.
3. **Execute:** use `c.Brain.MoveTo(...)`, `c.Brain.SetActivity(...)`, `c.Brain.Remember(...)`, `c.Say(...)` and
   Unity APIs. Honor the `CancellationToken` (use `Awaitable.*Async(ct)`). Throw an exception to fail the task
   with a message.
4. Add the id to an agent's `skills` in `agents.json`.
5. If the model should use it, give it an `Example`; the agent's prompt lists the examples of its skills.
6. Write tests: an EditMode test for `Validate`, and a PlayMode test with `MockLlm` returning a plan that uses it.

The `MissionControl` pipeline itself (scout → build) is C# and uses `inspect`, `build_house` and `build_farm`.
New skills become available to single-agent plans (`agent <role> <task>`) automatically; using them inside
the mission pipeline requires code changes.

## 5. Running and observing

- `mission <objective>` or **M** → multi-agent mission. Watch Mission Control: the Llama decisions,
  the agent conversation (Llama), Unity validation (rejections, recoveries), tasks, shared memory and outcome.
- `agent explorer <task>` → one agent, one Llama plan, validated, executed.
- Free sentences: "Find a safe place to build", "What can Linus build?", "Explain the current mission".
- Every mission line is also in `Debug.Log` with the `[mission]` prefix.

## 6. Evaluation

```
eval list
eval run village        # or farm, unsupported
eval report             # report for the last mission
```

The report uses only measured values: status vs expected status, houses/farms built, tasks done/failed,
invalid LLM responses (validator retries), recoveries, dialogue failures, dialogue turns and duration. It is
written as JSON to `%USERPROFILE%\AppData\LocalLow\DefaultCompany\AgentWorld\evaluations\`. Add scenarios in
`Evaluation.Scenarios` (objective + expected statuses).

## 7. Testing

```
Unity.exe -batchmode -projectPath . -executeMethod AgentWorld.EditorTools.WorldBuilder.Build -quit
Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults edit.xml
Unity.exe -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults play.xml
```

- **Deterministic tests** use `MockLlm` (`Tests/PlayMode/AiPlayModeTests.cs`): JSON replies are queued; `TextReply`
  answers dialogue calls; `FailText` simulates dialogue failures; `Delay` simulates slow models; `Prompts`
  records what the model saw. Reports from mocks say `mock-llama`.
- **Live tests** are `[Explicit]` and need Ollama: `LiveLlama_EvaluationSuite`, `LiveLlama_VillageMission`,
  `LiveLlama_UnsupportedMission`, `LiveLlama_BuilderBuildsHouse` (run them with `-testFilter`).

## 8. Safety constraints

- Only registered skills can run; unknown action types are rejected, and so is the whole plan (max 8 actions).
- Only landmark ids that exist in the scene are accepted. The model never provides coordinates.
- Construction placement is decided by Unity: inside the map, off roads and the lab, no overlap, clearance
  around each structure, nobody walled in, max 12 structures.
- LLM calls have timeouts (default 120 s) and can be cancelled (`mission stop`, `agent stop`); one mission at a time.
- Model text shown in the UI is escaped (no rich-text injection).
- No API keys or credentials anywhere; Ollama is called on localhost only.

## 9. Current limitations

- `agents.json` configures the three existing NPCs; it doesn't create new characters. New roles need code.
- One global model (`AGENTWORLD_MODEL`) for all agents.
- Skills are C#; there is no runtime plugin loading or scripting.
- The mission pipeline's steps are fixed in code (delegation, scouting, dialogue, selection, construction).
- Two structure types (house, farm). No roads or bridges.
