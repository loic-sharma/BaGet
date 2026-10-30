using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Catalog.Rebuild;
using BaGet.Core;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet;

/// <summary>The <c>baget</c> commands: <c>import downloads</c>, <c>catalog rebuild</c> and <c>account …</c>.</summary>
internal static class HostCommandLineExtensions
{
    /// <summary>Add every BaGet command to <paramref name="commands"/>; each runs against <paramref name="app"/>'s services.</summary>
    public static CommandLineApplication AddBaGetCommands(this CommandLineApplication commands, WebApplication app)
    {
        commands.AddImportCommands(app);
        commands.AddCatalogCommands(app);
        commands.AddAccountCommands(app);
        return commands;
    }

    private static void AddImportCommands(this CommandLineApplication commands, WebApplication app)
    {
        commands.Command("import", import =>
        {
            import.Command("downloads", downloads =>
            {
                downloads.Description = "Import the download counts into a repository's packages.";
                var repository = downloads.Option("-r|--repository <name>", "The repository to import into (default: 'default').", CommandOptionType.SingleValue);
                downloads.OnExecuteAsync(async cancellationToken =>
                {
                    using (var scope = await app.CreateRepositoryScopeAsync(repository.Value(), cancellationToken))
                    {
                        var importer = scope.ServiceProvider.GetRequiredService<DownloadsImporter>();

                        await importer.ImportAsync(cancellationToken);
                    }
                });
            });
        });
    }

    private static void AddCatalogCommands(this CommandLineApplication commands, WebApplication app)
    {
        commands.Command("catalog", catalog =>
        {
            catalog.Command("rebuild", rebuild =>
            {
                rebuild.Description = "Append the catalog entries missing for the packages in a repository.";
                var repository = rebuild.Option("-r|--repository <name>", "The repository whose catalog to rebuild (default: 'default').", CommandOptionType.SingleValue);
                rebuild.OnExecuteAsync(async cancellationToken =>
                {
                    using (var scope = await app.CreateRepositoryScopeAsync(repository.Value(), cancellationToken))
                    {
                        var rebuilder = scope.ServiceProvider.GetRequiredService<CatalogRebuilder>();

                        await rebuilder.RebuildAsync(cancellationToken);
                    }
                });
            });
        });
    }

