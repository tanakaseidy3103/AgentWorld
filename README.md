# AgentWorld (Unity)

Mundo 2D em pixel art onde, no futuro, agentes LLM e a *Living IDE* vão morar. Este é o **MVP 1**.

- Unity **6000.0.60f1** (Unity 6 LTS), Built-in Render Pipeline, 2D.
- Cena principal: `Assets/AgentWorld/Scenes/Main.unity`

## Abrir e jogar

1. Unity Hub → **Add** → **Add project from disk** → selecione esta pasta.
2. Abra com o editor **6000.0.60f1**.
3. Abra `Assets/AgentWorld/Scenes/Main.unity` e aperte **Play**.

| Tecla | Ação |
|---|---|
| WASD | andar |
| E | interagir (computador abre o terminal; NPC conversa) |
| Enter / Esc | enviar comando / fechar terminal |
| F5 / F9 | salvar / carregar o mundo |

Comandos do terminal: `help`, `echo`, `whoami`, `agents`, `time`, `ide`, `clear`.

O save fica em `%USERPROFILE%\AppData\LocalLow\DefaultCompany\AgentWorld\world.json`
(carregado ao iniciar, salvo ao sair).

## Regerar o mundo

Sprites, Prefabs e a cena são gerados por `Assets/AgentWorld/Scripts/Editor/WorldBuilder.cs`.
No editor: menu **AgentWorld → Rebuild World**. Em linha de comando:

```
Unity.exe -batchmode -quit -projectPath . -executeMethod AgentWorld.EditorTools.WorldBuilder.Build
```

## Testes

Window → General → Test Runner (EditMode e PlayMode), ou:

```
Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults TestResults-EditMode.xml
Unity.exe -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults TestResults-PlayMode.xml
```

## Arquitetura

Veja [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
