namespace Veel.GraphQL.SchemaIntelligence.Tests;

internal static class TestPaths
{
    private static readonly Lazy<string> RepositoryRoot = new(FindRepositoryRoot);

    public static string FromRepositoryRoot(string relativePath) => Path.Combine(RepositoryRoot.Value, relativePath);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Veel.GraphQL.SchemaIntelligence.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
