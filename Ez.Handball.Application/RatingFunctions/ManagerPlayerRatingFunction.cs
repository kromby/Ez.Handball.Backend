using Ez.Handball.Domain;

namespace Ez.Handball.Application.RatingFunctions;

// Deliberate stub. Real rating/market-value modelling is future work (see issue #20).
public sealed class ManagerPlayerRatingFunction : IPlayerRatingFunction
{
    public GameFlavor Flavor => GameFlavor.Manager;
    public int? DefaultRuleSetVersion => null;

    public PlayerRating Compute(PlayerRatingInputs inputs)
    {
        var s = inputs.Stats;
        double rating = s.Goals + s.Games;
        double marketValue = rating * 1000;

        // Stub component model: unlike FantasyPlayerRatingFunction, where each
        // PlayerRatingComponent.Value is a raw stat count, this exposes the derived
        // market value directly. It will be aligned with Fantasy's breakdown when the
        // real rating model lands (#20).
        var components = new List<PlayerRatingComponent>
        {
            new("marketValue", marketValue, 1, marketValue)
        };

        return new PlayerRating(inputs.PlayerId, "manager", rating, components, "manager-v0");
    }
}
