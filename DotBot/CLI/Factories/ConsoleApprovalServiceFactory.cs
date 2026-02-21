using DotBot.Abstractions;
using DotBot.Security;

namespace DotBot.CLI.Factories;

/// <summary>
/// Factory for creating console approval service instances.
/// </summary>
public sealed class ConsoleApprovalServiceFactory : IApprovalServiceFactory
{
    /// <inheritdoc />
    public IApprovalService Create(ApprovalServiceContext context)
    {
        return new ConsoleApprovalService(context.ApprovalStore);
    }
}
