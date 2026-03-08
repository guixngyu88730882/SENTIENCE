@echo off
chcp 65001 >nul
title SENTIENCE 语音服务器
color 0A
echo.
echo  SENTIENCE 语音服务器
echo  请勿关闭此窗口！
echo  Do not close this window!
echo.
python voice_server.py
pause