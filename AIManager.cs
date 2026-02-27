using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace GTA5MOD2026
{
    /// <summary>
    /// AI 大脑的管理员，负责和本地 LLM 服务器打交道的中间人
    /// 玩家和 NPC 说的每句话，都要经过这里去问 AI
    /// </summary>
    public class AIManager : IDisposable
    {
        // 复用一个 HttpClient，比每次请求都新建更高效
        private static readonly HttpClient _http = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        // 主线程回调队列 - 异步任务不能直接操作 GTA 游戏对象
        private readonly ConcurrentQueue<Action> _mainQueue
            = new ConcurrentQueue<Action>();

        // 记录哪些 NPC 正在等待 AI 回答，防止重复请求
        private readonly ConcurrentDictionary<int, bool> _npcPending
            = new ConcurrentDictionary<int, bool>();

        // 限流器 - 别把本地 LLM 服务器打爆了，好几个人同时说话也要排队
        private readonly SemaphoreSlim _semaphore
            = new SemaphoreSlim(1, 1);

        /// <summary>
        /// LLM API 地址，默认指向本地的 Ollama 服务
        /// </summary>
        public string Endpoint { get; set; }
            = "http://127.0.0.1:1234/v1/chat/completions";

        /// <summary>
        /// 用的模型名字，需要和你的 Ollama 配置匹配
        /// 默认是 qwen2.5-1.5b，在普通电脑上也能跑得动
        /// </summary>
        public string ModelName { get; set; }
            = "qwen2.5-1.5b-instruct";

        /// <summary>
        /// 每帧处理主线程队列里的回调
        /// 异步任务完成后会把结果丢到这里，等游戏主循环来取
        /// </summary>
        public void ProcessMainQueue()
        {
            int count = 0;
            while (_mainQueue.TryDequeue(out var action) && count < 5)
            {
                count++;
                try { action?.Invoke(); }
                catch { }
            }
        }

        /// <summary>
        /// 检查某个 NPC 是否正在等 AI 回复
        /// </summary>
        public bool IsNpcPending(int npcHandle)
            => _npcPending.ContainsKey(npcHandle);

        /// <summary>
        /// 异步请求 AI 生成回复
        /// 这是一个异步操作，不会卡住游戏主线程
        /// </summary>
        /// <param name="npcHandle">哪个 NPC 在等回答</param>
        /// <param name="jsonPayload">已经打包好的 prompt</param>
        /// <param name="onSuccess">AI 回答回来了，调用这个回调</param>
        /// <param name="onError">出错了，调用这个回调</param>
        public void RequestForNpcAsync(
            int npcHandle,
            string jsonPayload,
            Action<AIResponse> onSuccess,
            Action<Exception> onError = null)
        {
            // 已经在等了，别重复请求
            if (!_npcPending.TryAdd(npcHandle, true))
                return;

            Task.Run(async () =>
            {
                try
                {
                    // 抢到限流器的锁才开始问 AI
                    await _semaphore.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        // 设置超时，避免傻等
                        using (var cts = new CancellationTokenSource(
                            TimeSpan.FromSeconds(10)))
                        using (var content = new StringContent(
                            jsonPayload, Encoding.UTF8, "application/json"))
                        {
                            var resp = await _http.PostAsync(
                                Endpoint, content, cts.Token)
                                .ConfigureAwait(false);
                            resp.EnsureSuccessStatusCode();

                            var body = await resp.Content
                                .ReadAsStringAsync()
                                .ConfigureAwait(false);

                            // 解析 AI 返回的 JSON
                            var aiResp = ParseAIResponse(body);
                            aiResp.NpcHandle = npcHandle;

                            // 结果扔回主线程队列
                            _mainQueue.Enqueue(() => onSuccess?.Invoke(aiResp));
                        }
                    }
                    finally
                    {
                        // 用完锁要释放，让下一个请求进来
                        _semaphore.Release();
                    }
                }
                catch (Exception ex)
                {
                    _mainQueue.Enqueue(() => onError?.Invoke(ex));
                }
                finally
                {
                    // 不管成功还是失败，都要把等待标记清掉
                    _npcPending.TryRemove(npcHandle, out _);
                }
            });
        }

        /// <summary>
        /// 解析 AI 返回的 JSON，提取出动作、对话、情绪
        /// AI 可能返回各种奇奇怪怪的格式，这里尽量容错处理
        /// </summary>
        private AIResponse ParseAIResponse(string body)
        {
            var result = new AIResponse
            {
                action = "idle",
                dialogue = "",
                emotion = "neutral"
            };

            try
            {
                var jRoot = JObject.Parse(body);
                // 尝试从各种可能的路径拿 content
                string text = jRoot["choices"]?[0]?["message"]?["content"]
                    ?.ToString();

                if (string.IsNullOrWhiteSpace(text))
                {
                    result.dialogue = "[无响应]";
                    return result;
                }

                // 找 JSON 的开始和结束位置，有时候 AI 会说一堆废话再给 JSON
                int firstBrace = text.IndexOf('{');
                int lastBrace = text.LastIndexOf('}');

                if (firstBrace >= 0 && lastBrace > firstBrace)
                {
                    string jsonStr = text.Substring(
                        firstBrace, lastBrace - firstBrace + 1);
                    var parsed = JObject.Parse(jsonStr);

                    // 支持短字段名（a/d/e）和完整字段名
                    result.action = (parsed["a"] ?? parsed["action"])
                        ?.ToString()?.Trim() ?? "idle";
                    result.dialogue = (parsed["d"] ?? parsed["dialogue"])
                        ?.ToString()?.Trim() ?? "";
                    result.emotion = (parsed["e"] ?? parsed["emotion"])
                        ?.ToString()?.Trim() ?? "neutral";

                    // 检查动作是否合法，不合法就默认 idle
                    switch (result.action)
                    {
                        case "idle":
                        case "wave":
                        case "speak":
                        case "flee":
                        case "walk_to":
                            break;
                        default:
                            result.action = "idle";
                            break;
                    }

                    // 对话太长就截断，显示不下
                    if (result.dialogue.Length > 30)
                        result.dialogue = result.dialogue.Substring(0, 30);
                }
                else
                {
                    // AI 没返回 JSON，当作直接说话吧
                    result.dialogue = text.Length > 30
                        ? text.Substring(0, 30) : text;
                    result.action = "speak";
                }
            }
            catch
            {
                // 解析失败了，至少别崩溃
                result.dialogue = "[解析失败]";
            }

            return result;
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        public void Dispose()
        {
            _semaphore?.Dispose();
        }
    }
}
