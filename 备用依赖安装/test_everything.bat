@echo off
chcp 65001 >nul
echo ============================================
echo   正在测试所有组件
echo ============================================
echo.

echo [测试1] Python 检查...
python --version
echo.

echo [测试2] 库检查...
python -c "import edge_tts; import sounddevice; import numpy; from faster_whisper import WhisperModel; print('所有库: 正常')"
echo.

echo [测试3] TTS 语音测试...
echo 正在生成测试语音...
python -m edge_tts --voice "zh-CN-YunxiNeural" --text "AI NPC 模组安装成功" --write-media test_voice.mp3

if exist test_voice.mp3 (
    echo TTS: 正常！
    del test_voice.mp3
) else (
    echo TTS: 失败 - 检查网络连接
)
echo.

echo [测试4] LM Studio 检查...
python -c "import urllib.request; r=urllib.request.urlopen('http://127.0.0.1:1234/v1/models', timeout=3); print('LM Studio: 正常')" 2>nul
if errorlevel 1 (
    echo LM Studio: 未运行
    echo 请先启动 LM Studio 并加载模型！
)
echo.

echo ============================================
echo   测试完成！
echo ============================================
echo.
echo 如果所有测试显示正常，将 2_GTA脚本文件
echo 文件夹中的文件复制到 GTA V 的 scripts
echo 文件夹中，然后开始游戏！
echo.
pause