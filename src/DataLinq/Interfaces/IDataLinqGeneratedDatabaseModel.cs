using System;
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

    static virtual TDatabase NewDataLinqReadDatabase(IDataLinqReadSource readSource)
    {
        ArgumentNullException.ThrowIfNull(readSource);

        if (readSource is IDataSourceAccess legacySource)
            return TDatabase.NewDataLinqDatabase(legacySource);

        throw new InvalidOperationException(
            $"Generated read-source database factory not defined for '{typeof(TDatabase).FullName}'. " +
            "Regenerate the database model root with neutral IDataLinqReadSource construction before using a read source that does not implement IDataSourceAccess.");
    }
}

public interface IDataLinqGeneratedDatabaseModel<TDatabase> : IDatabaseModel<TDatabase>
    where TDatabase : class, IDatabaseModel<TDatabase>
{
}
