#!/usr/bin/env bash
cd app
dotnet OSMPbfWebAPI.dll --urls=http://10.10.10.13:8987 &