    private static void AddAccountCommands(this CommandLineApplication commands, WebApplication app)
    {
        commands.Command("account", account =>
        {
            account.Command("reset-password", reset =>
            {
                reset.Description = "Set a generated password for a user and switch off their two-factor authentication.";
                var userName = reset.Argument("user", "The user name.").IsRequired();
                var email = reset.Option("--email <address>", "Set this email address for the user and mark it confirmed. Needed for a user with no address when SMTP is configured.", CommandOptionType.SingleValue);
                reset.OnExecuteAsync(async cancellationToken =>
                {
                    using var scope = app.Services.CreateScope();
                    var recovery = scope.ServiceProvider.GetService<AccountRecoveryService>();
                    if (recovery == null)
                    {
                        Console.Error.WriteLine("Accounts are not enabled (\"Accounts:Enabled\").");
                        return 1;
                    }

                    var password = await recovery.ResetPasswordAsync(userName.Value, cancellationToken, email.Value());
                    if (password == null)
                    {
                        Console.Error.WriteLine($"There is no user named \"{userName.Value}\".");
                        return 1;
                    }

                    Console.WriteLine($"New password for {userName.Value}: {password}");
                    Console.WriteLine("Two-factor authentication is off for this user. Sign in and change the password now.");

                    var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<BaGetUser>>();
                    #pragma warning disable CS8604
                    var user = await users.FindByNameAsync(userName.Value);
                    #pragma warning restore CS8604
                    if (string.IsNullOrEmpty(user?.Email) && scope.ServiceProvider.GetRequiredService<IOptions<EmailOptions>>().Value.IsConfigured)
                    {
                        Console.WriteLine("This user has no email address, and sign-in needs a confirmed one while SMTP is configured. Run again with --email <address>.");
                    }

                    return 0;
                });
            });

            account.Command("approve", approve =>
            {
                approve.Description = "Enable a self-registered account that waits for approval (Accounts:RequireApproval), so it can sign in.";
                var userName = approve.Argument("user", "The user name.").IsRequired();
                approve.OnExecuteAsync(async cancellationToken =>
                {
                    using var scope = app.Services.CreateScope();
                    var approvals = scope.ServiceProvider.GetService<AccountApprovalService>();
                    if (approvals == null)
                    {
                        Console.Error.WriteLine("Accounts are not enabled (\"Accounts:Enabled\").");
                        return 1;
                    }

                    switch (await approvals.ApproveAsync(userName.Value, "command line", cancellationToken))
                    {
                        case AccountApprovalResult.UserNotFound:
                            Console.Error.WriteLine($"There is no user named \"{userName.Value}\".");
                            return 1;
                        case AccountApprovalResult.Conflict:
                            Console.Error.WriteLine($"{userName.Value} was changed or removed meanwhile, so nothing was approved. Run the command again.");
                            return 1;
                        case AccountApprovalResult.NotPending:
                            Console.WriteLine($"{userName.Value} is not waiting for approval.");
                            return 0;
                        default:
                            Console.WriteLine($"{userName.Value} was approved and can sign in.");
                            return 0;
                    }
                });
            });

            foreach (var (name, promote) in new[] { ("promote", true), ("demote", false) })
            {
                account.Command(name, change =>
                {
                    change.Description = promote
                        ? "Make a user an administrator, who may change the server's settings and promote other administrators."
                        : "Remove a user's administrator flag. The last administrator cannot be demoted.";
                    var userName = change.Argument("user", "The user name.").IsRequired();
                    change.OnExecuteAsync(async cancellationToken =>
                    {
                        using var scope = app.Services.CreateScope();
                        var administrators = scope.ServiceProvider.GetService<AdministratorService>();
                        if (administrators == null)
                        {
                            Console.Error.WriteLine("Accounts are not enabled (\"Accounts:Enabled\").");
                            return 1;
                        }

                        var result = promote
                            ? await administrators.PromoteAsync(userName.Value, "command line", cancellationToken)
                            : await administrators.DemoteAsync(userName.Value, "command line", cancellationToken);

                        switch (result)
                        {
                            case AdministratorChange.UserNotFound:
                                Console.Error.WriteLine($"There is no user named \"{userName.Value}\".");
                                return 1;
                            case AdministratorChange.LastAdministrator:
                                Console.Error.WriteLine("The last administrator cannot be demoted. Promote another user first.");
                                return 1;
                            case AdministratorChange.Unchanged:
                                Console.WriteLine(promote
                                    ? $"{userName.Value} is already an administrator."
                                    : $"{userName.Value} is not an administrator.");
                                return 0;
                            default:
                                Console.WriteLine(promote
                                    ? $"{userName.Value} is now an administrator."
                                    : $"{userName.Value} is no longer an administrator.");
                                return 0;
                        }
                    });
                });
            }
        });
    }

    /// <summary>
    /// A DI scope for a command, pinned to the named repository (the default one when <paramref name="name"/> is null).
    /// </summary>
    public static async Task<IServiceScope> CreateRepositoryScopeAsync(
        this WebApplication app,
        string? name,
        CancellationToken cancellationToken)
    {
        if (name == null) return app.Services.CreateScope();

        var scope = app.Services.CreateScope();
        var found = await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync(name, cancellationToken);
        if (found == null)
        {
            scope.Dispose();
            throw new InvalidOperationException($"There is no repository named '{name}'.");
        }

        scope.ServiceProvider.GetRequiredService<IRepositoryContext>().Use(found);
        return scope;
    }
}
