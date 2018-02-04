using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    public interface PROFILING : IViewModel
    {
        [Nullable]
        [Type("int")]
        int? BLOCK_OPS_IN { get; }

        [Nullable]
        [Type("int")]
        int? BLOCK_OPS_OUT { get; }

        [Nullable]
        [Type("int")]
        int? CONTEXT_INVOLUNTARY { get; }

        [Nullable]
        [Type("int")]
        int? CONTEXT_VOLUNTARY { get; }

        [Nullable]
        [Type("decimal")]
        decimal? CPU_SYSTEM { get; }

        [Nullable]
        [Type("decimal")]
        decimal? CPU_USER { get; }

        [Type("decimal")]
        decimal DURATION { get; }

        [Nullable]
        [Type("int")]
        int? MESSAGES_RECEIVED { get; }

        [Nullable]
        [Type("int")]
        int? MESSAGES_SENT { get; }

        [Nullable]
        [Type("int")]
        int? PAGE_FAULTS_MAJOR { get; }

        [Nullable]
        [Type("int")]
        int? PAGE_FAULTS_MINOR { get; }

        [Type("int")]
        int QUERY_ID { get; }

        [Type("int")]
        int SEQ { get; }

        [Nullable]
        [Type("varchar", 20)]
        string SOURCE_FILE { get; }

        [Nullable]
        [Type("varchar", 30)]
        string SOURCE_FUNCTION { get; }

        [Nullable]
        [Type("int")]
        int? SOURCE_LINE { get; }

        [Type("varchar", 30)]
        string STATE { get; }

        [Nullable]
        [Type("int")]
        int? SWAPS { get; }

    }
}