using DataLinq.Core.Factories;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Interfaces;

public interface IDataLinqGeneratedDatabaseModel<TDatabase>
    where TDatabase : class, IDatabaseModel, IDataLinqGeneratedDatabaseModel<TDatabase>
{
    static abstract MetadataDatabaseDraft GetDataLinqGeneratedMetadata();
    static abstract void SetDataLinqGeneratedMetadata(DatabaseDefinition metadata);
    static abstract GeneratedDatabaseModelDeclaration GetDataLinqGeneratedModel();
    static abstract TDatabase NewDataLinqDatabase(IDataSourceAccess dataSource);
}
