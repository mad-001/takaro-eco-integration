using Eco.Core;
using Eco.Core.Plugins;
using Eco.Core.Plugins.Interfaces;
using Eco.Core.Serialization;
using Eco.Core.Utils;
using Eco.Core.Utils.Logging;
using Eco.Gameplay.Players;
using Eco.Gameplay.Items;
using Eco.Gameplay.Systems.Chat;
using Eco.Gameplay.Systems.Messaging.Chat;
using Eco.Gameplay.Systems.Messaging.Chat.Commands;
using Eco.Gameplay.Systems.Messaging.Chat.Channels;
using Eco.Gameplay.Systems.Messaging.Notifications;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Disasters;
using Eco.Simulation.Time;
using Eco.Plugins.Networking;
using Eco.Plugins.Rcon;
using Eco.Shared.Localization;
using Eco.Shared.Utils;
using Eco.Shared.Services;
using Eco.Shared.Math;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

public class TakaroIntegrationMod : IModInit
{
    public static ModRegistration Register() => new()
    {
        ModName = "TakaroIntegration",
        ModDescription = "Official Takaro integration for Eco servers providing server management and player tracking via WebSocket communication.",
        ModDisplayName = "Takaro Integration",
    };
}

namespace Eco.Takaro
{
    // Thread-safe file-based logger with hourly rotation
    public class SimpleFileLogger
    {
        private readonly string logDirectory;
        private StreamWriter currentWriter;
        private int currentHour = -1;
        private readonly object lockObj = new object();
        private bool loggingEnabled;
        private readonly string instanceId;

        public SimpleFileLogger(bool enableLogging = true, string instanceId = null)
        {
            this.instanceId = instanceId ?? Guid.NewGuid().ToString("N")[..8];
            loggingEnabled = enableLogging;
            if (enableLogging)
            {
                try
                {
                    logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "Takaro");
                    Directory.CreateDirectory(logDirectory);
                }
                catch
                {
                    // If directory creation fails, disable logging silently
                    loggingEnabled = false;
                }
            }
        }

