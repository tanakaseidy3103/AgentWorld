# AgentWorld (Unity) — MVP 2: Autonomous Llama Agents

Mundo 2D em pixel art habitado por **agentes autônomos movidos por Meta Llama rodando localmente via Ollama**.
Você dá um objetivo em linguagem natural; o Llama gera um plano JSON; o Unity valida e o NPC executa
andando pelo mapa e modificando o mundo de forma visível e persistente.

- Unity **6000.0.60f1** (Unity 6 LTS), Built-in Render Pipeline, 2D.
- Cena principal: `Assets/AgentWorld/Scenes/Main.unity` (gerada pelo `WorldBuilder`).
- IA: **Meta Llama 3.2 (3B) local via Ollama**. Sem Meta Model API, sem API paga, nada sai da máquina.

## O que a IA faz de verdade

| Agente | NPC | Pode |
|---|---|---|
| `builder` | Linus | andar, **construir casas**, inspecionar, falar |
| `explorer` | Grace | andar, inspecionar locais e relatar o que há por perto |
| `assistant` | Ada | explicar o mundo, inspecionar, falar |

- Pedido → Llama devolve **JSON estruturado** (`move_to`, `build`, `inspect`, `speak`, `wait`).
- O Unity **valida** tudo: ação desconhecida, local inexistente, estrutura inválida, mais de 8 ações
  ou build por quem não é builder ⇒ plano rejeitado (1 nova tentativa com o erro, depois falha).
- O LLM **nunca** manda coordenadas, código ou comandos de sistema: só ids de locais reais do mundo
  (`lab`, `plaza`, `lake`, `house_1..3`, `north_meadow`, `east_field` e casas construídas).
  O prompt é montado com o estado atual do mundo.
- Construção: o Unity escolhe um terreno livre perto do local (sem sobrepor nada, com corredor em volta,
  fora do laboratório), o NPC anda até lá, a casa sobe semitransparente e só vira sólida quando
  ninguém está dentro dela.
- Máquina de estados visível sobre cada agente: `Idle → Thinking → Planning → Moving → Acting → Completed`
  (+ `Failed`, `Cancelled`).
- Conversa: **E** num agente abre um chat; ele responde conforme papel, personalidade, tarefa atual,
  memória (últimos 12 fatos) e histórico (últimas 6 falas).
- Requisições assíncronas, uma por vez por agente, intervalo mínimo de 2s, timeout de 120s, cancelável.
  O jogo nunca trava.

No terminal, `[Llama]` marca o que o modelo decidiu e `[Unity]` marca o que o jogo executou.

## Instalação (Windows)

1. **Ollama**: baixe em <https://ollama.com/download/windows> e instale (ele fica rodando na bandeja,
   em `http://localhost:11434`).
2. **Modelo Llama** (≈2 GB):
   ```
   ollama pull llama3.2
   ollama list            # confirme que llama3.2 aparece
   ```
   Máquina mais fraca: `ollama pull llama3.2:1b` e `setx AGENTWORLD_MODEL llama3.2:1b`.
3. Configuração opcional (variáveis de ambiente; reabra o Unity Hub depois do `setx`):

   | Variável | Padrão |
   |---|---|
   | `AGENTWORLD_OLLAMA_URL` | `http://localhost:11434` |
   | `AGENTWORLD_MODEL` | `llama3.2` |
   | `AGENTWORLD_TIMEOUT` | `90` (segundos por requisição HTTP) |

## Abrir e jogar

1. Unity Hub → **Add project from disk** → esta pasta → editor **6000.0.60f1**.
2. Menu **AgentWorld → Rebuild World** (gera sprites, prefabs e a cena; necessário após clonar).
3. Abra `Assets/AgentWorld/Scenes/Main.unity` e aperte **Play**.

| Tecla | Ação |
|---|---|
| WASD | andar |
| E | computador: terminal · agente IA: conversa |
| Enter / Esc | enviar / fechar |
| F5 / F9 | salvar / carregar o mundo |

### Comandos do terminal

