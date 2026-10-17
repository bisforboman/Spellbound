using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Spellbound.Web.Hosting;

/// <summary>
/// The app's UserManager. Deleting an account also deletes the account's network hashes and, through
/// <see cref="IAccountLifecycle"/>, the player's game data, all in one transaction.
/// </summary>
public sealed class HostUserManager(
    HostDb db,
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
    public override async Task<IdentityResult> DeleteAsync(ApplicationUser user)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken);
        await db.AccountNetworks.Where(n => n.OwnerId == user.Id).ExecuteDeleteAsync(CancellationToken);
        await game.OnAccountDeletedAsync(db, user.Id);
        var result = await base.DeleteAsync(user);
        if (result.Succeeded)
        {
            await transaction.CommitAsync(CancellationToken);
        }

        return result;
    }
}
