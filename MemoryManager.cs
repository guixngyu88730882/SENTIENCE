using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace GTA5MOD2026
{
    public class MemoryEntry
    {
        public string PlayerAction { get; set; }
        public string NpcResponse { get; set; }
        public string Emotion { get; set; }
        public int ThreatLevel { get; set; }
        public string Time { get; set; }
        public float Timestamp { get; set; }
    }

    public class NPCMemory
    {
        public string Personality { get; set; }
        public List<MemoryEntry> ShortTerm { get; set; }
            = new List<MemoryEntry>();
        public List<string> LongTerm { get; set; }
            = new List<string>();
        public int TotalInteractions { get; set; } = 0;
        public int TimesAttacked { get; set; } = 0;
        public int TimesFriendly { get; set; } = 0;
        public string PlayerReputation { get; set; } = "stranger";
        public int Relationship { get; set; } = 0;
    }

    public class MemoryManager
    {
        private readonly Dictionary<int, NPCMemory> _memories
            = new Dictionary<int, NPCMemory>();

        private const int MAX_SHORT_TERM = 5;
        private const int MAX_LONG_TERM = 10;

        private static readonly string SaveDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "GTA5MOD2026", "memory");

        public MemoryManager()
        {
            if (!Directory.Exists(SaveDir))
                Directory.CreateDirectory(SaveDir);
        }

        public NPCMemory GetMemory(int npcHandle, string personality)
        {
            if (!_memories.ContainsKey(npcHandle))
            {
                _memories[npcHandle] = new NPCMemory
                {
                    Personality = personality
                };
            }
            return _memories[npcHandle];
        }

        public void RecordInteraction(int npcHandle,
            string playerAction, string npcResponse,
            string emotion, int threat, string time,
            float gameTime)
        {
            if (!_memories.ContainsKey(npcHandle)) return;
            var mem = _memories[npcHandle];

            mem.ShortTerm.Add(new MemoryEntry
            {
                PlayerAction = playerAction,
                NpcResponse = npcResponse,
                Emotion = emotion,
                ThreatLevel = threat,
                Time = time,
                Timestamp = gameTime
            });

            while (mem.ShortTerm.Count > MAX_SHORT_TERM)
            {
                var oldest = mem.ShortTerm[0];
                mem.ShortTerm.RemoveAt(0);
                SummarizeToLongTerm(mem, oldest);
            }

            mem.TotalInteractions++;

            if (threat >= 4)
            {
                mem.TimesAttacked++;
                mem.Relationship = Math.Max(-100,
                    mem.Relationship - 15);
            }
            else if (playerAction == "approaching"
                || playerAction == "very_close")
            {
                mem.TimesFriendly++;
                mem.Relationship = Math.Min(100,
                    mem.Relationship + 5);
            }

            mem.PlayerReputation = ComputeReputation(mem);
        }

        private void SummarizeToLongTerm(NPCMemory mem,
            MemoryEntry entry)
        {
            string summary;
            if (entry.ThreatLevel >= 4)
                summary = $"玩家曾{entry.PlayerAction}，很危险";
            else if (entry.ThreatLevel >= 2)
                summary = $"玩家曾靠近，有点紧张";
            else
                summary = $"玩家曾友好地{entry.PlayerAction}";

            mem.LongTerm.Add(summary);

            while (mem.LongTerm.Count > MAX_LONG_TERM)
                mem.LongTerm.RemoveAt(0);
        }

        private string ComputeReputation(NPCMemory mem)
        {
            if (mem.Relationship <= -50) return "enemy";
            if (mem.Relationship <= -20) return "hostile";
            if (mem.Relationship <= 10) return "stranger";
            if (mem.Relationship <= 40) return "acquaintance";
            if (mem.Relationship <= 70) return "friend";
            return "close_friend";
        }

        public string BuildMemoryContext(int npcHandle)
        {
            if (!_memories.ContainsKey(npcHandle))
                return "";

            var mem = _memories[npcHandle];
            var parts = new List<string>();
            parts.Add($"关系:{mem.PlayerReputation}({mem.Relationship})");

            if (mem.TimesAttacked > 0)
                parts.Add($"被攻击{mem.TimesAttacked}次");
            if (mem.TotalInteractions > 3)
                parts.Add($"见过{mem.TotalInteractions}次");

            var recent = mem.ShortTerm
                .Skip(Math.Max(0, mem.ShortTerm.Count - 2))
                .ToList();

            foreach (var entry in recent)
            {
                parts.Add($"上次:玩家{entry.PlayerAction}→你{entry.NpcResponse}");
            }

            return string.Join("。", parts);
        }

        public void SaveAll()
        {
            try
            {
                string json = JsonConvert.SerializeObject(
                    _memories, Formatting.Indented);
                File.WriteAllText(
                    Path.Combine(SaveDir, "npc_memory.json"), json);
            }
            catch { }
        }

        public void ClearMemory(int npcHandle)
        {
            _memories.Remove(npcHandle);
        }

        public void ClearAll()
        {
            _memories.Clear();
        }
    }
}
