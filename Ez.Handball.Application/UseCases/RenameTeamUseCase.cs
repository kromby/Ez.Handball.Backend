using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Validation;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record RenameTeamResult
{
    public sealed record Success(ManagerView View) : RenameTeamResult;
    public sealed record ValidationError(string Field) : RenameTeamResult;
    public sealed record TeamNameTaken : RenameTeamResult;
    public sealed record NoTeam : RenameTeamResult;
}

public interface IRenameTeamUseCase
{
    Task<RenameTeamResult> ExecuteAsync(string userId, string newName, CancellationToken ct);
}

public sealed class RenameTeamUseCase : IRenameTeamUseCase
{
    private readonly IGameTeamRepository _teams;
    private readonly IGameTeamNameIndexRepository _nameIndex;
    private readonly IGetManagerUseCase _getManager;

    public RenameTeamUseCase(
        IGameTeamRepository teams, IGameTeamNameIndexRepository nameIndex, IGetManagerUseCase getManager)
    {
        _teams = teams;
        _nameIndex = nameIndex;
        _getManager = getManager;
    }

    public async Task<RenameTeamResult> ExecuteAsync(string userId, string newName, CancellationToken ct)
    {
        if (!AuthValidation.IsValidTeamName(newName)) return new RenameTeamResult.ValidationError("teamName");
        if (!ManagerValidation.IsAllowedTeamName(newName)) return new RenameTeamResult.ValidationError("teamName");

        var team = await _teams.GetAsync(userId, GameFlavor.Fantasy, ct);
        if (team is null) return new RenameTeamResult.NoTeam();

        var newNormalized = ManagerValidation.NormalizeTeamName(newName);
        var oldNormalized = ManagerValidation.NormalizeTeamName(team.Name);

        // Case/whitespace-only edit normalizes to the same name: pure no-op success. Skip both
        // the reserve/release dance (which would self-conflict on the user's own index row) and
        // the write — there is no meaningful change to persist.
        if (newNormalized == oldNormalized)
            return await ProjectAsync(userId, ct);

        var teamId = GameTeamId.For(userId, GameFlavor.Fantasy);
        if (!await _nameIndex.TryReserveAsync(newNormalized, teamId, ct))
            return new RenameTeamResult.TeamNameTaken();

        // Reserve-new succeeded. If the rename write fails, release the just-reserved name so a
        // retry isn't permanently blocked by an orphaned reservation; only free the OLD name once
        // the rename has actually committed.
        try
        {
            await _teams.RenameAsync(userId, GameFlavor.Fantasy, newName.Trim(), ct);
        }
        catch
        {
            await _nameIndex.ReleaseAsync(newNormalized, ct);
            throw;
        }
        await _nameIndex.ReleaseAsync(oldNormalized, ct);

        return await ProjectAsync(userId, ct);
    }

    private async Task<RenameTeamResult> ProjectAsync(string userId, CancellationToken ct)
    {
        var manager = await _getManager.ExecuteAsync(userId, null, ct);
        // The team exists (just renamed) and the rule set is the default, so this is Found in
        // practice; fall back defensively if the projection cannot be built.
        return manager is GetManagerResult.Found f
            ? new RenameTeamResult.Success(f.View)
            : new RenameTeamResult.NoTeam();
    }
}
