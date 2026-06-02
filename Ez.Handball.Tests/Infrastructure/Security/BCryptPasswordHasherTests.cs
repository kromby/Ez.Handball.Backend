using Ez.Handball.Application.Abstractions;
using Ez.Handball.Infrastructure.Security;

namespace Ez.Handball.Tests.Infrastructure.Security;

public class BCryptPasswordHasherTests
{
    private readonly IPasswordHasher _sut = new BCryptPasswordHasher();

    [Fact]
    public void Hash_ThenVerify_Succeeds()
    {
        var hash = _sut.Hash("hunter2hunter2");
        Assert.True(_sut.Verify("hunter2hunter2", hash));
    }

    [Fact]
    public void Verify_WrongPassword_Fails()
    {
        var hash = _sut.Hash("hunter2hunter2");
        Assert.False(_sut.Verify("not-the-password", hash));
    }

    [Fact]
    public void Hash_IsSaltedSoSamePasswordYieldsDifferentHashes()
    {
        Assert.NotEqual(_sut.Hash("hunter2hunter2"), _sut.Hash("hunter2hunter2"));
    }

    [Fact]
    public void VerifyDummy_AlwaysReturnsFalse_AndDoesNotThrow()
        => Assert.False(_sut.VerifyDummy("anything"));
}
