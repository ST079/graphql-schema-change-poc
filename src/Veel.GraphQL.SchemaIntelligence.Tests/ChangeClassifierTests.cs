using Veel.GraphQL.SchemaIntelligence.Changes;
using Veel.GraphQL.SchemaIntelligence.Schema;

namespace Veel.GraphQL.SchemaIntelligence.Tests;

public class ChangeClassifierTests
{
    private readonly ChangeClassifier _classifier = new();

    private static TypeReference Named(string name) => new NamedTypeReference(name);
    private static TypeReference NonNull(string name) => new NonNullTypeReference(new NamedTypeReference(name));

    private static SchemaChange Change(
        ChangeType changeType,
        string typeName = "Campaign",
        string? field = null,
        string? argument = null,
        TypeReference? oldType = null,
        TypeReference? newType = null,
        string? oldValue = null,
        string? newValue = null) =>
        new(changeType, typeName, field, argument, oldType, newType, oldValue, newValue, $"{changeType} on {typeName}");

    private static IReadOnlyList<ClassifiedSchemaChange> DiffAndClassify(string oldSdl, string newSdl) =>
        new ChangeClassifier().Classify(new SchemaDiffer().Compare(SchemaLoader.Parse(oldSdl), SchemaLoader.Parse(newSdl)));

    // ---- Rules for the core change types ---------------------------------------------------------

    [Fact]
    public void TypeAdded_IsInfo()
    {
        var result = _classifier.Classify(Change(ChangeType.TypeAdded, "CampaignVideo", newValue: "Object"));

        Assert.Equal(ChangeSeverity.Info, result.Severity);
        Assert.Equal("A new GraphQL type was added. Existing clients are not required to use it.", result.Reason);
    }

    [Fact]
    public void TypeRemoved_IsBreaking()
    {
        var result = _classifier.Classify(Change(ChangeType.TypeRemoved, "CampaignVideo", oldValue: "Object"));

        Assert.Equal(ChangeSeverity.Breaking, result.Severity);
        Assert.Equal("An existing GraphQL type was removed and clients using it may fail.", result.Reason);
    }

    [Fact]
    public void FieldAdded_IsInfo()
    {
        var result = _classifier.Classify(Change(ChangeType.FieldAdded, field: "video", newType: Named("CampaignVideo")));

        Assert.Equal(ChangeSeverity.Info, result.Severity);
        Assert.Equal("A new field was added. Existing clients can continue using the previous schema.", result.Reason);
    }

    [Fact]
    public void FieldAdded_NonNull_IsStillInfo()
    {
        var result = _classifier.Classify(Change(ChangeType.FieldAdded, field: "slug", newType: NonNull("String")));

        Assert.Equal(ChangeSeverity.Info, result.Severity);
    }

    [Fact]
    public void FieldRemoved_IsBreaking()
    {
        var result = _classifier.Classify(Change(ChangeType.FieldRemoved, field: "videoUrl", oldType: Named("String")));

        Assert.Equal(ChangeSeverity.Breaking, result.Severity);
        Assert.Equal("An existing field was removed and clients using it may fail.", result.Reason);
    }

    [Theory]
    [InlineData("String", "Int")]
    [InlineData("String", "String!")]
    [InlineData("String!", "String")]
    [InlineData("String", "CampaignVideo")]
    [InlineData("[Campaign]", "[Campaign!]!")]
    public void FieldTypeChanged_IsAlwaysBreaking(string oldType, string newType)
    {
        var change = Assert.Single(DiffAndClassify(
            $"type Campaign {{ id: ID! f: {oldType} }} type CampaignVideo {{ id: ID! }} type Query {{ c: Campaign, v: CampaignVideo }}",
            $"type Campaign {{ id: ID! f: {newType} }} type CampaignVideo {{ id: ID! }} type Query {{ c: Campaign, v: CampaignVideo }}"));

        Assert.Equal(ChangeType.FieldTypeChanged, change.Change.ChangeType);
        Assert.Equal(ChangeSeverity.Breaking, change.Severity);
        Assert.Equal("The type of an existing field changed and clients expecting the previous type may fail.", change.Reason);
    }

    [Fact]
    public void EnumValueAdded_IsInfo()
    {
        var result = _classifier.Classify(Change(ChangeType.EnumValueAdded, "CampaignStatus", field: "ARCHIVED"));

        Assert.Equal(ChangeSeverity.Info, result.Severity);
        Assert.Equal("A new enum value was added.", result.Reason);
    }

    [Fact]
    public void EnumValueRemoved_IsBreaking()
    {
        var result = _classifier.Classify(Change(ChangeType.EnumValueRemoved, "CampaignStatus", field: "ARCHIVED"));

        Assert.Equal(ChangeSeverity.Breaking, result.Severity);
        Assert.Equal("An existing enum value was removed and clients using that value may fail.", result.Reason);
    }

    // ---- Rules for the remaining Phase 3 change types --------------------------------------------

