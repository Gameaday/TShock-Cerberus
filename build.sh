#!/bin/bash
set -e

echo "=== Building Cerberus TShock Plugin ==="
cd CerberusPlugin
dotnet build -c Release

echo "=== Staging Plugin for Docker Volume ==="
cd ..
mkdir -p ./bridge_data/serverplugins
mkdir -p ./bridge_data/shared_state

# Copy the compiled DLL to the folder that will be mounted into the TShock container
cp ./CerberusPlugin/bin/Release/net9.0/CerberusPlugin.dll ./bridge_data/serverplugins/

echo "=== Starting Cerberus Bridge via Docker Compose ==="
docker-compose up --build -d

echo "Deployment complete. Check 'docker-compose logs -f' for status."