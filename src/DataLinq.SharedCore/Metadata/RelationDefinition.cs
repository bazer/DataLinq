using DataLinq.Attributes;

namespace DataLinq.Metadata;

public enum RelationType
{
    OneToMany
}

public class RelationDefinition
{
    private RelationPart foreignKey = null!;
    private RelationPart candidateKey = null!;
    private RelationType type;
    private string constraintName;
    private ReferentialAction onUpdate = ReferentialAction.Unspecified;
    private ReferentialAction onDelete = ReferentialAction.Unspecified;

    public RelationDefinition(string constraintName, RelationType type)
    {
        this.constraintName = constraintName;
        this.type = type;
    }

    public bool IsFrozen { get; private set; }

    public RelationPart ForeignKey
    {
        get => foreignKey;
        set
        {
            ThrowIfFrozen();
            foreignKey = value;
        }
    }

    public RelationPart CandidateKey
    {
        get => candidateKey;
        set
        {
            ThrowIfFrozen();
            candidateKey = value;
        }
    }

    public RelationType Type
    {
        get => type;
        set
        {
            ThrowIfFrozen();
            type = value;
        }
    }

    public string ConstraintName
    {
        get => constraintName;
        set
        {
            ThrowIfFrozen();
            constraintName = value;
        }
    }

    public ReferentialAction OnUpdate
    {
        get => onUpdate;
        set
        {
            ThrowIfFrozen();
            onUpdate = value;
        }
    }

    public ReferentialAction OnDelete
    {
        get => onDelete;
        set
        {
            ThrowIfFrozen();
            onDelete = value;
        }
    }

    public override string ToString()
    {
        return $"{ConstraintName}: {ForeignKey} -> {CandidateKey}";
    }

    internal void Freeze()
    {
        if (IsFrozen)
            return;

        IsFrozen = true;
    }

    private void ThrowIfFrozen() => MetadataMutationGuard.ThrowIfFrozen(IsFrozen, this);
}
