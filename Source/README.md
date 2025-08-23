# Source Code - Takaro Integration v1.1.0

This directory contains the complete source code and build files for the Takaro Integration plugin.

## Building from Source

### Prerequisites
- .NET 8.0 SDK
- Eco ModKit (included as ReferenceAssemblies)

### Build Instructions
```bash
# Clone or download this repository
# Navigate to the Source/ directory
cd Source/

# Restore packages and build
dotnet build

# The compiled DLL will be in bin/Debug/TakaroIntegration.dll
```

### Files Included
- `TakaroIntegrationPlugin.cs` - Main source code
- `EcoTakaroMod.csproj` - Project file with dependencies
- `ReferenceAssemblies/` - Eco ModKit reference assemblies

## Key Features in v1.1.0

### 🔧 Critical Fixes
- **Fixed singleton WebSocket client** causing simultaneous server disconnections
- **Added instance isolation** with unique IDs per server
- **Implemented jittered reconnection delays** to prevent connection storms

### 🎯 Technical Improvements
- Thread-safe connection state management
- Proper resource cleanup and disposal
- Enhanced error handling and logging
- Independent WebSocket connections per server instance

## Architecture Changes

### Before (v1.0.0)
```csharp
public static TakaroWebSocketClient takaroClient; // Shared singleton - PROBLEMATIC
```

### After (v1.1.0)
```csharp
private TakaroWebSocketClient takaroClient;       // Instance-based
private readonly string instanceId;               // Unique per server
```

This change ensures that each Eco server has its own independent connection to Takaro, preventing cascading failures when one server experiences network issues.

## Logging Changes

New instance-specific logging format:
```
[2025-08-23 15:30:45.123][a1b2c3d4][Info] Server connected to Takaro
```

Each server gets its own log file:
- `08-23-15-a1b2c3d4.log` (Main Server)
- `08-23-15-e5f6g7h8.log` (Test Server)

## License

Open source project for the Takaro community.