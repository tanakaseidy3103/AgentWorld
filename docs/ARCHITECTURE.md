# Arquitetura (final sprint)

```
Assets/AgentWorld/
  Scripts/Runtime   AgentWorld.Runtime   (jogo)
  Scripts/Editor    AgentWorld.Editor    (WorldBuilder: gera arte, prefabs, cena)
  Tests/EditMode    testes de save, terminal, cena e prefabs
  Tests/PlayMode    testes jogando a cena real
  Art/ Prefabs/ Scenes/   gerados pelo WorldBuilder
```

## Agentes IA (MVP 2)

| Peça | Arquivo | Papel |
|---|---|---|
| `ILlmClient` / `OllamaClient` | `Runtime/AI/LlmClient.cs` | HTTP assíncrono para o Ollama (`/api/chat`, `/api/tags`); config por env vars |
| `AgentPlan` / `PlanValidator` | `Runtime/AI/AgentPlan.cs` | parse tolerante do JSON + whitelist de ações, locais e estruturas |
| `LlamaAgentBrain` / `AgentHub` | `Runtime/AI/LlamaAgentBrain.cs` | máquina de estados, prompt com estado do mundo, memória, chat, timeout/cancel |
| `Landmark` | `Runtime/AI/Landmark.cs` | locais nomeados que o LLM pode citar |
| `StructureBuilder` / `BuiltStructure` | `Runtime/AI/StructureBuilder.cs` | busca de terreno livre, construção animada, capture/restore |
| `MissionControl` | `Runtime/AI/MissionControl.cs` | missão multi-agente: delegação (Llama) → exploração (Unity mede terrenos) → memória compartilhada → seleção (Llama) → construção com recuperação |
| `MissionPanel` | `Runtime/MissionPanel.cs` | painel Mission Control (Tab); separa decisões do Llama de ações do Unity |
| `NpcChatBackend` | `Runtime/TerminalBackend.cs` | chat com NPC reaproveitando a UI do terminal |

Regras:
- `ITerminalBackend.Execute` continua síncrono; trabalho longo escreve depois em `Computer.log`.
- Toda continuação `async` volta ao main thread (UnityWebRequest + `Awaitable`), então mexer em GameObjects é seguro.
  O timeout é medido no main thread (`WithTimeout`), não com timers de outra thread.
- O LLM só produz dados; nada do que ele escreve é executado como código.
- Cada `MonoBehaviour` fica num arquivo com o mesmo nome da classe (exigência do Unity para serializar na cena).

## Pontos de extensão para o MVP 2

| Interface | Hoje | Futuro |
|---|---|---|
| `ITerminalBackend.Execute(input, computer)` | `LocalShellBackend` (comandos locais) | backend da **Living IDE** / agente LLM por computador |
| `IAgentBrain.Steer / Talk` | `WanderBrain` (passeia e fala falas fixas) | cérebro LLM: decide movimento e responde em linguagem natural |
| `IInteractable` | `Computer`, `NpcAgent` | portas, painéis da IDE, objetos de missão |
| `WorldState` (JSON, `version`) | jogador, NPCs, logs dos terminais | memória dos agentes, arquivos da IDE |

`Computer.Backend` e `NpcAgent.Brain` são propriedades trocáveis em tempo de execução, então um
backend LLM pode ser injetado sem mudar UI, cena ou save.

`IAgentBrain` continua síncrona (`Steer`, `Talk`, `OnStuck`); o `LlamaAgentBrain` roda a tarefa em
`async` e `Steer` só lê o alvo atual.
