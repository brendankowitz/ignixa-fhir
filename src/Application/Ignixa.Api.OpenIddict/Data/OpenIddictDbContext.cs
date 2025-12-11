using Microsoft.EntityFrameworkCore;

namespace Ignixa.Api.OpenIddict.Data;

/// <summary>
/// Entity Framework DbContext for OpenIddict token and application storage.
/// </summary>
public class OpenIddictDbContext(DbContextOptions<OpenIddictDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure OpenIddict entity mappings
        modelBuilder.UseOpenIddict();
    }
}
