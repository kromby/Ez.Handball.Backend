using Ez.Handball.Domain;

namespace Ez.Handball.Application.ValueFunctions;

// Deliberate stub. Real rating/market-value modelling is future work (see issue #20).
public sealed class ManagerPlayerValueFunction : IPlayerValueFunction
{
    public GameFlavor Flavor => GameFlavor.Manager;
    public int? DefaultRuleSetVersion => null;

    public PlayerValue Compute(PlayerValueInputs inputs)
    {
        var s = inputs.Stats;
        double rating = s.Goals + s.Games;
        double marketValue = rating * 1000;

        // Stub component model: unlike FantasyPlayerValueFunction, where each
        // PlayerValueComponent.Value is a raw stat count, this exposes the derived
        // market value directly. It will be aligned with Fantasy's breakdown when the
        // real rating model lands (#20).
        var components = new List<PlayerValueComponent>
        {
            new("marketValue", marketValue, 1, marketValue)
        };

        return new PlayerValue(inputs.PlayerId, "manager", rating, components, "manager-v0");
    }
}
