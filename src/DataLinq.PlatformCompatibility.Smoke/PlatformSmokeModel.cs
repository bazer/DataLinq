using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.PlatformCompatibility.Smoke;

[Database("platform_smoke")]
[UseCache]
public sealed partial class PlatformSmokeDb(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<PlatformSmokeOwner> Owners { get; } = new(dataSource);

    public DbRead<PlatformSmokeTask> Tasks { get; } = new(dataSource);
}

[Table("platform_smoke_owners")]
public abstract partial class PlatformSmokeOwner(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<PlatformSmokeOwner, PlatformSmokeDb>(rowData, dataSource), ITableModel<PlatformSmokeDb>
{
    [PrimaryKey]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Column("id")]
    public abstract int Id { get; }

    [Type(DatabaseType.SQLite, "TEXT")]
    [Column("name")]
    public abstract string Name { get; }

    [Relation("platform_smoke_tasks", "owner_id", "FK_platform_smoke_task_owner")]
    public abstract IImmutableRelation<PlatformSmokeTask> Tasks { get; }
}

[Table("platform_smoke_tasks")]
public abstract partial class PlatformSmokeTask(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<PlatformSmokeTask, PlatformSmokeDb>(rowData, dataSource), ITableModel<PlatformSmokeDb>
{
    [PrimaryKey]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Column("id")]
    public abstract int Id { get; }

    [ForeignKey("platform_smoke_owners", "id", "FK_platform_smoke_task_owner")]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Column("owner_id")]
    public abstract int OwnerId { get; }

    [Type(DatabaseType.SQLite, "TEXT")]
    [Column("title")]
    public abstract string Title { get; }

    [Type(DatabaseType.SQLite, "INTEGER")]
    [Column("priority")]
    public abstract int Priority { get; }

    [Type(DatabaseType.SQLite, "INTEGER")]
    [Column("completed")]
    public abstract bool Completed { get; }

    [Relation("platform_smoke_owners", "id", "FK_platform_smoke_task_owner")]
    public abstract PlatformSmokeOwner Owner { get; }
}
