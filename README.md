# TShock-Cerberus
A TSHOCK plugin, bridge, and discord bot that automate tshock server access based on Discord roles. 

# Cerberus Authentication Bridge

Cerberus is an enterprise-grade, zero-trust authentication bridge for Terraria (TShock). It shifts the burden of server whitelisting to your Discord server. 

By utilizing a shared SQLite database running in WAL mode, Cerberus handles multi-server concurrency effortlessly, ensuring no file-locks or race conditions occur, even when running across multiple Docker containers and TSHOCK servers.

## Features
* **Challenge-Response Security:** Players cannot spoof names; they must prove Discord account ownership via a DM'd token.
* **Fail-Closed Architecture:** If the DB goes offline, the server locks down automatically.
* **Multi-Server Ready:** Run one Discord bot to govern an unlimited number of clustered TShock servers.
* **Auto-Limbo:** Unverified players are placed in an automatically generated `guest_limbo` state, completely neutralizing griefers before they complete verification.

## Prerequisites
* Docker and Docker Compose
* A Discord Bot Token (Get one from the [Discord Developer Portal](https://discord.com/developers/applications))

## Installation

### 1. Configure the Environment
Copy the `.env.example` file to `.env` and fill in your details:
```bash
cp .env.example .env


## How to use it as a Player

    Join the community Discord.

    Type !link MyTerrariaName in a text channel.

    The Bot will DM you a 4-digit code (e.g., 8821).

    Connect to the Terraria server. You will be frozen in spawn.

    Register/Login to your TShock account: /register mypassword then /login mypassword.

    Enter your code: /verify 8821.

    You are permanently linked!


## Architecture Note for Admins

Cerberus uses a "Shared Volume" sidecar pattern. The cerberus.db SQLite file acts as the single source of truth. The TShock plugin is strictly configured for fast read-polling, while the Discord bot maintains atomic write authority.


CerberusBridge/
├── build.sh                     # Compiles the plugin and starts Docker
├── docker-compose.yml           # Runs the Bot and the Game Server
├── .gitignore
├── bridge_data/                 # The shared local volume (created at runtime)
│   └── serverplugins/           # Where the build script drops the Plugin DLL
├── CerberusBot/                 # The Discord Bot (Worker Service)
│   ├── Dockerfile
│   ├── CerberusBot.csproj
│   ├── Program.cs
│   └── Worker.cs
└── CerberusPlugin/              # The TShock Plugin
    ├── CerberusPlugin.csproj
    ├── CerberusPlugin.cs
    └── Models.cs

    