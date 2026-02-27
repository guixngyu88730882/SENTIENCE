using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace GTA5MOD2026
{
    public class SpeechManager
    {
        private readonly ConcurrentQueue<Action> _mainQueue
            = new ConcurrentQueue<Action>();

        private volatile bool _isRecording = false;
        public bool IsRecording => _isRecording;

        private static readonly string TempDir = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments),
            "GTA5MOD2026", "temp");
        private static readonly string ModelPath
            = @"C:\whisper-tiny";

        public SpeechManager()
        {
            if (!Directory.Exists(TempDir))
                Directory.CreateDirectory(TempDir);

            WriteSttScript();
        }

        private void WriteSttScript()
        {
            string scriptPath = Path.Combine(TempDir, "stt.py");

            string pyScript = @"
import sys
import os
import tempfile
import wave

import sounddevice as sd
import numpy as np

def main():
    DURATION = 5
    SAMPLE_RATE = 16000
    MODEL_PATH = r'" + ModelPath + @"'

    try:
        print('REC_START', flush=True)
        audio = sd.rec(int(DURATION * SAMPLE_RATE),
                       samplerate=SAMPLE_RATE,
                       channels=1, dtype='int16')
        sd.wait()
        print('REC_DONE', flush=True)

        vol = np.abs(audio).mean()
        print(f'VOLUME:{vol}', flush=True)
        if vol < 10:
            print('ERROR:too_quiet')
            return

        tmp = os.path.join(tempfile.gettempdir(), 'gta_stt.wav')
        with wave.open(tmp, 'wb') as wf:
            wf.setnchannels(1)
            wf.setsampwidth(2)
            wf.setframerate(SAMPLE_RATE)
            wf.writeframes(audio.tobytes())

        print('TRANSCRIBING', flush=True)
        try:
            os.environ['HF_HUB_OFFLINE'] = '1'
            from faster_whisper import WhisperModel
            model = WhisperModel(MODEL_PATH, device='cpu', compute_type='int8')
            segments, info = model.transcribe(
                tmp,
                language='zh',
                beam_size=3,
                best_of=3
            )
            text = ''.join([seg.text for seg in segments]).strip()

            if text and len(text) > 0:
                print('RESULT:' + text)
            else:
                print('ERROR:empty')
        except Exception as e:
            print('ERROR:' + str(e))

        try:
            os.remove(tmp)
        except:
            pass

    except Exception as e:
        print('ERROR:' + str(e))

if __name__ == '__main__':
    main()
";

            try
            {
                File.WriteAllText(scriptPath, pyScript,
                    System.Text.Encoding.UTF8);
            }
            catch { }
        }

        public void RecordAndTranscribe(
            Action<string> onResult,
            Action<Exception> onError = null)
        {
            if (_isRecording) return;

            Task.Run(() =>
            {
                _isRecording = true;
                try
                {
                    string scriptPath = Path.Combine(
                        TempDir, "stt.py");
                    if (!File.Exists(scriptPath))
                        WriteSttScript();

                    var psi = new ProcessStartInfo
                    {
                        FileName = "python",
                        Arguments = $"\"{scriptPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = System.Text.Encoding.UTF8
                    };

                    using (var proc = Process.Start(psi))
                    {
                        if (proc == null)
                        {
                            _mainQueue.Enqueue(()
                                => onError?.Invoke(
                                    new Exception("Python not found")));
                            return;
                        }

                        bool exited = proc.WaitForExit(30000);

                        if (!exited)
                        {
                            try { proc.Kill(); } catch { }
                            _mainQueue.Enqueue(() =>
                                onError?.Invoke(
                                    new Exception("Timeout")));
                            return;
                        }

                        string output = proc.StandardOutput
                            .ReadToEnd().Trim();
                        string errors = proc.StandardError
                            .ReadToEnd().Trim();

                        string result = null;
                        foreach (var line in output.Split('\n'))
                        {
                            string l = line.Trim();
                            if (l.StartsWith("RESULT:"))
                            {
                                result = l.Substring(7).Trim();
                                break;
                            }
                        }

                        if (!string.IsNullOrEmpty(result))
                        {
                            _mainQueue.Enqueue(() =>
                                onResult?.Invoke(result));
                        }
                        else
                        {
                            string errMsg = "Unknown error";
                            foreach (var line in output.Split('\n'))
                            {
                                string l = line.Trim();
                                if (l.StartsWith("ERROR:"))
                                {
                                    errMsg = l.Substring(6);
                                    break;
                                }
                            }

                            if (!string.IsNullOrEmpty(errors))
                                errMsg += " | " + errors;

                            _mainQueue.Enqueue(() =>
                                onError?.Invoke(
                                    new Exception(errMsg)));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _mainQueue.Enqueue(()
                        => onError?.Invoke(ex));
                }
                finally
                {
                    _isRecording = false;
                }
            });
        }

        public void ProcessMainQueue()
        {
            int count = 0;
            while (_mainQueue.TryDequeue(out var action)
                && count < 3)
            {
                count++;
                try { action?.Invoke(); }
                catch { }
            }
        }
    }
}
