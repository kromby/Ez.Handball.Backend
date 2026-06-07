using System.Runtime.CompilerServices;

namespace Ez.Handball.Tests;

internal static class TestModuleInitializer
{
    // AuthInfrastructureRegistration eagerly reads Jwt:SigningKey at host-build time and throws
    // if it is missing. Public read-endpoint WebApplicationFactory tests (Clubs, Seasons,
    // Tournaments, PlayerRating, etc.) do not set their own Jwt config, so they only built
    // successfully when some auth test happened to populate the env var first — an
    // order-dependent flake (issue #69). Setting the key here, before any test or host builds,
    // makes every factory build deterministically. Auth tests still override it via their own
    // in-memory configuration, so their behaviour is unchanged.
    [ModuleInitializer]
    public static void Init()
    {
        Environment.SetEnvironmentVariable("Jwt__SigningKey", "test-module-init-signing-key-32-bytes-min!!");
    }
}