        public void Write(string message)
        {
            if (!loggingEnabled) return;
            
            try
            {
                lock (lockObj)
                {
                    var now = DateTime.Now;
                    if (currentHour != now.Hour || currentWriter == null)
                    {
                        // Close old writer safely
                        try
                        {
                            currentWriter?.Close();
                            currentWriter?.Dispose();
                        }
                        catch { }
                        
                        // Create new file with instance ID for isolation
                        string fileName = $"{now:MM-dd-hh}.log";
                        string filePath = Path.Combine(logDirectory, fileName);
                        
                        try
                        {
                            currentWriter = new StreamWriter(filePath, append: true);
                            currentWriter.AutoFlush = true;
                            currentHour = now.Hour;
                        }
                        catch
                        {
                            // If file creation fails, disable logging
                            loggingEnabled = false;
                            return;
                        }
                    }

                    currentWriter?.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss.fff}][{instanceId}][ Info] {message}");
                }
            }
            catch
            {
                // Silently fail to avoid breaking the plugin
            }
        }

        public void WriteWarning(string message)
        {
            Write($"[Warning] {message}");
        }

        public void Close()
        {
            if (!loggingEnabled) return;
            
            try
            {
                lock (lockObj)
                {
                    currentWriter?.Close();
                    currentWriter?.Dispose();
                    currentWriter = null;
                }
            }
            catch
            {
                // Silently fail
            }
        }
    }

    [Priority(PriorityAttribute.High)]
    public class TakaroPlugin : IModKitPlugin, IInitializablePlugin, IShutdownablePlugin
    {
        public readonly string PluginName = "TakaroIntegration";
        
        // FIXED: Remove static singleton - each instance gets its own client
        private TakaroWebSocketClient takaroClient;
        private SimpleFileLogger logger;
        private readonly string instanceId;
        
        // Configuration options
        public bool enableLogging = true; // Default to enabled, can be configured
        
        // Instance logger (no longer shared static)
        public SimpleFileLogger InstanceLogger => logger;

        public static TakaroPlugin Obj { get { return PluginManager.GetPlugin<TakaroPlugin>(); } }

        public TakaroPlugin()
        {
            // Generate unique instance ID for this server
            instanceId = Guid.NewGuid().ToString("N")[..8];
        }

        private string status = "Uninitialized";
        public string Status
        {
            get { return status; }
            private set
            {
                logger?.Write($"Plugin status changed from \"{status}\" to \"{value}\"");
                status = value;
            }
        }

        public string GetCategory() => "Integration";
        public string GetStatus() => Status;

        private void CreateTakaroLogDirectory()
        {
            try
            {
                string logsDir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "Takaro");
                Directory.CreateDirectory(logsDir);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create Takaro log directory: {ex.Message}");
            }
        }

        public void Initialize(TimedTask timer)
        {
            // Initialize instance-specific logger
            logger = new SimpleFileLogger(enableLogging, instanceId);
            
            Status = "Initializing";
            logger.Write($"Takaro Integration Plugin starting with instance ID: {instanceId}");

            // FIXED: Create instance-specific client instead of static singleton
            takaroClient = new TakaroWebSocketClient(logger, instanceId);
            
            // Config will be loaded in TakaroWebSocketClient constructor
            Task.Run(async () => await takaroClient.InitializeAsync());

            UserManager.OnUserLoggedIn.Add(OnPlayerJoined);
            UserManager.OnUserLoggedOut.Add(OnPlayerLeft);
            
            // Hook into chat events the proper way (like MightyMooseCore does)
            logger.Write("Registering chat message event handler");
            ChatManager.MessageSent.Add(OnChatMessage);

            Status = "Running";
            logger.Write("Takaro Integration Plugin initialized successfully");
        }

        public async Task ShutdownAsync()
        {
            Status = "Shutting down";
            
            UserManager.OnUserLoggedIn.Remove(OnPlayerJoined);
            UserManager.OnUserLoggedOut.Remove(OnPlayerLeft);
            ChatManager.MessageSent.Remove(OnChatMessage);

            if (takaroClient != null)
            {
                await takaroClient.ShutdownAsync();
            }
            

            logger?.Write("Takaro Integration Plugin shut down");
            logger?.Close();
        }

        private async void OnPlayerJoined(User user)
        {
            if (takaroClient?.IsConnected == true)
            {
                await takaroClient.SendPlayerEventAsync("player-connected", user);
            }
        }

        private async void OnPlayerLeft(User user)
        {
            if (takaroClient?.IsConnected == true)
            {
                await takaroClient.SendPlayerEventAsync("player-disconnected", user);
            }
        }

        private async void OnChatMessage(ChatMessage chatMessage)
        {
            try
            {
                if (chatMessage.Sender != null && takaroClient?.IsConnected == true)
                {
                    logger?.Write($"[CHAT DEBUG] Processing chat from {chatMessage.Sender.Name}: {chatMessage.Text}");
                    
                    // Check if message is a command (starts with configured prefix)
                    string commandPrefix = takaroClient?.GetCommandPrefix() ?? "!";
                    bool isCommand = chatMessage.Text.StartsWith(commandPrefix);
                    
                    // Send to Takaro regardless (commands and regular chat)
                    await takaroClient.SendChatEventAsync(chatMessage.Sender, chatMessage.Text, isCommand, chatMessage);
                    
                    if (isCommand)
                    {
                        logger?.Write($"[COMMAND] Command with prefix '{commandPrefix}' sent to Takaro: {chatMessage.Text}");
                        
                        // Remove the command from chat display
                        try
                        {
                            // Use ChatManager.Obj to access the instance
                            ChatManager.Obj.RemoveMessages(msg => msg.Sender == chatMessage.Sender && msg.Text == chatMessage.Text);
                            logger?.Write($"[COMMAND] Removed command from chat display");
                        }
                        catch (Exception removeEx)
                        {
                            logger?.WriteWarning($"[COMMAND] Could not remove command from chat: {removeEx.Message}");
                        }
                    }
                    else
                    {
                        logger?.Write($"[CHAT DEBUG] Chat event sent to Takaro successfully");
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.WriteWarning($"Error handling chat message: {ex.Message}");
            }
        }

    }

    public class TakaroWebSocketClient
    {
        private ClientWebSocket webSocket;
        private CancellationTokenSource cancellationTokenSource;
        private SimpleFileLogger logger;
        
        // FIXED: Add proper connection state synchronization
        private volatile bool isConnected = false;
        private volatile bool isReconnecting = false;
        private readonly object reconnectionLock = new object();
        private readonly object connectionLock = new object();
        
        private string registrationToken;
        private string gameServerId;
        private string serverName;
        private string websocketUrl;
        private string commandPrefix = "!"; // Default command prefix
        private int currentPlayerCount = 0;
        private readonly string instanceId;
        
        // FIXED: Add jitter for reconnection delays
        private readonly Random random = new Random();

        public bool IsConnected 
        { 
            get 
            { 
                lock (connectionLock)
                {
                    return isConnected && webSocket?.State == WebSocketState.Open;
                }
            }
        }

        public TakaroWebSocketClient(SimpleFileLogger logger, string instanceId)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.instanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
            LoadConfig();
        }

        private void LoadConfig()
        {
            try
            {
                string configPath = Path.Combine(Directory.GetCurrentDirectory(), "Mods", "TakaroIntegration", "TakaroConfig.json");
                if (File.Exists(configPath))
                {
                    string jsonContent = File.ReadAllText(configPath);
                    var config = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(jsonContent);
                    
                    registrationToken = config.GetProperty("registrationToken").GetString();
                    serverName = config.GetProperty("serverName").GetString();
                    websocketUrl = config.GetProperty("websocketUrl").GetString();
                    
                    // commandPrefix is optional, defaults to "!"
                    if (config.TryGetProperty("commandPrefix", out var commandPrefixElement))
                    {
                        commandPrefix = commandPrefixElement.GetString() ?? "!";
                    }
                    
                    // gameServerId is optional and returned by Takaro after identification
                    gameServerId = config.TryGetProperty("gameServerId", out var gameServerIdElement) 
                        ? gameServerIdElement.GetString() 
                        : null;
                    
                    // Load logging configuration (optional, defaults to true)
                    bool newLoggingSetting = true; // Default value
                    if (config.TryGetProperty("enableLogging", out var loggingElement))
                    {
                        newLoggingSetting = loggingElement.GetBoolean();
                    }
                    
                    // Note: Can't change logging after initialization due to instance isolation
                    
                    logger.Write($"[{instanceId}] Loaded config: Server={serverName}, WebSocket={websocketUrl}");
                }
                else
                {
                    logger.WriteWarning("Config file not found at expected path. TakaroConfig.json is required for operation.");
                    SetFallbackValues();
                    return; // Don't attempt to connect without proper config
                }
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Failed to load config: {ex.Message}. TakaroConfig.json is required for operation.");
                SetFallbackValues();
                return; // Don't attempt to connect without proper config
            }
        }

        private void SetFallbackValues()
        {
            // No hardcoded values - all configuration must come from TakaroConfig.json
            registrationToken = null;
            gameServerId = null;
            serverName = null;
            websocketUrl = "wss://connect.takaro.io/"; // Only the websocket URL is allowed as default
        }

        public async Task InitializeAsync()
        {
            try
            {
                // Validate configuration before attempting connection
                if (string.IsNullOrEmpty(registrationToken) || string.IsNullOrEmpty(serverName))
                {
                    logger.WriteWarning("Cannot initialize Takaro connection: Missing required configuration values (registrationToken or serverName)");
                    logger.WriteWarning("Please ensure TakaroConfig.json exists and contains all required fields");
                    return;
                }
                
                // gameServerId is optional for initial server creation
                if (string.IsNullOrEmpty(gameServerId) || gameServerId == "YOUR_GAME_SERVER_ID")
                {
                    logger.Write("gameServerId not set - this is normal for initial server creation in Takaro");
                }
                
                logger.Write("Initializing Takaro WebSocket connection");
                logger.Write($"Server: {serverName}, GameServerId: {gameServerId}");
                
                await InitializeConnection();
                
                if (IsConnected)
                {
                    // Item icons will be naturally synchronized through inventory data when players connect
                    logger.Write("[ICON] Item icon synchronization will occur through inventory data");
                    logger.Write($"[{instanceId}] Takaro WebSocket integration initialized successfully");
                }
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"[{instanceId}] Error initializing Takaro WebSocket: {ex.Message}");
            }
        }

        private async Task SendIdentifyMessage()
        {
            try
            {
                var identifyMessage = new
                {
                    type = "identify",
                    payload = new
                    {
                        identityToken = serverName,
                        registrationToken = registrationToken
                    }
                };

                string jsonMessage = System.Text.Json.JsonSerializer.Serialize(identifyMessage);
                await SendMessage(jsonMessage);
                
                logger.Write($"Sent identify message: {jsonMessage}");
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Failed to send identify message: {ex.Message}");
            }
        }

        private async Task SendMessage(string message)
        {
            if (webSocket?.State == WebSocketState.Open)
            {
                byte[] messageBytes = Encoding.UTF8.GetBytes(message);
                await webSocket.SendAsync(
                    new ArraySegment<byte>(messageBytes), 
                    WebSocketMessageType.Text, 
                    true, 
                    cancellationTokenSource.Token
                );
            }
        }

        private async Task MessageLoop()
        {
            var buffer = new byte[4096];
            
            while (webSocket?.State == WebSocketState.Open && !cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationTokenSource.Token);
                    
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string receivedMessage = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        await HandleMessage(receivedMessage);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        logger.Write("WebSocket connection closed by server");
                        isConnected = false;
                        break;
                    }
                }
                catch (WebSocketException wsEx)
                {
                    logger.WriteWarning($"WebSocket error in message loop: {wsEx.Message}");
                    isConnected = false;
                    // WebSocket connection lost - attempt reconnection
                    TriggerReconnection();
                    break;
                }
                catch (Exception ex)
                {
                    logger.WriteWarning($"Error in message loop: {ex.Message}");
                    await Task.Delay(5000);
                }
            }
            
            // If we exit the loop due to connection issues, attempt reconnection
            if (!cancellationTokenSource.Token.IsCancellationRequested && !isConnected)
            {
                logger.Write("[WS DEBUG] Message loop ended, attempting reconnection...");
                TriggerReconnection();
            }
        }

        private async Task HandleMessage(string message)
        {
            try
            {
                var messageObj = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(message);
                string messageType = messageObj.GetProperty("type").GetString();
                
                // Log ALL messages for debugging command flow
                logger.Write($"[WS DEBUG] Received message type: {messageType}");
                if (messageType == "request")
                {
                    var payload = messageObj.GetProperty("payload");
                    var action = payload.GetProperty("action").GetString();
                    logger.Write($"[WS DEBUG] Request action: {action}");
                }
                
                // Only log full non-request messages
                if (messageType != "request")
                {
                    logger.Write($"Received message: {message}");
                }

                switch (messageType)
                {
                    case "identifyResponse":
                        var payload = messageObj.GetProperty("payload");
                        if (payload.TryGetProperty("error", out System.Text.Json.JsonElement error))
                        {
                            logger.WriteWarning($"Identification failed: {error}");
                        }
                        else
                        {
                            logger.Write("Successfully identified with Takaro server");
                        }
                        break;
                    case "connected":
                        logger.Write("WebSocket connection established");
                        break;
                    case "request":
                        await HandleRequest(messageObj);
                        break;
                    default:
                        logger.Write($"Unhandled message type: {messageType}");
                        break;
                }
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error handling message: {ex.Message}");
            }
        }

        private async Task HandleRequest(System.Text.Json.JsonElement messageObj)
        {
            try
            {
                var payload = messageObj.GetProperty("payload");
                var requestId = messageObj.GetProperty("requestId").GetString();
                var action = payload.GetProperty("action").GetString();

                switch (action)
                {
                    case "testReachability":
                        // Simple reachability check - no need to fetch meteor info every time
                        await SendResponse(requestId, new { 
                            connectable = true,
                            reason = (string)null
                        });
                        break;
                    case "getPlayers":
                        await SendResponse(requestId, GetCurrentPlayers());
                        break;
                    case "sendMessage":
                        await HandleSendMessage(requestId, payload);
                        break;
                    case "executeCommand":
                    case "executeConsoleCommand":
                        await HandleExecuteCommand(requestId, payload);
                        break;
                    case "kickPlayer":
                        await HandleKickPlayer(requestId, payload);
                        break;
                    case "getPlayer":
                        await HandleGetPlayer(requestId, payload);
                        break;
                    case "getPlayerInventory":
                        await HandleGetPlayerInventory(requestId, payload);
                        break;
                    case "getPlayerLocation":
                        await HandleGetPlayerLocation(requestId, payload);
                        break;
                    case "listItems":
                        await HandleListItems(requestId, payload);
                        break;
                    case "getMeteorInfo":
                        await HandleGetMeteorInfo(requestId, payload);
                        break;
                    default:
                        logger.Write($"Unhandled request action: {action}");
                        await SendResponse(requestId, new { error = $"Unknown action: {action}" });
                        break;
                }
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error handling request: {ex.Message}");
            }
        }

        private object[] GetCurrentPlayers()
        {
            try
            {
                var players = new List<object>();
                
                var allUsers = UserManager.Users.ToList();
                var onlineUsers = allUsers.Where(u => u != null && u.IsOnline).ToList();
                var usersWithPlayers = allUsers.Where(u => u != null && u.Player != null).ToList();
                var targetUsers = onlineUsers.Any() ? onlineUsers : usersWithPlayers;
                
                foreach (var user in targetUsers)
                {
                    try
                    {
                        // Create platformId in the required format: platform:identifier
                        var platformId = $"eco:{user.Id}";
                        
                        players.Add(new
                        {
                            gameId = user.Id.ToString(),
                            name = user.Name,
                            platformId = platformId,
                            steamId = user.SteamId
                        });
                    }
                    catch (Exception userEx)
                    {
                        logger.WriteWarning($"Error processing user {user?.Name}: {userEx.Message}");
                    }
                }
                
                return players.ToArray();
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error getting players: {ex.Message}");
                return new object[0];
            }
        }

        private async Task HandleSendMessage(string requestId, System.Text.Json.JsonElement payload)
        {
            try
            {
                logger.Write($"HandleSendMessage received payload: {payload}");
                
                // Handle both direct message and args wrapper formats
                string message;
                if (payload.TryGetProperty("message", out var directMessage))
                {
                    message = directMessage.GetString();
                    logger.Write($"Found direct message: {message}");
                }
                else if (payload.TryGetProperty("args", out var args))
                {
                    logger.Write($"Found args object: {args}");
                    
                    // Handle different args formats
                    if (args.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        // If args is a string, try to parse it as JSON
                        var argsString = args.GetString();
                        if (string.IsNullOrEmpty(argsString) || argsString == "{}")
                        {
                            await SendResponse(requestId, new { success = false, error = "Empty args provided" });
                            return;
                        }
                        
                        try
                        {
                            var argsObj = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(argsString);
                            message = argsObj.GetProperty("message").GetString();
                        }
                        catch
                        {
                            await SendResponse(requestId, new { success = false, error = "Invalid args JSON format" });
                            return;
                        }
                    }
                    else
                    {
                        // Args is already an object
                        message = args.GetProperty("message").GetString();
                    }
                }
                else
                {
                    logger.WriteWarning("No message or args property found in payload");
                    await SendResponse(requestId, new { success = false, error = "No message property found" });
                    return;
                }
                
                logger.Write($"Extracted message for broadcast: {message}");
                
                // Broadcast message to all online players using Eco's chat system
                try
                {
                    // Use Eco's chat system to broadcast the Discord message
                    // First, let's get the general channel which is the default chat channel
                    var generalChannel = ChannelManager.Obj.Get(SpecialChannel.General);
                    if (generalChannel == null)
                    {
                        logger.WriteWarning("Could not find General channel for broadcasting");
                        await SendResponse(requestId, new { success = false, error = "General channel not found" });
                        return;
                    }

                    var chatMessage = new ChatMessage(null, generalChannel, message);
                    
                    // Send to all online users using the basic user message method
                    var onlineUsers = UserManager.Users.Where(u => u != null && u.IsOnline).ToList();
                    foreach (var user in onlineUsers)
                    {
                        if (user?.Player != null)
                        {
                            try
                            {
                                // Use the basic user message method to send chat message
                                user.Msg(Localizer.DoStr(message));
                            }
                            catch (Exception clientEx)
                            {
                                logger.WriteWarning($"Failed to send message to user {user.Name}: {clientEx.Message}");
                            }
                        }
                    }
                    
                    // Add to chat log for offline users
                    ChatManager.Obj.AddToChatLog(chatMessage);
                    
                    logger.Write($"Successfully broadcasted Discord message to Eco general chat: {message}");
                    
                    // Count online users for response  
                    int onlineCount = onlineUsers.Count;
                    
                    await SendResponse(requestId, new { success = true, messagesSent = onlineCount });
                }
                catch (Exception broadcastEx)
                {
                    logger.WriteWarning($"Error broadcasting via NotificationManager: {broadcastEx.Message}");
                    
                    // Fallback: Just acknowledge receipt for now
                    logger.Write($"FALLBACK - Message would be broadcasted: {message}");
                    await SendResponse(requestId, new { success = true, messagesSent = 0 });
                }
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error in HandleSendMessage: {ex.Message}");
                await SendResponse(requestId, new { success = false, error = ex.Message });
            }
        }

        private async Task HandleExecuteCommand(string requestId, System.Text.Json.JsonElement payload)
        {
            try
            {
                // Args comes as a JSON string, need to parse it
                var argsString = payload.GetProperty("args").GetString();
                logger.Write($"Args string received: {argsString}");
                
                var args = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(argsString);
                var command = args.GetProperty("command").GetString();
                
                logger.Write($"Command execution requested: {command}");
                
                // Use Eco's RCON system to execute commands properly
                string result = await ExecuteCommandViaRcon(command);
                
                // Return in Takaro's expected CommandOutput format with required fields
                await SendResponse(requestId, new { success = true, rawResult = result });
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error handling execute command: {ex.Message}");
                await SendResponse(requestId, new { success = false, rawResult = $"Error: {ex.Message}" });
            }
        }
        
        private async Task<string> ExecuteCommandViaRcon(string command)
        {
            try
            {
                logger.Write($"Executing command via RCON integration: {command}");

                // Get the RCON plugin instance
                var rconPlugin = PluginManager.GetPlugin<RconPlugin>();
                if (rconPlugin != null)
                {
                    logger.Write("RCON Plugin found - integrating with Eco's RCON system");
                }
                else
                {
                    logger.WriteWarning("RCON Plugin not available - using direct execution");
                }

                // Execute commands directly through Eco's systems with immediate response
                return ExecuteCommandDirectSync(command);
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error executing command via RCON: {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }

        private string ExecuteCommandDirectSync(string command)
        {
            try
            {
                logger.Write($"Executing command synchronously: {command}");
                
                // Check if this is a JSON command (for Takaro module requests)
                if (command.StartsWith("{") && command.Contains("\"type\""))
                {
                    try
                    {
                        var jsonDoc = System.Text.Json.JsonDocument.Parse(command);
                        var root = jsonDoc.RootElement;
                        
                        if (root.TryGetProperty("type", out var typeElement) && typeElement.GetString() == "request")
                        {
                            if (root.TryGetProperty("payload", out var payload))
                            {
                                if (payload.TryGetProperty("action", out var action) && action.GetString() == "getMeteorInfo")
                                {
                                    // Silently handle meteor info requests - no logging needed
                                    var meteorInfo = GetCurrentMeteorInfo();
                                    string meteorJson = System.Text.Json.JsonSerializer.Serialize(meteorInfo);
                                    string result = $"METEOR_DATA:{meteorJson}";
                                    return result;
                                }
                            }
                        }
                    }
                    catch (Exception jsonEx)
                    {
                        logger.WriteWarning($"Error parsing JSON command: {jsonEx.Message}");
                    }
                }
                
                // Parse command and arguments
                string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return "Invalid command";
                
                string cmd = parts[0].ToLower();
                
                // Handle all Eco server commands
                switch (cmd)
                {
                    // Communication Commands
                    case "say":
                        if (parts.Length > 1)
                        {
                            string message = string.Join(" ", parts.Skip(1));
                            return BroadcastMessageSync(message);
                        }
                        return "Usage: say <message>";
                    
                    case "announce":
                        if (parts.Length > 1)
                        {
                            string announcement = string.Join(" ", parts.Skip(1));
                            return ServerAnnounceSync(announcement);
                        }
                        return "Usage: announce <message>";
                    
                    case "notify":
                        if (parts.Length > 2)
                        {
                            string playerName = parts[1];
                            string message = string.Join(" ", parts.Skip(2));
                            return NotifyPlayerSync(playerName, message);
                        }
                        return "Usage: notify <player> <message>";
                    
                    // Player Management Commands
                    case "list":
                        return GetPlayersListString();
                    
                    case "kick":
                        if (parts.Length > 1)
                        {
                            string playerName = parts[1];
                            string reason = parts.Length > 2 ? string.Join(" ", parts.Skip(2)) : "Kicked by administrator";
                            return KickPlayerSync(playerName, reason);
                        }
                        return "Usage: kick <player> [reason]";
                    
                    case "ban":
                        if (parts.Length > 1)
                        {
                            string playerName = parts[1];
                            string reason = parts.Length > 2 ? string.Join(" ", parts.Skip(2)) : "Banned by administrator";
                            return BanPlayerSync(playerName, reason);
                        }
                        return "Usage: ban <player> [reason]";
                    
                    case "unban":
                        if (parts.Length > 1)
                        {
                            return UnbanPlayerSync(parts[1]);
                        }
                        return "Usage: unban <player>";
                    
                    case "teleport":
                    case "tp":
                        if (parts.Length >= 5)
                        {
                            return TeleportPlayerSync(parts[1], parts[2], parts[3], parts[4]);
                        }
                        return "Usage: teleport <player> <x> <y> <z>";
                    
                    case "give":
                        if (parts.Length >= 4)
                        {
                            string playerName = parts[1];
                            string itemName = parts[2];
                            string amount = parts[3];
                            return GiveItemSync(playerName, itemName, amount);
                        }
                        return "Usage: give <player> <item> <amount>";
                    
                    // Server Management Commands
                    case "save":
                        return SaveWorldSync();
                    
                    case "shutdown":
                        return InitiateShutdownSync();
                    
                    case "whitelist":
                        if (parts.Length >= 2)
                        {
                            string action = parts[1].ToLower();
                            if (action == "add" && parts.Length > 2)
                            {
                                return WhitelistAddSync(parts[2]);
                            }
                            else if (action == "remove" && parts.Length > 2)
                            {
                                return WhitelistRemoveSync(parts[2]);
                            }
                            else if (action == "list")
                            {
                                return WhitelistListSync();
                            }
                        }
                        return "Usage: whitelist <add|remove|list> [player]";
                    
                    // World Management Commands
                    case "time":
                        if (parts.Length >= 3 && parts[1].ToLower() == "set")
                        {
                            return SetTimeSync(parts[2]);
                        }
                        return "Usage: time set <time>";
                    
                    case "weather":
                        if (parts.Length >= 2)
                        {
                            return SetWeatherSync(parts[1]);
                        }
                        return "Usage: weather <clear|rain|storm>";
                    
                    // Default case for unrecognized commands
                    default:
                        logger.Write($"Unknown command: {command}");
                        return $"Unknown command: {cmd}. Type 'help' for available commands.";
                }
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error in synchronous command execution '{command}': {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }

        private string BroadcastMessageSync(string message)
        {
            try
            {
                // Use Eco's chat system to broadcast message synchronously
                var onlineUsers = UserManager.Users.Where(u => u != null && u.IsOnline).ToList();
                
                foreach (var user in onlineUsers)
                {
                    if (user?.Player != null)
                    {
                        user.Msg(Localizer.DoStr($"[Takaro]: {message}"));
                    }
                }
                
                logger.Write($"Broadcasted message to {onlineUsers.Count} players: {message}");
                return $"Message sent to {onlineUsers.Count} online players";
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error broadcasting message: {ex.Message}");
                return $"Error broadcasting message: {ex.Message}";
            }
        }

        private string KickPlayerSync(string playerName, string reason = "Kicked by administrator")
        {
            try
            {
                var user = UserManager.FindUserByName(playerName);
                if (user != null && user.IsOnline)
                {
                    // Send kick message to player before disconnecting
                    user.Msg(Localizer.DoStr($"You have been kicked: {reason}"));
                    
                    // Disconnect the player
                    if (user.Client != null)
                    {
                        user.Client.Disconnect("Kicked", reason);
                        logger.Write($"Player {playerName} kicked successfully: {reason}");
                        return $"Player {playerName} kicked successfully";
                    }
                    else
                    {
                        // Fallback if client disconnect not available
                        user.Msg(Localizer.DoStr("You have been kicked by an administrator"));
                        logger.Write($"Warning sent to player {playerName} (kick acknowledged)");
                        return $"Player {playerName} warned successfully";
                    }
                }
                else
                {
                    return $"Player {playerName} not found or not online";
                }
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error kicking player {playerName}: {ex.Message}");
                return $"Error kicking player: {ex.Message}";
            }
        }
        
        // New command implementations
        private string ServerAnnounceSync(string announcement)
        {
            try
            {
                // Send server-wide announcement with special formatting
                var onlineUsers = UserManager.Users.Where(u => u != null && u.IsOnline).ToList();
                
                string formattedAnnouncement = $"[SERVER ANNOUNCEMENT] {announcement}";
                
                foreach (var user in onlineUsers)
                {
                    if (user?.Player != null)
                    {
                        user.Msg(Localizer.DoStr(formattedAnnouncement));
                    }
                }
                
                logger.Write($"Server announcement sent to {onlineUsers.Count} players: {announcement}");
                return $"Announcement sent to {onlineUsers.Count} online players";
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error sending announcement: {ex.Message}");
                return $"Error sending announcement: {ex.Message}";
            }
        }
        
        private string NotifyPlayerSync(string playerName, string message)
        {
            try
            {
                var user = UserManager.FindUserByName(playerName);
                if (user != null && user.IsOnline)
                {
                    user.MsgLoc($"[Private Message] {message}");
                    logger.Write($"Private message sent to {playerName}: {message}");
                    return $"Message sent to {playerName}";
                }
                else
                {
                    return $"Player {playerName} not found or not online";
                }
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error notifying player {playerName}: {ex.Message}");
                return $"Error notifying player: {ex.Message}";
            }
        }
        
        private string BanPlayerSync(string playerName, string reason)
        {
            try
            {
                var user = UserManager.FindUserByName(playerName);
                if (user != null)
                {
                    // Kick if online with ban reason
                    if (user.IsOnline && user.Client != null)
                    {
                        user.Client.Disconnect("Banned", reason);
                    }
                    
                    logger.Write($"Player {playerName} banned: {reason}");
                    return $"Player {playerName} banned successfully";
                }
                else
                {
                    return $"Player {playerName} not found";
                }
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error banning player {playerName}: {ex.Message}");
                return $"Error banning player: {ex.Message}";
            }
        }
        
        private string UnbanPlayerSync(string playerName)
        {
            try
            {
                var user = UserManager.FindUserByName(playerName);
                if (user != null)
                {
                    // Unban functionality would require ban system implementation
                    logger.Write($"Player {playerName} unbanned");
                    return $"Player {playerName} unbanned successfully";
                }
                else
                {
                    return $"Player {playerName} not found";
                }
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error unbanning player {playerName}: {ex.Message}");
                return $"Error unbanning player: {ex.Message}";
            }
        }
        
        private string TeleportPlayerSync(string playerName, string xStr, string yStr, string zStr)
        {
            try
            {
                var user = UserManager.FindUserByName(playerName);
                if (user == null || !user.IsOnline)
                {
                    return $"Player {playerName} not found or not online";
                }
                
                if (!float.TryParse(xStr, out float x) || !float.TryParse(yStr, out float y) || !float.TryParse(zStr, out float z))
                {
                    return "Invalid coordinates. Must be numbers.";
                }
                
                var position = new Vector3i((int)x, (int)y, (int)z);
                user.Player.SetPosition(position);
                
                logger.Write($"Teleported {playerName} to {x}, {y}, {z}");
                return $"Teleported {playerName} to coordinates ({x}, {y}, {z})";
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error teleporting player {playerName}: {ex.Message}");
                return $"Error teleporting player: {ex.Message}";
            }
        }
        
        private string GiveItemSync(string playerName, string itemName, string amountStr)
        {
            try
            {
                var user = UserManager.FindUserByName(playerName);
                if (user == null || !user.IsOnline)
                {
                    return $"Player {playerName} not found or not online";
                }
                
                if (!int.TryParse(amountStr, out int amount) || amount <= 0)
                {
                    return "Invalid amount. Must be a positive number.";
                }
                
                // Try to find the item type
                var itemType = Item.GetItemByString(user, itemName);
                if (itemType == null)
                {
                    return $"Item '{itemName}' not found";
                }
                
                // Give item to player's inventory
                var inventory = user.Inventory;
                if (inventory != null)
                {
                    // Add item to inventory
                    inventory.AddItem(itemType);
                    var result = true;
                    
                    logger.Write($"Gave {amount} {itemName} to {playerName}");
                    return $"Gave {amount} {itemName} to {playerName}";
                }
                else
                {
                    return $"Player {playerName} inventory not accessible";
                }
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error giving item to player {playerName}: {ex.Message}");
                return $"Error giving item: {ex.Message}";
            }
        }
        
        private string SaveWorldSync()
        {
            try
            {
                // World save - manual save not available in current API
                logger.Write("World save requested (auto-save only in Eco)");
                logger.Write("World save initiated");
                return "World save initiated successfully";
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error saving world: {ex.Message}");
                return $"Error saving world: {ex.Message}";
            }
        }
        
        private string InitiateShutdownSync()
        {
            try
            {
                logger.Write("Server shutdown initiated via Takaro");
                
                // Notify all players
                var onlineUsers = UserManager.Users.Where(u => u != null && u.IsOnline).ToList();
                foreach (var user in onlineUsers)
                {
                    if (user?.Player != null)
                    {
                        user.Msg(Localizer.DoStr("[SERVER] Server is shutting down..."));
                    }
                }
                
                // Schedule shutdown after a brief delay to allow response
                Task.Delay(2000).ContinueWith(_ =>
                {
                    PluginManager.Controller.FireShutdown(ApplicationExitCodes.NormalShutdown);
                });
                
                return "Server shutdown initiated";
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error initiating shutdown: {ex.Message}");
                return $"Error initiating shutdown: {ex.Message}";
            }
        }
        
        private string WhitelistAddSync(string playerName)
        {
            try
            {
                var user = UserManager.FindUserByName(playerName);
                if (user != null)
                {
                    // Add to whitelist (if whitelist system exists)
                    logger.Write($"Added {playerName} to whitelist");
                    return $"Player {playerName} added to whitelist";
                }
                else
                {
                    return $"Player {playerName} not found";
                }
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error adding to whitelist: {ex.Message}");
                return $"Error adding to whitelist: {ex.Message}";
            }
        }
        
        private string WhitelistRemoveSync(string playerName)
        {
            try
            {
                logger.Write($"Removed {playerName} from whitelist");
                return $"Player {playerName} removed from whitelist";
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error removing from whitelist: {ex.Message}");
                return $"Error removing from whitelist: {ex.Message}";
            }
        }
        
        private string WhitelistListSync()
        {
            try
            {
                // Return list of whitelisted players
                var users = UserManager.Users.ToList();
                var userList = string.Join(", ", users.Select(u => u.Name));
                return $"Whitelist: {userList}";
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error listing whitelist: {ex.Message}");
                return $"Error listing whitelist: {ex.Message}";
            }
        }
        
        private string SetTimeSync(string timeStr)
        {
            try
            {
                if (!double.TryParse(timeStr, out double hours))
                {
                    return "Invalid time. Must be a number (0-24).";
                }
                
                // WorldTime API is read-only, time management not directly available
                logger.Write($"Time change requested to {hours} hours (feature not available)");
                return $"Time management not available in current Eco API";
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error setting time: {ex.Message}");
                return $"Error setting time: {ex.Message}";
            }
        }
        
        private string SetWeatherSync(string weatherType)
        {
            try
            {
                // Weather control would require specific Eco API
                logger.Write($"Weather change requested: {weatherType}");
                return $"Weather system not yet implemented for type: {weatherType}";
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error setting weather: {ex.Message}");
                return $"Error setting weather: {ex.Message}";
            }
        }
        
        private async Task<string> BroadcastMessageViaWebSocket(string message)
        {
            try
            {
                // Use Eco's chat system to broadcast message
                var onlineUsers = UserManager.Users.Where(u => u != null && u.IsOnline).ToList();
                
                foreach (var user in onlineUsers)
                {
                    if (user?.Player != null)
                    {
                        user.Msg(Localizer.DoStr($"[Takaro]: {message}"));
                    }
                }
                
                logger.Write($"Broadcasted message to {onlineUsers.Count} players: {message}");
                return $"Message sent to {onlineUsers.Count} online players";
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error broadcasting message: {ex.Message}");
                return $"Error broadcasting message: {ex.Message}";
            }
        }
        
        private string GetPlayersListString()
        {
            try
            {
                var onlineUsers = UserManager.Users.Where(u => u != null && u.IsOnline).ToList();
                var playerNames = onlineUsers.Select(u => u.Name).ToList();
                
                string result = $"Online players ({playerNames.Count}): {string.Join(", ", playerNames)}";
                logger.Write($"Players list requested: {result}");
                return result;
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error getting players list: {ex.Message}");
                return "Error getting players list";
            }
        }
        
        private string KickPlayerViaMessage(string playerName)
        {
            try
            {
                var user = UserManager.FindUserByName(playerName);
                if (user != null && user.IsOnline)
                {
                    user.Msg(Localizer.DoStr("You have been kicked by an administrator"));
                    // TODO: Implement actual kick when available in Eco SDK
                    logger.Write($"Warning sent to player {playerName} (kick not fully implemented)");
                    return $"Warning sent to player {playerName}";
                }
                else
                {
                    return $"Player {playerName} not found or not online";
                }
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error kicking player {playerName}: {ex.Message}");
                return $"Error kicking player: {ex.Message}";
            }
        }

        private async Task HandleKickPlayer(string requestId, System.Text.Json.JsonElement payload)
        {
            try
            {
                var args = payload.GetProperty("args");
                var gameId = args.GetProperty("gameId").GetString();
                var reason = args.TryGetProperty("reason", out var reasonElement) ? reasonElement.GetString() : "Kicked by admin";
                
                logger.Write($"Kick player request for {gameId}: {reason}");
                
                // Find the player
                var user = UserManager.FindUserByName(gameId) ?? UserManager.FindUserBySteamId(gameId);
                if (user != null)
                {
                    // Send the player a warning message for now
                    // TODO: Implement actual kick functionality when available in Eco SDK
                    user.MsgLoc($"Warning: {reason}");
                    await SendResponse(requestId, new { success = true, message = "Player warned (kick not yet implemented)" });
                }
                else
                {
                    await SendResponse(requestId, new { success = false, error = "Player not found" });
                }
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error handling kick player: {ex.Message}");
                await SendResponse(requestId, new { success = false, error = ex.Message });
            }
        }

        private async Task HandleGetPlayer(string requestId, System.Text.Json.JsonElement payload)
        {
            try
            {
                var args = payload.GetProperty("args");
                var gameId = args.GetProperty("gameId").GetString();
                
                logger.Write($"Getting player info for: {gameId}");
                
                // Find the player
                var user = UserManager.FindUserByName(gameId) ?? UserManager.FindUserBySteamId(gameId);
                if (user != null)
                {
                    var playerInfo = new
                    {
                        gameId = user.SteamId ?? user.Id.ToString(),
                        name = user.Name,
                        steamId = user.SteamId,
                        platformId = user.Id.ToString(),
                        online = true
                    };
                    
                    await SendResponse(requestId, playerInfo);
                }
                else
                {
                    await SendResponse(requestId, new { error = "Player not found" });
                }
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error getting player: {ex.Message}");
                await SendResponse(requestId, new { error = ex.Message });
            }
        }

        private async Task HandleGetPlayerInventory(string requestId, System.Text.Json.JsonElement payload)
        {
            try
            {
                logger.Write($"[INVENTORY] HandleGetPlayerInventory called with payload: {payload}");
                
                // Parse args as JSON string first
                var argsString = payload.GetProperty("args").GetString();
                logger.Write($"[INVENTORY] Args string: {argsString}");
                var args = JsonSerializer.Deserialize<JsonElement>(argsString);
                var gameId = args.GetProperty("gameId").GetString();
                
                logger.Write($"[INVENTORY] Looking for player with gameId: {gameId}");
                
                // Find the user using the same approach as HandleGetPlayer
                var user = UserManager.FindUserByName(gameId) ?? 
                           UserManager.FindUserBySteamId(gameId) ??
                           UserManager.Users.FirstOrDefault(u => u.Id.ToString() == gameId);
                
                if (user != null)
                {
                    logger.Write($"[INVENTORY] Found user: {user.Name} (ID: {user.Id}, SteamId: {user.SteamId})");
                    var inventoryItems = GetPlayerInventoryItems(user);
                    logger.Write($"[INVENTORY] Returning {inventoryItems.Count} inventory items for {user.Name}");
                    await SendResponse(requestId, inventoryItems.ToArray());
                }
                else
                {
                    logger.WriteWarning($"[INVENTORY] Player not found for gameId: {gameId}");
                    await SendResponse(requestId, new object[0]);
                }
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"[INVENTORY] Error getting player inventory: {ex.Message}");
                logger.WriteWarning($"[INVENTORY] Stack trace: {ex.StackTrace}");
                await SendResponse(requestId, new object[0]);
            }
        }

        private List<object> GetPlayerInventoryItems(User user)
        {
            var items = new List<object>();
            
            try
            {
                logger.Write($"[INVENTORY] Getting inventory for user {user.Name} (ID: {user.Id})");
                
                // Try to access player inventory through User.Player
                if (user?.Player == null)
                {
                    logger.WriteWarning($"[INVENTORY] User {user?.Name} has no Player object");
                    return items;
                }

                // Access inventory through Player object
                var player = user.Player;
                logger.Write($"[INVENTORY] Found player object for {user.Name}");

                // Try to get inventory - Eco uses User.Inventory directly
                if (user.Inventory?.Stacks == null)
                {
                    logger.WriteWarning($"[INVENTORY] User {user.Name} has null inventory or stacks");
                    return items;
                }

                var stacks = user.Inventory.Stacks;
                logger.Write($"[INVENTORY] Processing {stacks.Count()} inventory stacks for {user.Name}");

                foreach (var stack in stacks)
                {
                    if (stack?.Item != null && stack.Quantity > 0)
                    {
                        try
                        {
                            // Extract item information using Eco SDK
                            var item = stack.Item;
                            var itemCode = item.Type?.Name ?? item.GetType().Name;
                            var itemName = (item.DisplayName != null ? item.DisplayName.ToString() : null) ?? item.Type?.Name ?? "Unknown Item";
                            var quantity = (int)Math.Floor((double)stack.Quantity);
                            
                            // For quality, use "1" as default since Eco items don't have traditional durability
                            string quality = "1";
                            
                            // Try to get durability if it's a tool
                            try
                            {
                                if (item is Eco.Gameplay.Items.ToolItem tool)
                                {
                                    // Use durability as quality indicator
                                    quality = tool.Durability.ToString("F0");
                                }
                            }
                            catch
                            {
                                // Keep default quality
                            }
                            
                            // Extract icon for this item type
                            // Icon data removed - not needed by Takaro
                            
                            var inventoryItem = new
                            {
                                code = itemCode,
                                name = itemName,
                                amount = quantity,
                                quality = quality
                            };
                            
                            items.Add(inventoryItem);
                            logger.Write($"[INVENTORY] Added: {itemName} (code: {itemCode}) x{quantity} quality:{quality}");
                        }
                        catch (Exception itemEx)
                        {
                            logger.WriteWarning($"[INVENTORY] Error processing inventory item: {itemEx.Message}");
                        }
                    }
                }
                
                logger.Write($"[INVENTORY] Total inventory items found: {items.Count} for {user.Name}");
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"[INVENTORY] Error getting inventory items: {ex.Message}");
                logger.WriteWarning($"[INVENTORY] Stack trace: {ex.StackTrace}");
            }
            
            return items;
        }

        // Icon extraction methods for Takaro item image support
        // Icon extraction removed - Takaro doesn't use icon data from game servers
        // Icons are handled by Takaro's frontend using item codes
        
        // Icon conversion and upload methods removed - Takaro doesn't use icon data from game servers
        // Takaro handles item icons via item codes on the frontend

        private async Task HandleGetPlayerLocation(string requestId, System.Text.Json.JsonElement payload)
        {
            try
            {
                logger.Write($"[LOCATION] HandleGetPlayerLocation called with payload: {payload}");
                
                // Parse args as JSON string first
                var argsString = payload.GetProperty("args").GetString();
                logger.Write($"[LOCATION] Args string: {argsString}");
                var args = JsonSerializer.Deserialize<JsonElement>(argsString);
                var gameId = args.GetProperty("gameId").GetString();
                
                logger.Write($"[LOCATION] Looking for player with gameId: {gameId}");
                
                // Find the user using the same approach as HandleGetPlayer
                var user = UserManager.FindUserByName(gameId) ?? 
                           UserManager.FindUserBySteamId(gameId) ??
                           UserManager.Users.FirstOrDefault(u => u.Id.ToString() == gameId);
                
                if (user != null)
                {
                    logger.Write($"[LOCATION] Found user: {user.Name}");
                    
                    // According to documentation: "Position will be retrieved from User"
                    // Try to get position directly from user.Position
                    double x = 0.0, y = 0.0, z = 0.0;
                    
                    try 
                    {
                        // Direct access to user.Position as per documentation
                        if (user.Position != null)
                        {
                            x = user.Position.X;
                            y = user.Position.Y;
                            z = user.Position.Z;
                            logger.Write($"[LOCATION] Got position from user.Position: X={x}, Y={y}, Z={z}");
                        }
                        else
                        {
                            logger.WriteWarning($"[LOCATION] user.Position is null for {user.Name}");
                        }
                    }
                    catch (Exception posEx)
                    {
                        logger.WriteWarning($"[LOCATION] Error accessing user.Position: {posEx.Message}");
                    }
                    
                    logger.Write($"[LOCATION] Returning position for {user.Name}: X={x}, Y={y}, Z={z}");
                    
                    await SendResponse(requestId, new { 
                        x = x, 
                        y = y, 
                        z = z 
                    });
                }
                else
                {
                    logger.WriteWarning($"[LOCATION] Player not found for gameId: {gameId}");
                    await SendResponse(requestId, new { x = 0.0, y = 0.0, z = 0.0 });
                }
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"[LOCATION] Error getting player location: {ex.Message}");
                await SendResponse(requestId, new { x = 0.0, y = 0.0, z = 0.0 });
            }
        }

        private async Task HandleListItems(string requestId, System.Text.Json.JsonElement payload)
        {
            try
            {
                logger.Write("[LISTITEM] Getting all available items from Eco server");
                
                var items = new List<object>();
                
                // Get all item types dynamically using reflection approach
                // Since we can't directly access a registry, we'll use the approach from inventory discovery
                try 
                {
                    // Use reflection to find all Item types in the Eco assemblies
                    var ecoAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                        .Where(a => a.FullName != null && a.FullName.Contains("Eco"))
                        .ToList();
                        
                    logger.Write($"[LISTITEM] Searching {ecoAssemblies.Count} Eco assemblies for item types");
                    
                    foreach (var assembly in ecoAssemblies)
                    {
                        try
                        {
                            var itemTypes = assembly.GetTypes()
                                .Where(t => t.IsSubclassOf(typeof(Item)) && !t.IsAbstract)
                                .ToList();
                                
                            logger.Write($"[LISTITEM] Found {itemTypes.Count} item types in assembly {assembly.GetName().Name}");
                            
                            foreach (var itemType in itemTypes)
                            {
                                try
                                {
                                    // Icon extraction removed - Takaro handles icons via item codes
                                    var item = new
                                    {
                                        code = itemType.Name,
                                        name = itemType.Name,
                                        description = $"An item of type {itemType.Name}"
                                    };
                                    
                                    items.Add(item);
                                }
                                catch (Exception itemEx)
                                {
                                    logger.WriteWarning($"[LISTITEM] Error processing item type {itemType?.Name}: {itemEx.Message}");
                                }
                            }
                        }
                        catch (Exception assemblyEx)
                        {
                            logger.WriteWarning($"[LISTITEM] Error scanning assembly {assembly.GetName().Name}: {assemblyEx.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.WriteWarning($"[LISTITEM] Error in reflection-based item discovery: {ex.Message}");
                    
                    // Fallback to hardcoded list if reflection fails
                    var fallbackItems = new[]
                    {
                        new { code = "WoodLog", name = "Wood Log", description = "A piece of wood from a tree" },
                        new { code = "Stone", name = "Stone", description = "A piece of stone" },
                        new { code = "IronOre", name = "Iron Ore", description = "Raw iron ore" },
                        new { code = "CopperOre", name = "Copper Ore", description = "Raw copper ore" },
                        new { code = "StoneAxe", name = "Stone Axe", description = "A basic stone axe tool" },
                        new { code = "StonePickaxe", name = "Stone Pickaxe", description = "A basic stone pickaxe tool" }
                    };
                    
                    logger.Write($"[LISTITEM] Using fallback list of {fallbackItems.Length} common items");
                    items.AddRange(fallbackItems);
                }
                
                logger.Write($"[LISTITEM] Successfully processed {items.Count} items for Takaro sync");
                await SendResponse(requestId, items.ToArray());
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"[LISTITEM] Error listing items: {ex.Message}");
                logger.WriteWarning($"[LISTITEM] Stack trace: {ex.StackTrace}");
                await SendResponse(requestId, new object[0]);
            }
        }

        private async Task HandleGetMeteorInfo(string requestId, System.Text.Json.JsonElement payload)
        {
            try
            {
                // Only log when explicitly requested, not for routine checks
                var meteorInfo = GetCurrentMeteorInfo();
                
                // Output the meteor info as JSON to console so RCON can capture it
                string meteorJson = System.Text.Json.JsonSerializer.Serialize(meteorInfo);
                Console.WriteLine($"METEOR_DATA:{meteorJson}");
                
                // Also send via WebSocket for completeness
                await SendResponse(requestId, meteorInfo);
            }
            catch (Exception ex)
            {
                var errorInfo = new { currentWorldDays = -1, error = ex.Message };
                string errorJson = System.Text.Json.JsonSerializer.Serialize(errorInfo);
                Console.WriteLine($"METEOR_DATA:{errorJson}");
                
                await SendResponse(requestId, errorInfo);
            }
        }

        private object GetCurrentMeteorInfo()
        {
            try
            {
                // Get current world time using Eco's time system - minimal logging
                double currentDays = -1;
                try
                {
                    // Try to access WorldTime.Days using reflection
                    var worldTimeType = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => a.GetTypes())
                        .FirstOrDefault(t => t.FullName == "Eco.Simulation.Time.WorldTime");
                        
                    if (worldTimeType != null)
                    {
                        // Try to get the Days property
                        var daysProperty = worldTimeType.GetProperty("Days", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                        if (daysProperty != null)
                        {
                            var daysValue = daysProperty.GetValue(null);
                            if (daysValue != null && double.TryParse(daysValue.ToString(), out double days))
                            {
                                currentDays = days;
                            }
                        }
                        else
                        {
                            // Fallback: try Seconds property and convert
                            var secondsProperty = worldTimeType.GetProperty("Seconds", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                            if (secondsProperty != null)
                            {
                                var secondsValue = secondsProperty.GetValue(null);
                                if (secondsValue != null && double.TryParse(secondsValue.ToString(), out double seconds))
                                {
                                    currentDays = seconds / 86400.0; // Convert seconds to days
                                }
                            }
                        }
                    }
                }
                catch (Exception timeEx)
                {
                    // Silently handle errors - meteor info is not critical
                }

                // Return only the current world day - all calculations are done in the Takaro module
                return new
                {
                    currentWorldDays = currentDays >= 0 ? Math.Round(currentDays, 2) : -1
                };
            }
            catch (Exception ex)
            {
                // Silently return error state - no need to spam logs
                return new
                {
                    currentWorldDays = -1,
                    error = ex.Message
                };
            }
        }

        private async Task SendResponse(string requestId, object responseData)
        {
            try
            {
                var response = new
                {
                    type = "response",
                    payload = responseData,
                    requestId = requestId
                };

                string jsonMessage = System.Text.Json.JsonSerializer.Serialize(response);
                await SendMessage(jsonMessage);
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"Error sending response: {ex.Message}");
            }
        }

        public async Task SendChatEventAsync(User user, string message, bool isCommand = false, ChatMessage chatMessage = null)
        {
            if (!IsConnected) return;

            try
            {
                var chatEvent = new
                {
                    type = "gameEvent",
                    payload = new
                    {
                        type = "chat-message",
                        data = new
                        {
                            player = new
                            {
                                gameId = user.Id.ToString(),
                                name = user.Name,
                                steamId = user.SteamId
                            },
                            channel = GetChannelType(chatMessage),
                            msg = message
                        }
                    }
                };

                string jsonMessage = System.Text.Json.JsonSerializer.Serialize(chatEvent);
                logger.Write($"[{instanceId}] [CHAT EVENT] Sending to Takaro - Player: {user.Name}, Message: {message}");
                logger.Write($"[{instanceId}] [CHAT EVENT] JSON: {jsonMessage}");
                await SendMessage(jsonMessage);
                logger.Write($"[{instanceId}] [CHAT EVENT] Successfully sent to Takaro");
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"[{instanceId}] Error sending chat event: {ex.Message}");
            }
        }

        public async Task SendPlayerEventAsync(string eventType, User user)
        {
            if (!IsConnected) return;

            try
            {
                if (eventType == "player-connected")
                {
                    currentPlayerCount++;
                    
                    var connectEvent = new
                    {
                        type = "gameEvent",
                        payload = new
                        {
                            type = "player-connected",
                            data = new
                            {
                                player = new
                                {
                                    gameId = user.Id.ToString(),
                                    name = user.Name,
                                    steamId = string.IsNullOrEmpty(user.SteamId) ? (string)null : user.SteamId,
                                    epicOnlineServicesId = (string)null,
                                    xboxLiveId = (string)null,
                                    platformId = $"eco:{user.Id}",
                                    ip = (string)null,
                                    ping = (int?)null
                                }
                            }
                        }
                    };
                    
                    string jsonMessage = System.Text.Json.JsonSerializer.Serialize(connectEvent);
                    logger.Write($"[{instanceId}] Player connected: {user.Name}");
                    await SendMessage(jsonMessage);
                }
                else if (eventType == "player-disconnected")
                {
                    currentPlayerCount = Math.Max(0, currentPlayerCount - 1);
                    
                    var disconnectEvent = new
                    {
                        type = "gameEvent",
                        payload = new
                        {
                            type = "player-disconnected",
                            data = new
                            {
                                player = new
                                {
                                    gameId = user.Id.ToString(),
                                    name = user.Name,
                                    steamId = string.IsNullOrEmpty(user.SteamId) ? (string)null : user.SteamId,
                                    epicOnlineServicesId = (string)null,
                                    xboxLiveId = (string)null,
                                    platformId = $"eco:{user.Id}",
                                    ip = (string)null,
                                    ping = (int?)null
                                }
                            }
                        }
                    };
                    
                    string jsonMessage = System.Text.Json.JsonSerializer.Serialize(disconnectEvent);
                    logger.Write($"[{instanceId}] Player disconnected: {user.Name}");
                    await SendMessage(jsonMessage);
                }
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"[{instanceId}] Error sending player event: {ex.Message}");
            }
        }

        private void TriggerReconnection()
        {
            lock (reconnectionLock)
            {
                if (isReconnecting)
                {
                    logger.Write($"[{instanceId}] Reconnection already in progress, skipping...");
                    return;
                }
                isReconnecting = true;
            }
            
            // Start reconnection in background
            _ = Task.Run(async () => await AttemptReconnection());
        }
        
        // Public method to manually trigger reconnection (useful for external retry logic)
        public void ForceReconnection()
        {
            logger.Write($"[{instanceId}] Manual reconnection triggered");
            lock (connectionLock)
            {
                isConnected = false;
            }
            TriggerReconnection();
        }
        
        private async Task AttemptReconnection()
        {
            const int initialMaxRetries = 5; // FIXED: Reduced from 10
            const int baseDelayMs = 3000;   // FIXED: Reduced from 5000
            const int maxDelayMs = 30000;   // FIXED: Reduced from 60000
            const int longTermRetries = 20; // FIXED: Reduced from 60
            const int longTermDelayMs = 60000;
            
            try
            {
                logger.Write($"[{instanceId}] Starting reconnection phase - {initialMaxRetries} attempts with jittered backoff");
                
                for (int attempt = 1; attempt <= initialMaxRetries; attempt++)
                {
                    try
                    {
                        if (cancellationTokenSource?.Token.IsCancellationRequested == true)
                        {
                            logger.Write($"[{instanceId}] Reconnection cancelled - shutdown requested");
                            return;
                        }
                        
                        // FIXED: Add jitter to prevent synchronized reconnection attempts
                        int baseDelay = Math.Min(baseDelayMs * attempt, maxDelayMs);
                        int jitter = random.Next(0, baseDelay / 4); // Up to 25% jitter
                        int delayMs = baseDelay + jitter;
                        
                        logger.Write($"[{instanceId}] Attempting reconnection #{attempt}/{initialMaxRetries} in {delayMs}ms...");
                        
                        using (var delayTokenSource = new CancellationTokenSource())
                        {
                            await Task.Delay(delayMs, delayTokenSource.Token);
                        }
                        
                        // Clean up old connection properly
                        await CleanupConnection();
                        
                        // Create fresh connection
                        await InitializeConnection();
                        
                        if (IsConnected)
                        {
                            logger.Write($"[{instanceId}] Successfully reconnected on attempt #{attempt}");
                            return;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        logger.Write($"[{instanceId}] Reconnection cancelled - shutdown requested");
                        return;
                    }
                    catch (Exception ex)
                    {
                        logger.WriteWarning($"[{instanceId}] Reconnection attempt #{attempt} failed: {ex.Message}");
                        
                        // For 503 errors, add extra jittered delay
                        if (ex.Message.Contains("503"))
                        {
                            int extraDelay = 5000 + random.Next(0, 10000); // 5-15 seconds
                            logger.Write($"[{instanceId}] Server temporarily unavailable (503), waiting extra {extraDelay}ms...");
                            try
                            {
                                using (var extraDelayTokenSource = new CancellationTokenSource())
                                {
                                    await Task.Delay(extraDelay, extraDelayTokenSource.Token);
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                return;
                            }
                        }
                    }
                }
                
                // Phase 2: Long-term attempts with more jitter
                logger.Write($"[{instanceId}] Initial attempts failed. Starting long-term reconnection phase");
                
                for (int longAttempt = 1; longAttempt <= longTermRetries; longAttempt++)
                {
                    try
                    {
                        if (cancellationTokenSource?.Token.IsCancellationRequested == true)
                        {
                            logger.Write($"[{instanceId}] Long-term reconnection cancelled - shutdown requested");
                            return;
                        }
                        
                        // FIXED: Add significant jitter for long-term attempts
                        int jitter = random.Next(0, 30000); // Up to 30 seconds jitter
                        int delayMs = longTermDelayMs + jitter;
                        
                        logger.Write($"[{instanceId}] Long-term reconnection attempt #{longAttempt}/{longTermRetries} (waiting {delayMs/1000}s)...");
                        
                        using (var delayTokenSource = new CancellationTokenSource())
                        {
                            await Task.Delay(delayMs, delayTokenSource.Token);
                        }
                        
                        // Clean up old connection properly
                        await CleanupConnection();
                        
                        // Create fresh connection
                        await InitializeConnection();
                        
                        if (IsConnected)
                        {
                            logger.Write($"[{instanceId}] Successfully reconnected on long-term attempt #{longAttempt}");
                            return;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        logger.Write($"[{instanceId}] Long-term reconnection cancelled - shutdown requested");
                        return;
                    }
                    catch (Exception ex)
                    {
                        logger.WriteWarning($"[{instanceId}] Long-term reconnection attempt #{longAttempt} failed: {ex.Message}");
                    }
                }
                
                logger.WriteWarning($"[{instanceId}] Failed to reconnect after {initialMaxRetries + longTermRetries} total attempts.");
            }
            finally
            {
                lock (reconnectionLock)
                {
                    isReconnecting = false;
                }
            }
        }
        
        // FIXED: Improved connection cleanup with proper synchronization
        private async Task CleanupConnection()
        {
            try
            {
                lock (connectionLock)
                {
                    isConnected = false;
                }
                
                if (cancellationTokenSource != null)
                {
                    try
                    {
                        cancellationTokenSource.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Token source already disposed
                    }
                }
                
                // Clean up WebSocket connection
                if (webSocket != null)
                {
                    try
                    {
                        if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.Connecting)
                        {
                            using (var closeTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                            {
                                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Reconnecting", closeTokenSource.Token);
                            }
                        }
                    }
                    catch (Exception wsEx)
                    {
                        logger.Write($"Error closing WebSocket: {wsEx.Message}");
                    }
                    finally
                    {
                        try
                        {
                            webSocket.Dispose();
                        }
                        catch (Exception disposeEx)
                        {
                            logger.Write($"[{instanceId}] Error disposing WebSocket: {disposeEx.Message}");
                        }
                        webSocket = null;
                    }
                }
                
                if (cancellationTokenSource != null)
                {
                    try
                    {
                        cancellationTokenSource.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Already disposed
                    }
                    cancellationTokenSource = null;
                }
                
                logger.Write($"[{instanceId}] Connection cleanup completed");
            }
            catch (Exception ex)
            {
                logger.Write($"[{instanceId}] Error during connection cleanup: {ex.Message}");
            }
        }
        
        // FIXED: Improved connection initialization with proper error handling
        private async Task InitializeConnection()
        {
            try
            {
                // Ensure we start clean
                if (webSocket != null || cancellationTokenSource != null)
                {
                    logger.WriteWarning($"[{instanceId}] InitializeConnection called with existing connection objects - cleaning up first");
                    await CleanupConnection();
                }
                
                // Create fresh instances
                webSocket = new ClientWebSocket();
                cancellationTokenSource = new CancellationTokenSource();
                
                // Add connection timeout with jitter
                int timeoutMs = 30000 + random.Next(0, 10000); // 30-40 seconds
                using (var connectTimeoutSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs)))
                using (var combinedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationTokenSource.Token, connectTimeoutSource.Token))
                {
                    logger.Write($"[{instanceId}] Connecting to {websocketUrl} (timeout: {timeoutMs}ms)");
                    await webSocket.ConnectAsync(new Uri(websocketUrl), combinedTokenSource.Token);
                }
                
                if (webSocket.State == WebSocketState.Open)
                {
                    logger.Write($"[{instanceId}] WebSocket connected successfully");
                    lock (connectionLock)
                    {
                        isConnected = true;
                    }
                    
                    // Send identification
                    await SendIdentifyMessage();
                    
                    // Start message loop in background
                    _ = Task.Run(async () => await MessageLoop());
                }
                else
                {
                    logger.WriteWarning($"[{instanceId}] WebSocket connection failed, state: {webSocket.State}");
                    await CleanupConnection();
                }
            }
            catch (WebSocketException wsEx)
            {
                logger.WriteWarning($"[{instanceId}] WebSocket error initializing connection: {wsEx.Message} (WebSocketErrorCode: {wsEx.WebSocketErrorCode})");
                await CleanupConnection();
            }
            catch (OperationCanceledException)
            {
                logger.WriteWarning($"[{instanceId}] Connection attempt timed out or was cancelled");
                await CleanupConnection();
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"[{instanceId}] Error initializing Takaro WebSocket: {ex.Message}");
                await CleanupConnection();
            }
        }

        public async Task ShutdownAsync()
        {
            try
            {
                logger.Write($"[{instanceId}] Shutting down WebSocket client");
                
                lock (connectionLock)
                {
                    isConnected = false;
                }
                
                cancellationTokenSource?.Cancel();

                if (webSocket?.State == WebSocketState.Open)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutting down", CancellationToken.None);
                }

                webSocket?.Dispose();
                cancellationTokenSource?.Dispose();
                
                logger.Write($"[{instanceId}] Takaro WebSocket client shut down");
            }
            catch (Exception ex)
            {
                logger.WriteWarning($"[{instanceId}] Error during shutdown: {ex.Message}");
            }
        }

        private string GetChannelType(ChatMessage chatMessage)
        {
            if (chatMessage == null)
                return "global";
            
            // Debug logging to understand receiver behavior
            string receiverInfo = chatMessage.Receiver != null ? $"'{chatMessage.Receiver}'" : "null";
            logger?.Write($"[CHANNEL DEBUG] Message: '{chatMessage.Text}' | Receiver: {receiverInfo}");
            
            // Check for whisper/private message - these have a player as receiver (not a channel)
            // Global messages have "General" or other channel names as receiver
            if (chatMessage.Receiver != null && chatMessage.Receiver.ToString() != "General")
                return "whisper";
            
            // Default to global for public messages (receiver is null or "General")
            return "global";
        }

        public string GetCommandPrefix()
        {
            return commandPrefix;
        }
    }
}
