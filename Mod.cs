using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace WeekendDrops;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.20fpsguy.WeekendDrops";
    public override string Name { get; init; } = "WeekendDrops";
    public override string Author { get; init; } = "20fpsguy";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } =
        new(typeof(ModMetadata).Assembly.GetName().Version!.ToString(3));
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.0");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; } = "";
    public override bool? IsBundleMod { get; init; }
    public override string License { get; init; } = "MIT";
}
