using NexSync.Application.Validators;
using NexSync.Domain.Exceptions;

namespace NexSync.Tests;

[TestClass]
public sealed class AuthValidatorTests
{
    [TestMethod]
    public void ValidateRegister_AcceptsValidInput()
    {
        AuthValidator.ValidateRegister("user@nexsync.dev", "password123", "Test User");
    }

    [TestMethod]
    public void ValidateRegister_RejectsInvalidEmail()
    {
        Assert.ThrowsExactly<DomainException>(() => AuthValidator.ValidateRegister("not-an-email", "password123", "Test"));
    }

    [TestMethod]
    public void ValidateRegister_RejectsShortPassword()
    {
        Assert.ThrowsExactly<DomainException>(() => AuthValidator.ValidateRegister("a@b.dev", "short", "Test"));
    }

    [TestMethod]
    public void ValidateRegister_RejectsLongPassword()
    {
        Assert.ThrowsExactly<DomainException>(() => AuthValidator.ValidateRegister("a@b.dev", new string('x', 73), "Test"));
    }

    [TestMethod]
    public void ValidateRegister_RejectsEmptyName()
    {
        Assert.ThrowsExactly<DomainException>(() => AuthValidator.ValidateRegister("a@b.dev", "password123", " "));
    }
}



