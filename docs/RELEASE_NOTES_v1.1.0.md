# AgentWorld v1.1.0: Skill Library

v1.1.0 makes the skill system easy to discover, test and extend, and it guarantees that agents report only
results Unity actually measured. v1.0.0 and its downloads are unchanged.

## New

- **Skill Library (press K).** Lists every implemented skill and, separately, the documentation-only examples.
  For each skill it shows the description, parameters, the JSON the model writes, the roles allowed to hold it,
  safety notes, and which agent can perform it. If an agent can't, it says why. Works with mouse or keyboard
  (PgUp/PgDn, Tab, Enter, Ctrl+Enter, Esc).
- **Safe skill tester.** *Validate* is a dry run through the same validator the model's plans use. *Run* validates
  first and then the agent performs the action. Errors are specific: missing permission, invalid location (the
  valid ids are listed), documentation-only skill. The same tester is available in the terminal as `skill <id>`,
  `skill test <agent> <json>` and `skill run <agent> <json>`.
- **`count_people`, a working custom skill.** It is registered through the public API in `CustomSkills.cs`, and
  Grace has it in `agents.json`. Grace walks to a place and reports how many other characters Unity counts there.
  Llama 3.2 picks it on its own for questions like "how many people are in the lab right now?".
- **Generated skill reference.** The skill table in `docs/DEVELOPER_GUIDE.md` is generated from the registry
  and `agents.json` (**AgentWorld → Update Skill Docs**). A test fails when the table is stale.

## Fixed

- **No more predicted results.** In v1.0.x, Llama could write a speech line such as "There are 2 people in the
  lab" into its plan before any counting happened. Measuring skills (`count_people`, `report_findings`, `inspect`)
  now return a `SkillResult`, and speech that states a result reports that measured value. If nothing has been
  measured yet in the task, the line is skipped. Results never carry over from earlier tasks, and a failed skill
  ends the task before any later speech.
- The custom skill says "1 person" instead of "1 people".

## Safety

Model output is still JSON that names registered skills. It is never executed as code, and actions such as
`{"type":"exec",...}` are rejected as unknown.

## Tests

EditMode and PlayMode suites with a deterministic mock LLM, including new regressions for fabricated counts,
counts claimed before measuring, failed skills and stale results. There is also an opt-in live-Llama test,
`LiveLlama_GraceCountsPeople_SpeaksOnlyTheMeasuredCount`.

## Requirements

Windows 10/11 x64, and Ollama with `ollama pull llama3.2`. Unzip `AgentWorld-Windows-x64-v1.1.0.zip` and run
`AgentWorld.exe`.
