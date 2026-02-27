@echo off
chcp 65001 >nul
echo ============================================
echo   正在下载 Whisper 模型（约75MB）
echo ============================================
echo.
echo 可能需要几分钟，请耐心等待...
echo.

set HF_ENDPOINT=https://hf-mirror.com

python -c "from faster_whisper import WhisperModel; print('正在下载...'); m = WhisperModel('tiny', device='cpu', compute_type='int8'); print('下载完成！')"

if errorlevel 1 (
    echo.
    echo 镜像1失败，尝试镜像2...
    set HF_ENDPOINT=https://huggingface.sukaka.top
    python -c "from faster_whisper import WhisperModel; print('正在下载...'); m = WhisperModel('tiny', device='cpu', compute_type='int8'); print('下载完成！')"
)

echo.
echo ============================================
echo   语音识别模型已就绪！
echo ============================================
echo.
echo 下一步：运行 test_everything.bat
echo.
pause