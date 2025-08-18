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

## Installation

1. **Download Files**
   - Click the green **"Code"** button at the top of this page
   - Select **"Download ZIP"**
   - Extract the downloaded ZIP file
   - Find the `TakaroIntegration` folder inside the extracted files
   - Copy the entire `TakaroIntegration` folder to your Eco server's `Mods/` directory
   - Final structure should be: `YourEcoServer/Mods/TakaroIntegration/`

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
  "websocketUrl": "wss://connect.takaro.io/",
  "serverName": "YOUR_SERVER_NAME", 
  "registrationToken": "YOUR_REGISTRATION_TOKEN"
}
```

## Building from Source

If you need to build the plugin yourself:
1. Ensure you have .NET 8.0 SDK installed
2. Reference the Eco ModKit assemblies
3. Build the `TakaroIntegrationPlugin.cs` file

## Support

- **Documentation**: [Takaro Docs](https://docs.takaro.io)
- **Issues**: Report bugs via GitHub issues

## License

Open source project for the Takaro community.