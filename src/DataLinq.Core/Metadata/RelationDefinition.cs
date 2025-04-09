namespace DataLinq.Metadata;

public enum RelationType
{
    OneToMany
}

public class RelationDefinition(string constraintName, RelationType type)
{
    public RelationPart ForeignKey { get; set; }
    public RelationPart CandidateKey { get; set; }
    public RelationType Type { get; set; } = type;
    public string ConstraintName { get; set; } = constraintName;

    public override string ToString()
    {
        return $"{ConstraintName}: {ForeignKey} -> {CandidateKey}";
    }
}
