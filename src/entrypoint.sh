#!/bin/sh
set -eu

exec dotnet "/app/$SERVICE_DLL" "$@"
