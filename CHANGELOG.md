# Changelog

All notable changes to the Takaro Integration plugin will be documented in this file.

## [v1.2.0] - 2025-08-24

### 🎯 Chat System Improvements
- **FIXED**: Private message filtering - PMs no longer appear in Discord
- **ADDED**: Proper channel type detection using `ChatMessage.Receiver` property
- **IMPROVED**: Channel classification logic:
  - Global chat: `Receiver == "General"` → `channel: "global"` → Sent to Discord
  - Private messages: `Receiver != "General"` → `channel: "whisper"` → Filtered by Takaro hook
- **ENHANCED**: Command prefix filtering - commands removed from game chat but sent to Takaro

### 🔧 Bug Fixes
- **FIXED**: Log filename format corrected to `MM-dd-hh.log` (removed instance ID)
- **RESOLVED**: API validation error caused by invalid `isCommand` field
- **CORRECTED**: Channel type detection using proper ChatMessage properties

### 🧹 Code Quality  
- **ADDED**: Debug logging for channel type detection
- **IMPROVED**: Error handling for chat message processing
- **ENHANCED**: Message filtering logic with proper receiver analysis

### 🎯 Impact
This release fixes the critical issue where private messages were being sent to Discord channels. Now only global chat messages reach Discord, while private messages are properly filtered out by the Takaro hook system.

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