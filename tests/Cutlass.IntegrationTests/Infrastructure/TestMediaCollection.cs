namespace Cutlass.IntegrationTests.Infrastructure;

/// <summary>
/// The xunit collection sharing one <see cref="TestMediaFixture"/>. Test classes opt in with
/// <c>[Collection(TestMediaCollection.Name)]</c> and take the fixture as a constructor parameter.
/// </summary>
[CollectionDefinition(Name)]
public sealed class TestMediaCollection : ICollectionFixture<TestMediaFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "TestMedia";
}
