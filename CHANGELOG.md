# Changelog

All notable changes to the Takaro Integration plugin will be documented in this file.

## [v1.1.0] - 2025-08-23

### 🔧 Major Stability Fixes
- **CRITICAL FIX**: Resolved simultaneous server disconnection issues
- **FIXED**: Singleton WebSocket client pattern causing shared connection failures
- **ADDED**: Instance-based WebSocket connections for each server
- **ADDED**: Unique instance IDs for proper server isolation
- **ADDED**: Instance-specific logging with unique identifiers

### 🔄 Connection Improvements
- **IMPROVED**: Jittered reconnection delays to prevent connection storms
- **REDUCED**: Reconnection attempt counts (5 initial + 20 long-term, was 10 + 60)
- **ADDED**: Connection state synchronization with proper thread safety
- **ENHANCED**: Resource cleanup and disposal handling
- **FIXED**: Race conditions during connection attempts

### 📊 Logging Enhancements
- **ADDED**: Instance-specific log files: `MM-dd-HH-{instanceId}.log`
- **IMPROVED**: Log messages include instance IDs for troubleshooting
- **ENHANCED**: Thread-safe logging with hourly rotation

### 🧹 Code Quality
- **REFACTORED**: Removed static singleton patterns
- **IMPROVED**: Error handling and timeout management
- **ADDED**: Proper connection timeout with jitter (30-40 seconds)
- **ENHANCED**: WebSocket state validation and cleanup

### 🎯 Impact
This release resolves the critical issue where multiple Eco servers would go offline simultaneously due to shared WebSocket connection state. Each server now operates independently with its own connection, preventing cascading failures.

## [v1.0.0] - Initial Release
- Basic Takaro integration functionality
- WebSocket communication with Takaro platform
- Player event tracking and chat integration
- RCON server for remote command execution