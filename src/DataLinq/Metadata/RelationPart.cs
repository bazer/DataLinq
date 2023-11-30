namespace DataLinq.Metadata;

public enum RelationPartType
{
    ForeignKey,
    CandidateKey
}

public class RelationPart
{
    public Column Column { get; set; }
    public Relation Relation { get; set; }
    public RelationPartType Type { get; set; }
    public string CsName { get; set; }

    public RelationPart GetOtherSide() => Type == RelationPartType.CandidateKey
        ? Relation.ForeignKey
        : Relation.CandidateKey;

    public override string ToString()
    {
        return $"{Type}: {Column}";
    }
}