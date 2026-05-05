using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DataLinq.Attributes;
using DataLinq.ErrorHandling;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;
using ThrowAway;

namespace DataLinq.Core.Factories;

public struct MetadataFromDatabaseFactoryOptions
{
    public bool CapitaliseNames { get; set; } = false;
    public bool DeclareEnumsInClass { get; set; } = false;
    public List<string>? Include { get; set; }
    public Action<string>? Log { get; set; }

    public MetadataFromDatabaseFactoryOptions()
    {
    }
}

public static class MetadataFactory
{
    public static void ParseInterfaces(DatabaseDefinition database)
    {
        foreach (var tableModel in database.TableModels)
        {
            var model = tableModel.Model;

            if (model.ModelInstanceInterface == null)
            {
                var interfaceName = $"I{model.CsType.Name}";
                model.SetModelInstanceInterface(new CsTypeDeclaration(interfaceName, model.CsType.Namespace, ModelCsType.Interface));
            }
        }
    }

    public static Option<TableDefinition, IDLOptionFailure> ParseTable(ModelDefinition model)
    {
        if (model == null)
            return DLOptionFailure.Fail(DLFailureType.UnexpectedNull, "Model cannot be null");

        TableDefinition table;

        if (model.OriginalInterfaces.Any(x => x.Name.StartsWith("ITableModel")/* && x.Namespace == "DataLinq.Interfaces"*/))
            table = new TableDefinition(model.CsType.Name);
        else if (model.OriginalInterfaces.Any(x => x.Name.StartsWith("IViewModel")/* && x.Namespace == "DataLinq.Interfaces"*/))
            table = new ViewDefinition(model.CsType.Name);
        else
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, $"Model '{model.CsType.Name}' does not inherit from 'ITableModel' or 'IViewModel'.");

        foreach (var attribute in model.Attributes)
        {
            if (attribute is TableAttribute tableAttribute)
                table.SetDbName(tableAttribute.Name);

            if (attribute is ViewAttribute viewAttribute)
                table.SetDbName(viewAttribute.Name);

            if (attribute is UseCacheAttribute useCache)
                table.UseCache = useCache.UseCache;

            if (attribute is CacheLimitAttribute cacheLimit)
                table.CacheLimits.Add((cacheLimit.LimitType, cacheLimit.Amount));

            if (attribute is IndexCacheAttribute indexCache)
                table.IndexCache.Add((indexCache.Type, indexCache.Amount));

            if (table is ViewDefinition view && attribute is DefinitionAttribute definitionAttribute)
                view.SetDefinition(definitionAttribute.Sql);
        }

        table.SetColumns(model.ValueProperties.Values.Select(table.ParseColumn));

