﻿using System;
using System.Collections.Generic;
using System.Text;

namespace DataLinq.Metadata
{
    public enum RelationType
    {
        OneToMany
    }

    public class Relation
    {
        public RelationPart ForeignKey { get; set; }
        public RelationPart CandidateKey { get; set; }
        public RelationType Type { get; set; }
        public string ConstraintName { get; set; }

        public override string ToString()
        {
            return $"{ConstraintName}: {ForeignKey} -> {CandidateKey}";
        }
    }
}
