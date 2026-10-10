using SecureAgentLab.Comparisons.Evidence;

namespace SecureAgentLab.Comparisons.Scenarios.Portable;

public static class PortableScenarios
{
    public static readonly string[] Names = ["approval", "scope", "actions", "files", "injection", "retry", "audit", "version", "quota", "retry-limit"];
    public static Comparison Run(string id, string root) => id switch
    {
        "approval" => ApprovalScenario.Run(root),
        "scope" => ScopeScenario.Run(root),
        "actions" => ActionsScenario.Run(root),
        "files" => FilesScenario.Run(root),
        "injection" => InjectionScenario.Run(root),
        "retry" => RetryScenario.Run(root),
        "audit" => AuditScenario.Run(root),
        "version" => VersionScenario.Run(root),
        "quota" => QuotaScenario.Run(root),
        "retry-limit" => RetryLimitScenario.Run(root),
        _ => throw new ArgumentException("Unknown comparison scenario")
    };
}
