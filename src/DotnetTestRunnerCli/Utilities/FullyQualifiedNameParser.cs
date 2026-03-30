namespace DotnetTestRunnerCli.Utilities;

public static class FullyQualifiedNameParser
{
    /// <summary>
    /// Parses a fully qualified test name into (namespace, className, methodName).
    /// Handles xUnit Theory names with parameters like "MyClass.Add(1,2,3)".
    /// Rule: last segment = method, second-to-last = class, rest = namespace.
    /// </summary>
    public static (string Namespace, string ClassName, string MethodName) Parse(string fullyQualifiedName)
    {
        var clean = StripParameters(fullyQualifiedName).Trim();
        var parts = clean.Split('.');

        return parts.Length switch
        {
            0 => ("", "", clean),
            1 => ("", "", parts[0]),
            2 => ("", parts[0], parts[1]),
            _ => (string.Join('.', parts[..^2]), parts[^2], parts[^1])
        };
    }

    /// <summary>
    /// Strips parameter lists from Theory test names: "MyClass.Add(1, 2, 3)" → "MyClass.Add"
    /// Also handles nested parens in parameter values.
    /// </summary>
    public static string StripParameters(string fullyQualifiedName)
    {
        var parenIndex = fullyQualifiedName.IndexOf('(');
        return parenIndex >= 0 ? fullyQualifiedName[..parenIndex] : fullyQualifiedName;
    }
}