    [Theory]
    [InlineData(ChangeType.TypeKindChanged, ChangeSeverity.Breaking)]
    [InlineData(ChangeType.ArgumentRemoved, ChangeSeverity.Breaking)]
    [InlineData(ChangeType.ArgumentTypeChanged, ChangeSeverity.Breaking)]
    [InlineData(ChangeType.InputFieldRemoved, ChangeSeverity.Breaking)]
    [InlineData(ChangeType.InputFieldTypeChanged, ChangeSeverity.Breaking)]
    [InlineData(ChangeType.InterfaceAdded, ChangeSeverity.Info)]
    [InlineData(ChangeType.InterfaceRemoved, ChangeSeverity.Breaking)]
    [InlineData(ChangeType.UnionMemberAdded, ChangeSeverity.Info)]
    [InlineData(ChangeType.UnionMemberRemoved, ChangeSeverity.Breaking)]
    [InlineData(ChangeType.DeprecationAdded, ChangeSeverity.Warning)]
    [InlineData(ChangeType.DeprecationRemoved, ChangeSeverity.Info)]
    public void OtherChangeTypes_HaveExpectedSeverity(ChangeType changeType, ChangeSeverity expected)
    {
        var result = _classifier.Classify(Change(changeType, field: "f"));

        Assert.Equal(expected, result.Severity);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Theory]
    [InlineData("first: Int", ChangeSeverity.Info)]
    [InlineData("first: Int! = 10", ChangeSeverity.Info)]
    [InlineData("first: Int!", ChangeSeverity.Breaking)]
    public void ArgumentAdded_IsBreakingOnlyWhenRequiredWithoutDefault(string argument, ChangeSeverity expected)
    {
        var change = Assert.Single(DiffAndClassify(
            "type Query { campaigns: String }",
            $"type Query {{ campaigns({argument}): String }}"));

        Assert.Equal(ChangeType.ArgumentAdded, change.Change.ChangeType);
        Assert.Equal(expected, change.Severity);
    }

    [Theory]
    [InlineData("tag: String", ChangeSeverity.Info)]
    [InlineData("tag: String! = \"all\"", ChangeSeverity.Info)]
    [InlineData("tag: String!", ChangeSeverity.Breaking)]
    public void InputFieldAdded_IsBreakingOnlyWhenRequiredWithoutDefault(string inputField, ChangeSeverity expected)
    {
        var change = Assert.Single(DiffAndClassify(
            "input Filter { status: String } type Query { campaigns(filter: Filter): String }",
            $"input Filter {{ status: String {inputField} }} type Query {{ campaigns(filter: Filter): String }}"));

        Assert.Equal(ChangeType.InputFieldAdded, change.Change.ChangeType);
        Assert.Equal(expected, change.Severity);
    }

    // ---- General behaviour -------------------------------------------------------------------

    [Fact]
    public void EveryDefinedChangeType_HasARule()
    {
        foreach (var changeType in Enum.GetValues<ChangeType>())
        {
            var result = _classifier.Classify(Change(changeType));
            Assert.False(string.IsNullOrWhiteSpace(result.Reason), $"{changeType} has no reason");
        }
    }

    [Fact]
    public void UnknownChangeType_ThrowsNotSupportedException()
    {
        var ex = Assert.Throws<NotSupportedException>(() => _classifier.Classify(Change((ChangeType)999)));

        Assert.Equal("Unsupported schema change type: 999", ex.Message);
    }

    [Fact]
    public void Classify_PreservesOriginalChange()
    {
        var original = Change(ChangeType.FieldTypeChanged, "Campaign", field: "title", oldType: Named("String"), newType: NonNull("String"));

        var result = _classifier.Classify(original);

        Assert.Same(original, result.Change);
        Assert.Equal(ChangeType.FieldTypeChanged, result.Change.ChangeType);
        Assert.Equal("Campaign", result.Change.TypeName);
        Assert.Equal("title", result.Change.FieldName);
        Assert.Equal("String", result.Change.OldType!.ToString());
        Assert.Equal("String!", result.Change.NewType!.ToString());
    }

    [Fact]
    public void ClassifyMany_PreservesOrder_AndIsDeterministic()
    {
        SchemaChange[] changes =
        [
            Change(ChangeType.FieldRemoved, field: "b"),
            Change(ChangeType.FieldAdded, field: "a"),
            Change(ChangeType.DeprecationAdded, field: "c"),
        ];

        var first = _classifier.Classify(changes);
        var second = _classifier.Classify(changes);

        Assert.Equal(changes, first.Select(c => c.Change));
        Assert.Equal(
            [ChangeSeverity.Breaking, ChangeSeverity.Info, ChangeSeverity.Warning],
            first.Select(c => c.Severity));
        Assert.Equal(first, second);
    }

    [Fact]
    public void ClassifyMany_Empty_ReturnsEmpty()
    {
        Assert.Empty(_classifier.Classify([]));
    }

    // ---- Demo schemas end-to-end --------------------------------------------------------------

    [Fact]
    public void DemoSchemas_LoadDiffClassify_ProducesExpectedSeverities()
    {
        var oldSchema = SchemaLoader.LoadFromFile(TestPaths.FromRepositoryRoot("demo/old-schema.graphql"));
        var newSchema = SchemaLoader.LoadFromFile(TestPaths.FromRepositoryRoot("demo/new-schema.graphql"));

        var classified = _classifier.Classify(new SchemaDiffer().Compare(oldSchema, newSchema));

        Assert.Equal(
            [
                "FieldAdded Campaign.video →CampaignVideo Info",
                "FieldRemoved Campaign.videoUrl String→ Breaking",
                "TypeAdded CampaignVideo → Info",
            ],
            classified.Select(c => $"{c.Change.ChangeType} {c.Change.Path} {c.Change.OldType}→{c.Change.NewType} {c.Severity}"));
    }
}
