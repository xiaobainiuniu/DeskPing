@echo off
rem 完全独立单文件发布（约 60MB，目标机器无需安装任何运行时）
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish-self-contained
pause