```
help, echo, whoami, agents, time, ide, clear      (MVP 1, inalterados)
agent list                       agentes e estado
agent status                     objetivo, último resultado, memória
agent builder <tarefa>           ex: agent builder construa uma casa perto do lago
agent explorer <tarefa>          ex: agent explorer inspecione o lago e a casa 3
agent assistant <tarefa>         ex: agent assistant explique o laboratório
agent stop <id|papel>            cancela a tarefa
world status | world structures
ai status                        testa o Ollama e se o modelo está instalado
```

O save (`%USERPROFILE%\AppData\LocalLow\DefaultCompany\AgentWorld\world.json`, versão 2) guarda
casas construídas, memória/objetivo/estado dos agentes, posições e logs. Saves do MVP 1 continuam
carregando. Nenhuma credencial é salva.

## Arquitetura

```
 Terminal / Chat (IMGUI)                         Ollama (localhost:11434)
        │ agent builder ...                         ▲  POST /api/chat  format=json
        ▼                                           │
 LocalShellBackend ──Assign──► LlamaAgentBrain ──► OllamaClient (UnityWebRequest, async)
   (resposta imediata)          │  estados, memória, timeout, cancel
                                │  JSON ─► PlanValidator (whitelist + locais reais)
                                ▼
                     execução no main thread do Unity
             move_to ─► NpcAgent (física, desvio, porta do lab)
             build   ─► StructureBuilder (terreno livre, animação, colisão)
             inspect ─► Landmark (locais nomeados)
                                │
                                ▼
                    WorldSaveManager ─► world.json (v2)
```

Detalhes em [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Como usamos IA open-weight da Meta

O cérebro dos agentes é o **Llama 3.2** (pesos abertos da Meta) servido localmente pelo Ollama.
Ele interpreta o pedido do jogador, decide o plano e escreve as falas dos NPCs. O Unity trata a saída
como **dado não confiável**: ela só vira ação depois de passar por uma whitelist. Rodar local
significa grátis, offline, privado e com latência previsível na demo.

## Testes

Window → General → Test Runner, ou:

```
Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults TestResults-EditMode.xml
Unity.exe -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults TestResults-PlayMode.xml
```

Os testes usam um **LLM mock determinístico** (`MockLlm`) e cobrem: parsing do JSON, validação, respostas
inválidas, ação insegura, transições de estado, construção, save/load das casas, Ollama fora do ar,
cancelamento, timeout, terminal antigo + novo e o chat.

Teste com Llama real (opcional, precisa do Ollama rodando):

```
Unity.exe -batchmode -projectPath . -runTests -testPlatform PlayMode -testFilter AgentWorld.Tests.AiPlayModeTests.LiveLlama_BuilderBuildsHouse -testResults TestResults-Live.xml
```

## Roteiro de demo (3 min)

1. **0:00** AgentWorld: um mundo onde agentes de IA vivem e trabalham.
2. **0:20** Passeie: lab, lago, NPCs com nome, papel e estado sobre a cabeça.
3. **0:45** Entre no lab, E num computador, `ai status`, depois `agent builder construa uma casa perto do lago`.
4. **1:20** Mostre `[Llama] plano: ...`, o Linus andando até o lago e a casa subindo.
5. **2:20** F5, sai do Play, Play de novo: a casa continua lá. Ou E na Grace e pergunte o que ela viu.
6. **2:45** Visão: cada NPC um agente, cada computador um portal da Living IDE.

## Limitações conhecidas

- Só a estrutura `house` existe; sem pathfinding real (linha reta + desvio + rota pela porta do lab).
  Um caminho bloqueado por muito tempo faz a tarefa falhar com mensagem.
- Modelos de 1–3B às vezes erram o JSON; a validação rejeita e o agente tenta uma vez de novo.
- Uma tarefa por agente; tarefas em andamento não são retomadas depois de carregar um save.
- Sem colaboração entre agentes (P2). O comando `ide` ainda é só um protótipo de texto.
- Primeira resposta pode demorar enquanto o Ollama carrega o modelo na memória.
