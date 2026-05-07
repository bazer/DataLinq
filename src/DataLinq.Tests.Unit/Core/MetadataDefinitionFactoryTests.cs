using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using DataLinq.Testing;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public class MetadataDefinitionFactoryTests
{
    [Test]
    public async Task Build_TypedRelationDraft_AssignsOrdinalsAndResolvesRelations()
    {
        var database = CreateRelationTypedDraft();

        var built = new MetadataDefinitionFactory().Build(database).ValueOrException();

        var userTable = built.TableModels.Single(tm => tm.Table.DbName == "users").Table;
        var orderTable = built.TableModels.Single(tm => tm.Table.DbName == "orders").Table;

        await Assert.That(userTable.Columns.Select(c => c.Index).ToArray()).IsEquivalentTo([0, 1]);
        await Assert.That(orderTable.Columns.Select(c => c.Index).ToArray()).IsEquivalentTo([0, 1, 2]);
        await Assert.That(userTable.ColumnIndices.Any(x => x.Characteristic == IndexCharacteristic.PrimaryKey)).IsTrue();
        await Assert.That(orderTable.ColumnIndices.Any(x => x.Characteristic == IndexCharacteristic.ForeignKey && x.Name == "FK_Order_User")).IsTrue();

        var orderToUser = orderTable.Model.RelationProperties["Customer"];
        var userToOrders = userTable.Model.RelationProperties["Order"];

        await Assert.That(orderToUser.RelationPart.Type).IsEqualTo(RelationPartType.ForeignKey);
        await Assert.That(userToOrders.RelationPart.Type).IsEqualTo(RelationPartType.CandidateKey);
        await Assert.That(ReferenceEquals(orderToUser.RelationPart.Relation, userToOrders.RelationPart.Relation)).IsTrue();
    }

    [Test]
    public async Task Build_TypedRelationDraft_ReturnsFrozenMetadataSnapshot()
    {
        var database = CreateRelationTypedDraft(includeFreezeCoverageMetadata: true);

        var built = new MetadataDefinitionFactory().Build(database).ValueOrException();

        var orderTableModel = built.TableModels.Single(tm => tm.Table.DbName == "orders");
        var orderTable = orderTableModel.Table;
        var orderModel = orderTableModel.Model;
        var orderId = orderTable.Columns.Single(column => column.DbName == "order_id");
        var amount = orderTable.Columns.Single(column => column.DbName == "amount");
        var orderIdProperty = orderModel.ValueProperties["OrderId"];
        var customerProperty = orderModel.RelationProperties["Customer"];
        var foreignKeyIndex = orderTable.ColumnIndices.Single(index => index.Name == "FK_Order_User");
        var relation = customerProperty.RelationPart.Relation;
        var dbType = orderId.DbTypes.Single();
        var changedFile = new CsFileDeclaration("Changed.cs");
        var changedClass = new CsTypeDeclaration("Changed", "TestNamespace", ModelCsType.Class);
        var changedInterface = new CsTypeDeclaration("IChanged", "TestNamespace", ModelCsType.Interface);
        var changedRecord = new CsTypeDeclaration("ChangedRecord", "TestNamespace", ModelCsType.Record);
        var sourceSpan = new SourceTextSpan(1, 1);
        var attributeSourceSpan = new SourceTextSpan(2, 1);
        var databaseAttribute = new DatabaseAttribute("ChangedDb");
        var modelAttribute = new TableAttribute("changed");
        var propertyAttribute = new ColumnAttribute("changed");
        ColumnIndex NewAmountIndex(string name) => new(name, IndexCharacteristic.Simple, IndexType.BTREE, [amount]);

        await Assert.That(built.IsFrozen).IsTrue();
        await Assert.That(orderTableModel.IsFrozen).IsTrue();
        await Assert.That(orderTable.IsFrozen).IsTrue();
        await Assert.That(orderModel.IsFrozen).IsTrue();
        await Assert.That(orderId.IsFrozen).IsTrue();
        await Assert.That(orderIdProperty.IsFrozen).IsTrue();
        await Assert.That(customerProperty.IsFrozen).IsTrue();
        await Assert.That(foreignKeyIndex.IsFrozen).IsTrue();
        await Assert.That(relation.IsFrozen).IsTrue();
        await Assert.That(dbType.IsFrozen).IsTrue();
        await Assert.That(orderTable.ColumnIndices.IsFrozen).IsTrue();
        await Assert.That(orderTable.ColumnIndices.IsReadOnly).IsTrue();
        await Assert.That(orderModel.ValueProperties.IsFrozen).IsTrue();
        await Assert.That(orderModel.ValueProperties.IsReadOnly).IsTrue();
        await Assert.That(orderModel.RelationProperties.IsFrozen).IsTrue();
        await Assert.That(orderModel.RelationProperties.IsReadOnly).IsTrue();
        await Assert.That(foreignKeyIndex.Columns.IsFrozen).IsTrue();
        await Assert.That(foreignKeyIndex.Columns.IsReadOnly).IsTrue();
        await Assert.That(foreignKeyIndex.RelationParts.IsFrozen).IsTrue();
        await Assert.That(foreignKeyIndex.RelationParts.IsReadOnly).IsTrue();
        await Assert.That(built.CacheLimits.IsFrozen).IsTrue();
        await Assert.That(built.CacheLimits.IsReadOnly).IsTrue();
        await Assert.That(built.CacheCleanup.IsFrozen).IsTrue();
        await Assert.That(built.CacheCleanup.IsReadOnly).IsTrue();
        await Assert.That(built.IndexCache.IsFrozen).IsTrue();
        await Assert.That(built.IndexCache.IsReadOnly).IsTrue();
        await Assert.That(orderTable.CacheLimits.IsFrozen).IsTrue();
        await Assert.That(orderTable.CacheLimits.IsReadOnly).IsTrue();
        await Assert.That(orderTable.IndexCache.IsFrozen).IsTrue();
        await Assert.That(orderTable.IndexCache.IsReadOnly).IsTrue();

        await AssertArrayElementAssignmentDoesNotMutate(() => built.TableModels, orderTableModel);
        await AssertArrayElementAssignmentDoesNotMutate(() => built.Attributes, new DatabaseAttribute("ChangedDb"));
        await AssertArrayElementAssignmentDoesNotMutate(() => orderTable.Columns, amount);
        await AssertArrayElementAssignmentDoesNotMutate(() => orderTable.PrimaryKeyColumns, amount);
        await AssertArrayElementAssignmentDoesNotMutate(() => orderModel.OriginalInterfaces, new CsTypeDeclaration("IOther", "TestNamespace", ModelCsType.Interface));
        await AssertArrayElementAssignmentDoesNotMutate(() => orderModel.Usings, new ModelUsing("Other.Namespace"));
        await AssertArrayElementAssignmentDoesNotMutate(() => orderModel.Attributes, new TableAttribute("changed"));
        await AssertArrayElementAssignmentDoesNotMutate(() => orderId.DbTypes, new DatabaseColumnType(DatabaseType.MySQL, "bigint"));
        await AssertArrayElementAssignmentDoesNotMutate(() => orderIdProperty.Attributes, new ColumnAttribute("changed"));

        await AssertFrozenMutation(() => SetDatabaseName(built, "Changed"));
        await AssertFrozenMutation(() => SetDatabaseDbName(built, "changed"));
        await AssertFrozenMutation(() => SetDatabaseCsType(built, changedClass));
        await AssertFrozenMutation(() => SetDatabaseCsFile(built, changedFile));
        await AssertFrozenMutation(() => SetDatabaseCache(built, true));
        await AssertFrozenMutation(() => SetDatabaseAttributes(built, [databaseAttribute]));
        await AssertFrozenMutation(() => SetDatabaseSourceSpan(built, sourceSpan));
        await AssertFrozenMutation(() => SetDatabaseAttributeSourceSpan(built, databaseAttribute, attributeSourceSpan));
        await AssertFrozenMutation(() => SetDatabaseTableModels(built, [orderTableModel]));
        await AssertFrozenMutation(() => SetTableModelPropertyName(orderTableModel, "Changed"));
        await AssertFrozenMutation(() => SetTableDbName(orderTable, "changed"));
        await AssertFrozenMutation(() => SetTableColumns(orderTable, [orderId, amount]));
        await AssertFrozenMutation(() => SetTableUseCache(orderTable, true));
        await AssertFrozenMutation(() => AddTablePrimaryKeyColumn(orderTable, amount));
        await AssertFrozenMutation(() => RemoveTablePrimaryKeyColumn(orderTable, orderId));
        await AssertFrozenMutation(() => SetModelCsType(orderModel, new CsTypeDeclaration("Changed", "TestNamespace", ModelCsType.Class)));
        await AssertFrozenMutation(() => SetModelCsFile(orderModel, changedFile));
        await AssertFrozenMutation(() => SetModelImmutableType(orderModel, changedRecord));
        await AssertFrozenMutation(() => SetModelImmutableFactory(orderModel, new Func<object>(() => new object())));
        await AssertFrozenMutation(() => SetModelMutableType(orderModel, changedClass));
        await AssertFrozenMutation(() => SetModelInstanceInterface(orderModel, changedInterface));
        await AssertFrozenMutation(() => SetModelInterfaces(orderModel, [changedInterface]));
        await AssertFrozenMutation(() => SetModelUsings(orderModel, [new ModelUsing("Other.Namespace")]));
        await AssertFrozenMutation(() => SetModelAttributes(orderModel, [modelAttribute]));
        await AssertFrozenMutation(() => AddModelAttribute(orderModel, modelAttribute));
        await AssertFrozenMutation(() => SetModelSourceSpan(orderModel, sourceSpan));
        await AssertFrozenMutation(() => SetModelAttributeSourceSpan(orderModel, modelAttribute, attributeSourceSpan));
        await AssertFrozenMutation(() => AddModelProperties(orderModel, [orderIdProperty]));
        await AssertFrozenMutation(() => AddModelProperty(orderModel, orderIdProperty));
        await AssertFrozenMutation(() => SetColumnDbName(orderId, "changed"));
        await AssertFrozenMutation(() => SetColumnIndex(orderId, 42));
        await AssertFrozenMutation(() => SetColumnForeignKey(orderId, true));
        await AssertFrozenMutation(() => SetColumnAutoIncrement(orderId, true));
        await AssertFrozenMutation(() => SetColumnNullable(orderId, true));
        await AssertFrozenMutation(() => SetColumnValueProperty(orderId, orderIdProperty));
        await AssertFrozenMutation(() => SetColumnPrimaryKey(orderId));
        await AssertFrozenMutation(() => AddColumnDbType(orderId, new DatabaseColumnType(DatabaseType.MySQL, "bigint")));
        await AssertFrozenMutation(() => SetPropertyAttributes(orderIdProperty, [propertyAttribute]));
        await AssertFrozenMutation(() => AddPropertyAttribute(orderIdProperty, propertyAttribute));
        await AssertFrozenMutation(() => SetPropertyName(orderIdProperty, "Changed"));
        await AssertFrozenMutation(() => SetPropertyCsType(orderIdProperty, new CsTypeDeclaration(typeof(long))));
        await AssertFrozenMutation(() => SetPropertyCsNullable(orderIdProperty, true));
        await AssertFrozenMutation(() => SetPropertySourceInfo(orderIdProperty, new PropertySourceInfo(sourceSpan, attributeSourceSpan)));
        await AssertFrozenMutation(() => SetPropertyAttributeSourceSpan(orderIdProperty, propertyAttribute, attributeSourceSpan));
        await AssertFrozenMutation(() => SetValuePropertyColumn(orderIdProperty, amount));
        await AssertFrozenMutation(() => SetValuePropertyCsSize(orderIdProperty, 32));
        await AssertFrozenMutation(() => SetValuePropertyEnumProperty(orderIdProperty, new EnumProperty([("Changed", 1)])));
        await AssertFrozenMutation(() => SetRelationPropertyName(customerProperty, "Changed"));
        await AssertFrozenMutation(() => SetRelationPropertyPart(customerProperty, relation.ForeignKey));
        await AssertFrozenMutation(() => SetIndexTable(foreignKeyIndex, orderTable));
        await AssertFrozenMutation(() => AddColumnToIndex(foreignKeyIndex, amount));
        await AssertFrozenMutation(() => SetIndexRelationParts(foreignKeyIndex, new MetadataList<RelationPart>()));
        await AssertFrozenMutation(() => SetRelationForeignKey(relation, relation.ForeignKey));
        await AssertFrozenMutation(() => SetRelationCandidateKey(relation, relation.CandidateKey));
        await AssertFrozenMutation(() => SetRelationType(relation, RelationType.OneToMany));
        await AssertFrozenMutation(() => SetRelationConstraintName(relation, "Changed"));
        await AssertFrozenMutation(() => SetRelationOnUpdate(relation, ReferentialAction.Cascade));
        await AssertFrozenMutation(() => SetRelationOnDelete(relation, ReferentialAction.SetNull));
        await AssertFrozenMutation(() => SetDatabaseColumnTypeName(dbType, "bigint"));
        await AssertFrozenMutation(() => SetDatabaseColumnTypeLength(dbType, 12));
        await AssertFrozenMutation(() => SetDatabaseColumnTypeDecimals(dbType, 2U));
        await AssertFrozenMutation(() => SetDatabaseColumnTypeSigned(dbType, false));
        await AssertFrozenMutation(() => AddMetadataListItem(orderTable.ColumnIndices, NewAmountIndex("idx_amount")));
        await AssertFrozenMutation(() => AddMetadataListItems(orderTable.ColumnIndices, [NewAmountIndex("idx_amount_range")]));
        await AssertFrozenMutation(() => InsertMetadataListItem(orderTable.ColumnIndices, 0, NewAmountIndex("idx_amount_insert")));
        await AssertFrozenMutation(() => SetMetadataListItem(orderTable.ColumnIndices, 0, NewAmountIndex("idx_amount_replace")));
        await AssertFrozenMutation(() => RemoveMetadataListItem(orderTable.ColumnIndices, foreignKeyIndex));
        await AssertFrozenMutation(() => RemoveMetadataListItemAt(orderTable.ColumnIndices, 0));
        await AssertFrozenMutation(() => AddMetadataDictionaryValue(orderModel.ValueProperties, "OrderIdCopy", orderIdProperty));
        await AssertFrozenMutation(() => AddMetadataDictionaryItem(orderModel.ValueProperties, new KeyValuePair<string, ValueProperty>("OrderIdCopy", orderIdProperty)));
        await AssertFrozenMutation(() => SetMetadataDictionaryValue(orderModel.ValueProperties, "OrderId", orderIdProperty));
        await AssertFrozenMutation(() => RemoveMetadataDictionaryValue(orderModel.ValueProperties, "OrderId"));
        await AssertFrozenMutation(() => RemoveMetadataDictionaryItem(orderModel.ValueProperties, new KeyValuePair<string, ValueProperty>("OrderId", orderIdProperty)));
        await AssertFrozenMutation(() => ClearMetadataDictionary(orderModel.ValueProperties));
        await AssertFrozenMutation(() => ClearMetadataDictionary(orderModel.RelationProperties));
        await AssertFrozenMutation(() => AddMetadataListItem(foreignKeyIndex.Columns, amount));
        await AssertFrozenMutation(() => ClearMetadataList(foreignKeyIndex.RelationParts));
        await AssertFrozenMutation(() => ClearMetadataList(built.CacheLimits));
        await AssertFrozenMutation(() => ClearMetadataList(built.CacheCleanup));
        await AssertFrozenMutation(() => ClearMetadataList(built.IndexCache));
        await AssertFrozenMutation(() => ClearMetadataList(orderTable.CacheLimits));
        await AssertFrozenMutation(() => ClearMetadataList(orderTable.IndexCache));
    }

    [Test]
    public async Task Build_TypedViewDraft_ReturnsFrozenViewDefinition()
    {
        var database = CreateViewTypedDraft("select name from active_items");

        var built = new MetadataDefinitionFactory().Build(database).ValueOrException();

        var view = (ViewDefinition)built.TableModels.Single().Table;
        await Assert.That(view.IsFrozen).IsTrue();
        await AssertFrozenMutation(() => SetViewDefinition(view, "select 1"));
    }

    [Test]
    public async Task PublicMetadataDefinitionMutators_AreMarkedObsolete()
    {
        var missingMethods = new (Type Type, string[] MethodNames)[]
            {
                (typeof(DatabaseDefinition), new[] { nameof(DatabaseDefinition.SetName), nameof(DatabaseDefinition.SetDbName), nameof(DatabaseDefinition.SetCsType), nameof(DatabaseDefinition.SetCsFile), nameof(DatabaseDefinition.SetCache), nameof(DatabaseDefinition.SetAttributes), nameof(DatabaseDefinition.SetSourceSpan), nameof(DatabaseDefinition.SetAttributeSourceSpan), nameof(DatabaseDefinition.SetTableModels) }),
                (typeof(TableModel), [nameof(TableModel.SetCsPropertyName)]),
                (typeof(TableDefinition), [nameof(TableDefinition.SetDbName), nameof(TableDefinition.SetColumns), nameof(TableDefinition.AddPrimaryKeyColumn), nameof(TableDefinition.RemovePrimaryKeyColumn)]),
                (typeof(ViewDefinition), [nameof(ViewDefinition.SetDefinition)]),
                (typeof(ModelDefinition), [nameof(ModelDefinition.SetCsType), nameof(ModelDefinition.SetCsFile), nameof(ModelDefinition.SetImmutableType), nameof(ModelDefinition.SetImmutableFactory), nameof(ModelDefinition.SetMutableType), nameof(ModelDefinition.SetModelInstanceInterface), nameof(ModelDefinition.SetInterfaces), nameof(ModelDefinition.SetUsings), nameof(ModelDefinition.SetAttributes), nameof(ModelDefinition.AddAttribute), nameof(ModelDefinition.SetSourceSpan), nameof(ModelDefinition.SetAttributeSourceSpan), nameof(ModelDefinition.AddProperties), nameof(ModelDefinition.AddProperty)]),
                (typeof(ColumnDefinition), [nameof(ColumnDefinition.SetDbName), nameof(ColumnDefinition.SetIndex), nameof(ColumnDefinition.SetForeignKey), nameof(ColumnDefinition.SetAutoIncrement), nameof(ColumnDefinition.SetNullable), nameof(ColumnDefinition.SetValueProperty), nameof(ColumnDefinition.SetPrimaryKey), nameof(ColumnDefinition.AddDbType)]),
                (typeof(DatabaseColumnType), [nameof(DatabaseColumnType.SetName), nameof(DatabaseColumnType.SetLength), nameof(DatabaseColumnType.SetDecimals), nameof(DatabaseColumnType.SetSigned)]),
                (typeof(PropertyDefinition), [nameof(PropertyDefinition.SetAttributes), nameof(PropertyDefinition.AddAttribute), nameof(PropertyDefinition.SetPropertyName), nameof(PropertyDefinition.SetCsType), nameof(PropertyDefinition.SetCsNullable), nameof(PropertyDefinition.SetSourceInfo), nameof(PropertyDefinition.SetAttributeSourceSpan)]),
                (typeof(ValueProperty), [nameof(ValueProperty.SetColumn), nameof(ValueProperty.SetCsSize), nameof(ValueProperty.SetEnumProperty)]),
                (typeof(RelationProperty), [nameof(RelationProperty.SetRelationPart), nameof(RelationProperty.SetRelationName)]),
                (typeof(ColumnIndex), [nameof(ColumnIndex.AddColumn)]),
                (typeof(MetadataList<>), [nameof(MetadataList<object>.Add), nameof(MetadataList<object>.AddRange), nameof(MetadataList<object>.Clear), nameof(MetadataList<object>.Insert), nameof(MetadataList<object>.Remove), nameof(MetadataList<object>.RemoveAt)]),
                (typeof(MetadataDictionary<,>), [nameof(MetadataDictionary<string, object>.Add), nameof(MetadataDictionary<string, object>.Clear), nameof(MetadataDictionary<string, object>.Remove)])
            }
            .SelectMany(item => FindMissingObsoleteMethods(item.Type, item.MethodNames))
            .ToArray();

        var missingSetters = new (Type Type, string PropertyName)[]
            {
                (typeof(TableDefinition), nameof(TableDefinition.UseCache)),
                (typeof(ColumnIndex), nameof(ColumnIndex.Table)),
                (typeof(ColumnIndex), nameof(ColumnIndex.RelationParts)),
                (typeof(MetadataList<>), "Item"),
                (typeof(MetadataDictionary<,>), "Item"),
                (typeof(RelationDefinition), nameof(RelationDefinition.ForeignKey)),
                (typeof(RelationDefinition), nameof(RelationDefinition.CandidateKey)),
                (typeof(RelationDefinition), nameof(RelationDefinition.Type)),
                (typeof(RelationDefinition), nameof(RelationDefinition.ConstraintName)),
                (typeof(RelationDefinition), nameof(RelationDefinition.OnUpdate)),
                (typeof(RelationDefinition), nameof(RelationDefinition.OnDelete))
            }
            .Where(item => item.Type.GetProperty(item.PropertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                ?.SetMethod
                ?.GetCustomAttribute<ObsoleteAttribute>() is null)
            .Select(item => $"{item.Type.Name}.{item.PropertyName}.set")
            .ToArray();

        await Assert.That(missingMethods).IsEmpty();
        await Assert.That(missingSetters).IsEmpty();
    }

    [Test]
    public async Task MutableMetadataFactoryInputs_AreMarkedObsolete()
    {
        var missingMethods = new (Type Type, string MethodName, Type[] ParameterTypes, BindingFlags Flags)[]
            {
                (typeof(MetadataDefinitionFactory), nameof(MetadataDefinitionFactory.Build), [typeof(DatabaseDefinition)], BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
                (typeof(MetadataDefinitionFactory), nameof(MetadataDefinitionFactory.BuildProviderMetadata), [typeof(DatabaseDefinition)], BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
                (typeof(MetadataDefinitionDraft), nameof(MetadataDefinitionDraft.FromMutableMetadata), [typeof(DatabaseDefinition)], BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
            }
            .Select(item => FindMissingObsoleteMethod(item.Type, item.MethodName, item.ParameterTypes, item.Flags))
            .OfType<string>()
            .ToArray();

        await Assert.That(missingMethods).IsEmpty();
    }

    [Test]
    public async Task Build_TypedRelationDraft_MatchesMutableRelationDraft()
    {
        var factory = new MetadataDefinitionFactory();
        var mutableBuilt = BuildMutableMetadataDraft(factory, CreateRelationDraft()).ValueOrException();
        var typedBuilt = factory.Build(CreateRelationTypedDraft()).ValueOrException();

        await Assert.That(MetadataEquivalenceDigest.CreateText(typedBuilt))
            .IsEqualTo(MetadataEquivalenceDigest.CreateText(mutableBuilt));
    }

    [Test]
    public async Task Build_TypedDraftWithDuplicateColumnNames_ReturnsInvalidModelFailure()
    {
        var draft = new MetadataDatabaseDraft(
            "TestDb",
            new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Items",
                    new MetadataModelDraft(new CsTypeDeclaration("Item", "TestNamespace", ModelCsType.Class))
                    {
                        OriginalInterfaces =
                        [
                            new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface)
                        ],
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Id",
                                new CsTypeDeclaration(typeof(int)),
                                new MetadataColumnDraft("id") { PrimaryKey = true })
                            {
                                Attributes = [new PrimaryKeyAttribute(), new ColumnAttribute("id")]
                            },
                            new MetadataValuePropertyDraft(
                                "OtherId",
                                new CsTypeDeclaration(typeof(int)),
                                new MetadataColumnDraft("id"))
                            {
                                Attributes = [new ColumnAttribute("id")]
                            }
                        ]
                    },
                    new MetadataTableDraft("items"))
            ]
        };

        var result = new MetadataDefinitionFactory().Build(draft);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Duplicate column definition for 'id'");
    }

    [Test]
    public async Task Build_RelationDraft_FinalizesSnapshotWithoutMutatingDraft()
    {
        var database = CreateRelationDraft();
        var orderDraft = database.TableModels.Single(tm => tm.Table.DbName == "orders");

        var built = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database)
            .ValueOrException();

        var builtOrderTable = built.TableModels.Single(tm => tm.Table.DbName == "orders").Table;

        await Assert.That(ReferenceEquals(database, built)).IsFalse();
        await Assert.That(builtOrderTable.Columns.Select(c => c.Index).ToArray()).IsEquivalentTo([0, 1, 2]);
        await Assert.That(builtOrderTable.ColumnIndices.Any(x => x.Characteristic == IndexCharacteristic.ForeignKey)).IsTrue();
        await Assert.That(builtOrderTable.Model.RelationProperties).IsNotEmpty();

        await Assert.That(orderDraft.Table.Columns.Select(c => c.Index).ToArray()).IsEquivalentTo([0, 0, 0]);
        await Assert.That(orderDraft.Table.ColumnIndices.Count).IsEqualTo(0);
        await Assert.That(orderDraft.Model.RelationProperties.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Build_TableModelRegisteredOnWrongDatabase_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var ownerDatabase = new DatabaseDefinition(
            "OwnerDb",
            new CsTypeDeclaration("OwnerDb", "TestNamespace", ModelCsType.Class));
        var tableModel = CreateTableModel(ownerDatabase, "Items", "Item", "items");
        AddValueProperties(
            tableModel.Model,
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));

        var targetDatabase = new DatabaseDefinition(
            "TargetDb",
            new CsTypeDeclaration("TargetDb", "TestNamespace", ModelCsType.Class));
        SetDatabaseTableModels(targetDatabase, [tableModel]);

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), targetDatabase);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Table model 'Items' is registered on database 'TargetDb'");
        await Assert.That(failure.Message).Contains("belongs to database 'OwnerDb'");
    }

    [Test]
    public async Task Build_TableModelWithNullOwningDatabase_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = new DatabaseDefinition(
            "TestDb",
            new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class));
        var model = new ModelDefinition(new CsTypeDeclaration("Item", "TestNamespace", ModelCsType.Class));
        SetModelInterfaces(model, [new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface)]);
        var table = MetadataFactory.ParseTable(model).ValueOrException();
        SetTableDbName(table, "items");
        var tableModel = new TableModel("Items", null!, model, table);
        AddValueProperties(
            model,
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        SetDatabaseTableModels(database, [tableModel]);

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Table model 'Items'");
        await Assert.That(failure.Message).Contains("has no owning database");
    }

    [Test]
    public async Task Build_PrimaryKeyColumnNotRegisteredOnTable_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var table = database.TableModels.Single().Table;
        var ghostPrimaryKey = new ColumnDefinition("ghost_pk", table);
        SetColumnPrimaryKey(ghostPrimaryKey);

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("primary-key column 'ghost_pk'");
        await Assert.That(failure.Message).Contains("not registered on the table");
    }

    [Test]
    public async Task Build_PrimaryKeyFlagMissingTableRegistration_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var table = database.TableModels.Single().Table;
        var idColumn = table.Columns.Single(column => column.DbName == "id");
        RemoveTablePrimaryKeyColumn(table, idColumn);

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Column 'items.id' is marked as a primary key");
        await Assert.That(failure.Message).Contains("not registered in the table primary-key columns");
    }

    [Test]
    public async Task Build_DuplicateTableModelPropertyNames_ReturnsInvalidModelFailure()
    {
        var database = new MetadataDatabaseDraft(
            "TestDb",
            new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class))
        {
            TableModels =
            [
                CreateSingleTableModelTypedDraft("Items", "Item", "items", "Id", "id"),
                CreateSingleTableModelTypedDraft("Items", "ArchiveItem", "archive_items", "ArchiveId", "archive_id")
            ]
        };

        var result = new MetadataDefinitionFactory().Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Duplicate table model property 'Items'");
        await Assert.That(failure.Message).Contains("items");
        await Assert.That(failure.Message).Contains("archive_items");
        await Assert.That(failure.Message).Contains("Item");
        await Assert.That(failure.Message).Contains("ArchiveItem");
    }

    [Test]
    public async Task Build_DuplicateTableModelReference_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var tableModel = database.TableModels.Single();
        SetDatabaseTableModels(database, [tableModel, tableModel]);

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Duplicate table model property 'Items'");
        await Assert.That(failure.Message).Contains("items");
        await Assert.That(failure.Message).Contains("Item");
    }

    [Test]
    public async Task Build_TableWithUnsupportedTableType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = new DatabaseDefinition(
            "TestDb",
            new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class));
        var model = new ModelDefinition(new CsTypeDeclaration("Item", "TestNamespace", ModelCsType.Class));
        SetModelInterfaces(model, [new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface)]);
        var table = new MutableTableDefinition("items");
        table.SetType((TableType)999);
        var tableModel = new TableModel("Items", database, model, table);
        AddValueProperties(
            model,
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        SetDatabaseTableModels(database, [tableModel]);

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Table 'items' on model 'Item'");
        await Assert.That(failure.Message).Contains("unsupported table type '999'");
    }

    [Test]
    public async Task Build_TableDefinitionMarkedAsView_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = new DatabaseDefinition(
            "TestDb",
            new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class));
        var model = new ModelDefinition(new CsTypeDeclaration("Item", "TestNamespace", ModelCsType.Class));
        SetModelInterfaces(model, [new CsTypeDeclaration("IViewModel", "DataLinq.Interfaces", ModelCsType.Interface)]);
        var table = new MutableTableDefinition("items");
        table.SetType(TableType.View);
        var tableModel = new TableModel("Items", database, model, table);
        AddValueProperties(
            model,
            ("Id", typeof(int), [new ColumnAttribute("id")]));
        SetDatabaseTableModels(database, [tableModel]);

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Table 'items' on model 'Item'");
        await Assert.That(failure.Message).Contains("marked as a view");
        await Assert.That(failure.Message).Contains("not a view definition");
    }

    [Test]
    public async Task Build_ViewDefinitionMarkedAsTable_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = new DatabaseDefinition(
            "TestDb",
            new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class));
        var model = new ModelDefinition(new CsTypeDeclaration("ActiveItem", "TestNamespace", ModelCsType.Class));
        SetModelInterfaces(model, [new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface)]);
        var view = new MutableViewDefinition("active_items");
        SetViewDefinition(view, "select 1");
        view.SetType(TableType.Table);
        var tableModel = new TableModel("ActiveItems", database, model, view);
        AddValueProperties(
            model,
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        SetDatabaseTableModels(database, [tableModel]);

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("View 'active_items' on model 'ActiveItem'");
        await Assert.That(failure.Message).Contains("marked as a table");
        await Assert.That(failure.Message).Contains("must use table type 'View'");
    }

    [Test]
    public async Task Build_DatabaseWithNullAttribute_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            databaseAttributes: [null!]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Database 'TestDb'");
        await Assert.That(failure.Message).Contains("contains a null attribute");
    }

    [Test]
    public async Task Build_ModelWithNullAttribute_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            modelAttributes: [null!]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Model 'Item'");
        await Assert.That(failure.Message).Contains("contains a null attribute");
    }

    [Test]
    public async Task Build_ValuePropertyWithNullAttribute_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            valuePropertyAttributes: [null!]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Value property 'Item.Id'");
        await Assert.That(failure.Message).Contains("contains a null attribute");
    }

    [Test]
    public async Task Build_ValuePropertiesWithNullEntry_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        SetMetadataDictionaryValue(database.TableModels.Single().Model.ValueProperties, "Ghost", null!);

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Model 'Item'");
        await Assert.That(failure.Message).Contains("null value property for key 'Ghost'");
    }

    [Test]
    public async Task Build_TypedDraftWithNullTableModel_ReturnsInvalidModelFailureBeforeLowering()
    {
        var database = new MetadataDatabaseDraft(
            "TestDb",
            new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class))
        {
            TableModels = [null!]
        };

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Typed metadata draft for database 'TestDb'");
        await Assert.That(failure.Message).Contains("contains a null table model draft");
    }

    [Test]
    public async Task Build_TypedDraftWithNullValueProperty_ReturnsInvalidModelFailureBeforeLowering()
    {
        var database = CreateSingleTableTypedDraft(
            valueProperties: [null!]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Typed table model draft 'Items'");
        await Assert.That(failure.Message).Contains("contains a null value property draft");
    }

    [Test]
    public async Task Build_TypedDraftWithValuePropertyWithoutColumn_ReturnsInvalidModelFailureBeforeLowering()
    {
        var database = CreateSingleTableTypedDraft(
            valueProperties:
            [
                new MetadataValuePropertyDraft(
                    "Id",
                    new CsTypeDeclaration(typeof(int)),
                    null!)
            ]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Typed value property draft 'Items.Id'");
        await Assert.That(failure.Message).Contains("has no column draft");
    }

    [Test]
    public async Task Build_TypedDraftWithNullRelationProperty_ReturnsInvalidModelFailureBeforeLowering()
    {
        var database = CreateSingleTableTypedDraft(
            relationProperties: [null!]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Typed table model draft 'Items'");
        await Assert.That(failure.Message).Contains("contains a null relation property draft");
    }

    [Test]
    public async Task Build_TypedDraftWithNullAttributeSourceSpanAttribute_ReturnsInvalidModelFailureBeforeLowering()
    {
        var database = new MetadataDatabaseDraft(
            "TestDb",
            new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class))
        {
            AttributeSourceSpans = [(null!, new SourceTextSpan(1, 1))]
        };

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Typed database draft 'TestDb'");
        await Assert.That(failure.Message).Contains("null attribute source-span attribute");
    }

    [Test]
    public async Task Build_ValuePropertyWithUnsupportedPropertyType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var property = database.TableModels.Single().Model.ValueProperties["Id"];
        SetPropertyType(property, (PropertyType)999);

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Value property 'Item.Id'");
        await Assert.That(failure.Message).Contains("unsupported property type '999'");
    }

    [Test]
    public async Task Build_RelationPropertyWithNullAttribute_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateRelationTypedDraft(
            orderRelationProperties:
            [
                new MetadataRelationPropertyDraft(
                    "Customer",
                    new CsTypeDeclaration("User", "TestNamespace", ModelCsType.Class))
                {
                    Attributes = [null!]
                }
            ]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Relation property 'Order.Customer'");
        await Assert.That(failure.Message).Contains("contains a null attribute");
    }

    [Test]
    public async Task Build_RelationPropertiesWithNullEntry_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateRelationDraft();
        var orderModel = database.TableModels.Single(tm => tm.Table.DbName == "orders").Model;
        SetMetadataDictionaryValue(orderModel.RelationProperties, "Customer", null!);

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Model 'Order'");
        await Assert.That(failure.Message).Contains("null relation property for key 'Customer'");
    }

    [Test]
    public async Task Build_RelationPropertyWithWrongPropertyType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateRelationDraft();
        var userModel = database.TableModels.Single(tm => tm.Table.DbName == "users").Model;
        var orderModel = database.TableModels.Single(tm => tm.Table.DbName == "orders").Model;
        var relationProperty = new RelationProperty(
            "Customer",
            userModel.CsType,
            orderModel,
            [new RelationAttribute("users", "user_id", "FK_Order_User")]);
        SetPropertyType(relationProperty, PropertyType.Value);
        AddModelProperty(orderModel, relationProperty);

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Relation property 'Order.Customer'");
        await Assert.That(failure.Message).Contains("stored as a relation property");
        await Assert.That(failure.Message).Contains("marked as 'Value'");
    }

    [Test]
    public async Task Build_DatabaseWithEmptyName_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            databaseName: " ",
            databaseDbName: "TestDb");

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Database 'TestDb'");
        await Assert.That(failure.Message).Contains("empty database name");
    }

    [Test]
    public async Task Build_DatabaseWithEmptyPhysicalName_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            databaseDbName: "");

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Database 'TestDb'");
        await Assert.That(failure.Message).Contains("empty physical database name");
    }

    [Test]
    public async Task Build_TableWithEmptyName_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            tableDbName: " ");

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Model 'Item'");
        await Assert.That(failure.Message).Contains("empty table or view name");
    }

    [Test]
    public async Task Build_ColumnWithEmptyName_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            valueColumnName: "");

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Value property 'Item.Id'");
        await Assert.That(failure.Message).Contains("empty column name");
    }

    [Test]
    public async Task Build_ClassIndexWithUnsupportedType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            modelAttributes: [new IndexAttribute("IX_items_id", IndexCharacteristic.Simple, (IndexType)999, "id")]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Class-level index attribute on model 'Item'");
        await Assert.That(failure.Message).Contains("unsupported index type '999'");
    }

    [Test]
    public async Task Build_PropertyIndexWithEmptyColumnName_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            valuePropertyAttributes: [new IndexAttribute("IX_items_id", IndexCharacteristic.Simple, "")]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Index attribute on value property 'Item.Id'");
        await Assert.That(failure.Message).Contains("empty index column name");
    }

    [Test]
    public async Task Build_ForeignKeyWithUnsupportedReferentialAction_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateRelationTypedDraft(
            orderCustomerIdAttributes:
            [
                new ForeignKeyAttribute(
                    "users",
                    "user_id",
                    "FK_Order_User_Invalid",
                    (ReferentialAction)999,
                    ReferentialAction.Cascade),
                new ColumnAttribute("customer_id")
            ]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Foreign key attribute on value property 'Order.CustomerId'");
        await Assert.That(failure.Message).Contains("unsupported on-update action '999'");
    }

    [Test]
    public async Task Build_RelationAttributeWithEmptyReferencedColumn_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateRelationTypedDraft(
            orderRelationProperties:
            [
                new MetadataRelationPropertyDraft(
                    "BrokenCustomer",
                    new CsTypeDeclaration("User", "TestNamespace", ModelCsType.Class))
                {
                    Attributes = [new RelationAttribute("users", "", "FK_Order_User")]
                }
            ]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Relation attribute on relation property 'Order.BrokenCustomer'");
        await Assert.That(failure.Message).Contains("empty referenced column name");
    }

    [Test]
    public async Task Build_DatabaseCacheLimitWithUnsupportedType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            databaseCacheLimits: [((CacheLimitType)999, 1)]);

        var result = new MetadataDefinitionFactory().Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Database 'TestDb'");
        await Assert.That(failure.Message).Contains("unsupported cache limit type '999'");
    }

    [Test]
    public async Task Build_DatabaseCacheCleanupWithZeroAmount_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            databaseCacheCleanup: [(CacheCleanupType.Minutes, 0)]);

        var result = new MetadataDefinitionFactory().Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Database 'TestDb'");
        await Assert.That(failure.Message).Contains("Cache cleanup amounts must be greater than zero");
    }

    [Test]
    public async Task Build_TableCacheLimitWithNegativeAmount_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            tableCacheLimits: [(CacheLimitType.Rows, -1)]);

        var result = new MetadataDefinitionFactory().Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Table 'items'");
        await Assert.That(failure.Message).Contains("Cache limit amounts must be greater than zero");
    }

    [Test]
    public async Task Build_TableIndexCacheMaxRowsWithoutAmount_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            tableIndexCache: [(IndexCacheType.MaxAmountRows, null)]);

        var result = new MetadataDefinitionFactory().Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Table 'items'");
        await Assert.That(failure.Message).Contains("MaxAmountRows index-cache metadata without a row amount");
    }

    [Test]
    public async Task Build_DatabaseCacheLimitWithDuplicateType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            databaseCacheLimits:
            [
                (CacheLimitType.Megabytes, 128),
                (CacheLimitType.Megabytes, 256)
            ]);

        var result = new MetadataDefinitionFactory().Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Database 'TestDb'");
        await Assert.That(failure.Message).Contains("multiple cache limit entries for 'Megabytes'");
    }

    [Test]
    public async Task Build_DatabaseCacheCleanupWithDuplicateType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            databaseCacheCleanup:
            [
                (CacheCleanupType.Minutes, 5),
                (CacheCleanupType.Minutes, 10)
            ]);

        var result = new MetadataDefinitionFactory().Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Database 'TestDb'");
        await Assert.That(failure.Message).Contains("multiple cache cleanup entries for 'Minutes'");
    }

    [Test]
    public async Task Build_DatabaseIndexCacheWithConflictingPolicies_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            databaseIndexCache:
            [
                (IndexCacheType.All, null),
                (IndexCacheType.MaxAmountRows, 100)
            ]);

        var result = new MetadataDefinitionFactory().Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Database 'TestDb'");
        await Assert.That(failure.Message).Contains("conflicting index-cache policies");
    }

    [Test]
    public async Task Build_TableIndexCacheAllWithAmount_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            tableIndexCache: [(IndexCacheType.All, 100)]);

        var result = new MetadataDefinitionFactory().Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Table 'items'");
        await Assert.That(failure.Message).Contains("only MaxAmountRows can specify an amount");
    }

    [Test]
    public async Task Build_CheckAttributeWithUnsupportedDatabaseType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            modelAttributes: [new CheckAttribute((DatabaseType)999, "CK_items_id", "id > 0")]);

        var result = new MetadataDefinitionFactory().Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Check attribute on model 'Item'");
        await Assert.That(failure.Message).Contains("unsupported database type '999'");
    }

    [Test]
    public async Task Build_CommentAttributeWithUnsupportedDatabaseType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            valuePropertyAttributes: [new CommentAttribute((DatabaseType)999, "id comment")]);

        var result = new MetadataDefinitionFactory().Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Comment attribute on value property 'Item.Id'");
        await Assert.That(failure.Message).Contains("unsupported database type '999'");
    }

    [Test]
    public async Task Build_CommentAttributeWithUnknownDatabaseType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            valuePropertyAttributes: [new CommentAttribute(DatabaseType.Unknown, "id comment")]);

        var result = new MetadataDefinitionFactory().Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Comment attribute on value property 'Item.Id'");
        await Assert.That(failure.Message).Contains("unsupported database type 'Unknown'");
    }

    [Test]
    public async Task Build_DefaultSqlAttributeWithUnsupportedDatabaseType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            valuePropertyAttributes: [new DefaultSqlAttribute((DatabaseType)999, "0")]);

        var result = new MetadataDefinitionFactory().Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Default SQL attribute on value property 'Item.Id'");
        await Assert.That(failure.Message).Contains("unsupported database type '999'");
    }

    [Test]
    public async Task Build_CheckAttributeWithEmptyName_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            modelAttributes: [new CheckAttribute(DatabaseType.MariaDB, "", "id > 0")]);

        var result = new MetadataDefinitionFactory().Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Check attribute on model 'Item'");
        await Assert.That(failure.Message).Contains("empty check constraint name");
    }

    [Test]
    public async Task Build_CheckAttributeWithEmptyExpression_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            modelAttributes: [new CheckAttribute(DatabaseType.MariaDB, "CK_items_id", " ")]);

        var result = new MetadataDefinitionFactory().Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Check attribute on model 'Item'");
        await Assert.That(failure.Message).Contains("empty check expression");
    }

    [Test]
    public async Task Build_DuplicateProviderCheckAttributes_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            modelAttributes:
            [
                new CheckAttribute(DatabaseType.MariaDB, "CK_items_id", "id > 0"),
                new CheckAttribute(DatabaseType.MariaDB, "CK_items_id", "id >= 0")
            ]);

        var result = new MetadataDefinitionFactory().Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Model 'Item'");
        await Assert.That(failure.Message).Contains("duplicate check constraint 'CK_items_id'");
        await Assert.That(failure.Message).Contains("database type 'MariaDB'");
    }

    [Test]
    public async Task Build_CommentAttributeWithNullText_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            valuePropertyAttributes: [new CommentAttribute(DatabaseType.MySQL, null!)]);

        var result = new MetadataDefinitionFactory().Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Comment attribute on value property 'Item.Id'");
        await Assert.That(failure.Message).Contains("null comment text");
    }

    [Test]
    public async Task Build_DatabaseTypeWithInvalidCSharpName_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            databaseCsType: new CsTypeDeclaration("Bad Db", "TestNamespace", ModelCsType.Class));

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Database 'TestDb'");
        await Assert.That(failure.Message).Contains("C# type name 'Bad Db'");
    }

    [Test]
    public async Task Build_DatabaseTypeWithUnsupportedCSharpTypeKind_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            databaseCsType: new CsTypeDeclaration("TestDb", "TestNamespace", (ModelCsType)999));

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Database 'TestDb'");
        await Assert.That(failure.Message).Contains("unsupported C# type kind '999'");
    }

    [Test]
    public async Task Build_DatabaseTypeWithNonClassKind_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            databaseCsType: new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Interface));

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Database 'TestDb'");
        await Assert.That(failure.Message).Contains("database types must be classes");
    }

    [Test]
    public async Task Build_TableModelPropertyWithInvalidCSharpName_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            tableModelPropertyName: "Bad Name");

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Table 'items'");
        await Assert.That(failure.Message).Contains("C# database property name 'Bad Name'");
    }

    [Test]
    public async Task Build_ModelTypeWithInvalidCSharpName_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            modelCsType: new CsTypeDeclaration("Bad Model", "TestNamespace", ModelCsType.Class));

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Model 'Bad Model'");
        await Assert.That(failure.Message).Contains("C# type name 'Bad Model'");
    }

    [Test]
    public async Task Build_ModelTypeWithInvalidDeclarationKind_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            modelCsType: new CsTypeDeclaration("Item", "TestNamespace", ModelCsType.Enum));

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Model 'Item'");
        await Assert.That(failure.Message).Contains("model types must be classes, records, or interfaces");
    }

    [Test]
    public async Task Build_ModelInstanceInterfaceWithUnsupportedCSharpTypeKind_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            modelInstanceInterface: new CsTypeDeclaration("IItem", "TestNamespace", (ModelCsType)999));

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Model 'Item' model-instance interface");
        await Assert.That(failure.Message).Contains("unsupported C# type kind '999'");
    }

    [Test]
    public async Task Build_ModelInstanceInterfaceWithNonInterfaceKind_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            modelInstanceInterface: new CsTypeDeclaration("IItem", "TestNamespace", ModelCsType.Class));

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Model 'Item' model-instance interface");
        await Assert.That(failure.Message).Contains("must be an interface");
    }

    [Test]
    public async Task Build_ImmutableTypeWithNonConcreteKind_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            immutableType: new CsTypeDeclaration("IImmutableItem", "TestNamespace", ModelCsType.Interface));

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Model 'Item' immutable type");
        await Assert.That(failure.Message).Contains("must be a class or record");
    }

    [Test]
    public async Task Build_ModelUsingWithInvalidNamespace_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            modelUsings: [new ModelUsing("Bad Namespace")]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Model 'Item'");
        await Assert.That(failure.Message).Contains("using namespace 'Bad Namespace'");
    }

    [Test]
    public async Task Build_ModelUsingWithNullEntry_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            modelUsings: [null!]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Model 'Item'");
        await Assert.That(failure.Message).Contains("contains a null using namespace");
    }

    [Test]
    public async Task Build_ModelUsingWithEmptyNamespace_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            modelUsings: [new ModelUsing("")]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Model 'Item'");
        await Assert.That(failure.Message).Contains("empty using namespace");
    }

    [Test]
    public async Task Build_ModelDeclaredInterfaceWithInvalidCSharpType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            originalInterfaces:
            [
                new CsTypeDeclaration("ITableModel<", "DataLinq.Interfaces", ModelCsType.Interface)
            ]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Model 'Item' declared interface");
        await Assert.That(failure.Message).Contains("C# type name 'ITableModel<'");
    }

    [Test]
    public async Task Build_ModelDeclaredInterfaceWithInvalidNamespace_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            originalInterfaces:
            [
                new CsTypeDeclaration("ITableModel<TestDb>", "Bad Namespace", ModelCsType.Interface)
            ]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Model 'Item' declared interface");
        await Assert.That(failure.Message).Contains("uses C# namespace 'Bad Namespace'");
    }

    [Test]
    public async Task Build_ModelDeclaredInterfaceWithNonInterfaceKind_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            originalInterfaces:
            [
                new CsTypeDeclaration("ITableModel<TestDb>", "DataLinq.Interfaces", ModelCsType.Class)
            ]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Model 'Item' declared interface");
        await Assert.That(failure.Message).Contains("must be an interface");
    }

    [Test]
    public async Task Build_ValuePropertyWithInvalidCSharpName_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            valuePropertyName: "Bad Name");

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Value property 'Item.Bad Name'");
        await Assert.That(failure.Message).Contains("C# property name 'Bad Name'");
    }

    [Test]
    public async Task Build_ValuePropertyWithInvalidCSharpType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            valuePropertyCsType: new CsTypeDeclaration("Bad Type", "TestNamespace", ModelCsType.Class));

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Value property 'Item.Id'");
        await Assert.That(failure.Message).Contains("C# type name 'Bad Type'");
    }

    [Test]
    public async Task Build_ValuePropertyWithUnsupportedCSharpTypeKind_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            valuePropertyCsType: new CsTypeDeclaration("int", "", (ModelCsType)999));

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Value property 'Item.Id'");
        await Assert.That(failure.Message).Contains("unsupported C# type kind '999'");
    }

    [Test]
    public async Task Build_RelationPropertyWithInvalidCSharpType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateRelationTypedDraft(
            orderRelationProperties:
            [
                new MetadataRelationPropertyDraft(
                    "Customer",
                    new CsTypeDeclaration("Bad Type", "TestNamespace", ModelCsType.Class))
                {
                    Attributes = [new RelationAttribute("users", "user_id", "FK_Order_User")]
                }
            ]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Relation property 'Order.Customer'");
        await Assert.That(failure.Message).Contains("C# type name 'Bad Type'");
    }

    [Test]
    public async Task Build_GeneratedRelationPropertyWithInvalidCSharpName_ReturnsInvalidModelFailure()
    {
        var database = CreateRelationTypedDraft(
            foreignKeyColumnName: "id");

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Relation property 'Order.'");
        await Assert.That(failure.Message).Contains("C# property name ''");
    }

    [Test]
    public async Task Build_TableModelPropertyMatchingDatabaseType_RenamesBuiltDatabaseTypeWithoutMutatingDraft()
    {
        var database = CreateSingleTableTypedDraft(
            tableModelPropertyName: "TestDb");

        var built = new MetadataDefinitionFactory()
            .Build(database)
            .ValueOrException();

        await Assert.That(database.CsType.Name).IsEqualTo("TestDb");
        await Assert.That(database.TableModels.Single().CsPropertyName).IsEqualTo("TestDb");
        await Assert.That(built.CsType.Name).IsEqualTo("TestDbDb");
        await Assert.That(built.TableModels.Single().CsPropertyName).IsEqualTo("TestDb");
    }

    [Test]
    public async Task Build_RelationPropertyRegisteredUnderWrongKey_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateRelationDraft();
        var orderModel = database.TableModels.Single(tm => tm.Table.DbName == "orders").Model;
        var userModel = database.TableModels.Single(tm => tm.Table.DbName == "users").Model;
        var relationProperty = new RelationProperty(
            "Customer",
            userModel.CsType,
            orderModel,
            [new RelationAttribute("users", "user_id", "FK_Order_User")]);
        AddModelProperty(orderModel, relationProperty);
        SetPropertyName(relationProperty, "Buyer");

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Relation property 'Order.Buyer'");
        await Assert.That(failure.Message).Contains("registered under key 'Customer'");
    }

    [Test]
    public async Task Build_RelationPropertyRegisteredOnWrongModel_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateRelationDraft();
        var orderModel = database.TableModels.Single(tm => tm.Table.DbName == "orders").Model;
        var userModel = database.TableModels.Single(tm => tm.Table.DbName == "users").Model;
        AddModelProperty(orderModel, new RelationProperty(
            "Customer",
            userModel.CsType,
            userModel,
            [new RelationAttribute("users", "user_id", "FK_Order_User")]));

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Relation property 'User.Customer'");
        await Assert.That(failure.Message).Contains("registered on model 'Order'");
        await Assert.That(failure.Message).Contains("belongs to model 'User'");
    }

    [Test]
    public async Task Build_ValueAndRelationPropertiesWithSameName_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateRelationTypedDraft(
            orderRelationProperties:
            [
                new MetadataRelationPropertyDraft(
                    "OrderId",
                    new CsTypeDeclaration("User", "TestNamespace", ModelCsType.Class))
                {
                    Attributes = [new RelationAttribute("users", "user_id", "FK_Order_User")]
                }
            ]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("contains both a value property and a relation property named 'OrderId'");
        await Assert.That(failure.Message).Contains("Order");
    }

    [Test]
    public async Task Build_RelationPropertyPointingAtOtherTableRelationPart_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateRelationDraft();
        MetadataFactory.ParseIndices(database).ValueOrException();
        MetadataFactory.ParseRelations(database).ValueOrException();

        var orderModel = database.TableModels.Single(tm => tm.Table.DbName == "orders").Model;
        var userModel = database.TableModels.Single(tm => tm.Table.DbName == "users").Model;
        var wrongSidePart = userModel.RelationProperties["Order"].RelationPart;
        SetRelationPropertyPart(orderModel.RelationProperties["Customer"], wrongSidePart);

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Relation property 'Order.Customer'");
        await Assert.That(failure.Message).Contains("relation part on table 'users'");
        await Assert.That(failure.Message).Contains("own table 'orders'");
    }

    [Test]
    public async Task Build_MultipleDefaultAttributes_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            valueProperties:
            [
                CreateTypedIdProperty(),
                CreateTypedValueProperty(
                    "Name",
                    typeof(string),
                    "name",
                    attributes: [new ColumnAttribute("name"), new DefaultAttribute("first"), new DefaultAttribute("second")])
            ]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Value property 'Item.Name'");
        await Assert.That(failure.Message).Contains("multiple default attributes");
    }

    [Test]
    public async Task Build_DefaultNewUuidOnNonGuidProperty_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            valueProperties:
            [
                CreateTypedIdProperty(),
                CreateTypedValueProperty(
                    "Token",
                    typeof(string),
                    "token",
                    attributes: [new ColumnAttribute("token"), new DefaultNewUUIDAttribute()])
            ]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("DefaultNewUUIDAttribute can only be used with Guid properties");
        await Assert.That(failure.Message).Contains("Item.Token");
        await Assert.That(failure.Message).Contains("string");
    }

    [Test]
    public async Task Build_DefaultCurrentTimestampOnNonTemporalProperty_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            valueProperties:
            [
                CreateTypedIdProperty(),
                CreateTypedValueProperty(
                    "IsReady",
                    typeof(bool),
                    "is_ready",
                    attributes: [new ColumnAttribute("is_ready"), new DefaultCurrentTimestampAttribute()])
            ]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("DefaultCurrentTimestampAttribute can only be used");
        await Assert.That(failure.Message).Contains("Item.IsReady");
        await Assert.That(failure.Message).Contains("bool");
    }

    [Test]
    public async Task Build_DefaultValueIncompatibleWithPropertyType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            valueProperties:
            [
                CreateTypedIdProperty(),
                CreateTypedValueProperty(
                    "Count",
                    typeof(int),
                    "count",
                    attributes: [new ColumnAttribute("count"), new DefaultAttribute("not-a-number")])
            ]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Default value for value property 'Item.Count'");
        await Assert.That(failure.Message).Contains("not compatible with C# type 'int'");
    }

    [Test]
    public async Task Build_DefaultNewUuidWithUnsupportedVersion_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            valueProperties:
            [
                CreateTypedIdProperty(),
                CreateTypedValueProperty(
                    "PublicId",
                    typeof(Guid),
                    "public_id",
                    attributes: [new ColumnAttribute("public_id"), new DefaultNewUUIDAttribute((UUIDVersion)999)])
            ]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("DefaultNewUUIDAttribute on value property 'Item.PublicId'");
        await Assert.That(failure.Message).Contains("unsupported UUID version");
    }

    [Test]
    public async Task Build_ViewWithoutDefinition_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateViewTypedDraft(definition: null);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("View 'active_items'");
        await Assert.That(failure.Message).Contains("ActiveItem");
        await Assert.That(failure.Message).Contains("missing a SQL definition");
    }

    [Test]
    public async Task Build_ViewWithDefinition_SucceedsWithoutPrimaryKey()
    {
        const string definition = "select name from active_items";
        var database = CreateViewTypedDraft(definition);

        var built = new MetadataDefinitionFactory()
            .Build(database)
            .ValueOrException();

        var view = (ViewDefinition)built.TableModels.Single().Table;
        await Assert.That(view.Definition).IsEqualTo(definition);
        await Assert.That(view.PrimaryKeyColumns).IsEmpty();
        await Assert.That(view.Columns.Select(x => x.Index).ToArray()).IsEquivalentTo([0]);
    }

    [Test]
    public async Task Build_ViewWithExplicitEmptyDefinition_SucceedsForProviderPlaceholder()
    {
        var database = CreateViewTypedDraft("");

        var built = new MetadataDefinitionFactory()
            .Build(database)
            .ValueOrException();

        var view = (ViewDefinition)built.TableModels.Single().Table;
        await Assert.That(view.Definition).IsEqualTo("");
        await Assert.That(view.Columns.Select(x => x.Index).ToArray()).IsEquivalentTo([0]);
    }

    [Test]
    public async Task Build_ColumnWithNullDatabaseType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            valueColumnDbTypes: [null!]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Column 'items.id'");
        await Assert.That(failure.Message).Contains("null database type");
    }

    [Test]
    public async Task Build_ColumnWithEmptyDatabaseTypeName_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            valueColumnDbTypes: [new DatabaseColumnType(DatabaseType.MySQL, "")]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Column 'items.id'");
        await Assert.That(failure.Message).Contains("empty database type name");
        await Assert.That(failure.Message).Contains("MySQL");
    }

    [Test]
    public async Task Build_ColumnWithUnsupportedDatabaseType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            valueColumnDbTypes: [new DatabaseColumnType((DatabaseType)999, "int")]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Column 'items.id'");
        await Assert.That(failure.Message).Contains("unsupported database type");
        await Assert.That(failure.Message).Contains("999");
    }

    [Test]
    public async Task Build_ColumnWithUnknownDatabaseType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            valueColumnDbTypes: [new DatabaseColumnType(DatabaseType.Unknown, "int")]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Column 'items.id'");
        await Assert.That(failure.Message).Contains("unsupported database type");
        await Assert.That(failure.Message).Contains("Unknown");
    }

    [Test]
    public async Task Build_ColumnWithDatabaseTypeDecimalsWithoutLength_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            valueColumnDbTypes: [new DatabaseColumnType(DatabaseType.MySQL, "decimal", null, 2)]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Column 'items.id'");
        await Assert.That(failure.Message).Contains("decimals");
        await Assert.That(failure.Message).Contains("no length");
    }

    [Test]
    public async Task Build_ColumnWithDuplicateDatabaseType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            valueColumnDbTypes:
            [
                new DatabaseColumnType(DatabaseType.MySQL, "int"),
                new DatabaseColumnType(DatabaseType.MySQL, "integer")
            ]);

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Column 'items.id'");
        await Assert.That(failure.Message).Contains("multiple database types for 'MySQL'");
    }

    [Test]
    public async Task Build_EnumClrTypeWithoutEnumMetadata_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            valuePropertyCsType: new CsTypeDeclaration(typeof(RuntimeStatus)));

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Item.Id");
        await Assert.That(failure.Message).Contains("no enum metadata");
    }

    [Test]
    public async Task Build_EnumPropertyWithoutValues_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            valuePropertyCsType: new CsTypeDeclaration("StatusValue", "TestNamespace", ModelCsType.Enum),
            valuePropertyEnumProperty: new EnumProperty());

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Enum value property 'Item.Id'");
        await Assert.That(failure.Message).Contains("at least one enum value");
    }

    [Test]
    public async Task Build_EnumPropertyWithInvalidMemberName_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            valuePropertyCsType: new CsTypeDeclaration("StatusValue", "TestNamespace", ModelCsType.Enum),
            valuePropertyEnumProperty: new EnumProperty(csEnumValues: [("Not Valid", 1)]));

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Enum value property 'Item.Id'");
        await Assert.That(failure.Message).Contains("invalid C# enum member name 'Not Valid'");
    }

    [Test]
    public async Task Build_EnumPropertyWithDuplicateDatabaseValues_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableTypedDraft(
            valuePropertyCsType: new CsTypeDeclaration("StatusValue", "TestNamespace", ModelCsType.Enum),
            valuePropertyEnumProperty: new EnumProperty(
                enumValues: [("PRI", 1), ("pri", 2)],
                csEnumValues: [("Primary", 1), ("AlternatePrimary", 2)]));

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Enum value property 'Item.Id'");
        await Assert.That(failure.Message).Contains("duplicate database enum value 'PRI'");
    }

    [Test]
    public async Task Build_EnumPropertyWithEmptyDatabaseValueAndValidCsName_Succeeds()
    {
        var database = CreateSingleTableTypedDraft(
            valuePropertyCsType: new CsTypeDeclaration("StatusValue", "TestNamespace", ModelCsType.Enum),
            valuePropertyEnumProperty: new EnumProperty(
                enumValues: [("", 1), ("PRI", 2)],
                csEnumValues: [("Empty", 1), ("PRI", 2)]));

        var built = new MetadataDefinitionFactory()
            .Build(database)
            .ValueOrException();

        var enumProperty = built.TableModels.Single().Model.ValueProperties["Id"].EnumProperty!.Value;
        await Assert.That(enumProperty.DbEnumValues.Select(x => x.name).ToArray()).IsEquivalentTo(["", "PRI"]);
        await Assert.That(enumProperty.CsEnumValues.Select(x => x.name).ToArray()).IsEquivalentTo(["Empty", "PRI"]);
    }

    [Test]
    public async Task Build_ExternalEnumPropertyWithEmptyDatabaseValueWithoutCsNames_Succeeds()
    {
        var database = CreateSingleTableTypedDraft(
            valuePropertyCsType: new CsTypeDeclaration("COLUMN_KEY", "TestNamespace", ModelCsType.Enum),
            valuePropertyEnumProperty: new EnumProperty(
                enumValues: [("", 1), ("PRI", 2)],
                declaredInClass: false));

        var built = new MetadataDefinitionFactory()
            .Build(database)
            .ValueOrException();

        var enumProperty = built.TableModels.Single().Model.ValueProperties["Id"].EnumProperty!.Value;
        await Assert.That(enumProperty.DbEnumValues.Select(x => x.name).ToArray()).IsEquivalentTo(["", "PRI"]);
        await Assert.That(enumProperty.CsEnumValues).IsEmpty();
    }

    [Test]
    public async Task Build_ColumnWithoutValueProperty_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var table = database.TableModels.Single().Table;
        var orphanColumn = new ColumnDefinition("ghost", table);
        SetTableColumns(table, table.Columns.Concat([orphanColumn]));

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Column 'items.ghost'");
        await Assert.That(failure.Message).Contains("has no value property");
    }

    [Test]
    public async Task Build_ValuePropertyReferencingUnregisteredColumn_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var tableModel = database.TableModels.Single();
        var ghostProperty = new ValueProperty(
            "Ghost",
            new CsTypeDeclaration(typeof(string)),
            tableModel.Model,
            [new ColumnAttribute("ghost")]);
        var ghostColumn = new ColumnDefinition("ghost", tableModel.Table);
        SetColumnValueProperty(ghostColumn, ghostProperty);
        AddModelProperty(tableModel.Model, ghostProperty);

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Value property 'Item.Ghost'");
        await Assert.That(failure.Message).Contains("not registered on the table");
    }

    [Test]
    public async Task Build_IndexAttachedToWrongTable_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateRelationDraft();
        var users = database.TableModels.Single(tm => tm.Table.DbName == "users").Table;
        var orders = database.TableModels.Single(tm => tm.Table.DbName == "orders").Table;
        var userId = users.Columns.Single(column => column.DbName == "user_id");
        AddMetadataListItem(orders.ColumnIndices, new ColumnIndex("idx_wrong_table", IndexCharacteristic.Simple, IndexType.BTREE, [userId]));

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Index 'idx_wrong_table' is attached to table 'orders'");
        await Assert.That(failure.Message).Contains("belongs to table 'users'");
    }

    [Test]
    public async Task Build_IndexReferencingUnregisteredColumn_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var table = database.TableModels.Single().Table;
        var unregisteredColumn = new ColumnDefinition("ghost", table);
        AddMetadataListItem(table.ColumnIndices, new ColumnIndex("idx_ghost", IndexCharacteristic.Simple, IndexType.BTREE, [unregisteredColumn]));

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Index 'idx_ghost' on table 'items'");
        await Assert.That(failure.Message).Contains("not registered on the table");
    }

    [Test]
    public async Task Build_ExistingIndexWithUnsupportedCharacteristic_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var table = database.TableModels.Single().Table;
        var idColumn = table.Columns.Single();
        AddMetadataListItem(table.ColumnIndices, new ColumnIndex("idx_bad_characteristic", (IndexCharacteristic)999, IndexType.BTREE, [idColumn]));

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Index 'idx_bad_characteristic'");
        await Assert.That(failure.Message).Contains("unsupported index characteristic '999'");
    }

    [Test]
    public async Task Build_ExistingIndexWithUnsupportedType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var table = database.TableModels.Single().Table;
        var idColumn = table.Columns.Single();
        AddMetadataListItem(table.ColumnIndices, new ColumnIndex("idx_bad_type", IndexCharacteristic.Simple, (IndexType)999, [idColumn]));

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Index 'idx_bad_type'");
        await Assert.That(failure.Message).Contains("unsupported index type '999'");
    }

    [Test]
    public async Task Build_ExistingIndexWithNullRelationParts_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var table = database.TableModels.Single().Table;
        var idColumn = table.Columns.Single();
        var index = new ColumnIndex("idx_null_parts", IndexCharacteristic.Simple, IndexType.BTREE, [idColumn]);
        SetIndexRelationParts(index, null!);
        AddMetadataListItem(table.ColumnIndices, index);

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Index 'idx_null_parts'");
        await Assert.That(failure.Message).Contains("null relation-part collection");
    }

    [Test]
    public async Task Build_TableWithDuplicateExistingIndexNames_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var table = database.TableModels.Single().Table;
        var idColumn = table.Columns.Single();
        AddMetadataListItem(table.ColumnIndices, new ColumnIndex("idx_duplicate", IndexCharacteristic.Simple, IndexType.BTREE, [idColumn]));
        AddMetadataListItem(table.ColumnIndices, new ColumnIndex("idx_duplicate", IndexCharacteristic.Unique, IndexType.BTREE, [idColumn]));

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Table 'items'");
        await Assert.That(failure.Message).Contains("duplicate column index name 'idx_duplicate'");
    }

    [Test]
    public async Task Build_ExistingRelationMissingCandidateKey_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateRelationDraft();
        var users = database.TableModels.Single(tm => tm.Table.DbName == "users");
        var orders = database.TableModels.Single(tm => tm.Table.DbName == "orders");
        var customerId = orders.Table.Columns.Single(column => column.DbName == "customer_id");
        var foreignKeyIndex = new ColumnIndex("FK_Broken", IndexCharacteristic.ForeignKey, IndexType.BTREE, [customerId]);
        AddMetadataListItem(orders.Table.ColumnIndices, foreignKeyIndex);
        var relation = new RelationDefinition("FK_Broken", RelationType.OneToMany);
        var foreignKeyPart = new RelationPart(foreignKeyIndex, relation, RelationPartType.ForeignKey, "Customer");
        SetRelationForeignKey(relation, foreignKeyPart);
        AddMetadataListItem(foreignKeyIndex.RelationParts, foreignKeyPart);
        var relationProperty = new RelationProperty(
            "Customer",
            users.Model.CsType,
            orders.Model,
            [new RelationAttribute("users", "user_id", "FK_Broken")]);
        SetRelationPropertyPart(relationProperty, foreignKeyPart);
        AddModelProperty(orders.Model, relationProperty);

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Existing relation 'FK_Broken'");
        await Assert.That(failure.Message).Contains("missing a candidate-key part");
    }

    [Test]
    public async Task Build_ExistingRelationPartMissingIndexBackReference_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateRelationDraft();
        var users = database.TableModels.Single(tm => tm.Table.DbName == "users");
        var orders = database.TableModels.Single(tm => tm.Table.DbName == "orders");
        var userId = users.Table.Columns.Single(column => column.DbName == "user_id");
        var customerId = orders.Table.Columns.Single(column => column.DbName == "customer_id");
        var candidateKeyIndex = new ColumnIndex("users_primary_key", IndexCharacteristic.PrimaryKey, IndexType.BTREE, [userId]);
        var foreignKeyIndex = new ColumnIndex("FK_Broken", IndexCharacteristic.ForeignKey, IndexType.BTREE, [customerId]);
        AddMetadataListItem(users.Table.ColumnIndices, candidateKeyIndex);
        AddMetadataListItem(orders.Table.ColumnIndices, foreignKeyIndex);
        var relation = new RelationDefinition("FK_Broken", RelationType.OneToMany);
        var foreignKeyPart = new RelationPart(foreignKeyIndex, relation, RelationPartType.ForeignKey, "Customer");
        var candidateKeyPart = new RelationPart(candidateKeyIndex, relation, RelationPartType.CandidateKey, "Orders");
        SetRelationForeignKey(relation, foreignKeyPart);
        SetRelationCandidateKey(relation, candidateKeyPart);
        AddMetadataListItem(candidateKeyIndex.RelationParts, candidateKeyPart);
        var relationProperty = new RelationProperty(
            "Customer",
            users.Model.CsType,
            orders.Model,
            [new RelationAttribute("users", "user_id", "FK_Broken")]);
        SetRelationPropertyPart(relationProperty, foreignKeyPart);
        AddModelProperty(orders.Model, relationProperty);

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Existing relation 'FK_Broken'");
        await Assert.That(failure.Message).Contains("foreign-key part is not registered");
    }

    [Test]
    public async Task Build_ExistingRelationWithUnsupportedReferentialAction_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateRelationDraft();
        var users = database.TableModels.Single(tm => tm.Table.DbName == "users");
        var orders = database.TableModels.Single(tm => tm.Table.DbName == "orders");
        var userId = users.Table.Columns.Single(column => column.DbName == "user_id");
        var customerId = orders.Table.Columns.Single(column => column.DbName == "customer_id");
        var candidateKeyIndex = new ColumnIndex("users_primary_key", IndexCharacteristic.PrimaryKey, IndexType.BTREE, [userId]);
        var foreignKeyIndex = new ColumnIndex("FK_Broken", IndexCharacteristic.ForeignKey, IndexType.BTREE, [customerId]);
        AddMetadataListItem(users.Table.ColumnIndices, candidateKeyIndex);
        AddMetadataListItem(orders.Table.ColumnIndices, foreignKeyIndex);
        var relation = new RelationDefinition("FK_Broken", RelationType.OneToMany);
        SetRelationOnDelete(relation, (ReferentialAction)999);
        var foreignKeyPart = new RelationPart(foreignKeyIndex, relation, RelationPartType.ForeignKey, "Customer");
        var candidateKeyPart = new RelationPart(candidateKeyIndex, relation, RelationPartType.CandidateKey, "Orders");
        SetRelationForeignKey(relation, foreignKeyPart);
        SetRelationCandidateKey(relation, candidateKeyPart);
        AddMetadataListItem(foreignKeyIndex.RelationParts, foreignKeyPart);
        AddMetadataListItem(candidateKeyIndex.RelationParts, candidateKeyPart);
        var relationProperty = new RelationProperty(
            "Customer",
            users.Model.CsType,
            orders.Model,
            [new RelationAttribute("users", "user_id", "FK_Broken")]);
        SetRelationPropertyPart(relationProperty, foreignKeyPart);
        AddModelProperty(orders.Model, relationProperty);

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Existing relation 'FK_Broken'");
        await Assert.That(failure.Message).Contains("unsupported on-delete action '999'");
    }

    [Test]
    public async Task Build_DuplicateColumnDraft_ReturnsInvalidModelFailure()
    {
        var database = CreateSingleTableTypedDraft(
            valueProperties:
            [
                CreateTypedValueProperty(
                    "FirstId",
                    typeof(int),
                    "id",
                    primaryKey: true,
                    attributes: [new PrimaryKeyAttribute(), new ColumnAttribute("id")]),
                CreateTypedValueProperty(
                    "SecondId",
                    typeof(int),
                    "id",
                    attributes: [new ColumnAttribute("id")])
            ]);

        var result = new MetadataDefinitionFactory().Build(database);

        await Assert.That(result.HasValue).IsFalse();
        var failureMessage = result.Failure.ToString()!;
        await Assert.That(failureMessage).Contains("Duplicate column definition for 'id'");
        await Assert.That(failureMessage).Contains("FirstId");
        await Assert.That(failureMessage).Contains("SecondId");
    }

    [Test]
    public async Task Build_DuplicateColumnReference_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var table = database.TableModels.Single().Table;
        var idColumn = table.Columns.Single();
        SetTableColumns(table, [idColumn, idColumn]);

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Duplicate column definition for 'id'");
        await Assert.That(failure.Message).Contains("Id");
    }

    [Test]
    public async Task Build_TableWithoutPrimaryKey_ReturnsInvalidModelFailure()
    {
        var database = CreateSingleTableTypedDraft(
            valueProperties:
            [
                CreateTypedValueProperty(
                    "Name",
                    typeof(string),
                    "name",
                    attributes: [new ColumnAttribute("name")])
            ]);

        var result = new MetadataDefinitionFactory().Build(database);

        await Assert.That(result.HasValue).IsFalse();
        var failureMessage = result.Failure.ToString()!;
        await Assert.That(failureMessage).Contains("missing a primary key");
        await Assert.That(failureMessage).Contains("items");
    }

    [Test]
    public async Task Build_DuplicateRelationPropertiesForSameForeignKey_ReturnsInvalidModelFailure()
    {
        var database = CreateRelationTypedDraft(
            orderRelationProperties:
            [
                new MetadataRelationPropertyDraft(
                    "Customer",
                    new CsTypeDeclaration("User", "TestNamespace", ModelCsType.Class))
                {
                    Attributes = [new RelationAttribute("users", "user_id", "FK_Order_User")]
                },
                new MetadataRelationPropertyDraft(
                    "Buyer",
                    new CsTypeDeclaration("User", "TestNamespace", ModelCsType.Class))
                {
                    Attributes = [new RelationAttribute("users", "user_id", "FK_Order_User")]
                }
            ]);

        var result = new MetadataDefinitionFactory().Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        var failureMessage = failure.ToString()!;
        await Assert.That(failureMessage).Contains("Multiple relation properties");
        await Assert.That(failureMessage).Contains("FK_Order_User");
        await Assert.That(failureMessage).Contains("Customer");
        await Assert.That(failureMessage).Contains("Buyer");
    }

    [Test]
    public async Task Build_RelationPropertyWithEmptyStoredRelationName_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateRelationDraft();
        var userModel = database.TableModels.Single(tm => tm.Table.DbName == "users").Model;
        var orderModel = database.TableModels.Single(tm => tm.Table.DbName == "orders").Model;
        var relationProperty = new RelationProperty(
            "Customer",
            userModel.CsType,
            orderModel,
            [new RelationAttribute("users", "user_id", "FK_Order_User")]);
        SetRelationPropertyName(relationProperty, "");
        AddModelProperty(orderModel, relationProperty);

        var result = BuildMutableMetadataDraft(new MetadataDefinitionFactory(), database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Relation property 'Order.Customer'");
        await Assert.That(failure.Message).Contains("empty relation name");
    }

    [Test]
    public async Task Build_ForeignKeyWithEmptyConstraintName_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateRelationTypedDraft(foreignKeyName: "");

        var result = new MetadataDefinitionFactory()
            .Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Foreign key attribute on value property 'Order.CustomerId'");
        await Assert.That(failure.Message).Contains("empty constraint name");
    }

    [Test]
    public async Task BuildProviderMetadata_ProviderStyleDraft_AssignsInterfacesOrdinalsAndPrimaryKeyIndex()
    {
        var database = CreateProviderStyleTypedDraft();

        var built = new MetadataDefinitionFactory().BuildProviderMetadata(database).ValueOrException();

        var table = built.TableModels.Single().Table;

        await Assert.That(table.Model.ModelInstanceInterface.HasValue).IsTrue();
        await Assert.That(table.Model.ModelInstanceInterface!.Value.Name).IsEqualTo("IItem");
        await Assert.That(table.Columns.Select(c => c.Index).ToArray()).IsEquivalentTo([0, 1]);
        await Assert.That(table.ColumnIndices.Any(x => x.Characteristic == IndexCharacteristic.PrimaryKey)).IsTrue();
    }

    [Test]
    public async Task BuildProviderMetadata_ProviderStyleDraft_FinalizesSnapshotWithoutMutatingDraft()
    {
        var database = CreateProviderStyleDraft();
        var draftTable = database.TableModels.Single().Table;
        var draftModel = database.TableModels.Single().Model;

        var built = BuildProviderMutableMetadataDraft(new MetadataDefinitionFactory(), database)
            .ValueOrException();

        var builtTable = built.TableModels.Single().Table;

        await Assert.That(ReferenceEquals(database, built)).IsFalse();
        await Assert.That(builtTable.Model.ModelInstanceInterface.HasValue).IsTrue();
        await Assert.That(builtTable.Columns.Select(c => c.Index).ToArray()).IsEquivalentTo([0, 1]);
        await Assert.That(builtTable.ColumnIndices.Any(x => x.Characteristic == IndexCharacteristic.PrimaryKey)).IsTrue();

        await Assert.That(draftModel.ModelInstanceInterface.HasValue).IsFalse();
        await Assert.That(draftTable.Columns.Select(c => c.Index).ToArray()).IsEquivalentTo([0, 0]);
        await Assert.That(draftTable.ColumnIndices.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Build_StubTableDraft_PreservesStubFlagAndSkipsFinalization()
    {
        var database = new MetadataDatabaseDraft(
            "TestDb",
            new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "MissingModels",
                    new MetadataModelDraft(new CsTypeDeclaration("MissingModel", "TestNamespace", ModelCsType.Interface))
                    {
                        OriginalInterfaces =
                        [
                            new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface)
                        ]
                    },
                    new MetadataTableDraft("missing_models"))
                {
                    IsStub = true
                }
            ]
        };

        var built = new MetadataDefinitionFactory()
            .Build(database)
            .ValueOrException();

        await Assert.That(built.TableModels.Single().IsStub).IsTrue();
    }

    private static DatabaseDefinition CreateRelationDraft(string foreignKeyName = "FK_Order_User")
    {
        var database = new DatabaseDefinition(
            "TestDb",
            new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class));

        var userModel = CreateTableModel(database, "Users", "User", "users").Model;
        AddValueProperties(
            userModel,
            ("UserId", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("user_id")]),
            ("UserName", typeof(string), [new ColumnAttribute("user_name")]));

        var orderModel = CreateTableModel(database, "Orders", "Order", "orders").Model;
        AddValueProperties(
            orderModel,
            ("OrderId", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("order_id")]),
            ("CustomerId", typeof(int), [new ForeignKeyAttribute("users", "user_id", foreignKeyName), new ColumnAttribute("customer_id")]),
            ("Amount", typeof(decimal), [new ColumnAttribute("amount")]));

        SetDatabaseTableModels(database, [
            userModel.TableModel,
            orderModel.TableModel
        ]);

        return database;
    }

    private static MetadataDatabaseDraft CreateRelationTypedDraft(
        string foreignKeyName = "FK_Order_User",
        string foreignKeyColumnName = "customer_id",
        IReadOnlyList<Attribute>? orderCustomerIdAttributes = null,
        IReadOnlyList<MetadataRelationPropertyDraft>? orderRelationProperties = null,
        bool includeFreezeCoverageMetadata = false)
    {
        var orderIdColumn = new MetadataColumnDraft("order_id")
        {
            PrimaryKey = true,
            DbTypes = includeFreezeCoverageMetadata
                ? [new DatabaseColumnType(DatabaseType.MySQL, "int")]
                : []
        };

        return new MetadataDatabaseDraft(
            "TestDb",
            new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class))
        {
            Attributes = includeFreezeCoverageMetadata
                ? [new DatabaseAttribute("TestDb")]
                : [],
            CacheLimits = includeFreezeCoverageMetadata
                ? [(CacheLimitType.Megabytes, 128)]
                : [],
            CacheCleanup = includeFreezeCoverageMetadata
                ? [(CacheCleanupType.Minutes, 2)]
                : [],
            IndexCache = includeFreezeCoverageMetadata
                ? [(IndexCacheType.All, null)]
                : [],
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Users",
                    new MetadataModelDraft(new CsTypeDeclaration("User", "TestNamespace", ModelCsType.Class))
                    {
                        OriginalInterfaces =
                        [
                            new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface)
                        ],
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "UserId",
                                new CsTypeDeclaration(typeof(int)),
                                new MetadataColumnDraft("user_id") { PrimaryKey = true })
                            {
                                Attributes = [new PrimaryKeyAttribute(), new ColumnAttribute("user_id")]
                            },
                            new MetadataValuePropertyDraft(
                                "UserName",
                                new CsTypeDeclaration(typeof(string)),
                                new MetadataColumnDraft("user_name"))
                            {
                                Attributes = [new ColumnAttribute("user_name")]
                            }
                        ]
                    },
                    new MetadataTableDraft("users")),
                new MetadataTableModelDraft(
                    "Orders",
                    new MetadataModelDraft(new CsTypeDeclaration("Order", "TestNamespace", ModelCsType.Class))
                    {
                        OriginalInterfaces =
                        [
                            new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface)
                        ],
                        Usings = includeFreezeCoverageMetadata
                            ? [new ModelUsing("System")]
                            : [],
                        Attributes = includeFreezeCoverageMetadata
                            ? [new TableAttribute("orders")]
                            : [],
                        RelationProperties = orderRelationProperties ?? [],
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "OrderId",
                                new CsTypeDeclaration(typeof(int)),
                                orderIdColumn)
                            {
                                Attributes = [new PrimaryKeyAttribute(), new ColumnAttribute("order_id")]
                            },
                            new MetadataValuePropertyDraft(
                                "CustomerId",
                                new CsTypeDeclaration(typeof(int)),
                                new MetadataColumnDraft(foreignKeyColumnName) { ForeignKey = true })
                            {
                                Attributes = orderCustomerIdAttributes ??
                                [
                                    new ForeignKeyAttribute("users", "user_id", foreignKeyName),
                                    new ColumnAttribute("customer_id")
                                ]
                            },
                            new MetadataValuePropertyDraft(
                                "Amount",
                                new CsTypeDeclaration(typeof(decimal)),
                                new MetadataColumnDraft("amount"))
                            {
                                Attributes = [new ColumnAttribute("amount")]
                            }
                        ]
                    },
                    new MetadataTableDraft("orders")
                    {
                        CacheLimits = includeFreezeCoverageMetadata
                            ? [(CacheLimitType.Rows, 10)]
                            : [],
                        IndexCache = includeFreezeCoverageMetadata
                            ? [(IndexCacheType.None, null)]
                            : []
                    })
            ]
        };
    }

    private static MetadataDatabaseDraft CreateProviderStyleTypedDraft()
    {
        return new MetadataDatabaseDraft(
            "TestDb",
            new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Items",
                    new MetadataModelDraft(new CsTypeDeclaration("Item", "TestNamespace", ModelCsType.Class))
                    {
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "ItemId",
                                new CsTypeDeclaration(typeof(int)),
                                new MetadataColumnDraft("item_id") { PrimaryKey = true })
                            {
                                Attributes = [new PrimaryKeyAttribute(), new ColumnAttribute("item_id")]
                            },
                            new MetadataValuePropertyDraft(
                                "Name",
                                new CsTypeDeclaration(typeof(string)),
                                new MetadataColumnDraft("name"))
                            {
                                Attributes = [new ColumnAttribute("name")]
                            }
                        ]
                    },
                    new MetadataTableDraft("items"))
            ]
        };
    }

    private static DatabaseDefinition CreateProviderStyleDraft()
    {
        var database = new DatabaseDefinition(
            "TestDb",
            new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class));
        var table = new TableDefinition("items");
        var tableModel = new TableModel("Items", database, table, "Item");

        AddValueProperties(
            tableModel.Model,
            ("ItemId", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("item_id")]),
            ("Name", typeof(string), [new ColumnAttribute("name")]));

        SetDatabaseTableModels(database, [tableModel]);
        return database;
    }

    private static DatabaseDefinition CreateSingleTableDraft(params (string PropertyName, Type CsType, Attribute[] Attributes)[] properties)
    {
        var database = new DatabaseDefinition(
            "TestDb",
            new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class));
        var model = CreateTableModel(database, "Items", "Item", "items").Model;

        AddValueProperties(model, properties);

        SetDatabaseTableModels(database, [model.TableModel]);
        return database;
    }

    private static MetadataDatabaseDraft CreateSingleTableTypedDraft(
        string databaseName = "TestDb",
        string? databaseDbName = null,
        CsTypeDeclaration? databaseCsType = null,
        IReadOnlyList<Attribute>? databaseAttributes = null,
        string tableModelPropertyName = "Items",
        string tableDbName = "items",
        CsTypeDeclaration? modelCsType = null,
        CsTypeDeclaration? modelInstanceInterface = null,
        CsTypeDeclaration? immutableType = null,
        IReadOnlyList<CsTypeDeclaration>? originalInterfaces = null,
        IReadOnlyList<ModelUsing>? modelUsings = null,
        IReadOnlyList<Attribute>? modelAttributes = null,
        string valuePropertyName = "Id",
        CsTypeDeclaration? valuePropertyCsType = null,
        string valueColumnName = "id",
        IReadOnlyList<Attribute>? valuePropertyAttributes = null,
        IReadOnlyList<DatabaseColumnType>? valueColumnDbTypes = null,
        EnumProperty? valuePropertyEnumProperty = null,
        IReadOnlyList<MetadataValuePropertyDraft>? valueProperties = null,
        IReadOnlyList<MetadataRelationPropertyDraft>? relationProperties = null,
        IReadOnlyList<(CacheLimitType limitType, long amount)>? databaseCacheLimits = null,
        IReadOnlyList<(CacheCleanupType cleanupType, long amount)>? databaseCacheCleanup = null,
        IReadOnlyList<(IndexCacheType indexCacheType, int? amount)>? databaseIndexCache = null,
        IReadOnlyList<(CacheLimitType limitType, long amount)>? tableCacheLimits = null,
        IReadOnlyList<(IndexCacheType indexCacheType, int? amount)>? tableIndexCache = null)
    {
        return new MetadataDatabaseDraft(
            databaseName,
            databaseCsType ?? new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class))
        {
            DbName = databaseDbName,
            Attributes = databaseAttributes ?? [],
            CacheLimits = databaseCacheLimits ?? [],
            CacheCleanup = databaseCacheCleanup ?? [],
            IndexCache = databaseIndexCache ?? [],
            TableModels =
            [
                new MetadataTableModelDraft(
                    tableModelPropertyName,
                    new MetadataModelDraft(modelCsType ?? new CsTypeDeclaration("Item", "TestNamespace", ModelCsType.Class))
                    {
                        ImmutableType = immutableType,
                        ModelInstanceInterface = modelInstanceInterface,
                        OriginalInterfaces = originalInterfaces ??
                            [
                                new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface)
                            ],
                        Usings = modelUsings ?? [],
                        Attributes = modelAttributes ?? [],
                        RelationProperties = relationProperties ?? [],
                        ValueProperties = valueProperties ??
                        [
                            CreateTypedValueProperty(
                                valuePropertyName,
                                valuePropertyCsType ?? new CsTypeDeclaration(typeof(int)),
                                valueColumnName,
                                primaryKey: true,
                                attributes:
                                [
                                    new PrimaryKeyAttribute(),
                                    new ColumnAttribute("id"),
                                    .. (valuePropertyAttributes ?? [])
                                ],
                                dbTypes: valueColumnDbTypes,
                                enumProperty: valuePropertyEnumProperty)
                        ]
                    },
                    new MetadataTableDraft(tableDbName)
                    {
                        CacheLimits = tableCacheLimits ?? [],
                        IndexCache = tableIndexCache ?? []
                    })
            ]
        };
    }

    private static MetadataDatabaseDraft CreateViewTypedDraft(string? definition)
    {
        return new MetadataDatabaseDraft(
            "TestDb",
            new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "ActiveItems",
                    new MetadataModelDraft(new CsTypeDeclaration("ActiveItem", "TestNamespace", ModelCsType.Class))
                    {
                        OriginalInterfaces =
                        [
                            new CsTypeDeclaration("IViewModel", "DataLinq.Interfaces", ModelCsType.Interface)
                        ],
                        ValueProperties =
                        [
                            CreateTypedValueProperty(
                                "Name",
                                typeof(string),
                                "name",
                                attributes: [new ColumnAttribute("name")])
                        ]
                    },
                    new MetadataTableDraft("active_items")
                    {
                        Type = TableType.View,
                        Definition = definition
                    })
            ]
        };
    }

    private static MetadataTableModelDraft CreateSingleTableModelTypedDraft(
        string tablePropertyName,
        string modelName,
        string tableName,
        string primaryKeyPropertyName,
        string primaryKeyColumnName)
    {
        return new MetadataTableModelDraft(
            tablePropertyName,
            new MetadataModelDraft(new CsTypeDeclaration(modelName, "TestNamespace", ModelCsType.Class))
            {
                OriginalInterfaces =
                [
                    new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface)
                ],
                ValueProperties =
                [
                    CreateTypedValueProperty(
                        primaryKeyPropertyName,
                        typeof(int),
                        primaryKeyColumnName,
                        primaryKey: true,
                        attributes: [new PrimaryKeyAttribute(), new ColumnAttribute(primaryKeyColumnName)])
                ]
            },
            new MetadataTableDraft(tableName));
    }

    private static MetadataValuePropertyDraft CreateTypedValueProperty(
        string propertyName,
        Type csType,
        string columnName,
        bool primaryKey = false,
        IReadOnlyList<Attribute>? attributes = null,
        IReadOnlyList<DatabaseColumnType>? dbTypes = null,
        EnumProperty? enumProperty = null)
    {
        return CreateTypedValueProperty(
            propertyName,
            new CsTypeDeclaration(csType),
            columnName,
            primaryKey,
            attributes,
            dbTypes,
            enumProperty);
    }

    private static MetadataValuePropertyDraft CreateTypedIdProperty()
    {
        return CreateTypedValueProperty(
            "Id",
            typeof(int),
            "id",
            primaryKey: true,
            attributes: [new PrimaryKeyAttribute(), new ColumnAttribute("id")]);
    }

    private static MetadataValuePropertyDraft CreateTypedValueProperty(
        string propertyName,
        CsTypeDeclaration csType,
        string columnName,
        bool primaryKey = false,
        IReadOnlyList<Attribute>? attributes = null,
        IReadOnlyList<DatabaseColumnType>? dbTypes = null,
        EnumProperty? enumProperty = null)
    {
        return new MetadataValuePropertyDraft(
            propertyName,
            csType,
            new MetadataColumnDraft(columnName)
            {
                DbTypes = dbTypes ?? [],
                PrimaryKey = primaryKey
            })
        {
            Attributes = attributes ?? [],
            EnumProperty = enumProperty
        };
    }

    private static TableModel CreateTableModel(
        DatabaseDefinition database,
        string csPropertyName,
        string modelName,
        string tableName)
    {
        var model = new ModelDefinition(new CsTypeDeclaration(modelName, "TestNamespace", ModelCsType.Class));
        SetModelInterfaces(model, [new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface)]);

        var table = MetadataFactory.ParseTable(model).ValueOrException();
        SetTableDbName(table, tableName);

        return new TableModel(csPropertyName, database, model, table);
    }

    private static void AddValueProperties(
        ModelDefinition model,
        params (string PropertyName, Type CsType, Attribute[] Attributes)[] properties)
    {
        var columns = properties
            .Select(property =>
            {
                var valueProperty = new ValueProperty(
                    property.PropertyName,
                    new CsTypeDeclaration(property.CsType),
                    model,
                    property.Attributes);
                AddModelProperty(model, valueProperty);
                return MetadataFactory.ParseColumn(model.Table, valueProperty);
            })
            .ToArray();

        SetTableColumns(model.Table, columns);
    }

    private static IEnumerable<string> FindMissingObsoleteMethods(Type type, IEnumerable<string> methodNames)
    {
        foreach (var methodName in methodNames)
        {
            var methods = type
                .GetMember(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .OfType<MethodInfo>()
                .ToArray();

            if (methods.Length == 0)
            {
                yield return $"{type.Name}.{methodName}";
                continue;
            }

            foreach (var method in methods.Where(method => method.GetCustomAttribute<ObsoleteAttribute>() is null))
                yield return $"{type.Name}.{method.Name}";
        }
    }

    private static string? FindMissingObsoleteMethod(
        Type type,
        string methodName,
        Type[] parameterTypes,
        BindingFlags flags)
    {
        var method = type.GetMethod(methodName, flags, binder: null, types: parameterTypes, modifiers: null);
        if (method?.GetCustomAttribute<ObsoleteAttribute>() is not null)
            return null;

        return $"{type.Name}.{methodName}({string.Join(", ", parameterTypes.Select(parameterType => parameterType.Name))})";
    }

    private static void SetPropertyType(PropertyDefinition property, PropertyType type)
    {
        var backingField = typeof(PropertyDefinition).GetField(
            "<Type>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        backingField!.SetValue(property, type);
    }

#pragma warning disable CS0618 // These helpers intentionally exercise the legacy mutable metadata surface.
    private static Option<DatabaseDefinition, IDLOptionFailure> BuildMutableMetadataDraft(
        MetadataDefinitionFactory factory,
        DatabaseDefinition database) =>
        factory.Build(MetadataDefinitionDraft.FromMutableMetadata(database));

    private static Option<DatabaseDefinition, IDLOptionFailure> BuildProviderMutableMetadataDraft(
        MetadataDefinitionFactory factory,
        DatabaseDefinition database) =>
        factory.BuildProviderMetadata(MetadataDefinitionDraft.FromMutableMetadata(database));

    private static void SetMetadataListItem<T>(MetadataList<T> list, int index, T item) =>
        list[index] = item;

    private static void AddMetadataListItem<T>(MetadataList<T> list, T item) =>
        list.Add(item);

    private static void AddMetadataListItems<T>(MetadataList<T> list, IEnumerable<T> items) =>
        list.AddRange(items);

    private static void InsertMetadataListItem<T>(MetadataList<T> list, int index, T item) =>
        list.Insert(index, item);

    private static void ClearMetadataList<T>(MetadataList<T> list) =>
        list.Clear();

    private static void RemoveMetadataListItem<T>(MetadataList<T> list, T item) =>
        list.Remove(item);

    private static void RemoveMetadataListItemAt<T>(MetadataList<T> list, int index) =>
        list.RemoveAt(index);

    private static void SetMetadataDictionaryValue<TKey, TValue>(MetadataDictionary<TKey, TValue> dictionary, TKey key, TValue value)
        where TKey : notnull =>
        dictionary[key] = value;

    private static void AddMetadataDictionaryValue<TKey, TValue>(MetadataDictionary<TKey, TValue> dictionary, TKey key, TValue value)
        where TKey : notnull =>
        dictionary.Add(key, value);

    private static void AddMetadataDictionaryItem<TKey, TValue>(MetadataDictionary<TKey, TValue> dictionary, KeyValuePair<TKey, TValue> item)
        where TKey : notnull =>
        dictionary.Add(item);

    private static void ClearMetadataDictionary<TKey, TValue>(MetadataDictionary<TKey, TValue> dictionary)
        where TKey : notnull =>
        dictionary.Clear();

    private static void RemoveMetadataDictionaryValue<TKey, TValue>(MetadataDictionary<TKey, TValue> dictionary, TKey key)
        where TKey : notnull =>
        dictionary.Remove(key);

    private static void RemoveMetadataDictionaryItem<TKey, TValue>(MetadataDictionary<TKey, TValue> dictionary, KeyValuePair<TKey, TValue> item)
        where TKey : notnull =>
        dictionary.Remove(item);

    private static void SetDatabaseName(DatabaseDefinition database, string name) =>
        database.SetName(name);

    private static void SetDatabaseTableModels(DatabaseDefinition database, IEnumerable<TableModel> tableModels) =>
        database.SetTableModels(tableModels);

    private static void SetDatabaseDbName(DatabaseDefinition database, string dbName) =>
        database.SetDbName(dbName);

    private static void SetDatabaseCsType(DatabaseDefinition database, CsTypeDeclaration csType) =>
        database.SetCsType(csType);

    private static void SetDatabaseCsFile(DatabaseDefinition database, CsFileDeclaration csFile) =>
        database.SetCsFile(csFile);

    private static void SetDatabaseCache(DatabaseDefinition database, bool useCache) =>
        database.SetCache(useCache);

    private static void SetDatabaseAttributes(DatabaseDefinition database, IEnumerable<Attribute> attributes) =>
        database.SetAttributes(attributes);

    private static void SetDatabaseSourceSpan(DatabaseDefinition database, SourceTextSpan sourceSpan) =>
        database.SetSourceSpan(sourceSpan);

    private static void SetDatabaseAttributeSourceSpan(DatabaseDefinition database, Attribute attribute, SourceTextSpan sourceSpan) =>
        database.SetAttributeSourceSpan(attribute, sourceSpan);

    private static void SetTableModelPropertyName(TableModel tableModel, string propertyName) =>
        tableModel.SetCsPropertyName(propertyName);

    private static void SetTableDbName(TableDefinition table, string dbName) =>
        table.SetDbName(dbName);

    private static void SetTableUseCache(TableDefinition table, bool useCache) =>
        table.UseCache = useCache;

    private static void SetViewDefinition(ViewDefinition view, string definition) =>
        view.SetDefinition(definition);

    private static void SetModelCsType(ModelDefinition model, CsTypeDeclaration csType) =>
        model.SetCsType(csType);

    private static void SetModelCsFile(ModelDefinition model, CsFileDeclaration csFile) =>
        model.SetCsFile(csFile);

    private static void SetModelImmutableType(ModelDefinition model, CsTypeDeclaration immutableType) =>
        model.SetImmutableType(immutableType);

    private static void SetModelImmutableFactory(ModelDefinition model, Delegate immutableFactory) =>
        model.SetImmutableFactory(immutableFactory);

    private static void SetModelMutableType(ModelDefinition model, CsTypeDeclaration mutableType) =>
        model.SetMutableType(mutableType);

    private static void SetModelInstanceInterface(ModelDefinition model, CsTypeDeclaration? interfaceType) =>
        model.SetModelInstanceInterface(interfaceType);

    private static void SetModelInterfaces(ModelDefinition model, IEnumerable<CsTypeDeclaration> interfaces) =>
        model.SetInterfaces(interfaces);

    private static void SetModelUsings(ModelDefinition model, IEnumerable<ModelUsing> usings) =>
        model.SetUsings(usings);

    private static void SetModelAttributes(ModelDefinition model, IEnumerable<Attribute> attributes) =>
        model.SetAttributes(attributes);

    private static void AddModelAttribute(ModelDefinition model, Attribute attribute) =>
        model.AddAttribute(attribute);

    private static void SetModelSourceSpan(ModelDefinition model, SourceTextSpan sourceSpan) =>
        model.SetSourceSpan(sourceSpan);

    private static void SetModelAttributeSourceSpan(ModelDefinition model, Attribute attribute, SourceTextSpan sourceSpan) =>
        model.SetAttributeSourceSpan(attribute, sourceSpan);

    private static void AddModelProperties(ModelDefinition model, IEnumerable<PropertyDefinition> properties) =>
        model.AddProperties(properties);

    private static void AddModelProperty(ModelDefinition model, PropertyDefinition property) =>
        model.AddProperty(property);

    private static void SetTableColumns(TableDefinition table, IEnumerable<ColumnDefinition> columns) =>
        table.SetColumns(columns);

    private static void AddTablePrimaryKeyColumn(TableDefinition table, ColumnDefinition column) =>
        table.AddPrimaryKeyColumn(column);

    private static void SetColumnPrimaryKey(ColumnDefinition column) =>
        column.SetPrimaryKey();

    private static void RemoveTablePrimaryKeyColumn(TableDefinition table, ColumnDefinition column) =>
        table.RemovePrimaryKeyColumn(column);

    private static void SetColumnDbName(ColumnDefinition column, string dbName) =>
        column.SetDbName(dbName);

    private static void SetColumnIndex(ColumnDefinition column, int index) =>
        column.SetIndex(index);

    private static void SetColumnForeignKey(ColumnDefinition column, bool foreignKey) =>
        column.SetForeignKey(foreignKey);

    private static void SetColumnAutoIncrement(ColumnDefinition column, bool autoIncrement) =>
        column.SetAutoIncrement(autoIncrement);

    private static void SetColumnNullable(ColumnDefinition column, bool nullable) =>
        column.SetNullable(nullable);

    private static void SetColumnValueProperty(ColumnDefinition column, ValueProperty property) =>
        column.SetValueProperty(property);

    private static void AddColumnDbType(ColumnDefinition column, DatabaseColumnType columnType) =>
        column.AddDbType(columnType);

    private static void SetPropertyAttributes(PropertyDefinition property, IEnumerable<Attribute> attributes) =>
        property.SetAttributes(attributes);

    private static void AddPropertyAttribute(PropertyDefinition property, Attribute attribute) =>
        property.AddAttribute(attribute);

    private static void SetPropertyName(PropertyDefinition property, string propertyName) =>
        property.SetPropertyName(propertyName);

    private static void SetPropertyCsType(PropertyDefinition property, CsTypeDeclaration csType) =>
        property.SetCsType(csType);

    private static void SetPropertyCsNullable(PropertyDefinition property, bool csNullable) =>
        property.SetCsNullable(csNullable);

    private static void SetPropertySourceInfo(PropertyDefinition property, PropertySourceInfo sourceInfo) =>
        property.SetSourceInfo(sourceInfo);

    private static void SetPropertyAttributeSourceSpan(PropertyDefinition property, Attribute attribute, SourceTextSpan sourceSpan) =>
        property.SetAttributeSourceSpan(attribute, sourceSpan);

    private static void SetValuePropertyColumn(ValueProperty property, ColumnDefinition column) =>
        property.SetColumn(column);

    private static void SetValuePropertyCsSize(ValueProperty property, int? csSize) =>
        property.SetCsSize(csSize);

    private static void SetValuePropertyEnumProperty(ValueProperty property, EnumProperty enumProperty) =>
        property.SetEnumProperty(enumProperty);

    private static void SetRelationPropertyName(RelationProperty property, string? relationName) =>
        property.SetRelationName(relationName);

    private static void SetRelationPropertyPart(RelationProperty property, RelationPart relationPart) =>
        property.SetRelationPart(relationPart);

    private static void SetIndexTable(ColumnIndex index, TableDefinition table) =>
        index.Table = table;

    private static void AddColumnToIndex(ColumnIndex index, ColumnDefinition column) =>
        index.AddColumn(column);

    private static void SetIndexRelationParts(ColumnIndex index, MetadataList<RelationPart> relationParts) =>
        index.RelationParts = relationParts;

    private static void SetRelationForeignKey(RelationDefinition relation, RelationPart foreignKey) =>
        relation.ForeignKey = foreignKey;

    private static void SetRelationCandidateKey(RelationDefinition relation, RelationPart candidateKey) =>
        relation.CandidateKey = candidateKey;

    private static void SetRelationType(RelationDefinition relation, RelationType type) =>
        relation.Type = type;

    private static void SetRelationConstraintName(RelationDefinition relation, string constraintName) =>
        relation.ConstraintName = constraintName;

    private static void SetRelationOnUpdate(RelationDefinition relation, ReferentialAction onUpdate) =>
        relation.OnUpdate = onUpdate;

    private static void SetRelationOnDelete(RelationDefinition relation, ReferentialAction onDelete) =>
        relation.OnDelete = onDelete;

    private static void SetDatabaseColumnTypeName(DatabaseColumnType columnType, string name) =>
        columnType.SetName(name);

    private static void SetDatabaseColumnTypeLength(DatabaseColumnType columnType, ulong? length) =>
        columnType.SetLength(length);

    private static void SetDatabaseColumnTypeDecimals(DatabaseColumnType columnType, uint? decimals) =>
        columnType.SetDecimals(decimals);

    private static void SetDatabaseColumnTypeSigned(DatabaseColumnType columnType, bool signed) =>
        columnType.SetSigned(signed);
#pragma warning restore CS0618

    private static async Task AssertFrozenMutation(Action action)
    {
        InvalidOperationException? exception = null;
        try
        {
            action();
        }
        catch (InvalidOperationException caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("is frozen");
    }

    private static async Task AssertArrayElementAssignmentDoesNotMutate<T>(Func<T[]> getArray, T replacement)
    {
        var returnedArray = getArray();
        var originalFirstItem = returnedArray[0];

        returnedArray[0] = replacement;

        await Assert.That(getArray()[0]).IsEqualTo(originalFirstItem);
    }

    private sealed class MutableTableDefinition(string dbName) : TableDefinition(dbName)
    {
        public void SetType(TableType type) => Type = type;
    }

    private sealed class MutableViewDefinition(string dbName) : ViewDefinition(dbName)
    {
        public void SetType(TableType type) => Type = type;
    }

    private enum RuntimeStatus
    {
        Active = 1,
    }
}
