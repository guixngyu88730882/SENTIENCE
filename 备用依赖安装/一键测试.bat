@echo off
chcp 65001 >nul
title GTA V AI NPC - 系统测试
color 0B

echo.
echo  ╔════════════════════════════════════╗
echo  ║    SENTIENCE  v3.0            ║
echo  ║   系统测试                         ║
echo  ╚════════════════════════════════════╝
echo.

set PASS=0
set FAIL=0

echo  [测试1] Python...
python --version >nul 2>&1
if errorlevel 1 (
    echo          ❌ 未安装Python
    set /a FAIL+=1
) else (
    echo          ✅ Python 正常
    set /a PASS+=1
)
echo.

echo  [测试2] Python库...
python -c "import edge_tts; import sounddevice; import numpy; from faster_whisper import WhisperModel; print('         ✅ 所有库正常')" 2>nul
if errorlevel 1 (
    echo          ❌ 某些库缺失，请运行一键安装依赖.bat
    set /a FAIL+=1
) else (
    set /a PASS+=1
)
echo.

echo  [测试3] Whisper语音模型...
if exist "C:\whisper-tiny\model.bin" (
    echo          ✅ 语音模型已安装
    set /a PASS+=1
) else (
    echo          ❌ 未找到语音模型
    echo          请将Whisper_Model文件夹复制到 C:\whisper-tiny\
    set /a FAIL+=1
)
echo.

echo  [测试4] LM Studio服务器...
python -c "import urllib.request; r=urllib.request.urlopen('http://127.0.0.1:1234/v1/models',timeout=3); print('         ✅ LM Studio 正在运行')" 2>nul
if errorlevel 1 (
    echo          ❌ LM Studio 未运行
    echo          请打开LM Studio，加载模型，启动服务器
    set /a FAIL+=1
) else (
    set /a PASS+=1
)
echo.

echo  [测试5] GTA V scripts文件夹...
set FOUND_GTA=0

if exist "C:\Program Files\Steam\steamapps\common\Grand Theft Auto V\scripts\SENTIENCE.dll" (
    echo          ✅ Mod已安装 (Steam)
    set /a PASS+=1
    set FOUND_GTA=1
)
if exist "C:\Program Files\Epic Games\GTAV\scripts\SENTIENCE.dll" (
    echo          ✅ Mod已安装 (Epic)
    set /a PASS+=1
    set FOUND_GTA=1
)
if %FOUND_GTA%==0 (
    echo          ⚠️  未自动检测到mod文件
    echo          请确认已将dll复制到scripts文件夹
)
echo.

echo  [测试6] 语音合成测试...
python -m edge_tts --voice "zh-CN-YunxiNeural" --text "系统测试成功" --write-media "%TEMP%\test_tts.mp3" 2>nul
if exist "%TEMP%\test_tts.mp3" (
    echo          ✅ 语音合成正常
    del "%TEMP%\test_tts.mp3" >nul 2>&1
    set /a PASS+=1
) else (
    echo          ❌ 语音合成失败（需要网络连接）
    set /a FAIL+=1
)
echo.

echo  ════════════════════════════════════
echo.
if %FAIL%==0 (
    color 0A
    echo  🎉 所有测试通过！可以开始游戏了！
    echo.
    echo  游戏前检查清单：
    echo    ☑ LM Studio 已打开
    echo    ☑ 模型已加载
    echo    ☑ 服务器已启动
    echo    ☑ 启动 GTA V
    echo    ☑ 靠近NPC按G/H/T/J互动
) else (
    color 0E
    echo  ⚠️ 有 %FAIL% 项测试未通过
    echo  请根据上方提示修复问题后重新测试
)
echo.
echo  ════════════════════════════════════
echo.
pause