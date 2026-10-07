@echo off
cd /d "%~dp0"
python -c "import pygame, numpy" 2>nul || python -m pip install -r requirements.txt
python src\main.py
if errorlevel 1 pause
