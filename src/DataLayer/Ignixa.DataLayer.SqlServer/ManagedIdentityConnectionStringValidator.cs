using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Rejects password-bearing connection strings in Production, so a tenant's database can only be reached
/// with Managed Identity (Azure AD) credentials there.
/// <para>
/// Relocated from <c>SqlEntityFrameworkRepositoryFactory.ValidateManagedIdentityAuthentication</c>. That
/// version took the host's environment name as a constructor argument and then ignored it, reading
/// <c>ASPNETCORE_ENVIRONMENT</c> off the process instead. Constructing the factory with
/// <c>environment: "Production"</c> while the variable was unset therefore skipped the guard entirely --
/// exactly the deployment shape (a container with the environment supplied through configuration rather
/// than an OS variable) the guard exists for. This class reads only <paramref name="environmentName"/>.
/// </para>
/// <para>
/// Production deployments should use Managed Identity only: passwords must be stored and rotated, cannot
/// be governed by Azure RBAC, and defeat least privilege. Expected form:
/// <c>Server=tcp:servername.database.windows.net,1433;Database=FhirDatabase;Encrypt=true;TrustServerCertificate=false;Authentication=Active Directory Managed Identity;</c>
/// Any non-Production environment is exempt so local and test SQL Server instances can use integrated
/// security or SQL logins.
/// </para>
/// </summary>
public sealed class ManagedIdentityConnectionStringValidator(
    string environmentName,
    ILogger<ManagedIdentityConnectionStringValidator> logger)
{
    private const string ProductionEnvironmentName = "Production";

    private readonly string _environmentName = environmentName ?? throw new ArgumentNullException(nameof(environmentName));
    private readonly ILogger<ManagedIdentityConnectionStringValidator> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    /// <exception cref="InvalidOperationException">
    /// The environment is Production and <paramref name="connectionString"/> carries a password, or one
    /// <see cref="SqlConnectionStringBuilder"/> cannot parse.
    /// </exception>
    public void Validate(string connectionString, int tenantId)
    {
        ArgumentNullException.ThrowIfNull(connectionString);

        if (!string.Equals(_environmentName, ProductionEnvironmentName, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Tenant {TenantId} Managed Identity validation skipped (Environment: {Environment}). " +
                "SQL authentication allowed for non-production environments.",
                tenantId,
                _environmentName);
            return;
        }

        var hasPassword = HasPasswordKeyword(connectionString, tenantId);

        if (hasPassword)
        {
            _logger.LogError(
                "Tenant {TenantId} connection string contains local SQL authentication (User ID/Password). " +
                "Production deployments MUST use Managed Identity (Azure AD) authentication. " +
                "Expected: Authentication=Active Directory Managed Identity;",
                tenantId);
            throw new InvalidOperationException(
                $"Tenant {tenantId} connection string contains local SQL authentication (User ID/Password). " +
                "Production deployments MUST use Managed Identity (Azure AD) authentication. " +
                "Expected: Authentication=Active Directory Managed Identity;");
        }

        _logger.LogInformation("Tenant {TenantId} validated for Managed Identity authentication", tenantId);
    }

    /// <summary>
    /// Parses with <see cref="SqlConnectionStringBuilder"/> rather than a substring scan for "Password=" /
    /// "pwd=": the builder recognizes every keyword alias and tolerates the whitespace ADO.NET connection
    /// strings otherwise allow around "=" (e.g. "Password ="), which a literal substring match does not.
    /// <para>
    /// An explicit but empty password ("Password=;") is indistinguishable from an absent one:
    /// <see cref="SqlConnectionStringBuilder"/> itself drops an empty value from the keyword collection
    /// when it parses the string, the same way it does when the keyword is assigned an empty string
    /// through its indexer. There is no parsed representation left for this method to tell the two apart,
    /// so an explicit empty password is treated as no password -- which matches how the connection would
    /// eventually behave against the server anyway (an empty password is a failed login, not a Managed
    /// Identity bypass).
    /// </para>
    /// <para>
    /// A connection string too malformed to parse cannot be evaluated for credentials at all, so it is
    /// rethrown as an <see cref="InvalidOperationException"/> naming the tenant, rather than left to
    /// surface as a bare <see cref="ArgumentException"/> with no indication of which tenant's
    /// configuration is bad.
    /// </para>
    /// </summary>
    private bool HasPasswordKeyword(string connectionString, int tenantId)
    {
        try
        {
            return !string.IsNullOrEmpty(new SqlConnectionStringBuilder(connectionString).Password);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Tenant {TenantId} connection string could not be parsed", tenantId);
            throw new InvalidOperationException(
                $"Tenant {tenantId} connection string could not be parsed: {ex.Message}", ex);
        }
    }
}