using RATools.Tests.Workspaces;

namespace RATools.Tests.Persistence.Postgres;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PostgresNodeEditingTests(PostgresFixture fixture)
{
    [RequiresPostgresFact]
    public async Task CreateCloneInheritAndEditUseRealPostgresConstraints()
    {
        await using var database = fixture.CreateDbContext();
        using var workspace = new NodeMoveTestWorkspace(database);
        await CtdNodeEditingTests.ExerciseAsync(workspace);
    }
}
