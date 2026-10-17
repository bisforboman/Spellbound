using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Raven.Client.Documents.Session;

namespace Spellbound.Web.Hosting;

/// <summary>
/// RavenDB.Identity leaves saving to the app. Every UserManager (and SignInManager) change goes through
/// CreateAsync, DeleteAsync or UpdateUserAsync, so saving here persists it before any redirect is sent.
/// </summary>
public sealed class SavingUserManager(
    IAsyncDocumentSession session,
    IUserStore<ApplicationUser> store,
    IOptions<IdentityOptions> optionsAccessor,
    IPasswordHasher<ApplicationUser> passwordHasher,
    IEnumerable<IUserValidator<ApplicationUser>> userValidators,
    IEnumerable<IPasswordValidator<ApplicationUser>> passwordValidators,
    ILookupNormalizer keyNormalizer,
    IdentityErrorDescriber errors,
    IServiceProvider services,
    ILogger<UserManager<ApplicationUser>> logger,
    IAccountLifecycle game)
    : UserManager<ApplicationUser>(store, optionsAccessor, passwordHasher, userValidators, passwordValidators,
        keyNormalizer, errors, services, logger)
{
    public override async Task<IdentityResult> CreateAsync(ApplicationUser user) =>
        await SaveIfSucceeded(await base.CreateAsync(user));

    public override async Task<IdentityResult> DeleteAsync(ApplicationUser user)
    {
        var result = await base.DeleteAsync(user);
        if (result.Succeeded && user.Id is { } id)
        {
            session.Delete(HostStore.SignalKey(id)); // the account's network hashes go with it
            await game.OnAccountDeletedAsync(session, id); // and the player's game data, in the same save
        }

        return await SaveIfSucceeded(result);
    }

    protected override async Task<IdentityResult> UpdateUserAsync(ApplicationUser user) =>
        await SaveIfSucceeded(await base.UpdateUserAsync(user));

    /// <summary>
    /// RavenDB.Identity finds users by name with an eventually consistent query, which can miss a user that was
    /// just written. Usernames are always the email here, and email lookups are consistent, so try that first.
    /// </summary>
    public override async Task<ApplicationUser?> FindByNameAsync(string userName) =>
        await FindByEmailAsync(userName) ?? await base.FindByNameAsync(userName);

    private async Task<IdentityResult> SaveIfSucceeded(IdentityResult result)
    {
        if (result.Succeeded)
        {
            await session.SaveChangesAsync(CancellationToken);
        }

        return result;
    }
}
