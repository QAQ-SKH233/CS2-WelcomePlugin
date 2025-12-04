using System.Reflection;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.ProtobufDefinitions;


public class GameEventService{

    private ISwiftlyCore Core { get; set; }
    //playerID 应该指的是index
    private Dictionary<int, string> _playerLocation = new Dictionary<int, string>();

    private bool FirstJoin = true;

    private readonly IIpLocationService _ipLocationService;

    private readonly ILogger<GameEventService> _logger;
    public GameEventService(ISwiftlyCore core, ILogger<GameEventService> logger, IIpLocationService ipLocationService){
        Core = core;
        _logger = logger;
        _logger.LogInformation("GameEventService loaded ");

        _ipLocationService = ipLocationService;
        core.Registrator.Register(this);

        Core.GameEvent.HookPre<EventPlayerSpawn>(OnPlayerSpawned);

    }


    [EventListener<EventDelegates.OnClientConnected>]
    public void OnClientConnected(IOnClientConnectedEvent @event){
        var player = Core.PlayerManager.GetPlayer(@event.PlayerId);

        if (player != null)
        {

            Task.Run(async () =>
            {
                try
                {
                    var locationResult = await _ipLocationService.QueryLocationAsync(player.IPAddress, default);

                    if (locationResult.Success)
                    {
                        _playerLocation[@event.PlayerId] = locationResult.Location!;
                        _logger.LogInformation($"Got location: {locationResult.Location}");
                    }
                    else
                    {
                        // 处理错误情况
                        _logger.LogError($"Failed to get location: {locationResult.ErrorMessage}");
                    }
                }
                catch (Exception ex)
                {
                    // 异常处理
                    _logger.LogError($"Error getting location: {ex.Message}");
                }
            });

        }

    }

    public HookResult OnPlayerSpawned(EventPlayerSpawn @event){
        
        var player = Core.PlayerManager.GetPlayer(@event.UserId);
        if(player == null || !player.IsValid || player.IsFakeClient) return HookResult.Continue;
        if (FirstJoin == true)
        {
            Core.PlayerManager.SendMessage(MessageType.Chat, $"[green]欢迎玩家{player.Controller.PlayerName}加入FG抢先体验服务器[default]\nTA来自 [red]{_playerLocation[player.PlayerID]}");

            FirstJoin = false;
        }

        return HookResult.Continue;

    }
}