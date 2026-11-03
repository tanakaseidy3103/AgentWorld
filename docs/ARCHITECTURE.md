# Arquitetura (MVP 1)

```
Assets/AgentWorld/
  Scripts/Runtime   AgentWorld.Runtime   (jogo)
  Scripts/Editor    AgentWorld.Editor    (WorldBuilder: gera arte, prefabs, cena)
  Tests/EditMode    testes de save, terminal, cena e prefabs
  Tests/PlayMode    testes jogando a cena real
  Art/ Prefabs/ Scenes/   gerados pelo WorldBuilder
```

## Pontos de extensão para o MVP 2

| Interface | Hoje | Futuro |
|---|---|---|
| `ITerminalBackend.Execute(input, computer)` | `LocalShellBackend` (comandos locais) | backend da **Living IDE** / agente LLM por computador |
| `IAgentBrain.Steer / Talk` | `WanderBrain` (passeia e fala falas fixas) | cérebro LLM: decide movimento e responde em linguagem natural |
| `IInteractable` | `Computer`, `NpcAgent` | portas, painéis da IDE, objetos de missão |
| `WorldState` (JSON, `version`) | jogador, NPCs, logs dos terminais | memória dos agentes, arquivos da IDE |

`Computer.Backend` e `NpcAgent.Brain` são propriedades trocáveis em tempo de execução, então um
backend LLM pode ser injetado sem mudar UI, cena ou save.

Observação: `IAgentBrain` é síncrona. Um cérebro LLM deve fazer a chamada de rede em segundo plano
e retornar o último resultado disponível em `Steer`/`Talk`.
