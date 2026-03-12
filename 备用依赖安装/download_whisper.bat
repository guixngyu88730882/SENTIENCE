@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

echo ============================================
echo  Sentience AI - Whisper Model Downloader
echo ============================================
echo.

set "MODEL_DIR=C:\whisper-tiny"
set "MODEL_FILE=model.bin"
set "FULL_PATH=%MODEL_DIR%\%MODEL_FILE%"

set "EXPECTED_MD5=ab6cd58e5dcaf1296ab50ccbf36f9277"

if not exist "%MODEL_DIR%" mkdir "%MODEL_DIR%"

echo [1/2] Downloading from official HuggingFace...
echo       Using faster-whisper with Systran/faster-whisper-tiny
echo.
echo NOTE: This will download the model from HuggingFace.
echo       Use HF_ENDPOINT environment variable if you need a mirror.
echo.

set "HF_ENDPOINT=https://huggingface.co"

python -c "from faster_whisper import WhisperModel; print('Downloading...'); m = WhisperModel('tiny', device='cpu', compute_type='int8'); print('Done!')"

if exist "%FULL_PATH%" (
    echo [OK] Download complete.
    goto :verify
)

echo.
echo [WARN] Default download failed. Trying HF Mirror...
echo.

set "HF_ENDPOINT=https://hf-mirror.com"

python -c "from faster_whisper import WhisperModel; print('Downloading...'); m = WhisperModel('tiny', device='cpu', compute_type='int8'); print('Done!')"

if exist "%FULL_PATH%" (
    echo [OK] Download complete from mirror.
    goto :verify
)

echo.
echo [ERROR] Download failed from all sources.
echo [ERROR] Please download manually and place model files in: %MODEL_DIR%\
echo.

pause
exit /b 1

:verify

echo.
echo [INFO] Verifying file integrity (MD5 checksum)...

for /f "skip=1 tokens=* delims=" %%a in ('certutil -hashfile "%FULL_PATH%" MD5 2^>nul') do (
    if not defined FILE_MD5 set "FILE_MD5=%%a"
)

set "FILE_MD5=!FILE_MD5: =!"

echo [INFO] Expected : %EXPECTED_MD5%
echo [INFO] Got      : !FILE_MD5!

if /i "!FILE_MD5!"=="%EXPECTED_MD5%" (
    echo.
    echo [OK] Checksum verified! Model file is authentic.
    echo [OK] Model ready at: %FULL_PATH%
) else (
    echo.
    echo [WARNING] Checksum mismatch!
    echo [WARNING] The downloaded file may be corrupted or tampered with.
    echo [WARNING] Please delete %FULL_PATH% and try again.
)

echo.
echo ============================================
echo   Whisper Model Download Complete
echo ============================================
echo.
echo Next: Run test_everything.bat
echo.
pause
