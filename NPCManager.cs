using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace GTA5MOD2026
{
    public class NPCManager : Script
    {
        private static NPCManager _instance;
        public static NPCManager Instance => _instance;

        private readonly AIManager aiManager;
        private readonly MemoryManager memoryManager;
        private readonly VoiceManager voiceManager;
        private readonly SpeechManager speechManager;

        private readonly ConcurrentQueue<AIResponse> responseQueue
            = new ConcurrentQueue<AIResponse>();

        private Ped targetNpc = null;
        private NPCState targetState = null;
        private readonly Dictionary<int, NPCState> npcStates
            = new Dictionary<int, NPCState>();

        private bool voiceEnabled = true;
        private const float INTERACT_DISTANCE = 5f;
        private const float MENU_SHOW_DISTANCE = 8f;

        private float lastRequestTime = 0f;
        private const float REQUEST_COOLDOWN = 2f;
        private float lastDialogueNotifTime = 0f;

        private static readonly string[] COMPLIMENTS = new[]
        {
            "你今天看起来很不错！",
            "你真是个好人！",
            "你的衣服很好看！",
            "你看起来很酷！",
            "今天天气真好，和你一样！",
            "你是这条街最靓的仔！",
            "兄弟你真帅！",
            "你笑起来真好看！",
        };

        private static readonly string[] INSULTS = new[]
        {
            "你长得真难看！",
            "你是我见过最蠢的人！",
            "滚远点，别挡路！",
            "你看什么看，没见过人吗？",
            "你身上好臭！",
            "你是从垃圾堆里爬出来的吗？",
            "你这个废物！",
            "闭嘴，没人想听你说话！",
        };

        private static readonly string[] PERSONALITIES =
        {
            "友善", "冷漠", "暴躁", "胆小", "搞笑"
        };

        private static readonly string[] NPC_NAMES =
        {
            "Tony", "Mike", "Lucy", "Dave", "Rosa",
            "Jack", "Emma", "Alex", "Lisa", "Sam",
            "Rick", "Nina", "Carl", "Amy", "Pete"
        };

        private readonly Random _rand = new Random();

        private static void ShowNotification(string text)
        {
            Function.Call(Hash.BEGIN_TEXT_COMMAND_THEFEED_POST,
                "STRING");
            Function.Call(
                Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME,
                text);
            Function.Call(
                Hash.END_TEXT_COMMAND_THEFEED_POST_MESSAGETEXT,
                "CHAR_DEFAULT", "CHAR_DEFAULT", false, 0,
                "AI-NPC", "");
        }

        private static void ShowHelpText(string text)
        {
            Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_HELP,
                "STRING");
            Function.Call(
                Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME,
                text);
            Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_HELP,
                0, false, true, -1);
        }

        public NPCManager()
        {
            _instance = this;
            aiManager = new AIManager();
            memoryManager = new MemoryManager();
            voiceManager = new VoiceManager();
            speechManager = new SpeechManager();
            voiceManager.PreGenerateCommonPhrases();

            ShowNotification(
                "AI NPC v3.0\n" +
                "G=Compliment H=Insult T=Type J=Voice\n" +
                "F8=Voice F6=Status");

            Tick += OnTick;
            Interval = 0;
            KeyDown += OnKeyDown;
        }

        private void OnTick(object sender, EventArgs e)
        {
            aiManager.ProcessMainQueue();
            voiceManager.ProcessMainQueue();
            speechManager.ProcessMainQueue();
            ProcessResponses();

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            float gameTime = Game.GameTime / 1000f;

            FindNearestNPC(player);

            if (targetNpc != null && targetState != null)
            {
                float dist = Vector3.Distance(
                    player.Position, targetNpc.Position);

                if (dist < MENU_SHOW_DISTANCE)
                    DrawInteractionMenu(dist);
            }

            if (targetNpc != null && targetState != null)
            {
                float dist = Vector3.Distance(
                    player.Position, targetNpc.Position);
                AutoReact(targetState, player, dist, gameTime);
            }

            foreach (var kvp in npcStates)
            {
                var state = kvp.Value;
                if (!state.IsValid()) continue;
                DrawNPCText(state);
            }
        }

        private void FindNearestNPC(Ped player)
        {
            Ped nearest = null;
            float nearestDist = MENU_SHOW_DISTANCE;

            Ped[] nearbyPeds = World.GetNearbyPeds(
                player, MENU_SHOW_DISTANCE);

            foreach (var ped in nearbyPeds)
            {
                if (ped == null || !ped.Exists()) continue;
                if (ped.IsDead) continue;
                if (ped == player) continue;
                if (ped.IsInVehicle()) continue;

                float dist = Vector3.Distance(
                    player.Position, ped.Position);

                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = ped;
                }
            }

            if (nearest != null)
            {
                targetNpc = nearest;

                if (!npcStates.ContainsKey(nearest.Handle))
                {
                    string personality = PERSONALITIES[
                        _rand.Next(PERSONALITIES.Length)];
                    string name = NPC_NAMES[
                        _rand.Next(NPC_NAMES.Length)];

                    npcStates[nearest.Handle] = new NPCState
                    {
                        Ped = nearest,
                        Personality = personality,
                        NpcName = name
                    };

                    memoryManager.GetMemory(
                        nearest.Handle, personality);
                }

                targetState = npcStates[nearest.Handle];
            }
            else
            {
                targetNpc = null;
                targetState = null;
            }

            var toRemove = new List<int>();
            foreach (var kvp in npcStates)
            {
                if (!kvp.Value.IsValid())
                {
                    toRemove.Add(kvp.Key);
                    continue;
                }

                float dist = Vector3.Distance(
                    player.Position, kvp.Value.Ped.Position);
                if (dist > 100f)
                {
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var h in toRemove)
                npcStates.Remove(h);
        }

        private void AutoReact(NPCState state, Ped player,
            float dist, float gameTime)
        {
            if (state.IsInteracting) return;
            if (state.WaitingForAI) return;

            if (IsPlayerAimingAt(state.Ped))
            {
                if (state.CurrentAction != "flee_aim")
                {
                    state.CurrentAction = "flee_aim";
                    string dialogue;
                    switch (state.Personality)
                    {
                        case "暴躁":
                            dialogue = "你敢开枪试试！";
                            break;
                        case "胆小":
                            dialogue = "别别别！求你了！";
                            break;
                        case "搞笑":
                            dialogue = "别开枪！我还没结婚！";
                            break;
                        default:
                            dialogue = "冷静点！把枪放下！";
                            break;
                    }
                    ShowNPCResponse(state, dialogue);
                    state.Ped.Task.ClearAll();
                    state.Ped.Task.FleeFrom(player);
                }
                return;
            }

            if (player.IsShooting && dist < 20f)
            {
                if (state.CurrentAction != "flee_shoot")
                {
                    state.CurrentAction = "flee_shoot";
                    ShowNPCResponse(state, "有人开枪！快跑！");
                    state.Ped.Task.ClearAll();
                    state.Ped.Task.FleeFrom(player);
                }
                return;
            }

            if (dist < 2f && state.CurrentAction != "too_close"
                && (gameTime - state.LastRequestTime) > 8f)
            {
                state.CurrentAction = "too_close";
                state.LastRequestTime = gameTime;

                string dialogue;
                switch (state.Personality)
                {
                    case "暴躁":
                        dialogue = "你离我太近了！滚开！";
                        break;
                    case "胆小":
                        dialogue = "你...你要干嘛？";
                        break;
                    case "友善":
                        dialogue = "你有什么事吗？";
                        break;
                    case "搞笑":
                        dialogue = "哥们你是要亲我吗？";
                        break;
                    case "冷漠":
                        dialogue = "...走开。";
                        break;
                    default:
                        dialogue = "请保持距离。";
                        break;
                }
                ShowNPCResponse(state, dialogue);
            }

            if (dist > 10f)
            {
                state.CurrentAction = "idle";
            }
        }

        private void DrawInteractionMenu(float dist)
        {
            if (targetState == null) return;

            string menuText;

            if (speechManager.IsRecording)
            {
                menuText = "Recording... speak now";
            }
            else if (targetState.WaitingForAI)
            {
                menuText = "NPC thinking...";
            }
            else if (dist < INTERACT_DISTANCE)
            {
                string name = targetState.NpcName;
                var mem = memoryManager.GetMemory(
                    targetNpc.Handle, targetState.Personality);

                menuText =
                    $"{name} Favor:{mem.Relationship}\n" +
                    $"~g~G~w~ Compliment  " +
                    $"~r~H~w~ Insult\n" +
                    $"~b~T~w~ Type  " +
                    $"~y~J~w~ Voice";
            }
            else
            {
                menuText = $"Get closer to {targetState.NpcName}";
            }

            ShowHelpText(menuText);
        }

        private void HandleCompliment()
        {
            if (targetNpc == null || targetState == null) return;
            if (targetState.WaitingForAI) return;

            float dist = Vector3.Distance(
                Game.Player.Character.Position,
                targetNpc.Position);
            if (dist > INTERACT_DISTANCE) return;

            float gameTime = Game.GameTime / 1000f;
            if ((gameTime - lastRequestTime) < REQUEST_COOLDOWN)
                return;
            lastRequestTime = gameTime;

            string compliment = COMPLIMENTS[
                _rand.Next(COMPLIMENTS.Length)];

            SendInteraction(targetState, compliment);
        }

        private void HandleInsult()
        {
            if (targetNpc == null || targetState == null) return;
            if (targetState.WaitingForAI) return;

            float dist = Vector3.Distance(
                Game.Player.Character.Position,
                targetNpc.Position);
            if (dist > INTERACT_DISTANCE) return;

            float gameTime = Game.GameTime / 1000f;
            if ((gameTime - lastRequestTime) < REQUEST_COOLDOWN)
                return;
            lastRequestTime = gameTime;

            string insult = INSULTS[
                _rand.Next(INSULTS.Length)];

            SendInteraction(targetState, insult);
        }

        private void HandleVoiceInput()
        {
            if (targetNpc == null || targetState == null) return;
            if (targetState.WaitingForAI) return;
            if (speechManager.IsRecording) return;

            float dist = Vector3.Distance(
                Game.Player.Character.Position,
                targetNpc.Position);
            if (dist > INTERACT_DISTANCE) return;

            ShowNotification("Recording... speak now");

            int npcHandle = targetNpc.Handle;

            speechManager.RecordAndTranscribe(
                text =>
                {
                    ShowNotification("You: " + text);

                    if (npcStates.TryGetValue(npcHandle,
                        out var state))
                    {
                        SendInteraction(state, text);
                    }
                },
                ex =>
                {
                    ShowNotification("Voice failed: " + ex.Message);
                }
            );
        }

        private void HandleTextInput()
        {
            if (targetNpc == null || targetState == null) return;
            if (targetState.WaitingForAI) return;

            float dist = Vector3.Distance(
                Game.Player.Character.Position,
                targetNpc.Position);
            if (dist > INTERACT_DISTANCE) return;

            float gameTime = Game.GameTime / 1000f;
            if ((gameTime - lastRequestTime) < REQUEST_COOLDOWN)
                return;

            string input = Game.GetUserInput("");
            if (!string.IsNullOrEmpty(input) && input.Length > 30)
                input = input.Substring(0, 30);

            if (!string.IsNullOrEmpty(input)
                && input.Trim().Length > 0)
            {
                lastRequestTime = Game.GameTime / 1000f;
                ShowNotification("You: " + input);
                SendInteraction(targetState, input.Trim());
            }
        }

        private void SendInteraction(NPCState state,
            string playerText)
        {
            if (!state.IsValid()) return;

            state.WaitingForAI = true;
            state.IsInteracting = true;

            state.Ped.Task.TurnTo(Game.Player.Character);

            string memory = memoryManager.BuildMemoryContext(
                state.Ped.Handle);

            string system;
            if (string.IsNullOrEmpty(memory))
            {
                system =
                    $"GTA5 NPC,{state.Personality}。只回一个JSON。\n" +
                    "示例:{\"a\":\"speak\",\"d\":\"你好啊！\"}\n" +
                    "a只能选:idle,wave,speak,flee\n" +
                    "d:15字内中文,符合性格地回应玩家";
            }
            else
            {
                system =
                    $"GTA5 NPC,{state.Personality}。只回一个JSON。\n" +
                    $"记忆:{memory}\n" +
                    "示例:{\"a\":\"speak\",\"d\":\"又是你！\"}\n" +
                    "a只能选:idle,wave,speak,flee\n" +
                    "d:15字内中文,根据记忆和性格回应";
            }

            string user = $"玩家说:\"{playerText}\"";

            var promptObj = new
            {
                model = aiManager.ModelName,
                messages = new[]
                {
                    new { role = "system", content = system },
                    new { role = "user", content = user }
                },
                max_tokens = 40,
                temperature = 0.6,
                stop = new[] { "\n\n", "```" }
            };

            string payload = JsonConvert.SerializeObject(promptObj);

            aiManager.RequestForNpcAsync(
                state.Ped.Handle,
                payload,
                resp => responseQueue.Enqueue(resp),
                ex =>
                {
                    state.WaitingForAI = false;
                    ShowNotification("AI错误: " + ex.Message);
                }
            );
        }

        private void ProcessResponses()
        {
            float gameTime = Game.GameTime / 1000f;

            while (responseQueue.TryDequeue(out var resp))
            {
                if (!npcStates.TryGetValue(resp.NpcHandle,
                    out var state))
                    continue;
                if (!state.IsValid()) continue;

                string action = resp.action ?? "speak";
                string dialogue = resp.dialogue ?? "";
                string emotion = resp.emotion ?? "neutral";

                memoryManager.RecordInteraction(
                    resp.NpcHandle,
                    "talking",
                    dialogue,
                    emotion,
                    state.ThreatLevel,
                    GetTimeOfDay(),
                    gameTime
                );

                ShowNPCResponse(state, dialogue);

                state.Ped.Task.ClearAll();
                switch (action)
                {
                    case "flee":
                        state.Ped.Task.FleeFrom(
                            Game.Player.Character);
                        break;
                    case "wave":
                        PlayAnim(state.Ped,
                            "anim@mp_player_intcelebrationmale@wave",
                            "wave");
                        break;
                    case "speak":
                        state.Ped.Task.TurnTo(
                            Game.Player.Character);
                        break;
                    case "idle":
                    default:
                        state.Ped.Task.StandStill(5000);
                        break;
                }

                if (voiceEnabled && !string.IsNullOrEmpty(dialogue))
                {
                    string voice = voiceManager.GetVoiceForNpc(
                        resp.NpcHandle, true);
                    string voiceEmotion = voiceManager
                        .PersonalityToEmotion(
                            state.Personality, emotion);
                    state.IsPlayingVoice = true;
                    voiceManager.SpeakAsync(
                        resp.NpcHandle, dialogue,
                        voice, voiceEmotion,
                        () => { state.IsPlayingVoice = false; },
                        ex => { state.IsPlayingVoice = false; }
                    );
                }

                state.WaitingForAI = false;
                state.InteractionCount++;
            }
        }

        private void ShowNPCResponse(NPCState state, string dialogue)
        {
            state.LastLLMDialogue = dialogue;
            state.DialogueShowTime = Game.GameTime / 1000f;

            float gameTime = Game.GameTime / 1000f;
            if (!state.HasActiveDialogue(gameTime)) return;
            if ((gameTime - lastDialogueNotifTime) < 1.0f) return;
            lastDialogueNotifTime = gameTime;

            Function.Call(Hash.BEGIN_TEXT_COMMAND_THEFEED_POST, "STRING");
            Function.Call(
                Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME,
                dialogue);
            Function.Call(
                Hash.END_TEXT_COMMAND_THEFEED_POST_MESSAGETEXT,
                "CHAR_DEFAULT", "CHAR_DEFAULT", false, 0,
                $"{state.NpcName}[{state.Personality}]", "");
        }

        private void DrawNPCText(NPCState state)
        {
            if (!state.IsValid()) return;

            var ped = state.Ped;
            float dist = Vector3.Distance(
                Game.Player.Character.Position, ped.Position);
            if (dist > 15f) return;

            Vector3 headPos = ped.Bones[Bone.SkelHead].Position;
            headPos.Z += 0.3f;

            int r = 255, g = 255, b = 255;
            string pTag = "";
            switch (state.Personality)
            {
                case "友善": r = 100; g = 255; b = 100; pTag = "Kind"; break;
                case "暴躁": r = 255; g = 80; b = 80; pTag = "Angry"; break;
                case "胆小": r = 255; g = 255; b = 100; pTag = "Timid"; break;
                case "搞笑": r = 255; g = 165; b = 0; pTag = "Funny"; break;
                case "冷漠": r = 180; g = 180; b = 180; pTag = "Cold"; break;
                default: r = 255; g = 255; b = 255; pTag = "NPC"; break;
            }

            string status = $"{state.NpcName} [{pTag}]";
            if (state.WaitingForAI)
                status += " thinking...";
            else if (state.IsPlayingVoice)
                status += " speaking...";
            else if (state.HasActiveDialogue(Game.GameTime / 1000f))
                status += " (!)";

            float scale = Math.Max(0.25f, 0.4f - (dist / 40f));
            DrawText3D(headPos, status, r, g, b, scale);
        }

        private static void DrawText3D(Vector3 pos, string text,
            int r, int g, int b, float scale)
        {
            var outX = new OutputArgument();
            var outY = new OutputArgument();
            bool visible = Function.Call<bool>(
                Hash.GET_SCREEN_COORD_FROM_WORLD_COORD,
                pos.X, pos.Y, pos.Z, outX, outY);

            if (!visible) return;

            float sx = outX.GetResult<float>();
            float sy = outY.GetResult<float>();

            Function.Call(Hash.SET_TEXT_FONT, 4);
            Function.Call(Hash.SET_TEXT_SCALE, scale, scale);
            Function.Call(Hash.SET_TEXT_COLOUR, 0, 0, 0, 220);
            Function.Call(Hash.SET_TEXT_CENTRE, true);
            Function.Call(Hash.SET_TEXT_OUTLINE);
            Function.Call(
                Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
            Function.Call(
                Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME,
                text);
            Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT,
                sx + 0.001f, sy + 0.001f);

            Function.Call(Hash.SET_TEXT_FONT, 4);
            Function.Call(Hash.SET_TEXT_SCALE, scale, scale);
            Function.Call(Hash.SET_TEXT_COLOUR, r, g, b, 255);
            Function.Call(Hash.SET_TEXT_CENTRE, true);
            Function.Call(Hash.SET_TEXT_OUTLINE);
            Function.Call(
                Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
            Function.Call(
                Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME,
                text);
            Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT,
                sx, sy);
        }

        private bool IsPlayerAimingAt(Ped target)
        {
            if (!Function.Call<bool>(
                Hash.IS_PLAYER_FREE_AIMING, Game.Player.Handle))
                return false;

            var outEntity = new OutputArgument();
            if (Function.Call<bool>(
                Hash.GET_ENTITY_PLAYER_IS_FREE_AIMING_AT,
                Game.Player.Handle, outEntity))
            {
                return outEntity.GetResult<int>()
                    == target.Handle;
            }
            return false;
        }

        private void PlayAnim(Ped ped, string dict, string anim)
        {
            Function.Call(Hash.REQUEST_ANIM_DICT, dict);
            int timeout = 500;
            while (!Function.Call<bool>(
                Hash.HAS_ANIM_DICT_LOADED, dict) && timeout > 0)
            {
                Script.Wait(10);
                timeout -= 10;
            }
            if (Function.Call<bool>(
                Hash.HAS_ANIM_DICT_LOADED, dict))
            {
                Function.Call(Hash.TASK_PLAY_ANIM, ped.Handle,
                    dict, anim, 8.0f, -8.0f, 3000, 49, 0,
                    false, false, false);
            }
        }

        private string GetTimeOfDay()
        {
            int hour = Function.Call<int>(Hash.GET_CLOCK_HOURS);
            if (hour >= 6 && hour < 12) return "早上";
            if (hour >= 12 && hour < 18) return "下午";
            if (hour >= 18 && hour < 22) return "晚上";
            return "深夜";
        }

        private void OnKeyDown(object sender,
            System.Windows.Forms.KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case System.Windows.Forms.Keys.G:
                    HandleCompliment();
                    break;
                case System.Windows.Forms.Keys.H:
                    HandleInsult();
                    break;
                case System.Windows.Forms.Keys.J:
                    HandleVoiceInput();
                    break;
                case System.Windows.Forms.Keys.T:
                    HandleTextInput();
                    break;
                case System.Windows.Forms.Keys.F8:
                    voiceEnabled = !voiceEnabled;
                    ShowNotification(
                        voiceEnabled
                            ? "Voice ON"
                            : "Voice OFF");
                    break;
                case System.Windows.Forms.Keys.F9:
                    memoryManager.SaveAll();
                    ShowNotification("Memory saved");
                    break;
                case System.Windows.Forms.Keys.F6:
                    if (targetState != null)
                    {
                        var mem = memoryManager.GetMemory(
                            targetNpc.Handle,
                            targetState.Personality);
                        ShowNotification(
                            $"{targetState.NpcName}\n" +
                            $"Favor:{mem.Relationship}\n" +
                            $"Met:{mem.TotalInteractions}\n" +
                            $"Rel:{mem.PlayerReputation}");
                    }
                    else
                    {
                        ShowNotification("No NPC nearby");
                    }
                    break;
            }
        }
    }
}
