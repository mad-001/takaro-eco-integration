# Takaro Integration for Eco Servers

**Current Version: v1.1.0** - [📋 Changelog](CHANGELOG.md)

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

## 🎯 Latest Update - v1.1.0

**CRITICAL STABILITY FIX**: This version resolves the issue where multiple Eco servers would disconnect from Takaro simultaneously. Each server now has independent WebSocket connections with jittered reconnection delays.

## Repository Structure

- **`Source/`** - Source code for building your own version (includes .csproj file)
- **`TakaroIntegration/`** - Ready-to-use plugin files
- **`CHANGELOG.md`** - Version history and detailed changes

## 📥 Quick Download

**🌟 [Download Plugin Here](https://mad-001.github.io/takaro-eco-integration/) 🌟**

Or use the [GitHub Releases](https://github.com/mad-001/takaro-eco-integration/releases) page directly.

## Features

- **WebSocket Integration**: Real-time event streaming to Takaro API
- **RCON Server**: Remote command execution from Takaro dashboard  
- **Player Tracking**: Connect/disconnect events with Steam ID linking
- **Chat Integration**: Relay chat messages between Eco and Discord
- **Player Management**: Kick, ban, and manage players remotely

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
