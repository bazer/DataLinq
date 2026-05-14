using DataLinq.Core.Factories;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Interfaces;

public interface IDatabaseModel<TDatabase> : IDatabaseModel
    where TDatabase : class, IDatabaseModel<TDatabase>
{
    static abstract MetadataDatabaseDraft GetDataLinqGeneratedMetadata();
    static abstract void SetDataLinqGeneratedMetadata(DatabaseDefinition metadata);
    static abstract GeneratedDatabaseModelDeclaration GetDataLinqGeneratedModel();
    static abstract TDatabase NewDataLinqDatabase(IDataSourceAccess dataSource);
}

public interface IDataLinqGeneratedDatabaseModel<TDatabase> : IDatabaseModel<TDatabase>
    where TDatabase : class, IDatabaseModel<TDatabase>
{
}
