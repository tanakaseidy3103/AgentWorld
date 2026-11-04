# AgentWorld Developer Guide

This guide is for developers who want to configure the agents, add a skill, run missions and test the results.
Everything described here exists in the code; the limitations are listed at the end.

## 1. Architecture in one page

| Piece | File | Job |
|---|---|---|
| `ILlmClient`, `OllamaClient`, `LlmConfig` | `Scripts/Runtime/AI/LlmClient.cs` | Provider interface + local Ollama implementation (async `UnityWebRequest`) |
| `SkillRegistry`, `Skill`, `SkillContext` | `Scripts/Runtime/AI/SkillRegistry.cs` | Every capability: id, roles, parameters, `Validate`, `Execute`, safety notes |
| `CustomSkills` | `Scripts/Runtime/AI/CustomSkills.cs` | Skills added through the public `Register` API (ships `count_people`) |
| `SkillLibrary`, `SkillLibraryPanel` | `Scripts/Runtime/AI/SkillLibrary.cs`, `Scripts/Runtime/SkillLibraryPanel.cs` | Skill Library (K), safe skill tester, generated skill docs |
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
      "skills": ["move_to", "inspect", "report_findings", "count_people", "speak", "wait"] }
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

### Skill Library (in game)

Press **K** anywhere in the world to open the Skill Library. Mouse or keyboard: PgUp/PgDn select a skill, Tab
picks the agent, Enter validates, Ctrl+Enter runs, Esc closes. It lists:

