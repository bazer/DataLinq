using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Tests.Unit.Core;

public class InstanceFactoryTests
{
    [Test]
    public async Task NewDatabase_DataSourceAccessConstructor_CreatesDatabaseModel()
    {
        var dataSource = new FakeDataSourceAccess();

        var database = InstanceFactory.NewDatabase<FactoryDatabase>(dataSource);

        await Assert.That(database.DataSource).IsSameReferenceAs(dataSource);
    }

    private sealed class FactoryDatabase : IDatabaseModel, IDataLinqGeneratedDatabaseModel<FactoryDatabase>
    {
        public FactoryDatabase(DataSourceAccess dataSource)
        {
            DataSource = dataSource;
        }

        public DataSourceAccess DataSource { get; }

        public static GeneratedDatabaseModelDeclaration GetDataLinqGeneratedModel() => new([]);

        public static FactoryDatabase NewDataLinqDatabase(IDataSourceAccess dataSource) =>
            new((DataSourceAccess)dataSource);
    }

    private sealed class FakeDataSourceAccess : DataSourceAccess
    {
        public FakeDataSourceAccess()
            : base(null!)
        {
        }

        public override IDatabaseAccess DatabaseAccess => throw new NotSupportedException();

        public override IEnumerable<T> GetFromQuery<T>(string query) => throw new NotSupportedException();

        public override IEnumerable<T> GetFromCommand<T>(IDbCommand dbCommand) => throw new NotSupportedException();
    }
}
