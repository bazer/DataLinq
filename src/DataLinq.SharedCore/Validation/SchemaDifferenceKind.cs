namespace DataLinq.Validation;

public enum SchemaDifferenceKind
{
    MissingTable,
    ExtraTable,
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
    MissingCheck,
    ExtraCheck,
    TableCommentMismatch,
    ColumnCommentMismatch
}
