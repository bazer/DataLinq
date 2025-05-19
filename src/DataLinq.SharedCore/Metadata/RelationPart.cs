namespace DataLinq.Metadata;

public enum RelationPartType
{
    ForeignKey,
    CandidateKey
}

public class RelationPart(
    ColumnIndex columnIndex,
    RelationDefinition relation,
    RelationPartType type,
    string csName)
{
    public ColumnIndex ColumnIndex { get; } = columnIndex;
    public RelationDefinition Relation { get; } = relation;
    public RelationPartType Type { get; } = type;
    public string CsName { get; } = csName;

    public RelationPart GetOtherSide() => Type == RelationPartType.CandidateKey
        ? Relation.ForeignKey
        : Relation.CandidateKey;

    public override string ToString()
    {
        return $"{Type}: {ColumnIndex}";
    }
}