        return table;
    }

    public static void IndexColumns(DatabaseDefinition database)
    {
        foreach (var table in database.TableModels.Select(x => x.Table))
            for (var i = 0; i < table.Columns.Length; i++)
                table.Columns[i].SetIndex(i);
    }

    public static Option<bool, IDLOptionFailure> ParseIndices(DatabaseDefinition database)
    {
        foreach (var tableModel in database.TableModels.Where(x => !x.IsStub))
        {
            foreach (var indexAttribute in tableModel.Model.Attributes.OfType<IndexAttribute>())
            {
                if (!indexAttribute.Columns.Any())
                    return CreateIndexFailure(
                        tableModel.Model,
                        indexAttribute,
                        $"Class-level index '{indexAttribute.Name}' on table '{tableModel.Table.DbName}' must specify its columns. IndexAttribute.Columns expects database column names.");

                if (!TryResolveIndexColumns(tableModel.Table, indexAttribute, out var columnsForIndex, out var missingColumn))
                    return CreateIndexFailure(
                        tableModel.Model,
                        indexAttribute,
                        $"Index '{indexAttribute.Name}' on table '{tableModel.Table.DbName}' references column '{missingColumn}', but that column does not exist. IndexAttribute.Columns expects database column names, not C# property names.");

                try
                {
                    tableModel.Table.ColumnIndices.Add(new ColumnIndex(indexAttribute.Name, indexAttribute.Characteristic, indexAttribute.Type, columnsForIndex));
                }
                catch (InvalidOperationException exception)
                {
                    return CreateIndexFailure(tableModel.Model, indexAttribute, exception.Message);
                }
                catch (ArgumentException exception)
                {
                    return CreateIndexFailure(tableModel.Model, indexAttribute, exception.Message);
                }
            }
        }

        var indices = database.TableModels
            .Where(x => !x.IsStub)
            .SelectMany(tableModel => tableModel.Table.Columns
                .Select(column => (column, indexAttributes: column.ValueProperty.Attributes.OfType<IndexAttribute>().ToList())))
            .Where(t => t.indexAttributes.Any());

        foreach (var (column, indexAttributes) in indices)
        {
            foreach (var indexAttribute in indexAttributes)
            {
                var existingIndex = column.Table.ColumnIndices.FirstOrDefault(x => x.Name == indexAttribute.Name);

                if (existingIndex != null)
                {
                    if (!existingIndex.Columns.Contains(column))
                        existingIndex.AddColumn(column);
                }
                else
                {
                    List<ColumnDefinition> columnsForIndex;
                    if (indexAttribute.Columns.Any())
                    {
                        if (!TryResolveIndexColumns(column.Table, indexAttribute, out columnsForIndex, out var missingColumn))
                            return CreateIndexFailure(
                                column,
                                indexAttribute,
                                $"Index '{indexAttribute.Name}' on table '{column.Table.DbName}' references column '{missingColumn}', but that column does not exist. IndexAttribute.Columns expects database column names, not C# property names.");
                    }
                    else
                    {
                        columnsForIndex = [column];
                    }

                    try
                    {
                        column.Table.ColumnIndices.Add(new ColumnIndex(indexAttribute.Name, indexAttribute.Characteristic, indexAttribute.Type, columnsForIndex));
                    }
                    catch (InvalidOperationException exception)
                    {
                        return CreateIndexFailure(column, indexAttribute, exception.Message);
                    }
                    catch (ArgumentException exception)
                    {
                        return CreateIndexFailure(column, indexAttribute, exception.Message);
                    }
                }
            }
        }

        return true;
    }

    private static bool TryResolveIndexColumns(
        TableDefinition table,
        IndexAttribute indexAttribute,
        out List<ColumnDefinition> columnsForIndex,
        out string? missingColumn)
    {
        columnsForIndex = [];
        missingColumn = null;

        foreach (var columnName in indexAttribute.Columns)
        {
            var indexColumn = table.Columns.SingleOrDefault(c => c.DbName == columnName);
            if (indexColumn == null)
            {
                missingColumn = columnName;
                return false;
            }

            columnsForIndex.Add(indexColumn);
        }

        return true;
    }

    private static IDLOptionFailure CreateIndexFailure(ModelDefinition model, IndexAttribute attribute, string message)
    {
        var attributeLocation = model.GetAttributeSourceLocation(attribute);
        if (attributeLocation.HasValue)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, attributeLocation.Value);

        var modelLocation = model.GetSourceLocation();
        if (modelLocation.HasValue)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, modelLocation.Value);

        return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, model);
    }

    private static IDLOptionFailure CreateIndexFailure(ColumnDefinition column, IndexAttribute attribute, string message)
    {
        var attributeLocation = column.ValueProperty.GetAttributeSourceLocation(attribute);
        if (attributeLocation.HasValue)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, attributeLocation.Value);

        var property = column.ValueProperty;
        if (property.SourceInfo.HasValue && property.CsFile.HasValue)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, property.SourceInfo.Value.GetPropertyLocation(property.CsFile.Value));

        return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, column);
    }

    public static Option<bool, IDLOptionFailure> ValidateUniqueTableNames(DatabaseDefinition database)
    {
        var duplicateGroup = database.TableModels
            .Where(x => !x.IsStub)
            .GroupBy(x => x.Table.DbName, StringComparer.Ordinal)
            .FirstOrDefault(x => x.Count() > 1);

        if (duplicateGroup == null)
            return true;

        var duplicates = duplicateGroup.ToArray();
        var first = duplicates[0];
        var duplicate = duplicates[1];
        var message = $"Duplicate table definition for '{duplicateGroup.Key}' in database '{database.DbName}'. Models '{first.Model.CsType.Name}' and '{duplicate.Model.CsType.Name}' both map to the same table name.";
        var sourceLocation = GetTableNameSourceLocation(duplicate.Model);

        return sourceLocation.HasValue
            ? DLOptionFailure.Fail(DLFailureType.InvalidModel, message, sourceLocation.Value)
            : DLOptionFailure.Fail(DLFailureType.InvalidModel, message, duplicate.Model);
    }

    private static SourceLocation? GetTableNameSourceLocation(ModelDefinition model)
    {
        var tableAttribute = model.Attributes
            .FirstOrDefault(x => x is TableAttribute or ViewAttribute);

        if (tableAttribute != null)
        {
            var attributeLocation = model.GetAttributeSourceLocation(tableAttribute);
            if (attributeLocation.HasValue)
                return attributeLocation;
        }

        return model.GetSourceLocation();
    }

    public static Option<bool, IDLOptionFailure> ValidateUniqueColumnNames(DatabaseDefinition database)
    {
        foreach (var tableModel in database.TableModels.Where(x => !x.IsStub))
        {
            var duplicateGroup = tableModel.Table.Columns
                .GroupBy(x => x.DbName, StringComparer.Ordinal)
                .FirstOrDefault(x => x.Count() > 1);

            if (duplicateGroup == null)
                continue;

            var duplicates = duplicateGroup.ToArray();
            var first = duplicates[0];
            var duplicate = duplicates[1];
            var message = $"Duplicate column definition for '{duplicateGroup.Key}' in table '{tableModel.Table.DbName}'. Properties '{first.ValueProperty.PropertyName}' and '{duplicate.ValueProperty.PropertyName}' both map to the same column name.";
            var sourceLocation = GetColumnNameSourceLocation(duplicate.ValueProperty);

            return sourceLocation.HasValue
                ? DLOptionFailure.Fail(DLFailureType.InvalidModel, message, sourceLocation.Value)
                : DLOptionFailure.Fail(DLFailureType.InvalidModel, message, duplicate);
        }

        return true;
    }

    private static SourceLocation? GetColumnNameSourceLocation(ValueProperty property)
    {
        var columnAttribute = property.Attributes
            .FirstOrDefault(x => x is ColumnAttribute);

        if (columnAttribute != null)
        {
            var attributeLocation = property.GetAttributeSourceLocation(columnAttribute);
            if (attributeLocation.HasValue)
                return attributeLocation;
        }

        if (property.SourceInfo.HasValue && property.CsFile.HasValue)
            return property.SourceInfo.Value.GetPropertyLocation(property.CsFile.Value);

        return null;
    }

    public static Option<bool, IDLOptionFailure> ParseRelations(DatabaseDefinition database)
    {
        foreach (var table in database.TableModels.Where(x => !x.IsStub && x.Table.Type == TableType.Table).Select(x => x.Table))
        {
            var columns = table.Columns.Where(x => x.PrimaryKey).ToList();
            if (!columns.Any())
                return CreateMissingPrimaryKeyFailure(table);

            if (!table.ColumnIndices.Any(x => x.Characteristic == IndexCharacteristic.PrimaryKey))
                table.ColumnIndices.Add(new ColumnIndex($"{table.DbName}_primary_key", IndexCharacteristic.PrimaryKey, IndexType.BTREE, columns));
        }

        foreach (var foreignKeyGroup in database.TableModels
            .Where(x => !x.IsStub && x.Table.Type == TableType.Table)
            .Select(x => x.Table)
            .SelectMany(table => table.Columns
                .Where(column => column.ForeignKey)
                .SelectMany(column => column.ValueProperty.Attributes
                    .OfType<ForeignKeyAttribute>()
                    .Select(attribute => new ForeignKeyColumn(column, attribute))))
            .GroupBy(x => new { ForeignKeyTable = x.Column.Table, x.Attribute.Name, CandidateTableName = x.Attribute.Table }))
        {
            var orderedForeignKeys = foreignKeyGroup
                .OrderBy(x => x.Attribute.Ordinal ?? x.Column.Index)
                .ThenBy(x => x.Column.Index)
                .ToList();
            var firstForeignKey = orderedForeignKeys[0];
            var firstAttribute = firstForeignKey.Attribute;
            var foreignKeyTable = firstForeignKey.Column.Table;
            var candidateTableModel = database
                .TableModels.FirstOrDefault(x => x.Table.DbName == firstAttribute.Table);

            if (candidateTableModel == null)
                return CreateForeignKeyFailure(
                    firstForeignKey.Column,
                    firstAttribute,
                    $"Foreign key '{firstAttribute.Name}' on table '{foreignKeyTable.DbName}' references table '{firstAttribute.Table}', but no matching table exists in database '{database.DbName}'.");

            var candidateColumns = new List<ColumnDefinition>();
            foreach (var foreignKey in orderedForeignKeys)
            {
                var foreignKeyColumn = foreignKey.Column;
                var attribute = foreignKey.Attribute;
                var candidateColumn = candidateTableModel
                    .Table.Columns.FirstOrDefault(x => x.DbName == attribute.Column);

                if (candidateColumn == null)
                    return CreateForeignKeyFailure(
                        foreignKeyColumn,
                        attribute,
                        $"Foreign key '{attribute.Name}' on column '{foreignKeyColumn.Table.DbName}.{foreignKeyColumn.DbName}' references column '{attribute.Table}.{attribute.Column}', but that column does not exist.");

                candidateColumns.Add(candidateColumn);
            }

            var foreignKeyColumns = orderedForeignKeys.Select(x => x.Column).ToList();
            var manySideModel = foreignKeyTable.Model;
            var oneSideModel = candidateTableModel.Model;

            var foreignKeyIndex = foreignKeyTable.ColumnIndices.FirstOrDefault(x =>
                x.Characteristic == IndexCharacteristic.ForeignKey &&
                x.Name == firstAttribute.Name &&
                ColumnsMatch(x.Columns, foreignKeyColumns));
            if (foreignKeyIndex == null)
            {
                if (!TryCreateColumnIndex(
                    firstAttribute.Name,
                    IndexCharacteristic.ForeignKey,
                    IndexType.BTREE,
                    foreignKeyColumns,
                    out foreignKeyIndex,
                    out var foreignKeyIndexFailure))
                {
                    return CreateForeignKeyFailure(
                        firstForeignKey.Column,
                        firstAttribute,
                        $"Foreign key '{firstAttribute.Name}' on table '{foreignKeyTable.DbName}' could not create its index: {foreignKeyIndexFailure}");
                }

                foreignKeyTable.ColumnIndices.Add(foreignKeyIndex);
            }

            var candidateKeyIndex = FindCandidateKeyIndex(candidateTableModel.Table, candidateColumns);
            if (candidateKeyIndex == null)
                return CreateForeignKeyFailure(
                    firstForeignKey.Column,
                    firstAttribute,
                    $"Foreign key '{firstAttribute.Name}' on table '{foreignKeyTable.DbName}' references columns '{candidateColumns.Select(x => x.DbName).ToJoinedString(", ")}' on table '{candidateTableModel.Table.DbName}', but no matching primary or unique key exists.");

            var relation = new RelationDefinition(firstAttribute.Name, RelationType.OneToMany)
            {
                OnUpdate = firstAttribute.OnUpdate,
                OnDelete = firstAttribute.OnDelete
            };
            var manySidePart = new RelationPart(foreignKeyIndex, relation, RelationPartType.ForeignKey, "");
            var oneSidePart = new RelationPart(candidateKeyIndex, relation, RelationPartType.CandidateKey, "");
            relation.ForeignKey = manySidePart;
            relation.CandidateKey = oneSidePart;

            // --- Link or Create Many-to-One Property ---
            if (!TryGetRelationProperty(
                manySideModel,
                oneSideModel.Table.DbName,
                candidateColumns.Select(x => x.DbName).ToArray(),
                firstAttribute.Name,
                out var manyToOneProp,
                out var manyToOnePropertyFailure))
            {
                return manyToOnePropertyFailure;
            }

            if (manyToOneProp != null)
            {
                manyToOneProp.SetRelationPart(manySidePart);
                if (!manySidePart.ColumnIndex.RelationParts.Contains(manySidePart))
                    manySidePart.ColumnIndex.RelationParts.Add(manySidePart);
            }
            else
            {
                var propName = GetForeignKeyRelationPropertyName(manySideModel, oneSideModel, foreignKeyColumns, firstAttribute);
                var propType = oneSideModel.CsType;
                var propAttr = new RelationAttribute(oneSideModel.Table.DbName, candidateColumns.Select(x => x.DbName).ToArray(), firstAttribute.Name);
                AddRelationProperty(manySideModel, propName, propType, manySidePart, propAttr);
            }

            // --- Link or Create One-to-Many Property ---
            if (!TryGetRelationProperty(
                oneSideModel,
                manySideModel.Table.DbName,
                foreignKeyColumns.Select(x => x.DbName).ToArray(),
                firstAttribute.Name,
                out var oneToManyProp,
                out var oneToManyPropertyFailure))
            {
                return oneToManyPropertyFailure;
            }

            if (oneToManyProp != null)
            {
                oneToManyProp.SetRelationPart(oneSidePart);
                if (!oneSidePart.ColumnIndex.RelationParts.Contains(oneSidePart))
                    oneSidePart.ColumnIndex.RelationParts.Add(oneSidePart);
            }
            else
            {
                var propName = GetCandidateKeyRelationPropertyName(manySideModel, oneSideModel, foreignKeyColumns, firstAttribute);
                var genericTypeName = manySideModel.CsType.Name;
                var propType = new CsTypeDeclaration($"IImmutableRelation<{genericTypeName}>", "DataLinq.Instances", ModelCsType.Interface);
                var propAttr = new RelationAttribute(manySideModel.Table.DbName, foreignKeyColumns.Select(x => x.DbName).ToArray(), firstAttribute.Name);
                AddRelationProperty(oneSideModel, propName, propType, oneSidePart, propAttr);
            }
        }

        return ValidateResolvedRelationProperties(database);
    }

    public static Option<bool, IDLOptionFailure> ValidateResolvedRelationProperties(DatabaseDefinition database)
    {
        var unresolvedRelation = database.TableModels
            .Where(x => !x.IsStub)
            .SelectMany(x => x.Model.RelationProperties.Values)
            .FirstOrDefault(x => x.RelationPart == null && ShouldValidateUnresolvedRelation(database, x));

        if (unresolvedRelation == null)
            return true;

        var relationAttribute = unresolvedRelation.Attributes
            .OfType<RelationAttribute>()
            .FirstOrDefault();
        var target = relationAttribute == null
            ? "a matching foreign-key relation"
            : $"relation target '{relationAttribute.Table}.({relationAttribute.Columns.ToJoinedString(", ")})'";
        var message = $"Relation property '{unresolvedRelation.Model.CsType.Name}.{unresolvedRelation.PropertyName}' could not be resolved to {target}. Check that the [Relation] table, column, and constraint name match a [ForeignKey] definition.";
        var sourceLocation = relationAttribute == null
            ? null
            : unresolvedRelation.GetAttributeSourceLocation(relationAttribute);

        if (!sourceLocation.HasValue && unresolvedRelation.SourceInfo.HasValue && unresolvedRelation.CsFile.HasValue)
            sourceLocation = unresolvedRelation.SourceInfo.Value.GetPropertyLocation(unresolvedRelation.CsFile.Value);

        return sourceLocation.HasValue
            ? DLOptionFailure.Fail(DLFailureType.InvalidModel, message, sourceLocation.Value)
            : DLOptionFailure.Fail(DLFailureType.InvalidModel, message, unresolvedRelation);
    }

    private static bool ShouldValidateUnresolvedRelation(DatabaseDefinition database, RelationProperty relation)
    {
        var relationAttribute = relation.Attributes
            .OfType<RelationAttribute>()
            .FirstOrDefault();

        if (relationAttribute == null)
            return true;

        return database.TableModels
            .Where(x => !x.IsStub)
            .Any(x => x.Table.DbName == relationAttribute.Table);
    }

    private static IDLOptionFailure CreateMissingPrimaryKeyFailure(TableDefinition table)
    {
        var message = $"Table '{table.DbName}' is missing a primary key.";
        var sourceLocation = GetTableNameSourceLocation(table.Model);

        return sourceLocation.HasValue
            ? DLOptionFailure.Fail(DLFailureType.InvalidModel, message, sourceLocation.Value)
            : DLOptionFailure.Fail(DLFailureType.InvalidModel, message, table);
    }

    private static IDLOptionFailure CreateForeignKeyFailure(ColumnDefinition foreignKeyColumn, ForeignKeyAttribute attribute, string message)
    {
        var attributeLocation = foreignKeyColumn.ValueProperty.GetAttributeSourceLocation(attribute);
        if (attributeLocation.HasValue)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, attributeLocation.Value);

        var property = foreignKeyColumn.ValueProperty;
        if (property.SourceInfo.HasValue && property.CsFile.HasValue)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, property.SourceInfo.Value.GetPropertyLocation(property.CsFile.Value));

        return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, foreignKeyColumn);
    }

    private static ColumnIndex? FindCandidateKeyIndex(TableDefinition table, IReadOnlyList<ColumnDefinition> columns)
    {
        return table.ColumnIndices.FirstOrDefault(index =>
            index.Characteristic is IndexCharacteristic.PrimaryKey or IndexCharacteristic.Unique &&
            ColumnsMatch(index.Columns, columns));
    }

    private static bool ColumnsMatch(IReadOnlyList<ColumnDefinition> left, IReadOnlyList<ColumnDefinition> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            if (!ReferenceEquals(left[i], right[i]))
                return false;
        }

        return true;
    }

    private static bool TryCreateColumnIndex(
        string name,
        IndexCharacteristic characteristic,
        IndexType type,
        List<ColumnDefinition> columns,
        out ColumnIndex index,
        out string failure)
    {
        try
        {
            index = new ColumnIndex(name, characteristic, type, columns);
            failure = null!;
            return true;
        }
        catch (InvalidOperationException exception)
        {
            index = null!;
            failure = exception.Message;
            return false;
        }
        catch (ArgumentException exception)
        {
            index = null!;
            failure = exception.Message;
            return false;
        }
    }

    private sealed class ForeignKeyColumn
    {
        public ForeignKeyColumn(ColumnDefinition column, ForeignKeyAttribute attribute)
        {
            Column = column;
            Attribute = attribute;
        }

        public ColumnDefinition Column { get; }
        public ForeignKeyAttribute Attribute { get; }

        public void Deconstruct(out ColumnDefinition column, out ForeignKeyAttribute attribute)
        {
            column = Column;
            attribute = Attribute;
        }
    }

    private static bool TryGetRelationProperty(
        ModelDefinition model,
        string referencedTableName,
        string[] referencedColumnNames,
        string constraintName,
        out RelationProperty? property,
        out IDLOptionFailure failure)
    {
        // Find a property in the model that has a [Relation] attribute matching the target table, columns, and constraint name.
        var matches = model.RelationProperties.Values
            .Where(p => p.Attributes.OfType<RelationAttribute>().Any(a =>
                RelationAttributeMatches(a, referencedTableName, referencedColumnNames, constraintName)))
            .ToArray();

        if (matches.Length <= 1)
        {
            property = matches.FirstOrDefault();
            failure = null!;
            return true;
        }

        property = null;

        var target = $"{referencedTableName}.({referencedColumnNames.ToJoinedString(", ")})";
        var propertyNames = matches.Select(x => x.PropertyName).ToJoinedString(", ");
        var message = $"Multiple relation properties on model '{model.CsType.Name}' match relation target '{target}' for constraint '{constraintName}': {propertyNames}. Relation attributes must identify at most one property for a database relation.";
        var duplicateAttribute = matches[1]
            .Attributes
            .OfType<RelationAttribute>()
            .FirstOrDefault(attribute => RelationAttributeMatches(attribute, referencedTableName, referencedColumnNames, constraintName));

        failure = CreateRelationPropertyFailure(matches[1], duplicateAttribute, message);
        return false;
    }

    private static bool RelationAttributeMatches(
        RelationAttribute attribute,
        string referencedTableName,
        string[] referencedColumnNames,
        string constraintName) =>
        attribute.Table == referencedTableName &&
        attribute.Columns.SequenceEqual(referencedColumnNames) &&
        (attribute.Name == null || attribute.Name == constraintName);

    private static IDLOptionFailure CreateRelationPropertyFailure(
        RelationProperty relation,
        RelationAttribute? attribute,
        string message)
    {
        if (attribute != null)
        {
            var attributeLocation = relation.GetAttributeSourceLocation(attribute);
            if (attributeLocation.HasValue)
                return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, attributeLocation.Value);
        }

        if (relation.SourceInfo.HasValue && relation.CsFile.HasValue)
            return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, relation.SourceInfo.Value.GetPropertyLocation(relation.CsFile.Value));

        return DLOptionFailure.Fail(DLFailureType.InvalidModel, message, relation);
    }

    private static string GetForeignKeyRelationPropertyName(ModelDefinition manySideModel, ModelDefinition oneSideModel, IReadOnlyList<ColumnDefinition> foreignKeyColumns, ForeignKeyAttribute attribute)
    {
        if (foreignKeyColumns.Count > 1)
        {
            if (!HasMultipleForeignKeyConstraintsBetween(manySideModel.Table, oneSideModel.Table))
                return oneSideModel.CsType.Name;

            var constraintName = GetRelationPropertyNameFromConstraint(oneSideModel.CsType.Name, attribute.Name);
            return string.IsNullOrEmpty(constraintName)
                ? oneSideModel.CsType.Name
                : constraintName;
        }

        return Regex.Replace(foreignKeyColumns[0].DbName, "(_id|id|fk)$", "", RegexOptions.IgnoreCase).ToPascalCase();
    }

    private static string GetCandidateKeyRelationPropertyName(ModelDefinition manySideModel, ModelDefinition oneSideModel, IReadOnlyList<ColumnDefinition> foreignKeyColumns, ForeignKeyAttribute attribute)
    {
        if (!HasMultipleForeignKeyConstraintsBetween(manySideModel.Table, oneSideModel.Table))
            return manySideModel.CsType.Name;

        var relationName = attribute.Name.Any(char.IsLetter)
            ? GetRelationPropertyNameFromConstraint(manySideModel.CsType.Name, attribute.Name)
            : GetRelationPropertyNameFromColumn(manySideModel.CsType.Name, foreignKeyColumns[0].DbName);

        return string.IsNullOrEmpty(relationName)
            ? manySideModel.CsType.Name
            : relationName;
    }

    private static bool HasMultipleForeignKeyConstraintsBetween(TableDefinition foreignKeyTable, TableDefinition candidateTable)
    {
        return foreignKeyTable.Columns
            .SelectMany(column => column.ValueProperty.Attributes.OfType<ForeignKeyAttribute>())
            .Where(attribute => attribute.Table == candidateTable.DbName)
            .Select(attribute => attribute.Name)
            .Distinct(StringComparer.Ordinal)
            .Count() > 1;
    }

    private static string GetRelationPropertyNameFromConstraint(string fallbackPrefix, string constraintName)
    {
        var words = Regex
            .Split(constraintName, "[^A-Za-z0-9]+")
            .Where(word => !string.IsNullOrWhiteSpace(word))
            .ToList();

        if (words.Count == 0)
            return string.Empty;

        if (string.Equals(words[0], "fk", StringComparison.OrdinalIgnoreCase))
            words.RemoveAt(0);

        if (words.Count > 0 && string.Equals(words[words.Count - 1], "fk", StringComparison.OrdinalIgnoreCase))
            words.RemoveAt(words.Count - 1);

        if (words.Count == 0)
            return string.Empty;

        var propertyName = words
            .Select(word => word.FirstCharToUpper())
            .ToJoinedString("");

        return propertyName.StartsWith(fallbackPrefix, StringComparison.Ordinal)
            ? propertyName
            : fallbackPrefix + propertyName;
    }

    private static string GetRelationPropertyNameFromColumn(string fallbackPrefix, string columnName)
    {
        var nameWithoutCommonSuffix = Regex.Replace(columnName, "(_id| id|id|fk)$", "", RegexOptions.IgnoreCase);
        var words = Regex
            .Split(nameWithoutCommonSuffix, "[^A-Za-z0-9]+")
            .Where(word => !string.IsNullOrWhiteSpace(word));

        var propertyName = words
            .Select(word => word.FirstCharToUpper())
            .ToJoinedString("");

        return string.IsNullOrEmpty(propertyName)
            ? string.Empty
            : fallbackPrefix + propertyName;
    }

    public static void AddRelationProperty(ModelDefinition model, string propertyName, CsTypeDeclaration propertyType, RelationPart relationPart, RelationAttribute relationAttribute)
    {
        var originalPropertyName = propertyName;
        var i = 2;
        while (model.RelationProperties.ContainsKey(propertyName) || model.ValueProperties.ContainsKey(propertyName))
        {
            propertyName = $"{originalPropertyName}_{i++}";
        }

        var relationProperty = new RelationProperty(propertyName, propertyType, model, [relationAttribute]);
        relationProperty.SetRelationName(relationAttribute.Name);
        relationProperty.SetRelationPart(relationPart); // Directly link the part
        if (relationPart.Type == RelationPartType.ForeignKey && relationPart.ColumnIndex.Columns.Any(x => x.Nullable))
            relationProperty.SetCsNullable();

        model.AddProperty(relationProperty);

        // Also ensure the back-reference on the index is set
        if (!relationPart.ColumnIndex.RelationParts.Contains(relationPart))
        {
            relationPart.ColumnIndex.RelationParts.Add(relationPart);
        }
    }

    public static ValueProperty AttachValueProperty(ColumnDefinition column, string csTypeName, bool capitaliseNames)
    {
        var name = column.DbName.ToCSharpIdentifier(capitaliseNames);

        var type = MetadataTypeConverter.GetType(csTypeName);

        CsTypeDeclaration csType;

        if (type == null)
        {
            if (csTypeName == "enum")
                csType = new CsTypeDeclaration(csTypeName, "", ModelCsType.Enum);
            else
                throw new Exception($"Type {csTypeName} not found.");
        }
        else
        {
            csType = new CsTypeDeclaration(type);
        }


        var property = new ValueProperty(name, csType, column.Table.Model, GetAttributes(column));
        property.SetCsSize(MetadataTypeConverter.CsTypeSize(csTypeName));
        property.SetCsNullable(column.Nullable || column.AutoIncrement);
        //property.SetAttributes(GetAttributes(property));
        property.SetColumn(column);

        column.SetValueProperty(property);
        column.Table.Model.AddProperty(column.ValueProperty);

        return property;
    }

    //public static void AttachEnumProperty(ValueProperty property, IEnumerable<(string name, int value)> enumValues, bool declaredInClass)
    //{
    //    property.SetEnumProperty(new EnumProperty(enumValues, enumValues, declaredInClass));
    //}

    public static IEnumerable<Attribute> GetAttributes(ColumnDefinition column)
    {
        if (column.PrimaryKey)
            yield return new PrimaryKeyAttribute();

        if (column.AutoIncrement)
            yield return new AutoIncrementAttribute();

        if (column.Nullable)
            yield return new NullableAttribute();

        yield return new ColumnAttribute(column.DbName);

        foreach (var dbType in column.DbTypes)
        {
            yield return new TypeAttribute(dbType);
        }
    }

    public static void ParseAttributes(this DatabaseDefinition database)
    {
        foreach (var attribute in database.Attributes)
        {
            if (attribute is DatabaseAttribute databaseAttribute)
            {
                database.SetName(databaseAttribute.Name);
                database.SetDbName(databaseAttribute.Name);
            }

            if (attribute is UseCacheAttribute useCache)
                database.SetCache(useCache.UseCache);

            if (attribute is CacheLimitAttribute cacheLimit)
                database.CacheLimits.Add((cacheLimit.LimitType, cacheLimit.Amount));

            if (attribute is IndexCacheAttribute indexCache)
                database.IndexCache.Add((indexCache.Type, indexCache.Amount));

            if (attribute is CacheCleanupAttribute cacheCleanup)
                database.CacheCleanup.Add((cacheCleanup.LimitType, cacheCleanup.Amount));
        }
    }

    public static ColumnDefinition ParseColumn(this TableDefinition table, ValueProperty property)
    {
        var column = new ColumnDefinition(property.PropertyName, table);
        column.SetValueProperty(property);

        foreach (var attribute in property.Attributes)
        {
            if (attribute is ColumnAttribute columnAttribute)
                column.SetDbName(columnAttribute.Name);

            if (attribute is NullableAttribute)
                column.SetNullable();

            //if (attribute is DefaultAttribute defaultAttribute)
            //    column.AddDefaultValue(defaultAttribute.Value);

            if (attribute is AutoIncrementAttribute)
                column.SetAutoIncrement();

            if (attribute is PrimaryKeyAttribute)
                column.SetPrimaryKey();

            if (attribute is ForeignKeyAttribute)
                column.SetForeignKey();

            if (attribute is TypeAttribute t)
                column.AddDbType(new DatabaseColumnType(t.DatabaseType, t.Name, t.Length, t.Decimals, t.Signed));
        }

        return column;
    }
}
