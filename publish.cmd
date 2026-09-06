@echo off
rem 框架依赖单文件发布（约 300KB，目标机器需安装 .NET 10 桌面运行时）
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
pause
