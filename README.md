# Takaro Integration for Eco Servers

A comprehensive integration plugin that connects Eco game servers to the Takaro platform, providing server management, player tracking, and real-time communication capabilities.

## Features

- **WebSocket Integration**: Real-time event streaming to Takaro API
- **RCON Server**: Remote command execution from Takaro dashboard  
- **Player Tracking**: Connect/disconnect events with Steam ID linking
- **Chat Integration**: Relay chat messages between Eco and Discord
- **Player Management**: Kick, ban, and manage players remotely

## Getting Access to Takaro

**⚠️ IMPORTANT: Takaro is currently invite-only!**

Before you can use this plugin, you need to get access to Takaro:

1. **Join the Discord**: https://discord.gg/pwenDRrtnA
2. **Fill out the survey**: https://forms.gle/53ebt7m92RkmqSvn6
3. **Request an invite**: Ask in the Discord to be invited to Takaro
4. **Wait for approval**: The Takaro team will review and invite you

## Repository Structure

- **`TakaroIntegration/`** - Ready-to-install plugin files (download this folder)
- **`Source/`** - Source code for building your own version (optional)

## Installation

### Option 1: Download Release (Recommended)
1. **Go to [Releases](https://github.com/mad-001/takaro-eco-integration/releases)**
2. **Download the latest `TakaroIntegration.zip`** or `TakaroIntegration.tar.gz`
3. **Extract the archive**
4. **Copy the `TakaroIntegration` folder** to your Eco server's `Mods/` directory

### Option 2: Download Individual Files
1. **Navigate to the `TakaroIntegration` folder** above
2. **Download each file individually**:
   - Right-click `TakaroIntegration.dll` → Save As
   - Right-click `TakaroConfig.json` → Save As  
   - Right-click `README.md` → Save As
3. **Create a `TakaroIntegration` folder** in your Eco server's `Mods/` directory
4. **Place all downloaded files** in that folder

### Option 3: Clone Repository (Advanced)
1. **Clone this repository**: `git clone https://github.com/mad-001/takaro-eco-integration.git`
2. **Copy only the `TakaroIntegration` folder** to your Eco server's `Mods/` directory

**Final structure should be**: `YourEcoServer/Mods/TakaroIntegration/`

2. **Configure Takaro** (after getting invited)
   - Log into your Takaro dashboard
   - Click "Add a gameserver" but don't complete the setup
   - Copy the registration token from this process
   - Edit `Mods/TakaroIntegration/TakaroConfig.json` with your server name and registration token

3. **Start Server**
   - Restart your Eco server
   - Check logs for successful Takaro connection

## Configuration

Edit `TakaroConfig.json`:
```json
{
  "serverName": "SERVER_NAME",
  "_serverName_help": "Choose your server name, it can be anything you want",
  "registrationToken": "REGISTRATION_TOKEN",
  "_registrationToken_help": "In Takaro.io you need an account, click on add a gameserver but do not make a server, copy the registration token.",
  "websocketUrl": "wss://connect.takaro.io/",
  "enableLogging": false
}
```

## Building from Source

If you need to build the plugin yourself:
1. Ensure you have .NET 8.0 SDK installed
2. Reference the Eco ModKit assemblies
3. Build the `Source/TakaroIntegrationPlugin.cs` file
4. The source code is provided in the `Source/` folder

## Support

- **Documentation**: [Takaro Docs](https://docs.takaro.io)
- **Issues**: Report bugs via GitHub issues

## License


Open source project for the Takaro community.