- every **implemented** skill (registered in C#, with a validator and an executor), marked `custom` when it was
  added through the public API rather than built in;
- the **documentation-only** examples (`water_crops`, `trade_items`), greyed out. They are not registered, so no
  agent can use them and any plan that names them is rejected;
- for the selected skill: name, description, parameters, the JSON the model writes, the roles allowed to hold it,
  its safety notes, and **who can perform it**. Each agent shows ✔, or ✘ with the reason: the role is not allowed,
  or the role is allowed but `agents.json` doesn't enable it.

At the bottom is a **safe tester**. Pick an agent and edit the JSON action:

- **Validate (dry run)** runs the same `PlanValidator` the model's plans go through and executes nothing. Errors are
  specific, for example `builder does not have skill 'count_people'`, `invalid location 'moon' in target`
  followed by the list of valid ids, or `'water_crops' is a documentation-only example`.
- **Run on <agent>** validates first, then the agent performs the action through the normal executors. No LLM is
  involved, and nothing runs while a mission is in progress or the agent is busy.

The same tools exist in a lab terminal:

```
skills                                                  # overview
skill count_people                                      # details and who can perform it
skill test grace {"type":"count_people","target":"lab"} # dry run
skill run grace {"type":"count_people","target":"lab"}  # validate, then perform
```

The tester only accepts JSON actions that name registered skills. There is no path from model or player text
to code execution: an action such as `{"type":"exec","code":"..."}` is rejected as an unknown action type.

### Skill reference (generated)

This table is generated from the registry and `agents.json` by **AgentWorld → Update Skill Docs**
(batch: `-executeMethod AgentWorld.EditorTools.WorldBuilder.UpdateSkillDocs`). The EditMode test
`DeveloperGuide_SkillReference_IsUpToDate` fails when it is stale.

<!-- SKILLS:BEGIN (generated by AgentWorld > Update Skill Docs; do not edit) -->
| Skill | Origin | Parameters | Roles allowed | Enabled for | Safety |
|---|---|---|---|---|---|
| `build_farm` Build farm: Build a walkable fenced farm on the nearest free site next to a location. | built-in | location: location id | builder | linus | Unity picks the exact site: inside the map, off roads and the lab, no overlap, clearance around it; max 12 structures. |
| `build_house` Build house: Build a house on the nearest free site next to a location. | built-in | location: location id | builder | linus | Unity picks the exact site: inside the map, off roads and the lab, no overlap, clearance around it; max 12 structures. |
| `count_people` Count people: Walk to a location and count the other characters within 5 units of it. | custom (CustomSkills.cs) | target: location id | explorer, assistant | grace | Read-only; the count is computed by Unity, never by the model. |
| `inspect` Inspect: Walk to a location and describe what is nearby (landmarks, people). | built-in | target: location id | assistant, explorer, builder | ada, grace, linus | Read-only. |
| `move_to` Move to: Walk to a named location (route through the lab door when needed). | built-in | target: location id | assistant, explorer, builder | ada, grace, linus | Only known landmark ids; no coordinates from the model. |
| `report_findings` Report findings: Measure how many free building sites a location has right now and report it. | built-in | target: location id | explorer | grace | Read-only; the number is computed by Unity, never by the model. |
| `speak` Speak: Say one short sentence in a speech bubble. | built-in | message: text (max 200 chars) | assistant, explorer, builder | ada, grace, linus | Text only; shown escaped in the UI. |
| `wait` Wait: Pause for a few seconds. | built-in | seconds: 0.5 to 10 | assistant, explorer, builder | ada, grace, linus | Clamped to 0.5-10 s. |

Documentation-only examples (not registered; agents cannot use them and plans naming them are rejected):

- `water_crops`: Water the crops of a farm. Not implemented: there is no crop-growth system yet.
- `trade_items`: Exchange items with another agent. Not implemented: agents have no inventory.
<!-- SKILLS:END -->

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

### Complete, working example: a custom C# skill (`count_people`)

`count_people` ships with AgentWorld in `Assets/AgentWorld/Scripts/Runtime/AI/CustomSkills.cs` and is registered
with nothing but the public API, exactly as your own skill would be. Grace has it in `agents.json`; open the Skill
Library (K) to see it marked `custom`. The code below is copied from that file:

```csharp
public static void RegisterAll()
{
    if (SkillRegistry.Get("count_people") == null) SkillRegistry.Register(CountPeople());
}

public static Skill CountPeople() => new Skill
{
    Id = "count_people",
    Name = "Count people",
    Origin = "custom (CustomSkills.cs)",
    ReportsResult = true, // speech in the same plan must use this measured count
    Description = "Walk to a location and count the other characters within 5 units of it.",
    Roles = new[] { "explorer", "assistant" },
    Parameters = new[] { "target: location id" },
    Safety = "Read-only; the count is computed by Unity, never by the model.",
    Example = "{\"type\":\"count_people\",\"target\":\"<location id>\"}",
    // 1. Validation: normalize the id and require a real landmark. Return an error to reject.
    Validate = (a, locations) =>
    {
        a.target = PlanValidator.Loc(a.target);
        return locations.Contains(a.target) ? null : $"invalid location '{a.target}' in target";
    },
    // 2. Execution on Unity's main thread with real world data.
    Execute = async (c, ct) =>
    {
        var place = Landmark.Find(c.Action.target);
        await c.Brain.MoveTo(place.StandPoint, c.Action.target, ct);
        c.Brain.SetActivity(AgentStatus.Acting, "counting people");
        Vector2 p = place.transform.position;
        var people = Object.FindObjectsByType<NpcAgent>(FindObjectsSortMode.None).Where(n => n != c.Npc).Select(n => n.transform)
            .Concat(Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None).Select(x => x.transform))
            .Count(t => Vector2.Distance(p, t.position) < CountRadius);
        var line = $"{people} {(people == 1 ? "person" : "people")} near {c.Action.target}, not counting me";
        c.Brain.Remember($"Counted {line}");
        c.Say(line);
        c.Npc.Say(line);
        return line;
    },
};
```

`CustomSkills.RegisterAll()` is called once when the registry starts, right after the built-ins, so the skill
exists before `agents.json` is validated. Add your own `Register` calls there.

It is enabled for Grace in `agents.json`:

```json
{ "id": "grace", "role": "explorer", "title": "Explorer",
  "skills": ["move_to", "inspect", "report_findings", "count_people", "speak", "wait"] }
```

What the tests check (`Tests/PlayMode/CustomSkillExampleTests.cs`, `Tests/EditMode/SkillLibraryEditModeTests.cs`):
1. With the skill registered, that config validates without errors.
2. A plan `{"actions":[{"type":"count_people","target":"lab"}]}` runs on Grace: she walks to the lab and says
   "N people near lab, not counting me" ("1 person" when there is one). The Skill Library tester runs the same action the same way.
3. The same plan is rejected for Linus ("does not have skill 'count_people'").
4. Listing an unregistered skill in JSON (`"teleport"`) is rejected. **JSON alone cannot create a skill**; the C# registration is required.

Run the example: `Unity.exe -batchmode -projectPath . -runTests -testPlatform PlayMode -testFilter AgentWorld.Tests.CustomSkillExampleTests`.

### Creating a new skill (steps)

1. Call `SkillRegistry.Register(new Skill { ... })` from `CustomSkills.RegisterAll()`. Set `Origin` so the
   Skill Library shows where it comes from. `Validate` and `Execute` are mandatory; ids must be unique.
2. **Validate:** check every parameter against the world (`locations` holds the valid landmark ids) and clamp
   numbers. Return an error string to reject the action.
3. **Execute:** use `c.Brain.MoveTo(...)`, `c.Brain.SetActivity(...)`, `c.Brain.Remember(...)`, `c.Say(...)` and
   Unity APIs. Honor the `CancellationToken` (use `Awaitable.*Async(ct)`). Throw an exception to fail the task
   with a message.
4. Add the id to an agent's `skills` in `agents.json`.
5. If the model should use it, give it an `Example`; the agent's prompt lists the examples of its skills.
6. Open the Skill Library (K), select it, and use **Validate (dry run)** and **Run** to try it in the world.
7. Run **AgentWorld → Update Skill Docs** so the generated reference above includes it.
8. Write tests: an EditMode test for `Validate`, and a PlayMode test with `MockLlm` returning a plan that uses it.

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

- **Measured results come from Unity, not the model.** Skills with `ReportsResult = true` (`count_people`,
  `report_findings`, `inspect`) return a `SkillResult`. In a plan that contains one, a `speak` action that states a
  result (a number, "nobody", ...) is skipped if nothing has been measured yet in this task, and replaced by the
  SkillResult Unity returned otherwise. Results are never carried over from earlier tasks, and a failed skill ends
  the task before any later speech. Tests: `ResultReportingPlayModeTests`.

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
