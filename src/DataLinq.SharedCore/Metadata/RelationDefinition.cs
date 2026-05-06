using System;
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
        [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
        set
        {
            SetForeignKeyCore(value);
        }
    }

    internal void SetForeignKeyCore(RelationPart foreignKey)
    {
        ThrowIfFrozen();
        this.foreignKey = foreignKey;
    }

    public RelationPart CandidateKey
    {
        get => candidateKey;
        [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
        set
        {
            SetCandidateKeyCore(value);
        }
    }

    internal void SetCandidateKeyCore(RelationPart candidateKey)
    {
        ThrowIfFrozen();
        this.candidateKey = candidateKey;
    }

    public RelationType Type
    {
        get => type;
        [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
        set
        {
            SetTypeCore(value);
        }
    }

    internal void SetTypeCore(RelationType type)
    {
        ThrowIfFrozen();
        this.type = type;
    }

    public string ConstraintName
    {
        get => constraintName;
        [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
        set
        {
            SetConstraintNameCore(value);
        }
    }

    internal void SetConstraintNameCore(string constraintName)
    {
        ThrowIfFrozen();
        this.constraintName = constraintName;
    }

    public ReferentialAction OnUpdate
    {
        get => onUpdate;
        [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
        set
        {
            SetOnUpdateCore(value);
        }
    }

    internal void SetOnUpdateCore(ReferentialAction onUpdate)
    {
        ThrowIfFrozen();
        this.onUpdate = onUpdate;
    }

    public ReferentialAction OnDelete
    {
        get => onDelete;
        [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
        set
        {
            SetOnDeleteCore(value);
        }
    }

    internal void SetOnDeleteCore(ReferentialAction onDelete)
    {
        ThrowIfFrozen();
        this.onDelete = onDelete;
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
