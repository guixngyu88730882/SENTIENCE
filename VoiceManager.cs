using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace GTA5MOD2026
{
    public class VoiceManager : IDisposable
    {
        private readonly ConcurrentQueue<Action> _mainQueue
            = new ConcurrentQueue<Action>();

        private static readonly string AudioDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "GTA5MOD2026", "audio");

        private static readonly string[] MALE_VOICES = new[]
        {
            "zh-CN-YunxiNeural",
            "zh-CN-YunjianNeural",
        };

        private static readonly string[] FEMALE_VOICES = new[]
        {
            "zh-CN-XiaoxiaoNeural",
            "zh-CN-XiaoyiNeural",
        };

        private readonly Random _rand = new Random();
        private readonly ConcurrentDictionary<int, string> _npcVoices
            = new ConcurrentDictionary<int, string>();
        private readonly ConcurrentDictionary<string, string> _audioCache
            = new ConcurrentDictionary<string, string>();
        private volatile bool _isPlaying = false;

        public VoiceManager()
        {
            if (!Directory.Exists(AudioDir))
                Directory.CreateDirectory(AudioDir);
            CleanOldAudio();
        }

        public string GetVoiceForNpc(int npcHandle, bool isMale = true)
        {
            return _npcVoices.GetOrAdd(npcHandle, _ =>
            {
                var voices = isMale ? MALE_VOICES : FEMALE_VOICES;
                return voices[_rand.Next(voices.Length)];
            });
        }

        private struct EmotionParams
        {
            public string Rate;
            public string Pitch;
            public string Volume;
        }

        private EmotionParams GetEmotionParams(string emotion)
        {
            switch (emotion)
            {
                case "angry":
                    return new EmotionParams
                    {
                        Rate = "+30%",
                        Pitch = "-10Hz",
                        Volume = "+20%"
                    };

                case "scared":
                    return new EmotionParams
                    {
                        Rate = "+50%",
                        Pitch = "+15Hz",
                        Volume = "+10%"
                    };

                case "happy":
                    return new EmotionParams
                    {
                        Rate = "+15%",
                        Pitch = "+5Hz",
                        Volume = "+0%"
                    };

                case "sad":
                    return new EmotionParams
                    {
                        Rate = "-15%",
                        Pitch = "-8Hz",
                        Volume = "-10%"
                    };

                case "cold":
                    return new EmotionParams
                    {
                        Rate = "-20%",
                        Pitch = "-5Hz",
                        Volume = "-5%"
                    };

                case "neutral":
                default:
                    return new EmotionParams
                    {
                        Rate = "+0%",
                        Pitch = "+0Hz",
                        Volume = "+0%"
                    };
            }
        }

        public string PersonalityToEmotion(string personality,
            string responseEmotion)
        {
            if (!string.IsNullOrEmpty(responseEmotion)
                && responseEmotion != "neutral")
                return responseEmotion;

            switch (personality)
            {
                case "暴躁": return "angry";
                case "胆小": return "scared";
                case "友善": return "happy";
                case "搞笑": return "happy";
                case "冷漠": return "cold";
                default: return "neutral";
            }
        }

        public void PreGenerateCommonPhrases()
        {
            Task.Run(() =>
            {
                string[] commonPhrases = new[]
                {
                    "Hello friend",
                    "Get lost",
                    "Dont shoot",
                    "Help me",
                    "What do you want",
                    "Go away",
                };

                string voice = "zh-CN-YunxiNeural";

                foreach (var phrase in commonPhrases)
                {
                    try
                    {
                        string filename = $"cache_{phrase.GetHashCode():X8}.mp3";
                        string filepath = Path.Combine(AudioDir, filename);

                        if (File.Exists(filepath))
                        {
                            _audioCache[phrase] = filepath;
                            continue;
                        }

                        var psi = new ProcessStartInfo
                        {
                            FileName = "python",
                            Arguments = $"-m edge_tts " +
                                $"--voice \"{voice}\" " +
                                $"--text \"{phrase}\" " +
                                $"--write-media \"{filepath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };

                        using (var proc = Process.Start(psi))
                        {
                            proc.WaitForExit(10000);
                            if (proc.ExitCode == 0 && File.Exists(filepath))
                            {
                                _audioCache[phrase] = filepath;
                            }
                        }
                    }
                    catch { }
                }
            });
        }

        public void SpeakAsync(int npcHandle, string text,
            string voice, string emotion = "neutral",
            Action onComplete = null,
            Action<Exception> onError = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (_isPlaying) return;

            if (_audioCache.TryGetValue(text, out string cachedPath)
                && File.Exists(cachedPath))
            {
                Task.Run(() =>
                {
                    _isPlaying = true;
                    try
                    {
                        PlayAudioSync(cachedPath, false);
                        _mainQueue.Enqueue(() => onComplete?.Invoke());
                    }
                    finally
                    {
                        _isPlaying = false;
                    }
                });
                return;
            }

            string filename = $"npc_{npcHandle}_{DateTime.Now.Ticks}.mp3";
            string filepath = Path.Combine(AudioDir, filename);
            var emo = GetEmotionParams(emotion);

            Task.Run(() =>
            {
                _isPlaying = true;
                try
                {
                    string args =
                        $"-m edge_tts " +
                        $"--voice \"{voice}\" " +
                        $"--rate=\"{emo.Rate}\" " +
                        $"--pitch=\"{emo.Pitch}\" " +
                        $"--volume=\"{emo.Volume}\" " +
                        $"--text \"{EscapeText(text)}\" " +
                        $"--write-media \"{filepath}\"";

                    var psi = new ProcessStartInfo
                    {
                        FileName = "python",
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using (var proc = Process.Start(psi))
                    {
                        bool exited = proc.WaitForExit(8000);

                        if (exited && proc.ExitCode == 0
                            && File.Exists(filepath))
                        {
                            PlayAudioSync(filepath, true);
                            _mainQueue.Enqueue(() => onComplete?.Invoke());
                        }
                        else
                        {
                            if (!exited)
                            {
                                try { proc.Kill(); } catch { }
                            }
                            _mainQueue.Enqueue(() => onError?.Invoke(
                                new Exception("TTS failed")));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _mainQueue.Enqueue(() => onError?.Invoke(ex));
                }
                finally
                {
                    _isPlaying = false;
                }
            });
        }

        private void PlayAudioSync(string filepath, bool deleteAfter)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments =
                        "-NoProfile -Command \"" +
                        "Add-Type -AssemblyName PresentationCore; " +
                        "$p = New-Object System.Windows.Media.MediaPlayer; " +
                        "$p.Open([Uri]::new('" +
                        filepath.Replace("'", "''") +
                        "')); " +
                        "$p.Play(); " +
                        "Start-Sleep -Milliseconds 500; " +
                        "while($p.Position -lt $p.NaturalDuration.TimeSpan " +
                        "-and $p.Position -ne [TimeSpan]::Zero) " +
                        "{ Start-Sleep -Milliseconds 200 }; " +
                        "Start-Sleep -Milliseconds 300; " +
                        "$p.Close()\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(psi))
                {
                    proc.WaitForExit(10000);
                    if (!proc.HasExited)
                    {
                        try { proc.Kill(); } catch { }
                    }
                }

                if (deleteAfter)
                    try { File.Delete(filepath); } catch { }
            }
            catch { }
        }

        private string EscapeText(string text)
        {
            return text
                .Replace("\"", "'")
                .Replace("\n", " ")
                .Replace("\r", "")
                .Replace("&", "and")
                .Replace("|", " ");
        }

        private void CleanOldAudio()
        {
            try
            {
                foreach (var file in Directory.GetFiles(AudioDir, "*.mp3"))
                {
                    if (Path.GetFileName(file).StartsWith("cache_"))
                        continue;
                    var age = DateTime.Now - File.GetCreationTime(file);
                    if (age.TotalMinutes > 10)
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }
        }

        public void ProcessMainQueue()
        {
            int count = 0;
            while (_mainQueue.TryDequeue(out var action) && count < 3)
            {
                count++;
                try { action?.Invoke(); }
                catch { }
            }
        }

        public void Dispose()
        {
            CleanOldAudio();
        }
    }
}
