# Takaro Integration for Eco Servers

A comprehensive integration plugin that connects Eco game servers to the Takaro platform, providing server management, player tracking, and real-time communication capabilities.

## Features

- **WebSocket Integration**: Real-time event streaming to Takaro API
- **RCON Server**: Remote command execution from Takaro dashboard  
- **Player Tracking**: Connect/disconnect events with Steam ID linking
- **Chat Integration**: Relay chat messages between Eco and Discord
- **Player Management**: Kick, ban, and manage players remotely

## Installation

1. **Download Files**
   - Copy `TakaroIntegration.dll` to your Eco server's `Mods/TakaroIntegration/` directory
   - Copy `TakaroConfig.json` to the same location

2. **Configure Takaro**
   - Register your server at [Takaro Dashboard](https://app.takaro.io)
   - Update `TakaroConfig.json` with your server's identity token

3. **Start Server**
   - Restart your Eco server
   - Check logs for successful Takaro connection

## Configuration

Edit `TakaroConfig.json`:
```json
{
  "WebSocketUrl": "wss://api.takaro.io/ws",
  "IdentityToken": "your-server-identity-token",
  "RconPort": 6002,
  "EnableDebugLogging": true
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