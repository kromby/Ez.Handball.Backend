using Ez.Handball.Domain;

namespace Ez.Handball.Application.ValueFunctions;

// Deliberate stub. Real rating/market-value modelling is future work (see issue #20).
public sealed class ManagerPlayerValueFunction : IPlayerValueFunction
{
    public ValueFlavor Flavor => ValueFlavor.Manager;
    public int? DefaultRuleSetVersion => null;

    public PlayerValue Compute(PlayerValueInputs inputs)
    {
        var s = inputs.Stats;
        double rating = s.Goals + s.Games;
        double marketValue = rating * 1000;

        var components = new List<PlayerValueComponent>
        {
            new("marketValue", marketValue, 1, marketValue)
        };

        return new PlayerValue(inputs.PlayerId, "manager", rating, components, "manager-v0");
    }
}
