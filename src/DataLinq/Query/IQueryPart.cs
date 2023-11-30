namespace DataLinq.Query
{
    public interface IQueryPart
    {
        void AddCommandString(Sql sql, string prefix, bool addCommandParameter = true, bool addParentheses = false);
        //protected abstract void GetCommandParameter(Sql sql, string prefix);
    }
}
