using GTA;

namespace GTA5MOD2026
{
    public class NPCState
    {
        public const float DIALOGUE_DURATION = 6f;

        public Ped Ped { get; set; }
        public string CurrentAction { get; set; } = "idle";
        public int LastStateHash { get; set; } = 0;
        public float LastRequestTime { get; set; } = 0f;
        public string Personality { get; set; } = "normal";
        public string LastLLMDialogue { get; set; } = "";
        public float DialogueShowTime { get; set; } = 0f;
        public bool IsPlayingVoice { get; set; } = false;
        public string NpcName { get; set; } = "";
        public string LastPlayerAction { get; set; } = "idle";
        public int ThreatLevel { get; set; } = 0;
        public int InteractionCount { get; set; } = 0;
        public bool IsInteracting { get; set; } = false;
        public bool WaitingForAI { get; set; } = false;

        public bool HasActiveDialogue(float gameTime)
        {
            return !string.IsNullOrEmpty(LastLLMDialogue)
                && (gameTime - DialogueShowTime) < DIALOGUE_DURATION;
        }

        public bool IsValid()
        {
            return Ped != null && Ped.Exists() && !Ped.IsDead;
        }

        public int Handle => Ped?.Handle ?? -1;
    }
}
