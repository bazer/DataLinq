namespace DataLinq.Validation;

public enum SchemaDifferenceKind
{
    MissingTable,
    ExtraTable,
    TableTypeMismatch,
    MissingColumn,
    ExtraColumn,
    ColumnTypeMismatch,
    ColumnNullabilityMismatch,
    ColumnPrimaryKeyMismatch,
    ColumnAutoIncrementMismatch,
    ColumnDefaultMismatch,
    MissingIndex,
    ExtraIndex,
    MissingForeignKey,
    ExtraForeignKey,
    ForeignKeyActionMismatch,
    MissingCheck,
    ExtraCheck,
    TableCommentMismatch,
    ColumnCommentMismatch,
    ColumnGuidStorageFormatUnresolved,
    ColumnGuidStorageFormatMismatch
}
