namespace DataLinq.MySql.Shared;

public enum COLUMN_KEYValue
{
    Empty = 1,
    PRI = 2,
    UNI = 3,
    MUL = 4,
}

public interface ICOLUMNS
{
    string TABLE_SCHEMA { get; }
    string TABLE_NAME { get; }
    string DATA_TYPE { get; }
    string COLUMN_TYPE { get; }
    ulong? NUMERIC_PRECISION { get; }
    ulong? NUMERIC_SCALE { get; }
    ulong? CHARACTER_MAXIMUM_LENGTH { get; }
    string IS_NULLABLE { get; }
    COLUMN_KEYValue COLUMN_KEY { get; }
    string EXTRA { get; }
    string COLUMN_DEFAULT { get; }
    string COLUMN_NAME { get; }
}
//public partial interface IMYSQLCOLUMNS : ICOLUMNS { }
//public partial interface IMARIADBCOLUMNS : ICOLUMNS { }

public interface ITABLES
{
    string TABLE_SCHEMA { get; }
    string TABLE_TYPE { get; }
    string TABLE_NAME { get; }
}
public partial interface IMYSQLTABLES : ITABLES { }
public partial interface IMARIADBTABLES : ITABLES { }


public interface IKEY_COLUMN_USAGE
{
    string TABLE_SCHEMA { get; }
    string REFERENCED_TABLE_NAME { get; }
    string TABLE_NAME { get; }
    string COLUMN_NAME { get; }
    string REFERENCED_COLUMN_NAME { get; }
    string CONSTRAINT_NAME { get; }
}
public partial interface IMYSQLKEY_COLUMN_USAGE : IKEY_COLUMN_USAGE { }
public partial interface IMARIADBKEY_COLUMN_USAGE : IKEY_COLUMN_USAGE { }

public interface ISTATISTICS
{
    string TABLE_SCHEMA { get; }
    string INDEX_NAME { get; }
    string TABLE_NAME { get; }
    string COLUMN_NAME { get; }
    uint SEQ_IN_INDEX { get; }
    string INDEX_TYPE { get; }
    int NON_UNIQUE { get; }
}
public partial interface IMYSQLSTATISTICS : ISTATISTICS { }
public partial interface IMARIADBSTATISTICS : ISTATISTICS { }

public interface IVIEWS
{
    string TABLE_SCHEMA { get; }
    string TABLE_NAME { get; }
    string VIEW_DEFINITION { get; }
}
public partial interface IMYSQLVIEWS : IVIEWS { }
public partial interface IMARIADBVIEWS : IVIEWS { }

public interface IInformationSchema
{
//    public DbRead<ICOLUMNS> COLUMNS { get; }
//    public DbRead<IKEY_COLUMN_USAGE> KEY_COLUMN_USAGE { get; }
//    public DbRead<ISTATISTICS> STATISTICS { get; }
//    public DbRead<ITABLES> TABLES { get; }
//    public DbRead<IVIEWS> VIEWS { get; }
}