@echo off
chcp 65001 > nul
echo === 安裝依賴 ===
pip install -r requirements.txt

echo.
echo === 複製 HTML ===
copy /Y "..\WebUI\MaskEditor.html" "MaskEditor.html"

echo.
echo === 打包 exe ===
pyinstaller --onefile --noconsole ^
  --add-data "MaskEditor.html;." ^
  --name MaskEditor ^
  --icon NONE ^
  main.py

echo.
echo === 完成 ===
echo 輸出：dist\MaskEditor.exe
pause
