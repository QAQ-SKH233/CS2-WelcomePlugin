using Microsoft.Extensions.DependencyInjection;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared;

namespace FGWelcome;

[PluginMetadata(Id = "FGWelcome", Version = "1.0.0", Name = "FGWelcome", Author = "x", Description = "No description.")]
public partial class FGWelcome : BasePlugin {
  public FGWelcome(ISwiftlyCore core) : base(core)
  {
  }

  public override void ConfigureSharedInterface(IInterfaceManager interfaceManager) {
  }

  public override void UseSharedInterface(IInterfaceManager interfaceManager) {
  }

  public override void Load(bool hotReload)
  {
        ServiceCollection services = new();
        services.AddSwiftly(Core).AddSingleton<IIpLocationService, IpLocationService>();
        services.AddSwiftly(Core).AddSingleton<GameEventService>();

        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<GameEventService>();
        provider.GetRequiredService<IIpLocationService>();


    }

  public override void Unload() {
  }
} 