using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace AgentWorld
{
    /// Text-in/text-out LLM. OllamaClient for the real game, a mock in tests.
    public interface ILlmClient
    {
        string Model { get; }
        /// json=true asks the model for a JSON object (Ollama "format": "json").
        Task<string> Chat(string system, string user, bool json, CancellationToken ct);
        /// Returns installed model names; throws if the server is unreachable.
        Task<string[]> ListModels(CancellationToken ct);
    }

    /// Endpoint/model come from env vars so nothing is hardcoded in the build.
    public static class LlmConfig
    {
        public static string Endpoint => Env("AGENTWORLD_OLLAMA_URL", "http://localhost:11434").TrimEnd('/');
        public static string Model => Env("AGENTWORLD_MODEL", "llama3.2");
        public static int TimeoutSeconds => int.TryParse(Environment.GetEnvironmentVariable("AGENTWORLD_TIMEOUT"), out var t) && t > 0 ? t : 90;

        static string Env(string key, string fallback)
        {
            var v = Environment.GetEnvironmentVariable(key);
            return string.IsNullOrWhiteSpace(v) ? fallback : v.Trim();
        }
    }

    public class OllamaClient : ILlmClient
    {
        [Serializable] class Msg { public string role, content; }
        [Serializable] class Options { public float temperature = 0.2f; public int num_predict = 400; }
        [Serializable] class ChatReq { public string model; public bool stream; public string format; public Msg[] messages; public Options options = new Options(); }
        [Serializable] class ChatRes { public Msg message; public string error; }
        [Serializable] class Tag { public string name; }
        [Serializable] class Tags { public Tag[] models; }

        readonly string endpoint;
        public string Model { get; }
        public int TimeoutSeconds { get; set; } = LlmConfig.TimeoutSeconds;

        public OllamaClient(string endpoint = null, string model = null)
        {
            this.endpoint = (endpoint ?? LlmConfig.Endpoint).TrimEnd('/');
            Model = model ?? LlmConfig.Model;
        }

        public async Task<string> Chat(string system, string user, bool json, CancellationToken ct)
        {
            var body = JsonUtility.ToJson(new ChatReq
            {
                model = Model,
                format = json ? "json" : null,
                messages = new[] { new Msg { role = "system", content = system }, new Msg { role = "user", content = user } },
            });
            if (!json) body = body.Replace("\"format\":\"\",", ""); // JsonUtility writes null strings as ""
            var text = await Send(UnityWebRequest.kHttpVerbPOST, "/api/chat", body, ct);
            var res = JsonUtility.FromJson<ChatRes>(text);
            if (!string.IsNullOrEmpty(res?.error)) throw new Exception("Ollama: " + res.error);
            return res?.message?.content ?? throw new Exception("Ollama: empty response");
        }

        public async Task<string[]> ListModels(CancellationToken ct)
        {
            var tags = JsonUtility.FromJson<Tags>(await Send(UnityWebRequest.kHttpVerbGET, "/api/tags", null, ct));
            return tags?.models == null ? new string[0] : Array.ConvertAll(tags.models, m => m.name);
        }

        async Task<string> Send(string method, string path, string body, CancellationToken ct)
        {
            using var req = new UnityWebRequest(endpoint + path, method) { downloadHandler = new DownloadHandlerBuffer(), timeout = TimeoutSeconds };
            if (body != null)
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                req.SetRequestHeader("Content-Type", "application/json");
            }
            using (ct.Register(req.Abort))
                await req.SendWebRequest();
            ct.ThrowIfCancellationRequested();
            if (req.result != UnityWebRequest.Result.Success)
            {
                var detail = req.downloadHandler?.text;
                if (req.responseCode == 404 && detail != null && detail.Contains("not found"))
                    throw new Exception($"model '{Model}' is not installed. Run: ollama pull {Model}");
                throw new Exception($"Ollama unavailable at {endpoint} ({req.error})");
            }
            return req.downloadHandler.text;
        }
    }
}
