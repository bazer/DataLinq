using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    [Name("INNODB_TRX")]
    public interface INNODB_TRX : IViewModel
    {
        [Type("int")]
        int trx_adaptive_hash_latched { get; }

        [Type("int")]
        int trx_autocommit_non_locking { get; }

        [Type("bigint")]
        long trx_concurrency_tickets { get; }

        [Type("int")]
        int trx_foreign_key_checks { get; }

        [Type("varchar", 18)]
        string trx_id { get; }

        [Type("int")]
        int trx_is_read_only { get; }

        [Type("varchar", 16)]
        string trx_isolation_level { get; }

        [Nullable]
        [Type("varchar", 256)]
        string trx_last_foreign_key_error { get; }

        [Type("bigint")]
        long trx_lock_memory_bytes { get; }

        [Type("bigint")]
        long trx_lock_structs { get; }

        [Type("bigint")]
        long trx_mysql_thread_id { get; }

        [Nullable]
        [Type("varchar", 64)]
        string trx_operation_state { get; }

        [Nullable]
        [Type("varchar", 1024)]
        string trx_query { get; }

        [Nullable]
        [Type("varchar", 81)]
        string trx_requested_lock_id { get; }

        [Type("bigint")]
        long trx_rows_locked { get; }

        [Type("bigint")]
        long trx_rows_modified { get; }

        [Type("datetime")]
        DateTime trx_started { get; }

        [Type("varchar", 13)]
        string trx_state { get; }

        [Type("bigint")]
        long trx_tables_in_use { get; }

        [Type("bigint")]
        long trx_tables_locked { get; }

        [Type("int")]
        int trx_unique_checks { get; }

        [Nullable]
        [Type("datetime")]
        DateTime? trx_wait_started { get; }

        [Type("bigint")]
        long trx_weight { get; }

    }
}