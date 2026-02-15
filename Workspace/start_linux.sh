#!/bin/bash
# DotBot start script from source

export DOTNET_ROOT=$HOME/.dotnet
export PATH=$PATH:$HOME/.dotnet:$HOME/.dotnet/tools

cd ..
dotnet build

cd Workspace
dotnet ../DotBot/bin/Debug/net10.0/DotBot.dll
