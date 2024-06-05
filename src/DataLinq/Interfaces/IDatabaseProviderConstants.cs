namespace DataLinq.Interfaces;

public interface IDatabaseProviderConstants
{
    string ParameterSign { get; }
    string LastInsertCommand { get; }
    string EscapeCharacter { get; }
}