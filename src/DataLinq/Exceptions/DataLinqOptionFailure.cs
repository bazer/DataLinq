namespace DataLinq.Exceptions
{
    public abstract class IDataLinqOptionFailure
    {
        public static implicit operator string(IDataLinqOptionFailure optionFailure) =>
            optionFailure.ToString();

        public static implicit operator IDataLinqOptionFailure(string failure) =>
            DataLinqOptionFailure.Fail(failure);
    }

    public static class DataLinqOptionFailure
    {
        public static DataLinqOptionFailure<T> Fail<T>(T failure) =>
            new DataLinqOptionFailure<T>(failure);

        public static DataLinqOptionFailure<T> Fail<T>(T failure, IDataLinqOptionFailure innerFailure) =>
            new DataLinqOptionFailure<T>(failure, innerFailure);
    }

    public class DataLinqOptionFailure<T> : IDataLinqOptionFailure
    {
        public T Failure { get; }
        public IDataLinqOptionFailure InnerFailure { get; }

        public DataLinqOptionFailure(T failure)
        {
            Failure = failure;
        }

        public DataLinqOptionFailure(T failure, IDataLinqOptionFailure innerFailure)
        {
            Failure = failure;
            InnerFailure = innerFailure;
        }

        public override string ToString()
        {
            if (InnerFailure == null)
                return Failure.ToString();

            return Failure.ToString() + "\n" + InnerFailure.ToString();
        }

        public static implicit operator T(DataLinqOptionFailure<T> optionFailure) =>
            optionFailure.Failure;

        public static implicit operator DataLinqOptionFailure<T>(T failure) =>
            DataLinqOptionFailure.Fail(failure);
    }
}
