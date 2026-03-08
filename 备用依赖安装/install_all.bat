@echo off
chcp 65001 >nul
echo ============================================
echo   GTA V AI NPC Mod - 自动安装程序
echo   一键安装所有依赖
echo ============================================
echo.

echo [1/5] 检查 Python...
python --version >nul 2>&1
if errorlevel 1 (
    echo 错误：未找到 Python！
    echo 请从 https://www.python.org 安装 Python
    echo 重要：安装时勾选 "Add Python to PATH"
    echo.
    pause
    exit /b
)
echo Python 正常！

echo.
echo [2/5] 安装 edge-tts（语音输出）...
pip install edge-tts

echo.
echo [3/5] 安装音频库...
pip install sounddevice numpy

echo.
echo [4/5] 安装语音识别库...
pip install faster-whisper SpeechRecognition

echo.
echo [5/5] 测试安装...
python -c "import edge_tts; print('edge-tts: 正常')"
python -c "import sounddevice; print('sounddevice: 正常')"
python -c "import numpy; print('numpy: 正常')"
python -c "from faster_whisper import WhisperModel; print('faster-whisper: 正常')"

echo.
echo ============================================
echo   安装完成！
echo ============================================
echo.
echo 下一步：运行 download_whisper.bat
echo.
pause