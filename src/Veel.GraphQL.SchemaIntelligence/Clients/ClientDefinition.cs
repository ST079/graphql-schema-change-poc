namespace Veel.GraphQL.SchemaIntelligence.Clients;

/// <summary>
/// A consumer of the GraphQL API (Android, Frontend, iOS, ...) and where its operations live.
/// Every client is analyzed the same way; the name is only used for attribution in results.
/// </summary>
/// <param name="Name">Display name, e.g. <c>Android</c>.</param>
/// <param name="OperationsDirectory">Directory scanned recursively for <c>.graphql</c>/<c>.gql</c> files.</param>
public sealed record ClientDefinition(string Name, string OperationsDirectory);
